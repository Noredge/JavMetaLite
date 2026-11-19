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
    @ViewState private var dragTarget = false
    @ViewState<Bool> private var showingDiagnostics = false
    @ViewState<String> private var diagnosticDetails = ""

    var body: some View {
        VStack(spacing: 0) {
            HSplitView {
                if store.queueVisible && !store.showingBatchOverview {
                    queuePane.frame(minWidth: 200, idealWidth: 225, maxWidth: 262)
                }
                detailPane.frame(minWidth: 425, maxWidth: .infinity, maxHeight: .infinity)
                if store.inspectorVisible {
                    SaveInspector().frame(minWidth: 250, idealWidth: 260, maxWidth: 300)
                }
            }.frame(maxWidth: .infinity, maxHeight: .infinity)
            statusBar
        }
        .background(FilmDesk.paper)
        .tint(FilmDesk.accent)
        .overlay {
            if dragTarget && store.canImportFiles {
                RoundedRectangle(cornerRadius: 12).strokeBorder(FilmDesk.accent, style: StrokeStyle(lineWidth: 3, dash: [8, 5]))
                    .padding(6).allowsHitTesting(false).accessibilityHidden(true)
            }
        }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                if !store.jobs.isEmpty && !store.showingBatchOverview {
                    Button { store.queueVisible.toggle() } label: { Image(systemName: "sidebar.left") }
                        .help(tr("toggleQueue")).accessibilityLabel(tr("toggleQueue"))
                }
            }
            ToolbarItem(placement: .navigation) {
                Text("JavMetaLite").font(.system(.headline, design: .rounded))
            }
            ToolbarItem(placement: .automatic) { Spacer() }
            ToolbarItemGroup(placement: .primaryAction) {
                if store.showingBatchOverview {
                    Button(store.queryActionLabel) { store.searchActionTargets() }.disabled(!store.canSearchActionTargets)
                    Button(store.saveActionLabel) { store.saveActionTargets() }.disabled(!store.canPreviewActionTargets)
                }
                if !store.queueVisible || store.showingBatchOverview {
                    Button { store.chooseFiles() } label: { Label(tr("addMoviesShort"), systemImage: "plus") }.disabled(!store.canImportFiles)
                }
                Button { store.showingLookup = true } label: { Label(tr("lookupByID"), systemImage: "magnifyingglass") }
                    .labelStyle(.titleAndIcon).disabled(!store.canImportFiles || !store.capabilities["lookupById"].boolValue)
                if store.showingBatchOverview || store.jobs.isEmpty {
                    Button { store.inspectorVisible.toggle() } label: { Label(tr("saveSettings"), systemImage: "slider.horizontal.3") }
                }
            }
        }
            .onDrop(of: [UTType.fileURL], isTargeted: $dragTarget) { providers in
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
                guard !store.jobs.isEmpty else { return }
                if !store.showingBatchOverview { store.queueVisible = true }
                DispatchQueue.main.async { searchFocused = true }
            }
            .sheet(isPresented: $store.showingLookup) { NumberLookup().environmentObject(store) }
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

    private var queueTools: some View {
        HStack(spacing: 8) {
            Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
            TextField(tr("searchQueue"), text: $store.searchText).textFieldStyle(.plain).focused($searchFocused)
            if !store.searchText.isEmpty {
                Button { store.searchText = "" } label: { Image(systemName: "xmark.circle.fill") }
                    .buttonStyle(.plain).accessibilityLabel(tr("clearSearch"))
            }
            Menu {
                Picker(tr("queueFilter"), selection: $store.queueCategory) {
                    ForEach(QueueCategory.allCases, id: \.self) { category in
                        Text(String(format: tr("queueCategory." + category.rawValue), store.categoryJobs(category).count)).tag(category)
                    }
                }
            } label: { Image(systemName: store.queueCategory == .all ? "line.3.horizontal.decrease.circle" : "line.3.horizontal.decrease.circle.fill") }
                .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize().accessibilityLabel(tr("queueFilter")).help(tr("queueFilter"))
        }.padding(8).background(FilmDesk.surface, in: RoundedRectangle(cornerRadius: 7))
    }

    private var queuePane: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline) {
                Text(tr("movieShelf")).font(.title3.weight(.semibold))
                Text(String(store.jobs.count)).font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                Spacer()
                Menu {
                    Button(tr("clearQueueShort"), role: .destructive) { store.requestClearQueue() }.disabled(!store.canClearQueue)
                } label: { Image(systemName: "ellipsis.circle") }
                    .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize().accessibilityLabel(tr("queueActions"))
            }.padding(.horizontal, 16).padding(.top, 17)
            if !store.jobs.isEmpty { queueTools.padding(12) }
            if !store.jobs.isEmpty && store.visibleJobs.isEmpty { noMatches } else {
                List(selection: Binding<String?>(get: { store.queueNavigationSelection.first },
                    set: { store.selectQueueNavigation($0.map { [$0] } ?? []) })) {
                    ForEach(store.visibleJobs) { job in
                        QueueRow(job: job).tag(job.jobID)
                            .contextMenu {
                                if let path = job.videoPaths.first { Button(tr("showFinder")) { store.reveal(path) } }
                                Button(tr("removeFromQueue")) { store.remove(job) }.disabled(store.working || job.busy)
                            }
                    }
                }.listStyle(.sidebar).scrollContentBackground(.hidden)
            }
            Spacer(minLength: 0)
            if store.jobs.count > 1 {
                Button { store.showBatchOverview() } label: { Label(tr("batchMode"), systemImage: "checklist") }
                    .buttonStyle(.link).disabled(!store.ready || store.working || store.statusUnconfirmed)
            }
            Button { store.chooseFiles() } label: {
                VStack(spacing: 5) {
                    Image(systemName: "folder.badge.plus")
                    Text(tr("dropMovie")).fontWeight(.medium)
                    Text(tr("clickChoose")).font(.caption).foregroundStyle(.secondary)
                }.frame(maxWidth: .infinity).padding(12).contentShape(Rectangle())
                    .overlay { RoundedRectangle(cornerRadius: 8).strokeBorder(.secondary.opacity(0.5), style: StrokeStyle(lineWidth: 1, dash: [4, 3])) }
            }.buttonStyle(.plain).disabled(!store.canImportFiles).padding(12)
        }.frame(maxHeight: .infinity, alignment: .top).background(FilmDesk.shelf)
    }

    private var noMatches: some View {
        VStack(spacing: 10) {
            Text(tr("noMovieMatches")).foregroundStyle(.secondary)
            Button(tr("resetFilter")) { store.searchText = ""; store.queueCategory = .all }
        }.padding().frame(maxWidth: .infinity)
    }

    @ViewBuilder private var detailPane: some View {
        if store.jobs.isEmpty {
            ImportDesk()
        } else if store.showingBatchOverview {
            VStack(alignment: .leading, spacing: 16) {
                HStack {
                    Button { store.showCurrentMovie() } label: { Label(tr("backToMovie"), systemImage: "chevron.left") }
                    Spacer()
                    queueTools.frame(maxWidth: 240)
                }
                BatchSummaryView()
                if store.visibleJobs.isEmpty { noMatches }
            }.padding(24).background(FilmDesk.paper)
        } else if let job = store.selectedJob {
            EditorView(job: job).id(job.jobID)
        } else {
            Button(tr("chooseMovie")) { if let first = store.visibleJobs.first ?? store.jobs.first { store.selectNavigation([first.jobID]) } }
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
                else if !store.recoveryWarnings.isEmpty {
                    Button { store.presentTransactionRecovery() } label: {
                        Label(tr("recoveryRequired"), systemImage: "exclamationmark.triangle").foregroundStyle(.orange)
                    }
                    .buttonStyle(.plain).disabled(!store.canInspectTransactionRecovery).help(tr("transactionRecovery"))
                    Spacer()
                }
                else if let result = store.resultMessage { Image(systemName: store.operationHadProblems ? "exclamationmark.triangle" : "checkmark.circle").foregroundStyle(store.operationHadProblems ? .orange : .green); Text(result).lineLimit(2); Spacer(); if !store.operationResults.isEmpty { Button(tr("viewResults")) { store.showingResults = true } }; if let path = store.savedOutputs.first { Button(tr("showFinder")) { store.reveal(path) } } }
                else { Image(systemName: "internaldrive").accessibilityHidden(true); Text(tr("localWorkspace")); Spacer(); if store.unsavedCount > 0 { Text(String(format: tr("dirtyCount"), store.unsavedCount)).foregroundStyle(.secondary) } }
                Menu {
                    Button(tr("transactionRecovery")) { store.presentTransactionRecovery() }
                        .disabled(!store.canInspectTransactionRecovery).accessibilityIdentifier("transactionRecovery")
                    Button(tr("exportReviewReference")) { store.presentReviewReferenceExport() }
                        .disabled(!store.canExportReviewReference)
                } label: {
                    Label(tr("diagnosticTools"), systemImage: "ellipsis.circle")
                        .labelStyle(.iconOnly).foregroundStyle(.secondary).frame(minWidth: 22, minHeight: 16)
                }
                .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize()
                .help(tr("diagnosticTools")).accessibilityLabel(tr("diagnosticTools"))
            }.font(.caption).padding(.horizontal, 16).padding(.vertical, 9)
        }
    }
}

