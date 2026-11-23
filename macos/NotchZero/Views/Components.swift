import SwiftUI
import AppKit

struct VisualEffectBlur: NSViewRepresentable {
    var material: NSVisualEffectView.Material

    func makeNSView(context: Context) -> NSVisualEffectView {
        let v = NSVisualEffectView()
        v.material = material
        v.blendingMode = .behindWindow
        v.state = .active
        return v
    }

    func updateNSView(_ nsView: NSVisualEffectView, context: Context) {
        nsView.material = material
    }
}

struct IconButton: View {
    let systemName: String
    var size: CGFloat = 13
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: size, weight: .semibold))
                .frame(width: 26, height: 26)
                .background(Circle().fill(Color.white.opacity(hover ? 0.16 : 0)))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}

struct ControlButton: View {
    let systemName: String
    var size: CGFloat = 15
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: size, weight: .semibold))
                .contentTransition(.symbolEffect(.replace))
                .frame(width: size + 16, height: size + 12)
                .background(Capsule().fill(Color.white.opacity(hover ? 0.14 : 0)))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .scaleEffect(hover ? 1.06 : 1)
        .animation(.easeOut(duration: 0.15), value: hover)
        .onHover { hover = $0 }
    }
}

struct PillButton: View {
    let title: String
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(Color.white)
                .padding(.horizontal, 11)
                .padding(.vertical, 5)
                .background(Capsule().fill(Color.white.opacity(hover ? 0.22 : 0.12)))
                .overlay(Capsule().stroke(Color.white.opacity(0.25), lineWidth: 0.5))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}

struct PanelDivider: View {
    var body: some View {
        Rectangle()
            .fill(Color.white.opacity(0.12))
            .frame(width: 1)
            .padding(.vertical, 8)
            .padding(.horizontal, 14)
    }
}

struct CenteredNote: View {
    let icon: String
    let text: String
    var action: (() -> Void)? = nil

    var body: some View {
        let content = VStack(spacing: 5) {
            Image(systemName: icon)
                .font(.system(size: 18, weight: .regular))
                .foregroundStyle(Color.white.opacity(0.5))
            Text(text)
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(Color.white.opacity(0.6))
                .multilineTextAlignment(.center)
                .lineLimit(2)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)

        if let action {
            Button(action: action) {
                content.contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        } else {
            content
        }
    }
}
