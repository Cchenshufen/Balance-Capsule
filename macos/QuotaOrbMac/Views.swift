import AppKit

final class OrbPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class DetailPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class OrbView: NSView {
    var state = OrbState() { didSet { needsDisplay = true } }
    var mode: QuotaWindowMode = .fiveHour { didSet { needsDisplay = true } }
    var animationsEnabled = true
    var onHoverChanged: ((Bool) -> Void)?
    var onPositionCommitted: ((NSPoint) -> Void)?
    var onRightClick: ((NSEvent) -> Void)?
    var onRefresh: (() -> Void)?

    private var tracking: NSTrackingArea?
    private var dragOffset = NSPoint.zero
    private var isDragging = false
    private var animationPhase: CGFloat = 0
    private var hoverIntensity: CGFloat = 0
    private var hoverTarget: CGFloat = 0
    private var animationTimer: Timer?

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor.clear.cgColor
        animationTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { [weak self] _ in
            guard let self, self.animationsEnabled else { return }
            self.animationPhase += 0.055
            self.hoverIntensity += (self.hoverTarget - self.hoverIntensity) * 0.16
            self.needsDisplay = true
        }
    }

    required init?(coder: NSCoder) { nil }
    deinit { animationTimer?.invalidate() }

    override func updateTrackingAreas() {
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(
            rect: bounds,
            options: [.mouseEnteredAndExited, .mouseMoved, .activeAlways, .inVisibleRect],
            owner: self
        )
        addTrackingArea(area)
        tracking = area
        super.updateTrackingAreas()
    }

    override func mouseEntered(with event: NSEvent) {
        hoverTarget = 1
        onHoverChanged?(true)
    }
    override func mouseMoved(with event: NSEvent) {
        hoverTarget = 1
        onHoverChanged?(true)
    }
    override func mouseExited(with event: NSEvent) {
        if !isDragging {
            hoverTarget = 0
            onHoverChanged?(false)
        }
    }
    override func rightMouseDown(with event: NSEvent) { onRightClick?(event) }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 {
            isDragging = false
            onRefresh?()
            return
        }
        guard let window else { return }
        isDragging = true
        dragOffset = event.locationInWindow
        window.orderFrontRegardless()
    }

    override func mouseDragged(with event: NSEvent) {
        guard isDragging, let window else { return }
        let point = NSEvent.mouseLocation
        window.setFrameOrigin(NSPoint(x: point.x - dragOffset.x, y: point.y - dragOffset.y))
    }

    override func mouseUp(with event: NSEvent) {
        guard isDragging, let window else { return }
        isDragging = false
        let snapped = snap(origin: window.frame.origin, size: window.frame.size)
        window.setFrameOrigin(snapped)
        onPositionCommitted?(snapped)
        onHoverChanged?(bounds.contains(convert(event.locationInWindow, from: nil)))
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        let bob = animationsEnabled ? sin(animationPhase * 0.72) * 0.65 : 0
        drawAppleOrb(
            state: state,
            mode: mode,
            in: NSRect(x: 6, y: 8 + bob, width: 62, height: 62),
            phase: animationPhase,
            interaction: hoverIntensity,
            showGlow: true
        )
    }

    private func snap(origin: NSPoint, size: NSSize) -> NSPoint {
        let center = NSPoint(x: origin.x + size.width / 2, y: origin.y + size.height / 2)
        let screen = NSScreen.screens.first { $0.frame.contains(center) } ?? NSScreen.main
        guard let visible = screen?.visibleFrame else { return origin }
        var result = NSPoint(
            x: min(max(origin.x, visible.minX), visible.maxX - size.width),
            y: min(max(origin.y, visible.minY), visible.maxY - size.height)
        )
        let distance: CGFloat = 14
        if abs(result.x - visible.minX) < distance { result.x = visible.minX }
        if abs((result.x + size.width) - visible.maxX) < distance { result.x = visible.maxX - size.width }
        if abs(result.y - visible.minY) < distance { result.y = visible.minY }
        if abs((result.y + size.height) - visible.maxY) < distance { result.y = visible.maxY - size.height }
        return result
    }
}

final class DetailView: NSView {
    var state = OrbState() { didSet { needsDisplay = true } }
    var mode: QuotaWindowMode = .fiveHour { didSet { needsDisplay = true } }
    var opensToRight = true { didSet { needsDisplay = true } }
    var expansionProgress: CGFloat = 0 { didSet { needsDisplay = true } }
    var previewPhase: CGFloat? { didSet { needsDisplay = true } }
    var onHoverChanged: ((Bool) -> Void)?
    var onRightClick: ((NSEvent) -> Void)?
    var onPanelMoved: ((NSPoint, Bool) -> Void)?
    var onRefresh: (() -> Void)?

