import AppKit
import SwiftUI

enum MusicSource: String, CaseIterable, Sendable {
    case spotify
    case appleMusic

    var bundleID: String {
        switch self {
        case .spotify: return "com.spotify.client"
        case .appleMusic: return "com.apple.Music"
        }
    }

    /// Name used in AppleScript `tell application "..."`.
    var appName: String {
        switch self {
        case .spotify: return "Spotify"
        case .appleMusic: return "Music"
        }
    }

    var displayName: String {
        switch self {
        case .spotify: return "Spotify"
        case .appleMusic: return "Apple Music"
        }
    }
}

private struct PlayerSnapshot: Sendable {
    let source: MusicSource
    let isPlaying: Bool
    let title: String
    let artist: String
    let album: String
    let duration: Double
    let position: Double
    let trackID: String
    let artworkURL: String?
}

private enum QueryResult: Sendable {
    case track(PlayerSnapshot)
    case idle
    case denied
}

/// Talks to Spotify / Music through AppleScript. Only ever used on `MusicController.scriptQueue`,
/// so the main thread (and every animation) never waits on another app.
private final class ScriptRunner: @unchecked Sendable {
    private var scripts: [String: NSAppleScript] = [:]

    func run(_ source: String, cacheKey: String?) -> (descriptor: NSAppleEventDescriptor?, errorCode: Int) {
        let script: NSAppleScript
        if let key = cacheKey, let cached = scripts[key] {
            script = cached
        } else {
            guard let s = NSAppleScript(source: source) else { return (nil, 0) }
            var compileError: NSDictionary?
            guard s.compileAndReturnError(&compileError) else { return (nil, 0) }
            if let key = cacheKey { scripts[key] = s }
            script = s
        }

        var error: NSDictionary?
        let result = script.executeAndReturnError(&error)
        if let error {
            let code = (error[NSAppleScript.errorNumber] as? NSNumber)?.intValue ?? 0
            return (nil, code)
        }
        return (result, 0)
    }

    func query(_ src: MusicSource) -> QueryResult {
        let source: String
        switch src {
        case .spotify:
            source = """
            tell application "Spotify"
                if player state is stopped then return {"stopped"}
                set t to current track
                return {"ok", (player state is playing), (name of t), (artist of t), (album of t), (duration of t), (player position), (artwork url of t), (id of t)}
            end tell
            """
        case .appleMusic:
            source = """
            tell application "Music"
                if player state is stopped then return {"stopped"}
                set t to current track
                set tid to ""
                try
                    set tid to (persistent ID of t) as string
                end try
                return {"ok", (player state is playing), (name of t), (artist of t), (album of t), (duration of t), (player position), "", tid}
            end tell
            """
        }

        let (descriptor, code) = run(source, cacheKey: "query-\(src.rawValue)")
        if code == -1743 { return .denied } // errAEEventNotPermitted: blocked in Automation
        guard let d = descriptor, d.numberOfItems >= 9, d.atIndex(1)?.stringValue == "ok" else { return .idle }

        let playing = d.atIndex(2)?.booleanValue ?? false
        let title = d.atIndex(3)?.stringValue ?? ""
        let artist = d.atIndex(4)?.stringValue ?? ""
        let album = d.atIndex(5)?.stringValue ?? ""
        var duration = d.atIndex(6)?.doubleValue ?? 0
        if src == .spotify { duration /= 1000 } // Spotify reports milliseconds
        let position = d.atIndex(7)?.doubleValue ?? 0
        let art = d.atIndex(8)?.stringValue
        var tid = d.atIndex(9)?.stringValue ?? ""
        if tid.isEmpty { tid = "\(title)|\(artist)|\(album)" }

        return .track(PlayerSnapshot(source: src, isPlaying: playing, title: title, artist: artist, album: album,
                                     duration: duration, position: position, trackID: tid,
                                     artworkURL: (art?.isEmpty ?? true) ? nil : art))
    }