struct QueueRow: View {
    @EnvironmentObject var store: WorkspaceStore
    let job: MovieJobSnapshot
    private var artwork: JSONValue? { ArtworkReviewPolicy.primary(job.artwork, kind: "poster") }
    var body: some View {
        HStack(spacing: 10) {
            ZStack {
                RoundedRectangle(cornerRadius: 4).fill(FilmDesk.ink.opacity(0.10))
                if let artwork, let url = store.artworkPaths[store.artworkKey(artwork, jobID: job.jobID)] {
                    CachedArtwork(url: url, maximumPixelSize: 120, posterCrop: artwork["source"].stringValue != "local", fill: true)
                } else { Image(systemName: "film").foregroundStyle(.secondary) }
            }.frame(width: 34, height: 47).clipShape(RoundedRectangle(cornerRadius: 4))
            VStack(alignment: .leading, spacing: 4) {
                Text(store.value("id", job: job).isEmpty ? tr("unknownID") : store.value("id", job: job))
                    .font(.system(.caption, design: .monospaced).weight(.semibold))
                Text(store.displayTitle(for: job))
                    .font(.callout.weight(.medium)).lineLimit(1)
                Text(store.status(job)).font(.caption2).foregroundStyle(.secondary).lineLimit(1)
            }
        }.padding(.vertical, 4).help(job.videoPaths.joined(separator: "\n"))
        .modifier(ArtworkReadLifetime(artwork: artwork, job: job, scope: "queue"))
    }
}