    private var tracking: NSTrackingArea?
    private var phase: CGFloat = 0
    private var timer: Timer?
    private var dragOffset = NSPoint.zero
    private var isDragging = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor.clear.cgColor
        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { [weak self] _ in
            self?.phase += 0.035
            self?.needsDisplay = true
        }
    }

    required init?(coder: NSCoder) { nil }
    deinit { timer?.invalidate() }

    override func updateTrackingAreas() {
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(
            rect: bounds,
            options: [.mouseEnteredAndExited, .mouseMoved, .activeAlways, .inVisibleRect],
            owner: self
        )
        addTrackingArea(area)
        tracking = area
        super.updateTrackingAreas()
    }

    override func mouseEntered(with event: NSEvent) { onHoverChanged?(true) }
    override func mouseMoved(with event: NSEvent) { onHoverChanged?(true) }
    override func mouseExited(with event: NSEvent) { if !isDragging { onHoverChanged?(false) } }
    override func rightMouseDown(with event: NSEvent) { onRightClick?(event) }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        let orbHitRect = opensToRight
            ? NSRect(x: 0, y: 68, width: 74, height: 74)
            : NSRect(x: bounds.maxX - 74, y: 68, width: 74, height: 74)
        if event.clickCount == 2, orbHitRect.contains(point) {
            isDragging = false
            onRefresh?()
            return
        }
        guard let window else { return }
        isDragging = true
        dragOffset = event.locationInWindow
        onHoverChanged?(true)
        window.orderFrontRegardless()
    }

    override func mouseDragged(with event: NSEvent) {
        guard isDragging, let window else { return }
        let point = NSEvent.mouseLocation
        let origin = NSPoint(x: point.x - dragOffset.x, y: point.y - dragOffset.y)
        window.setFrameOrigin(origin)
        onPanelMoved?(origin, false)
    }

    override func mouseUp(with event: NSEvent) {
        guard isDragging, let window else { return }
        isDragging = false
        onPanelMoved?(window.frame.origin, true)
        onHoverChanged?(bounds.contains(convert(event.locationInWindow, from: nil)))
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        guard let context = NSGraphicsContext.current?.cgContext else { return }

        let orbRect = opensToRight
            ? NSRect(x: 6, y: 74, width: 62, height: 62)
            : NSRect(x: bounds.maxX - 68, y: 74, width: 62, height: 62)
        let cardRect = opensToRight
            ? NSRect(x: 92, y: 12, width: 310, height: 186)
            : NSRect(x: 8, y: 12, width: 310, height: 186)
        let normalizedProgress = max(0, min(1, expansionProgress))
        let reactionProgress = min(1, normalizedProgress / 0.22)
        let neckProgress = max(0, min(1, (normalizedProgress - 0.16) / 0.36))
        let cardProgress = max(0, min(1, (normalizedProgress - 0.42) / 0.58))
        let easedNeck = 1 - pow(1 - neckProgress, 3)
        let easedCard = cardProgress * cardProgress * (3 - 2 * cardProgress)

        context.saveGState()
        drawConnectedGlass(
            cardRect: cardRect,
            orbRect: orbRect,
            opensToRight: opensToRight,
            neckProgress: easedNeck,
            cardProgress: easedCard
        )

        let contentOpacity = max(0, min(1, (cardProgress - 0.48) / 0.52))
        if contentOpacity > 0 {
            context.saveGState()
            context.setAlpha(contentOpacity)
            drawCardContent(in: cardRect)
            context.restoreGState()
        }
        drawAppleOrb(
            state: state,
            mode: mode,
            in: orbRect,
            phase: previewPhase ?? phase,
            interaction: reactionProgress,
            showGlow: true
        )
        context.restoreGState()
    }

    private func drawConnectedGlass(
        cardRect: NSRect,
        orbRect: NSRect,
        opensToRight: Bool,
        neckProgress: CGFloat,
        cardProgress: CGFloat
    ) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        context.saveGState()

        if neckProgress > 0.001 && cardProgress < 0.04 {
            let capsule = makeLiquidNeckPath(
                orbRect: orbRect,
                cardRect: cardRect,
                opensToRight: opensToRight,
                neckProgress: neckProgress,
                cardProgress: cardProgress
            )
            let capsuleGradient = NSGradient(
                starting: NSColor.white.withAlphaComponent(0.19),
                ending: NSColor(calibratedRed: 0.63, green: 0.86, blue: 1, alpha: 0.045)
            )!
            capsuleGradient.draw(in: capsule, angle: opensToRight ? 0 : 180)
            NSColor.white.withAlphaComponent(0.72).setStroke()
            capsule.lineWidth = 0.75
            capsule.stroke()
        }

        guard cardProgress > 0.001 else {
            context.restoreGState()
            return
        }
        let visibleWidth = max(2, cardRect.width * cardProgress)
        let visibleHeight = 26 + (cardRect.height - 26) * cardProgress
        let visibleCard = opensToRight
            ? NSRect(x: cardRect.minX, y: orbRect.midY - visibleHeight / 2, width: visibleWidth, height: visibleHeight)
            : NSRect(x: cardRect.maxX - visibleWidth, y: orbRect.midY - visibleHeight / 2, width: visibleWidth, height: visibleHeight)

        let card = makeUnifiedGlassPath(
            cardRect: visibleCard,
            orbRect: orbRect,
            opensToRight: opensToRight,
            progress: cardProgress
        )
        context.setShadow(
            offset: CGSize(width: 0, height: -8),
            blur: 22,
            color: NSColor(calibratedRed: 0.14, green: 0.34, blue: 0.52, alpha: 0.15).cgColor
        )
        let fill = NSGradient(
            starting: NSColor(calibratedWhite: 1, alpha: 0.13),
            ending: NSColor(calibratedRed: 0.78, green: 0.90, blue: 1.0, alpha: 0.035)
        )!
        fill.draw(in: card, angle: 125)
        context.setShadow(offset: .zero, blur: 0, color: nil)

        NSColor.white.withAlphaComponent(0.72).setStroke()
        card.lineWidth = 1.0
        card.stroke()
        context.saveGState()
        context.translateBy(x: -0.55, y: 0)
        NSColor(calibratedRed: 0.22, green: 0.76, blue: 1, alpha: 0.16).setStroke()
        card.lineWidth = 0.65
        card.stroke()
        context.restoreGState()
        context.saveGState()
        context.translateBy(x: 0.55, y: 0)
        NSColor(calibratedRed: 1, green: 0.38, blue: 0.52, alpha: 0.09).setStroke()
        card.lineWidth = 0.55
        card.stroke()
        context.restoreGState()
        let topHighlight = NSBezierPath()
        topHighlight.move(to: NSPoint(x: visibleCard.minX + min(20, visibleCard.width * 0.15), y: visibleCard.maxY - 3.5))
        topHighlight.line(to: NSPoint(x: visibleCard.maxX - min(20, visibleCard.width * 0.15), y: visibleCard.maxY - 3.5))
        NSColor.white.withAlphaComponent(0.72).setStroke()
        topHighlight.lineWidth = 0.8
        topHighlight.stroke()
        context.restoreGState()
    }

    private func drawCardContent(in card: NSRect) {
        let leading = opensToRight ? card.minX + 30 : card.minX + 24
        let contentWidth = card.width - 54
        drawText(
            "Balance Capsule",
            in: NSRect(x: leading, y: card.maxY - 36, width: 170, height: 24),
            font: .systemFont(ofSize: 17, weight: .regular),
            color: appleInk,
            alignment: .left
        )
        drawStatusDot(at: NSPoint(x: card.maxX - 22, y: card.maxY - 22))
        drawAgentPill(in: NSRect(x: leading, y: card.maxY - 61, width: 78, height: 22))

        if let balance = state.balanceText {
            drawText(
                balance,
                in: NSRect(x: leading, y: card.maxY - 114, width: contentWidth, height: 40),
                font: .systemFont(ofSize: balance.count > 12 ? 20 : 27, weight: .regular),
                color: appleInk,
                alignment: .left
            )
            drawText(
                state.balanceCaption ?? "Balance",
                in: NSRect(x: leading, y: card.maxY - 137, width: contentWidth, height: 16),
                font: .systemFont(ofSize: 10.5, weight: .medium),
                color: appleSecondary,
                alignment: .left
            )
        } else if state.risk == .error {
            drawText(
                state.message ?? "Unable to read quota",
                in: NSRect(x: leading, y: card.maxY - 136, width: contentWidth, height: 42),
                font: .systemFont(ofSize: 11.5, weight: .medium),
                color: NSColor(calibratedRed: 0.58, green: 0.27, blue: 0.31, alpha: 1),
                alignment: .left
            )
        } else {
            let selected = state.selectedPercent(mode: mode) ?? 0
            let percentText = "\(Int(selected.rounded()))"
            let percentFont = NSFont.systemFont(ofSize: 34, weight: .regular)
            let percentWidth = ceil((percentText as NSString).size(withAttributes: [.font: percentFont]).width)
            drawText(
                percentText,
                in: NSRect(x: leading, y: card.maxY - 111, width: percentWidth + 2, height: 40),
                font: percentFont,
                color: appleInk,
                alignment: .left
            )
            drawText(
                "%",
                in: NSRect(x: leading + percentWidth + 4, y: card.maxY - 106, width: 24, height: 28),
                font: .systemFont(ofSize: 19, weight: .regular),
                color: appleInk,
                alignment: .left
            )

            NSColor.white.withAlphaComponent(0.82).setFill()
            NSRect(x: leading, y: card.maxY - 116, width: contentWidth, height: 0.65).fill()
            if let usage = state.tokenUsage {
                drawTokenUsage(
                    usage,
                    in: NSRect(
                        x: leading + 96,
                        y: card.maxY - 109,
                        width: contentWidth - 96,
                        height: 34
                    )
                )
                drawProgressRow(
                    label: "Week",
                    value: state.weekly?.remainingPercent,
                    y: card.maxY - 151,
                    leading: leading,
                    width: contentWidth
                )
            } else {
                drawProgressRow(
                    label: "5h",
                    value: state.fiveHour?.remainingPercent,
                    y: card.maxY - 143,
                    leading: leading,
                    width: contentWidth
                )
                drawProgressRow(
                    label: "Week",
                    value: state.weekly?.remainingPercent,
                    y: card.maxY - 166,
                    leading: leading,
                    width: contentWidth
                )
            }
        }

        if (state.balanceText != nil || state.risk == .error), let usage = state.tokenUsage {
            drawTokenUsage(
                usage,
                in: NSRect(x: leading, y: card.minY + 23, width: contentWidth, height: 27)
            )
        }

        let updateText: String
        if let updated = state.updatedAt {
            let formatter = DateFormatter()
            formatter.dateFormat = "HH:mm"
            updateText = "Updated \(formatter.string(from: updated))"
        } else {
            updateText = "Waiting for update"
        }
        drawText(
            updateText,
            in: NSRect(x: leading, y: card.minY + 5, width: 130, height: 14),
            font: .systemFont(ofSize: 9.5, weight: .regular),
            color: appleSecondary,
            alignment: .left
        )
    }

    private func drawAgentPill(in rect: NSRect) {
        NSColor.white.withAlphaComponent(0.44).setFill()
        let pill = NSBezierPath(roundedRect: rect, xRadius: rect.height / 2, yRadius: rect.height / 2)
        pill.fill()
        NSColor.white.withAlphaComponent(0.82).setStroke()
        pill.lineWidth = 0.75
        pill.stroke()

        let blue = NSColor(calibratedRed: 0.20, green: 0.57, blue: 0.98, alpha: 1)
        blue.setStroke()
        for offset in stride(from: CGFloat(0), through: 5, by: 2.5) {
            let layer = NSBezierPath()
            layer.move(to: NSPoint(x: rect.minX + 9, y: rect.midY + 3 - offset))
            layer.line(to: NSPoint(x: rect.minX + 14, y: rect.midY + 6 - offset))
            layer.line(to: NSPoint(x: rect.minX + 19, y: rect.midY + 3 - offset))
            layer.lineWidth = 1.2
            layer.lineCapStyle = .round
            layer.lineJoinStyle = .round
            layer.stroke()
        }
        drawText(
            state.agentName,
            in: NSRect(x: rect.minX + 24, y: rect.minY + 4, width: rect.width - 28, height: 15),
            font: .systemFont(ofSize: 10.5, weight: .medium),
            color: NSColor(calibratedRed: 0.23, green: 0.32, blue: 0.46, alpha: 1),
            alignment: .left
        )
    }

    private func drawStatusDot(at point: NSPoint) {
        let color = state.risk == .safe
            ? NSColor(calibratedRed: 1.0, green: 0.60, blue: 0.32, alpha: 1)
            : state.risk.color
        color.setFill()
        NSBezierPath(ovalIn: NSRect(x: point.x - 3.5, y: point.y - 3.5, width: 7, height: 7)).fill()
        NSColor.white.withAlphaComponent(0.76).setStroke()
        let ring = NSBezierPath(ovalIn: NSRect(x: point.x - 4.5, y: point.y - 4.5, width: 9, height: 9))
        ring.lineWidth = 0.7
        ring.stroke()
    }

    private func drawProgressRow(
        label: String,
        value: Double?,
        y: CGFloat,
        leading: CGFloat,
        width: CGFloat
    ) {
        let percent = max(0, min(100, value ?? 0))
        drawText(
            label,
            in: NSRect(x: leading, y: y, width: 36, height: 17),
            font: .systemFont(ofSize: 11.5, weight: .regular),
            color: appleInk,
            alignment: .left
        )
        let track = NSRect(x: leading + 45, y: y + 6, width: width - 82, height: 5)
        NSColor(calibratedRed: 0.76, green: 0.81, blue: 0.88, alpha: 0.48).setFill()
        NSBezierPath(roundedRect: track, xRadius: 2.5, yRadius: 2.5).fill()
        let fill = NSRect(x: track.minX, y: track.minY, width: track.width * CGFloat(percent / 100), height: track.height)
        let fillPath = NSBezierPath(roundedRect: fill, xRadius: 2.5, yRadius: 2.5)
        let fillGradient = NSGradient(
            starting: NSColor(calibratedRed: 0.24, green: 0.77, blue: 0.98, alpha: 0.92),
            ending: NSColor(calibratedRed: 0.18, green: 0.49, blue: 0.94, alpha: 0.92)
        )!
        fillGradient.draw(in: fillPath, angle: 0)
        NSColor.white.withAlphaComponent(0.46).setStroke()
        fillPath.lineWidth = 0.45
        fillPath.stroke()
        drawText(
            value == nil ? "—" : "\(Int(percent.rounded()))%",
            in: NSRect(x: leading + width - 34, y: y, width: 34, height: 17),
            font: .systemFont(ofSize: 11.5, weight: .regular),
            color: appleInk,
            alignment: .right
        )
    }

    private func drawTokenUsage(_ usage: TokenUsageSummary, in rect: NSRect) {
        let values = [
            ("今日", compactTokenCount(usage.todayTokens)),
            ("本月", compactTokenCount(usage.monthTokens)),
            ("总计", compactTokenCount(usage.totalTokens))
        ]
        let columnWidth = rect.width / CGFloat(values.count)
        for (index, item) in values.enumerated() {
            let column = NSRect(
                x: rect.minX + CGFloat(index) * columnWidth,
                y: rect.minY,
                width: columnWidth,
                height: rect.height
            )
            if index > 0 {
                NSColor.white.withAlphaComponent(0.50).setFill()
                NSRect(x: column.minX, y: column.minY + 3, width: 0.55, height: column.height - 6).fill()
            }
            drawText(
                item.1,
                in: NSRect(x: column.minX + 2, y: column.minY + 13, width: column.width - 4, height: 15),
                font: .systemFont(ofSize: 10.2, weight: .semibold),
                color: appleInk,
                alignment: .center
            )
            drawText(
                item.0,
                in: NSRect(x: column.minX + 2, y: column.minY, width: column.width - 4, height: 12),
                font: .systemFont(ofSize: 7.8, weight: .medium),
                color: appleSecondary,
                alignment: .center
            )
        }
    }
}

