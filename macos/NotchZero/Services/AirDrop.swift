import AppKit
import UniformTypeIdentifiers

enum AirDrop {
    /// Opens the system AirDrop window (shows nearby people) for the given files.
    @MainActor
    static func send(_ urls: [URL]) {
        guard !urls.isEmpty else { return }
        guard let service = NSSharingService(named: .sendViaAirDrop) else {
            NSSound.beep()
            return
        }
        NSApp.activate()
        if service.canPerform(withItems: urls) {
            service.perform(withItems: urls)
        } else {
            NSSound.beep()
        }
    }
}

enum DropLoader {
    /// Pulls file URLs out of a SwiftUI drop and hands them back on the main thread.
    static func loadURLs(from providers: [NSItemProvider], completion: @escaping @MainActor ([URL]) -> Void) {
        let group = DispatchGroup()
        let lock = NSLock()
        var urls: [URL] = []

        for provider in providers where provider.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) {
            group.enter()
            provider.loadItem(forTypeIdentifier: UTType.fileURL.identifier, options: nil) { item, _ in
                var url: URL?
                if let data = item as? Data {
                    url = URL(dataRepresentation: data, relativeTo: nil)
                } else if let u = item as? URL {
                    url = u
                } else if let s = item as? String {
                    url = URL(string: s)
                }
                if let url, url.isFileURL {
                    lock.lock()
                    urls.append(url)
                    lock.unlock()
                }
                group.leave()
            }
        }

        group.notify(queue: .main) {
            MainActor.assumeIsolated {
                completion(urls)
            }
        }
    }
}
