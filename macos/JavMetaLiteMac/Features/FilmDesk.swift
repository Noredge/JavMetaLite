import SwiftUI
#if SWIFT_PACKAGE
import WorkerKit
#endif

enum FilmDesk {
    static let ink = Color(red: 0.055, green: 0.15, blue: 0.16)
    static let brass = Color(red: 0.98, green: 0.76, blue: 0.47)
    static let accent = Color(light: (0.11, 0.37, 0.34), dark: (0.52, 0.79, 0.70))
    static let paper = Color(light: (0.965, 0.960, 0.943), dark: (0.105, 0.12, 0.12))
    static let shelf = Color(light: (0.925, 0.924, 0.899), dark: (0.085, 0.10, 0.10))
    static let surface = Color(light: (0.995, 0.992, 0.980), dark: (0.145, 0.16, 0.16))
}

private extension Color {
    init(light: (CGFloat, CGFloat, CGFloat), dark: (CGFloat, CGFloat, CGFloat)) {
        self.init(nsColor: NSColor(name: nil) { appearance in
            let rgb = appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? dark : light
            return NSColor(srgbRed: rgb.0, green: rgb.1, blue: rgb.2, alpha: 1)
        })
    }
}

struct ImportDesk: View {
    @EnvironmentObject var store: WorkspaceStore
    var body: some View {
        VStack(spacing: 14) {
            Image(systemName: "film.stack").font(.system(size: 36, weight: .light)).foregroundStyle(FilmDesk.accent)
            Text(tr("dropMovie")).font(.title.weight(.semibold))
            Text(tr("dropLookupHint")).foregroundStyle(.secondary).multilineTextAlignment(.center)
            Button(tr("addMovies")) { store.chooseFiles() }.buttonStyle(.borderedProminent)
                .controlSize(.large).disabled(!store.canImportFiles)
        }.padding(36).frame(maxWidth: .infinity, maxHeight: .infinity).background(FilmDesk.paper)
    }
}

struct QuerySourcePicker: View {
    @EnvironmentObject var store: WorkspaceStore
    var body: some View {
        Picker(tr("querySource"), selection: $store.sourceMode) {
            Text("LibreDMM").tag("libredmm")
            Text("R18.dev").tag("r18dev").disabled(!store.capabilities["searchR18Dev"].boolValue)
            Text(tr("customSources")).tag("custom").disabled(!store.capabilities["searchR18Dev"].boolValue)
        }.pickerStyle(.menu)
    }
}

struct NumberLookup: View {
    @EnvironmentObject var store: WorkspaceStore
    @ViewState private var number = ""
    @FocusState private var focused: Bool
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text(tr("lookupByID")).font(.title2.weight(.semibold))
            TextField(tr("field.id"), text: $number).textFieldStyle(.roundedBorder).focused($focused)
            QuerySourcePicker()
            HStack { Spacer()
                Button(tr("cancel")) { store.showingLookup = false }.keyboardShortcut(.cancelAction)
                Button(tr("searchMetadata")) { store.lookupNumber(number) }.buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction).disabled(number.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
        }.padding(22).frame(width: 370).onAppear { focused = true }
    }
}
