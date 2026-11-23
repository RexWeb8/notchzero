import Foundation
import Darwin
import IOKit.ps
import SwiftUI

/// CPU, RAM and battery, sampled every 1.5 s while the notch is open (every 20 s otherwise).
@MainActor
final class SystemMonitor: ObservableObject {
    @Published private(set) var cpu: Double = 0
    @Published private(set) var memoryUsed: Double = 0
    @Published private(set) var battery: Double = 1
    @Published private(set) var isCharging = false
    @Published private(set) var onAC = false
    @Published private(set) var hasBattery = false

    let memoryTotal = Double(ProcessInfo.processInfo.physicalMemory)

    private let host: host_t = mach_host_self()
    private var lastTicks: [UInt64]?
    private var timer: Timer?
    private var active = false

    var memoryFraction: Double {
        memoryTotal > 0 ? min(memoryUsed / memoryTotal, 1) : 0
    }

    func start() {
        sample()
        reschedule()
    }

    func setActive(_ value: Bool) {
        active = value
        if value { sample() }
        reschedule()
    }

    private func reschedule() {
        timer?.invalidate()
        let t = Timer(timeInterval: active ? 1.5 : 20, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.sample() }
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    private func sample() {
        sampleCPU()
        sampleMemory()
        sampleBattery()
    }

    private func sampleCPU() {
        var info = host_cpu_load_info()
        var count = mach_msg_type_number_t(MemoryLayout<host_cpu_load_info>.stride / MemoryLayout<integer_t>.stride)
        let h = host
        let kr = withUnsafeMutablePointer(to: &info) { ptr in
            ptr.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics(h, HOST_CPU_LOAD_INFO, $0, &count)
            }
        }
        guard kr == KERN_SUCCESS else { return }

        let ticks: [UInt64] = [
            UInt64(info.cpu_ticks.0), // user
            UInt64(info.cpu_ticks.1), // system
            UInt64(info.cpu_ticks.2), // idle
            UInt64(info.cpu_ticks.3)  // nice
        ]
        if let last = lastTicks {
            let user = ticks[0] &- last[0]
            let sys = ticks[1] &- last[1]
            let idle = ticks[2] &- last[2]
            let nice = ticks[3] &- last[3]
            let total = user + sys + idle + nice
            if total > 0 {
                cpu = Double(user + sys + nice) / Double(total)
            }
        }
        lastTicks = ticks
    }

    private func sampleMemory() {
        var stats = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.stride / MemoryLayout<integer_t>.stride)
        let h = host
        let kr = withUnsafeMutablePointer(to: &stats) { ptr in
            ptr.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics64(h, HOST_VM_INFO64, $0, &count)
            }
        }
        guard kr == KERN_SUCCESS else { return }

        // Same idea as Activity Monitor's "Memory Used": app memory + wired + compressed.
        let page = Double(sysconf(_SC_PAGESIZE))
        let appPages = Double(stats.internal_page_count) - Double(stats.purgeable_count)
        let used = (appPages + Double(stats.wire_count) + Double(stats.compressor_page_count)) * page
        memoryUsed = max(0, used)
    }

    private func sampleBattery() {
        guard let blob = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let list = IOPSCopyPowerSourcesList(blob)?.takeRetainedValue() as? [CFTypeRef] else {
            hasBattery = false
            return
        }
        for ps in list {
            guard let desc = IOPSGetPowerSourceDescription(blob, ps)?.takeUnretainedValue() as? [String: Any] else {
                continue
            }
            guard (desc[kIOPSTypeKey] as? String) == kIOPSInternalBatteryType else { continue }
            let current = desc[kIOPSCurrentCapacityKey] as? Int ?? 0
            let capacity = desc[kIOPSMaxCapacityKey] as? Int ?? 100
            hasBattery = true
            battery = capacity > 0 ? Double(current) / Double(capacity) : 0
            isCharging = desc[kIOPSIsChargingKey] as? Bool ?? false
            onAC = (desc[kIOPSPowerSourceStateKey] as? String) == kIOPSACPowerValue
            return
        }
        hasBattery = false
    }
}
