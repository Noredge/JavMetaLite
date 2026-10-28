import SwiftUI
#if SWIFT_PACKAGE
import WorkerKit
#endif

struct EditorView: View {
    @EnvironmentObject var store: WorkspaceStore
    let job: MovieJobSnapshot
    @Binding var tab: String
    var body: some View {
        GeometryReader { viewport in
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    header
                    VStack(alignment: .leading, spacing: 12) {
                        HStack {
                            Text(tr("artwork")).font(.caption).foregroundStyle(.secondary)
                            Spacer()
                            ReviewSourcePicker(job: job).frame(maxWidth: 240)
                        }
                        HStack(alignment: .top, spacing: 20) {
                            PrimaryArtworkPreview(job: job, kind: "poster").frame(maxWidth: .infinity)
                            PrimaryArtworkPreview(job: job, kind: "fanart").frame(maxWidth: .infinity)
                        }.accessibilityIdentifier("classicArtworkPair")
                    }
                    if job.hasFailedSources { sourceFailures }
                    if !job.warnings.isEmpty {
                        DisclosureGroup(tr("technicalDetails")) {
                            ForEach(job.warnings, id: \.self) { warning in Label(warning, systemImage: "exclamationmark.triangle").font(.callout).foregroundStyle(.orange).textSelection(.enabled) }
                        }
                    }
                    Picker(tr("workspaceTab"), selection: $tab) {
                        Text(tr("metadata")).tag("metadata")
                        Text(tr("artwork")).tag("artwork")
                    }.pickerStyle(.segmented).frame(maxWidth: 270)
                    if tab == "artwork" { artworkPage(viewportWidth: viewport.size.width) }
                    else {
                        metadataSection("metadataTitles", fields: ["title", "originalTitle"], columns: 1)
                        metadataSection("metadataBasics", fields: ["id", "releaseDate", "runtimeMinutes", "rating"], columns: viewport.size.width < 500 ? 1 : 2)
                        metadataSection("metadataPeople", fields: ["actorsText", "genresText", "director", "maker", "label"], columns: viewport.size.width < 500 ? 1 : 2)
                        VStack(alignment: .leading, spacing: 6) {
                            HStack { Text(tr("field.plot")).font(.callout).foregroundStyle(.secondary); Spacer(); CandidateButton(field: "plot", job: job) }
                            NativeMetadataEditor(session: store.editorSession(field: "plot", jobID: job.jobID), value: store.value("plot", job: job), label: tr("field.plot"), editable: !store.working && !store.statusUnconfirmed && !job.busy, multiline: true) { store.edit("plot", value: $0, job: job) }
                                .id(job.jobID + "/plot").frame(minHeight: 150)
                        }.disabled(store.working || job.busy)
                        sourceInfo
                    }
                    ForEach(job.videoPaths, id: \.self) { path in
                        Button { store.reveal(path) } label: {
                            Label(URL(fileURLWithPath: path).lastPathComponent, systemImage: "doc")
                                .font(.caption).lineLimit(1).truncationMode(.middle)
                        }.buttonStyle(.link).help(path)
                    }
                }.padding(20).frame(maxWidth: 1050, alignment: .leading).frame(maxWidth: .infinity)
            }
        }
    }
    private func metadataSection(_ title: String, fields: [String], columns: Int) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            if title != "metadataTitles" { Text(tr(title)).font(.caption).foregroundStyle(.secondary) }
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(minimum: 120), spacing: 18), count: columns), alignment: .leading, spacing: 16) {
                ForEach(fields, id: \.self) { field in MetadataFieldView(field: field, job: job) }
            }.disabled(store.working || job.busy)
        }
    }
    private var sourceFailures: some View {
        VStack(alignment: .leading, spacing: 8) {
            ForEach(Array(job.searchAttempts.filter { !$0["success"].boolValue }.enumerated()), id: \.offset) { _, attempt in
                VStack(alignment: .leading, spacing: 5) {
                    Text(attempt["displayName"].stringValue.isEmpty ? attempt["source"].stringValue : attempt["displayName"].stringValue).font(.headline)
                    let key = "error." + attempt["error"]["reason"].stringValue
                    Text(tr(key) == key ? tr("operationFailed") : tr(key)).font(.callout)
                    DisclosureGroup(tr("technicalDetails")) { Text(attempt["error"]["message"].stringValue).font(.caption).textSelection(.enabled) }
                    if ["libredmm", "r18dev"].contains(attempt["source"].stringValue) {
                        Button(tr("retrySources")) { store.search(retry: true, sourceOverride: attempt["source"].stringValue) }.disabled(!store.canRetrySources)
                    }
                }
            }
        }
    }
    private var header: some View {
        HStack(alignment: .top, spacing: 16) {
            VStack(alignment: .leading, spacing: 10) {
                Text(job.metadata["id"].stringValue.isEmpty ? tr("unknownID") : job.metadata["id"].stringValue).font(.title2).fontWeight(.semibold).textSelection(.enabled)
                Text(store.value("title", job: job).isEmpty ? tr("untitledMovie") : store.value("title", job: job)).font(.headline).lineLimit(2)
                Label(store.status(job), systemImage: job.saveState == "Completed" ? "checkmark.circle" : "pencil.line").font(.callout).foregroundStyle(.secondary)
                if store.hasLowResolutionCover(job) {
                    Label(tr("artworkLowResolution"), systemImage: "exclamationmark.triangle").font(.caption).foregroundStyle(.orange)
                    if job.coverQuality["width"].intValue > 0 && job.coverQuality["height"].intValue > 0 { Text(String(format: tr("artworkDimensions"), job.coverQuality["width"].intValue, job.coverQuality["height"].intValue)).font(.caption).foregroundStyle(.secondary) }
                }
            }
            Spacer(minLength: 0)
        }
    }
    private var sourceInfo: some View {
        VStack(alignment: .leading, spacing: 8) {
            Divider()
            HStack {
                Text(tr("source")).foregroundStyle(.secondary)
                Text(metadataSourceLabel(source: job.metadata["sourceName"].stringValue, displayName: job.metadata["sourceDisplayName"].stringValue))
                Spacer()
                if let url = URL(string: job.metadata["sourceUrl"].stringValue), ["http", "https"].contains(url.scheme?.lowercased() ?? "") { Link(tr("openSource"), destination: url) }
            }.font(.caption)
            Text(tr("rawValuesNote")).font(.caption).foregroundStyle(.secondary)
        }
    }
    private func artworkPage(viewportWidth: CGFloat) -> some View {
        // Measure from the outer viewport, not a card's intrinsic image/control
        // size. Each lazy row receives finite, fixed-width card proposals.
        let contentWidth = max(1, min(viewportWidth.isFinite ? viewportWidth : 0, 1050) - 40)
        let columnCount = max(1, Int((contentWidth + 18) / (180 + 18)))
        let columnWidth = max(1, (contentWidth - CGFloat(columnCount - 1) * 18) / CGFloat(columnCount))
        let artwork = store.visibleArtwork(job)
        let rows = stride(from: 0, to: artwork.count, by: columnCount).map {
            Array(artwork[$0..<min($0 + columnCount, artwork.count)])
        }
        return VStack(alignment: .leading, spacing: 18) {
            Text(tr(store.supportsArtwork ? "artworkSelectionNote" : "artworkReadOnlyNote")).font(.callout).foregroundStyle(.secondary)
            ArtworkSourcePicker(job: job)
            if job.artwork.isEmpty { ContentUnavailableView(tr("noArtwork"), systemImage: "photo.on.rectangle", description: Text(tr("noArtworkMessage"))) }
            LazyVStack(alignment: .leading, spacing: 18) {
                ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                    HStack(alignment: .top, spacing: 18) {
                        ForEach(row, id: \.artworkIdentity) { art in
                            let id = store.artworkKey(art, jobID: job.jobID)
                            VStack(alignment: .leading, spacing: 8) {
                                if let url = store.artworkPaths[id] {
                                    Button { store.openArtwork(art, job: job) } label: { CachedArtwork(url: url).frame(width: columnWidth, height: 200).background(Color(nsColor: .controlBackgroundColor)) }.buttonStyle(.plain).accessibilityLabel(tr("viewArtwork"))
                                } else if let error = store.artworkErrors[id] { VStack { Text(tr("artworkPreviewFailed")); DisclosureGroup(tr("technicalDetails")) { Text(error).textSelection(.enabled) } }.font(.caption).foregroundStyle(.secondary).frame(width: columnWidth, height: 200) }
                                else { ProgressView().frame(width: columnWidth, height: 200) }
                                Text(tr("artwork." + art["kind"].stringValue)).font(.headline)
                                Text(art["source"].stringValue == "local" ? tr("localSource") : art["source"].stringValue).font(.caption).foregroundStyle(.secondary)
                                if let dimensions = store.artworkDimensions[id] { Text(String(format: tr("artworkDimensions"), Int(dimensions.width), Int(dimensions.height))).font(.caption2).foregroundStyle(.secondary) }
                                if store.artworkLowResolution.contains(id) { Label(tr("artworkLowResolution"), systemImage: "exclamationmark.triangle").font(.caption).foregroundStyle(.orange) }
                                if art["source"].stringValue == "local" { Button(tr("trashArtwork")) { store.trashArtwork(art, jobID: job.jobID) }.font(.caption) }
                                if store.supportsArtwork {
                                    if art["kind"].stringValue == "sample" {
                                        Toggle(tr("selectedSample"), isOn: Binding(get: { store.artworkSelected(art, jobID: job.jobID) }, set: { store.selectArtwork(art, jobID: job.jobID, selected: $0) })).toggleStyle(.checkbox)
                                    } else {
                                        Button(art["selected"].boolValue ? tr("selectedCoverSource") : tr("useCoverSource")) { store.selectArtworkSource(art["source"].stringValue, jobID: job.jobID) }.disabled(art["selected"].boolValue)
                                    }
                                }
                            }.frame(width: columnWidth, alignment: .leading).disabled(store.working || store.statusUnconfirmed).modifier(ArtworkReadLifetime(artwork: art, job: job))
                        }
                    }.frame(width: contentWidth, alignment: .leading)
                }
            }.frame(width: contentWidth, alignment: .leading)
        }
    }
}

