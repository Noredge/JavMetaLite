import SwiftUI
import AppKit
#if SWIFT_PACKAGE
import NativeBridgeKit
import WorkerKit
#endif

struct CachedArtwork: View {
    @EnvironmentObject var store: WorkspaceStore
    let url: URL
    var maximumPixelSize: Int = 600
    var posterCrop = false
    var fill = false
    @ViewState<NSImage?> private var image: NSImage?
    @ViewState<String> private var loadedKey = ""
    private var requestKey: String { "\(url.path)-\(maximumPixelSize)-\(posterCrop)" }
    var body: some View {
        Group {
            if let image, loadedKey == requestKey { Image(nsImage: image).resizable().aspectRatio(contentMode: fill ? .fill : .fit) }
            else if loadedKey == requestKey { Image(systemName: "exclamationmark.triangle").foregroundStyle(.secondary).accessibilityLabel(tr("artworkPreviewFailed")) }
            else { Image(systemName: "photo").foregroundStyle(.secondary).accessibilityLabel(tr("artworkLoading")) }
        }.task(id: requestKey) {
            let key = requestKey
            guard let reader = store.beginArtworkURLRead(url) else { return }
            defer { store.endArtworkURLRead(reader) }
            let loaded = await ArtworkThumbnailCache.shared.image(url: url, maximumPixelSize: maximumPixelSize, posterCrop: posterCrop)
            if !Task.isCancelled { image = loaded?.image; loadedKey = key }
        }
    }
}

struct NativeArtworkCanvas: NSViewRepresentable {
    let image: NSImage
    let pixelSize: CGSize
    let imageKey: String
    let commandID: UUID
    let command: ArtworkZoomCommand
    let onScale: (CGFloat) -> Void
    let onNavigate: (Int) -> Void
    final class Coordinator { var lastCommand: UUID? }
    func makeCoordinator() -> Coordinator { Coordinator() }
    func makeNSView(context: Context) -> ArtworkScrollView { ArtworkScrollView(frame: .zero) }
    func updateNSView(_ view: ArtworkScrollView, context: Context) {
        view.onScaleChanged = onScale; view.onNavigate = onNavigate
        view.show(image, pixelSize: pixelSize, key: imageKey)
        if context.coordinator.lastCommand != commandID {
            context.coordinator.lastCommand = commandID; view.apply(command)
        }
    }
    static func dismantleNSView(_ view: ArtworkScrollView, coordinator: Coordinator) {
        view.onScaleChanged = nil; view.onNavigate = nil
    }
}

