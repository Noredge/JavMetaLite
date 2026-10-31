import SwiftUI
import AppKit
import UniformTypeIdentifiers
#if SWIFT_PACKAGE
import WorkerKit
import NativeBridgeKit
#endif

struct WorkspaceView: View {
    @EnvironmentObject var store: WorkspaceStore
    @FocusState private var searchFocused: Bool
    @FocusState private var queueFocused: Bool
    @ViewState<Bool> private var showingDiagnostics = false
    @ViewState<String> private var diagnosticDetails = ""

    var body: some View {
        VStack(spacing: 0) {
            HSplitView {
                if store.queueVisible { queuePane.frame(minWidth: 190, idealWidth: 215, maxWidth: 280) }
                detailPane.frame(minWidth: 440, maxWidth: .infinity, maxHeight: .infinity)
                if store.inspectorVisible {
                    SaveInspector().frame(minWidth: 250, idealWidth: 260, maxWidth: 300)
                }
            }.frame(maxWidth: .infinity, maxHeight: .infinity)
            statusBar
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .toolbar {
            ToolbarItem(placement: .navigation) {
                Button { store.queueVisible.toggle() } label: { Image(systemName: "sidebar.left") }
                    .help(tr("toggleQueue")).accessibilityLabel(tr("toggleQueue"))
            }
            ToolbarItem(placement: .navigation) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("JavMetaLite").font(.headline)
                    Text(String(format: tr("movieCount"), store.jobs.count)).font(.caption2).foregroundStyle(.secondary)
                }
            }
            ToolbarItem(placement: .automatic) { Spacer() }
            ToolbarItemGroup(placement: .primaryAction) {
                Button { store.chooseFiles() } label: { Label(tr("addMoviesShort"), systemImage: "plus") }
                    .labelStyle(.titleAndIcon).disabled(!store.canImportFiles)
                HStack(spacing: 0) {
                    Button { store.searchActionTargets() } label: { Label(queryLabel, systemImage: "arrow.clockwise") }
                        .id(queryLabel).labelStyle(.titleAndIcon).accessibilityLabel(queryLabel)
                        .disabled(!store.canSearchActionTargets).help(queryLabel + " · ⌘R")
                    Menu {
                        Picker(tr("querySource"), selection: $store.sourceMode) {
                            Text("LibreDMM").tag("libredmm")
                            Text("R18.dev").tag("r18dev").disabled(!store.capabilities["searchR18Dev"].boolValue)
                            Text(tr("customSources")).tag("custom").disabled(!store.capabilities["searchR18Dev"].boolValue)
                        }
                        Button(tr("configureSources")) { store.showingSourceProfile = true }
                        Divider()
                        Button(tr("manualBrowserShort")) { store.openBrowser() }.disabled(!store.canOpenBrowser)
                    } label: { Image(systemName: "chevron.down") }
                    .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize().help(tr("queryOptions")).accessibilityLabel(tr("queryOptions"))
                    .disabled(!store.ready || store.working || store.statusUnconfirmed)
                }
                Button { store.saveActionTargets() } label: {
                    Label(String(format: tr("saveActionCount"), store.actionTargetCount), systemImage: "checkmark")
                }.labelStyle(.titleAndIcon).buttonStyle(.borderedProminent).disabled(!store.canPreviewActionTargets).help(tr(store.skipSavePreview ? "saveSafeguards" : "previewEnabledNote") + " · ⌘S")
            }
            ToolbarItem(placement: .primaryAction) {
                Button { store.inspectorVisible.toggle() } label: {
                    Label(tr("saveSettings"), systemImage: "sidebar.right")
                }.help(tr("inspector")).accessibilityLabel(tr("inspector"))
            }
        }
            .onDrop(of: [UTType.fileURL], isTargeted: nil) { providers in
                guard store.canImportFiles else { return false }
                let group = DispatchGroup(); let lock = NSLock(); var paths: [String] = []
                for provider in providers {
                    group.enter()
                    provider.loadDataRepresentation(forTypeIdentifier: UTType.fileURL.identifier) { data, _ in
                        defer { group.leave() }
                        if let data, let url = URL(dataRepresentation: data, relativeTo: nil) { lock.lock(); paths.append(url.path); lock.unlock() }
                    }
                }
                group.notify(queue: .main) { Task { await store.importPaths(paths) } }
                return true
            }
            .onReceive(NotificationCenter.default.publisher(for: .focusQueueSearch)) { _ in
                store.queueVisible = true
                DispatchQueue.main.async { searchFocused = true }
            }
            .sheet(isPresented: $store.showingSourceProfile) { SourceProfileView().environmentObject(store) }
            .sheet(isPresented: Binding(get: { store.discoveryReview != nil }, set: { if !$0 { store.discardDiscovery() } })) { DiscoveryReviewView().environmentObject(store) }
            .sheet(item: $store.preview, onDismiss: { store.previewDidDismiss() }) { preview in SaveReviewView(preview: preview).environmentObject(store) }
            .sheet(isPresented: $store.showingResults) { BatchResultView().environmentObject(store) }
            .sheet(isPresented: $store.showingTransactionRecovery) { TransactionRecoveryView().environmentObject(store) }
            .sheet(isPresented: $store.showingParts, onDismiss: { store.partsReviewDidDismiss() }) { MultipartReviewView().environmentObject(store) }
            .sheet(item: $store.browser, onDismiss: { store.browser?.close() }) { BrowserImportView(session: $0).environmentObject(store) }
            .sheet(isPresented: $store.showingImage) { ArtworkViewer().environmentObject(store) }
            .sheet(isPresented: $showingDiagnostics, onDismiss: { store.dismissErrorDetails() }) {
                VStack(alignment: .leading, spacing: 16) {
                    Text(tr("technicalDetails")).font(.headline)
                    NativeDiagnosticTextView(text: diagnosticDetails, accessibilityLabel: tr("technicalDetails"))
                    HStack {
                        Button(tr("copyDetails")) { if !DiagnosticClipboard.copy(diagnosticDetails) { NSSound.beep() } }.disabled(diagnosticDetails.isEmpty)
                        Spacer()
                        Button(tr("close")) { showingDiagnostics = false }.keyboardShortcut(.cancelAction)
                    }
                }.padding(24).frame(width: 700, height: 440)
            }
            .alert(tr("attention"), isPresented: Binding(get: { store.errorMessage != nil && !store.showingTransactionRecovery }, set: { if !$0 && !store.showingTransactionRecovery { store.errorMessage = nil } })) {
                Button(tr("ok"), role: .cancel) { store.errorMessage = nil }
                if !store.errorDetails.isEmpty {
                    Button(tr("technicalDetails")) {
                        diagnosticDetails = store.errorDetails; store.errorMessage = nil
                        DispatchQueue.main.async { showingDiagnostics = true }
                    }
                }
            } message: { Text(store.errorMessage ?? "") }
    }

    private var queryLabel: String {
        String(format: tr("queryAction." + store.actionTargetScope), store.actionTargetCount)
    }
    private var queuePane: some View {
        VStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    Text(tr("queue")).font(.headline)
                    Text("\(store.jobs.count)").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                    Spacer()
                    Button(tr(store.allMoviesChecked ? "uncheckAllMovies" : "checkAllMovies")) { store.toggleAllChecks() }
                        .buttonStyle(.plain).font(.caption).disabled(!store.ready || store.working || store.statusUnconfirmed || store.jobs.isEmpty).help(tr("checkAllMoviesHelp")).accessibilityIdentifier("toggleAllQueueChecks")
                    Menu {
                        Button(tr("selectRecognizedQueue")) { store.selectRecognized() }
                        Button(tr("selectPendingQueue")) { store.selectCategory(.pending) }
                        Button(tr("selectIssuesQueue")) { store.selectCategory(.issues) }
                        Button(tr("clearChecks")) { store.checkedJobIDs.removeAll() }
                        Divider()
                        Button(tr("confirmParts")) { store.reviewParts() }.disabled(!store.canReviewParts)
                    } label: { Image(systemName: "ellipsis.circle") }
                        .menuStyle(.borderlessButton).fixedSize().help(tr("queueActions")).accessibilityLabel(tr("queueActions"))
                        .disabled(!store.ready || store.working || store.statusUnconfirmed)
                }
                HStack(spacing: 6) {
                    Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                    TextField(tr("searchQueue"), text: $store.searchText).textFieldStyle(.plain).focused($searchFocused).help(tr("queueSearchHint"))
                }.padding(7).background(Color(nsColor: .textBackgroundColor), in: RoundedRectangle(cornerRadius: 7))
                Picker(tr("queueFilter"), selection: $store.queueCategory) {
                    ForEach(QueueCategory.allCases, id: \.self) { category in
                        Text(String(format: tr("queueCategory." + category.rawValue), store.categoryJobs(category).count)).tag(category)
                    }
                }.labelsHidden().frame(maxWidth: .infinity)
            }.padding(14)
            List(selection: Binding<String?>(get: { store.queueNavigationSelection.first }, set: { store.selectQueueNavigation($0.map { Set([$0]) } ?? []) })) {
                ForEach(store.visibleJobs) { job in
                    QueueRow(job: job, focusQueue: { queueFocused = true }).tag(job.jobID)
                        .contextMenu {
                            if let path = job.videoPaths.first { Button(tr("showFinder")) { store.reveal(path) } }
                            Button(tr("removeFromQueue")) { store.remove(job) }.disabled(store.working || job.busy)
                        }
                }
            }.listStyle(.sidebar).focusable().focused($queueFocused)
            .onKeyPress(keys: ["a", "A"], phases: .down) { press in
                guard press.modifiers.contains(.command) else { return .ignored }
                store.setAllChecks(!press.modifiers.contains(.shift)); return .handled
            }
            .onKeyPress(.space) {
                guard !store.queueNavigationSelection.isEmpty else { return .ignored }
                store.toggleQueueNavigationChecks(); return .handled
            }
            VStack(alignment: .leading, spacing: 10) {
                Text(tr("queueSelectionHint")).font(.caption2).foregroundStyle(.secondary)
                HStack {
                    Text(String(format: tr("checkedMovieCount"), store.checkedJobs.count)).foregroundStyle(.secondary)
                    Spacer(minLength: 0)
                    Button(tr("clearQueueShort"), role: .destructive) { store.requestClearQueue() }
                        .disabled(!store.canClearQueue)
                }.font(.caption).controlSize(.small)
                if store.sourceMode != "libredmm" { Text(tr("r18BatchSlowNote")).font(.caption2).foregroundStyle(.secondary) }
            }.padding(12)
        }.background(Color(nsColor: .windowBackgroundColor))
    }
    private var detailPane: some View {
        VStack(spacing: 0) {
            if !store.checkedJobs.isEmpty {
                HStack {
                    Text(String(format: tr("checkedMovieCount"), store.checkedJobs.count)).font(.callout).foregroundStyle(.secondary)
                    Spacer()
                    if store.showingBatchOverview {
                        Button(tr("viewCurrentMovie")) { store.showCurrentMovie() }.disabled(store.selectedJob == nil)
                    } else {
                        Button { store.showBatchOverview() } label: { Label(tr("batchOverview"), systemImage: "rectangle.stack") }
                            .accessibilityIdentifier("showCheckedBatchOverview")
                    }
                }.padding(.horizontal, 20).padding(.vertical, 9)
                Divider()
            }
            if store.showingBatchOverview {
                BatchSummaryView(checkedJobs: store.checkedJobs)
            } else if let job = store.selectedJob {
                EditorView(job: job, tab: $store.detailTab)
            } else {
                ContentUnavailableView {
                    Label(tr("workspaceHeading"), systemImage: "film.stack")
                } description: {
                    Text(tr("workspaceEmptySteps"))
                } actions: {
                    Button(tr("addMovies")) { store.chooseFiles() }.buttonStyle(.borderedProminent).disabled(!store.canImportFiles)
                    Text(tr("dropHint")).font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }
    private var phaseLabel: String {
        let key = "phase." + store.phase.lowercased()
        let localized = tr(key)
        let label = localized == key ? tr("working") : localized
        if store.currentJobIndex > 0 && store.totalOperationJobs > 1 { return String(format: tr("batchProgress"), store.currentJobIndex, store.totalOperationJobs) + " · " + label }
        return label
    }
    private var statusBar: some View {
        VStack(spacing: 0) {
            Divider()
            HStack(spacing: 8) {
                Text(String(format: tr("movieCount"), store.jobs.count)).foregroundStyle(.secondary)
                Divider().frame(height: 12)
                if store.engineStarting {
                    ProgressView().controlSize(.small)
                    Text(tr("engineStarting")); Spacer()
                } else if store.engineProcessExited {
                    Image(systemName: "exclamationmark.triangle").foregroundStyle(.orange)
                    Text(tr("engineExited")); Spacer()
                    if store.canRetryEngineStart { Button(tr("retryEngineStart")) { store.retryEngineStart() } }
                    Button(tr("exportReviewRecovery")) { store.presentReviewRecoveryExport() }.disabled(!store.canEndExitedEngineSession)
                    Button(tr("endSession")) { NSApp.terminate(nil) }.disabled(!store.canEndExitedEngineSession)
                } else if store.engineStartupFailed && !store.statusUnconfirmed {
                    Image(systemName: "exclamationmark.triangle").foregroundStyle(.orange)
                    Text(tr("engineStartupFailed")); Spacer()
                    Button(tr("retryEngineStart")) { store.retryEngineStart() }.disabled(!store.canRetryEngineStart)
                } else if store.statusUnconfirmed {
                    Image(systemName: "exclamationmark.triangle").foregroundStyle(.orange)
                    Text(tr("statusUnconfirmed")); Spacer()
                    Button(tr("checkStatus")) { store.checkStatus() }.disabled(store.working)
                    if store.operationID != nil { Button(tr("cancelOperation")) { store.cancel() } }
                    Button(tr("openLogs")) { store.openLogs() }.disabled(store.logRoot == nil)
                }
                else if store.working { ProgressView().controlSize(.small); Text(phaseLabel); Spacer(); Button(tr("cancel")) { store.cancel() }.disabled(store.operationID == nil) }
                else if let result = store.resultMessage { Image(systemName: store.operationHadProblems ? "exclamationmark.triangle" : "checkmark.circle").foregroundStyle(store.operationHadProblems ? .orange : .green); Text(result).lineLimit(2); Spacer(); if !store.operationResults.isEmpty { Button(tr("viewResults")) { store.showingResults = true } }; if let path = store.savedOutputs.first { Button(tr("showFinder")) { store.reveal(path) } } }
                else { Text(store.selectedJob.map(store.status) ?? tr("localFirst")); Spacer(); if store.unsavedCount > 0 { Text(String(format: tr("dirtyCount"), store.unsavedCount)).foregroundStyle(.secondary) } }
            }.font(.caption).padding(.horizontal, 16).padding(.vertical, 9)
        }
    }
}

struct QueueRow: View {
    @EnvironmentObject var store: WorkspaceStore
    let job: MovieJobSnapshot
    var focusQueue: (() -> Void)? = nil
    var body: some View {
        HStack(spacing: 10) {
            Toggle(tr("includeInBatch"), isOn: Binding(get: { store.checkedJobIDs.contains(job.jobID) }, set: { store.setChecked(job.jobID, checked: $0) }))
                .toggleStyle(.checkbox).labelsHidden().help(tr("includeInBatch"))
                .accessibilityLabel(tr("includeInBatch") + " · " + store.value("id", job: job))
                .accessibilityIdentifier("queueCheck-" + job.jobID).disabled(store.working || store.statusUnconfirmed)
            Button { store.selectNavigation([job.jobID]); focusQueue?() } label: {
                HStack(spacing: 10) {
            if let art = job.artwork.first(where: { $0["selected"].boolValue && $0["kind"].stringValue != "sample" }) ?? job.artwork.first, let url = store.artworkPaths[store.artworkKey(art, jobID: job.jobID)] { CachedArtwork(url: url, maximumPixelSize: 128).frame(width: 34, height: 48).clipShape(RoundedRectangle(cornerRadius: 3)) }
            else { Image(systemName: "film").font(.title2).foregroundStyle(.secondary).frame(width: 34, height: 48) }
            VStack(alignment: .leading, spacing: 3) {
                Text(job.metadata["id"].stringValue.isEmpty ? tr("unknownID") : job.metadata["id"].stringValue).font(.headline)
                Text(store.value("title", job: job).isEmpty ? job.videoPaths.first.map { URL(fileURLWithPath: $0).lastPathComponent } ?? "" : store.value("title", job: job)).font(.caption).lineLimit(1)
                if store.hasLowResolutionCover(job) {
                    Label { Text(store.status(job)).lineLimit(nil).fixedSize(horizontal: false, vertical: true) } icon: { Image(systemName: "exclamationmark.triangle.fill") }
                        .font(.caption2).foregroundStyle(.orange)
                } else { Text(store.status(job)).font(.caption2).foregroundStyle(.secondary).lineLimit(nil).fixedSize(horizontal: false, vertical: true) }
            }.frame(maxWidth: .infinity, alignment: .leading)
                }.contentShape(Rectangle())
            }.buttonStyle(.plain).accessibilityIdentifier("queueOpen-" + job.jobID)
        }.padding(.vertical, 4).help(job.videoPaths.joined(separator: "\n"))
        .modifier(ArtworkReadLifetime(artwork: job.artwork.first(where: { $0["selected"].boolValue && $0["kind"].stringValue != "sample" }) ?? job.artwork.first, job: job, scope: "queue"))
    }
}

struct SaveInspector: View {
    @EnvironmentObject var store: WorkspaceStore
    private var outputPreset: Binding<String> {
        Binding(get: {
            if store.writeNfo && !store.writePoster && !store.writeFanart && !store.writeSamples { return "metadata" }
            if store.writeNfo && store.writePoster && store.writeFanart && !store.writeSamples { return "artwork" }
            return "custom"
        }, set: { value in
            guard value != "custom" else { return }
            store.writeNfo = true
            store.writePoster = value == "artwork" && store.supportsArtwork
            store.writeFanart = value == "artwork" && store.supportsArtwork
            store.writeSamples = false
        })
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 6) {
                HStack { Text(tr("saveSettings")).font(.headline); Spacer(); Image(systemName: "slider.horizontal.3").foregroundStyle(.secondary) }
                Text(String(format: tr("actionScope." + store.actionTargetScope), store.actionTargetCount))
                    .font(.caption).foregroundStyle(.secondary)
            }.padding(16)
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    VStack(alignment: .leading, spacing: 8) {
                        sectionLabel("outputPreset")
                        Picker(tr("outputPreset"), selection: outputPreset) {
                            Text(tr("presetMetadata")).tag("metadata")
                            Text(tr("presetArtwork")).tag("artwork").disabled(!store.supportsArtwork)
                            Text(tr("presetCustom")).tag("custom")
                        }.labelsHidden()
                    }
                    VStack(alignment: .leading, spacing: 12) {
                        sectionLabel("output")
                        Toggle(tr("nfoOutput"), isOn: $store.writeNfo)
                        Toggle(tr("artwork.poster"), isOn: $store.writePoster).disabled(!store.supportsArtwork)
                        Toggle(tr("artwork.fanart"), isOn: $store.writeFanart).disabled(!store.supportsArtwork)
                        Toggle(tr("writeSamples"), isOn: $store.writeSamples).disabled(!store.supportsArtwork)
                        if store.writeSamples {
                            Toggle(tr("replaceLocalSamples"), isOn: $store.replaceLocalSamples).help(tr("replaceSamplesNote"))
                        }
                        if !store.supportsArtwork { Text(tr("nfoOnlyNote")).font(.caption).foregroundStyle(.secondary) }
                    }
                    Divider()
                    VStack(alignment: .leading, spacing: 8) {
                        sectionLabel("location")
                        Picker(tr("destinationMode"), selection: $store.targetMode) {
                            Text(tr("originalLocation")).tag("VideoDirectory")
                            Text(tr("sourceNumberFolder")).tag("SourceNumberFolder").disabled(!store.supportsOrganization)
                            Text(tr("customNumberFolder")).tag("CustomRootNumberFolder").disabled(!store.supportsOrganization)
                        }.labelsHidden()
                        if store.targetMode == "CustomRootNumberFolder" {
                            Button(tr("chooseOrganizationRoot")) { store.chooseOrganizationRoot() }
                            Text(store.customRootDirectory.isEmpty ? tr("chooseRootRequired") : store.customRootDirectory).font(.caption).textSelection(.enabled)
                        }
                    }
                    VStack(alignment: .leading, spacing: 0) {
                        Button { store.moreOptionsExpanded.toggle() } label: {
                            HStack(spacing: 6) {
                                Image(systemName: store.moreOptionsExpanded ? "chevron.down" : "chevron.right").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
                                Text(tr("moreOptions"))
                                Spacer(minLength: 0)
                            }.frame(maxWidth: .infinity, minHeight: 24, alignment: .leading).contentShape(Rectangle())
                        }.buttonStyle(.plain).accessibilityLabel(tr("moreOptions"))
                            .accessibilityValue(tr(store.moreOptionsExpanded ? "expanded" : "collapsed")).accessibilityIdentifier("classicMoreOptions")
                        if store.moreOptionsExpanded { VStack(alignment: .leading, spacing: 12) {
                            Toggle(tr("renameVideoExplicit"), isOn: $store.renameVideo)
                                .disabled(!store.supportsOrganization).help(tr("renameVideoHelp"))
                            Toggle(tr("includeIDTitleExplicit"), isOn: $store.includeIDInTitle).help(tr("includeIDTitleHelp"))
                            Toggle(tr("skipSavePreview"), isOn: $store.skipSavePreview).help(tr("skipSavePreviewHelp"))
                        }.padding(.top, 10).padding(.leading, 16) }
                    }
                    if !store.recoveryWarnings.isEmpty {
                        VStack(alignment: .leading, spacing: 8) {
                            sectionLabel("recoveryRequired")
                            ForEach(store.recoveryWarnings, id: \.self) { Text($0).font(.caption).foregroundStyle(.orange).textSelection(.enabled) }
                        }
                    }
                    Button(tr("transactionRecovery")) { store.presentTransactionRecovery() }
                        .disabled(!store.canInspectTransactionRecovery).accessibilityIdentifier("transactionRecovery")
                    Button(tr("exportReviewReference")) { store.presentReviewReferenceExport() }
                        .disabled(!store.canExportReviewReference)
                }.toggleStyle(.checkbox).controlSize(.small).font(.callout).padding(.horizontal, 16).padding(.vertical, 10)
                    .disabled(store.working || store.statusUnconfirmed)
            }
            Label(tr(store.skipSavePreview ? "saveSafeguards" : "previewEnabledNote"), systemImage: "checkmark.shield").font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true).padding(16)
        }.frame(maxHeight: .infinity).background(Color(nsColor: .controlBackgroundColor))
    }
    private func sectionLabel(_ key: String) -> some View { Text(tr(key)).font(.caption).foregroundStyle(.secondary) }
}

