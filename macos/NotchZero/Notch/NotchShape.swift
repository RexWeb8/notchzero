import SwiftUI

/// The notch outline: small concave "ears" where it meets the top of the screen,
/// and rounded corners at the bottom.
struct NotchShape: Shape {
    var topRadius: CGFloat
    var bottomRadius: CGFloat

    var animatableData: AnimatablePair<CGFloat, CGFloat> {
        get { AnimatablePair(topRadius, bottomRadius) }
        set {
            topRadius = newValue.first
            bottomRadius = newValue.second
        }
    }

    func path(in rect: CGRect) -> Path {
        let t = min(topRadius, rect.width / 4)
        let b = min(bottomRadius, (rect.width - 2 * t) / 2, rect.height - t)

        var p = Path()
        p.move(to: CGPoint(x: rect.minX, y: rect.minY))
        // left ear
        p.addQuadCurve(to: CGPoint(x: rect.minX + t, y: rect.minY + t),
                       control: CGPoint(x: rect.minX + t, y: rect.minY))
        // left side
        p.addLine(to: CGPoint(x: rect.minX + t, y: rect.maxY - b))
        // bottom-left corner
        p.addCurve(to: CGPoint(x: rect.minX + t + b, y: rect.maxY),
                   control1: CGPoint(x: rect.minX + t, y: rect.maxY - b * 0.45),
                   control2: CGPoint(x: rect.minX + t + b * 0.45, y: rect.maxY))
        // bottom
        p.addLine(to: CGPoint(x: rect.maxX - t - b, y: rect.maxY))
        // bottom-right corner
        p.addCurve(to: CGPoint(x: rect.maxX - t, y: rect.maxY - b),
                   control1: CGPoint(x: rect.maxX - t - b * 0.45, y: rect.maxY),
                   control2: CGPoint(x: rect.maxX - t, y: rect.maxY - b * 0.45))
        // right side
        p.addLine(to: CGPoint(x: rect.maxX - t, y: rect.minY + t))
        // right ear
        p.addQuadCurve(to: CGPoint(x: rect.maxX, y: rect.minY),
                       control: CGPoint(x: rect.maxX - t, y: rect.minY))
        p.closeSubpath()
        return p
    }
}
