import AppKit
import SwiftUI
import QuartzCore

// MARK: - Dot sphere (music visualizer)

/// A rotating sphere of white dots that ripples while music plays.
/// Drawn on a CADisplayLink at up to 120 Hz (ProMotion).
final class DotBlobNSView: NSView {
    var isPlaying = false {
        didSet {
            guard isPlaying != oldValue else { return }
            idle = false
            updateFrameRate()
        }
    }

    private var link: CADisplayLink?
    private var lastTimestamp: CFTimeInterval = 0
    private var phase: Double = 0
    private var energy: Double = 0.12
    private var speed: Double = 0.25
    private var idle = false
    private let points: [SIMD3<Double>] = DotBlobNSView.fibonacciSphere(count: 160)

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layerContentsRedrawPolicy = .onSetNeedsDisplay
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    override var isFlipped: Bool { true }
    override var isOpaque: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window != nil {
            startLink()
        } else {
            stopLink()
        }
    }

    private func startLink() {
        guard link == nil else { return }
        let newLink = displayLink(target: self, selector: #selector(step(_:)))
        newLink.add(to: .main, forMode: .common)
        link = newLink
        lastTimestamp = 0
        updateFrameRate()
    }

    private func stopLink() {
        link?.invalidate()
        link = nil
    }

    private func updateFrameRate() {
        guard let link else { return }
        if idle {
            link.preferredFrameRateRange = CAFrameRateRange(minimum: 10, maximum: 30, preferred: 30)
        } else {
            link.preferredFrameRateRange = CAFrameRateRange(minimum: 60, maximum: 120, preferred: 120)
        }
    }

    @objc private func step(_ link: CADisplayLink) {
        let now = link.timestamp
        var dt = 1.0 / 120.0
        if lastTimestamp > 0 {
            dt = min(max(now - lastTimestamp, 0), 1.0 / 20.0)
        }
        lastTimestamp = now

        let targetEnergy = isPlaying ? 1.0 : 0.12
        let targetSpeed = isPlaying ? 1.0 : 0.25
        let k = 1 - exp(-dt * 2.2)
        energy += (targetEnergy - energy) * k
        speed += (targetSpeed - speed) * k
        phase += dt * speed

        let settled = !isPlaying && abs(energy - targetEnergy) < 0.003
        if settled != idle {
            idle = settled
            updateFrameRate()
        }
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let w = Double(bounds.width)
        let h = Double(bounds.height)
        guard w > 2, h > 2 else { return }

        let cx = w / 2
        let cy = h / 2
        let radius = min(w, h) * 0.5 / 1.32
        let t = phase
        let e = energy

        let ay = t * 0.7
        let ax = 0.42 + 0.12 * sin(t * 0.5)
        let cosY = cos(ay)
        let sinY = sin(ay)
        let cosX = cos(ax)
        let sinX = sin(ax)
        let pulse = 1 + 0.05 * e * sin(t * 4.2)
        let baseDot = max(0.5, radius * 0.045)

        ctx.setFillColor(NSColor.white.cgColor)
        for p in points {
            let w1: Double = 0.20 * sin(3.1 * p.x + 1.7 * t) * cos(2.3 * p.y - 1.1 * t)
            let w2: Double = 0.14 * sin(2.6 * p.z + 2.3 * t + 1.4 * p.y)
            let w3: Double = 0.08 * sin(4.7 * p.x + 3.9 * p.y + 2.9 * t)
            let r: Double = pulse * (1 + e * (w1 + w2 + w3) * 1.25)

            let x = p.x * r
            let y = p.y * r
            let z = p.z * r
            let x1 = x * cosY + z * sinY
            let z1 = -x * sinY + z * cosY
            let y2 = y * cosX - z1 * sinX
            let z2 = y * sinX + z1 * cosX

            let depth = min(max((z2 / 1.3 + 1) / 2, 0), 1)
            let alpha = 0.05 + 0.95 * pow(depth, 2.2)
            let dot = baseDot * (0.45 + 0.75 * depth)
            let sx = cx + x1 * radius
            let sy = cy - y2 * radius

            ctx.setAlpha(CGFloat(alpha))
            ctx.fillEllipse(in: CGRect(x: sx - dot, y: sy - dot, width: dot * 2, height: dot * 2))
        }
    }

    nonisolated private static func fibonacciSphere(count: Int) -> [SIMD3<Double>] {
        let golden = Double.pi * (3 - 5.0.squareRoot())
        return (0..<count).map { i in
            let y = 1 - 2 * (Double(i) + 0.5) / Double(count)
            let r = (1 - y * y).squareRoot()
            let phi = Double(i) * golden
            return SIMD3<Double>(cos(phi) * r, y, sin(phi) * r)
        }
    }
}