func makeLiquidNeckPath(
    orbRect: NSRect,
    cardRect: NSRect,
    opensToRight: Bool,
    neckProgress: CGFloat,
    cardProgress: CGFloat
) -> NSBezierPath {
    let direction: CGFloat = opensToRight ? 1 : -1
    let startX = opensToRight ? orbRect.midX + 17 : orbRect.midX - 17
    let destinationX = opensToRight ? cardRect.minX + 14 : cardRect.maxX - 14
    let tipX = startX + (destinationX - startX) * neckProgress
    let travel = abs(tipX - startX)
    let pulse = sin(neckProgress * .pi)
    let startHalfHeight = 11 + pulse * 4 + cardProgress * 9
    let tipHalfHeight = 6 + pulse * 9 + cardProgress * 29
    let shoulderX = startX + direction * travel * 0.22
    let waistX = startX + direction * travel * 0.52
    let approachX = tipX - direction * travel * 0.18
    let waistHalfHeight = 5 + pulse * 3 + cardProgress * 4
    let tipBulge = direction * (5 + pulse * 7) * (1 - cardProgress * 0.55)
    let centerY = orbRect.midY

    let path = NSBezierPath()
    path.move(to: NSPoint(x: startX, y: centerY + startHalfHeight))
    path.curve(
        to: NSPoint(x: waistX, y: centerY + waistHalfHeight),
        controlPoint1: NSPoint(x: shoulderX, y: centerY + startHalfHeight + pulse * 3),
        controlPoint2: NSPoint(x: waistX - direction * travel * 0.14, y: centerY + waistHalfHeight)
    )
    path.curve(
        to: NSPoint(x: tipX, y: centerY + tipHalfHeight),
        controlPoint1: NSPoint(x: waistX + direction * travel * 0.16, y: centerY + waistHalfHeight),
        controlPoint2: NSPoint(x: approachX, y: centerY + tipHalfHeight)
    )
    path.curve(
        to: NSPoint(x: tipX, y: centerY - tipHalfHeight),
        controlPoint1: NSPoint(x: tipX + tipBulge, y: centerY + tipHalfHeight * 0.55),
        controlPoint2: NSPoint(x: tipX + tipBulge, y: centerY - tipHalfHeight * 0.55)
    )
    path.curve(
        to: NSPoint(x: waistX, y: centerY - waistHalfHeight),
        controlPoint1: NSPoint(x: approachX, y: centerY - tipHalfHeight),
        controlPoint2: NSPoint(x: waistX + direction * travel * 0.16, y: centerY - waistHalfHeight)
    )
    path.curve(
        to: NSPoint(x: startX, y: centerY - startHalfHeight),
        controlPoint1: NSPoint(x: waistX - direction * travel * 0.14, y: centerY - waistHalfHeight),
        controlPoint2: NSPoint(x: shoulderX, y: centerY - startHalfHeight - pulse * 3)
    )
    path.curve(
        to: NSPoint(x: startX, y: centerY + startHalfHeight),
        controlPoint1: NSPoint(x: startX - direction * 4, y: centerY - startHalfHeight * 0.45),
        controlPoint2: NSPoint(x: startX - direction * 4, y: centerY + startHalfHeight * 0.45)
    )
    path.close()
    return path
}

