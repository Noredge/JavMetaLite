import AppKit

public enum NativeImportPanelConfiguration {
    @MainActor public static func configure(_ panel: NSOpenPanel, recursiveControl: NSButton) {
        panel.canChooseFiles = true
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = true
        panel.canCreateDirectories = false
        recursiveControl.sizeToFit()
        panel.accessoryView = recursiveControl
        panel.isAccessoryViewDisclosed = true
    }
}

public enum WorkspaceWindowPolicy {
    public static let minimumContentSize = NSSize(width: 920, height: 620)

    /// Preserve the restored display when it still intersects an available
    /// screen; otherwise recover onto the primary available display.
    public static func fittedFrame(_ proposed: NSRect, visibleFrames: [NSRect], minimumFrameSize: NSSize) -> NSRect {
        let screens = visibleFrames.filter { $0.origin.x.isFinite && $0.origin.y.isFinite && $0.width.isFinite && $0.height.isFinite && $0.width > 0 && $0.height > 0 }
        guard let first = screens.first else { return proposed }
        let valid = proposed.origin.x.isFinite && proposed.origin.y.isFinite && proposed.width.isFinite && proposed.height.isFinite
        var target = first
        if valid {
            var largestIntersection: CGFloat = 0
            for screen in screens {
                let overlap = proposed.intersection(screen)
                let area = overlap.isNull ? 0 : overlap.width * overlap.height
                if area > largestIntersection { target = screen; largestIntersection = area }
            }
        }
        let width = min(target.width, max(minimumFrameSize.width, valid ? proposed.width : minimumFrameSize.width))
        let height = min(target.height, max(minimumFrameSize.height, valid ? proposed.height : minimumFrameSize.height))
        let x = valid ? proposed.minX : target.minX
        let y = valid ? proposed.minY : target.maxY - height
        return NSRect(x: max(target.minX, min(x, target.maxX - width)),
                      y: max(target.minY, min(y, target.maxY - height)), width: width, height: height)
    }

    @MainActor public static func constrainRestoredFrame(_ window: NSWindow, visibleFrames: [NSRect]? = nil) {
        window.contentMinSize = minimumContentSize
        window.styleMask.insert(.resizable)
        let minimumFrame = window.frameRect(forContentRect: NSRect(origin: .zero, size: minimumContentSize)).size
        let fitted = fittedFrame(window.frame, visibleFrames: visibleFrames ?? NSScreen.screens.map(\.visibleFrame), minimumFrameSize: minimumFrame)
        window.setFrame(fitted, display: true)
    }
}
