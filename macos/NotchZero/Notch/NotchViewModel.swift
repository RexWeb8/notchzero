import SwiftUI
import AppKit

enum NotchState: Equatable {
    case closed
    case open
    case drop
    case alert   // prayer-time card
    case peek    // notch grows down for a moment to show the new song
}

/// What the closed notch shows in its side "wings".
enum ClosedMode: Equatable {
    case plain       // nothing: just the notch
    case music       // album orb + dot sphere
    case companion   // next prayer + Kapas
    case prayer      // prayer coming up / just started + Kapas
}

enum NotchStyle: String, CaseIterable, Identifiable {
    case liquid
    case frost
    case dark

    var id: String { rawValue }

    var title: String {
        switch self {
        case .liquid: return "Liquid Glass"
        case .frost: return "Frosted Glass"
        case .dark: return "Solid Black"
        }
    }
}

struct NotchMetrics: Equatable {
    var notchWidth: CGFloat = 190
    var notchHeight: CGFloat = 32
    var hasPhysicalNotch = false

    static func from(_ screen: NSScreen) -> NotchMetrics {
        var m = NotchMetrics()
        let top = screen.safeAreaInsets.top
        if top > 0, let left = screen.auxiliaryTopLeftArea, let right = screen.auxiliaryTopRightArea {
            m.notchWidth = screen.frame.width - left.width - right.width
            m.notchHeight = top
            m.hasPhysicalNotch = true
        } else {
            let menuBar = screen.frame.maxY - screen.visibleFrame.maxY
            m.notchHeight = menuBar > 0 ? menuBar : 28
            m.notchWidth = 190
        }
        return m
    }
}

@MainActor
final class NotchViewModel: ObservableObject {
    @Published var state: NotchState = .closed
    @Published var metrics = NotchMetrics()
    @Published var closedMode: ClosedMode = .plain
    @Published var kapasMood: KapasMood = .awake
    @Published var alertEvent: PrayerEvent?
    @Published var dropTargeted = false
    @Published var style: NotchStyle {
        didSet { UserDefaults.standard.set(style.rawValue, forKey: Self.styleKey) }
    }
    @Published var showCompanion: Bool {
        didSet { UserDefaults.standard.set(showCompanion, forKey: Self.companionKey) }
    }
    @Published var mascot: MascotKind {
        didSet { UserDefaults.standard.set(mascot.rawValue, forKey: Self.mascotKey) }
    }
    @Published var showSongPeek: Bool {
        didSet { UserDefaults.standard.set(showSongPeek, forKey: Self.peekKey) }
    }

    /// True while a menu or file picker is up — stops the notch from closing under it.
    var isInteractionLocked = false

    var onSettings: () -> Void = {}
    var onPickAirDrop: () -> Void = {}
    var onDropURLs: ([URL]) -> Void = { _ in }
    var onDismissAlert: () -> Void = {}

    static let styleKey = "notchStyle"
    static let companionKey = "showKapasWhenIdle"
    static let peekKey = "showSongPeek"
    static let mascotKey = "mascotKind"
    static let peekContentHeight: CGFloat = 40
    static let openWidth: CGFloat = 900
    static let openContentHeight: CGFloat = 196
    static let dropWidth: CGFloat = 560
    static let dropContentHeight: CGFloat = 112
    static let alertWidth: CGFloat = 400
    static let alertContentHeight: CGFloat = 72

    init() {
        let raw = UserDefaults.standard.string(forKey: Self.styleKey) ?? NotchStyle.liquid.rawValue
        style = NotchStyle(rawValue: raw) ?? .liquid
        showCompanion = UserDefaults.standard.object(forKey: Self.companionKey) as? Bool ?? true
        showSongPeek = UserDefaults.standard.object(forKey: Self.peekKey) as? Bool ?? true
        mascot = MascotKind(rawValue: UserDefaults.standard.string(forKey: Self.mascotKey) ?? "") ?? .cat
    }

    var isOpen: Bool { state != .closed }

    /// Glass background for the big states; closed and peek stay black like the real notch.
    var usesGlass: Bool { state == .open || state == .drop || state == .alert }

    /// The closed notch has wings (music, Kapas or prayer).
    var liveActivity: Bool { closedMode != .plain }

    func topRadius(for s: NotchState) -> CGFloat {
        (s == .closed || s == .peek) ? 6 : 18
    }

    func bottomRadius(for s: NotchState) -> CGFloat {
        switch s {
        case .closed: return liveActivity ? 12 : 10
        case .open: return 32
        case .drop: return 28
        case .alert: return 28
        case .peek: return 18
        }
    }

    /// Width of each wing beside the notch.
    var liveSideWidth: CGFloat {
        metrics.notchHeight + (closedMode == .music ? 4 : 12)
    }

    /// Album orb / dot sphere / Kapas size in the closed notch.
    var liveItemSize: CGFloat { (metrics.notchHeight * 0.6).rounded() }

    func size(for s: NotchState) -> CGSize {
        switch s {
        case .closed:
            let ears = topRadius(for: .closed) * 2
            if liveActivity {
                return CGSize(width: metrics.notchWidth + liveSideWidth * 2 + ears, height: metrics.notchHeight)
            }
            return CGSize(width: metrics.notchWidth - 4 + ears, height: metrics.notchHeight)
        case .open:
            return CGSize(width: Self.openWidth, height: metrics.notchHeight + Self.openContentHeight)
        case .drop:
            return CGSize(width: Self.dropWidth, height: metrics.notchHeight + Self.dropContentHeight)
        case .alert:
            return CGSize(width: Self.alertWidth, height: metrics.notchHeight + Self.alertContentHeight)
        case .peek:
            // Same width as the closed notch while music plays, just taller.
            let ears = topRadius(for: .closed) * 2
            let side = metrics.notchHeight + 4
            return CGSize(width: metrics.notchWidth + side * 2 + ears,
                          height: metrics.notchHeight + Self.peekContentHeight)
        }
    }

    var windowSize: CGSize {
        CGSize(width: Self.openWidth + 80, height: metrics.notchHeight + Self.openContentHeight + 50)
    }
}
