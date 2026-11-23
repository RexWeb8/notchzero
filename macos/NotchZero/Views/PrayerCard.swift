import SwiftUI

/// Where things sit on the sky arc. Fraction 0 = sunrise (left horizon), 1 = sunset (right horizon).
/// Below 0 or above 1 the body drops under the horizon, so it rises / sets out of view.
struct SkyGeometry {
    var x0: CGFloat
    var x1: CGFloat
    var horizon: CGFloat
    var height: CGFloat

    func point(at f: Double) -> CGPoint {
        let sink: CGFloat = 320
        if f < 0 { return CGPoint(x: x0, y: horizon + CGFloat(-f) * sink) }
        if f > 1 { return CGPoint(x: x1, y: horizon + CGFloat(f - 1) * sink) }
        return CGPoint(x: x0 + (x1 - x0) * CGFloat(f),
                       y: horizon - CGFloat(sin(f * Double.pi)) * height)
    }
}

/// The sun's (or moon's) path across the sky.
struct SkyArc: Shape {
    var geometry: SkyGeometry

    func path(in rect: CGRect) -> Path {
        var p = Path()
        let steps = 64
        for i in 0...steps {
            let pt = geometry.point(at: Double(i) / Double(steps))
            if i == 0 { p.move(to: pt) } else { p.addLine(to: pt) }
        }
        return p
    }
}

/// Moves a view along the sky arc; animatable, so the sun travels along the curve.
struct ArcPlacement: ViewModifier, Animatable {
    var fraction: Double
    var geometry: SkyGeometry

    var animatableData: Double {
        get { fraction }
        set { fraction = newValue }
    }

    func body(content: Content) -> some View {
        content.position(geometry.point(at: fraction))
    }
}

/// Big prayer card: next prayer, a live sky (sun by day, moon by night) and today's five times.
struct PrayerCard: View {
    @ObservedObject var prayer: PrayerController
    var kapasMood: KapasMood
    var isVisible: Bool

    @State private var shown: Double = 0

    private static let skyHeight: CGFloat = 112
    private static let horizon: CGFloat = 100