func makeUnifiedGlassPath(
    cardRect: NSRect,
    orbRect: NSRect,
    opensToRight: Bool,
    progress: CGFloat
) -> NSBezierPath {
    if !opensToRight {
        let minimumX = min(cardRect.minX, orbRect.minX)
        let maximumX = max(cardRect.maxX, orbRect.maxX)
        let axis = (minimumX + maximumX) / 2
        func mirrored(_ rect: NSRect) -> NSRect {
            NSRect(
                x: axis * 2 - rect.maxX,
                y: rect.minY,
                width: rect.width,
                height: rect.height
            )
        }
        let path = makeUnifiedGlassPath(
            cardRect: mirrored(cardRect),
            orbRect: mirrored(orbRect),
            opensToRight: true,
            progress: progress
        )
        let transform = AffineTransform(
            m11: -1,
            m12: 0,
            m21: 0,
            m22: 1,
            tX: axis * 2,
            tY: 0
        )
        path.transform(using: transform)
        return path
    }

    let radius = min(20, min(cardRect.width, cardRect.height) / 2)
    let centerY = orbRect.midY
    let connectionHalfHeight = min(
        max(8, cardRect.height / 2 - radius - 4),
        10 + progress * 28
    )
    let orbHalfHeight = 8 + progress * 13
    let orbJoinX = orbRect.maxX - 4
    let path = NSBezierPath()

    path.move(to: NSPoint(x: cardRect.minX + radius, y: cardRect.maxY))
    path.line(to: NSPoint(x: cardRect.maxX - radius, y: cardRect.maxY))
    path.appendArc(
        withCenter: NSPoint(x: cardRect.maxX - radius, y: cardRect.maxY - radius),
        radius: radius,
        startAngle: 90,
        endAngle: 0,
        clockwise: true
    )
    path.line(to: NSPoint(x: cardRect.maxX, y: cardRect.minY + radius))
    path.appendArc(
        withCenter: NSPoint(x: cardRect.maxX - radius, y: cardRect.minY + radius),
        radius: radius,
        startAngle: 0,
        endAngle: -90,
        clockwise: true
    )
    path.line(to: NSPoint(x: cardRect.minX + radius, y: cardRect.minY))
    path.appendArc(
        withCenter: NSPoint(x: cardRect.minX + radius, y: cardRect.minY + radius),
        radius: radius,
        startAngle: 270,
        endAngle: 180,
        clockwise: true
    )
    path.line(to: NSPoint(x: cardRect.minX, y: centerY - connectionHalfHeight))
    path.curve(
        to: NSPoint(x: orbJoinX, y: centerY - orbHalfHeight),
        controlPoint1: NSPoint(x: cardRect.minX - 3, y: centerY - connectionHalfHeight * 0.58),
        controlPoint2: NSPoint(x: orbRect.maxX + 17, y: centerY - orbHalfHeight * 0.78)
    )
    path.curve(
        to: NSPoint(x: orbJoinX, y: centerY + orbHalfHeight),
        controlPoint1: NSPoint(x: orbRect.maxX + 4, y: centerY - orbHalfHeight * 0.34),
        controlPoint2: NSPoint(x: orbRect.maxX + 4, y: centerY + orbHalfHeight * 0.34)
    )
    path.curve(
        to: NSPoint(x: cardRect.minX, y: centerY + connectionHalfHeight),
        controlPoint1: NSPoint(x: orbRect.maxX + 17, y: centerY + orbHalfHeight * 0.78),
        controlPoint2: NSPoint(x: cardRect.minX - 3, y: centerY + connectionHalfHeight * 0.58)
    )
    path.line(to: NSPoint(x: cardRect.minX, y: cardRect.maxY - radius))
    path.appendArc(
        withCenter: NSPoint(x: cardRect.minX + radius, y: cardRect.maxY - radius),
        radius: radius,
        startAngle: 180,
        endAngle: 90,
        clockwise: true
    )
    path.close()
    return path
}

