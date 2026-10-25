import AppKit
import ImageIO

public struct DecodedArtwork: @unchecked Sendable {
    public let image: NSImage
    public let width: Int
    public let height: Int
    public let decodedCost: Int
}

/// A byte-bounded LRU of decoded thumbnails. Large viewer images are not retained.
public actor ArtworkThumbnailCache {
    public static let shared = ArtworkThumbnailCache()
    public let maximumCost: Int
    private var entries: [String: (image: DecodedArtwork, access: Int)] = [:]
    private var pending: [String: Task<DecodedArtwork?, Never>] = [:]
    private var cost = 0
    private var clock = 0
    public init(maximumCost: Int = 128 * 1024 * 1024) { self.maximumCost = maximumCost }
    public var retainedCost: Int { cost }
    public func image(url: URL, maximumPixelSize: Int, posterCrop: Bool = false) async -> DecodedArtwork? {
        let key = url.path + "/" + String(maximumPixelSize) + (posterCrop ? "/poster" : "")
        clock += 1
        if let entry = entries[key] { entries[key] = (entry.image, clock); return entry.image }
        if let task = pending[key] { return await task.value }
        let task = Task.detached(priority: .utility) { () -> DecodedArtwork? in
            guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
                  let thumbnail = CGImageSourceCreateThumbnailAtIndex(source, 0, [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: maximumPixelSize, kCGImageSourceCreateThumbnailWithTransform: !posterCrop, kCGImageSourceShouldCacheImmediately: true] as CFDictionary) else { return nil }
            // Match Core's raw-pixel, right-half poster contract. Local posters
            // and the full-cover viewer continue to use the uncropped cache key.
            let cg: CGImage
            if posterCrop && thumbnail.width > thumbnail.height {
                let width = max(1, thumbnail.width / 2)
                guard let cropped = thumbnail.cropping(to: CGRect(x: thumbnail.width - width, y: 0, width: width, height: thumbnail.height)) else { return nil }
                cg = cropped
            } else { cg = thumbnail }
            return DecodedArtwork(image: NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height)), width: cg.width, height: cg.height, decodedCost: cg.bytesPerRow * cg.height)
        }
        pending[key] = task
        let decoded = await task.value
        pending.removeValue(forKey: key)
        guard let decoded else { return nil }
        if decoded.decodedCost <= maximumCost {
            while cost + decoded.decodedCost > maximumCost, let oldest = entries.min(by: { $0.value.access < $1.value.access }) {
                cost -= oldest.value.image.decodedCost; entries.removeValue(forKey: oldest.key)
            }
            clock += 1; entries[key] = (decoded, clock); cost += decoded.decodedCost
        }
        return decoded
    }
    public func removeAll() { entries.removeAll(); cost = 0 }
}

public enum ArtworkDimensions {
    public static func read(url: URL) async -> CGSize? {
        await Task.detached(priority: .utility) {
            guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
                  let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
                  let width = properties[kCGImagePropertyPixelWidth] as? Int,
                  let height = properties[kCGImagePropertyPixelHeight] as? Int else { return nil }
            return CGSize(width: width, height: height)
        }.value
    }
}

public enum ArtworkZoomCommand { case fit, actualSize, zoomIn, zoomOut }

private final class CenteredArtworkClipView: NSClipView {
    override func constrainBoundsRect(_ proposedBounds: NSRect) -> NSRect {
        var result = super.constrainBoundsRect(proposedBounds)
        if let document = documentView {
            if document.frame.width < result.width { result.origin.x = (document.frame.width - result.width) / 2 }
            if document.frame.height < result.height { result.origin.y = (document.frame.height - result.height) / 2 }
        }
        return result
    }
}

