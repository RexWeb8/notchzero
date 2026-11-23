import SwiftUI

struct NotchRootView: View {
    @ObservedObject var vm: NotchViewModel
    @ObservedObject var music: MusicController
    @ObservedObject var system: SystemMonitor
    @ObservedObject var prayer: PrayerController

    var body: some View {
        let size = vm.size(for: vm.state)
        let openSize = vm.size(for: .open)
        let dropSize = vm.size(for: .drop)
        let isOpen = vm.state == .open
        let shape = NotchShape(topRadius: vm.topRadius(for: vm.state),
                               bottomRadius: vm.bottomRadius(for: vm.state))

        ZStack(alignment: .top) {
            NotchBackground(style: vm.style, isOpen: vm.usesGlass, shape: shape)

            ZStack(alignment: .top) {
                if (vm.state == .closed || vm.state == .peek) && vm.liveActivity {
                    VStack(spacing: 0) {
                        ClosedLiveView(music: music, vm: vm, prayer: prayer)
                            .frame(width: vm.size(for: .closed).width, height: vm.metrics.notchHeight)
                        if vm.state == .peek {
                            SongPeekRow(music: music)
                                .frame(width: vm.size(for: .peek).width, height: NotchViewModel.peekContentHeight)
                                .transition(AnyTransition.opacity.animation(.easeOut(duration: 0.22).delay(0.06)))
                        }
                    }
                    .frame(width: size.width, height: size.height, alignment: .top)
                    .transition(AnyTransition.opacity.animation(.easeOut(duration: 0.18)))
                }

                // The open panel stays alive (just hidden) so hovering never has to rebuild it —
                // the shape grows and reveals it, keeping every frame cheap.
                OpenNotchView(vm: vm, music: music, system: system, prayer: prayer, isVisible: isOpen)
                    .frame(width: openSize.width, height: openSize.height)
                    .opacity(isOpen ? 1 : 0)
                    .scaleEffect(isOpen ? 1 : 0.96, anchor: .top)
                    .animation(isOpen ? .easeOut(duration: 0.24).delay(0.05) : .easeIn(duration: 0.1), value: isOpen)
                    .allowsHitTesting(isOpen)

                if vm.state == .alert, let event = vm.alertEvent {
                    PrayerAlertView(vm: vm, event: event, zoneName: prayer.zoneName)
                        .frame(width: vm.size(for: .alert).width, height: vm.size(for: .alert).height)
                        .transition(AnyTransition.opacity.animation(.easeOut(duration: 0.25).delay(0.08)))
                }

                if vm.state == .drop {
                    DropNotchView(vm: vm)
                        .frame(width: dropSize.width, height: dropSize.height)
                        .transition(AnyTransition.opacity.animation(.easeOut(duration: 0.18)))
                }
            }
            .frame(width: size.width, height: size.height, alignment: .top)
            .clipShape(shape)
        }
        .frame(width: size.width, height: size.height)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .animation(.spring(response: 0.4, dampingFraction: 0.85), value: vm.closedMode)
        .foregroundStyle(Color.white)
        .environment(\.colorScheme, .dark)
        .environment(\.mascotKind, vm.mascot)
    }
}

/// Black when closed (blends into the real notch), glass when open.
struct NotchBackground: View {
    let style: NotchStyle
    let isOpen: Bool
    let shape: NotchShape

    var body: some View {
        ZStack {
            shape
                .fill(Color.black)
                .opacity(isOpen && style != .dark ? 0 : 1)
                .shadow(color: Color.black.opacity(isOpen && style == .dark ? 0.45 : 0), radius: 18, x: 0, y: 8)

            if style == .liquid {
                Color.clear
                    .glassEffect(.regular.tint(Color.black.opacity(0.30)), in: shape)
                    .opacity(isOpen ? 1 : 0)
            } else if style == .frost {
                VisualEffectBlur(material: .hudWindow)
                    .clipShape(shape)
                    .opacity(isOpen ? 1 : 0)
            }

            shape
                .stroke(Color.white.opacity(isOpen ? 0.12 : 0), lineWidth: 1)
        }
    }
}

// MARK: - Open state

struct OpenNotchView: View {
    @ObservedObject var vm: NotchViewModel
    @ObservedObject var music: MusicController
    @ObservedObject var system: SystemMonitor
    @ObservedObject var prayer: PrayerController
    var isVisible: Bool

    var body: some View {
        VStack(spacing: 0) {
            TopBar(vm: vm)
                .frame(height: vm.metrics.notchHeight)

            HStack(alignment: .top, spacing: 16) {
                MusicCard(music: music, isActive: isVisible)
                    .frame(width: 168, height: 168)
                PrayerCard(prayer: prayer, kapasMood: vm.kapasMood, isVisible: isVisible)
                    .frame(maxWidth: .infinity)
                    .frame(height: 168)
                SystemCard(system: system)
                    .frame(width: 222, height: 168)
            }
            .padding(.top, 10)

            Spacer(minLength: 0)
        }
        .padding(.horizontal, vm.topRadius(for: .open) + 16)
    }
}

/// Time and date on the left; AirDrop and settings on the right.
struct TopBar: View {
    @ObservedObject var vm: NotchViewModel

