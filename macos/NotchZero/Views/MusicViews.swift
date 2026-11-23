import SwiftUI
import AppKit

/// Square music tile: artwork fills the card, a glass pill with the silver-ring orb and title on top,
/// time, progress and glass controls at the bottom.
struct MusicCard: View {
    @ObservedObject var music: MusicController
    /// False while the panel is hidden, so the ring and progress bar stop animating.
    var isActive: Bool = true

    private let corner: CGFloat = 22

    var body: some View {
        ZStack {
            artwork
            if music.source != nil {
                LinearGradient(stops: [
                    .init(color: Color.black.opacity(0.45), location: 0),
                    .init(color: Color.black.opacity(0), location: 0.35),
                    .init(color: Color.black.opacity(0), location: 0.45),
                    .init(color: Color.black.opacity(0.72), location: 1)
                ], startPoint: .top, endPoint: .bottom)
                playing
            } else if let denied = music.deniedSources.first {
                permission(denied)
            } else {
                empty
            }
        }
        .frame(width: 168, height: 168)
        .clipShape(RoundedRectangle(cornerRadius: corner, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: corner, style: .continuous).strokeBorder(Color.white.opacity(0.1), lineWidth: 1))
        .animation(.easeInOut(duration: 0.3), value: music.trackID)
    }

    @ViewBuilder
    private var artwork: some View {
        if let image = music.artwork {
            Image(nsImage: image)
                .resizable()
                .aspectRatio(contentMode: .fill)
                .frame(width: 168, height: 168)
                .id(music.trackID)
                .transition(.opacity)
        } else {
            ZStack {
                Color.white.opacity(0.06)
                Image(systemName: "music.note")
                    .font(.system(size: 30, weight: .medium))
                    .foregroundStyle(Color.white.opacity(music.source == nil ? 0.25 : 0.4))
                    .offset(y: music.source == nil ? -22 : 0)
            }
        }
    }

    private var playing: some View {
        VStack(spacing: 0) {
            // Title pill
            HStack(spacing: 7) {
                AlbumOrb(image: music.artwork, size: 26, isSpinning: music.isPlaying && isActive)
                VStack(alignment: .leading, spacing: 0) {
                    Text(music.title.isEmpty ? "Unknown Track" : music.title)
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(Color.white)
                    Text(music.artist)
                        .font(.system(size: 9.5, weight: .medium))
                        .foregroundStyle(Color.white.opacity(0.7))
                }
                .lineLimit(1)
                Spacer(minLength: 0)
            }
            .padding(.leading, 4)
            .padding(.trailing, 10)
            .padding(.vertical, 4)
            .glassEffect(.regular.tint(Color.black.opacity(0.25)), in: Capsule())

            Spacer(minLength: 0)

            CardProgress(music: music, isActive: isActive)

            HStack(spacing: 12) {
                GlassButton(systemName: "backward.fill", diameter: 28, iconSize: 10) { music.previous() }
                GlassButton(systemName: music.isPlaying ? "pause.fill" : "play.fill", diameter: 34, iconSize: 13) { music.playPause() }
                GlassButton(systemName: "forward.fill", diameter: 28, iconSize: 10) { music.next() }
            }
            .padding(.top, 6)
        }
        .padding(8)
    }

    private var empty: some View {
        VStack(spacing: 6) {
            Spacer(minLength: 0)
            Text("Nothing Playing")
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(Color.white)
            HStack(spacing: 6) {
                if music.isInstalled(.spotify) {
                    PillButton(title: "Spotify") { music.openPlayer(.spotify) }
                }
                PillButton(title: "Music") { music.openPlayer(.appleMusic) }
            }
        }
        .padding(.bottom, 16)
    }

    private func permission(_ src: MusicSource) -> some View {
        VStack(spacing: 6) {
            Spacer(minLength: 0)
            Text("Allow \(src.displayName)")
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(Color.white)
            Text("Privacy & Security › Automation")
                .font(.system(size: 9.5))
                .foregroundStyle(Color.white.opacity(0.5))
            PillButton(title: "Open Settings") { music.openAutomationSettings() }
        }
        .padding(.bottom, 14)
    }
}

/// Round control with a glass background, like the reference player.
struct GlassButton: View {
    let systemName: String
    var diameter: CGFloat
    var iconSize: CGFloat
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: iconSize, weight: .bold))
                .foregroundStyle(Color.white)
                .contentTransition(.symbolEffect(.replace))
                .frame(width: diameter, height: diameter)
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .glassEffect(.regular.tint(Color.black.opacity(hover ? 0.1 : 0.25)), in: Circle())
        .scaleEffect(hover ? 1.06 : 1)
        .animation(.easeOut(duration: 0.15), value: hover)
        .onHover { hover = $0 }
    }
}

struct CardProgress: View {
    @ObservedObject var music: MusicController
    var isActive: Bool = true

    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 30.0, paused: !(music.isPlaying && isActive))) { ctx in
            let pos = music.currentPosition(at: ctx.date)
            let total = max(music.duration, 0.001)
            VStack(spacing: 3) {
                HStack {
                    Text(Self.format(pos))
                    Spacer()
                    Text("-" + Self.format(max(0, music.duration - pos)))
                }
                .font(.system(size: 9, weight: .semibold).monospacedDigit())
                .foregroundStyle(Color.white.opacity(0.8))

                GeometryReader { geo in
                    ZStack(alignment: .leading) {
                        Capsule()
                            .fill(Color.white.opacity(0.25))
                            .frame(height: 3)
                        Capsule()
                            .fill(Color.white)
                            .frame(width: max(3, geo.size.width * CGFloat(pos / total)), height: 3)
                    }
                    .frame(width: geo.size.width, height: geo.size.height)
                    .contentShape(Rectangle())
                    .gesture(
                        DragGesture(minimumDistance: 0)
                            .onEnded { value in
                                music.seek(toFraction: Double(value.location.x / max(geo.size.width, 1)))
                            }
                    )
                }
                .frame(height: 8)
            }
            .padding(.horizontal, 4)
        }
    }

    static func format(_ seconds: Double) -> String {
        guard seconds.isFinite, seconds >= 0 else { return "0:00" }
        let t = Int(seconds.rounded(.down))
        return String(format: "%d:%02d", t / 60, t % 60)
    }
}