extension NSBezierPath {
    func compatibleCGPath() -> CGPath {
        let result = CGMutablePath()
        var points = [NSPoint](repeating: .zero, count: 3)
        for index in 0..<elementCount {
            switch element(at: index, associatedPoints: &points) {
            case .moveTo:
                result.move(to: points[0])
            case .lineTo:
                result.addLine(to: points[0])
            case .curveTo:
                result.addCurve(to: points[2], control1: points[0], control2: points[1])
            case .cubicCurveTo:
                result.addCurve(to: points[2], control1: points[0], control2: points[1])
            case .quadraticCurveTo:
                result.addQuadCurve(to: points[1], control: points[0])
            case .closePath:
                result.closeSubpath()
            @unknown default:
                break
            }
        }
        return result
    }
}

private let appleInk = NSColor(calibratedRed: 0.05, green: 0.15, blue: 0.29, alpha: 1)
private let appleSecondary = NSColor(calibratedRed: 0.42, green: 0.51, blue: 0.65, alpha: 1)

private func compactTokenCount(_ value: Int64) -> String {
    let amount = Double(max(0, value))
    if amount >= 100_000_000 { return chineseTokenAmount(amount / 100_000_000) + "亿" }
    return chineseTokenAmount(amount / 10_000) + "万"
}

