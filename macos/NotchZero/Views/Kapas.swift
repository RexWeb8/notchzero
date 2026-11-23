import SwiftUI

enum KapasMood: Equatable {
    case awake      // blinking now and then
    case alert      // a prayer is coming up
    case sleeping   // you're away, or it's late
    case praying    // it's prayer time: sits on the sejadah
    case vibing     // music is playing: dancing
}

/// Which mascot lives in the notch.
enum MascotKind: String, CaseIterable, Identifiable {
    case cat
    case rabbit
    case dragon
    case owl
    case mouseDeer

    var id: String { rawValue }

    var name: String {
        switch self {
        case .cat: return "Kapas"
        case .rabbit: return "Gebu"
        case .dragon: return "Bara"
        case .owl: return "Pungguk"
        case .mouseDeer: return "Cil"
        }
    }

    var species: String {
        switch self {
        case .cat: return "Cat"
        case .rabbit: return "Rabbit"
        case .dragon: return "Dragon"
        case .owl: return "Owl"
        case .mouseDeer: return "Mouse Deer"
        }
    }

    var menuTitle: String { "\(name) the \(species)" }
}

private struct MascotKindKey: EnvironmentKey {
    static let defaultValue: MascotKind = .cat
}

extension EnvironmentValues {
    var mascotKind: MascotKind {
        get { self[MascotKindKey.self] }
        set { self[MascotKindKey.self] = newValue }
    }
}

/// A mascot's face, drawn in a 100-unit design space (108 tall with the sejadah).
struct MascotFace: View {
    var kind: MascotKind
    var mood: KapasMood
    var eyeOpen: CGFloat
    var detailed: Bool
    var mat: Bool
    /// Degrees the ears (cat, rabbit) or side fins (dragon) flick outwards while dancing.
    var earWiggle: Double = 0