struct DotBlob: NSViewRepresentable {
    var isPlaying: Bool

    func makeNSView(context: Context) -> DotBlobNSView {
        let view = DotBlobNSView(frame: .zero)
        view.isPlaying = isPlaying
        return view
    }

    func updateNSView(_ nsView: DotBlobNSView, context: Context) {
        nsView.isPlaying = isPlaying
    }
}

// MARK: - Album orb with a silver ring

private enum SilverRing {
    private static func grey(_ v: CGFloat, _ a: CGFloat = 1) -> CGColor {
        // a touch cool, like polished aluminium
        CGColor(srgbRed: v * 0.96, green: v * 0.98, blue: v, alpha: a)
    }

    /// Chrome: uneven light/dark bands like reflections on polished metal.
    static let chrome: [(CGFloat, CGColor)] = [
        (0.00, grey(0.35)), (0.06, grey(0.85)), (0.10, grey(1.00)), (0.14, grey(0.70)),
        (0.22, grey(0.30)), (0.30, grey(0.22)), (0.38, grey(0.55)), (0.45, grey(0.92)),
        (0.48, grey(1.00)), (0.52, grey(0.62)), (0.62, grey(0.28)), (0.72, grey(0.40)),
        (0.80, grey(0.80)), (0.84, grey(0.95)), (0.88, grey(0.60)), (0.95, grey(0.30)),
        (1.00, grey(0.35))
    ]

    /// Moving glints that sweep the other way, so the light ripples across the metal.
    static let shine: [(CGFloat, CGColor)] = [
        (0.00, grey(1, 0)), (0.05, grey(1, 0)), (0.09, grey(1, 0.95)), (0.12, grey(1, 0)),
        (0.40, grey(1, 0)), (0.46, grey(1, 0.6)), (0.50, grey(1, 0)),
        (0.70, grey(1, 0)), (0.74, grey(1, 0.8)), (0.77, grey(1, 0)),
        (1.00, grey(1, 0))
    ]
}

/// A polished silver ring: a chrome band and counter-rotating glints, spun by Core Animation
/// (runs at the display's full 120 Hz without touching the main thread), with a fixed
/// top-left light and bevelled edges for depth.
final class GlowRingNSView: NSView {
    var lineWidth: CGFloat = 2 {
        didSet { if lineWidth != oldValue { needsLayout = true } }
    }
    var isSpinning = false {
        didSet { if isSpinning != oldValue { updateSpin() } }
    }

    private let baseSpin = CALayer()
    private let shineSpin = CALayer()
    private let chrome = CAGradientLayer()
    private let shine = CAGradientLayer()
    private let shineGlow = CAGradientLayer()
    private let bevel = CAGradientLayer()
    private let chromeMask = CAShapeLayer()
    private let shineMask = CAShapeLayer()
    private let bevelMask = CAShapeLayer()
    private let glowMask = CAGradientLayer()
    private let innerEdge = CAShapeLayer()
    private let outerEdge = CAShapeLayer()

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.masksToBounds = false

        for g in [chrome, shine, shineGlow] {
            g.type = .conic
            g.startPoint = CGPoint(x: 0.5, y: 0.5)
            g.endPoint = CGPoint(x: 0.5, y: 0)
        }
        Self.apply(SilverRing.chrome, to: chrome)
        Self.apply(SilverRing.shine, to: shine)
        Self.apply(SilverRing.shine, to: shineGlow)
        shineGlow.opacity = 0.35

