import Testing
import Foundation
import AppKit
#if SWIFT_PACKAGE
@testable import NativeBridgeKit
#else
@testable import JavMetaLiteMac
#endif

@Suite(.serialized) @MainActor struct DiagnosticTextTests {
    @Test func completeLongDiagnosticsCanBeSelectedAndCopiedWithoutEditing() throws {
        _ = NSApplication.shared
        let first = "/synthetic/" + String(repeating: "长い名前-组合é-", count: 180) + "/first.mp4"
        let second = "/synthetic/" + String(repeating: "another-component/", count: 160) + "/last.mp4"
        let details = "AccessDenied\n\(first)\nDenied.\n\nReadFailed\n\(second)\nSynthetic I/O error."
        let scroll = NativeDiagnosticTextView.makeScrollView(text: details, accessibilityLabel: "Full diagnostics")
        let view = try #require(scroll.documentView as? NSTextView)
        #expect(view.string == details)
        #expect(view.isSelectable && !view.isEditable && !view.isRichText)
        #expect(view.textContainer?.widthTracksTextView == true)
        #expect(view.textContainer?.lineBreakMode == .byCharWrapping)
        #expect(scroll.hasVerticalScroller && !scroll.hasHorizontalScroller)
        #expect(view.accessibilityLabel() == "Full diagnostics")
        let original = view.string
        #expect(!view.shouldChangeText(in: NSRange(location: 0, length: 1), replacementString: "X"))
        #expect(view.string == original)
        view.setSelectedRange(NSRange(location: 0, length: (details as NSString).length))
        let board = NSPasteboard.withUniqueName()
        defer { board.releaseGlobally() }
        // Match NSTextView's native Copy command. On this SDK it advertises the
        // legacy NSStringPboardType and bridges that to the modern string type.
        #expect(view.writeSelection(to: board, types: view.writablePasteboardTypes))
        #expect(board.string(forType: .string) == details)
        let tail = (details as NSString).range(of: second)
        view.setSelectedRange(tail)
        #expect(view.writeSelection(to: board, types: view.writablePasteboardTypes))
        #expect(board.string(forType: .string) == second)
        #expect(view.string == original, "Selecting and copying must never mutate diagnostics.")
    }
}
