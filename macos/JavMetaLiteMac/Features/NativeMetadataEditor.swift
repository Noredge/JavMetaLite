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
}
