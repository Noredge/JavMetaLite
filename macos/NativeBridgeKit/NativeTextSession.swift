import AppKit

/// A distinct text view and undo stack for one immutable job/field identity.
@MainActor public final class NativeTextSession: NSObject, NSTextViewDelegate {
    public let jobID: String
    public let field: String
    public let textView: CompositionTextView
    public let scrollView: NSScrollView
    public var onChange: ((String) -> Void)?
    private var applyingModel = false
    private var initialized = false
    private var multiline = false
    private var lastPublished: String?

    public init(jobID: String, field: String) {
        self.jobID = jobID; self.field = field
        textView = CompositionTextView(frame: .zero)
        scrollView = NSScrollView(frame: .zero)
        super.init()
        textView.delegate = self
        NotificationCenter.default.addObserver(self, selector: #selector(undoOrRedoDidChange(_:)), name: .NSUndoManagerDidUndoChange, object: textView.undoManager)
        NotificationCenter.default.addObserver(self, selector: #selector(undoOrRedoDidChange(_:)), name: .NSUndoManagerDidRedoChange, object: textView.undoManager)
        textView.onCompositionCommit = { [weak self] in self?.publish() }
        textView.isRichText = false; textView.importsGraphics = false
        textView.allowsUndo = true; textView.isAutomaticQuoteSubstitutionEnabled = false
        textView.isAutomaticDashSubstitutionEnabled = false
        textView.font = .systemFont(ofSize: NSFont.systemFontSize)
        textView.textContainerInset = NSSize(width: 6, height: 5)
        textView.isVerticallyResizable = true; textView.isHorizontallyResizable = false
        textView.autoresizingMask = [.width]
        textView.textContainer?.widthTracksTextView = true
        textView.textContainer?.containerSize = NSSize(width: 0, height: CGFloat.greatestFiniteMagnitude)
        scrollView.documentView = textView; scrollView.borderType = .bezelBorder
        scrollView.drawsBackground = true; scrollView.backgroundColor = .textBackgroundColor
    }
    public func configure(value: String, label: String, editable: Bool, multiline: Bool) {
        self.multiline = multiline
        textView.setAccessibilityLabel(label)
        textView.isEditable = editable; textView.isSelectable = true
        scrollView.hasVerticalScroller = multiline
        // Neither composition nor an active native edit may be overwritten by a snapshot.
        guard !textView.hasMarkedText(), !initialized || textView.window?.firstResponder !== textView else { return }
        guard !initialized || textView.string != value else { return }
        applyingModel = true
        textView.breakUndoCoalescing()
        textView.string = value
        textView.undoManager?.removeAllActions()
        lastPublished = value
        applyingModel = false; initialized = true
    }
    public func textView(_ textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        guard !textView.hasMarkedText() else { return false }
        if commandSelector == #selector(NSResponder.insertTab(_:)) || (!multiline && commandSelector == #selector(NSResponder.insertNewline(_:))) {
            guard commitComposition() else { return true }
            textView.window?.selectNextKeyView(nil); return true
        }
        if commandSelector == #selector(NSResponder.insertBacktab(_:)) {
            guard commitComposition() else { return true }
            textView.window?.selectPreviousKeyView(nil); return true
        }
        return false
    }
    public func undoManager(for view: NSTextView) -> UndoManager? { textView.undoManager }
    public func textDidChange(_ notification: Notification) { publish() }
    public func textDidEndEditing(_ notification: Notification) { publish() }
    @objc private func undoOrRedoDidChange(_ notification: Notification) { publish() }
    private func publish() {
        guard !applyingModel, !textView.hasMarkedText(), lastPublished != textView.string else { return }
        lastPublished = textView.string
        onChange?(textView.string)
    }
    @discardableResult public func commitComposition() -> Bool {
        if textView.hasMarkedText() { textView.unmarkText() }
        guard !textView.hasMarkedText() else { return false }
        publish()
        textView.breakUndoCoalescing()
        return true
    }
}

@MainActor public final class CompositionTextView: NSTextView {
    private let fieldUndoManager = UndoManager()
    public var onCompositionCommit: (() -> Void)?
    public override var undoManager: UndoManager? { fieldUndoManager }
    public override func unmarkText() {
        super.unmarkText()
        if !hasMarkedText() { onCompositionCommit?() }
    }
}