    func musicArtworkData() -> Data? {
        if let d = run("tell application \"Music\" to get raw data of artwork 1 of current track",
                       cacheKey: "music-art-raw").descriptor {
            return d.data
        }
        if let d = run("tell application \"Music\" to get data of artwork 1 of current track",
                       cacheKey: "music-art-data").descriptor {
            return d.data
        }
        return nil
    }
}

/// What's playing in Spotify or Apple Music, plus playback controls.
@MainActor
final class MusicController: ObservableObject {
    @Published private(set) var source: MusicSource?
    @Published private(set) var title = ""
    @Published private(set) var artist = ""
    @Published private(set) var album = ""
    @Published private(set) var duration: Double = 0
    @Published private(set) var isPlaying = false
    @Published private(set) var artwork: NSImage?
    @Published private(set) var trackID = ""
    @Published private(set) var deniedSources: Set<MusicSource> = []

    private let scriptQueue = DispatchQueue(label: "com.farisfadzli.NotchZero.applescript", qos: .userInitiated)
    private let runner = ScriptRunner()
    private var refreshInFlight = false
    private var refreshQueued = false

    private var position: Double = 0
    private var positionStamp = Date()
    private var lastActive: MusicSource?
    private var timer: Timer?
    private var active = false
    private var artworkTrackID = ""
    private var resumeWork: DispatchWorkItem?

    // MARK: - Lifecycle

