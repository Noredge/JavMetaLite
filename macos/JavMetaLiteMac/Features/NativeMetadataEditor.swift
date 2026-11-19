import AppKit
import SwiftUI
#if SWIFT_PACKAGE
import NativeBridgeKit
#endif

struct NativeMetadataEditor: NSViewRepresentable {
    let session: NativeTextSession
    let value: String
    let label: String
    var editable = true
    var multiline = false
    let onChange: (String) -> Void
    func makeNSView(context: Context) -> NSScrollView { session.scrollView }
    func updateNSView(_ view: NSScrollView, context: Context) {
        session.onChange = onChange
        session.configure(value: value, label: label, editable: editable, multiline: multiline)
    }

    func fieldSurface() -> some View {
        padding(.vertical, 2).background(FilmDesk.surface, in: RoundedRectangle(cornerRadius: 6))
            .overlay { RoundedRectangle(cornerRadius: 6).strokeBorder(.primary.opacity(0.15)).allowsHitTesting(false) }
    }
}