extension JSONValue {
    var artworkIdentity: String { self["artworkId"].stringValue }
}

struct ReviewSourcePicker: View {
    @EnvironmentObject var store: WorkspaceStore
    let job: MovieJobSnapshot
    var body: some View {
        Picker(tr("source"), selection: Binding(get: { store.reviewSource(job) }, set: { store.selectReviewSource($0, jobID: job.jobID) })) {
            Text(tr("reviewSourceMixed")).tag("").disabled(true)
            ForEach(job.reviewSources, id: \.self) { source in
                Text(source == "local" ? tr("reviewSourceLocal") : source == "libredmm" ? "LibreDMM" : "R18.dev").tag(source)
            }
        // AppKit's popup can retain its selected label across a live language
        // change even after the menu items update. Recreate only the control.
        }.id(store.language).help(tr("reviewSourceHelp")).accessibilityIdentifier("reviewSourcePicker")
            .disabled(job.reviewSources.isEmpty || !store.canAct)
    }
}

struct ArtworkSourcePicker: View {
    @EnvironmentObject var store: WorkspaceStore
    let job: MovieJobSnapshot
    var fromViewer = false
    var compact = false
    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Picker(tr("source"), selection: Binding(get: { fromViewer ? store.viewerSource : store.artworkSource(job) }, set: { store.selectArtworkSource($0, jobID: job.jobID, fromViewer: fromViewer) })) {
                Text(tr("artworkAllSources")).tag("")
                ForEach(ArtworkReviewPolicy.sources(job.artwork).filter { $0 != "sources" }, id: \.self) { source in
                    Text(source == "local" ? tr("localSource") : source).tag(source)
                }
            }.frame(maxWidth: 340).disabled(store.working || store.statusUnconfirmed)
            if !compact { Text(tr("artworkSourceSelectionNote")).font(.caption).foregroundStyle(.secondary) }
        }
    }
}

