import Foundation
import CoreLocation
import AppKit

// MARK: - Model

enum PrayerName: String, CaseIterable, Codable {
    case fajr, dhuhr, asr, maghrib, isha

    var title: String {
        switch self {
        case .fajr: return "Fajr"
        case .dhuhr: return "Dhuhr"
        case .asr: return "Asr"
        case .maghrib: return "Maghrib"
        case .isha: return "Isha"
        }
    }
}

struct PrayerDay: Codable, Equatable {
    var date: Date
    var fajr: Date
    var dhuhr: Date
    var asr: Date
    var maghrib: Date
    var isha: Date
    var syuruk: Date?

    /// Sunrise, or a close estimate if the source didn't give one.
    var sunrise: Date { syuruk ?? fajr.addingTimeInterval(72 * 60) }

    func time(_ prayer: PrayerName) -> Date {
        switch prayer {
        case .fajr: return fajr
        case .dhuhr: return dhuhr
        case .asr: return asr
        case .maghrib: return maghrib
        case .isha: return isha
        }
    }
}

struct PrayerEvent: Equatable {
    let name: PrayerName
    let time: Date
}

enum PrayerWindow: Equatable {
    case none
    case soon(PrayerEvent)   // up to 10 minutes before
    case now(PrayerEvent)    // first 15 minutes after it starts
}

private struct PrayerCache: Codable {
    var zone: String
    var automatic: Bool
    var days: [PrayerDay]
}

// MARK: - Sources (JAKIM data)

enum PrayerAPI {
    static let malaysia = TimeZone(identifier: "Asia/Kuala_Lumpur") ?? .current

    static var calendar: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = malaysia
        return c
    }

    private struct V2Response: Decodable {
        let zone: String?
        let prayers: [V2Day]?
    }

    private struct V2Day: Decodable {
        let syuruk: Double?
        let fajr: Double?
        let dhuhr: Double?
        let asr: Double?
        let maghrib: Double?
        let isha: Double?
    }

    private struct JakimResponse: Decodable {
        let prayerTime: [JakimDay]?
    }

    private struct JakimDay: Decodable {
        let date: String?
        let syuruk: String?
        let fajr: String?
        let dhuhr: String?
        let asr: String?
        let maghrib: String?
        let isha: String?
    }

    /// api.waktusolat.app (JAKIM data). `path` is a zone code like "WLY01" or "gps/3.1390/101.6869".
    static func fetchWaktuSolat(path: String, year: Int? = nil, month: Int? = nil) async throws -> (zone: String?, days: [PrayerDay]) {
        guard var comps = URLComponents(string: "https://api.waktusolat.app/v2/solat/\(path)") else {
            throw URLError(.badURL)
        }
        if let year, let month {
            comps.queryItems = [URLQueryItem(name: "year", value: "\(year)"),
                                URLQueryItem(name: "month", value: "\(month)")]
        }
        guard let url = comps.url else { throw URLError(.badURL) }
        var request = URLRequest(url: url, timeoutInterval: 15)
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw URLError(.badServerResponse) }

        let decoded = try JSONDecoder().decode(V2Response.self, from: data)
        let cal = calendar
        let days: [PrayerDay] = (decoded.prayers ?? []).compactMap { d in
            guard let f = d.fajr, let z = d.dhuhr, let a = d.asr, let m = d.maghrib, let i = d.isha else { return nil }
            let fajr = Date(timeIntervalSince1970: f)
            return PrayerDay(date: cal.startOfDay(for: fajr),
                             fajr: fajr,
                             dhuhr: Date(timeIntervalSince1970: z),
                             asr: Date(timeIntervalSince1970: a),
                             maghrib: Date(timeIntervalSince1970: m),
                             isha: Date(timeIntervalSince1970: i),
                             syuruk: d.syuruk.map { Date(timeIntervalSince1970: $0) })
        }
        guard !days.isEmpty else { throw URLError(.cannotParseResponse) }
        return (decoded.zone, days)
    }

    /// Straight from JAKIM e-Solat, used if the API above is down. Current month only.
    static func fetchJakim(zone: String) async throws -> [PrayerDay] {
        guard let url = URL(string: "https://www.e-solat.gov.my/index.php?r=esolatApi/TakwimSolat&period=month&zone=\(zone)") else {
            throw URLError(.badURL)
        }
        let (data, response) = try await URLSession.shared.data(for: URLRequest(url: url, timeoutInterval: 15))
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw URLError(.badServerResponse) }
        let decoded = try JSONDecoder().decode(JakimResponse.self, from: data)

        let cal = calendar
        let dayFormat = DateFormatter()
        dayFormat.locale = Locale(identifier: "en_US_POSIX")
        dayFormat.timeZone = malaysia
        dayFormat.dateFormat = "dd-MMM-yyyy"

        func at(_ time: String?, on day: Date) -> Date? {
            guard let time else { return nil }
            let parts = time.split(separator: ":").compactMap { Int($0) }
            guard parts.count >= 2 else { return nil }
            return cal.date(bySettingHour: parts[0], minute: parts[1], second: parts.count > 2 ? parts[2] : 0, of: day)
        }

        let days: [PrayerDay] = (decoded.prayerTime ?? []).compactMap { d in
            guard let ds = d.date, let day = dayFormat.date(from: ds),
                  let f = at(d.fajr, on: day), let z = at(d.dhuhr, on: day), let a = at(d.asr, on: day),
                  let m = at(d.maghrib, on: day), let i = at(d.isha, on: day) else { return nil }
            return PrayerDay(date: cal.startOfDay(for: day), fajr: f, dhuhr: z, asr: a, maghrib: m, isha: i,
                             syuruk: at(d.syuruk, on: day))
        }
        guard !days.isEmpty else { throw URLError(.cannotParseResponse) }
        return days
    }

    static func zoneName(_ code: String) -> String {
        switch code {
        case "WLY01": return "Kuala Lumpur"
        case "WLY02": return "Labuan"
        default:
            let states = ["JHR": "Johor", "KDH": "Kedah", "KTN": "Kelantan", "MLK": "Melaka",
                          "NGS": "Negeri Sembilan", "PHG": "Pahang", "PLS": "Perlis", "PNG": "Penang",
                          "PRK": "Perak", "SBH": "Sabah", "SGR": "Selangor", "SWK": "Sarawak",
                          "TRG": "Terengganu"]
            return states[String(code.prefix(3))] ?? code
        }
    }
}