private struct TransactionRecoveryView: View {
    @EnvironmentObject var store: WorkspaceStore
    @ViewState<Bool> private var confirmingArchive = false
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(tr("transactionRecovery")).font(.title2.bold())
            Text(tr("recoveryExplanation")).font(.callout).foregroundStyle(.secondary)
            HSplitView {
                VStack(alignment: .leading, spacing: 10) {
                    ScrollView {
                        VStack(alignment: .leading, spacing: 12) {
                            if store.transactionRecoveryRecords.isEmpty { Text(tr("recoveryNoRecords")).foregroundStyle(.secondary) }
                            ForEach(store.transactionRecoveryRecords.indices, id: \.self) { index in
                                let record = store.transactionRecoveryRecords[index]
                                VStack(alignment: .leading, spacing: 5) {
                                    Label(tr(record["blocksSaving"].boolValue ? "recoveryRequired" : "recoveryFinishedRecord"), systemImage: record["blocksSaving"].boolValue ? "exclamationmark.triangle" : "doc.text")
                                    Text(record["recordPath"].stringValue).font(.caption).textSelection(.enabled).lineLimit(3)
                                    Text(tr("recovery.status." + record["status"].stringValue) + " · " + record["startedAt"].stringValue).font(.caption2).foregroundStyle(.secondary)
                                    HStack {
                                        Button(tr("recoveryVerify")) { store.inspectTransactionRecovery(record["recordId"].stringValue) }
                                            .disabled(!store.canInspectTransactionRecovery)
                                        Button(tr("showFinder")) { store.reveal(record["recordPath"].stringValue) }
                                    }
                                }.padding(10).frame(maxWidth: .infinity, alignment: .leading).background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 8))
                            }
                        }.padding(.trailing, 8)
                    }
                    HStack {
                        Button(tr("recoveryRefresh")) { store.reloadTransactionRecovery() }.disabled(!store.canInspectTransactionRecovery)
                        if let offset = store.transactionRecoveryNextOffset {
                            Button(tr("recoveryNextRecords")) { store.reloadTransactionRecovery(offset: offset) }.disabled(!store.canInspectTransactionRecovery)
                        }
                    }
                }.frame(minWidth: 260, idealWidth: 310)
                ScrollView {
                    VStack(alignment: .leading, spacing: 12) {
                        if store.transactionRecoveryInspection == .null {
                            Text(tr("recoverySelectRecord")).foregroundStyle(.secondary)
                        } else {
                            let inspection = store.transactionRecoveryInspection
                            Text(tr(inspection["canArchive"].boolValue ? "recoveryVerified." + inspection["outcome"].stringValue : "recoveryCannotArchive")).font(.headline)
                            ForEach(inspection["issues"].arrayValue.indices, id: \.self) { index in
                                Text(tr("recovery.issue." + inspection["issues"].arrayValue[index].stringValue)).foregroundStyle(.orange)
                            }
                            if !inspection["canArchive"].boolValue {
                                Text(tr("recoveryManualInstructions")).font(.callout).foregroundStyle(.secondary)
                            }
                            ForEach(inspection["files"].arrayValue.indices, id: \.self) { index in
                                let file = inspection["files"].arrayValue[index]
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(file["path"].stringValue).font(.caption).textSelection(.enabled)
                                    Text(tr("recovery.file." + file["state"].stringValue)).font(.caption).foregroundStyle(.secondary)
                                }.padding(.vertical, 5)
                                Divider()
                            }
                        }
                    }.padding(.leading, 14).frame(maxWidth: .infinity, alignment: .leading)
                }.frame(minWidth: 390)
            }
            if !store.transactionRecoveryMessage.isEmpty { Text(store.transactionRecoveryMessage).foregroundStyle(.secondary) }
            if let message = store.errorMessage {
                HStack {
                    Text(message).foregroundStyle(.red).textSelection(.enabled)
                    Spacer()
                    Button(tr("ok")) { store.errorMessage = nil; store.errorDetails = "" }
                }
                if !store.errorDetails.isEmpty {
                    DisclosureGroup(tr("technicalDetails")) {
                        Text(store.errorDetails).font(.caption).textSelection(.enabled)
                    }
                }
            }
            if store.statusUnconfirmed {
                HStack {
                    Text(tr("statusUnconfirmed")).foregroundStyle(.orange)
                    Button(tr("checkStatus")) { store.checkStatus() }.disabled(store.working)
                    if store.operationID != nil { Button(tr("cancel")) { store.cancel() }.disabled(store.working) }
                }
            }
            HStack {
                if store.working {
                    ProgressView().controlSize(.small); Text(tr("recoveryVerifying"))
                    Button(tr("cancel")) { store.cancel() }.disabled(store.operationID == nil)
                } else {
                    Menu(tr("recoveryExports")) {
                        Button(tr("recoveryExportReport")) { store.exportTransactionRecoveryReport() }.disabled(store.transactionRecoveryInspection == .null)
                        Button(tr("exportReviewReference")) { store.presentReviewReferenceExport() }.disabled(!store.canExportReviewReference)
                    }
                }
                Spacer()
                Button(tr("close")) { store.showingTransactionRecovery = false }.keyboardShortcut(.cancelAction).disabled(store.working)
                Button(tr("recoveryArchive")) { confirmingArchive = true }.buttonStyle(.borderedProminent).disabled(!store.canArchiveTransactionRecovery)
            }
        }.padding(24).frame(minWidth: 800, idealWidth: 900, minHeight: 540, idealHeight: 620)
            .interactiveDismissDisabled(store.working)
            .alert(tr("recoveryArchiveTitle"), isPresented: $confirmingArchive) {
                Button(tr("cancel"), role: .cancel) { }
                Button(tr("recoveryArchive")) { store.archiveVerifiedTransactionRecovery() }
            } message: { Text(tr("recoveryArchiveConfirmation")) }
    }
}