private func chineseTokenAmount(_ value: Double) -> String {
    let precision = value >= 100 ? 0 : (value >= 10 ? 1 : 2)
    var text = String(format: "%.*f", precision, value)
    while text.contains(".") && text.last == "0" { text.removeLast() }
    if text.last == "." { text.removeLast() }
    return text
}

final class AppIconView: NSView {
    var state = OrbState()

    override var isFlipped: Bool { false }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        let plateRect = bounds.insetBy(dx: 3, dy: 3)
        let plate = NSBezierPath(roundedRect: plateRect, xRadius: 112, yRadius: 112)

        context.saveGState()
        let fullBleed = NSGradient(colors: [
            NSColor(calibratedRed: 0.96, green: 0.995, blue: 1, alpha: 1),
            NSColor(calibratedRed: 0.73, green: 0.91, blue: 1, alpha: 1),
            NSColor(calibratedRed: 0.84, green: 0.82, blue: 1, alpha: 1)
        ])!
        fullBleed.draw(in: bounds, angle: 126)

        context.saveGState()
        plate.addClip()
        let base = NSGradient(colors: [
            NSColor(calibratedRed: 1, green: 1, blue: 1, alpha: 0.72),
            NSColor(calibratedRed: 0.66, green: 0.88, blue: 1, alpha: 0.42),
            NSColor(calibratedRed: 0.92, green: 0.82, blue: 1, alpha: 0.30)
        ])!
        base.draw(in: plate, angle: 128)

        let upperLens = NSBezierPath(ovalIn: NSRect(
            x: plateRect.minX - 92,
            y: plateRect.midY + 6,
            width: plateRect.width + 172,
            height: plateRect.height * 0.70
        ))
        let upperGradient = NSGradient(
            starting: NSColor.white.withAlphaComponent(0.66),
            ending: NSColor.white.withAlphaComponent(0.02)
        )!
        upperGradient.draw(in: upperLens, angle: 90)

        let prism = NSBezierPath()
        prism.move(to: NSPoint(x: plateRect.minX - 18, y: plateRect.minY + 86))
        prism.line(to: NSPoint(x: plateRect.maxX + 24, y: plateRect.maxY - 42))
        prism.line(to: NSPoint(x: plateRect.maxX + 24, y: plateRect.maxY + 58))
        prism.line(to: NSPoint(x: plateRect.minX + 64, y: plateRect.minY - 18))
        prism.close()
        NSColor(calibratedRed: 0.55, green: 0.86, blue: 1, alpha: 0.13).setFill()
        prism.fill()
        context.restoreGState()

