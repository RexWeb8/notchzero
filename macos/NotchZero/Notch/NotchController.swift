import AppKit
import SwiftUI
import Combine

/// Owns the notch panel and decides when it opens, closes, or switches to the AirDrop drop zone.
@MainActor
final class NotchController {
    let vm = NotchViewModel()
    let music = MusicController()
    let system = SystemMonitor()
    let prayer = PrayerController()

    private var panel: NotchPanel!
    private var monitors: [Any] = []
    private var pollTimer: Timer?
    private var closeWork: DispatchWorkItem?
    private var dragChangeCount = NSPasteboard(name: .drag).changeCount
    private var screenFrame: NSRect = .zero
    private var cancellables = Set<AnyCancellable>()
    private var alertWork: DispatchWorkItem?
    private var peekWork: DispatchWorkItem?
    private var pendingAlert = false

    // MARK: - Setup

    func start() {
        vm.onSettings = { [weak self] in
            DispatchQueue.main.async {
                MainActor.assumeIsolated { self?.showSettingsMenu() }
            }
        }
        vm.onPickAirDrop = { [weak self] in
            DispatchQueue.main.async {
                MainActor.assumeIsolated { self?.pickFilesForAirDrop() }
            }
        }
        vm.onDropURLs = { [weak self] urls in
            self?.close()
            AirDrop.send(urls)
        }
        vm.onDismissAlert = { [weak self] in
            self?.close()
        }
        prayer.onPrayerTime = { [weak self] event in
            self?.handlePrayerTime(event)
        }

        panel = NotchPanel(contentRect: NSRect(x: 0, y: 0, width: 10, height: 10),
                           styleMask: [.borderless, .nonactivatingPanel],
                           backing: .buffered,
                           defer: false)
        let root = NotchRootView(vm: vm, music: music, system: system, prayer: prayer)
        let hosting = NotchHostingView(rootView: root)
        hosting.sizingOptions = []
        panel.contentView = hosting

        reposition()
        panel.orderFrontRegardless()

        // Re-decide what the closed notch shows whenever music or prayer state changes.
        Publishers.Merge4(
            music.$isPlaying.map { _ in () },
            music.$source.map { _ in () },
            prayer.$now.map { _ in () },
            prayer.$days.map { _ in () }
        )
        .merge(with: vm.$showCompanion.map { _ in () })
        .receive(on: RunLoop.main)
        .sink { [weak self] _ in
            MainActor.assumeIsolated { self?.updateClosedMode() }
        }
        .store(in: &cancellables)

        // Song peek: when the track changes or playback starts.
        music.$trackID.removeDuplicates().dropFirst().map { _ in () }
            .merge(with: music.$isPlaying.removeDuplicates().dropFirst().filter { $0 }.map { _ in () })
            .debounce(for: .milliseconds(350), scheduler: RunLoop.main)
            .sink { [weak self] _ in
                MainActor.assumeIsolated { self?.showSongPeek() }
            }
            .store(in: &cancellables)

        _ = NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                                   object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.reposition() }
        }

        let mask: NSEvent.EventTypeMask = [.mouseMoved, .leftMouseDragged, .leftMouseDown, .leftMouseUp]
        if let global = NSEvent.addGlobalMonitorForEvents(matching: mask, handler: { [weak self] event in
            let type = event.type
            MainActor.assumeIsolated { self?.handle(type) }
        }) {
            monitors.append(global)
        }
        if let local = NSEvent.addLocalMonitorForEvents(matching: mask, handler: { [weak self] event in
            let type = event.type
            MainActor.assumeIsolated { self?.handle(type) }
            return event
        }) {
            monitors.append(local)
        }

        system.start()
        music.start()
        prayer.start()
        updateClosedMode()
    }

    // MARK: - Closed notch content and Kapas

    private func updateClosedMode() {
        let mode: ClosedMode
        switch prayer.window {
        case .soon, .now:
            mode = .prayer
        case .none:
            if music.isPlaying && music.source != nil {
                mode = .music
            } else if vm.showCompanion {
                mode = .companion
            } else {
                mode = .plain
            }
        }
        if vm.closedMode != mode { vm.closedMode = mode }

        let mood = currentMood()
        if vm.kapasMood != mood { vm.kapasMood = mood }
    }

    private func currentMood() -> KapasMood {
        switch prayer.window {
        case .now: return .praying
        case .soon: return .alert
        case .none: break
        }
        if music.isPlaying { return .vibing }
        let anyInput = CGEventType(rawValue: UInt32.max) ?? .mouseMoved
        let idle = CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: anyInput)
        if idle > 180 { return .sleeping }
        let hour = Calendar.current.component(.hour, from: Date())
        if hour < 5 { return .sleeping }
        return .awake
    }

    // MARK: - Prayer time

    private func handlePrayerTime(_ event: PrayerEvent) {
        vm.alertEvent = event
        music.pauseBriefly(seconds: 5)
        updateClosedMode()
        if vm.state == .closed || vm.state == .peek {
            peekWork?.cancel()
            showAlert()
        } else {
            pendingAlert = true // show it once you move away from the panel
        }
    }

    private func showSongPeek() {
        guard vm.showSongPeek, music.isPlaying, !music.title.isEmpty,
              vm.state == .closed || vm.state == .peek,
              case .none = prayer.window else { return }
        cancelClose()
        if vm.state != .peek {
            withAnimation(.spring(response: 0.42, dampingFraction: 0.8)) {
                vm.state = .peek
            }
        }
        startPolling()

        peekWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.vm.state == .peek else { return }
                self.close()
            }
        }
        peekWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 4, execute: work)
    }

    private func showAlert() {
        pendingAlert = false
        cancelClose()
        withAnimation(.spring(response: 0.45, dampingFraction: 0.78)) {
            vm.state = .alert
        }
        startPolling()

        alertWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.vm.state == .alert else { return }
                self.close()
            }
        }
        alertWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 30, execute: work)
    }

    private func reposition() {
        guard let screen = Self.targetScreen() else { return }
        screenFrame = screen.frame
        vm.metrics = NotchMetrics.from(screen)
        let size = vm.windowSize
        let frame = NSRect(x: screen.frame.midX - size.width / 2,
                           y: screen.frame.maxY - size.height,
                           width: size.width,
                           height: size.height)
        panel.setFrame(frame, display: true)
    }

    /// Prefer the built-in display (the one with a notch).
    static func targetScreen() -> NSScreen? {
        NSScreen.screens.first(where: { $0.safeAreaInsets.top > 0 }) ?? NSScreen.main ?? NSScreen.screens.first
    }

    // MARK: - Geometry (screen coordinates)

    private func rect(for state: NotchState) -> NSRect {
        let size = vm.size(for: state)
        return NSRect(x: screenFrame.midX - size.width / 2,
                      y: screenFrame.maxY - size.height,
                      width: size.width,
                      height: size.height)
    }

    private func hoverZone(forDrag: Bool) -> NSRect {
        var r = rect(for: .closed)
        if forDrag {
            r = r.insetBy(dx: -60, dy: 0)
            r.origin.y -= 30
            r.size.height += 30
        } else {
            r = r.insetBy(dx: -4, dy: 0)
        }
        r.size.height += 4 // include the very top pixel row
        return r
    }

    // MARK: - Mouse tracking

    private func handle(_ type: NSEvent.EventType) {
        if type == .leftMouseDown {
            dragChangeCount = NSPasteboard(name: .drag).changeCount
        }
        evaluate(mouseUp: type == .leftMouseUp)
    }

    private var isDraggingFile: Bool {
        guard NSEvent.pressedMouseButtons & 1 != 0 else { return false }
        let pb = NSPasteboard(name: .drag)
        guard pb.changeCount != dragChangeCount else { return false }
        return pb.types?.contains(.fileURL) ?? false
    }

    private func evaluate(mouseUp: Bool = false) {
        guard !vm.isInteractionLocked else { return }
        let p = NSEvent.mouseLocation
        let buttonDown = NSEvent.pressedMouseButtons & 1 != 0

        switch vm.state {
        case .closed:
            if buttonDown {
                if isDraggingFile && hoverZone(forDrag: true).contains(p) {
                    open(.drop)
                }
            } else if hoverZone(forDrag: false).contains(p) {
                open(.open)
            }

        case .open:
            let r = rect(for: .open)
            if buttonDown && isDraggingFile && r.contains(p) {
                setState(.drop)
                return
            }
            if r.insetBy(dx: -6, dy: -6).contains(p) {
                cancelClose()
            } else {
                scheduleClose(after: 0.15)
            }

        case .alert, .peek:
            // Hovering the card / peek opens the full panel.
            if buttonDown {
                if isDraggingFile && hoverZone(forDrag: true).contains(p) { open(.drop) }
            } else if rect(for: vm.state).insetBy(dx: -4, dy: 0).contains(p) {
                open(.open)
            }

        case .drop:
            if mouseUp || !buttonDown {
                // Drag finished (dropped on us, or let go somewhere else).
                scheduleClose(after: 0.4)
                return
            }
            if rect(for: .drop).insetBy(dx: -40, dy: -40).contains(p) {
                cancelClose()
            } else {
                scheduleClose(after: 0.15)
            }
        }
    }

    // MARK: - Open / close

    private func open(_ state: NotchState) {
        cancelClose()
        alertWork?.cancel()
        alertWork = nil
        peekWork?.cancel()
        peekWork = nil
        panel.ignoresMouseEvents = false
        setState(state)
        NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now)
        startPolling()

        // Let the expand animation play on an idle main thread, then refresh the data.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.vm.state != .closed else { return }
                self.music.setActive(true)
                self.system.setActive(true)
            }
        }
    }

    private func setState(_ s: NotchState) {
        guard vm.state != s else { return }
        withAnimation(.spring(response: 0.4, dampingFraction: 0.8)) {
            vm.state = s
        }
    }

    func close() {
        cancelClose()
        alertWork?.cancel()
        alertWork = nil
        peekWork?.cancel()
        peekWork = nil
        guard vm.state != .closed else { return }
        withAnimation(.spring(response: 0.34, dampingFraction: 0.9)) {
            vm.state = .closed
        }
        vm.dropTargeted = false
        panel.ignoresMouseEvents = true
        stopPolling()
        music.setActive(false)
        system.setActive(false)

        if pendingAlert, let event = vm.alertEvent, Date().timeIntervalSince(event.time) < 180 {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { [weak self] in
                MainActor.assumeIsolated {
                    guard let self, self.vm.state == .closed else { return }
                    self.showAlert()
                }
            }
        }
        pendingAlert = false
    }

    private func scheduleClose(after delay: TimeInterval) {
        guard closeWork == nil else { return }
        let work = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.closeWork = nil
                guard !self.vm.isInteractionLocked else { return }
                self.close()
            }
        }
        closeWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
    }

    private func cancelClose() {
        closeWork?.cancel()
        closeWork = nil
    }

    /// While open, re-check the mouse 10×/sec so we never miss a leave or a drag ending.
    private func startPolling() {
        guard pollTimer == nil else { return }
        let t = Timer(timeInterval: 0.1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.evaluate() }
        }
        RunLoop.main.add(t, forMode: .common)
        pollTimer = t
    }

    private func stopPolling() {
        pollTimer?.invalidate()
        pollTimer = nil
    }

    // MARK: - Actions

    private func showSettingsMenu() {
        let menu = NSMenu()
        menu.autoenablesItems = false

        let header = NSMenuItem(title: "Notch Style", action: nil, keyEquivalent: "")
        header.isEnabled = false
        menu.addItem(header)
        for style in NotchStyle.allCases {
            let item = ActionMenuItem(title: style.title) { [weak self] in
                self?.vm.style = style
            }
            item.state = vm.style == style ? .on : .off
            item.indentationLevel = 1
            menu.addItem(item)
        }

        menu.addItem(.separator())
        let mascotHeader = NSMenuItem(title: "Mascot", action: nil, keyEquivalent: "")
        mascotHeader.isEnabled = false
        menu.addItem(mascotHeader)
        for kind in MascotKind.allCases {
            let item = ActionMenuItem(title: kind.menuTitle) { [weak self] in
                self?.vm.mascot = kind
            }
            item.state = vm.mascot == kind ? .on : .off
            item.indentationLevel = 1
            menu.addItem(item)
        }

        menu.addItem(.separator())
        let companion = ActionMenuItem(title: "Show Mascot When Idle") { [weak self] in
            guard let self else { return }
            self.vm.showCompanion.toggle()
        }
        companion.state = vm.showCompanion ? .on : .off
        menu.addItem(companion)

        let peek = ActionMenuItem(title: "Show Song Peek") { [weak self] in
            guard let self else { return }
            self.vm.showSongPeek.toggle()
        }
        peek.state = vm.showSongPeek ? .on : .off
        menu.addItem(peek)

        let zoneTitle = "Prayer Zone: \(prayer.zoneName) (\(prayer.zone))" + (prayer.zoneIsAutomatic ? " · from location" : "")
        let zoneItem = NSMenuItem(title: zoneTitle, action: nil, keyEquivalent: "")
        zoneItem.isEnabled = false
        menu.addItem(zoneItem)
        menu.addItem(ActionMenuItem(title: "Refresh Prayer Times") { [weak self] in
            self?.prayer.refresh()
        })

        menu.addItem(.separator())
        let login = ActionMenuItem(title: "Launch at Login") {
            LaunchAtLogin.toggle()
        }
        login.state = LaunchAtLogin.isEnabled ? .on : .off
        menu.addItem(login)

        menu.addItem(.separator())
        menu.addItem(ActionMenuItem(title: "Quit NotchZero") {
            NSApp.terminate(nil)
        })

        guard let view = panel.contentView else { return }
        vm.isInteractionLocked = true
        cancelClose()
        let windowPoint = panel.convertPoint(fromScreen: NSEvent.mouseLocation)
        let viewPoint = view.convert(windowPoint, from: nil)
        _ = menu.popUp(positioning: nil, at: viewPoint, in: view)
        vm.isInteractionLocked = false
        evaluate()
    }

    private func pickFilesForAirDrop() {
        close()
        vm.isInteractionLocked = true
        defer { vm.isInteractionLocked = false }

        NSApp.activate()
        let openPanel = NSOpenPanel()
        openPanel.allowsMultipleSelection = true
        openPanel.canChooseFiles = true
        openPanel.canChooseDirectories = true
        openPanel.prompt = "Send"
        openPanel.message = "Choose files to send with AirDrop"
        if openPanel.runModal() == .OK {
            AirDrop.send(openPanel.urls)
        }
    }
}