    private static let time: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US")
        f.timeZone = PrayerAPI.malaysia
        f.timeStyle = .short
        f.dateStyle = .none
        return f
    }()

    private static let short: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US")
        f.timeZone = PrayerAPI.malaysia
        f.dateFormat = "h:mm"
        return f
    }()

    /// Where the sun (day) or moon (night) should be right now, and which it is.
    private var sky: (fraction: Double, isNight: Bool) {
        guard let today = prayer.today else { return (0.5, false) }
        let now = prayer.now
        let rise = today.sunrise
        let set = today.maghrib
        let dayLength = max(set.timeIntervalSince(rise), 1)
        let day = now.timeIntervalSince(rise) / dayLength
        if day >= -0.06 && day <= 1.06 { return (day, false) }

        let nightStart = day > 1 ? set : set.addingTimeInterval(-86_400)
        let nightEnd = day > 1 ? rise.addingTimeInterval(86_400) : rise
        let night = now.timeIntervalSince(nightStart) / max(nightEnd.timeIntervalSince(nightStart), 1)
        return (min(max(night, 0), 1), true)
    }

    var body: some View {
        let target = sky
        VStack(spacing: 0) {
            GeometryReader { geo in
                let g = SkyGeometry(x0: 162, x1: geo.size.width - 24, horizon: Self.horizon, height: 72)
                ZStack(alignment: .topLeading) {
                    skyLayer(g, width: geo.size.width, isNight: target.isNight)
                    headline
                        .padding(.leading, 16)
                        .padding(.top, 14)
                    Text(prayer.zoneName)
                        .font(.system(size: 9.5, weight: .medium))
                        .foregroundStyle(Color.white.opacity(0.4))
                        .frame(maxWidth: .infinity, alignment: .trailing)
                        .padding(.trailing, 16)
                        .padding(.top, 14)
                    MascotView(mood: kapasMood, size: 24, mat: kapasMood == .praying)
                        .position(x: 136, y: Self.horizon - (kapasMood == .praying ? 13 : 12))
                }
            }
            .frame(height: Self.skyHeight)

            timesRow
                .frame(maxHeight: .infinity)
        }
        .background(RoundedRectangle(cornerRadius: 22, style: .continuous).fill(Color.white.opacity(0.05)))
        .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).strokeBorder(Color.white.opacity(0.08), lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
        .onAppear { shown = target.fraction }
        .onChange(of: isVisible) { _, visible in
            if visible { sweepIn(to: target) }
        }
        .onChange(of: target.fraction) { _, newValue in
            withAnimation(.linear(duration: 0.6)) { shown = newValue }
        }
    }

    /// When the panel opens, the sun (or moon) rises from the horizon and travels to where it is now.
    private func sweepIn(to target: (fraction: Double, isNight: Bool)) {
        var reset = Transaction()
        reset.disablesAnimations = true
        withTransaction(reset) { shown = target.isNight ? 0 : -0.06 }
        DispatchQueue.main.async {
            withAnimation(.timingCurve(0.25, 0.1, 0.25, 1, duration: 1.6)) {
                shown = target.fraction
            }
        }
    }

    // MARK: Sky

    @ViewBuilder
    private func skyLayer(_ g: SkyGeometry, width: CGFloat, isNight: Bool) -> some View {
        ZStack(alignment: .topLeading) {
            if isNight {
                ForEach(0..<Self.stars.count, id: \.self) { i in
                    let star = Self.stars[i]
                    Circle()
                        .fill(Color.white.opacity(star.o))
                        .frame(width: star.r * 2, height: star.r * 2)
                        .position(x: g.x0 + (g.x1 - g.x0) * star.x, y: 18 + (Self.horizon - 34) * star.y)
                }
            }

            // Whole path, faint and dotted.
            SkyArc(geometry: g)
                .stroke(Color.white.opacity(0.16), style: StrokeStyle(lineWidth: 1, lineCap: .round, dash: [1.5, 4]))

            // The part already travelled today.
            SkyArc(geometry: g)
                .trim(from: 0, to: CGFloat(min(max(shown, 0), 1)))
                .stroke(Color.white.opacity(isNight ? 0.3 : 0.55), style: StrokeStyle(lineWidth: 1.5, lineCap: .round))

            // Dhuhr and Asr marked on the arc.
            if !isNight, let today = prayer.today {
                ForEach([PrayerName.dhuhr, .asr], id: \.self) { name in
                    let f = today.time(name).timeIntervalSince(today.sunrise) / max(today.maghrib.timeIntervalSince(today.sunrise), 1)
                    Circle()
                        .fill(Color.white.opacity(0.6))
                        .frame(width: 4, height: 4)
                        .position(g.point(at: f))
                }
            }

            // Sun or moon, hidden below the horizon so it really sets.
            Group {
                if isNight {
                    Image(systemName: "moon.fill")
                        .font(.system(size: 13, weight: .regular))
                        .foregroundStyle(Color.white)
                        .shadow(color: Color.white.opacity(0.6), radius: 6)
                } else {
                    ZStack {
                        Circle().fill(Color.white.opacity(0.10)).frame(width: 34, height: 34)
                        Circle().fill(Color.white.opacity(0.18)).frame(width: 22, height: 22)
                        Circle().fill(Color.white).frame(width: 13, height: 13)
                            .shadow(color: Color.white.opacity(0.9), radius: 8)
                    }
                }
            }
            .modifier(ArcPlacement(fraction: shown, geometry: g))
            .mask(
                Rectangle()
                    .frame(height: Self.horizon)
                    .frame(maxHeight: .infinity, alignment: .top)
            )

            // Horizon, with a soft glow where the sun touches it.
            Rectangle()
                .fill(Color.white.opacity(0.18))
                .frame(height: 1)
                .position(x: width / 2, y: Self.horizon)
            if !isNight {
                let near = max(0, 1 - min(abs(shown), abs(shown - 1)) / 0.12)
                Ellipse()
                    .fill(Color.white.opacity(0.22 * near))
                    .frame(width: 90, height: 14)
                    .blur(radius: 8)
                    .position(x: shown < 0.5 ? g.x0 : g.x1, y: Self.horizon)
            }
        }
        .frame(width: width, height: Self.skyHeight)
    }

    private static let stars: [(x: CGFloat, y: CGFloat, r: CGFloat, o: Double)] = [
        (0.05, 0.55, 0.8, 0.5), (0.18, 0.15, 1.0, 0.7), (0.32, 0.6, 0.7, 0.4), (0.47, 0.05, 0.9, 0.6),
        (0.58, 0.42, 0.7, 0.45), (0.71, 0.12, 1.1, 0.75), (0.84, 0.5, 0.8, 0.5), (0.95, 0.22, 0.9, 0.6)
    ]

    // MARK: Text

    @ViewBuilder
    private var headline: some View {
        let state = headlineState
        VStack(alignment: .leading, spacing: 1) {
            Text(state.label)
                .font(.system(size: 8.5, weight: .bold))
                .tracking(0.8)
                .foregroundStyle(Color.white.opacity(0.45))
            Text(state.title)
                .font(.system(size: 28, weight: .semibold, design: .rounded))
                .foregroundStyle(Color.white)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
            Text(state.detail)
                .font(.system(size: 11, weight: .medium).monospacedDigit())
                .foregroundStyle(Color.white.opacity(0.6))
                .lineLimit(1)
        }
        .frame(width: 140, alignment: .leading)
    }

    private var headlineState: (label: String, title: String, detail: String) {
        switch prayer.window {
        case .now(let e):
            return ("NOW", e.name.title, "Started \(Self.time.string(from: e.time))")
        default:
            if let next = prayer.nextEvent {
                return ("NEXT PRAYER", next.name.title,
                        "\(Self.time.string(from: next.time)) · \(prayer.countdownText(to: next))")
            }
            return ("PRAYER TIMES", prayer.isLoading ? "Loading…" : "Unavailable", prayer.zoneName)
        }
    }

    // MARK: Times row

    @ViewBuilder
    private var timesRow: some View {
        if let today = prayer.today {
            let next = prayer.nextEvent
            HStack(spacing: 4) {
                ForEach(PrayerName.allCases, id: \.self) { name in
                    let time = today.time(name)
                    let isNext = next.map { $0.name == name && abs($0.time.timeIntervalSince(time)) < 60 } ?? false
                    let isPast = time <= prayer.now && !isNext
                    VStack(spacing: 2) {
                        Text(name.title.uppercased())
                            .font(.system(size: 8.5, weight: .bold))
                            .tracking(0.5)
                        Text(Self.short.string(from: time))
                            .font(.system(size: 14, weight: .semibold, design: .rounded).monospacedDigit())
                    }
                    .foregroundStyle(Color.white.opacity(isNext ? 1 : (isPast ? 0.3 : 0.6)))
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 6)
                    .background(
                        RoundedRectangle(cornerRadius: 10, style: .continuous)
                            .fill(Color.white.opacity(isNext ? 0.12 : 0))
                    )
                }
            }
            .padding(.horizontal, 10)
            .padding(.bottom, 4)
        } else {
            Color.clear
        }
    }
}
