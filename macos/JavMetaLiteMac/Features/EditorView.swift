import SwiftUI
#if SWIFT_PACKAGE
import WorkerKit
#endif

struct EditorView: View {
    @EnvironmentObject var store: WorkspaceStore
    let job: MovieJobSnapshot
    @ViewState private var kind = "poster"
    @ViewState private var editing = false
    @ViewState private var focusField: String?
    @ViewState private var picking = false

    private func value(_ field: String) -> String { store.value(field, job: job) }
    private var title: String { store.displayTitle(for: job) }
    var body: some View {
        GeometryReader { geometry in
            VStack(spacing: 0) {
                HStack(spacing: 10) {
                    QuerySourcePicker().fixedSize().disabled(!store.canAct)
                    Button(tr("searchMetadata")) { store.searchActionTargets() }
                        .buttonStyle(.borderedProminent).disabled(!store.canSearchActionTargets)
                    Spacer(minLength: 2)
                    Button {
                        if editing {
                            store.perform { try await store.flush(job.jobID); editing = false }
                        } else { beginEditing() }
                    } label: {
                        Label(tr(editing ? "doneReview" : "editDetails"), systemImage: editing ? "checkmark" : "square.and.pencil")
                    }.disabled(!store.canAct)
                    Menu {
                        Button(tr("manualBrowserShort")) { store.openBrowser() }.disabled(!store.canOpenBrowser)
                        Button(tr("configureSources")) { store.showingSourceProfile = true }
                            .disabled(!store.capabilities["searchR18Dev"].boolValue)
                    } label: { Image(systemName: "ellipsis.circle") }
                        .fixedSize().accessibilityLabel(tr("queryOptions")).disabled(!store.canAct)
                }.padding(.horizontal, 24).padding(.vertical, 15)
                ScrollView {
                    HStack(alignment: .top, spacing: geometry.size.width < 540 ? 18 : 30) {
                        let width = min(260, max(124, geometry.size.width * 0.33))
                        VStack(spacing: 10) {
                            Button { picking = true } label: {
                                DeskArtwork(job: job, art: ArtworkReviewPolicy.primary(job.artwork, kind: kind), height: width * 1.36, crop: kind == "poster")
                            }.buttonStyle(.plain).accessibilityLabel(tr("chooseImages")).disabled(job.artwork.isEmpty || !store.canAct)
                            .contextMenu {
                                if let art = ArtworkReviewPolicy.primary(job.artwork, kind: kind) {
                                    Button(tr("viewArtwork")) { store.openArtwork(art, job: job) }
                                }
                            }
                            Picker(tr("artwork"), selection: $kind) {
                                Text(tr("artwork.poster")).tag("poster")
                                Text(tr("artwork.fanart")).tag("fanart")
                            }.labelsHidden().pickerStyle(.segmented)
                            Button(tr("chooseImages")) { picking = true }.buttonStyle(.link)
                                .disabled(job.artwork.isEmpty || !store.canAct)
                        }.frame(width: width)
                        VStack(alignment: .leading, spacing: 12) {
                            if editing {
                                ForEach(["id", "title", "originalTitle"], id: \.self) { field in editor(field) }
                            } else {
                                editableText("id", value("id").isEmpty ? tr("unknownID") : value("id"))
                                    .font(.system(.callout, design: .monospaced).weight(.semibold)).foregroundStyle(FilmDesk.accent)
                                editableText("title", title)
                                    .font(.system(size: geometry.size.width < 560 ? 24 : 32, weight: .semibold))
                                if !value("originalTitle").isEmpty && value("originalTitle") != title {
                                    editableText("originalTitle", value("originalTitle"))
                                        .font(.system(.title3, design: .serif).italic()).foregroundStyle(.secondary)
                                }
                            }
                            if !job.reviewSources.isEmpty { cachedResults }
                            Divider()
                            fieldGrid(["actorsText", "maker", "releaseDate", "runtimeMinutes"])
                            Divider()
                            if editing {
                                editor("plot")
                            } else {
                                Text(tr("field.plot")).font(.caption).foregroundStyle(.secondary)
                                editableText("plot", value("plot")).font(.callout).lineSpacing(3)
                            }
                            Divider()
                            fieldGrid(["director", "label", "genresText", "rating"] + (value("series").isEmpty ? [] : ["series"]))
                            if let url = URL(string: value("sourceUrl")), ["http", "https"].contains(url.scheme?.lowercased() ?? "") {
                                Link(tr("openSource"), destination: url).font(.callout)
                            }
                            if job.hasFailedSources || value("id").isEmpty {
                                HStack {
                                    Button(tr(value("id").isEmpty ? "field.id" : "retrySources")) {
                                        if value("id").isEmpty { beginEditing("id") } else { store.search(retry: true) }
                                    }.disabled(!store.canAct)
                                    Button(tr("manualBrowserShort")) { store.openBrowser() }.disabled(!store.canOpenBrowser)
                                }
                            }
                            if store.hasLowResolutionCover(job) { Label(tr("artworkLowResolution"), systemImage: "exclamationmark.triangle").foregroundStyle(.orange).font(.caption) }
                            if !job.warnings.isEmpty {
                                DisclosureGroup(tr("technicalDetails")) {
                                    Text(job.warnings.joined(separator: "\n")).font(.caption).textSelection(.enabled)
                                }
                            }
                        }.frame(maxWidth: .infinity, alignment: .leading)
                    }.padding(.horizontal, 24).padding(.vertical, 10)
                }
                Divider()
                HStack(spacing: 10) {
                    Image(systemName: job.lookupOnly ? "magnifyingglass" : "film").foregroundStyle(.secondary)
                    VStack(alignment: .leading, spacing: 3) {
                        Text(tr(job.lookupOnly ? "lookupByID" : "videoFile")).font(.caption).foregroundStyle(.secondary)
                        if let path = job.videoPaths.first {
                            Menu {
                                ForEach(job.videoPaths, id: \.self) { path in
                                    Button(URL(fileURLWithPath: path).lastPathComponent) { store.reveal(path) }
                                }
                            } label: { Text(URL(fileURLWithPath: path).lastPathComponent).lineLimit(1).truncationMode(.middle) }
                                .menuStyle(.borderlessButton).fixedSize(horizontal: false, vertical: true).help(tr("showFinder"))
                        } else { Text(tr("unlinkedMovie")).font(.callout) }
                    }
                    Spacer()
                    Button { store.inspectorVisible.toggle() } label: { Image(systemName: "slider.horizontal.3") }
                        .help(tr("saveSettings")).accessibilityLabel(tr("saveSettings"))
                    if job.lookupOnly {
                        Button(tr("attachVideo")) { store.attachVideo(to: job.jobID) }.disabled(!store.canAct)
                    } else {
                        Button(store.saveActionLabel) { store.saveActionTargets() }.buttonStyle(.borderedProminent)
                            .disabled(!store.canPreviewActionTargets)
                    }
                }.padding(.horizontal, 22).padding(.vertical, 14)
            }.background(FilmDesk.paper)
        }
        .sheet(isPresented: $picking) { GalleryPicker(job: job, initialKind: kind) }
    }