        glowMask.type = .radial
        glowMask.startPoint = CGPoint(x: 0.5, y: 0.5)
        glowMask.endPoint = CGPoint(x: 1, y: 1)

        // Fixed light from the top-left: brighter there, darker bottom-right.
        bevel.type = .axial
        bevel.startPoint = CGPoint(x: 0.15, y: 0.95)
        bevel.endPoint = CGPoint(x: 0.85, y: 0.05)
        bevel.colors = [
            NSColor.white.withAlphaComponent(0.55).cgColor,
            NSColor.white.withAlphaComponent(0).cgColor,
            NSColor.black.withAlphaComponent(0).cgColor,
            NSColor.black.withAlphaComponent(0.5).cgColor
        ]
        bevel.locations = [0, 0.45, 0.55, 1]

        for m in [chromeMask, shineMask, bevelMask] {
            m.fillColor = nil
            m.strokeColor = NSColor.black.cgColor
        }
        chrome.mask = chromeMask
        shine.mask = shineMask
        bevel.mask = bevelMask
        shineGlow.mask = glowMask

        innerEdge.fillColor = nil
        innerEdge.strokeColor = NSColor.black.withAlphaComponent(0.5).cgColor
        outerEdge.fillColor = nil
        outerEdge.strokeColor = NSColor.white.withAlphaComponent(0.22).cgColor

        baseSpin.addSublayer(chrome)
        shineSpin.addSublayer(shineGlow)
        shineSpin.addSublayer(shine)
        layer?.addSublayer(baseSpin)
        layer?.addSublayer(shineSpin)
        layer?.addSublayer(bevel)
        layer?.addSublayer(innerEdge)
        layer?.addSublayer(outerEdge)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    private static func apply(_ stops: [(CGFloat, CGColor)], to layer: CAGradientLayer) {
        layer.colors = stops.map { $0.1 }
        layer.locations = stops.map { NSNumber(value: Double($0.0)) }
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        updateScale()
        needsLayout = true
        if window != nil {
            updateSpin()
        }
    }

    override func viewDidChangeBackingProperties() {
        super.viewDidChangeBackingProperties()
        updateScale()
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        needsLayout = true
    }

    private func updateScale() {
        let scale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        let layers: [CALayer] = [baseSpin, shineSpin, chrome, shine, shineGlow, bevel,
                                 chromeMask, shineMask, bevelMask, glowMask, innerEdge, outerEdge]
        for l in layers { l.contentsScale = scale }
    }

    override func layout() {
        super.layout()
        let b = bounds
        guard b.width > 0, b.height > 0 else { return }

        let size = min(b.width, b.height)
        let pad = lineWidth * 6
        let full = size + pad * 2
        let local = CGRect(x: 0, y: 0, width: full, height: full)
        let c = CGPoint(x: full / 2, y: full / 2)
        let r = size / 2 - lineWidth / 2

        func circle(_ radius: CGFloat) -> CGPath {
            CGPath(ellipseIn: CGRect(x: c.x - radius, y: c.y - radius, width: radius * 2, height: radius * 2),
                   transform: nil)
        }

        CATransaction.begin()
        CATransaction.setDisableActions(true)

        for spin in [baseSpin, shineSpin] {
            spin.bounds = local
            spin.position = CGPoint(x: b.midX, y: b.midY)
        }
        let originInView = CGPoint(x: b.midX - full / 2, y: b.midY - full / 2)
        bevel.frame = CGRect(origin: originInView, size: local.size)
        innerEdge.frame = bevel.frame
        outerEdge.frame = bevel.frame
        for l in [chrome, shine, shineGlow, chromeMask, shineMask, bevelMask, glowMask] as [CALayer] {
            l.frame = local
        }

        let ringPath = circle(r)
        for m in [chromeMask, shineMask, bevelMask] {
            m.path = ringPath
            m.lineWidth = lineWidth
        }
        innerEdge.path = circle(r - lineWidth / 2)
        innerEdge.lineWidth = 0.6
        outerEdge.path = circle(r + lineWidth / 2)
        outerEdge.lineWidth = 0.5

        // Soft bloom around the glints.
        let outer = full / 2
        let band = lineWidth * 2
        let distances: [CGFloat] = [0, r - band, r - lineWidth * 0.4, r, r + lineWidth * 0.8, r + band, outer]
        let alphas: [CGFloat] = [0, 0, 0.4, 0.8, 0.4, 0, 0]
        var last: CGFloat = 0
        glowMask.locations = distances.map { d -> NSNumber in
            let v = max(last, min(max(d / outer, 0), 1))
            last = v
            return NSNumber(value: Double(v))
        }
        glowMask.colors = alphas.map { NSColor.black.withAlphaComponent($0).cgColor }

        CATransaction.commit()
    }