struct SaveInspector: View {
    @EnvironmentObject var store: WorkspaceStore
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 6) {
                Text(tr("saveSettings")).font(.title3.weight(.semibold))
                Text(String(format: tr("actionScope." + store.actionTargetScope), store.actionTargetCount))
                    .font(.caption).foregroundStyle(.secondary)
            }.padding(16)
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    VStack(alignment: .leading, spacing: 12) {
                        sectionLabel("output")
                        Toggle(tr("nfoOutput"), isOn: $store.writeNfo)
                        Toggle(tr("posterOutput"), isOn: $store.writePoster).disabled(!store.supportsArtwork)
                        Toggle(tr("fanartOutput"), isOn: $store.writeFanart).disabled(!store.supportsArtwork)
                        Toggle(tr("writeSamples"), isOn: $store.writeSamples).disabled(!store.supportsArtwork)
                        Toggle(tr("replaceLocalSamples"), isOn: $store.replaceLocalSamples)
                            .disabled(!store.supportsArtwork || !store.writeSamples).help(tr("replaceSamplesNote"))
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
                }.toggleStyle(.checkbox).controlSize(.small).font(.callout).padding(.horizontal, 16).padding(.vertical, 10)
                    .disabled(store.working || store.statusUnconfirmed)
            }
        }.frame(maxHeight: .infinity).background(FilmDesk.surface)
    }
    private func sectionLabel(_ key: String) -> some View { Text(tr(key)).font(.headline) }
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