    private func beginEditing(_ field: String? = nil) {
        guard store.canAct else { return }
        focusField = field
        editing = true
    }

    private func editableText(_ field: String, _ text: String) -> some View {
        Button { beginEditing(field) } label: {
            Text(text.isEmpty ? "—" : text).multilineTextAlignment(.leading).fixedSize(horizontal: false, vertical: true)
        }
        .buttonStyle(.plain).disabled(!store.canAct)
        .accessibilityLabel(tr("field." + field)).accessibilityValue(text)
        .help(tr("editDetails") + " · " + tr("field." + field))
    }

    private func editor(_ field: String) -> some View {
        MetadataFieldView(field: field, job: job)
            .onAppear {
                guard focusField == field else { return }
                DispatchQueue.main.async {
                    guard focusField == field, editing, store.selectedID == job.jobID else { return }
                    let view = store.editorSession(field: field, jobID: job.jobID).textView
                    view.window?.makeFirstResponder(view)
                    focusField = nil
                }
            }
    }

    private func fieldGrid(_ fields: [String]) -> some View {
        LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], alignment: .leading, spacing: 12) {
            ForEach(fields, id: \.self) { field in
                let readOnly = field == "series"
                if editing && !readOnly {
                    editor(field)
                } else {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(tr("field." + field)).font(.caption).foregroundStyle(.secondary)
                        if readOnly { Text(value(field)).font(.callout).textSelection(.enabled) }
                        else { editableText(field, value(field)).font(.callout) }
                    }.frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
    }

    private var cachedResults: some View {
        let current = store.reviewSource(job)
        return Menu {
            ForEach(job.reviewSources, id: \.self) { source in
                Button {
                    store.selectReviewSource(source, jobID: job.jobID, metadataOnly: true)
                } label: {
                    if current == source { Label(sourceName(source), systemImage: "checkmark") }
                    else { Text(sourceName(source)) }
                }.disabled(current == source)
            }
        } label: {
            Text(tr("resultSource") + " · " + (current.isEmpty ? tr("reviewSourceMixed") : sourceName(current)))
        }.menuStyle(.borderlessButton).fixedSize().font(.caption).help(tr("reviewSourceHelp")).disabled(!store.canAct)
    }
}

func sourceName(_ name: String) -> String {
    switch name {
    case "local", "local-nfo": tr("localSource")
    case "libredmm": "LibreDMM"
    case "r18dev": "R18.dev"
    case "javlibrary": "JAVLibrary"
    default: name
    }
}

extension JSONValue {
    var artworkIdentity: String { self["artworkId"].stringValue }
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
            HStack { Text(tr("field." + field)).font(.caption.weight(.medium)).foregroundStyle(.secondary); Spacer(); CandidateButton(field: field, job: job) }
            NativeMetadataEditor(session: store.editorSession(field: field, jobID: job.jobID), value: store.value(field, job: job), label: tr("field." + field), editable: store.canAct, multiline: field == "plot") { store.edit(field, value: $0, job: job) }
                .fieldSurface().id(job.jobID + "/" + field).frame(height: field == "plot" ? 100 : field == "title" ? 48 : 32)
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