struct ArtworkViewer: View {
    var previewOnly = false
    @EnvironmentObject var store: WorkspaceStore
    @Environment(\.dismiss) var dismiss
    @FocusState private var viewerHasKeyboardFocus: Bool
    @ViewState private var scale: CGFloat = 1
    @ViewState private var nativeSize: CGSize = .zero
    @ViewState private var decodedImage: NSImage?
    @ViewState private var loadedKey = ""
    @ViewState private var failedDecodeKey = ""
    @ViewState private var decodeAttempt = UUID()
    @ViewState private var command = ArtworkZoomCommand.fit
    @ViewState private var commandID = UUID()
    private var url: URL? { store.viewerImageURL }
    private var posterCrop: Bool {
        guard store.viewerArtwork.indices.contains(store.viewerIndex) else { return false }
        let art = store.viewerArtwork[store.viewerIndex]
        return art["kind"].stringValue == "poster" && art["source"].stringValue != "local"
    }
    private var imageKey: String { (url?.path ?? "") + (posterCrop ? "/poster" : "/full") }
    private var nativeDecodeFailed: Bool { url != nil && failedDecodeKey == imageKey }
    private var decodeRequestKey: String { imageKey + "/" + decodeAttempt.uuidString }
    private func zoom(_ action: ArtworkZoomCommand) { command = action; commandID = UUID() }
    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Button { dismiss() } label: { Label(tr("close"), systemImage: "xmark") }.keyboardShortcut(.cancelAction)
                Divider().frame(height: 18)
                Text(tr("viewArtwork")).font(.headline)
                if nativeSize != .zero { Text(String(format: tr("artworkDimensions"), Int(nativeSize.width), Int(nativeSize.height))).font(.caption).foregroundStyle(.secondary) }
                Spacer()
                Text("\(store.viewerIndex + 1) / \(store.viewerArtwork.count)").font(.caption).monospacedDigit()
                Button(tr("fitWindow")) { zoom(.fit) }
                Button(tr("actualSize")) { zoom(.actualSize) }.help(tr("actualSizeHelp")).disabled(nativeSize == .zero)
                Button { zoom(.zoomOut) } label: { Image(systemName: "minus.magnifyingglass") }.help(tr("zoomOut")).accessibilityLabel(tr("zoomOut"))
                Text("\(Int((scale * 100).rounded()))%").font(.caption.monospacedDigit()).frame(minWidth: 40)
                Button { zoom(.zoomIn) } label: { Image(systemName: "plus.magnifyingglass") }.help(tr("zoomIn")).accessibilityLabel(tr("zoomIn"))
            }.controlSize(.small).padding(14)
            if !previewOnly, let job = store.jobs.first(where: { $0.jobID == store.viewerJobID }) { ArtworkSourcePicker(job: job, fromViewer: true).padding(.horizontal, 16).padding(.bottom, 10) }
            if max(nativeSize.width, nativeSize.height) > 8192 { Text(tr("artworkDecodeLimit")).font(.caption).foregroundStyle(.secondary).padding(.bottom, 8) }
            Divider()
            ZStack {
                if let decodedImage, loadedKey == imageKey {
                    NativeArtworkCanvas(image: decodedImage, pixelSize: nativeSize, imageKey: imageKey,
                        commandID: commandID, command: command,
                        onScale: { if abs(scale - $0) > 0.0001 { scale = $0 } },
                        onNavigate: { store.moveViewer(by: $0) })
                        .accessibilityLabel(tr("viewArtwork")).help(tr("artworkGestureHint"))
                } else if store.viewerImageError != nil || nativeDecodeFailed {
                    VStack(spacing: 16) {
                        Label(tr("artworkPreviewFailed"), systemImage: "exclamationmark.triangle").foregroundStyle(.secondary)
                        Button(tr("retryArtwork")) {
                            let invalidate = nativeDecodeFailed
                            failedDecodeKey = ""; decodeAttempt = UUID()
                            store.retryViewerArtwork(invalidatePublishedPreview: invalidate)
                            viewerHasKeyboardFocus = true
                        }
                            .accessibilityIdentifier("retryCurrentArtwork").disabled(!store.ready || store.working || store.statusUnconfirmed)
                        if let error = store.viewerImageError {
                            DisclosureGroup(tr("technicalDetails")) { NativeDiagnosticTextView(text: error, accessibilityLabel: tr("technicalDetails")).frame(height: 120) }
                                .font(.caption).frame(maxWidth: 560)
                        }
                    }.padding(24).frame(maxWidth: .infinity, maxHeight: .infinity)
                } else { ProgressView(tr("artworkLoading")).frame(maxWidth: .infinity, maxHeight: .infinity) }
                HStack {
                    navigationButton(-1)
                    Spacer(minLength: 0)
                    navigationButton(1)
                }.padding(.horizontal, 14)
            }
        }.frame(minWidth: 760, minHeight: 500)
        .focusable().focusEffectDisabled().focused($viewerHasKeyboardFocus).defaultFocus($viewerHasKeyboardFocus, true)
        .onAppear { viewerHasKeyboardFocus = true }
        .onKeyPress(.leftArrow) { guard viewerHasKeyboardFocus else { return .ignored }; store.moveViewer(by: -1); return .handled }
        .onKeyPress(.rightArrow) { guard viewerHasKeyboardFocus else { return .ignored }; store.moveViewer(by: 1); return .handled }
        .onDisappear { store.closeArtworkViewer() }
        .task(id: decodeRequestKey) {
            decodedImage = nil; loadedKey = ""; failedDecodeKey = ""; nativeSize = .zero; scale = 1; zoom(.fit)
            guard let url, let reader = store.beginArtworkURLRead(url) else { return }
            defer { store.endArtworkURLRead(reader) }
            let key = imageKey
            let decoded = await ArtworkThumbnailCache.shared.image(url: url, maximumPixelSize: 8192, posterCrop: posterCrop)
            guard !Task.isCancelled else { return }
            guard let decoded else { failedDecodeKey = key; return }
            nativeSize = decoded.nativeSize; decodedImage = decoded.image; loadedKey = key
        }
    }
    private func navigationButton(_ offset: Int) -> some View {
        Button { store.moveViewer(by: offset); viewerHasKeyboardFocus = true } label: {
            Image(systemName: offset < 0 ? "chevron.left" : "chevron.right").font(.title3.weight(.semibold))
                .frame(width: 42, height: 42).background(.regularMaterial, in: Circle())
        }.buttonStyle(.plain).help(tr(offset < 0 ? "artworkPrevious" : "artworkNext"))
            .accessibilityLabel(tr(offset < 0 ? "artworkPrevious" : "artworkNext"))
            .accessibilityIdentifier(offset < 0 ? "previousArtwork" : "nextArtwork")
            .disabled(offset < 0 ? store.viewerIndex <= 0 : store.viewerIndex + 1 >= store.viewerArtwork.count)
    }
}