struct MetadataFieldView: View {
    @EnvironmentObject var store: WorkspaceStore
    let field: String
    let job: MovieJobSnapshot
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack { Text(tr("field." + field)).font(.callout).foregroundStyle(.secondary); Spacer(); CandidateButton(field: field, job: job) }
            NativeMetadataEditor(session: store.editorSession(field: field, jobID: job.jobID), value: store.value(field, job: job), label: tr("field." + field), editable: field != "series" && field != "contentId" && !store.working && !store.statusUnconfirmed && !job.busy) { store.edit(field, value: $0, job: job) }
                .id(job.jobID + "/" + field).frame(height: field == "title" || field == "originalTitle" ? 40 : 31)
            if field == "series" || field == "contentId" { Text(tr("preservedField")).font(.caption2).foregroundStyle(.secondary) }
        }
    }
}

struct CandidateButton: View {
    @EnvironmentObject var store: WorkspaceStore
    let field: String
    let job: MovieJobSnapshot
    @ViewState<Bool> private var expanded = false
    var candidates: [JSONValue] { job.candidates.filter { $0["field"].stringValue == field } }
    var body: some View {
        if !candidates.isEmpty {
            Button { expanded.toggle() } label: { Image(systemName: "square.stack.3d.up").font(.caption) }.buttonStyle(.plain).help(tr("compareSources")).accessibilityLabel(tr("compareSources"))
                .popover(isPresented: $expanded) {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(tr("compareSources") + " · " + tr("field." + field)).font(.headline)
                        ForEach(Array(candidates.enumerated()), id: \.offset) { _, candidate in
                            VStack(alignment: .leading, spacing: 6) {
                                HStack { Text(metadataSourceLabel(source: candidate["source"].stringValue, displayName: candidate["sourceDisplayName"].stringValue)).font(.caption).foregroundStyle(.secondary); Spacer(); if candidate["selected"].boolValue { Image(systemName: "checkmark") } }
                                Text(candidate["value"].stringValue).lineLimit(6).textSelection(.enabled)
                                Button(tr("useValue")) { store.selectCandidate(candidate, jobID: job.jobID); expanded = false }.disabled(store.working)
                            }
                            Divider()
                        }
                    }.padding(18).frame(width: 380)
                }
        }
    }
}

