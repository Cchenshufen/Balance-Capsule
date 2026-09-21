import AppKit
import Darwin
import Foundation
import ImageIO
import UniformTypeIdentifiers

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private enum OrbSlot {
        case primary
        case secondary
    }

    private var settings = SettingsStore.shared.load()
    private var state = OrbState()
    private var sourceStates: [AgentSource: OrbState] = [:]
    private var orbPanel: OrbPanel!
    private var detailPanel: DetailPanel!
    private var orbContainer: NSView!
    private var orbGlassView: LiquidGlassEffectView!
    private var orbView: OrbView!
    private var detailView: DetailView!
    private var detailContainer: NSView!
    private var detailGlassView: LiquidGlassEffectView!
    private var secondaryOrbPanel: OrbPanel!
    private var secondaryDetailPanel: DetailPanel!
    private var secondaryOrbContainer: NSView!
    private var secondaryOrbGlassView: LiquidGlassEffectView!
    private var secondaryOrbView: OrbView!
    private var secondaryDetailView: DetailView!
    private var secondaryDetailContainer: NSView!
    private var secondaryDetailGlassView: LiquidGlassEffectView!
    private var statusItem: NSStatusItem!
    private var menu: NSMenu!
    private var hideDetailWorkItem: DispatchWorkItem?
    private var detailAnimationTimer: Timer?
    private var secondaryHideDetailWorkItem: DispatchWorkItem?
    private var secondaryDetailAnimationTimer: Timer?
    private var refreshTimer: Timer?
    private var quotaDisplayTimer: Timer?
    private var refreshGeneration = 0
    private var refreshInProgress = false
    private var refreshQueued = false
    private var detailRequested = false
    private var secondaryDetailRequested = false
    private var orbEnabled = true
    private var displayedQuotaWindow: QuotaWindowMode = .fiveHour
    private var instanceLockDescriptor: Int32 = -1

    private var selectedAgentDisplayMode: AgentDisplayMode {
        settings.agentDisplayMode ?? (settings.selectedAgent == .codex ? .codex : .claudeCode)
    }

    private var activeSources: [AgentSource] {
        selectedAgentDisplayMode.sources
    }

    private var isShowingBothSources: Bool {
        selectedAgentDisplayMode == .both
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        guard acquireInstanceLock() else {
            activateExistingInstance()
            NSApp.terminate(nil)
            return
        }
        NSApp.setActivationPolicy(.accessory)
        var migratedSettings = false
        if !settings.animationsEnabled {
            settings.animationsEnabled = true
            migratedSettings = true
        }
        if migratedSettings {
            SettingsStore.shared.save(settings)
        }
        createWindows()
        createStatusItem()
        updateUI()
        orbPanel.orderFrontRegardless()
        if isShowingBothSources {
            secondaryOrbPanel.orderFrontRegardless()
        }
        refresh()
        refreshTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            self?.refresh()
        }
        quotaDisplayTimer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
            self?.rotateQuotaDisplay()
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        refreshTimer?.invalidate()
        quotaDisplayTimer?.invalidate()
        detailAnimationTimer?.invalidate()
        secondaryDetailAnimationTimer?.invalidate()
        if orbPanel != nil {
            settings.orbX = Double(orbPanel.frame.origin.x)
            settings.orbY = Double(orbPanel.frame.origin.y)
            if secondaryOrbPanel != nil {
                settings.secondaryOrbX = Double(secondaryOrbPanel.frame.origin.x)
                settings.secondaryOrbY = Double(secondaryOrbPanel.frame.origin.y)
            }
            SettingsStore.shared.save(settings)
        }
        releaseInstanceLock()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    private func acquireInstanceLock() -> Bool {
        let directory = SettingsStore.shared.supportDirectory
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        } catch {
            return false
        }
        let path = directory.appendingPathComponent("BalanceCapsule.lock").path
        let descriptor = Darwin.open(path, O_CREAT | O_RDWR, mode_t(S_IRUSR | S_IWUSR))
        guard descriptor >= 0 else { return false }
        guard Darwin.lockf(descriptor, F_TLOCK, 0) == 0 else {
            Darwin.close(descriptor)
            return false
        }
        instanceLockDescriptor = descriptor
        return true
    }

    private func releaseInstanceLock() {
        guard instanceLockDescriptor >= 0 else { return }
        _ = Darwin.lockf(instanceLockDescriptor, F_ULOCK, 0)
        Darwin.close(instanceLockDescriptor)
        instanceLockDescriptor = -1
    }

    private func activateExistingInstance() {
        let currentPID = ProcessInfo.processInfo.processIdentifier
        NSRunningApplication.runningApplications(withBundleIdentifier: "com.anye37154.balancecapsule")
            .first { $0.processIdentifier != currentPID }?
            .activate(options: [])
    }

    private func createWindows() {
        let size = NSSize(width: 74, height: 78)
        let origin: NSPoint
        if let x = settings.orbX, let y = settings.orbY {
            origin = NSPoint(x: x, y: y)
        } else if let visible = NSScreen.main?.visibleFrame {
            origin = NSPoint(
                x: max(visible.minX + 12, visible.maxX - 530),
                y: visible.midY + visible.height / 8
            )
        } else {
            origin = NSPoint(x: 100, y: 500)
        }
        orbPanel = OrbPanel(
            contentRect: NSRect(origin: origin, size: size),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        configure(panel: orbPanel)
        orbPanel.level = .floating
        orbPanel.acceptsMouseMovedEvents = true
        orbPanel.ignoresMouseEvents = false

        orbContainer = NSView(frame: NSRect(origin: .zero, size: size))
        orbContainer.wantsLayer = true
        orbContainer.layer?.backgroundColor = NSColor.clear.cgColor
        orbGlassView = makeOrbGlassView(frame: NSRect(x: 6, y: 8, width: 62, height: 62))
        orbView = OrbView(frame: NSRect(origin: .zero, size: size))
        orbView.animationsEnabled = settings.animationsEnabled
        orbView.mode = displayedQuotaWindow
        orbView.onHoverChanged = { [weak self] hovered in
            hovered ? self?.showDetail(for: .primary) : self?.scheduleDetailHide(for: .primary)
        }
        orbView.onPositionCommitted = { [weak self] point in
            self?.settings.orbX = Double(point.x)
            self?.settings.orbY = Double(point.y)
            if let settings = self?.settings { SettingsStore.shared.save(settings) }
        }
        orbView.onRightClick = { [weak self] event in self?.showMenu(for: event) }
        orbView.onRefresh = { [weak self] in self?.refresh() }
        orbContainer.addSubview(orbGlassView)
        orbContainer.addSubview(orbView)
        orbPanel.contentView = orbContainer

        let detailSize = NSSize(width: 410, height: 210)
        detailPanel = DetailPanel(
            contentRect: NSRect(origin: .zero, size: detailSize),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        configure(panel: detailPanel)
        detailPanel.level = .floating
        detailPanel.acceptsMouseMovedEvents = true
        detailPanel.ignoresMouseEvents = false
        detailPanel.alphaValue = 1
        detailContainer = NSView(frame: NSRect(origin: .zero, size: detailSize))
        detailContainer.wantsLayer = true
        detailContainer.layer?.backgroundColor = NSColor.clear.cgColor
        detailGlassView = LiquidGlassEffectView(frame: .zero)
        detailGlassView.blendingMode = .behindWindow
        detailGlassView.state = .active
        detailGlassView.alphaValue = 0
        detailGlassView.wantsLayer = true
        detailGlassView.layer?.masksToBounds = true
        detailView = DetailView(frame: NSRect(origin: .zero, size: detailSize))
        detailView.mode = detailQuotaWindow
        applyDetailGlassConfiguration()
        detailView.onHoverChanged = { [weak self] hovered in
            hovered ? self?.cancelDetailHide(for: .primary) : self?.scheduleDetailHide(for: .primary)
        }
        detailView.onRightClick = { [weak self] event in self?.showMenu(for: event) }
        detailView.onRefresh = { [weak self] in self?.refresh() }
        detailView.onPanelMoved = { [weak self] origin, committed in
            self?.syncOrbPosition(fromDetailOrigin: origin, committed: committed, slot: .primary)
        }
        detailContainer.addSubview(detailGlassView)
        detailContainer.addSubview(detailView)
        detailPanel.contentView = detailContainer

        let secondaryOrigin: NSPoint
        if let x = settings.secondaryOrbX, let y = settings.secondaryOrbY {
            secondaryOrigin = NSPoint(x: x, y: y)
        } else {
            let horizontalOffset = size.width + 14
            let visible = (NSScreen.screens.first { $0.frame.contains(origin) } ?? NSScreen.main)?.visibleFrame
            let opensToRight = visible.map { origin.x + horizontalOffset + size.width <= $0.maxX } ?? true
            secondaryOrigin = snapOrb(
                origin: NSPoint(
                    x: opensToRight ? origin.x + horizontalOffset : origin.x - horizontalOffset,
                    y: origin.y
                ),
                size: size
            )
        }
        secondaryOrbPanel = OrbPanel(
            contentRect: NSRect(origin: secondaryOrigin, size: size),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        configure(panel: secondaryOrbPanel)
        secondaryOrbPanel.level = .floating
        secondaryOrbPanel.acceptsMouseMovedEvents = true
        secondaryOrbPanel.ignoresMouseEvents = false
        secondaryOrbContainer = NSView(frame: NSRect(origin: .zero, size: size))
        secondaryOrbContainer.wantsLayer = true
        secondaryOrbContainer.layer?.backgroundColor = NSColor.clear.cgColor
        secondaryOrbGlassView = makeOrbGlassView(frame: NSRect(x: 6, y: 8, width: 62, height: 62))
        secondaryOrbView = OrbView(frame: NSRect(origin: .zero, size: size))
        secondaryOrbView.animationsEnabled = settings.animationsEnabled
        secondaryOrbView.mode = displayedQuotaWindow
        secondaryOrbView.onHoverChanged = { [weak self] hovered in
            hovered ? self?.showDetail(for: .secondary) : self?.scheduleDetailHide(for: .secondary)
        }
        secondaryOrbView.onPositionCommitted = { [weak self] point in
            self?.settings.secondaryOrbX = Double(point.x)
            self?.settings.secondaryOrbY = Double(point.y)
            if let settings = self?.settings { SettingsStore.shared.save(settings) }
        }
        secondaryOrbView.onRightClick = { [weak self] event in self?.showMenu(for: event) }
        secondaryOrbView.onRefresh = { [weak self] in self?.refresh() }
        secondaryOrbContainer.addSubview(secondaryOrbGlassView)
        secondaryOrbContainer.addSubview(secondaryOrbView)
        secondaryOrbPanel.contentView = secondaryOrbContainer

        secondaryDetailPanel = DetailPanel(
            contentRect: NSRect(origin: .zero, size: detailSize),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        configure(panel: secondaryDetailPanel)
        secondaryDetailPanel.level = .floating
        secondaryDetailPanel.acceptsMouseMovedEvents = true
        secondaryDetailPanel.ignoresMouseEvents = false
        secondaryDetailPanel.alphaValue = 1
        secondaryDetailContainer = NSView(frame: NSRect(origin: .zero, size: detailSize))
        secondaryDetailContainer.wantsLayer = true
        secondaryDetailContainer.layer?.backgroundColor = NSColor.clear.cgColor
        secondaryDetailGlassView = LiquidGlassEffectView(frame: .zero)
        secondaryDetailGlassView.blendingMode = .behindWindow
        secondaryDetailGlassView.state = .active
        secondaryDetailGlassView.alphaValue = 0
        secondaryDetailGlassView.wantsLayer = true
        secondaryDetailGlassView.layer?.masksToBounds = true
        secondaryDetailView = DetailView(frame: NSRect(origin: .zero, size: detailSize))
        secondaryDetailView.mode = detailQuotaWindow
        secondaryDetailView.onHoverChanged = { [weak self] hovered in
            hovered ? self?.cancelDetailHide(for: .secondary) : self?.scheduleDetailHide(for: .secondary)
        }
        secondaryDetailView.onRightClick = { [weak self] event in self?.showMenu(for: event) }
        secondaryDetailView.onRefresh = { [weak self] in self?.refresh() }
        secondaryDetailView.onPanelMoved = { [weak self] origin, committed in
            self?.syncOrbPosition(fromDetailOrigin: origin, committed: committed, slot: .secondary)
        }
        secondaryDetailContainer.addSubview(secondaryDetailGlassView)
        secondaryDetailContainer.addSubview(secondaryDetailView)
        secondaryDetailPanel.contentView = secondaryDetailContainer
        applyDetailGlassConfiguration()
    }

    private func makeOrbGlassView(frame: NSRect) -> LiquidGlassEffectView {
        let glassView = LiquidGlassEffectView(frame: frame)
        glassView.material = .underWindowBackground
        glassView.blendingMode = .behindWindow
        glassView.state = .active
        glassView.isEmphasized = false
        glassView.wantsLayer = true
        glassView.layer?.masksToBounds = true
        glassView.alphaValue = 1
        configureOrbGlassView(glassView, frame: frame)
        return glassView
    }

    private func configureOrbGlassView(_ glassView: LiquidGlassEffectView, frame: NSRect) {
        glassView.frame = frame
        glassView.layer?.cornerRadius = frame.width / 2
        glassView.updateRefraction(
            primitives: [.ellipse(NSRect(origin: .zero, size: frame.size))],
            strength: frame.width >= 60 ? 13 : 8,
            edgeDepth: frame.width >= 60 ? 11 : 6
        )
    }

    private var selectedDetailGlassStyle: DetailGlassStyle {
        settings.detailGlassStyle ?? .frosted
    }

    private var selectedDetailGlassTransparency: CGFloat {
        let value = settings.detailGlassTransparency ?? 0.55
        return CGFloat(min(max(value, 0.25), 0.70))
    }

    private var selectedDetailBackdropOpacity: CGFloat {
        // The backdrop is intentionally less transparent than the label suggests so
        // the text remains readable on a black wallpaper or a dark editor window.
        min(0.95, 0.20 + (1 - selectedDetailGlassTransparency))
    }

    private func applyDetailGlassConfiguration() {
        applyDetailGlassConfiguration(to: detailGlassView, detailView: detailView, slot: .primary)
        if secondaryDetailGlassView != nil, secondaryDetailView != nil {
            applyDetailGlassConfiguration(
                to: secondaryDetailGlassView,
                detailView: secondaryDetailView,
                slot: .secondary
            )
        }
    }

    private func applyDetailGlassConfiguration(
        to glassView: LiquidGlassEffectView,
        detailView: DetailView,
        slot: OrbSlot
    ) {
        let style = selectedDetailGlassStyle
        switch style {
        case .frosted:
            glassView.material = .popover
            glassView.isEmphasized = false
            glassView.appearance = NSAppearance(named: .aqua)
        case .midnight:
            glassView.material = .hudWindow
            glassView.isEmphasized = true
            glassView.appearance = NSAppearance(named: .darkAqua)
        }
        detailView.glassStyle = style
        detailView.glassTransparency = selectedDetailGlassTransparency
        updateGlassFrame(progress: detailView.expansionProgress, slot: slot)
    }

    private func configure(panel: NSPanel) {
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        panel.isMovableByWindowBackground = false
    }

    private func createStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = makeMenuBarImage(color: state.risk.color)
        statusItem.button?.title = " …"
        statusItem.button?.toolTip = "Balance Capsule 正在加载"
        menu = NSMenu(title: "Balance Capsule")
        menu.delegate = self
        statusItem.menu = menu
        rebuildMenu()
    }

    func menuWillOpen(_ menu: NSMenu) { rebuildMenu() }

    private func rebuildMenu() {
        menu.removeAllItems()
        let summary = NSMenuItem(title: summaryText(), action: nil, keyEquivalent: "")
        summary.isEnabled = false
        menu.addItem(summary)
        menu.addItem(.separator())
        let refreshItem = item(
            refreshInProgress ? "正在刷新…" : "立即刷新",
            action: #selector(refreshAction),
            key: refreshInProgress ? "" : "r"
        )
        refreshItem.isEnabled = !refreshInProgress
        menu.addItem(refreshItem)
        let orbTitle = orbEnabled ? "隐藏悬浮球" : "显示悬浮球"
        menu.addItem(item(orbTitle, action: #selector(toggleOrbVisibility)))

        let sources = NSMenu()
        for mode in AgentDisplayMode.allCases {
            let sourceItem = item(mode.menuTitle, action: #selector(selectAgentDisplayMode(_:)))
            sourceItem.representedObject = mode.rawValue
            sourceItem.state = selectedAgentDisplayMode == mode ? .on : .off
            sources.addItem(sourceItem)
        }
        let sourceRoot = NSMenuItem(title: "显示来源", action: nil, keyEquivalent: "")
        sourceRoot.submenu = sources
        menu.addItem(sourceRoot)

        let glassStyles = NSMenu()
        for style in DetailGlassStyle.allCases {
            let styleItem = item(style.menuTitle, action: #selector(selectDetailGlassStyle(_:)))
            styleItem.representedObject = style.rawValue
            styleItem.state = style == selectedDetailGlassStyle ? .on : .off
            glassStyles.addItem(styleItem)
        }
        let glassRoot = NSMenuItem(title: "详情玻璃效果", action: nil, keyEquivalent: "")
        glassRoot.submenu = glassStyles
        menu.addItem(glassRoot)

        let transparencyOptions: [(String, Double)] = [
            ("70%（更通透）", 0.70),
            ("55%（均衡）", 0.55),
            ("40%（更清晰）", 0.40),
            ("25%（最清晰）", 0.25)
        ]
        let transparencies = NSMenu()
        for option in transparencyOptions {
            let opacityItem = item(option.0, action: #selector(selectDetailGlassTransparency(_:)))
            opacityItem.representedObject = NSNumber(value: option.1)
            opacityItem.state = abs(Double(selectedDetailGlassTransparency) - option.1) < 0.001 ? .on : .off
            transparencies.addItem(opacityItem)
        }
        let transparencyRoot = NSMenuItem(title: "详情透明度", action: nil, keyEquivalent: "")
        transparencyRoot.submenu = transparencies
        menu.addItem(transparencyRoot)

        menu.addItem(.separator())
        let startup = item("登录时启动", action: #selector(toggleStartup))
        startup.state = settings.startAtLogin ? .on : .off
        menu.addItem(startup)
        menu.addItem(item("安装 Claude Code 桥接…", action: #selector(installClaudeBridge)))
        menu.addItem(.separator())
        menu.addItem(item("关于 Balance Capsule", action: #selector(showAbout)))
        menu.addItem(item("退出", action: #selector(quit), key: "q"))
    }

    private func item(_ title: String, action: Selector, key: String = "") -> NSMenuItem {
        let value = NSMenuItem(title: title, action: action, keyEquivalent: key)
        value.target = self
        return value
    }

    private func showMenu(for event: NSEvent) {
        rebuildMenu()
        NSMenu.popUpContextMenu(menu, with: event, for: event.window?.contentView ?? orbView)
    }

    private func orbPanel(for slot: OrbSlot) -> OrbPanel {
        slot == .primary ? orbPanel : secondaryOrbPanel
    }

    private func detailPanel(for slot: OrbSlot) -> DetailPanel {
        slot == .primary ? detailPanel : secondaryDetailPanel
    }

    private func detailView(for slot: OrbSlot) -> DetailView {
        slot == .primary ? detailView : secondaryDetailView
    }

    private func detailGlassView(for slot: OrbSlot) -> LiquidGlassEffectView {
        slot == .primary ? detailGlassView : secondaryDetailGlassView
    }

    private func isDetailRequested(for slot: OrbSlot) -> Bool {
        slot == .primary ? detailRequested : secondaryDetailRequested
    }

    private func setDetailRequested(_ requested: Bool, for slot: OrbSlot) {
        if slot == .primary {
            detailRequested = requested
        } else {
            secondaryDetailRequested = requested
        }
    }

    private func showDetail(for slot: OrbSlot) {
        cancelDetailHide(for: slot)
        guard orbEnabled else { return }
        if slot == .secondary, !isShowingBothSources { return }
        guard !isDetailRequested(for: slot) else { return }
        setDetailRequested(true, for: slot)
        let orb = orbPanel(for: slot).frame
        let targetDetailPanel = detailPanel(for: slot)
        let targetDetailView = detailView(for: slot)
        let detailSize = targetDetailPanel.frame.size
        let visible = (NSScreen.screens.first { $0.frame.intersects(orb) } ?? NSScreen.main)?.visibleFrame
        let opensRight = visible.map { orb.minX + detailSize.width <= $0.maxX } ?? true
        targetDetailView.opensToRight = opensRight
        updateGlassFrame(progress: targetDetailView.expansionProgress, slot: slot)
        var origin = NSPoint(
            x: opensRight ? orb.minX : orb.minX - 336,
            y: orb.minY - 66
        )
        if let visible {
            origin.x = min(max(origin.x, visible.minX), visible.maxX - detailSize.width)
            origin.y = min(max(origin.y, visible.minY), visible.maxY - detailSize.height)
        }
        targetDetailPanel.setFrameOrigin(origin)
        targetDetailPanel.orderFrontRegardless()
        animateDetail(to: 1, slot: slot)
    }

    private func scheduleDetailHide(for slot: OrbSlot) {
        guard isDetailRequested(for: slot) else { return }
        cancelDetailHide(for: slot)
        let work = DispatchWorkItem { [weak self] in self?.hideDetail(for: slot) }
        if slot == .primary {
            hideDetailWorkItem = work
        } else {
            secondaryHideDetailWorkItem = work
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.32, execute: work)
    }

    private func cancelDetailHide(for slot: OrbSlot) {
        if slot == .primary {
            hideDetailWorkItem?.cancel()
            hideDetailWorkItem = nil
        } else {
            secondaryHideDetailWorkItem?.cancel()
            secondaryHideDetailWorkItem = nil
        }
    }

    private func hideDetail(for slot: OrbSlot) {
        guard isDetailRequested(for: slot) else { return }
        setDetailRequested(false, for: slot)
        animateDetail(to: 0, slot: slot) { [weak self] in
            guard let self else { return }
            self.detailPanel(for: slot).orderOut(nil)
            guard self.orbEnabled else { return }
            if slot == .secondary, !self.isShowingBothSources { return }
            let targetOrbPanel = self.orbPanel(for: slot)
            targetOrbPanel.alphaValue = 1
            targetOrbPanel.orderFrontRegardless()
        }
    }

    private func animateDetail(to target: CGFloat, slot: OrbSlot, completion: (() -> Void)? = nil) {
        let targetDetailView = detailView(for: slot)
        let targetOrbPanel = orbPanel(for: slot)
        if slot == .primary {
            detailAnimationTimer?.invalidate()
        } else {
            secondaryDetailAnimationTimer?.invalidate()
        }
        let start = targetDetailView.expansionProgress
        if abs(start - target) < 0.001 {
            completion?()
            return
        }
        let startedAt = Date()
        let duration = target > start ? 1.02 : 0.32
        let timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 60.0, repeats: true) { [weak self] timer in
            guard let self else {
                timer.invalidate()
                return
            }
            let elapsed = Date().timeIntervalSince(startedAt)
            let raw = min(1, elapsed / duration)
            let eased: Double
            if target > start {
                eased = stagedExpansionProgress(raw)
            } else {
                eased = raw * raw * (3 - 2 * raw)
            }
            let value = start + (target - start) * CGFloat(eased)
            targetDetailView.expansionProgress = value
            self.updateGlassFrame(progress: value, slot: slot)
            targetOrbPanel.alphaValue = target > start ? max(0, 1 - value * 5.5) : min(1, 1 - value)
            if target > start, value > 0.22, targetOrbPanel.isVisible {
                targetOrbPanel.orderOut(nil)
            }
            if raw >= 1 {
                timer.invalidate()
                if slot == .primary {
                    self.detailAnimationTimer = nil
                } else {
                    self.secondaryDetailAnimationTimer = nil
                }
                targetDetailView.expansionProgress = target
                self.updateGlassFrame(progress: target, slot: slot)
                targetOrbPanel.alphaValue = target > 0 ? 0 : 1
                if target <= 0 {
                    if slot == .primary || self.isShowingBothSources {
                        targetOrbPanel.orderFrontRegardless()
                    }
                }
                completion?()
            }
        }
        if slot == .primary {
            detailAnimationTimer = timer
        } else {
            secondaryDetailAnimationTimer = timer
        }
    }

    private func updateGlassFrame(progress: CGFloat, slot: OrbSlot) {
        guard detailGlassView != nil, detailView != nil else { return }
        if slot == .secondary,
           (secondaryDetailGlassView == nil || secondaryDetailView == nil) { return }
        let targetDetailView = detailView(for: slot)
        let targetGlassView = detailGlassView(for: slot)
        let normalized = max(0, min(1, progress))
        let neckProgress = max(0, min(1, (normalized - 0.16) / 0.36))
        let cardProgress = max(0, min(1, (normalized - 0.42) / 0.58))
        let easedNeck = 1 - pow(1 - neckProgress, 3)
        let easedCard = cardProgress * cardProgress * (3 - 2 * cardProgress)
        let orbRect = targetDetailView.opensToRight
            ? NSRect(x: 6, y: 74, width: 62, height: 62)
            : NSRect(x: targetDetailView.bounds.maxX - 68, y: 74, width: 62, height: 62)
        let cardRect = targetDetailView.opensToRight
            ? NSRect(x: 92, y: 12, width: 310, height: 186)
            : NSRect(x: 8, y: 12, width: 310, height: 186)
        let maskPath = CGMutablePath()
        maskPath.addEllipse(in: orbRect)
        var refractionPrimitives: [LiquidGlassPrimitive] = [.ellipse(orbRect)]

        if easedNeck > 0.001 && easedCard < 0.04 {
            let neckPath = makeLiquidNeckPath(
                orbRect: orbRect,
                cardRect: cardRect,
                opensToRight: targetDetailView.opensToRight,
                neckProgress: easedNeck,
                cardProgress: easedCard
            )
            maskPath.addPath(neckPath.compatibleCGPath())
            refractionPrimitives.append(
                .roundedRect(neckPath.bounds, min(neckPath.bounds.width, neckPath.bounds.height) / 2)
            )
        }

        if easedCard > 0.001 {
            let width = max(2, cardRect.width * easedCard)
            let height = 26 + (cardRect.height - 26) * easedCard
            let visibleCard = targetDetailView.opensToRight
                ? CGRect(x: cardRect.minX, y: orbRect.midY - height / 2, width: width, height: height)
                : CGRect(x: cardRect.maxX - width, y: orbRect.midY - height / 2, width: width, height: height)
            let unifiedPath = makeUnifiedGlassPath(
                cardRect: visibleCard,
                orbRect: orbRect,
                opensToRight: targetDetailView.opensToRight,
                progress: easedCard
            )
            maskPath.addPath(unifiedPath.compatibleCGPath())
            refractionPrimitives.append(.roundedRect(visibleCard, min(20, min(width, height) / 2)))
            refractionPrimitives.append(.roundedRect(unifiedPath.bounds, min(15, unifiedPath.bounds.height / 2)))
        }

        targetGlassView.frame = targetDetailView.bounds
        let maskLayer = CAShapeLayer()
        maskLayer.frame = targetDetailView.bounds
        maskLayer.path = maskPath
        targetGlassView.layer?.mask = maskLayer
        targetGlassView.updateRefraction(
            primitives: refractionPrimitives,
            strength: 18 + easedNeck * 8,
            edgeDepth: 16
        )
        targetGlassView.alphaValue = selectedDetailBackdropOpacity
    }

    private func syncOrbPosition(fromDetailOrigin origin: NSPoint, committed: Bool, slot: OrbSlot) {
        let targetDetailPanel = detailPanel(for: slot)
        let targetDetailView = detailView(for: slot)
        let targetOrbPanel = orbPanel(for: slot)
        let detailWidth = targetDetailPanel.frame.width
        let rawOrbOrigin = NSPoint(
            x: targetDetailView.opensToRight ? origin.x : origin.x + detailWidth - 74,
            y: origin.y + 66
        )
        let orbOrigin = committed
            ? snapOrb(origin: rawOrbOrigin, size: targetOrbPanel.frame.size)
            : rawOrbOrigin
        targetOrbPanel.setFrameOrigin(orbOrigin)

        if committed {
            let alignedDetailOrigin = NSPoint(
                x: targetDetailView.opensToRight ? orbOrigin.x : orbOrigin.x - detailWidth + 74,
                y: orbOrigin.y - 66
            )
            targetDetailPanel.setFrameOrigin(alignedDetailOrigin)
            if slot == .primary {
                settings.orbX = Double(orbOrigin.x)
                settings.orbY = Double(orbOrigin.y)
            } else {
                settings.secondaryOrbX = Double(orbOrigin.x)
                settings.secondaryOrbY = Double(orbOrigin.y)
            }
            SettingsStore.shared.save(settings)
        }
    }

    private func snapOrb(origin: NSPoint, size: NSSize) -> NSPoint {
        let center = NSPoint(x: origin.x + size.width / 2, y: origin.y + size.height / 2)
        let visible = (NSScreen.screens.first { $0.frame.contains(center) } ?? NSScreen.main)?.visibleFrame
        guard let visible else { return origin }
        var result = NSPoint(
            x: min(max(origin.x, visible.minX), visible.maxX - size.width),
            y: min(max(origin.y, visible.minY), visible.maxY - size.height)
        )
        let distance: CGFloat = 14
        if abs(result.x - visible.minX) < distance { result.x = visible.minX }
        if abs(result.x + size.width - visible.maxX) < distance { result.x = visible.maxX - size.width }
        if abs(result.y - visible.minY) < distance { result.y = visible.minY }
        if abs(result.y + size.height - visible.maxY) < distance { result.y = visible.maxY - size.height }
        return result
    }

    private func refresh() {
        refreshGeneration += 1
        let generation = refreshGeneration
        if refreshInProgress {
            refreshQueued = true
            return
        }
        refreshInProgress = true
        rebuildMenu()
        if state.updatedAt == nil {
            state = loadingState(for: activeSources.first ?? .codex)
            updateUI()
        }
        let sources = activeSources
        let group = DispatchGroup()
        let lock = NSLock()
        var results: [AgentSource: Result<OrbState, Error>] = [:]
        for source in sources {
            group.enter()
            DispatchQueue.global(qos: .utility).async {
                let result = Result { try ProviderCoordinator.read(source: source) }
                lock.lock()
                results[source] = result
                lock.unlock()
                group.leave()
            }
        }
        group.notify(queue: .main) { [weak self] in
            guard let self else { return }
            if generation == self.refreshGeneration {
                for source in sources {
                    guard let result = results[source] else { continue }
                    switch result {
                    case .success(let updated):
                        self.sourceStates[source] = updated
                    case .failure(let error):
                        self.recordRefreshFailure(error, for: source)
                    }
                }
                self.syncDisplayedState(resetQuotaWindow: true)
                self.updateUI()
            }
            self.finishRefresh()
        }
    }

    private func loadingState(for source: AgentSource) -> OrbState {
        OrbState(
            sourceName: source == .codex ? "Codex 官方" : "Claude Code 官方",
            agentName: source.displayName
        )
    }

    private func recordRefreshFailure(_ error: Error, for source: AgentSource) {
        if var existing = sourceStates[source] {
            existing.message = "刷新失败：\(error.localizedDescription)"
            existing.isStale = true
            sourceStates[source] = existing
        } else {
            sourceStates[source] = OrbState(
                sourceName: source == .codex ? "Codex" : "Claude Code",
                agentName: source.displayName,
                risk: .error,
                message: error.localizedDescription
            )
        }
    }

    private func syncDisplayedState(resetQuotaWindow: Bool) {
        let primarySource: AgentSource = isShowingBothSources ? .codex : activeSources.first ?? .codex
        state = sourceStates[primarySource] ?? loadingState(for: primarySource)
        if resetQuotaWindow {
            displayedQuotaWindow = state.fiveHour != nil ? .fiveHour : .weekly
        }
    }

    private func finishRefresh() {
        refreshInProgress = false
        if refreshQueued {
            refreshQueued = false
            refresh()
        } else {
            rebuildMenu()
        }
    }

    private func updateUI(rebuildMenu shouldRebuildMenu: Bool = true) {
        let displayMode = activeQuotaWindow
        let codexState = isShowingBothSources ? (sourceStates[.codex] ?? loadingState(for: .codex)) : state
        let claudeState = isShowingBothSources
            ? (sourceStates[.claudeCode] ?? loadingState(for: .claudeCode))
            : nil
        orbView?.state = codexState
        orbView?.mode = activeQuotaWindow(for: codexState)
        orbView?.sourceBadge = isShowingBothSources ? .codex : nil
        detailView?.state = codexState
        detailView?.mode = detailQuotaWindow(for: codexState)
        detailView?.secondaryState = nil
        detailView?.statusRisk = combinedDisplayRisk(states: [codexState])
        if let claudeState {
            secondaryOrbView?.state = claudeState
            secondaryOrbView?.mode = activeQuotaWindow(for: claudeState)
            secondaryOrbView?.sourceBadge = .claudeCode
            secondaryDetailView?.state = claudeState
            secondaryDetailView?.mode = detailQuotaWindow(for: claudeState)
            secondaryDetailView?.secondaryState = nil
            secondaryDetailView?.statusRisk = combinedDisplayRisk(states: [claudeState])
            if orbEnabled, !secondaryDetailRequested {
                secondaryOrbPanel?.alphaValue = 1
                secondaryOrbPanel?.orderFrontRegardless()
            }
        } else {
            resetDetail(for: .secondary)
            secondaryOrbPanel?.orderOut(nil)
        }
        let risk = combinedDisplayRisk(states: [codexState, claudeState].compactMap { $0 })
        statusItem?.button?.image = makeMenuBarImage(color: risk.color)
        statusItem?.button?.title = statusTitle(for: displayMode)
        statusItem?.button?.toolTip = summaryText()
        if shouldRebuildMenu { rebuildMenu() }
    }

    private func summaryText() -> String {
        if isShowingBothSources {
            let codex = sourceStates[.codex] ?? loadingState(for: .codex)
            let claude = sourceStates[.claudeCode] ?? loadingState(for: .claudeCode)
            return [sourceSummary(for: codex), sourceSummary(for: claude)]
                .joined(separator: "  |  ")
        }
        return sourceSummary(for: state)
    }

    private var activeQuotaWindow: QuotaWindowMode {
        activeQuotaWindow(for: state)
    }

    private func activeQuotaWindow(for sourceState: OrbState) -> QuotaWindowMode {
        if sourceState.fiveHour != nil && sourceState.weekly != nil { return displayedQuotaWindow }
        return sourceState.fiveHour != nil ? .fiveHour : .weekly
    }

    private var detailQuotaWindow: QuotaWindowMode {
        detailQuotaWindow(for: state)
    }

    private func detailQuotaWindow(for sourceState: OrbState) -> QuotaWindowMode {
        sourceState.weekly != nil ? .weekly : .fiveHour
    }

    private func rotateQuotaDisplay() {
        let states = isShowingBothSources
            ? [sourceStates[.codex], sourceStates[.claudeCode]].compactMap { $0 }
            : [state]
        guard states.contains(where: { $0.balanceText == nil && $0.fiveHour != nil && $0.weekly != nil }) else { return }
        displayedQuotaWindow = displayedQuotaWindow == .fiveHour ? .weekly : .fiveHour
        updateUI(rebuildMenu: false)
    }

    private func statusTitle(for mode: QuotaWindowMode) -> String {
        if isShowingBothSources {
            let codex = sourceStates[.codex] ?? loadingState(for: .codex)
            let claude = sourceStates[.claudeCode] ?? loadingState(for: .claudeCode)
            let values = [
                "C \(compactQuotaText(for: codex, mode: mode))",
                "Cl \(compactQuotaText(for: claude, mode: activeQuotaWindow(for: claude)))"
            ]
            return " " + values.joined(separator: " · ")
        }
        return " \(compactQuotaText(for: state, mode: mode))"
    }

    private func compactQuotaText(for sourceState: OrbState, mode: QuotaWindowMode) -> String {
        let display = sourceState.displayText(mode: mode)
        let suffix = sourceState.balanceText == nil && sourceState.selectedPercent(mode: mode) != nil ? "%" : ""
        return "\(display)\(suffix)"
    }

    private func sourceSummary(for sourceState: OrbState) -> String {
        if let message = sourceState.message { return "\(sourceState.sourceName)：\(message)" }
        let mode = activeQuotaWindow(for: sourceState)
        return "\(sourceState.sourceName) · \(sourceState.caption(mode: mode)) \(sourceState.displayText(mode: mode))"
    }

    private func combinedDisplayRisk(states: [OrbState]) -> QuotaRisk {
        states
            .map { sourceState in
                if sourceState.isStale && sourceState.risk.severity < QuotaRisk.warning.severity {
                    return QuotaRisk.warning
                }
                return sourceState.risk
            }
            .max { $0.severity < $1.severity }
            ?? .loading
    }

    private func makeMenuBarImage(color: NSColor) -> NSImage {
        let image = NSImage(size: NSSize(width: 18, height: 18))
        image.lockFocus()
        color.setFill()
        NSBezierPath(ovalIn: NSRect(x: 2, y: 2, width: 14, height: 14)).fill()
        NSColor.white.withAlphaComponent(0.72).setStroke()
        let ring = NSBezierPath(ovalIn: NSRect(x: 4.5, y: 4.5, width: 9, height: 9))
        ring.lineWidth = 1.2
        ring.stroke()
        image.unlockFocus()
        image.isTemplate = false
        return image
    }

    @objc private func refreshAction() { refresh() }

    @objc private func toggleOrbVisibility() {
        orbEnabled.toggle()
        resetDetail(for: .primary)
        resetDetail(for: .secondary)
        if orbEnabled {
            orbPanel.alphaValue = 1
            orbPanel.orderFrontRegardless()
            if isShowingBothSources {
                secondaryOrbPanel.alphaValue = 1
                secondaryOrbPanel.orderFrontRegardless()
            }
        } else {
            orbPanel.orderOut(nil)
            secondaryOrbPanel.orderOut(nil)
        }
        rebuildMenu()
    }

    private func resetDetail(for slot: OrbSlot) {
        cancelDetailHide(for: slot)
        setDetailRequested(false, for: slot)
        if slot == .primary {
            detailAnimationTimer?.invalidate()
            detailAnimationTimer = nil
        } else {
            secondaryDetailAnimationTimer?.invalidate()
            secondaryDetailAnimationTimer = nil
        }
        let targetDetailView = detailView(for: slot)
        targetDetailView.expansionProgress = 0
        detailGlassView(for: slot).alphaValue = 0
        detailPanel(for: slot).orderOut(nil)
        orbPanel(for: slot).alphaValue = 1
    }

    @objc private func selectDetailGlassStyle(_ sender: NSMenuItem) {
        guard let rawValue = sender.representedObject as? String,
              let style = DetailGlassStyle(rawValue: rawValue) else { return }
        settings.detailGlassStyle = style
        SettingsStore.shared.save(settings)
        applyDetailGlassConfiguration()
        rebuildMenu()
    }

    @objc private func selectDetailGlassTransparency(_ sender: NSMenuItem) {
        guard let value = sender.representedObject as? NSNumber else { return }
        settings.detailGlassTransparency = min(max(value.doubleValue, 0.25), 0.70)
        SettingsStore.shared.save(settings)
        applyDetailGlassConfiguration()
        rebuildMenu()
    }

    @objc private func selectAgentDisplayMode(_ sender: NSMenuItem) {
        guard let rawValue = sender.representedObject as? String,
              let mode = AgentDisplayMode(rawValue: rawValue) else { return }
        settings.agentDisplayMode = mode
        if mode == .codex {
            settings.selectedAgent = .codex
        } else if mode == .claudeCode {
            settings.selectedAgent = .claudeCode
        }
        SettingsStore.shared.save(settings)
        syncDisplayedState(resetQuotaWindow: true)
        updateUI()
        refresh()
    }

    @objc private func toggleStartup() {
        let enable = !settings.startAtLogin
        do {
            try StartupService.setEnabled(enable)
            settings.startAtLogin = enable
            SettingsStore.shared.save(settings)
            rebuildMenu()
        } catch {
            showAlert(title: "无法更新登录项", message: error.localizedDescription)
        }
    }

    @objc private func installClaudeBridge() {
        do {
            let message = try ClaudeQuotaProvider.installBridge()
            showAlert(title: "Claude Code 桥接", message: message)
        } catch {
            showAlert(title: "无法安装桥接", message: error.localizedDescription)
        }
    }

    @objc private func showAbout() {
        showAlert(
            title: "Balance Capsule for macOS",
            message: "版本 BalanceCapsule-mac.15 · macOS 26+\n\n最低支持 macOS 26；应用图标使用全画布液态玻璃，详情百分号已收紧，Token 统一使用万和亿。"
        )
    }

    private func showAlert(title: String, message: String) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.alertStyle = .informational
        alert.addButton(withTitle: "好")
        alert.runModal()
        NSApp.setActivationPolicy(.accessory)
    }

    @objc private func quit() { NSApp.terminate(nil) }
}

enum StartupService {
    static func setEnabled(_ enabled: Bool) throws {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let directory = home.appendingPathComponent("Library/LaunchAgents", isDirectory: true)
        let url = directory.appendingPathComponent("com.anye37154.balancecapsule.plist")
        if !enabled {
            if FileManager.default.fileExists(atPath: url.path) {
                try FileManager.default.removeItem(at: url)
            }
            return
        }
        guard let executable = Bundle.main.executablePath else {
            throw ProviderError.message("无法定位 Balance Capsule 可执行文件。")
        }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let plist: [String: Any] = [
            "Label": "com.anye37154.balancecapsule",
            "ProgramArguments": [executable],
            "RunAtLoad": true,
            "KeepAlive": false
        ]
        let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
        try data.write(to: url, options: .atomic)
    }
}

func runProbe() -> Int32 {
    do {
        let state = try ProviderCoordinator.read(source: .codex)
        let output: [String: Any] = [
            "source": state.sourceName,
            "fiveHourRemaining": state.fiveHour?.remainingPercent as Any,
            "weeklyRemaining": state.weekly?.remainingPercent as Any,
            "tokensToday": state.tokenUsage?.todayTokens as Any,
            "tokensMonth": state.tokenUsage?.monthTokens as Any,
            "tokensTotal": state.tokenUsage?.totalTokens as Any,
            "balance": state.balanceText as Any,
            "status": state.risk.statusText
        ]
        let data = try JSONSerialization.data(withJSONObject: output, options: [.prettyPrinted, .sortedKeys])
        try FileHandle.standardOutput.write(contentsOf: data)
        try FileHandle.standardOutput.write(contentsOf: Data([0x0A]))
        return 0
    } catch {
        FileHandle.standardError.write(Data(("Balance Capsule probe failed: \(error.localizedDescription)\n").utf8))
        return 1
    }
}

func renderPreview(to directory: URL) -> Int32 {
    do {
        _ = NSApplication.shared
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let previewState = OrbState(
            sourceName: "Codex 官方",
            agentName: "Codex",
            fiveHour: QuotaWindowValue(remainingPercent: 72, durationMinutes: 300, resetsAt: Date().addingTimeInterval(7200)),
            weekly: QuotaWindowValue(remainingPercent: 64, durationMinutes: 10_080, resetsAt: Date().addingTimeInterval(172_800)),
            tokenUsage: TokenUsageSummary(todayTokens: 248_300, monthTokens: 12_640_000, totalTokens: 801_160_000),
            risk: .safe,
            updatedAt: Date()
        )
        let claudePreviewState = OrbState(
            sourceName: "Claude Code 官方",
            agentName: "Claude Code",
            weekly: QuotaWindowValue(remainingPercent: 87, durationMinutes: 10_080, resetsAt: Date().addingTimeInterval(216_000)),
            risk: .safe,
            updatedAt: Date()
        )

        let orb = OrbView(frame: NSRect(x: 0, y: 0, width: 74, height: 78))
        orb.animationsEnabled = false
        orb.mode = .weekly
        orb.state = previewState
        try render(view: orb, to: directory.appendingPathComponent("apple-orb-preview.png"))
        let claudeOrb = OrbView(frame: NSRect(x: 0, y: 0, width: 74, height: 78))
        claudeOrb.animationsEnabled = false
        claudeOrb.mode = .weekly
        claudeOrb.state = claudePreviewState
        orb.sourceBadge = .codex
        claudeOrb.sourceBadge = .claudeCode
        try render(view: claudeOrb, to: directory.appendingPathComponent("apple-claude-orb-preview.png"))
        let orbPair = NSView(frame: NSRect(x: 0, y: 0, width: 162, height: 78))
        orbPair.wantsLayer = true
        orbPair.layer?.backgroundColor = NSColor.clear.cgColor
        orb.frame.origin = .zero
        claudeOrb.frame.origin = NSPoint(x: 88, y: 0)
        orbPair.addSubview(orb)
        orbPair.addSubview(claudeOrb)
        try render(view: orbPair, to: directory.appendingPathComponent("apple-dual-source-orb-preview.png"))
        let claudeStagePreview = NSView(frame: NSRect(x: 0, y: 0, width: 246, height: 78))
        claudeStagePreview.wantsLayer = true
        claudeStagePreview.layer?.backgroundColor = NSColor.clear.cgColor
        for (index, percent) in [87.0, 35.0, 12.0].enumerated() {
            let stageOrb = OrbView(frame: NSRect(x: CGFloat(index * 86), y: 0, width: 74, height: 78))
            stageOrb.animationsEnabled = false
            stageOrb.mode = .weekly
            stageOrb.sourceBadge = .claudeCode
            stageOrb.state = OrbState(
                sourceName: "Claude Code 官方",
                agentName: "Claude Code",
                weekly: QuotaWindowValue(remainingPercent: percent, durationMinutes: 10_080, resetsAt: nil),
                risk: risk(for: percent),
                updatedAt: Date()
            )
            claudeStagePreview.addSubview(stageOrb)
        }
        try render(
            view: claudeStagePreview,
            to: directory.appendingPathComponent("apple-claude-stage-preview.png")
        )

        let appIcon = AppIconView(frame: NSRect(x: 0, y: 0, width: 512, height: 512))
        appIcon.state = previewState
        try render(view: appIcon, to: directory.appendingPathComponent("app-icon.png"))

        let detail = DetailView(frame: NSRect(x: 0, y: 0, width: 410, height: 210))
        detail.state = previewState
        detail.mode = .weekly
        detail.expansionProgress = 1
        detail.opensToRight = true
        try render(view: detail, to: directory.appendingPathComponent("apple-expanded-preview.png"))
        let claudeDetail = DetailView(frame: NSRect(x: 0, y: 0, width: 410, height: 210))
        claudeDetail.state = claudePreviewState
        claudeDetail.mode = .weekly
        claudeDetail.expansionProgress = 1
        claudeDetail.opensToRight = true
        try render(view: claudeDetail, to: directory.appendingPathComponent("apple-claude-detail-preview.png"))
        let detailPair = NSView(frame: NSRect(x: 0, y: 0, width: 836, height: 210))
        detailPair.wantsLayer = true
        detailPair.layer?.backgroundColor = NSColor.clear.cgColor
        detail.frame.origin = .zero
        claudeDetail.frame.origin = NSPoint(x: 426, y: 0)
        detailPair.addSubview(detail)
        detailPair.addSubview(claudeDetail)
        try render(view: detailPair, to: directory.appendingPathComponent("apple-dual-source-detail-preview.png"))

        detail.expansionProgress = 0.34
        try render(view: detail, to: directory.appendingPathComponent("apple-hover-liquid-preview.png"))
        try renderHoverAnimation(
            view: detail,
            to: directory.appendingPathComponent("apple-hover-animation.gif")
        )
        return 0
    } catch {
        FileHandle.standardError.write(Data(("Balance Capsule preview failed: \(error.localizedDescription)\n").utf8))
        return 1
    }
}

func renderHoverAnimation(view: DetailView, to url: URL) throws {
    guard let destination = CGImageDestinationCreateWithURL(
        url as CFURL,
        UTType.gif.identifier as CFString,
        42,
        nil
    ) else {
        throw ProviderError.message("无法创建动画预览。")
    }
    let gifProperties: [CFString: Any] = [
        kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]
    ]
    CGImageDestinationSetProperties(destination, gifProperties as CFDictionary)
    let frameProperties: [CFString: Any] = [
        kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFDelayTime: 1.0 / 30.0]
    ]

    for index in 0..<42 {
        let raw = CGFloat(index) / 41
        let progress = CGFloat(stagedExpansionProgress(Double(raw)))
        view.expansionProgress = min(1, progress)
        view.previewPhase = CGFloat(index) * 0.23
        let frameImage = NSImage(size: view.bounds.size)
        frameImage.lockFocus()
        NSColor(calibratedRed: 0.96, green: 0.975, blue: 1, alpha: 1).setFill()
        view.bounds.fill()
        view.draw(view.bounds)
        frameImage.unlockFocus()
        var proposedRect = view.bounds
        guard let image = frameImage.cgImage(
            forProposedRect: &proposedRect,
            context: nil,
            hints: nil
        ) else {
            throw ProviderError.message("无法编码动画帧。")
        }
        CGImageDestinationAddImage(destination, image, frameProperties as CFDictionary)
    }
    view.previewPhase = nil
    view.expansionProgress = 0.34
    guard CGImageDestinationFinalize(destination) else {
        throw ProviderError.message("无法写入动画预览。")
    }
}

func stagedExpansionProgress(_ rawValue: Double) -> Double {
    let raw = min(1, max(0, rawValue))
    func smooth(_ value: Double) -> Double {
        value * value * (3 - 2 * value)
    }
    if raw < 0.24 {
        return 0.16 * smooth(raw / 0.24)
    }
    if raw < 0.52 {
        return 0.16 + 0.28 * smooth((raw - 0.24) / 0.28)
    }
    let local = (raw - 0.52) / 0.48
    let polishedEase = 1 - pow(1 - local, 3)
    return 0.44 + 0.56 * polishedEase
}

func render(view: NSView, to url: URL) throws {
    guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
        throw ProviderError.message("无法创建预览位图。")
    }
    view.cacheDisplay(in: view.bounds, to: bitmap)
    guard let data = bitmap.representation(using: .png, properties: [:]) else {
        throw ProviderError.message("无法编码预览 PNG。")
    }
    try data.write(to: url, options: .atomic)
}

if CommandLine.arguments.contains("--claude-statusline") {
    exit(ClaudeQuotaProvider.runStatusLineBridge())
}
if CommandLine.arguments.contains("--probe") {
    exit(runProbe())
}
if let previewIndex = CommandLine.arguments.firstIndex(of: "--render-preview"),
   CommandLine.arguments.indices.contains(previewIndex + 1) {
    exit(renderPreview(to: URL(fileURLWithPath: CommandLine.arguments[previewIndex + 1], isDirectory: true)))
}

let application = NSApplication.shared
let delegate = AppDelegate()
application.delegate = delegate
application.run()