/// A view owns its preview while visible; its decoder owns a separate URL read
/// until asynchronous decoding has really returned, including cancellation.
struct ArtworkReadLifetime: ViewModifier {
    @EnvironmentObject var store: WorkspaceStore
    let artwork: JSONValue?
    let job: MovieJobSnapshot
    var scope = "detail"
    @ViewState private var readerID = UUID()
    private var identity: String {
        (artwork.map { store.artworkRequestKey($0, job: job) } ?? job.jobID) + "/\(store.artworkLoadGeneration)"
    }
    func body(content: Content) -> some View {
        content.onAppear { retain() }
            .onChange(of: identity) { _, _ in retain() }
            .onDisappear { store.endArtworkRead(readerID) }
    }
    private func retain() {
        if let artwork { store.beginArtworkRead(artwork, jobID: job.jobID, readerID: readerID, scope: scope) }
        else { store.endArtworkRead(readerID) }
    }
}

struct DeskArtwork: View {
    @EnvironmentObject var store: WorkspaceStore
    @Environment(\.displayScale) private var displayScale
    let job: MovieJobSnapshot
    let art: JSONValue?
    let height: CGFloat
    var crop = false
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 6).fill(FilmDesk.shelf)
            if let art, let url = store.artworkPaths[store.artworkKey(art, jobID: job.jobID)] {
                GeometryReader { frame in
                    let edge = max(frame.size.width, frame.size.height) * displayScale
                    // Use size buckets so resizing the window reuses nearby thumbnails.
                    let pixels = max(128, Int(ceil(edge / 128)) * 128)
                    CachedArtwork(url: url, maximumPixelSize: pixels, posterCrop: crop && art["source"].stringValue != "local")
                        .frame(width: frame.size.width, height: frame.size.height)
                }
            } else if let art, store.artworkErrors[store.artworkKey(art, jobID: job.jobID)] != nil {
                Image(systemName: "exclamationmark.triangle").accessibilityLabel(tr("artworkPreviewFailed"))
            } else if art != nil {
                ProgressView().controlSize(.small)
            } else {
                Image(systemName: "photo").accessibilityLabel(tr("noArtwork"))
            }
        }.frame(maxWidth: .infinity).frame(height: height).clipShape(RoundedRectangle(cornerRadius: 6))
            .modifier(ArtworkReadLifetime(artwork: art, job: job))
    }
}

struct GalleryPicker: View {
    @EnvironmentObject var store: WorkspaceStore
    @Environment(\.dismiss) private var dismiss
    let job: MovieJobSnapshot
    @ViewState private var kind: String
    @ViewState private var coverSource: String
    @ViewState private var selectedSamples: Set<String>
    @ViewState private var highlight: String?
    @ViewState private var compare = false

    @ViewState private var enlarging = false
    private let initialSamples: Set<String>

