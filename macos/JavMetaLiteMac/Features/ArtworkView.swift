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
    @ViewState<NSImage?> private var image: NSImage?
    @ViewState<String> private var loadedKey = ""
    private var requestKey: String { "\(url.path)-\(maximumPixelSize)-\(posterCrop)" }
    var body: some View {
        Group {
            if let image, loadedKey == requestKey { Image(nsImage: image).resizable().scaledToFit() }
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

/// The editor and batch overview use the same reviewed pair and content-keyed
/// requests as the gallery. Previewing images never changes review selection.
struct PrimaryArtworkPreview: View {
    @EnvironmentObject var store: WorkspaceStore
    let job: MovieJobSnapshot
    let kind: String
    var height: CGFloat = 184
    private var artwork: JSONValue? { ArtworkReviewPolicy.primary(job.artwork, kind: kind) }
    var body: some View {
        VStack(spacing: 7) {
            Button {
                if let artwork { store.openArtwork(artwork, job: job) }
            } label: {
                Group {
                    if let artwork, let url = store.artworkPaths[store.artworkKey(artwork, jobID: job.jobID)] {
                        CachedArtwork(url: url, maximumPixelSize: height < 100 ? 200 : 700,
                                      posterCrop: kind == "poster" && artwork["source"].stringValue != "local")
                    } else {
                        VStack(spacing: 8) {
                            Image(systemName: "photo").font(height < 100 ? .body : .title)
                            if height >= 100 { Text(tr(ArtworkReviewPolicy.previewState(hasArtwork: artwork != nil, loaded: false,
                                failed: artwork.map { store.artworkErrors[store.artworkKey($0, jobID: job.jobID)] != nil } ?? false))).font(.caption).multilineTextAlignment(.center) }
                        }.foregroundStyle(.secondary).frame(maxWidth: .infinity).frame(height: height)
                            .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 6))
                    }
                }.frame(maxWidth: .infinity).frame(height: height)
                    .clipShape(RoundedRectangle(cornerRadius: 6))
                    .contentShape(Rectangle())
            }.buttonStyle(.plain).disabled(artwork == nil || store.working || store.statusUnconfirmed)
                .accessibilityLabel(tr("artwork." + kind) + " · " + tr("viewArtwork")).help(tr("viewArtwork"))
            if height >= 100 { Text(tr("artwork." + kind)).font(.caption).foregroundStyle(.secondary) }
        }.modifier(ArtworkReadLifetime(artwork: artwork, job: job, scope: height < 100 ? "queue" : "detail"))
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
    @EnvironmentObject var store: WorkspaceStore
    @Environment(\.dismiss) var dismiss
    @FocusState private var viewerHasKeyboardFocus: Bool
    @ViewState private var scale: CGFloat = 1
    @ViewState private var nativeSize: CGSize = .zero
    @ViewState private var decodedImage: NSImage?
    @ViewState private var loadedURL = ""
    @ViewState private var command = ArtworkZoomCommand.fit
    @ViewState private var commandID = UUID()
    private var url: URL? { store.viewerImageURL }
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
            if let job = store.jobs.first(where: { $0.jobID == store.viewerJobID }) { ArtworkSourcePicker(job: job, fromViewer: true).padding(.horizontal, 16).padding(.bottom, 10) }
            if max(nativeSize.width, nativeSize.height) > 8192 { Text(tr("artworkDecodeLimit")).font(.caption).foregroundStyle(.secondary).padding(.bottom, 8) }
            Divider()
            ZStack {
                if let url, let decodedImage, loadedURL == url.path {
                    NativeArtworkCanvas(image: decodedImage, pixelSize: nativeSize, imageKey: url.path,
                        commandID: commandID, command: command,
                        onScale: { if abs(scale - $0) > 0.0001 { scale = $0 } },
                        onNavigate: { store.moveViewer(by: $0) })
                        .accessibilityLabel(tr("viewArtwork")).help(tr("artworkGestureHint"))
                } else if let error = store.viewerImageError {
                    VStack(spacing: 16) {
                        Label(tr("artworkPreviewFailed"), systemImage: "exclamationmark.triangle").foregroundStyle(.secondary)
                        Button(tr("retryArtwork")) { store.retryViewerArtwork(); viewerHasKeyboardFocus = true }
                            .accessibilityIdentifier("retryCurrentArtwork").disabled(!store.ready || store.working || store.statusUnconfirmed)
                        DisclosureGroup(tr("technicalDetails")) { NativeDiagnosticTextView(text: error, accessibilityLabel: tr("technicalDetails")).frame(height: 120) }
                            .font(.caption).frame(maxWidth: 560)
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
        .task(id: url) {
            decodedImage = nil; loadedURL = ""; nativeSize = .zero; scale = 1; zoom(.fit)
            guard let url, let reader = store.beginArtworkURLRead(url) else { return }
            defer { store.endArtworkURLRead(reader) }
            guard let dimensions = await ArtworkDimensions.read(url: url),
                  let decoded = await ArtworkThumbnailCache.shared.image(url: url, maximumPixelSize: 8192), !Task.isCancelled else { return }
            nativeSize = dimensions; decodedImage = decoded.image; loadedURL = url.path
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
