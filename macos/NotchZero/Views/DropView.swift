import SwiftUI
import UniformTypeIdentifiers

/// Shown while a file is being dragged onto the notch. Drop → AirDrop window with nearby people.
struct DropNotchView: View {
    @ObservedObject var vm: NotchViewModel

    var body: some View {
        VStack(spacing: 0) {
            Color.clear
                .frame(height: vm.metrics.notchHeight)

            HStack(spacing: 14) {
                ZStack {
                    Circle()
                        .fill(Color.white.opacity(vm.dropTargeted ? 1 : 0.12))
                    Image(systemName: "antenna.radiowaves.left.and.right")
                        .font(.system(size: 19, weight: .semibold))
                        .foregroundStyle(vm.dropTargeted ? Color.black : Color.white)
                }
                .frame(width: 48, height: 48)
                .scaleEffect(vm.dropTargeted ? 1.08 : 1)

                VStack(alignment: .leading, spacing: 3) {
                    Text(vm.dropTargeted ? "Release to AirDrop" : "Drop Files to AirDrop")
                        .font(.system(size: 15, weight: .semibold))
                        .foregroundStyle(Color.white)
                    Text("Then choose a nearby device.")
                        .font(.system(size: 11.5))
                        .foregroundStyle(Color.white.opacity(0.5))
                }
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 18)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .fill(Color.white.opacity(vm.dropTargeted ? 0.10 : 0.04))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .strokeBorder(Color.white.opacity(vm.dropTargeted ? 0.55 : 0.22),
                                  style: StrokeStyle(lineWidth: 1.2, dash: [6, 5]))
            )
            .padding(.top, 6)
            .padding(.bottom, 16)
            .padding(.horizontal, vm.topRadius(for: .drop) + 12)
            .animation(.spring(response: 0.3, dampingFraction: 0.7), value: vm.dropTargeted)
        }
        .contentShape(Rectangle())
        .onDrop(of: [UTType.fileURL], isTargeted: $vm.dropTargeted) { providers in
            DropLoader.loadURLs(from: providers) { urls in
                vm.onDropURLs(urls)
            }
            return true
        }
    }
}