    // MARK: Spin

    private func updateSpin() {
        addSpinIfNeeded(to: baseSpin, duration: 9, clockwise: true)
        addSpinIfNeeded(to: shineSpin, duration: 6, clockwise: false)
        for spin in [baseSpin, shineSpin] {
            if isSpinning {
                Self.resume(spin)
            } else {
                Self.pause(spin)
            }
        }
    }

    private func addSpinIfNeeded(to layer: CALayer, duration: CFTimeInterval, clockwise: Bool) {
        guard layer.animation(forKey: "spin") == nil else { return }
        let spin = CABasicAnimation(keyPath: "transform.rotation.z")
        spin.fromValue = 0.0
        spin.toValue = (clockwise ? -2.0 : 2.0) * Double.pi
        spin.duration = duration
        spin.repeatCount = .infinity
        spin.isRemovedOnCompletion = false
        spin.preferredFrameRateRange = CAFrameRateRange(minimum: 80, maximum: 120, preferred: 120)
        layer.add(spin, forKey: "spin")
    }

    private static func pause(_ layer: CALayer) {
        guard layer.speed != 0 else { return }
        let t = layer.convertTime(CACurrentMediaTime(), from: nil)
        layer.speed = 0
        layer.timeOffset = t
    }

    private static func resume(_ layer: CALayer) {
        guard layer.speed == 0 else { return }
        let paused = layer.timeOffset
        layer.speed = 1
        layer.timeOffset = 0
        layer.beginTime = 0
        let since = layer.convertTime(CACurrentMediaTime(), from: nil) - paused
        layer.beginTime = since
    }
}

struct GlowRing: NSViewRepresentable {
    var lineWidth: CGFloat
    var isSpinning: Bool

    func makeNSView(context: Context) -> GlowRingNSView {
        let view = GlowRingNSView(frame: .zero)
        view.lineWidth = lineWidth
        view.isSpinning = isSpinning
        return view
    }

    func updateNSView(_ nsView: GlowRingNSView, context: Context) {
        nsView.lineWidth = lineWidth
        nsView.isSpinning = isSpinning
    }
}

/// Round album artwork inside a dark gap and a polished silver ring.
struct AlbumOrb: View {
    let image: NSImage?
    var size: CGFloat
    var isSpinning: Bool

    var body: some View {
        let ring = max(1.3, size * 0.045)
        let inset = ring * (size < 40 ? 1.6 : 1.9)
        let artSize = size - inset * 2

        ZStack {
            Circle()
                .fill(Color.black.opacity(0.55))
            GlowRing(lineWidth: ring, isSpinning: isSpinning)

            Group {
                if let image {
                    Image(nsImage: image)
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                } else {
                    ZStack {
                        Color.white.opacity(0.08)
                        Image(systemName: "music.note")
                            .font(.system(size: artSize * 0.36, weight: .medium))
                            .foregroundStyle(Color.white.opacity(0.55))
                    }
                }
            }
            .frame(width: artSize, height: artSize)
            .clipShape(Circle())
            .overlay(Circle().strokeBorder(Color.white.opacity(0.10), lineWidth: 0.5))
        }
        .frame(width: size, height: size)
    }
}