// MARK: - Location

/// One-shot location lookups for working out the prayer zone.
@MainActor
final class LocationProvider: NSObject, CLLocationManagerDelegate {
    private let manager = CLLocationManager()
    private var completion: ((CLLocationCoordinate2D?) -> Void)?

    override init() {
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyKilometer
    }

    func requestLocation(_ completion: @escaping (CLLocationCoordinate2D?) -> Void) {
        finish(nil) // drop any earlier request
        self.completion = completion

        switch manager.authorizationStatus {
        case .notDetermined:
            manager.requestWhenInUseAuthorization()
        case .denied, .restricted:
            finish(nil)
            return
        default:
            manager.requestLocation()
        }

        DispatchQueue.main.asyncAfter(deadline: .now() + 20) { [weak self] in
            MainActor.assumeIsolated { self?.finish(nil) }
        }
    }

    private func finish(_ coordinate: CLLocationCoordinate2D?) {
        let callback = completion
        completion = nil
        callback?(coordinate)
    }

    nonisolated func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        let status = manager.authorizationStatus
        MainActor.assumeIsolated {
            guard self.completion != nil else { return }
            switch status {
            case .notDetermined:
                break
            case .denied, .restricted:
                self.finish(nil)
            default:
                self.manager.requestLocation()
            }
        }
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        let coordinate = locations.last?.coordinate
        MainActor.assumeIsolated { self.finish(coordinate) }
    }

    nonisolated func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        MainActor.assumeIsolated { self.finish(nil) }
    }
}

// MARK: - Controller

/// Today's prayer times for wherever the Mac is (JAKIM zone from location, else WLY01),
/// cached so it keeps working offline, plus the "it's time" trigger.
@MainActor
final class PrayerController: ObservableObject {
    @Published private(set) var days: [PrayerDay] = []
    @Published private(set) var zone: String
    @Published private(set) var zoneIsAutomatic = false
    @Published private(set) var now = Date()
    @Published private(set) var isLoading = false

    /// Called once when a prayer time arrives.
    var onPrayerTime: ((PrayerEvent) -> Void)?

    static let defaultZone = "WLY01"
    private static let cacheKey = "prayerCache.v1"

    private let location = LocationProvider()
    private var tickTimer: Timer?
    private var eventTimer: Timer?
    private var lastAlerted: Date?
    private var lastRefresh: Date?

    init() {
        zone = Self.defaultZone
        loadCache()
    }

    var zoneName: String { PrayerAPI.zoneName(zone) }