        NSColor.white.withAlphaComponent(0.92).setStroke()
        plate.lineWidth = 7
        plate.stroke()
        let innerPlate = NSBezierPath(
            roundedRect: plateRect.insetBy(dx: 10, dy: 10),
            xRadius: 102,
            yRadius: 102
        )
        NSColor(calibratedRed: 0.28, green: 0.68, blue: 0.94, alpha: 0.28).setStroke()
        innerPlate.lineWidth = 2
        innerPlate.stroke()

        let highlight = NSBezierPath()
        highlight.appendArc(
            withCenter: NSPoint(x: plateRect.midX, y: plateRect.midY),
            radius: plateRect.width / 2 - 22,
            startAngle: 28,
            endAngle: 150,
            clockwise: false
        )
        NSColor.white.withAlphaComponent(0.88).setStroke()
        highlight.lineWidth = 5
        highlight.lineCapStyle = .round
        highlight.stroke()

        let orbScale: CGFloat = 4.75
        context.translateBy(
            x: bounds.midX - 31 * orbScale,
            y: bounds.midY - 31 * orbScale + 2
        )
        context.scaleBy(x: orbScale, y: orbScale)
        drawAppleOrb(
            state: state,
            mode: .weekly,
            in: NSRect(x: 0, y: 0, width: 62, height: 62),
            phase: 0.7,
            interaction: 0.35,
            showGlow: true
        )
        context.restoreGState()
    }
}

private func drawAppleOrb(
    state: OrbState,
    mode: QuotaWindowMode,
    in rect: NSRect,
    phase: CGFloat,
    interaction: CGFloat,
    showGlow: Bool
) {
    guard let context = NSGraphicsContext.current?.cgContext else { return }
    context.saveGState()

    if showGlow {
        let floorGlow = NSBezierPath(ovalIn: NSRect(
            x: rect.minX + rect.width * 0.20,
            y: rect.minY - 4,
            width: rect.width * 0.60,
            height: 5
        ))
        context.setShadow(
            offset: CGSize(width: 0, height: -3),
            blur: 7 + interaction * 5,
            color: NSColor(calibratedRed: 0.12, green: 0.72, blue: 1, alpha: 0.14 + interaction * 0.12).cgColor
        )
        NSColor(calibratedRed: 0.35, green: 0.78, blue: 1, alpha: 0.025).setFill()
        floorGlow.fill()
        context.setShadow(offset: .zero, blur: 0, color: nil)
    }

    let lens = NSBezierPath(ovalIn: rect)
    let lensGradient = NSGradient(
        starting: NSColor(calibratedWhite: 1, alpha: 0.52),
        ending: NSColor(calibratedRed: 0.55, green: 0.82, blue: 1, alpha: 0.18)
    )!
    lensGradient.draw(in: lens, relativeCenterPosition: NSPoint(x: -0.24, y: 0.28))

    context.saveGState()
    lens.addClip()
    let chamber = rect.insetBy(dx: 6.2, dy: 6.2)
    NSColor(calibratedRed: 0.95, green: 0.985, blue: 1, alpha: 0.18).setFill()
    NSBezierPath(ovalIn: chamber).fill()

    let selected = state.selectedPercent(mode: mode)
    let fraction: CGFloat
    if state.risk == .error {
        fraction = 0.72
    } else if let selected {
        fraction = CGFloat(max(0.08, min(1, selected / 100)))
    } else {
        fraction = 0.55
    }
    let liquidTop = chamber.minY + chamber.height * fraction
    let liquidRect = NSRect(x: chamber.minX - 8, y: chamber.minY - 4, width: chamber.width + 16, height: liquidTop - chamber.minY + 5)
    let liquidColor = state.risk == .safe || state.risk == .loading
        ? NSColor(calibratedRed: 0.27, green: 0.72, blue: 0.98, alpha: 1)
        : state.risk.color
    let liquidGradient = NSGradient(
        starting: liquidColor.withAlphaComponent(0.56),
        ending: NSColor(calibratedRed: 0.67, green: 0.92, blue: 1, alpha: 0.26)
    )!
    liquidGradient.draw(in: liquidRect, angle: 90)

    let wave = NSBezierPath()
    wave.move(to: NSPoint(x: chamber.minX - 10, y: liquidTop))
    for index in 0...20 {
        let x = chamber.minX - 10 + CGFloat(index) * (chamber.width + 20) / 20
        let xRatio = CGFloat(index) / 20
        let oscillation = sin(CGFloat(index) * 0.68 + phase * (2.2 + interaction * 1.8))
        let tilt = (xRatio - 0.5) * sin(phase * 1.45) * interaction * 5.5
        let y = liquidTop + oscillation * (1.05 + interaction * 2.2) + tilt
        wave.line(to: NSPoint(x: x, y: y))
    }
    NSColor(calibratedRed: 0.12, green: 0.63, blue: 0.96, alpha: 0.72).setStroke()
    wave.lineWidth = 1.0
    wave.stroke()
    let highlightWave = wave.copy() as! NSBezierPath
    let transform = AffineTransform(translationByX: 0, byY: 1.4)
    highlightWave.transform(using: transform)
    NSColor.white.withAlphaComponent(0.72).setStroke()
    highlightWave.lineWidth = 0.7
    highlightWave.stroke()

    for bubble in bubbleRects(in: chamber, fraction: fraction, phase: phase) {
        NSColor.white.withAlphaComponent(0.52).setFill()
        NSBezierPath(ovalIn: bubble).fill()
        NSColor(calibratedRed: 0.15, green: 0.61, blue: 0.92, alpha: 0.36).setStroke()
        let outline = NSBezierPath(ovalIn: bubble)
        outline.lineWidth = 0.6
        outline.stroke()
    }

    if interaction > 0.01 {
        let sweep = (sin(phase * 1.4) + 1) * 0.5
        let sweepRect = NSRect(
            x: chamber.minX - 8 + (chamber.width + 16) * sweep,
            y: chamber.minY - 6,
            width: 6 + interaction * 5,
            height: chamber.height + 12
        )
        context.setShadow(
            offset: .zero,
            blur: 7,
            color: NSColor.white.withAlphaComponent(0.34 * interaction).cgColor
        )
        NSColor.white.withAlphaComponent(0.10 * interaction).setFill()
        NSBezierPath(ovalIn: sweepRect).fill()
        context.setShadow(offset: .zero, blur: 0, color: nil)
    }
    context.restoreGState()

    NSColor.white.withAlphaComponent(0.92).setStroke()
    lens.lineWidth = 1.25
    lens.stroke()
    NSColor(calibratedRed: 0.36, green: 0.64, blue: 0.86, alpha: 0.40).setStroke()
    let middleRing = NSBezierPath(ovalIn: rect.insetBy(dx: 2.8, dy: 2.8))
    middleRing.lineWidth = 0.65
    middleRing.stroke()
    NSColor.white.withAlphaComponent(0.70).setStroke()
    let innerRing = NSBezierPath(ovalIn: rect.insetBy(dx: 5.7, dy: 5.7))
    innerRing.lineWidth = 0.6
    innerRing.stroke()

    drawPrecisionTicks(center: NSPoint(x: rect.midX, y: rect.midY), radius: rect.width / 2 - 8.2)

    let specularArc = NSBezierPath()
    specularArc.appendArc(
        withCenter: NSPoint(x: rect.midX, y: rect.midY),
        radius: rect.width / 2 - 2,
        startAngle: 108,
        endAngle: 212,
        clockwise: false
    )
    NSColor.white.withAlphaComponent(0.72).setStroke()
    specularArc.lineWidth = 1.0
    specularArc.lineCapStyle = .round
    specularArc.stroke()

    let text = state.displayText(mode: mode)
    let suffix = state.balanceText == nil && selected != nil ? "%" : ""
    let fullText = text + suffix
    let fontSize: CGFloat = state.balanceText == nil ? 15 : (fullText.count > 10 ? 6.2 : 8.5)
    drawText(
        fullText,
        in: NSRect(x: rect.minX + 8, y: rect.midY - 9, width: rect.width - 16, height: 20),
        font: .systemFont(ofSize: fontSize, weight: .regular),
        color: appleInk,
        alignment: .center
    )
    context.restoreGState()
}

