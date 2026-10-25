import AppKit
import SwiftUI

/// Native, read-only diagnostics keep selection and copy available even when a
/// single path is wider than the sheet. Wrapping never changes the stored text.
@MainActor public struct NativeDiagnosticTextView: NSViewRepresentable {
    public let text: String
    public let accessibilityLabel: String

    public init(text: String, accessibilityLabel: String) {
        self.text = text
        self.accessibilityLabel = accessibilityLabel
    }

    public func makeNSView(context: Context) -> NSScrollView {
        Self.makeScrollView(text: text, accessibilityLabel: accessibilityLabel)
    }

    public func updateNSView(_ scroll: NSScrollView, context: Context) {
        guard let view = scroll.documentView as? NSTextView else { return }
        if view.string != text { view.string = text }
        view.setAccessibilityLabel(accessibilityLabel)
    }

    public static func makeScrollView(text: String, accessibilityLabel: String) -> NSScrollView {
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 640, height: 280))
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = false
        scroll.autohidesScrollers = false
        scroll.borderType = .bezelBorder
        scroll.drawsBackground = true
        scroll.backgroundColor = .textBackgroundColor
        let view = NSTextView(frame: scroll.contentView.bounds)
        view.isEditable = false
        view.isSelectable = true
        view.isRichText = false
        view.allowsUndo = false
        view.font = .monospacedSystemFont(ofSize: NSFont.systemFontSize, weight: .regular)
        view.textColor = .textColor
        view.backgroundColor = .textBackgroundColor
        view.textContainerInset = NSSize(width: 10, height: 10)
        view.minSize = .zero
        view.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        view.isVerticallyResizable = true
        view.isHorizontallyResizable = false
        view.autoresizingMask = [.width]
        view.textContainer?.containerSize = NSSize(width: scroll.contentSize.width, height: CGFloat.greatestFiniteMagnitude)
        view.textContainer?.widthTracksTextView = true
        view.textContainer?.lineBreakMode = .byCharWrapping
        view.setAccessibilityLabel(accessibilityLabel)
        view.string = text
        scroll.documentView = view
        return scroll
    }
}

public enum DiagnosticClipboard {
    /// Copies the unabridged diagnostics. Tests inject an isolated named board;
    /// the general clipboard is touched only by the user's explicit action.
    @MainActor @discardableResult public static func copy(_ details: String, to pasteboard: NSPasteboard = .general) -> Bool {
        pasteboard.clearContents()
        return pasteboard.setString(details, forType: .string)
    }
}

@MainActor public extension NSAlert {
    /// The first button must be the non-destructive choice.
    func configureSafeCancelDefault() {
        guard let safeButton = buttons.first else { return }
        safeButton.keyEquivalent = "\r"
        window.defaultButtonCell = safeButton.cell as? NSButtonCell
    }

    /// AppKit cannot assign Escape and Return to the same button. Keep Return
    /// as the default and route only Escape in this alert's own window.
    private func installSafeEscapeHandler() -> Any? {
        NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, event.keyCode == 53,
                  event.modifierFlags.intersection([.command, .control, .option, .shift]).isEmpty,
                  event.window === self.window || NSApp.keyWindow === self.window else { return event }
            self.buttons.first?.performClick(nil)
            return nil
        }
    }

    func runSafeModal() -> NSApplication.ModalResponse {
        configureSafeCancelDefault()
        let monitor = installSafeEscapeHandler()
        defer { if let monitor { NSEvent.removeMonitor(monitor) } }
        return runModal()
    }

    /// Awaiting a parented sheet keeps Swift concurrency and AppKit on the same
    /// event loop. An unavailable parent safely cancels rather than starting an
    /// unowned application-modal loop from an asynchronous operation.
    func presentSafeSheet(for parent: NSWindow?) async -> NSApplication.ModalResponse {
        guard let parent, parent.isVisible, parent.attachedSheet == nil else { return .alertFirstButtonReturn }
        configureSafeCancelDefault()
        let monitor = installSafeEscapeHandler()
        defer { if let monitor { NSEvent.removeMonitor(monitor) } }
        return await withCheckedContinuation { continuation in
            beginSheetModal(for: parent) { response in
                continuation.resume(returning: response)
            }
            configureSafeCancelDefault()
        }
    }
}