// MARK: - Equalizer bars (closed notch, music playing)

/// Four white bars that bounce smoothly while music plays and settle into dots when paused.
/// Drawn on a CADisplayLink at up to 120 Hz.
final class EqualizerNSView: NSView {
    var isPlaying = false {
        didSet {
            guard isPlaying != oldValue else { return }
            idle = false
            updateFrameRate()
        }
    }

    private let count = 4
    private var heights: [Double] = [0.2, 0.2, 0.2, 0.2]
    private var velocities: [Double] = [0, 0, 0, 0]
    private var targets: [Double] = [0.2, 0.2, 0.2, 0.2]
    private var retargetIn: [Double] = [0, 0.05, 0.1, 0.15]
    private var link: CADisplayLink?
    private var lastTimestamp: CFTimeInterval = 0
    private var idle = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layerContentsRedrawPolicy = .onSetNeedsDisplay
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    override var isFlipped: Bool { true }
    override var isOpaque: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window != nil {
            if link == nil {
                let newLink = displayLink(target: self, selector: #selector(step(_:)))
                newLink.add(to: .main, forMode: .common)
                link = newLink
                lastTimestamp = 0
                updateFrameRate()
            }
        } else {
            link?.invalidate()
            link = nil
        }
    }

    private func updateFrameRate() {
        guard let link else { return }
        if idle {
            link.isPaused = true
        } else {
            link.isPaused = false
            link.preferredFrameRateRange = CAFrameRateRange(minimum: 60, maximum: 120, preferred: 120)
        }
    }

    @objc private func step(_ link: CADisplayLink) {
        let now = link.timestamp
        var dt = 1.0 / 120.0
        if lastTimestamp > 0 { dt = min(max(now - lastTimestamp, 0), 1.0 / 20.0) }
        lastTimestamp = now

        var settled = true
        for i in 0..<count {
            if isPlaying {
                retargetIn[i] -= dt
                if retargetIn[i] <= 0 {
                    // New level for this bar; outer bars a little calmer than the middle ones.
                    let middle = (i == 1 || i == 2)
                    targets[i] = Double.random(in: middle ? 0.35...1.0 : 0.25...0.8)
                    retargetIn[i] = Double.random(in: 0.11...0.22)
                }
            } else {
                targets[i] = 0.18
            }
            // Critically damped spring towards the target: smooth, no jitter.
            let stiffness = 170.0
            let damping = 2 * stiffness.squareRoot()
            let accel = stiffness * (targets[i] - heights[i]) - damping * velocities[i]
            velocities[i] += accel * dt
            heights[i] += velocities[i] * dt
            if abs(heights[i] - targets[i]) > 0.002 || abs(velocities[i]) > 0.002 { settled = false }
        }

        if !isPlaying && settled && !idle {
            idle = true
            needsDisplay = true
            updateFrameRate()
            return
        }
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let w = bounds.width
        let h = bounds.height
        let barWidth = max(2, w / CGFloat(count * 2 - 1) * 1.0)
        let gap = (w - barWidth * CGFloat(count)) / CGFloat(count - 1)
        ctx.setFillColor(NSColor.white.cgColor)
        for i in 0..<count {
            let bh = max(barWidth, h * CGFloat(min(max(heights[i], 0), 1)))
            let x = CGFloat(i) * (barWidth + gap)
            let y = (h - bh) / 2
            let rect = CGRect(x: x, y: y, width: barWidth, height: bh)
            ctx.addPath(CGPath(roundedRect: rect, cornerWidth: barWidth / 2, cornerHeight: barWidth / 2, transform: nil))
        }
        ctx.fillPath()
    }
}

struct EqualizerBars: NSViewRepresentable {
    var isPlaying: Bool

    func makeNSView(context: Context) -> EqualizerNSView {
        let view = EqualizerNSView(frame: .zero)
        view.isPlaying = isPlaying
        return view
    }

    func updateNSView(_ nsView: EqualizerNSView, context: Context) {
        nsView.isPlaying = isPlaying
        if isPlaying { nsView.needsDisplay = true }
    }
}