private func drawPrecisionTicks(center: NSPoint, radius: CGFloat) {
    for index in 0..<52 {
        let angle = CGFloat(index) / 52 * .pi * 2
        let major = index % 13 == 0
        let emphasized = index > 7 && index < 19
        let innerRadius = radius - (major ? 2.8 : 1.5)
        let inner = NSPoint(x: center.x + cos(angle) * innerRadius, y: center.y + sin(angle) * innerRadius)
        let outer = NSPoint(x: center.x + cos(angle) * radius, y: center.y + sin(angle) * radius)
        let color = emphasized
            ? NSColor(calibratedRed: 0.08, green: 0.63, blue: 1, alpha: 0.82)
            : NSColor(calibratedRed: 0.28, green: 0.54, blue: 0.76, alpha: 0.38)
        color.setStroke()
        let path = NSBezierPath()
        path.move(to: inner)
        path.line(to: outer)
        path.lineWidth = major ? 1.0 : 0.45
        path.stroke()
    }
}

private func bubbleRects(
    in chamber: NSRect,
    fraction: CGFloat,
    phase: CGFloat
) -> [NSRect] {
    let points: [(CGFloat, CGFloat, CGFloat)] = [
        (0.24, 0.26, 2.6), (0.42, 0.42, 2.0), (0.62, 0.24, 3.0),
        (0.76, 0.52, 2.2), (0.31, 0.61, 1.8), (0.56, 0.70, 2.4)
    ]
    return points.compactMap { x, y, size in
        let animatedY = (y + (sin(phase + x * 8) + 1) * 0.025).truncatingRemainder(dividingBy: 0.88)
        guard animatedY < fraction - 0.06 else { return nil }
        return NSRect(
            x: chamber.minX + chamber.width * x,
            y: chamber.minY + chamber.height * animatedY,
            width: size,
            height: size
        )
    }
}

func drawText(
    _ text: String,
    in rect: NSRect,
    font: NSFont,
    color: NSColor,
    alignment: NSTextAlignment
) {
    let paragraph = NSMutableParagraphStyle()
    paragraph.alignment = alignment
    paragraph.lineBreakMode = .byTruncatingTail
    (text as NSString).draw(
        in: rect,
        withAttributes: [
            .font: font,
            .foregroundColor: color,
            .paragraphStyle: paragraph
        ]
    )
}