    func start() {
        let dnc = DistributedNotificationCenter.default()
        for name in ["com.spotify.client.PlaybackStateChanged", "com.apple.Music.playerInfo"] {
            _ = dnc.addObserver(forName: Notification.Name(name), object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.refreshSoon() }
            }
        }
        _ = NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didTerminateApplicationNotification,
                                                              object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        refresh()
        reschedule()
    }

    func setActive(_ value: Bool) {
        guard value != active else { return }
        active = value
        if value { refresh() }
        reschedule()
    }

    private func reschedule() {
        timer?.invalidate()
        let interval: TimeInterval = active ? 1.5 : 10
        let t = Timer(timeInterval: interval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        t.tolerance = interval * 0.2
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    private func refreshSoon() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { [weak self] in
            MainActor.assumeIsolated { self?.refresh() }
        }
    }

    // MARK: - Reading state (AppleScript runs on scriptQueue)

    func refresh() {
        if refreshInFlight {
            refreshQueued = true
            return
        }
        refreshInFlight = true
        let running = MusicSource.allCases.filter { isRunning($0) }
        let runner = self.runner
        scriptQueue.async {
            var results: [(MusicSource, QueryResult)] = []
            for src in running {
                results.append((src, runner.query(src)))
            }
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    self.finishRefresh(results)
                }
            }
        }
    }

    private func finishRefresh(_ results: [(MusicSource, QueryResult)]) {
        refreshInFlight = false

        var snaps: [PlayerSnapshot] = []
        var denied: Set<MusicSource> = []
        for (src, result) in results {
            switch result {
            case .track(let snapshot): snaps.append(snapshot)
            case .idle: break
            case .denied: denied.insert(src)
            }
        }
        if denied != deniedSources { deniedSources = denied }

        let pick = snaps.first(where: { $0.isPlaying && $0.source == lastActive })
            ?? snaps.first(where: { $0.isPlaying })
            ?? snaps.first(where: { $0.source == lastActive })
            ?? snaps.first
        apply(pick)

        if refreshQueued {
            refreshQueued = false
            refresh()
        }
    }

    private func apply(_ s: PlayerSnapshot?) {
        guard let s else {
            if source != nil { source = nil }
            if isPlaying { isPlaying = false }
            if !title.isEmpty { title = "" }
            if !artist.isEmpty { artist = "" }
            if !album.isEmpty { album = "" }
            if duration != 0 { duration = 0 }
            if !trackID.isEmpty { trackID = "" }
            if artwork != nil { artwork = nil }
            artworkTrackID = ""
            position = 0
            return
        }
        if s.isPlaying { lastActive = s.source }
        if source != s.source { source = s.source }
        if title != s.title { title = s.title }
        if artist != s.artist { artist = s.artist }
        if album != s.album { album = s.album }
        if duration != s.duration { duration = s.duration }
        position = s.position
        positionStamp = Date()
        if isPlaying != s.isPlaying { isPlaying = s.isPlaying }
        if trackID != s.trackID { trackID = s.trackID }
        if artworkTrackID != s.trackID { loadArtwork(for: s) }
    }

    /// Playback position, extrapolated between polls so the progress bar moves smoothly.
    func currentPosition(at date: Date = Date()) -> Double {
        guard duration > 0 else { return 0 }
        let p = isPlaying ? position + date.timeIntervalSince(positionStamp) : position
        return min(max(p, 0), duration)
    }

    private func isRunning(_ src: MusicSource) -> Bool {
        !NSRunningApplication.runningApplications(withBundleIdentifier: src.bundleID).isEmpty
    }

    // MARK: - Artwork

    private func loadArtwork(for s: PlayerSnapshot) {
        artworkTrackID = s.trackID
        let expected = s.trackID

        switch s.source {
        case .spotify:
            guard let str = s.artworkURL, let url = URL(string: str) else {
                artwork = nil
                return
            }
            URLSession.shared.dataTask(with: url) { data, _, _ in
                DispatchQueue.main.async {
                    MainActor.assumeIsolated {
                        guard self.trackID == expected else { return }
                        self.artwork = data.flatMap { NSImage(data: $0) }
                    }
                }
            }.resume()

        case .appleMusic:
            let runner = self.runner
            scriptQueue.async {
                let data = runner.musicArtworkData()
                DispatchQueue.main.async {
                    MainActor.assumeIsolated {
                        guard self.trackID == expected else { return }
                        self.artwork = data.flatMap { NSImage(data: $0) }
                    }
                }
            }
        }
    }

    // MARK: - Controls

    func playPause() {
        guard let src = source ?? lastActive else {
            openPlayer(.spotify)
            return
        }
        position = currentPosition()
        positionStamp = Date()
        isPlaying.toggle() // optimistic, corrected on next refresh
        command("playpause", on: src)
    }

    func next() {
        guard let src = source else { return }
        command("next track", on: src)
    }

    func previous() {
        guard let src = source else { return }
        command("previous track", on: src)
    }

    func seek(toFraction fraction: Double) {
        guard let src = source, duration > 0 else { return }
        let target = max(0, min(1, fraction)) * duration
        position = target
        positionStamp = Date()
        objectWillChange.send()
        command("set player position to \(target)", on: src, cache: false)
    }

    /// Pauses whatever is playing for a few seconds, then resumes it, unless you've already
    /// pressed play or switched player in the meantime. Used when a prayer time comes in.
    func pauseBriefly(seconds: TimeInterval) {
        guard isPlaying, let src = source else { return }
        position = currentPosition()
        positionStamp = Date()
        isPlaying = false
        command("pause", on: src)

        resumeWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.source == src, !self.isPlaying else { return }
                self.position = self.currentPosition()
                self.positionStamp = Date()
                self.isPlaying = true
                self.command("play", on: src)
            }
        }
        resumeWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: work)
    }

    private func command(_ cmd: String, on src: MusicSource, cache: Bool = true) {
        let source = "tell application \"\(src.appName)\" to \(cmd)"
        let key: String? = cache ? "\(src.rawValue)-\(cmd)" : nil
        let runner = self.runner
        scriptQueue.async {
            _ = runner.run(source, cacheKey: key)
        }
        refreshSoon()
    }

    func openPlayer(_ src: MusicSource) {
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: src.bundleID) else {
            NSSound.beep()
            return
        }
        NSWorkspace.shared.openApplication(at: url, configuration: NSWorkspace.OpenConfiguration()) { _, _ in }
    }

    func isInstalled(_ src: MusicSource) -> Bool {
        NSWorkspace.shared.urlForApplication(withBundleIdentifier: src.bundleID) != nil
    }

    func openAutomationSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation") {
            NSWorkspace.shared.open(url)
        }
    }
}