    func start() {
        refresh()
        scheduleNextEvent()

        let t = Timer(timeInterval: 15, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        t.tolerance = 2
        RunLoop.main.add(t, forMode: .common)
        tickTimer = t

        _ = NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification,
                                                              object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.now = Date()
                self?.refresh()
                self?.scheduleNextEvent()
            }
        }
    }

    // MARK: Queries

    var today: PrayerDay? { day(for: now) }

    func day(for date: Date) -> PrayerDay? {
        let cal = PrayerAPI.calendar
        return days.first { cal.isDate($0.date, inSameDayAs: date) }
    }

    private var allEvents: [PrayerEvent] {
        days.flatMap { d in PrayerName.allCases.map { PrayerEvent(name: $0, time: d.time($0)) } }
    }

    var nextEvent: PrayerEvent? {
        if let next = allEvents.filter({ $0.time > now }).min(by: { $0.time < $1.time }) {
            return next
        }
        // Past the end of the data we have: tomorrow's Fajr is about the same time as today's.
        if let last = days.max(by: { $0.date < $1.date }) {
            let guess = last.fajr.addingTimeInterval(86_400)
            if guess > now { return PrayerEvent(name: .fajr, time: guess) }
        }
        return nil
    }

    var currentEvent: PrayerEvent? {
        allEvents.filter { $0.time <= now }.max(by: { $0.time < $1.time })
    }

    var window: PrayerWindow {
        if let current = currentEvent, now.timeIntervalSince(current.time) < 15 * 60 {
            return .now(current)
        }
        if let next = nextEvent, next.time.timeIntervalSince(now) <= 10 * 60 {
            return .soon(next)
        }
        return .none
    }

    func countdownText(to event: PrayerEvent) -> String {
        let seconds = Int(event.time.timeIntervalSince(now))
        if seconds <= 0 { return "now" }
        let minutes = Int((Double(seconds) / 60).rounded(.up))
        if minutes < 60 { return "in \(minutes)m" }
        return "in \(minutes / 60)h \(minutes % 60)m"
    }

    // MARK: Timing

    private func tick() {
        now = Date()
        checkDue()
        if today == nil || (lastRefresh.map { now.timeIntervalSince($0) > 6 * 3600 } ?? true) {
            refresh()
        }
    }

    private func scheduleNextEvent() {
        eventTimer?.invalidate()
        guard let next = nextEvent else { return }
        let t = Timer(fire: next.time.addingTimeInterval(0.5), interval: 0, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.now = Date()
                self?.checkDue()
                self?.scheduleNextEvent()
            }
        }
        RunLoop.main.add(t, forMode: .common)
        eventTimer = t
    }

    /// Fires `onPrayerTime` for a prayer that started within the last 2 minutes (never twice).
    private func checkDue() {
        guard let current = currentEvent,
              now.timeIntervalSince(current.time) < 120,
              lastAlerted != current.time else { return }
        lastAlerted = current.time
        onPrayerTime?(current)
    }

    // MARK: Loading

    func refresh() {
        guard !isLoading else { return }
        isLoading = true
        location.requestLocation { [weak self] coordinate in
            guard let self else { return }
            Task { @MainActor in
                await self.load(coordinate: coordinate)
                self.isLoading = false
            }
        }
    }

    private func load(coordinate: CLLocationCoordinate2D?) async {
        var newZone = zone
        var automatic = zoneIsAutomatic
        var fetched: [PrayerDay] = []

        if let c = coordinate {
            let path = String(format: "gps/%.4f/%.4f", c.latitude, c.longitude)
            if let result = try? await PrayerAPI.fetchWaktuSolat(path: path) {
                if let z = result.zone, !z.isEmpty { newZone = z.uppercased() }
                fetched = result.days
                automatic = true
            }
        }
        if fetched.isEmpty {
            if coordinate == nil { automatic = false }
            if let result = try? await PrayerAPI.fetchWaktuSolat(path: newZone) {
                fetched = result.days
            } else if let jakim = try? await PrayerAPI.fetchJakim(zone: newZone) {
                fetched = jakim
            }
        }
        guard !fetched.isEmpty else { return } // offline: keep what's cached

        // Next month too (best effort), so the first days of next month work offline.
        let cal = PrayerAPI.calendar
        if let last = fetched.map(\.date).max(),
           let nextMonth = cal.date(byAdding: .day, value: 1, to: last) {
            let comps = cal.dateComponents([.year, .month], from: nextMonth)
            if let y = comps.year, let m = comps.month,
               let more = try? await PrayerAPI.fetchWaktuSolat(path: newZone, year: y, month: m) {
                fetched.append(contentsOf: more.days)
            }
        }

        var byDay: [Date: PrayerDay] = [:]
        for d in fetched { byDay[d.date] = d }
        let cutoff = cal.date(byAdding: .day, value: -2, to: cal.startOfDay(for: Date())) ?? .distantPast
        let merged = byDay.values.filter { $0.date >= cutoff }.sorted { $0.date < $1.date }

        zone = newZone
        zoneIsAutomatic = automatic
        days = merged
        lastRefresh = Date()
        now = Date()
        saveCache()
        scheduleNextEvent()
    }

    private func loadCache() {
        guard let data = UserDefaults.standard.data(forKey: Self.cacheKey),
              let cache = try? JSONDecoder().decode(PrayerCache.self, from: data) else { return }
        zone = cache.zone
        zoneIsAutomatic = cache.automatic
        days = cache.days
    }

    private func saveCache() {
        let cache = PrayerCache(zone: zone, automatic: zoneIsAutomatic, days: days)
        if let data = try? JSONEncoder().encode(cache) {
            UserDefaults.standard.set(data, forKey: Self.cacheKey)
        }
    }
}