    var body: some View {
        Canvas { ctx, size in
            let k = size.width / 100
            func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x * k, y: y * k) }
            func r(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> CGRect {
                CGRect(x: x * k, y: y * k, width: w * k, height: h * k)
            }
            func polygon(_ pts: [(CGFloat, CGFloat)]) -> Path {
                var path = Path()
                for (i, pt) in pts.enumerated() {
                    if i == 0 { path.move(to: p(pt.0, pt.1)) } else { path.addLine(to: p(pt.0, pt.1)) }
                }
                path.closeSubpath()
                return path
            }
            /// A copy of the context rotated around a point in design units.
            func rotated(_ degrees: Double, around x: CGFloat, _ y: CGFloat,
                         from base: GraphicsContext? = nil) -> GraphicsContext {
                var c = base ?? ctx
                let pivot = p(x, y)
                c.translateBy(x: pivot.x, y: pivot.y)
                c.rotate(by: .degrees(degrees))
                c.translateBy(x: -pivot.x, y: -pivot.y)
                return c
            }

            let white = GraphicsContext.Shading.color(.white)
            let ink = GraphicsContext.Shading.color(.black)
            let soft = GraphicsContext.Shading.color(Color(white: 0.84))

            // Sejadah
            if mat {
                let matColor = GraphicsContext.Shading.color(Color(white: 0.36))
                ctx.fill(Path(roundedRect: r(4, 89, 92, 10), cornerRadius: 3 * k), with: matColor)
                var fringe = Path()
                for x in [7.0, 11.0, 89.0, 93.0] as [CGFloat] {
                    fringe.move(to: p(x, 99))
                    fringe.addLine(to: p(x, 106))
                }
                ctx.stroke(fringe, with: matColor, style: StrokeStyle(lineWidth: max(0.8, 1.4 * k), lineCap: .round))
            }

            func drawEyes(cy: CGFloat, left: CGFloat, right: CGFloat, scale sc: CGFloat = 1) {
                switch mood {
                case .sleeping, .praying:
                    for cx in [left, right] {
                        var lid = Path()
                        lid.move(to: p(cx - 6 * sc, cy))
                        lid.addQuadCurve(to: p(cx + 6 * sc, cy), control: p(cx, cy + 6 * sc))
                        ctx.stroke(lid, with: ink, style: StrokeStyle(lineWidth: max(1, 2.4 * k), lineCap: .round))
                    }
                case .vibing:
                    for cx in [left, right] {
                        var lid = Path()
                        lid.move(to: p(cx - 6 * sc, cy + 3 * sc))
                        lid.addQuadCurve(to: p(cx + 6 * sc, cy + 3 * sc), control: p(cx, cy - 6 * sc))
                        ctx.stroke(lid, with: ink, style: StrokeStyle(lineWidth: max(1, 2.6 * k), lineCap: .round))
                    }
                case .awake, .alert:
                    let base: CGFloat = mood == .alert ? 7.5 : 6.5
                    let rx = 5.5 * sc
                    let ry = base * sc * max(eyeOpen, 0.12)
                    for cx in [left, right] {
                        ctx.fill(Path(ellipseIn: r(cx - rx, cy - ry, rx * 2, ry * 2)), with: ink)
                        if detailed && eyeOpen > 0.5 {
                            ctx.fill(Path(ellipseIn: r(cx - rx * 0.58, cy - ry * 0.75, 2.8 * sc, 2.8 * sc)), with: white)
                        }
                    }
                }
            }

            func drawSmile(at y: CGFloat) {
                guard detailed else { return }
                var mouth = Path()
                mouth.move(to: p(44, y)); mouth.addQuadCurve(to: p(50, y), control: p(47, y + 4))
                mouth.move(to: p(50, y)); mouth.addQuadCurve(to: p(56, y), control: p(53, y + 4))
                ctx.stroke(mouth, with: ink, style: StrokeStyle(lineWidth: max(0.8, 1.6 * k), lineCap: .round))
            }

            switch kind {
            case .cat:
                let leftEar = polygon([(16, 54), (21, 12), (47, 35)])
                let rightEar = polygon([(53, 35), (79, 12), (84, 54)])
                let lctx = rotated(-earWiggle, around: 30, 46)
                let rctx = rotated(earWiggle, around: 70, 46)
                lctx.fill(leftEar, with: white)
                rctx.fill(rightEar, with: white)
                if detailed {
                    lctx.fill(polygon([(22, 44), (25, 22), (39, 36)]), with: soft)
                    rctx.fill(polygon([(61, 36), (75, 22), (78, 44)]), with: soft)
                }
                ctx.fill(Path(roundedRect: r(14, 32, 72, 60), cornerRadius: 28 * k, style: .continuous), with: white)
                drawEyes(cy: 62, left: 37, right: 63)
                ctx.fill(polygon([(46.5, 71), (53.5, 71), (50, 75)]), with: ink)
                drawSmile(at: 75)

            case .rabbit:
                // Long ears: upright when alert, droopy when asleep.
                let rest: Double = mood == .sleeping ? 40 : (mood == .alert ? 2 : 8)
                let tilt = rest + earWiggle
                let lctx = rotated(-tilt, around: 35, 48)
                let rctx = rotated(tilt, around: 65, 48)
                lctx.fill(Path(roundedRect: r(28, 4, 14, 48), cornerRadius: 7 * k), with: white)
                rctx.fill(Path(roundedRect: r(58, 4, 14, 48), cornerRadius: 7 * k), with: white)
                if detailed {
                    lctx.fill(Path(roundedRect: r(31.5, 10, 7, 34), cornerRadius: 3.5 * k), with: soft)
                    rctx.fill(Path(roundedRect: r(61.5, 10, 7, 34), cornerRadius: 3.5 * k), with: soft)
                }
                ctx.fill(Path(roundedRect: r(18, 40, 64, 54), cornerRadius: 27 * k, style: .continuous), with: white)
                drawEyes(cy: 66, left: 39, right: 61)
                ctx.fill(polygon([(47, 74), (53, 74), (50, 77.5)]), with: ink)
                drawSmile(at: 79)

            case .dragon:
                // Side fins flap while dancing.
                let leftFin = polygon([(17, 46), (3, 40), (10, 52), (1, 58), (10, 63), (4, 72), (17, 70)])
                let rightFin = polygon([(83, 46), (97, 40), (90, 52), (99, 58), (90, 63), (96, 72), (83, 70)])
                rotated(-earWiggle, around: 17, 58).fill(leftFin, with: white)
                rotated(earWiggle, around: 83, 58).fill(rightFin, with: white)

                var leftHorn = Path()
                leftHorn.move(to: p(27, 38))
                leftHorn.addQuadCurve(to: p(23, 6), control: p(19, 24))
                leftHorn.addQuadCurve(to: p(41, 33), control: p(33, 18))
                leftHorn.closeSubpath()
                var rightHorn = Path()
                rightHorn.move(to: p(73, 38))
                rightHorn.addQuadCurve(to: p(77, 6), control: p(81, 24))
                rightHorn.addQuadCurve(to: p(59, 33), control: p(67, 18))
                rightHorn.closeSubpath()
                let hornShade: GraphicsContext.Shading = detailed ? .color(Color(white: 0.88)) : white
                ctx.fill(leftHorn, with: hornShade)
                ctx.fill(rightHorn, with: hornShade)
                ctx.fill(polygon([(44, 33), (50, 20), (56, 33)]), with: white)

                ctx.fill(Path(roundedRect: r(16, 30, 68, 62), cornerRadius: 26 * k, style: .continuous), with: white)
                drawEyes(cy: 60, left: 37, right: 63)
                for x in [45.0, 55.0] as [CGFloat] {
                    ctx.fill(Path(ellipseIn: r(x - 1.9, 73.1, 3.8, 3.8)), with: ink)
                }
                if detailed {
                    var mouth = Path()
                    mouth.move(to: p(41, 81))
                    mouth.addQuadCurve(to: p(59, 81), control: p(50, 88))
                    ctx.stroke(mouth, with: ink, style: StrokeStyle(lineWidth: max(0.8, 1.8 * k), lineCap: .round))
                }

            case .owl:
                // Feathery ear tufts flick while dancing.
                let leftTuft = polygon([(16, 44), (9, 8), (24, 22), (27, 12), (40, 30)])
                let rightTuft = polygon([(84, 44), (91, 8), (76, 22), (73, 12), (60, 30)])
                rotated(-earWiggle, around: 26, 36).fill(leftTuft, with: white)
                rotated(earWiggle, around: 74, 36).fill(rightTuft, with: white)
                ctx.fill(Path(roundedRect: r(14, 26, 72, 68), cornerRadius: 30 * k, style: .continuous), with: white)
                // Facial discs around the big eyes.
                let disc = GraphicsContext.Shading.color(Color(white: 0.82))
                ctx.fill(Path(ellipseIn: r(20, 43, 30, 30)), with: disc)
                ctx.fill(Path(ellipseIn: r(50, 43, 30, 30)), with: disc)
                drawEyes(cy: 58, left: 35, right: 65, scale: 1.3)
                ctx.fill(polygon([(45.5, 68), (54.5, 68), (50, 77)]), with: .color(Color(white: 0.2)))
                if detailed {
                    var chest = Path()
                    for x in [40.0, 50.0, 60.0] as [CGFloat] {
                        chest.move(to: p(x - 3, 84))
                        chest.addLine(to: p(x, 87))
                        chest.addLine(to: p(x + 3, 84))
                    }
                    ctx.stroke(chest, with: .color(Color(white: 0.75)),
                               style: StrokeStyle(lineWidth: max(0.8, 1.4 * k), lineCap: .round, lineJoin: .round))
                }

            case .mouseDeer:
                // Big side ears: perk up when alert, droop when asleep, flick while dancing.
                let droop: Double = mood == .sleeping ? 35 : (mood == .alert ? -8 : 0)
                let lctx = rotated(-38, around: 21, 38, from: rotated(-(droop + earWiggle), around: 32, 52))
                let rctx = rotated(38, around: 79, 38, from: rotated(droop + earWiggle, around: 68, 52))
                lctx.fill(Path(ellipseIn: r(12, 19, 18, 38)), with: white)
                rctx.fill(Path(ellipseIn: r(70, 19, 18, 38)), with: white)
                if detailed {
                    lctx.fill(Path(ellipseIn: r(16.5, 26, 9, 24)), with: soft)
                    rctx.fill(Path(ellipseIn: r(74.5, 26, 9, 24)), with: soft)
                }
                ctx.fill(Path(roundedRect: r(24, 30, 52, 54), cornerRadius: 24 * k, style: .continuous), with: white)
                ctx.fill(Path(ellipseIn: r(33, 62, 34, 32)), with: white)
                drawEyes(cy: 56, left: 39, right: 61)
                ctx.fill(Path(ellipseIn: r(45, 79, 10, 6)), with: ink)
                drawSmile(at: 86)
            }

            if detailed && mood == .sleeping {
                ctx.draw(Text("z")
                            .font(.system(size: 15 * k, weight: .bold, design: .rounded))
                            .foregroundColor(Color.white.opacity(0.7)),
                         at: p(94, 16))
            }
        }
    }
}

