import AppKit
import Foundation
import ImageIO
import UniformTypeIdentifiers

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var settings = SettingsStore.shared.load()
    private var state = OrbState()
    private var orbPanel: OrbPanel!
    private var detailPanel: DetailPanel!
    private var orbContainer: NSView!
    private var orbGlassView: LiquidGlassEffectView!
    private var orbView: OrbView!
    private var detailView: DetailView!
    private var detailContainer: NSView!
    private var detailGlassView: LiquidGlassEffectView!
    private var statusItem: NSStatusItem!
    private var menu: NSMenu!
    private var hideDetailWorkItem: DispatchWorkItem?
    private var detailAnimationTimer: Timer?
    private var refreshTimer: Timer?
    private var refreshGeneration = 0
    private var refreshInProgress = false
    private var refreshQueued = false
    private var detailRequested = false
    private var orbEnabled = true

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        var migratedSettings = false
        if settings.quotaWindow != .weekly {
            settings.quotaWindow = .weekly
            migratedSettings = true
        }
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
        refresh()
        refreshTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            self?.refresh()
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        refreshTimer?.invalidate()
        detailAnimationTimer?.invalidate()
        settings.orbX = Double(orbPanel.frame.origin.x)
        settings.orbY = Double(orbPanel.frame.origin.y)
        SettingsStore.shared.save(settings)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

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
        orbGlassView = LiquidGlassEffectView(frame: NSRect(x: 6, y: 8, width: 62, height: 62))
        orbGlassView.material = .underWindowBackground
        orbGlassView.blendingMode = .behindWindow
        orbGlassView.state = .active
        orbGlassView.isEmphasized = false
        orbGlassView.wantsLayer = true
        orbGlassView.layer?.cornerRadius = 31
        orbGlassView.layer?.masksToBounds = true
        orbGlassView.alphaValue = 1
        orbGlassView.updateRefraction(
            primitives: [.ellipse(NSRect(x: 0, y: 0, width: 62, height: 62))],
            strength: 13,
            edgeDepth: 11
        )
        orbView = OrbView(frame: NSRect(origin: .zero, size: size))
        orbView.animationsEnabled = settings.animationsEnabled
        orbView.mode = settings.quotaWindow
        orbView.onHoverChanged = { [weak self] hovered in
            hovered ? self?.showDetail() : self?.scheduleDetailHide()
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
        detailGlassView.material = .underWindowBackground
        detailGlassView.blendingMode = .behindWindow
        detailGlassView.state = .active
        detailGlassView.isEmphasized = false
        detailGlassView.alphaValue = 0
        detailGlassView.wantsLayer = true
        detailGlassView.layer?.masksToBounds = true
        detailView = DetailView(frame: NSRect(origin: .zero, size: detailSize))
        detailView.mode = settings.quotaWindow
        detailView.onHoverChanged = { [weak self] hovered in
            hovered ? self?.cancelDetailHide() : self?.scheduleDetailHide()
        }
        detailView.onRightClick = { [weak self] event in self?.showMenu(for: event) }
        detailView.onRefresh = { [weak self] in self?.refresh() }
        detailView.onPanelMoved = { [weak self] origin, committed in
            self?.syncOrbPosition(fromDetailOrigin: origin, committed: committed)
        }
        detailContainer.addSubview(detailGlassView)
        detailContainer.addSubview(detailView)
        detailPanel.contentView = detailContainer
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
        let codex = item("Codex", action: #selector(selectCodex))
        codex.state = settings.selectedAgent == .codex ? .on : .off
        sources.addItem(codex)
        let claude = item("Claude Code", action: #selector(selectClaude))
        claude.state = settings.selectedAgent == .claudeCode ? .on : .off
        sources.addItem(claude)
        let sourceRoot = NSMenuItem(title: "数据来源", action: nil, keyEquivalent: "")
        sourceRoot.submenu = sources
        menu.addItem(sourceRoot)

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

    private func showDetail() {
        cancelDetailHide()
        guard orbEnabled else { return }
        guard !detailRequested else { return }
        detailRequested = true
        let orb = orbPanel.frame
        let detailSize = detailPanel.frame.size
        let visible = (NSScreen.screens.first { $0.frame.intersects(orb) } ?? NSScreen.main)?.visibleFrame
        let opensRight = visible.map { orb.minX + detailSize.width <= $0.maxX } ?? true
        detailView.opensToRight = opensRight
        updateGlassFrame(progress: detailView.expansionProgress)
        var origin = NSPoint(
            x: opensRight ? orb.minX : orb.minX - 336,
            y: orb.minY - 66
        )
        if let visible {
            origin.x = min(max(origin.x, visible.minX), visible.maxX - detailSize.width)
            origin.y = min(max(origin.y, visible.minY), visible.maxY - detailSize.height)
        }
        detailPanel.setFrameOrigin(origin)
        detailPanel.orderFrontRegardless()
        animateDetail(to: 1)
    }

    private func scheduleDetailHide() {
        guard detailRequested else { return }
        cancelDetailHide()
        let work = DispatchWorkItem { [weak self] in self?.hideDetail() }
        hideDetailWorkItem = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.32, execute: work)
    }

    private func cancelDetailHide() {
        hideDetailWorkItem?.cancel()
        hideDetailWorkItem = nil
    }

    private func hideDetail() {
        guard detailRequested else { return }
        detailRequested = false
        animateDetail(to: 0) { [weak self] in
            self?.detailPanel.orderOut(nil)
            guard self?.orbEnabled == true else { return }
            self?.orbPanel.alphaValue = 1
            self?.orbPanel.orderFrontRegardless()
        }
    }

    private func animateDetail(to target: CGFloat, completion: (() -> Void)? = nil) {
        detailAnimationTimer?.invalidate()
        let start = detailView.expansionProgress
        if abs(start - target) < 0.001 {
            completion?()
            return
        }
        let startedAt = Date()
        let duration = target > start ? 1.02 : 0.32
        detailAnimationTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 60.0, repeats: true) { [weak self] timer in
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
            self.detailView.expansionProgress = value
            self.updateGlassFrame(progress: value)
            self.orbPanel.alphaValue = target > start ? max(0, 1 - value * 5.5) : min(1, 1 - value)
            if target > start, value > 0.22, self.orbPanel.isVisible {
                self.orbPanel.orderOut(nil)
            }
            if raw >= 1 {
                timer.invalidate()
                self.detailAnimationTimer = nil
                self.detailView.expansionProgress = target
                self.updateGlassFrame(progress: target)
                self.orbPanel.alphaValue = target > 0 ? 0 : 1
                if target <= 0 {
                    self.orbPanel.orderFrontRegardless()
                }
                completion?()
            }
        }
    }

    private func updateGlassFrame(progress: CGFloat) {
        guard detailGlassView != nil, detailView != nil else { return }
        let normalized = max(0, min(1, progress))
        let neckProgress = max(0, min(1, (normalized - 0.16) / 0.36))
        let cardProgress = max(0, min(1, (normalized - 0.42) / 0.58))
        let easedNeck = 1 - pow(1 - neckProgress, 3)
        let easedCard = cardProgress * cardProgress * (3 - 2 * cardProgress)
        let orbRect = detailView.opensToRight
            ? NSRect(x: 6, y: 74, width: 62, height: 62)
            : NSRect(x: detailView.bounds.maxX - 68, y: 74, width: 62, height: 62)
        let cardRect = detailView.opensToRight
            ? NSRect(x: 92, y: 12, width: 310, height: 186)
            : NSRect(x: 8, y: 12, width: 310, height: 186)
        let maskPath = CGMutablePath()
        maskPath.addEllipse(in: orbRect)
        var refractionPrimitives: [LiquidGlassPrimitive] = [.ellipse(orbRect)]

        if easedNeck > 0.001 && easedCard < 0.04 {
            let neckPath = makeLiquidNeckPath(
                orbRect: orbRect,
                cardRect: cardRect,
                opensToRight: detailView.opensToRight,
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
            let visibleCard = detailView.opensToRight
                ? CGRect(x: cardRect.minX, y: orbRect.midY - height / 2, width: width, height: height)
                : CGRect(x: cardRect.maxX - width, y: orbRect.midY - height / 2, width: width, height: height)
            let unifiedPath = makeUnifiedGlassPath(
                cardRect: visibleCard,
                orbRect: orbRect,
                opensToRight: detailView.opensToRight,
                progress: easedCard
            )
            maskPath.addPath(unifiedPath.compatibleCGPath())
            refractionPrimitives.append(.roundedRect(visibleCard, min(20, min(width, height) / 2)))
            refractionPrimitives.append(.roundedRect(unifiedPath.bounds, min(15, unifiedPath.bounds.height / 2)))
        }

        detailGlassView.frame = detailView.bounds
        let maskLayer = CAShapeLayer()
        maskLayer.frame = detailView.bounds
        maskLayer.path = maskPath
        detailGlassView.layer?.mask = maskLayer
        detailGlassView.updateRefraction(
            primitives: refractionPrimitives,
            strength: 18 + easedNeck * 8,
            edgeDepth: 16
        )
        detailGlassView.alphaValue = 1
    }

    private func syncOrbPosition(fromDetailOrigin origin: NSPoint, committed: Bool) {
        let detailWidth = detailPanel.frame.width
        let rawOrbOrigin = NSPoint(
            x: detailView.opensToRight ? origin.x : origin.x + detailWidth - 74,
            y: origin.y + 66
        )
        let orbOrigin = committed
            ? snapOrb(origin: rawOrbOrigin, size: orbPanel.frame.size)
            : rawOrbOrigin
        orbPanel.setFrameOrigin(orbOrigin)

        if committed {
            let alignedDetailOrigin = NSPoint(
                x: detailView.opensToRight ? orbOrigin.x : orbOrigin.x - detailWidth + 74,
                y: orbOrigin.y - 66
            )
            detailPanel.setFrameOrigin(alignedDetailOrigin)
            settings.orbX = Double(orbOrigin.x)
            settings.orbY = Double(orbOrigin.y)
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
        let source = settings.selectedAgent
        if state.updatedAt == nil {
            state = OrbState(sourceName: source == .codex ? "Codex 官方" : "Claude Code 官方", agentName: source.displayName)
            updateUI()
        }
        DispatchQueue.global(qos: .utility).async { [weak self] in
            do {
                let result = try ProviderCoordinator.read(source: source)
                DispatchQueue.main.async {
                    guard let self else { return }
                    if generation == self.refreshGeneration {
                        self.state = result
                        self.updateUI()
                    }
                    self.finishRefresh()
                }
            } catch {
                DispatchQueue.main.async {
                    guard let self else { return }
                    if generation == self.refreshGeneration {
                        if self.state.updatedAt == nil {
                            self.state = OrbState(
                                sourceName: source == .codex ? "Codex" : "Claude Code",
                                agentName: source.displayName,
                                risk: .error,
                                message: error.localizedDescription
                            )
                        } else {
                            self.state.message = "刷新失败：\(error.localizedDescription)"
                        }
                        self.updateUI()
                    }
                    self.finishRefresh()
                }
            }
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

    private func updateUI() {
        orbView?.state = state
        orbView?.mode = settings.quotaWindow
        detailView?.state = state
        detailView?.mode = settings.quotaWindow
        statusItem?.button?.image = makeMenuBarImage(color: state.risk.color)
        let display = state.displayText(mode: settings.quotaWindow)
        let suffix = state.balanceText == nil && state.selectedPercent(mode: settings.quotaWindow) != nil ? "%" : ""
        statusItem?.button?.title = " \(display)\(suffix)"
        statusItem?.button?.toolTip = summaryText()
        rebuildMenu()
    }

    private func summaryText() -> String {
        if let message = state.message { return "\(state.sourceName)：\(message)" }
        return "\(state.sourceName) · \(state.caption(mode: settings.quotaWindow)) \(state.displayText(mode: settings.quotaWindow))"
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
        cancelDetailHide()
        detailRequested = false
        detailAnimationTimer?.invalidate()
        detailAnimationTimer = nil
        detailView.expansionProgress = 0
        detailGlassView.alphaValue = 0
        detailPanel.orderOut(nil)
        if orbEnabled {
            orbPanel.alphaValue = 1
            orbPanel.orderFrontRegardless()
        } else {
            orbPanel.orderOut(nil)
        }
        rebuildMenu()
    }

    @objc private func selectCodex() { selectSource(.codex) }
    @objc private func selectClaude() { selectSource(.claudeCode) }

    private func selectSource(_ source: AgentSource) {
        settings.selectedAgent = source
        SettingsStore.shared.save(settings)
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
            message: "版本 1.2.15-mac.13 · Full-Bleed Glass Icon\n\n应用图标改为全画布液态玻璃，避免系统补灰色外框或形成双层方块；详情百分号已收紧，Token 统一使用万和亿。"
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

        let orb = OrbView(frame: NSRect(x: 0, y: 0, width: 74, height: 78))
        orb.animationsEnabled = false
        orb.mode = .weekly
        orb.state = previewState
        try render(view: orb, to: directory.appendingPathComponent("apple-orb-preview.png"))

        let appIcon = AppIconView(frame: NSRect(x: 0, y: 0, width: 512, height: 512))
        appIcon.state = previewState
        try render(view: appIcon, to: directory.appendingPathComponent("app-icon.png"))

        let detail = DetailView(frame: NSRect(x: 0, y: 0, width: 410, height: 210))
        detail.state = previewState
        detail.mode = .weekly
        detail.expansionProgress = 1
        detail.opensToRight = true
        try render(view: detail, to: directory.appendingPathComponent("apple-expanded-preview.png"))

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