/// Native pinch-to-zoom and precise two-finger panning. A conventional mouse
/// wheel zooms around the pointer; Command-scroll also zooms on a trackpad.
@MainActor public final class ArtworkScrollView: NSScrollView {
    private let artworkView = NSImageView()
    private var pixels = CGSize.zero
    private var artworkKey = ""
    private var applyingZoom = false
    private var notificationPending = false
    public private(set) var fitsWindow = true
    public var onScaleChanged: ((CGFloat) -> Void)?
    public var onNavigate: ((Int) -> Void)?

    public override init(frame: NSRect) {
        super.init(frame: frame)
        contentView = CenteredArtworkClipView()
        drawsBackground = true; backgroundColor = .windowBackgroundColor
        contentView.drawsBackground = false
        hasHorizontalScroller = true; hasVerticalScroller = true; autohidesScrollers = true
        borderType = .noBorder; allowsMagnification = true
        minMagnification = 0.02; maxMagnification = 16
        artworkView.imageScaling = .scaleAxesIndependently
        artworkView.isEditable = false
        documentView = artworkView
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }
    public func show(_ image: NSImage, pixelSize: CGSize, key: String) {
        guard key != artworkKey else { return }
        artworkKey = key; pixels = pixelSize; artworkView.image = image
        fitsWindow = true; updateDocumentSize(); updateFit()
    }
    private func updateDocumentSize() {
        let backingScale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        artworkView.frame = NSRect(origin: .zero, size: NSSize(width: max(1, pixels.width / backingScale), height: max(1, pixels.height / backingScale)))
    }
    public override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow(); updateDocumentSize()
        if fitsWindow { updateFit() }
    }
    public override func layout() {
        super.layout()
        if fitsWindow && !applyingZoom { updateFit() }
    }
    public func apply(_ command: ArtworkZoomCommand) {
        switch command {
        case .fit: fitsWindow = true; updateFit()
        case .actualSize: fitsWindow = false; setScale(1)
        case .zoomIn: fitsWindow = false; setScale(magnification * 1.25)
        case .zoomOut: fitsWindow = false; setScale(magnification / 1.25)
        }
    }
    private func updateFit() {
        guard pixels.width > 0, pixels.height > 0, contentSize.width > 0, contentSize.height > 0 else { return }
        setScale(min(contentSize.width / artworkView.frame.width, contentSize.height / artworkView.frame.height))
    }
    private func setScale(_ value: CGFloat, anchor: NSPoint? = nil) {
        guard value.isFinite, !applyingZoom else { return }
        applyingZoom = true
        let bounded = max(minMagnification, min(maxMagnification, value))
        if abs(bounded - magnification) > 0.0001 {
            setMagnification(bounded, centeredAt: anchor ?? NSPoint(x: contentView.bounds.midX, y: contentView.bounds.midY))
        }
        applyingZoom = false; announceScale()
    }
    public func applyWheelDelta(_ delta: CGFloat, anchor: NSPoint? = nil) {
        guard delta.isFinite, delta != 0 else { return }
        fitsWindow = false
        setScale(magnification * pow(1.12, max(-4, min(4, delta))), anchor: anchor)
    }
    public override func scrollWheel(with event: NSEvent) {
        if event.hasPreciseScrollingDeltas && !event.modifierFlags.contains(.command) { super.scrollWheel(with: event); return }
        applyWheelDelta(event.scrollingDeltaY, anchor: contentView.convert(event.locationInWindow, from: nil))
    }
    public override func magnify(with event: NSEvent) {
        fitsWindow = false; super.magnify(with: event); announceScale()
    }
    public override func keyDown(with event: NSEvent) {
        if let onNavigate, !event.modifierFlags.contains(.command), [123, 124].contains(event.keyCode) {
            onNavigate(event.keyCode == 123 ? -1 : 1)
        } else { super.keyDown(with: event) }
    }
    private func announceScale() {
        guard !notificationPending else { return }; notificationPending = true
        // Never publish SwiftUI state synchronously during AppKit layout.
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }; self.notificationPending = false
            self.onScaleChanged?(self.magnification)
        }
    }
}