    private static let time: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US")
        f.dateFormat = "h:mm a"
        return f
    }()

    private static let date: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US")
        f.dateFormat = "EEE, d MMM"
        return f
    }()

    var body: some View {
        HStack(spacing: 0) {
            TimelineView(.everyMinute) { ctx in
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(Self.time.string(from: ctx.date))
                        .font(.system(size: 14, weight: .semibold, design: .rounded).monospacedDigit())
                        .foregroundStyle(Color.white)
                    Text(Self.date.string(from: ctx.date))
                        .font(.system(size: 11.5, weight: .medium))
                        .foregroundStyle(Color.white.opacity(0.55))
                }
                .lineLimit(1)
            }

            Spacer(minLength: vm.metrics.notchWidth + 20)

            HStack(spacing: 6) {
                IconButton(systemName: "antenna.radiowaves.left.and.right", size: 12) {
                    vm.onPickAirDrop()
                }
                .help("AirDrop Files")
                IconButton(systemName: "gearshape.fill", size: 13) {
                    vm.onSettings()
                }
                .help("Settings")
            }
        }
        .padding(.top, 2)
    }
}

// MARK: - Closed "live activity"

struct ClosedLiveView: View {
    @ObservedObject var music: MusicController
    @ObservedObject var vm: NotchViewModel
    @ObservedObject var prayer: PrayerController

    var body: some View {
        let item = vm.liveItemSize
        let side = vm.liveSideWidth
        HStack(spacing: 0) {
            Group {
                switch vm.closedMode {
                case .music:
                    EqualizerBars(isPlaying: music.isPlaying)
                        .frame(width: item * 0.72, height: item * 0.62)
                case .companion:
                    if let next = prayer.nextEvent {
                        PrayerMini(title: next.name.title, value: Self.short.string(from: next.time))
                    }
                case .prayer:
                    switch prayer.window {
                    case .soon(let e):
                        PrayerMini(title: e.name.title, value: prayer.countdownText(to: e).replacingOccurrences(of: "in ", with: ""))
                    case .now(let e):
                        PrayerMini(title: e.name.title, value: "now")
                    case .none:
                        EmptyView()
                    }
                case .plain:
                    EmptyView()
                }
            }
            .frame(width: side)

            Spacer(minLength: 0)

            Group {
                switch vm.closedMode {
                case .music, .companion, .prayer:
                    MascotView(mood: vm.kapasMood, size: item, mat: vm.kapasMood == .praying)
                case .plain:
                    EmptyView()
                }
            }
            .frame(width: side)
        }
        .padding(.horizontal, vm.topRadius(for: .closed))
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private static let short: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US")
        f.timeZone = PrayerAPI.malaysia
        f.dateFormat = "h:mm"
        return f
    }()
}

/// Two-line prayer label for the closed notch: "ASR" over "4:20" / "9m" / "now".
struct PrayerMini: View {
    let title: String
    let value: String

    var body: some View {
        VStack(spacing: 0) {
            Text(title.uppercased())
                .font(.system(size: 7, weight: .bold))
                .tracking(0.3)
                .foregroundStyle(Color.white.opacity(0.5))
            Text(value)
                .font(.system(size: 11, weight: .semibold, design: .rounded).monospacedDigit())
                .foregroundStyle(Color.white)
        }
        .lineLimit(1)
        .minimumScaleFactor(0.7)
    }
}

// MARK: - Prayer alert card

struct PrayerAlertView: View {
    @ObservedObject var vm: NotchViewModel
    let event: PrayerEvent
    let zoneName: String

    private static let time: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US")
        f.timeZone = PrayerAPI.malaysia
        f.timeStyle = .short
        f.dateStyle = .none
        return f
    }()

    var body: some View {
        VStack(spacing: 0) {
            Color.clear
                .frame(height: vm.metrics.notchHeight)
            HStack(spacing: 16) {
                MascotView(mood: .praying, size: 46, mat: true)
                VStack(alignment: .leading, spacing: 3) {
                    Text("Time for \(event.name.title)")
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundStyle(Color.white)
                    Text("\(Self.time.string(from: event.time)) · \(zoneName)")
                        .font(.system(size: 11.5, weight: .medium))
                        .foregroundStyle(Color.white.opacity(0.55))
                }
                Spacer(minLength: 0)
            }
            .padding(.horizontal, vm.topRadius(for: .alert) + 14)
            .frame(maxHeight: .infinity)
            .padding(.bottom, 6)
        }
    }
}

// MARK: - Song peek

/// One quiet row under the notch when a new song starts: art, title, artist, elapsed time.
struct SongPeekRow: View {
    @ObservedObject var music: MusicController

    var body: some View {
        HStack(spacing: 9) {
            Group {
                if let art = music.artwork {
                    Image(nsImage: art)
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                } else {
                    ZStack {
                        Color.white.opacity(0.1)
                        Image(systemName: "music.note")
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundStyle(Color.white.opacity(0.6))
                    }
                }
            }
            .frame(width: 26, height: 26)
            .clipShape(RoundedRectangle(cornerRadius: 7, style: .continuous))

            VStack(alignment: .leading, spacing: 0) {
                Text(music.title)
                    .font(.system(size: 11.5, weight: .semibold))
                    .foregroundStyle(Color.white)
                Text(music.artist)
                    .font(.system(size: 9.5, weight: .medium))
                    .foregroundStyle(Color.white.opacity(0.55))
            }
            .lineLimit(1)

            Spacer(minLength: 4)

            TimelineView(.periodic(from: .now, by: 1)) { ctx in
                Text(CardProgress.format(music.currentPosition(at: ctx.date)))
                    .font(.system(size: 10, weight: .semibold, design: .rounded).monospacedDigit())
                    .foregroundStyle(Color.white.opacity(0.55))
            }
        }
        .padding(.horizontal, 16)
        .padding(.bottom, 6)
    }
}