/// The current mascot with its little behaviours: blinks, dances to music, sleeps, prays.
struct MascotView: View {
    var mood: KapasMood
    var size: CGFloat
    var mat: Bool = false

    @Environment(\.mascotKind) private var kind
    @State private var eyeOpen: CGFloat = 1

    var body: some View {
        let height = size * (mat ? 1.08 : 1)
        TimelineView(.animation(minimumInterval: nil, paused: mood != .vibing)) { timeline in
            let dance = KapasDance(time: timeline.date.timeIntervalSinceReferenceDate, active: mood == .vibing)
            MascotFace(kind: kind, mood: mood, eyeOpen: eyeOpen, detailed: size >= 30, mat: mat,
                       earWiggle: dance.ears)
                .frame(width: size, height: height)
                .scaleEffect(x: dance.stretchX, y: dance.stretchY, anchor: .bottom)
                .rotationEffect(.degrees(dance.tilt), anchor: .bottom)
                .offset(x: CGFloat(dance.sway) * size, y: CGFloat(dance.lift) * -size)
        }
        .frame(width: size, height: height)
        .task(id: mood) {
            eyeOpen = 1
            guard mood == .awake || mood == .alert else { return }
            while !Task.isCancelled {
                let wait = UInt64.random(in: 2_500_000_000...5_500_000_000)
                try? await Task.sleep(nanoseconds: wait)
                if Task.isCancelled { break }
                eyeOpen = 0.1
                try? await Task.sleep(nanoseconds: 120_000_000)
                eyeOpen = 1
            }
        }
    }
}

