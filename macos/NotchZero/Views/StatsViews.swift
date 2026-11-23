import SwiftUI

/// CPU on a dial with a glowing needle; RAM and battery as glowing segment bars.
struct SystemCard: View {
    @ObservedObject var system: SystemMonitor

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Text("System")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(Color.white)
                Spacer(minLength: 0)
                Text(memoryText)
                    .font(.system(size: 9.5, weight: .medium).monospacedDigit())
                    .foregroundStyle(Color.white.opacity(0.45))
            }

            HStack(alignment: .center, spacing: 10) {
                DialGauge(value: system.cpu, label: "CPU")
                    .frame(width: 92, height: 92)

                VStack(alignment: .leading, spacing: 14) {
                    SegmentMeter(label: "RAM",
                                 value: system.memoryFraction,
                                 detail: Self.percent(system.memoryFraction))
                    SegmentMeter(label: system.hasBattery ? "Battery" : "Power",
                                 value: system.hasBattery ? system.battery : 1,
                                 detail: system.hasBattery ? Self.percent(system.battery) : "AC",
                                 icon: system.isCharging ? "bolt.fill" : nil)
                }
            }
            .frame(maxHeight: .infinity)
        }
        .padding(14)
        .background(RoundedRectangle(cornerRadius: 22, style: .continuous).fill(Color.white.opacity(0.05)))
        .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).strokeBorder(Color.white.opacity(0.08), lineWidth: 1))
    }

    private var memoryText: String {
        let gb = 1_073_741_824.0
        return String(format: "%.1f / %.0f GB", system.memoryUsed / gb, system.memoryTotal / gb)
    }

    static func percent(_ v: Double) -> String {
        "\(Int((min(max(v, 0), 1) * 100).rounded()))%"
    }
}

/// Speedometer-style dial: tick marks over 270°, a glowing needle, the value underneath.
struct DialGauge: View {
    let value: Double
    let label: String

    private let startAngle: Double = 135
    private let sweep: Double = 270

    var body: some View {
        GeometryReader { geo in
            let size = min(geo.size.width, geo.size.height)
            let r = size / 2
            let c = CGPoint(x: geo.size.width / 2, y: geo.size.height / 2)
            let v = min(max(value, 0), 1)

            ZStack {
                Circle()
                    .fill(RadialGradient(colors: [Color.white.opacity(0.10), Color.white.opacity(0)],
                                         center: .center, startRadius: 0, endRadius: r))
                Circle()
                    .stroke(Color.white.opacity(0.22), lineWidth: 1.5)

                // Ticks
                Path { p in
                    let count = 24
                    for i in 0...count {
                        let a = (startAngle + sweep * Double(i) / Double(count)) * Double.pi / 180
                        let major = i % 6 == 0
                        let outer = r - 5
                        let inner = r - (major ? 11 : 8)
                        p.move(to: CGPoint(x: c.x + CGFloat(cos(a)) * inner, y: c.y + CGFloat(sin(a)) * inner))
                        p.addLine(to: CGPoint(x: c.x + CGFloat(cos(a)) * outer, y: c.y + CGFloat(sin(a)) * outer))
                    }
                }
                .stroke(Color.white.opacity(0.35), style: StrokeStyle(lineWidth: 1, lineCap: .round))

                // Needle (drawn pointing right, rotated to the value)
                Capsule()
                    .fill(Color.white)
                    .frame(width: r * 0.62, height: 2.6)
                    .offset(x: r * 0.31)
                    .rotationEffect(.degrees(startAngle + sweep * v))
                    .shadow(color: Color.white.opacity(0.8), radius: 4)
                    .animation(.spring(response: 0.6, dampingFraction: 0.75), value: v)
                Circle()
                    .fill(Color.white)
                    .frame(width: 5, height: 5)

                VStack(spacing: 0) {
                    Text(SystemCard.percent(v))
                        .font(.system(size: 15, weight: .semibold, design: .rounded).monospacedDigit())
                        .foregroundStyle(Color.white)
                    Text(label)
                        .font(.system(size: 8.5, weight: .bold))
                        .tracking(0.6)
                        .foregroundStyle(Color.white.opacity(0.5))
                }
                .offset(y: r * 0.5)
            }
            .frame(width: geo.size.width, height: geo.size.height)
        }
    }
}

/// Row of glowing bars, lit up to the value.
struct SegmentMeter: View {
    let label: String
    let value: Double
    let detail: String
    var icon: String? = nil

    private let segments = 14

    var body: some View {
        let lit = Int((min(max(value, 0), 1) * Double(segments)).rounded())
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 4) {
                Text(label)
                    .font(.system(size: 9.5, weight: .medium))
                    .foregroundStyle(Color.white.opacity(0.55))
                Spacer(minLength: 0)
                if let icon {
                    Image(systemName: icon)
                        .font(.system(size: 8, weight: .bold))
                        .foregroundStyle(Color.white)
                }
                Text(detail)
                    .font(.system(size: 11, weight: .semibold, design: .rounded).monospacedDigit())
                    .foregroundStyle(Color.white)
            }
            HStack(spacing: 3.5) {
                ForEach(0..<segments, id: \.self) { i in
                    Capsule()
                        .fill(Color.white.opacity(i < lit ? 0.95 : 0.15))
                        .frame(width: 3, height: 18)
                        .shadow(color: Color.white.opacity(i < lit ? 0.55 : 0), radius: 3)
                }
            }
            .animation(.easeOut(duration: 0.4), value: lit)
        }
        .frame(width: 88)
    }
}
