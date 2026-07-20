import AppKit
import CoreImage

enum LiquidGlassPrimitive {
    case ellipse(CGRect)
    case roundedRect(CGRect, CGFloat)

    func signedDistance(to point: CGPoint) -> CGFloat {
        switch self {
        case let .ellipse(rect):
            let radius = min(rect.width, rect.height) / 2
            let center = CGPoint(x: rect.midX, y: rect.midY)
            return hypot(point.x - center.x, point.y - center.y) - radius
        case let .roundedRect(rect, radius):
            let safeRadius = min(radius, min(rect.width, rect.height) / 2)
            let qx = abs(point.x - rect.midX) - (rect.width / 2 - safeRadius)
            let qy = abs(point.y - rect.midY) - (rect.height / 2 - safeRadius)
            return hypot(max(qx, 0), max(qy, 0)) + min(max(qx, qy), 0) - safeRadius
        }
    }
}

final class LiquidGlassEffectView: NSVisualEffectView {
    private var refractionFilter: CIFilter?
    private weak var backdropLayer: CALayer?
    private var pendingMap: CIImage?
    private var pendingStrength: CGFloat = 16

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        scheduleFilterInstallation()
    }

    override func layout() {
        super.layout()
        scheduleFilterInstallation()
    }

    func updateRefraction(
        primitives: [LiquidGlassPrimitive],
        strength: CGFloat,
        edgeDepth: CGFloat = 14
    ) {
        let mapScale: CGFloat = 0.5
        guard bounds.width >= 2, bounds.height >= 2,
              let map = makeDisplacementMap(
                size: bounds.size,
                primitives: primitives,
                edgeDepth: edgeDepth,
                scale: mapScale
              ) else { return }
        pendingMap = CIImage(cgImage: map).transformed(
            by: CGAffineTransform(scaleX: 1 / mapScale, y: 1 / mapScale)
        )
        pendingStrength = strength
        applyPendingMap()
        scheduleFilterInstallation()
    }

    private func scheduleFilterInstallation() {
        DispatchQueue.main.async { [weak self] in
            self?.installFilterIfNeeded()
        }
    }

    private func installFilterIfNeeded() {
        guard let rootLayer = layer,
              let backdrop = findBackdropLayer(in: rootLayer) else { return }
        for sibling in backdrop.superlayer?.sublayers ?? [] where sibling !== backdrop {
            sibling.opacity = 0
            sibling.isHidden = true
        }
        backdrop.opacity = 1
        if backdropLayer !== backdrop || refractionFilter == nil {
            let filter = CIFilter(name: "CIDisplacementDistortion")
            filter?.name = "BalanceCapsuleEdgeRefraction"
            refractionFilter = filter
            backdropLayer = backdrop
            let existing = backdrop.filters ?? []
            backdrop.filters = filter.map { [$0] + existing } ?? existing
        }
        applyPendingMap()
    }

    private func applyPendingMap() {
        guard let filter = refractionFilter,
              let map = pendingMap else { return }
        filter.setValue(map, forKey: "inputDisplacementImage")
        filter.setValue(pendingStrength, forKey: kCIInputScaleKey)
        if let backdropLayer, let filters = backdropLayer.filters {
            backdropLayer.filters = filters
            backdropLayer.setNeedsDisplay()
        }
    }

    private func findBackdropLayer(in layer: CALayer) -> CALayer? {
        if NSStringFromClass(type(of: layer)).contains("Backdrop") {
            return layer
        }
        for child in layer.sublayers ?? [] {
            if let match = findBackdropLayer(in: child) {
                return match
            }
        }
        return nil
    }

    private func makeDisplacementMap(
        size: CGSize,
        primitives: [LiquidGlassPrimitive],
        edgeDepth: CGFloat,
        scale: CGFloat
    ) -> CGImage? {
        let width = max(2, Int((size.width * scale).rounded(.up)))
        let height = max(2, Int((size.height * scale).rounded(.up)))
        var pixels = [UInt8](repeating: 128, count: width * height * 4)

        func distance(_ point: CGPoint) -> CGFloat {
            primitives.reduce(CGFloat.greatestFiniteMagnitude) { current, primitive in
                min(current, primitive.signedDistance(to: point))
            }
        }

        for row in 0..<height {
            let y = (CGFloat(height - row) - 0.5) / scale
            for column in 0..<width {
                let x = (CGFloat(column) + 0.5) / scale
                let point = CGPoint(x: x, y: y)
                let signedDistance = distance(point)
                guard signedDistance <= 1.5 else {
                    let index = (row * width + column) * 4
                    pixels[index + 3] = 255
                    continue
                }

                let insideDepth = max(0, -signedDistance)
                let normalizedDepth = min(1, insideDepth / max(1, edgeDepth))
                let edgeAmount = 1 - normalizedDepth * normalizedDepth * (3 - 2 * normalizedDepth)
                let dx = distance(CGPoint(x: x + 0.75, y: y)) - distance(CGPoint(x: x - 0.75, y: y))
                let dy = distance(CGPoint(x: x, y: y + 0.75)) - distance(CGPoint(x: x, y: y - 0.75))
                let length = max(0.001, hypot(dx, dy))
                let normalX = dx / length
                let normalY = dy / length
                let encodedX = 0.5 + normalX * edgeAmount * 0.5
                let encodedY = 0.5 + normalY * edgeAmount * 0.5
                let index = (row * width + column) * 4
                pixels[index] = UInt8(max(0, min(255, encodedX * 255)))
                pixels[index + 1] = UInt8(max(0, min(255, encodedY * 255)))
                pixels[index + 2] = pixels[index + 1]
                pixels[index + 3] = 255
            }
        }

        let data = Data(pixels) as CFData
        guard let provider = CGDataProvider(data: data) else { return nil }
        return CGImage(
            width: width,
            height: height,
            bitsPerComponent: 8,
            bitsPerPixel: 32,
            bytesPerRow: width * 4,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
            provider: provider,
            decode: nil,
            shouldInterpolate: true,
            intent: .defaultIntent
        )
    }
}