private func metadataSourceLabel(source: String, displayName: String) -> String {
    switch source {
    case "local-nfo": return tr("source.localNfo")
    case "manual": return tr("source.manual")
    default: return displayName.isEmpty ? (source.isEmpty ? tr("localAndManual") : source) : displayName
    }
}

enum SourceProfileFields {
    static let fields: [(key: String, label: String)] = [
        ("titleSource", "field.title"), ("originalTitleSource", "field.originalTitle"),
        ("releaseDateSource", "field.releaseDate"), ("runtimeSource", "field.runtimeMinutes"),
        ("makerSource", "field.maker"), ("directorSource", "field.director"),
        ("labelSource", "field.label"), ("actorsSource", "field.actorsText"),
        ("genresSource", "field.genresText"), ("plotSource", "field.plot"),
        ("ratingSource", "field.rating"), ("artworkSource", "artwork")
    ]
    static var defaults: [String: String] { Dictionary(uniqueKeysWithValues: fields.map { ($0.key, "libredmm") }) }
    static func normalized(_ profile: [String: String]) -> [String: String] {
        Dictionary(uniqueKeysWithValues: fields.map { ($0.key, profile[$0.key] == "r18dev" ? "r18dev" : "libredmm") })
    }
}

struct SourceProfileView: View {
    @EnvironmentObject var store: WorkspaceStore
    @ViewState private var draft = SourceProfileFields.defaults
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text(tr("configureSources")).font(.title2.weight(.semibold))
            Text(tr("customSourcesHint")).font(.callout).foregroundStyle(.secondary)
            Form {
                ForEach(SourceProfileFields.fields, id: \.key) { field in
                    Picker(tr(field.label), selection: Binding(get: { draft[field.key] ?? "libredmm" }, set: { draft[field.key] = $0 })) {
                        Text("LibreDMM").tag("libredmm")
                        Text("R18.dev").tag("r18dev")
                    }
                }
            }.formStyle(.grouped)
            HStack {
                Button(tr("cancel")) { store.showingSourceProfile = false }.keyboardShortcut(.cancelAction)
                Spacer()
                Button(tr("save")) {
                    store.sourceProfile = SourceProfileFields.normalized(draft)
                    store.sourceMode = "custom"
                    store.showingSourceProfile = false
                }.buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction)
            }
        }.padding(22).frame(width: 510, height: 610).onAppear { draft = store.sourceProfile }
    }
}