/// The mascot's little dance, as a pure function of time (about 112 bpm):
/// sways side to side, bops on every beat, flicks its ears, and hops on every 8th beat.
struct KapasDance {
    var sway: Double = 0      // fraction of size, sideways
    var lift: Double = 0      // fraction of size, upwards
    var tilt: Double = 0      // degrees
    var ears: Double = 0      // degrees
    var stretchX: CGFloat = 1
    var stretchY: CGFloat = 1

    init(time: TimeInterval, active: Bool) {
        guard active else { return }
        let beats = time * 112 / 60
        let beat = beats.truncatingRemainder(dividingBy: 8)   // 0..<8
        let phase = beats.truncatingRemainder(dividingBy: 1) // 0..<1 within a beat

        // Side-to-side sway over two beats, head tilting with it.
        let swing = sin(beats * Double.pi)
        sway = swing * 0.05
        tilt = swing * 8

        // Bop: a small dip on each beat.
        let bop = pow(sin(phase * Double.pi), 2)
        lift = bop * 0.04

        // Ears flick on the off-beat.
        ears = max(0, sin(phase * 2 * Double.pi)) * 14

        // Squash a touch as each bop lands.
        let squash = pow(cos(phase * Double.pi), 8) * 0.06
        stretchX = CGFloat(1 + squash)
        stretchY = CGFloat(1 - squash)

        // Hop on beat 8: up and down over one beat, stretched in the air.
        if beat >= 7 {
            let hop = sin(phase * Double.pi)
            lift += hop * 0.2
            tilt *= 0.3
            stretchX = CGFloat(1 - hop * 0.05)
            stretchY = CGFloat(1 + hop * 0.07)
        }
    }
}