    init(job: MovieJobSnapshot, initialKind: String) {
        self.job = job
        let current = job.artwork.first { $0["kind"].stringValue != "sample" && $0["selected"].boolValue }
        _coverSource = ViewState(initialValue: current?["source"].stringValue ?? "")
        initialSamples = Set(job.artwork.filter { $0["kind"].stringValue == "sample" && $0["selected"].boolValue }.map(Self.sampleKey))
        _selectedSamples = ViewState(initialValue: initialSamples)
        _kind = ViewState(initialValue: job.artwork.contains { $0["kind"].stringValue == initialKind } ? initialKind : job.artwork.first?["kind"].stringValue ?? initialKind)
    }
    private static func sampleKey(_ art: JSONValue) -> String {
        art["selectionKey"].stringValue.isEmpty ? art.artworkIdentity : art["selectionKey"].stringValue
    }
    private var samples: [JSONValue] {
        var seen = Set<String>()
        return job.artwork.filter { $0["kind"].stringValue == "sample" && seen.insert(Self.sampleKey($0)).inserted }
    }
    private var choices: [JSONValue] { kind == "sample" ? samples : job.artwork.filter { $0["kind"].stringValue == kind } }
    private var shown: JSONValue? {
        choices.first { $0.artworkIdentity == highlight } ?? choices.first { $0["source"].stringValue == coverSource } ?? choices.first
    }
    private var applied: JSONValue? { choices.first { $0["selected"].boolValue } }
    private var chosenCover: JSONValue? { job.artwork.first { $0["kind"].stringValue != "sample" && $0["source"].stringValue == coverSource } }
    private var coverChanged: Bool { chosenCover.map { !$0["selected"].boolValue } ?? false }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text(tr("chooseImages")).font(.title2.weight(.semibold))
                Text(job.metadata["id"].stringValue).foregroundStyle(.secondary)
                Spacer()
                Picker(tr("artwork"), selection: $kind) {
                    ForEach(["poster", "fanart", "sample"], id: \.self) { type in
                        Text(tr("artwork." + type)).tag(type)
                    }
                }.pickerStyle(.segmented).labelsHidden().fixedSize()
            }
            HStack(spacing: 14) {
                if compare, kind != "sample", let applied, applied.artworkIdentity != shown?.artworkIdentity {
                    imagePanel(applied)
                }
                if let shown { imagePanel(shown) }
                else { ContentUnavailableView(tr("noArtwork"), systemImage: "photo").frame(maxWidth: .infinity, minHeight: 245) }
            }
            if let shown {
                HStack {
                    if kind == "sample" {
                        Toggle(tr("selectedSample"), isOn: Binding(get: { selectedSamples.contains(Self.sampleKey(shown)) }, set: {
                            if $0 { selectedSamples.insert(Self.sampleKey(shown)) } else { selectedSamples.remove(Self.sampleKey(shown)) }
                        })).toggleStyle(.checkbox)
                    } else {
                        Toggle(tr("compareCover"), isOn: $compare).toggleStyle(.checkbox)
                            .disabled(applied == nil || shown.artworkIdentity == applied?.artworkIdentity)
                    }
                    Spacer()
                    Button(tr("viewArtwork")) {
                        store.prepareArtworkViewer(shown, job: job)
                        enlarging = true
                    }
                    if shown["source"].stringValue == "local" {
                        Menu {
                            Button(tr("trashArtwork"), role: .destructive) { store.trashArtwork(shown, jobID: job.jobID) }
                        } label: { Image(systemName: "ellipsis.circle") }.fixedSize()
                    }
                }
            }
            ScrollView(.horizontal) {
                LazyHStack(spacing: 8) {
                    ForEach(choices, id: \.artworkIdentity) { art in
                        Button {
                            highlight = art.artworkIdentity
                            if kind != "sample" { coverSource = art["source"].stringValue }
                        } label: {
                            VStack(spacing: 3) {
                                DeskArtwork(job: job, art: art, height: 50, crop: kind == "poster")
                                HStack(spacing: 3) {
                                    if kind == "sample", selectedSamples.contains(Self.sampleKey(art)) { Image(systemName: "checkmark") }
                                    Text(sourceName(art["source"].stringValue)).lineLimit(1)
                                }.font(.caption)
                            }.padding(3).frame(width: 87).overlay {
                                RoundedRectangle(cornerRadius: 6).strokeBorder(shown?.artworkIdentity == art.artworkIdentity ? FilmDesk.accent : .clear, lineWidth: 2)
                            }
                        }.buttonStyle(.plain)
                    }
                }.padding(2)
            }.frame(height: 83)
            Divider()
            HStack {
                Text(String(format: tr("selectedStillCount"), selectedSamples.count)).font(.callout).foregroundStyle(.secondary)
                Spacer()
                Button(tr("cancel")) { dismiss() }.keyboardShortcut(.cancelAction)
                Button(tr("applyImages")) {
                    guard selectedSamples != initialSamples || coverChanged else { dismiss(); return }
                    store.applyGallery(job: job, coverID: chosenCover?.artworkIdentity ?? "",
                                       sampleIDs: samples.filter { selectedSamples.contains(Self.sampleKey($0)) }.map(\.artworkIdentity)) { dismiss() }
                }.buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction)
            }
        }.padding(20).frame(minWidth: 600, idealWidth: 690, maxWidth: 820).background(FilmDesk.paper)
            .disabled(store.working || store.statusUnconfirmed)
            .onChange(of: kind) { _, _ in highlight = nil }
            .interactiveDismissDisabled(store.working)
            .sheet(isPresented: $enlarging) { ArtworkViewer(previewOnly: true) }
            .onChange(of: job.reviewRevision) { _, _ in dismiss() }
    }
    private func imagePanel(_ art: JSONValue) -> some View {
        VStack(spacing: 5) {
            DeskArtwork(job: job, art: art, height: 245, crop: kind == "poster")
            Text(sourceName(art["source"].stringValue) + " · " + tr("artwork." + kind))
                .font(.caption).foregroundStyle(.secondary)
        }.frame(maxWidth: .infinity)
    }
}
