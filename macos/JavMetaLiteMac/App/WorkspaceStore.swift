import AppKit
import SwiftUI
import UniformTypeIdentifiers
#if SWIFT_PACKAGE
import WorkerKit
import NativeBridgeKit
#endif

@MainActor final class WorkspaceStore: ObservableObject {
    weak var workspaceWindow: NSWindow?
    @Published private(set) var workspaceSheetPresented = false
    @Published var jobs: [MovieJobSnapshot] = []
    @Published private(set) var selectedID: String? { didSet { if let selectedID, navigationSelection != [selectedID] { navigationSelection = [selectedID] } } }
    @Published var navigationSelection: Set<String> = []
    private var pendingQueueSelection: Set<String>?
    private var pendingQueueToggles: [Set<String>] = []
    private var queueUpdateScheduled = false
    private var queueUpdateGeneration = 0
    var queueNavigationSelection: Set<String> { pendingQueueSelection ?? navigationSelection }
    @Published var checkedJobIDs: Set<String> = [] {
        didSet { if checkedJobIDs.isEmpty && showingBatchOverview { showingBatchOverview = false } }
    }
    @Published private(set) var showingBatchOverview = false
    @Published var capabilities: JSONValue = .null
    @Published var sourceMode = "libredmm" { didSet { persistWorkspacePreferences() } }
    @Published var detailTab = "metadata"
    @Published var queueVisible = true { didSet { persistWorkspacePreferences() } }
    @Published var moreOptionsExpanded = false { didSet { persistWorkspacePreferences() } }
    @Published var showingSourceProfile = false
    @Published var sourceProfile: [String: String] = SourceProfileFields.defaults {
        didSet { if !restoringPreferences { defaults.set(sourceProfile, forKey: "customSourceProfile") } }
    }
    @Published var skipSavePreview = false {
        didSet { persistWorkspacePreferences() }
    }
    @Published var writeNfo = true { didSet { saveConfigurationChanged(writeNfo != oldValue) } }
    @Published var writePoster = false { didSet { saveConfigurationChanged(writePoster != oldValue) } }
    @Published var writeFanart = false { didSet { saveConfigurationChanged(writeFanart != oldValue) } }
    @Published var writeSamples = false { didSet { saveConfigurationChanged(writeSamples != oldValue) } }
    @Published var replaceLocalSamples = false { didSet { saveConfigurationChanged(replaceLocalSamples != oldValue) } }
    @Published var targetMode = "VideoDirectory" { didSet { saveConfigurationChanged(targetMode != oldValue) } }
    @Published var renameVideo = false { didSet { saveConfigurationChanged(renameVideo != oldValue) } }
    @Published var customRootDirectory = "" { didSet { saveConfigurationChanged(customRootDirectory != oldValue) } }
    @Published var operationResults: [JSONValue] = []
    @Published var operationWarnings: [String] = []
    @Published var operationCleanupIssues: [JSONValue] = []
    @Published var showingResults = false
    @Published var operationHadProblems = false
    @Published var showingParts = false
    @Published var partsReviewIDs: [String] = []
    private var pendingPartsPreviewIDs: [String]?
    @Published var drafts: [String: [String: String]] = [:]
    @Published var appearance = "system"
    @Published var language = "system"
    @Published var logRoot: String?
    @Published var dataRoot: String?
    @Published var browser: BrowserSession?
    @Published var ready = false
    @Published private(set) var engineStarting = false
    @Published private(set) var engineStartupFailed = false
    @Published var working = false
    @Published var statusUnconfirmed = false
    @Published private(set) var engineProcessExited = false
    @Published private(set) var engineExitCode: Int?
    @Published var phase = ""
    @Published var currentJobIndex = 0
    @Published var totalOperationJobs = 0
    @Published var operationID: String?
    @Published var errorMessage: String?
    @Published var errorDetails = ""
    @Published var recoveryWarnings: [String] = []
    @Published var showingTransactionRecovery = false
    @Published private(set) var transactionRecoveryRecords: [JSONValue] = []
    @Published private(set) var transactionRecoveryInspection: JSONValue = .null
    @Published private(set) var transactionRecoveryNextOffset: Int?
    @Published private(set) var transactionRecoveryRoot = ""
    @Published private(set) var transactionRecoveryMessage = ""
    @Published var preview: SavePreview?
    @Published var previewStale = false
    @Published var includeIDInTitle = false { didSet { saveConfigurationChanged(includeIDInTitle != oldValue) } }
    @Published var savedOutputs: [String] = []
    @Published private var resultSummary: LocalizedStatusMessage?
    var resultMessage: String? { resultSummary?.resolved(using: tr) }
    @Published var searchText = ""
    @Published var queueCategory: QueueCategory = .all
    @Published var importRecursively = true { didSet { persistWorkspacePreferences() } }
    @Published private(set) var discoveryReview: InputDiscoveryReview?
    @Published private(set) var discoveryErrorMessage: String?
    @Published private(set) var discoveryErrorDetails = ""
    private var discoveryInputPaths: [String] = []
    private var discoveryRecursive = true
    private var discoveringInputs = false
    private var discoveryCancellationRequested = false
    @Published var artworkSourceByJob: [String: String] = [:]
    @Published private var pendingArtworkSelections: [String: (token: UUID, selected: Bool)] = [:]
    private var artworkSelectionTail: Task<Void, Never>?
    private var artworkSelectionFailure: Error?
    var hasPendingArtworkSelections: Bool { !pendingArtworkSelections.isEmpty }
    func waitForArtworkSelections() async { await artworkSelectionTail?.value }
    private func settleArtworkSelections() async throws {
        await waitForArtworkSelections()
        if let failure = artworkSelectionFailure { artworkSelectionFailure = nil; throw failure }
    }
    @Published var viewerSource = ""
    // Fresh installs open the inspector; later launches remember the user's choice.
    @Published var inspectorVisible = true { didSet { persistWorkspacePreferences() } }
    @Published var artworkPaths: [String: URL] = [:]
    private var artworkLeaseIDs: [String: String] = [:]
    // These budgets apply to completed history only. Visible views and decoders
    // keep their leases until their actual read lifetime ends.
    private let artworkHistoryMaximumCount: Int
    private let artworkHistoryMaximumBytes: Int
    private var artworkReaders: [UUID: String] = [:]
    private var artworkURLReaders: [UUID: URL] = [:]
    private var retiredArtworkLeases: [URL: Set<String>] = [:]
    private var artworkAccess: [String: UInt64] = [:]
    private var artworkFileBytes: [String: Int] = [:]
    private var artworkClock: UInt64 = 0
    private var artworkReleaseTail: Task<Void, Never>?
    private let viewerReaderID = UUID()
    @Published var artworkErrors: [String: String] = [:]
    @Published var artworkLowResolution: Set<String> = []
    @Published private(set) var artworkLoadGeneration = 0
    private var pendingTrashToken: String?
    private var trashRecycleInFlight = false
    private var artworkTrashFlowActive = false
    @Published var artworkDimensions: [String: CGSize] = [:]
    @Published var viewerJobID = ""
    @Published var viewerArtwork: [JSONValue] = []
    @Published var viewerIndex = 0
    @Published var showingImage = false
    let worker: WorkerClient
    private let engineExecutable: () throws -> URL
    private let engineArguments: [String]?
    private var operationProgress = OperationProgressCache()
    var operationProgressRetention: (count: Int, bytes: Int) { (operationProgress.count, operationProgress.retainedBytes) }
    private var started = false
    private var startupCompleted = false
    private var confirmedSavePlanID: String?
    private final class ArtworkRequest {
        let id = UUID()
        let jobID: String
        let key: String
        let cacheKey: String
        let scope: String
        let usesLease: Bool
        var operationID: String?
        var task: Task<Void, Never>?
        init(jobID: String, key: String, cacheKey: String, scope: String, usesLease: Bool) {
            self.jobID = jobID; self.key = key; self.cacheKey = cacheKey; self.scope = scope; self.usesLease = usesLease
        }
    }
    private var imageRequests: [UUID: ArtworkRequest] = [:]
    private var artworkRequestOwners: [String: UUID] = [:]
    private var textSessions: [String: NativeTextSession] = [:]
    private var jobLabels: [String: String] = [:]
    private let defaults: UserDefaults
    private var restoringPreferences = true
    static let workspacePreferencesKey = "workspacePreferences.v1"

    init(worker: WorkerClient = WorkerClient(), engineExecutable: @escaping () throws -> URL = { try WorkerClient.bundledExecutable() }, engineArguments: [String]? = nil, defaults: UserDefaults = .standard, artworkHistoryMaximumCount: Int = 24, artworkHistoryMaximumBytes: Int = 64 * 1024 * 1024) {
        self.worker = worker
        self.engineExecutable = engineExecutable
        self.engineArguments = engineArguments
        self.defaults = defaults
        self.artworkHistoryMaximumCount = max(0, artworkHistoryMaximumCount)
        self.artworkHistoryMaximumBytes = max(0, artworkHistoryMaximumBytes)
        appearance = defaults.string(forKey: "interfaceAppearance") ?? "system"
        language = AppLanguage.normalizePreference(defaults.string(forKey: "interfaceLanguage") ?? "system")
        skipSavePreview = defaults.bool(forKey: "skipSavePreview") // Migrate the former standalone preference.
        if let saved = defaults.dictionary(forKey: "customSourceProfile") {
            sourceProfile = SourceProfileFields.normalized(saved.compactMapValues { $0 as? String })
        }
        let saved = defaults.dictionary(forKey: Self.workspacePreferencesKey) ?? [:]
        writeNfo = saved["writeNfo"] as? Bool ?? true
        writePoster = saved["writePoster"] as? Bool ?? false
        writeFanart = saved["writeFanart"] as? Bool ?? false
        writeSamples = saved["writeSamples"] as? Bool ?? false
        replaceLocalSamples = saved["replaceLocalSamples"] as? Bool ?? false
        renameVideo = saved["renameVideo"] as? Bool ?? false
        includeIDInTitle = saved["includeIDInTitle"] as? Bool ?? false
        skipSavePreview = saved["skipSavePreview"] as? Bool ?? skipSavePreview
        if let mode = saved["targetMode"] as? String, ["VideoDirectory", "SourceNumberFolder", "CustomRootNumberFolder"].contains(mode) { targetMode = mode }
        if let root = saved["customRootDirectory"] as? String, root.isEmpty || root.hasPrefix("/") { customRootDirectory = root }
        if let mode = saved["sourceMode"] as? String, ["libredmm", "r18dev", "custom"].contains(mode) { sourceMode = mode }
        queueVisible = saved["queueVisible"] as? Bool ?? true
        inspectorVisible = saved["inspectorVisible"] as? Bool ?? true
        moreOptionsExpanded = saved["moreOptionsExpanded"] as? Bool ?? false
        importRecursively = saved["importRecursively"] as? Bool ?? true
        restoringPreferences = false
        previewStale = false
    }
    private func saveConfigurationChanged(_ changed: Bool) {
        if changed && !restoringPreferences { previewStale = true; persistWorkspacePreferences() }
    }
    private func persistWorkspacePreferences() {
        guard !restoringPreferences else { return }
        // Remember controls, never queue membership, drafts, plans or worker revisions.
        defaults.set([
            "writeNfo": writeNfo, "writePoster": writePoster, "writeFanart": writeFanart,
            "writeSamples": writeSamples, "replaceLocalSamples": replaceLocalSamples,
            "renameVideo": renameVideo, "includeIDInTitle": includeIDInTitle, "skipSavePreview": skipSavePreview,
            "targetMode": targetMode, "customRootDirectory": customRootDirectory, "sourceMode": sourceMode,
            "queueVisible": queueVisible, "inspectorVisible": inspectorVisible,
            "moreOptionsExpanded": moreOptionsExpanded, "importRecursively": importRecursively
        ], forKey: Self.workspacePreferencesKey)
    }

    var navigationJobs: [MovieJobSnapshot] { jobs.filter { navigationSelection.contains($0.jobID) } }
    var checkedJobs: [MovieJobSnapshot] { jobs.filter { checkedJobIDs.contains($0.jobID) } }
    var actionTargetScope: String { "checked" }
    var actionTargetJobs: [MovieJobSnapshot] { checkedJobs }
    var actionTargetCount: Int { actionTargetJobs.count }
    private var workspaceHasModalReview: Bool {
        preview != nil || browser != nil || showingImage || showingParts || showingResults || showingSourceProfile || showingTransactionRecovery || workspaceSheetPresented
    }
    func setWorkspaceSheetPresented(_ presented: Bool) {
        // AppKit completes sheet dismissal after SwiftUI clears its binding.
        // Publish both lifecycle edges so action availability updates again.
        if workspaceSheetPresented != presented { workspaceSheetPresented = presented }
    }
    private var canUseActionTargets: Bool {
        ready && !working && !statusUnconfirmed && discoveryReview == nil && !workspaceHasModalReview &&
        !actionTargetJobs.isEmpty && actionTargetJobs.allSatisfy { !$0.busy }
    }
    var canSearchActionTargets: Bool {
        canUseActionTargets && actionTargetJobs.allSatisfy { !value("id", job: $0).trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }
    var canOpenBrowser: Bool {
        ready && !working && !statusUnconfirmed && discoveryReview == nil && !workspaceHasModalReview &&
        capabilities["manualBrowserImport"].boolValue && queueNavigationSelection.count == 1 &&
        jobs.contains { queueNavigationSelection.contains($0.jobID) && !$0.busy && !value("id", job: $0).trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }
    var canPreviewActionTargets: Bool { canUseActionTargets && (actionTargetCount == 1 || supportsBatch) }
    var recognizedSearchJobs: [MovieJobSnapshot] { jobs.filter { !value("id", job: $0).trimmingCharacters(in: .whitespacesAndNewlines).isEmpty } }
    var canSearchAllRecognized: Bool {
        ready && !working && !statusUnconfirmed && discoveryReview == nil && !workspaceHasModalReview &&
        !recognizedSearchJobs.isEmpty && recognizedSearchJobs.allSatisfy { !$0.busy }
    }
    var canClearQueue: Bool {
        ready && !working && !hasPendingArtworkSelections && !statusUnconfirmed && operationID == nil && discoveryReview == nil && !jobs.isEmpty && jobs.allSatisfy { !$0.busy } &&
        !workspaceHasModalReview && pendingTrashToken == nil && !trashRecycleInFlight && !artworkTrashFlowActive
    }
    var canImportFiles: Bool { ready && !working && !statusUnconfirmed && discoveryReview == nil && !workspaceHasModalReview && capabilities["discoverInputs"].boolValue }
    var canBatch: Bool { ready && !working && !statusUnconfirmed && discoveryReview == nil && !checkedJobs.isEmpty && checkedJobs.allSatisfy { !$0.busy } }
    var supportsArtwork: Bool { capabilities["writeArtwork"].boolValue }
    var supportsOrganization: Bool { capabilities["organizeVideo"].boolValue }
    var canReviewParts: Bool { capabilities["confirmParts"].boolValue && !working && !statusUnconfirmed && checkedJobs.count > 1 }
    var supportsBatch: Bool { capabilities["batchSave"].boolValue }
    var selectedJob: MovieJobSnapshot? { jobs.first { $0.jobID == selectedID } }
    var visibleJobs: [MovieJobSnapshot] {
        let query = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        return categoryJobs(queueCategory).filter { job in
            guard !query.isEmpty else { return true }
            let fields = [job.metadata["id"].stringValue, value("id", job: job), value("title", job: job)]
            return fields.contains { $0.localizedCaseInsensitiveContains(query) } || job.videoPaths.contains {
                URL(fileURLWithPath: $0).lastPathComponent.localizedCaseInsensitiveContains(query)
            }
        }
    }
    func hasLowResolutionCover(_ job: MovieJobSnapshot) -> Bool {
        job.coverQuality["state"].stringValue == "lowResolution" || job.artwork.contains {
            $0["kind"].stringValue == "fanart" && $0["selected"].boolValue && artworkLowResolution.contains(artworkKey($0, jobID: job.jobID))
        }
    }
    func categoryJobs(_ category: QueueCategory) -> [MovieJobSnapshot] {
        jobs.filter { QueueReviewPolicy.matches($0, category: category, hasDraft: !(drafts[$0.jobID] ?? [:]).isEmpty, knownLowResolution: hasLowResolutionCover($0)) }
    }
    func selectCategory(_ category: QueueCategory) { checkedJobIDs = Set(categoryJobs(category).map(\.jobID)) }
    func selectRecognized() { checkedJobIDs = QueueReviewPolicy.recognizedIDs(jobs) }
    var unsavedCount: Int { jobs.filter { $0.hasUnsavedChanges || !(drafts[$0.jobID] ?? [:]).isEmpty }.count }
    var canAct: Bool { ready && !working && !statusUnconfirmed && discoveryReview == nil && selectedJob != nil && selectedJob?.busy != true }
    var canRetrySources: Bool { canAct && !workspaceHasModalReview && selectedID.map(checkedJobIDs.contains) == true && selectedJob?.hasFailedSources == true }
    var canRetryEngineStart: Bool {
        engineStartupFailed && !startupCompleted && !engineStarting && !ready && !working && !statusUnconfirmed && operationID == nil && discoveryReview == nil
    }
    // A dead engine cannot keep writing, but native Trash may still be finishing.
    // Unknown save outcomes remain unknown even when ending the UI session.
    var canEndExitedEngineSession: Bool {
        engineProcessExited && !hasPendingArtworkSelections && !trashRecycleInFlight && !artworkTrashFlowActive
    }

    func start() async {
        guard !started else { return }; started = true
        applyAppearance(appearance)
        if let index = CommandLine.arguments.firstIndex(of: "--appearance"), CommandLine.arguments.indices.contains(index + 1) {
            NSApp.appearance = NSAppearance(named: CommandLine.arguments[index + 1] == "dark" ? .darkAqua : .aqua)
        }
        await initializeEngine(importLaunchFixture: true)
    }
    @discardableResult func retryEngineStart() -> Task<Void, Never>? {
        guard canRetryEngineStart else { return nil }
        // Publish synchronously so repeated clicks cannot enqueue a second start.
        engineStarting = true
        return Task { await initializeEngine(importLaunchFixture: false) }
    }
    private func initializeEngine(importLaunchFixture: Bool) async {
        engineStarting = true; engineStartupFailed = false; ready = false
        engineProcessExited = false; engineExitCode = nil
        defer { engineStarting = false }
        await worker.setNotificationHandler { [weak self] event in Task { @MainActor in self?.receive(event) } }
        do {
            let executable = try engineExecutable()
            var workerArguments: [String] = engineArguments ?? []
            if engineArguments == nil, let index = CommandLine.arguments.firstIndex(of: "--worker-data-root"), CommandLine.arguments.indices.contains(index + 1) {
                workerArguments = ["--data-root", CommandLine.arguments[index + 1]]
            }
            let initialize = try await worker.start(executable: executable, arguments: workerArguments)
            let preferences = try await worker.request("preferences.get")
            let snapshot = try await worker.request("workspace.getSnapshot")
            capabilities = initialize["capabilities"]
            // The Worker preference is only a migration fallback. Never reset
            // restored native controls after engine startup or a startup retry.
            if defaults.dictionary(forKey: Self.workspacePreferencesKey)?["includeIDInTitle"] == nil {
                includeIDInTitle = preferences["includeIdInTitle"].boolValue
                persistWorkspacePreferences()
            }
            logRoot = preferences["logRoot"].stringValue
            dataRoot = preferences["dataRoot"].stringValue
            applySnapshot(snapshot)
            errorMessage = nil; errorDetails = ""
            ready = true; startupCompleted = true
            if importLaunchFixture, let index = CommandLine.arguments.firstIndex(of: "--import-fixture"), CommandLine.arguments.indices.contains(index + 1) {
                // Preserve the existing explicit QA launch option. Ordinary file
                // selection/drop always follows the discovery confirmation flow.
                await importFixturePaths([CommandLine.arguments[index + 1]])
            }
        } catch { display(error); ready = false; engineStartupFailed = !startupCompleted }
    }
    func changeAppearance(_ value: String) {
        appearance = value; defaults.set(value, forKey: "interfaceAppearance"); applyAppearance(value)
    }
    private func applyAppearance(_ value: String) {
        NSApp.appearance = value == "system" ? nil : NSAppearance(named: value == "dark" ? .darkAqua : .aqua)
    }
    func changeLanguage(_ value: String) {
        let normalized = AppLanguage.normalizePreference(value)
        defaults.set(normalized, forKey: "interfaceLanguage"); language = normalized
    }
    /// A reference document, never an executable plan or a claim that a save failed/succeeded.
    var canInspectTransactionRecovery: Bool {
        ready && capabilities["transactionRecovery"].boolValue && !working && !statusUnconfirmed && !hasPendingArtworkSelections
    }
    var canArchiveTransactionRecovery: Bool {
        canInspectTransactionRecovery && transactionRecoveryInspection["canArchive"].boolValue &&
        !transactionRecoveryInspection["verificationToken"].stringValue.isEmpty
    }
    func presentTransactionRecovery() {
        guard canInspectTransactionRecovery, commitEditing() else { return }
        showingTransactionRecovery = true
        reloadTransactionRecovery()
    }
    func reloadTransactionRecovery(offset: Int = 0) {
        guard canInspectTransactionRecovery else { return }
        perform { try await self.loadTransactionRecovery(offset: offset) }
    }
    func loadTransactionRecovery(offset: Int = 0) async throws {
        transactionRecoveryInspection = .null; transactionRecoveryMessage = ""
        errorMessage = nil; errorDetails = ""
        let result = try await worker.request("recovery.list", params: .object(["offset": .number(Double(offset))]))
        transactionRecoveryRecords = result["records"].arrayValue
        transactionRecoveryRoot = result["recoveryRoot"].stringValue
        transactionRecoveryNextOffset = result["nextOffset"] == .null ? nil : result["nextOffset"].intValue
        try await refresh()
    }
    func inspectTransactionRecovery(_ recordID: String) {
        guard canInspectTransactionRecovery else { return }
        perform { try await self.verifyTransactionRecovery(recordID) }
    }
    func verifyTransactionRecovery(_ recordID: String) async throws {
        transactionRecoveryInspection = .null; transactionRecoveryMessage = ""
        errorMessage = nil; errorDetails = ""
        guard transactionRecoveryRecords.contains(where: { $0["recordId"].stringValue == recordID }) else {
            throw RPCFailure(code: -32602, message: tr("recoveryRefreshFirst"))
        }
        let started = try await worker.request("recovery.verify", params: .object(["recordId": .string(recordID)]))
        transactionRecoveryInspection = try await waitOperation(started)
        try await refresh()
    }
    func archiveVerifiedTransactionRecovery() {
        guard canArchiveTransactionRecovery else { return }
        perform { try await self.archiveTransactionRecovery(confirmed: true) }
    }
    func archiveTransactionRecovery(confirmed: Bool) async throws {
        let inspection = transactionRecoveryInspection
        guard confirmed, inspection["canArchive"].boolValue, !inspection["verificationToken"].stringValue.isEmpty else {
            throw RPCFailure(code: -32602, message: tr("recoveryConfirmRequired"))
        }
        // Consume the displayed authorization locally before sending. A lost response
        // can only be reconciled by refreshing the list, never by replaying this call.
        transactionRecoveryInspection = .null
        errorMessage = nil; errorDetails = ""
        let started = try await worker.request("recovery.archive", params: .object([
            "verificationToken": inspection["verificationToken"], "confirmed": .bool(true)
        ]))
        let result = try await waitOperation(started)
        guard result["archived"].boolValue else { throw RPCFailure(code: -32603, message: tr("recoveryArchiveUnconfirmed")) }
        try await loadTransactionRecovery()
        transactionRecoveryMessage = tr("recoveryArchivedReimport")
    }
    func exportTransactionRecoveryReport() {
        guard transactionRecoveryInspection != .null else { return }
        let panel = NSSavePanel(); panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = "JavMetaLite-transaction-inspection.json"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let report: JSONValue = .object([
            "kind": .string("JavMetaLite.transaction-inspection-reference"), "formatVersion": .number(1),
            "instructions": .string(tr("recoveryManualInstructions")),
            // No archive authorization or executable plan is exported.
            "recordPath": transactionRecoveryInspection["recordPath"], "outcome": transactionRecoveryInspection["outcome"],
            "files": transactionRecoveryInspection["files"], "issues": transactionRecoveryInspection["issues"]
        ])
        do {
            let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(report).write(to: url, options: .atomic)
        } catch { display(error) }
    }

    /// A live-session reference is data only. It cannot authorize ending an
    /// unknown or active session, restore old revisions, or replay an operation.
    var canExportReviewReference: Bool {
        ready && !engineProcessExited && !working && !statusUnconfirmed && operationID == nil &&
        !hasPendingArtworkSelections && !trashRecycleInFlight && !artworkTrashFlowActive &&
        pendingTrashToken == nil && discoveryReview == nil && !jobs.isEmpty && jobs.allSatisfy { !$0.busy }
    }
    func reviewReferenceDocument() throws -> JSONValue {
        guard canExportReviewReference else { throw RPCFailure(code: -32010, message: tr("reviewReferenceUnavailable")) }
        return .object([
            "formatVersion": .number(1), "kind": .string("JavMetaLite.review-reference"),
            "createdAt": .string(ISO8601DateFormatter().string(from: Date())),
            "engineProcessExited": .bool(false), "instructions": .string(tr("reviewReferenceInstructions")),
            "jobs": .array(jobs.map { job in .object([
                "jobID": .string(job.jobID), "metadata": job.metadata,
                "videoPaths": .array(job.videoPaths.map(JSONValue.string)),
                "localNfoPath": job.localNfoPath.map(JSONValue.string) ?? .null,
                "candidates": .array(job.candidates), "artwork": .array(job.artwork),
                "hasUnsavedChanges": .bool(job.hasUnsavedChanges)
            ]) }),
            "drafts": .object(drafts.mapValues { .object($0.mapValues(JSONValue.string)) }),
            "checkedJobIDs": .array(checkedJobIDs.sorted().map(JSONValue.string)),
            "selectedJobID": selectedID.map(JSONValue.string) ?? .null
        ])
    }
    func exportReviewReference(to url: URL) throws {
        guard url.isFileURL, url.pathExtension.lowercased() == "json" else {
            throw RPCFailure(code: -32602, message: tr("recoveryExportJSON"))
        }
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        try encoder.encode(reviewReferenceDocument()).write(to: url, options: .atomic)
    }
    @discardableResult func presentReviewReferenceExport() -> Bool {
        guard canExportReviewReference, commitEditing() else { return false }
        let panel = NSSavePanel(); panel.allowedContentTypes = [.json]
        panel.title = tr("exportReviewReference"); panel.message = tr("reviewReferenceInstructions")
        panel.nameFieldStringValue = "JavMetaLite-review-reference.json"
        guard panel.runModal() == .OK, let url = panel.url else { return false }
        do { try exportReviewReference(to: url); return true }
        catch { display(error); return false }
    }

    /// Authoritative disk recovery continues to belong to the Worker's journal.
    func reviewRecoveryDocument() throws -> JSONValue {
        guard canEndExitedEngineSession else {
            throw RPCFailure(code: -32010, message: tr("recoveryExportUnavailable"))
        }
        return .object([
            "formatVersion": .number(1), "kind": .string("JavMetaLite.review-recovery"),
            "createdAt": .string(ISO8601DateFormatter().string(from: Date())),
            "engineProcessExited": .bool(true),
            "engineExitCode": engineExitCode.map { .number(Double($0)) } ?? .null,
            "operationOutcome": .string("unknown"),
            "operationID": operationID.map(JSONValue.string) ?? .null,
            "dataRoot": dataRoot.map(JSONValue.string) ?? .null,
            "instructions": .string("Last received review snapshots and unsent drafts only. Save outcome is unknown. Reopen the app to inspect transaction recovery and reread the original files before applying any edits. Old revisions, operations and plans must never be replayed."),
            "jobs": .array(jobs.map { job in .object([
                "jobID": .string(job.jobID), "reviewRevision": .number(Double(job.reviewRevision)),
                "metadata": job.metadata, "videoPaths": .array(job.videoPaths.map(JSONValue.string)),
                "localNfoPath": job.localNfoPath.map(JSONValue.string) ?? .null,
                "candidates": .array(job.candidates), "artwork": .array(job.artwork),
                "searchState": .string(job.searchState), "saveState": .string(job.saveState),
                "hasUnsavedChanges": .bool(job.hasUnsavedChanges)
            ]) }),
            "drafts": .object(drafts.mapValues { .object($0.mapValues(JSONValue.string)) }),
            "checkedJobIDs": .array(checkedJobIDs.sorted().map(JSONValue.string)),
            "selectedJobID": selectedID.map(JSONValue.string) ?? .null,
            "recoveryWarnings": .array(recoveryWarnings.map(JSONValue.string))
        ])
    }
    func exportReviewRecovery(to url: URL) throws {
        guard url.isFileURL, url.pathExtension.lowercased() == "json" else {
            throw RPCFailure(code: -32602, message: tr("recoveryExportJSON"))
        }
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        let data = try encoder.encode(reviewRecoveryDocument())
        // Atomic export failure/cancellation never clears drafts, checks or unknown state.
        try data.write(to: url, options: .atomic)
    }
    @discardableResult func presentReviewRecoveryExport() -> Bool {
        guard canEndExitedEngineSession, commitEditing() else { return false }
        let panel = NSSavePanel()
        panel.title = tr("exportReviewRecovery")
        panel.message = tr("recoveryExportMessage")
        panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = "JavMetaLite-review-recovery.json"
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return false }
        do { try exportReviewRecovery(to: url); return true }
        catch { display(error); return false }
    }
    /// Shared by Quit and close. No authorization is cached: later edits must be reviewed again.
    func confirmEndExitedEngineSession(closingWindow: Bool = false) -> Bool {
        guard canEndExitedEngineSession, commitEditing() else { return false }
        let alert = NSAlert()
        alert.messageText = tr("engineExited")
        alert.informativeText = tr("engineExitedEndMessage")
        alert.addButton(withTitle: tr("keepOpen"))
        alert.addButton(withTitle: tr(closingWindow ? "exportAndClose" : "exportAndQuit"))
        alert.addButton(withTitle: tr("endWithoutExport"))
        alert.configureSafeCancelDefault()
        switch alert.runSafeModal() {
        case .alertSecondButtonReturn: return presentReviewRecoveryExport() && canEndExitedEngineSession
        case .alertThirdButtonReturn:
            let discard = NSAlert()
            discard.messageText = tr("endWithoutExport")
            discard.informativeText = tr("endWithoutExportMessage")
            discard.addButton(withTitle: tr("keepOpen"))
            discard.addButton(withTitle: tr("confirmEndSession"))
            discard.configureSafeCancelDefault()
            return discard.runSafeModal() == .alertSecondButtonReturn && canEndExitedEngineSession
        default: return false
        }
    }
    func openLogs() { if let logRoot, !logRoot.isEmpty { NSWorkspace.shared.open(URL(fileURLWithPath: logRoot, isDirectory: true)) } }
    func receive(_ event: JSONValue) {
        let method = event["method"].stringValue, params = event["params"]
        if method == "worker.exited" || method == "worker.unavailable" {
            if method == "worker.exited" {
                engineProcessExited = true
                engineExitCode = params["exitCode"].intValue
            }
            operationProgress.removeAll()
            statusUnconfirmed = statusUnconfirmed || startupCompleted || working || operationID != nil
            ready = false; working = false; preview = nil
            confirmedSavePlanID = nil
            errorMessage = tr(engineProcessExited ? "engineExited" : "workerUnavailable"); return
        }
        guard let progress = operationProgress.receive(method: method, params: params, protecting: operationID) else { return }
        if progress["operationId"].stringValue == operationID { applyOperationProgress(progress) }
    }
    private func applyOperationProgress(_ event: JSONValue) {
        phase = event["phase"].stringValue.lowercased()
        if event["details"]["currentIndex"].intValue > 0 { currentJobIndex = event["details"]["currentIndex"].intValue }
        if event["details"]["totalJobs"].intValue > 0 { totalOperationJobs = event["details"]["totalJobs"].intValue }
    }
    func refresh() async throws {
        let snapshot = try await worker.request("workspace.getSnapshot")
        applySnapshot(snapshot)
    }
    private func applySnapshot(_ snapshot: JSONValue, preserveEmptySelection: Bool = false) {
        let refreshed = snapshot["jobs"].arrayValue.map(MovieJobSnapshot.init)
        let previousByID = Dictionary(jobs.map { ($0.jobID, $0) }, uniquingKeysWith: { _, last in last })
        let available = Set(refreshed.map(\.jobID))
        for previous in jobs where !available.contains(previous.jobID) {
            invalidateArtworkCache(jobID: previous.jobID, previous: previous)
        }
        for job in refreshed { reconcileReviewState(job, previous: previousByID[job.jobID]) }
        // Even an unchanged job list must retire orphaned drafts and undo stacks.
        retireReviewState(keeping: available)
        if jobs != refreshed { jobs = refreshed }
        let warnings = snapshot["recoveryWarnings"].arrayValue.map(\.stringValue)
        if recoveryWarnings != warnings { recoveryWarnings = warnings }
        if !snapshot["activeTrash"]["trashToken"].stringValue.isEmpty {
            pendingTrashToken = snapshot["activeTrash"]["trashToken"].stringValue
            if !working && !trashRecycleInFlight { statusUnconfirmed = true }
        } else if !trashRecycleInFlight {
            let releasedLease = pendingTrashToken != nil
            pendingTrashToken = nil
            if releasedLease { resumeArtworkAfterTrash() }
        }
        let navigation = navigationSelection.intersection(available)
        let checked = checkedJobIDs.intersection(available)
        if navigationSelection != navigation { navigationSelection = navigation }
        if checkedJobIDs != checked { checkedJobIDs = checked }
        let selection: String?
        if navigation.isEmpty { selection = preserveEmptySelection ? nil : refreshed.first?.jobID }
        else if navigation.count == 1 { selection = navigation.first }
        else { selection = nil }
        if selectedID != selection { selectedID = selection }
        else if let selection, navigationSelection != [selection] { navigationSelection = [selection] }
    }
    private func retireReviewState(keeping available: Set<String>) {
        // A confirmed-parts save can retire jobs without calling remove(). Only
        // an authoritative snapshot may discard their drafts and undo stacks.
        for key in textSessions.keys where !available.contains(textSessions[key]!.jobID) {
            let session = textSessions.removeValue(forKey: key)
            session?.onChange = nil
            session?.textView.undoManager?.removeAllActions()
        }
        let retainedDrafts = drafts.filter { available.contains($0.key) }
        if drafts != retainedDrafts { drafts = retainedDrafts }
        jobLabels = jobLabels.filter { available.contains($0.key) }
        let retainedSources = artworkSourceByJob.filter { available.contains($0.key) }
        if artworkSourceByJob != retainedSources { artworkSourceByJob = retainedSources }
    }
    func selectNavigation(_ ids: Set<String>) {
        invalidatePendingQueueUpdates()
        applyNavigation(ids)
    }
    private func singleNavigation(_ ids: Set<String>) -> Set<String> {
        jobs.first(where: { ids.contains($0.jobID) }).map { Set([$0.jobID]) } ?? []
    }
    private func applyNavigation(_ requested: Set<String>, editingCommitted: Bool = false) {
        let ids = singleNavigation(requested)
        let nextSelectedID = ids.count == 1 ? ids.first : nil
        guard ids != navigationSelection || nextSelectedID != selectedID || !ids.isEmpty && showingBatchOverview else { return }
        guard editingCommitted || commitEditing() else { return }
        if !ids.isEmpty && showingBatchOverview { showingBatchOverview = false }
        browser?.invalidateCapture(); cancelArtworkRequests(except: ids)
        if navigationSelection != ids { navigationSelection = ids }
        if selectedID != nextSelectedID { selectedID = nextSelectedID }
    }
    var allMoviesChecked: Bool { !jobs.isEmpty && Set(jobs.map(\.jobID)).isSubset(of: checkedJobIDs) }
    func toggleAllChecks() { setAllChecks(!allMoviesChecked) }
    func setAllChecks(_ checked: Bool) {
        guard ready, !working, !statusUnconfirmed else { return }
        checkedJobIDs = checked ? Set(jobs.map(\.jobID)) : []
    }
    func showBatchOverview() {
        guard flushPendingQueueUpdates(), !checkedJobs.isEmpty, !working, !workspaceHasModalReview, commitEditing() else { return }
        showingBatchOverview = true
    }
    func showCurrentMovie() {
        guard commitEditing() else { return }
        showingBatchOverview = false
    }
    func selectQueueNavigation(_ requested: Set<String>) {
        let ids = singleNavigation(requested)
        // Native List can report the current selection during a view update.
        // A no-op must not publish changes or clear an implicit current review.
        guard ids != queueNavigationSelection else { return }
        // SwiftUI may clear a List's selection when its row is filtered out.
        // Filtering must retain the current review and all batch checkmarks.
        if ids.isEmpty, !queueNavigationSelection.isEmpty, queueNavigationSelection.isDisjoint(with: Set(visibleJobs.map(\.jobID))) { return }
        pendingQueueSelection = ids
        scheduleQueueUpdate()
    }
    func toggleQueueNavigationChecks() {
        guard ready, !working, !statusUnconfirmed else { return }
        let ids = queueNavigationSelection
        guard !ids.isEmpty else { return }
        // Capture the target, but evaluate each toggle in order when it commits.
        pendingQueueToggles.append(ids)
        scheduleQueueUpdate()
    }
    private func scheduleQueueUpdate() {
        guard !queueUpdateScheduled else { return }
        queueUpdateScheduled = true
        let generation = queueUpdateGeneration
        DispatchQueue.main.async { [weak self] in
            guard let self, self.queueUpdateGeneration == generation else { return }
            self.flushPendingQueueUpdates()
        }
    }
    private func invalidatePendingQueueUpdates() {
        queueUpdateGeneration += 1
        queueUpdateScheduled = false
        pendingQueueSelection = nil
        pendingQueueToggles.removeAll()
    }
    // Commands flush before capturing job IDs; native List callbacks only enqueue.
    // The commit dependency also lets the rejection policy be tested without a
    // fake system key window or a replacement NSApplication implementation.
    @discardableResult func flushPendingQueueUpdates(commit: (() -> Bool)? = nil) -> Bool {
        guard pendingQueueSelection != nil || !pendingQueueToggles.isEmpty else { return true }
        let available = Set(jobs.map(\.jobID))
        let ids = pendingQueueSelection.map { $0.intersection(available) }
        let toggles = pendingQueueToggles.map { $0.intersection(available) }
        invalidatePendingQueueUpdates()
        let changesSelection = ids.map { $0 != navigationSelection || ($0.count == 1 ? $0.first : nil) != selectedID } ?? false
        guard !changesSelection && toggles.isEmpty || (commit?() ?? commitEditing()) else {
            objectWillChange.send() // Restore the native List to the accepted selection.
            return false
        }
        if let ids { applyNavigation(ids, editingCommitted: true) }
        var checked = checkedJobIDs
        for target in toggles where !target.isEmpty {
            if target.isSubset(of: checked) { checked.subtract(target) }
            else { checked.formUnion(target) }
        }
        if checked != checkedJobIDs { checkedJobIDs = checked }
        return true
    }
    func toggleChecked(_ jobID: String) {
        setChecked(jobID, checked: !checkedJobIDs.contains(jobID))
    }
    func setChecked(_ jobID: String, checked: Bool) {
        guard ready, !working, !statusUnconfirmed, jobs.contains(where: { $0.jobID == jobID }) else { return }
        if checked { checkedJobIDs.insert(jobID) }
        else { checkedJobIDs.remove(jobID) }
    }
    func chooseOrganizationRoot() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false; panel.canChooseDirectories = true; panel.allowsMultipleSelection = false
        panel.canCreateDirectories = false; panel.title = tr("chooseOrganizationRoot")
        panel.begin { [weak self] response in
            guard response == .OK, let path = panel.url?.path else { return }
            Task { @MainActor in self?.customRootDirectory = path; self?.targetMode = "CustomRootNumberFolder" }
        }
    }
    func reviewParts() {
        guard flushPendingQueueUpdates() else { return }
        guard canReviewParts else { return }
        partsReviewIDs = checkedJobs.map(\.jobID)
        pendingPartsPreviewIDs = nil
        showingParts = true
    }
    func movePart(_ jobID: String, by offset: Int) {
        guard let index = partsReviewIDs.firstIndex(of: jobID), partsReviewIDs.indices.contains(index + offset) else { return }
        partsReviewIDs.swapAt(index, index + offset)
    }
    func confirmPartsPreview() {
        guard partsReviewIDs.count > 1 else { return }
        pendingPartsPreviewIDs = partsReviewIDs
        showingParts = false
    }
    func partsReviewDidDismiss() {
        guard let ids = pendingPartsPreviewIDs else { return }
        pendingPartsPreviewIDs = nil
        makePreview(orderedJobIDs: ids, confirmedParts: true)
    }
    func jobLabel(_ jobID: String) -> String { jobLabels[jobID] ?? tr("unknownID") }
    func update(_ snapshot: JSONValue) {
        let job = MovieJobSnapshot(snapshot)
        let index = jobs.firstIndex(where: { $0.jobID == job.jobID })
        let previous = index.map { jobs[$0] }
        reconcileReviewState(job, previous: previous)
        if let index {
            if previous != job { jobs[index] = job }
        } else { jobs.append(job) }
    }
    private func reconcileReviewState(_ job: MovieJobSnapshot, previous: MovieJobSnapshot?) {
        if let previous, previous.reviewRevision != job.reviewRevision || previous.artwork != job.artwork {
            let retained = Set(job.artwork.filter { art in
                let content = art["contentKey"].stringValue
                return !content.isEmpty && previous.artwork.contains { $0["artworkId"] == art["artworkId"] && $0["contentKey"].stringValue == content }
            }.map { artworkKey($0, jobID: job.jobID) })
            invalidateArtworkCache(jobID: job.jobID, retaining: retained, previous: previous)
        }
        reconcileArtworkSource(job)
        jobLabels[job.jobID] = [job.metadata["id"].stringValue, job.metadata["title"].stringValue].filter { !$0.isEmpty }.joined(separator: " · ")
    }
    private func invalidateArtworkCache(jobID: String, retaining keys: Set<String> = [], previous indexedPrevious: MovieJobSnapshot? = nil) {
        let previous = indexedPrevious ?? jobs.first(where: { $0.jobID == jobID })
        let retainedRequests = Set((previous?.artwork ?? []).filter { keys.contains(artworkKey($0, jobID: jobID)) }
            .map { artworkRequestKey($0, job: previous!) })
        for request in imageRequests.values where request.jobID == jobID && !retainedRequests.contains(request.key) { cancelArtworkRequest(request) }
        let prefix = jobID + "/"
        releaseArtworkLeases(for: Set(artworkLeaseIDs.keys.filter { $0.hasPrefix(prefix) && !keys.contains($0) }))
        artworkPaths = artworkPaths.filter { !$0.key.hasPrefix(prefix) || keys.contains($0.key) }
        artworkErrors = artworkErrors.filter { !$0.key.hasPrefix(prefix) || keys.contains($0.key) }
        artworkDimensions = artworkDimensions.filter { !$0.key.hasPrefix(prefix) || keys.contains($0.key) }
        artworkLowResolution = Set(artworkLowResolution.filter { !$0.hasPrefix(prefix) || keys.contains($0) })
        artworkAccess = artworkAccess.filter { artworkPaths[$0.key] != nil }
        artworkFileBytes = artworkFileBytes.filter { artworkPaths[$0.key] != nil }
    }
    private func reconcileArtworkSource(_ job: MovieJobSnapshot) {
        if let selected = artworkSourceByJob[job.jobID], !ArtworkReviewPolicy.sources(job.artwork).contains(selected) { artworkSourceByJob.removeValue(forKey: job.jobID) }
    }
    func value(_ field: String, job: MovieJobSnapshot) -> String { drafts[job.jobID]?[field] ?? job.metadata[field].stringValue }
    func edit(_ field: String, value: String, job: MovieJobSnapshot) {
        let baseline = jobs.first(where: { $0.jobID == job.jobID })?.metadata[field].stringValue ?? job.metadata[field].stringValue
        if value == baseline { drafts[job.jobID]?.removeValue(forKey: field) }
        else { drafts[job.jobID, default: [:]][field] = value }
        if preview?.jobIDs.contains(job.jobID) == true { previewStale = true }
    }
    func binding(_ field: String, job: MovieJobSnapshot) -> Binding<String> {
        Binding(get: { self.value(field, job: job) }, set: { self.edit(field, value: $0, job: job) })
    }
    func editorSession(field: String, jobID: String) -> NativeTextSession {
        let key = jobID + "/" + field
        if let session = textSessions[key] { return session }
        let session = NativeTextSession(jobID: jobID, field: field)
        textSessions[key] = session
        return session
    }
    @discardableResult func commitEditing() -> Bool {
        guard let window = NSApp.keyWindow else { return true }
        if let editor = window.firstResponder as? NSTextView {
            if editor.hasMarkedText() { editor.unmarkText() }
            guard !editor.hasMarkedText(), window.makeFirstResponder(nil) else {
                errorMessage = tr("finishComposition"); return false
            }
        }
        return true
    }
    func flush(_ jobID: String) async throws {
        guard commitEditing() else { throw RPCFailure(code: -32602, message: tr("finishComposition")) }
        guard let job = jobs.first(where: { $0.jobID == jobID }), let edits = drafts[jobID], !edits.isEmpty else { return }
        let result = try await worker.request("jobs.applyEdits", params: .object([
            "jobId": .string(jobID), "expectedReviewRevision": .number(Double(job.reviewRevision)),
            "edits": .object(edits.mapValues(JSONValue.string))
        ]))
        // Clear only fields whose values still match the sent buffer; newer typing stays local.
        for (field, value) in edits where drafts[jobID]?[field] == value { drafts[jobID]?.removeValue(forKey: field) }
        update(result)
    }
    func perform(_ work: @escaping () async throws -> Void) {
        guard flushPendingQueueUpdates() else { return }
        guard !working, !statusUnconfirmed, discoveryReview == nil, commitEditing() else { return }
        working = true; currentJobIndex = 0; totalOperationJobs = 0; resultSummary = nil; savedOutputs = []; operationResults = []; operationWarnings = []; operationCleanupIssues = []; operationHadProblems = false
        Task {
            defer { working = false; if !statusUnconfirmed { operationID = nil; phase = "" } }
            do { try await settleArtworkSelections(); try await work() } catch {
                if pendingTrashToken != nil && !trashRecycleInFlight { statusUnconfirmed = true }
                if let rpc = error as? RPCFailure, rpc.reason == "CANCELED" {
                    preview = nil; previewStale = true
                    errorMessage = nil; errorDetails = ""
                    resultSummary = LocalizedStatusMessage("operationCanceled")
                } else { display(error) }
                try? await refresh()
            }
        }
    }
    func chooseFiles() {
        guard canImportFiles, commitEditing() else { return }
        let panel = NSOpenPanel()
        panel.title = tr("addMovies"); panel.canChooseFiles = true; panel.canChooseDirectories = true
        panel.allowsMultipleSelection = true; panel.prompt = tr("add")
        let recursive = NSButton(checkboxWithTitle: tr("scanSubfolders"), target: nil, action: nil)
        recursive.state = importRecursively ? .on : .off
        NativeImportPanelConfiguration.configure(panel, recursiveControl: recursive)
        panel.begin { [weak self] response in
            guard response == .OK else { return }
            Task { @MainActor in
                self?.importRecursively = recursive.state == .on
                await self?.importPaths(panel.urls.map(\.path))
            }
        }
    }
    func importPaths(_ paths: [String]) async {
        guard canImportFiles, !paths.isEmpty, commitEditing() else { return }
        discoveryInputPaths = paths; discoveryRecursive = importRecursively
        working = true; phase = "discovering"; discoveringInputs = true; discoveryCancellationRequested = false
        defer { working = false; discoveringInputs = false; if !statusUnconfirmed { operationID = nil; phase = "" } }
        do {
            try await settleArtworkSelections()
            let response = try await worker.request("workspace.discoverInputs", params: .object(["paths": .array(paths.map(JSONValue.string)), "recursive": .bool(discoveryRecursive)]))
            let discovered = try await waitOperation(response)
            guard !discovered["discoveryId"].stringValue.isEmpty else { throw RPCFailure(code: -32603, message: "The discovery result has no token.") }
            if discoveryCancellationRequested {
                _ = try await worker.request("workspace.discardDiscovery", params: .object(["discoveryId": discovered["discoveryId"]]))
                return
            }
            installDiscovery(discovered)
        } catch { display(error) }
    }
    func installDiscovery(_ result: JSONValue) {
        discoveryReview = InputDiscoveryReview(InputDiscovery(result))
        discoveryErrorMessage = nil; discoveryErrorDetails = ""
    }
    func selectAllDiscovered() { discoveryReview?.selectAll() }
    func deselectAllDiscovered() { discoveryReview?.deselectAll() }
    func selectRecognizedDiscovered() { discoveryReview?.selectRecognized() }
    func selectDiscovered(_ itemID: String, selected: Bool) { discoveryReview?.setSelected(itemID, selected) }
    var discoveryDetails: String { discoveryReview.map { scanDiagnosticDetails($0.discovery.raw) } ?? "" }
    @discardableResult func confirmDiscovery() -> Task<Void, Never>? {
        guard ready, !working, !statusUnconfirmed, let token = discoveryReview?.discovery.discoveryID,
              let ids = discoveryReview?.beginConfirmation() else { return nil }
        // Freeze membership and retain the sheet before starting asynchronous work.
        // Dismissing it cannot enqueue a later, hidden import or a second submission.
        working = true; phase = "loadinglocal"
        discoveryErrorMessage = nil; discoveryErrorDetails = ""
        return Task {
            defer { working = false; if !statusUnconfirmed { operationID = nil; phase = "" } }
            do {
                let response = try await worker.request("workspace.importDiscovery", params: .object(["discoveryId": .string(token), "selectedItemIds": .array(ids.map(JSONValue.string))]))
                let result = try await waitOperation(response)
                try await refresh()
                discoveryReview = nil
                applyImportOutcome(result)
            } catch { displayDiscoveryError(error) }
        }
    }
    @discardableResult func discardDiscovery() -> Task<Void, Never>? {
        guard !working, !statusUnconfirmed, let token = discoveryReview?.discovery.discoveryID else { return nil }
        working = true; phase = "discardingdiscovery"
        return Task {
            defer { working = false; if !statusUnconfirmed { operationID = nil; phase = "" } }
            do {
                do { _ = try await worker.request("workspace.discardDiscovery", params: .object(["discoveryId": .string(token)])) }
                catch let failure as RPCFailure where failure.reason == "DISCOVERY_STALE" { /* Already expired/consumed: nothing remains to discard. */ }
                discoveryReview = nil; discoveryErrorMessage = nil; discoveryErrorDetails = ""
            } catch { displayDiscoveryError(error) }
        }
    }
    func rescanDiscovery() {
        guard let discard = discardDiscovery() else { return }
        let paths = discoveryInputPaths, recursive = discoveryRecursive
        Task {
            await discard.value
            guard discoveryReview == nil, !statusUnconfirmed else { return }
            importRecursively = recursive
            await importPaths(paths)
        }
    }
    private func displayDiscoveryError(_ error: Error) {
        display(error)
        discoveryErrorMessage = errorMessage; discoveryErrorDetails = errorDetails
        errorMessage = nil; errorDetails = ""
    }
    private func importFixturePaths(_ paths: [String]) async {
        guard ready, !working, !statusUnconfirmed, commitEditing() else { return }
        working = true
        defer { working = false; if !statusUnconfirmed { operationID = nil; phase = "" } }
        do {
            let result = try await worker.request("workspace.importInputs", params: .object(["paths": .array(paths.map(JSONValue.string)), "recursive": .bool(importRecursively)]))
            let imported = try await waitOperation(result)
            try await refresh()
            applyImportOutcome(imported)

        } catch { display(error); try? await refresh() }
    }
    private func scanDiagnosticDetails(_ imported: JSONValue) -> String {
        let diagnostics = imported["diagnostics"].arrayValue
        let warnings = imported["warnings"].arrayValue.map(\.stringValue)
        let representedWarnings = Set(diagnostics.map { $0["path"].stringValue + ": " + $0["message"].stringValue })
        let details = diagnostics.map { diagnostic in
            let kind = diagnostic["kind"].stringValue
            let key = "scanDiagnostic." + kind
            let label = tr(key) == key ? tr("operationFailed") : tr(key)
            return [label + " [" + kind + "]", diagnostic["path"].stringValue, diagnostic["message"].stringValue].joined(separator: "\n")
        } + warnings.filter { !representedWarnings.contains($0) }
        return details.joined(separator: "\n\n")
    }
    func applyImportOutcome(_ imported: JSONValue) {
        let details = scanDiagnosticDetails(imported)
        savedOutputs = []; operationResults = []; operationWarnings = []; operationCleanupIssues = []
        operationHadProblems = !details.isEmpty
        errorMessage = details.isEmpty ? nil : tr("importPartial")
        errorDetails = details
        resultSummary = LocalizedStatusMessage("importResult", counts: [imported["importedJobIds"].arrayValue.count, imported["duplicateJobIds"].arrayValue.count])
    }
    func dismissErrorDetails() { errorDetails = "" }
    func openBrowser() {
        guard flushPendingQueueUpdates() else { return }
        guard canOpenBrowser, let jobID = selectedID else { return }
        perform {
            try await self.flush(jobID)
            let result = try await self.worker.request("browser.getTargets", params: .object(["jobId": .string(jobID)]))
            self.browser = BrowserSession(jobID: jobID, response: result, store: self)
        }
    }
    @discardableResult private func prepareActionCommand(commit: (() -> Bool)? = nil) -> Bool {
        guard commit?() ?? commitEditing() else {
            _ = flushPendingQueueUpdates(commit: { false })
            return false
        }
        return flushPendingQueueUpdates(commit: { true })
    }
    func searchActionTargets(commit: (() -> Bool)? = nil) {
        guard prepareActionCommand(commit: commit), canSearchActionTargets else { return }
        search(orderedJobIDs: actionTargetJobs.map(\.jobID))
    }
    func previewActionTargets(commit: (() -> Bool)? = nil) {
        guard prepareActionCommand(commit: commit), canPreviewActionTargets else { return }
        makePreview(orderedJobIDs: actionTargetJobs.map(\.jobID))
    }
    func saveActionTargets(commit: (() -> Bool)? = nil) {
        guard prepareActionCommand(commit: commit), canPreviewActionTargets else { return }
        makePreview(orderedJobIDs: actionTargetJobs.map(\.jobID), automaticallySave: skipSavePreview)
    }
    func search(retry: Bool = false, batch: Bool = false, sourceOverride: String? = nil, orderedJobIDs: [String]? = nil) {
        guard flushPendingQueueUpdates() else { return }
        guard !retry || canRetrySources else { return }
        let ids = orderedJobIDs ?? (retry ? selectedID.map { [$0] } ?? [] : checkedJobs.map(\.jobID))
        guard !ids.isEmpty, Set(ids).isSubset(of: checkedJobIDs) else { return }
        let isBatch = batch || ids.count > 1
        let failedSources = Set((selectedJob?.searchAttempts ?? []).filter { !$0["success"].boolValue }
            .map { $0["source"].stringValue }.filter { ["libredmm", "r18dev"].contains($0) })
        // Menu retry follows the recorded failures, not a subsequently changed
        // new-query picker. Per-source buttons still keep their explicit scope.
        let source = sourceOverride ?? (retry && !failedSources.isEmpty
            ? (failedSources.count == 1 ? failedSources.first! : "custom") : sourceMode)
        perform {
            for id in ids { try await self.flush(id) }
            var request: [String: JSONValue] = ["jobIds": .array(ids.map(JSONValue.string)), "sourceMode": .string(source)]
            if source == "custom" { request["sourceProfile"] = .object(SourceProfileFields.normalized(self.sourceProfile).mapValues(JSONValue.string)) }
            let result = try await self.worker.request(retry ? "jobs.retryFailedSources" : "jobs.search", params: .object(request))
            let completed = try await self.waitOperation(result)
            self.operationWarnings = completed["warnings"].arrayValue.map(\.stringValue)
            self.operationResults = completed["jobResults"].arrayValue
            self.operationHadProblems = self.operationResults.contains { $0["status"].stringValue != "Completed" } || !self.operationWarnings.isEmpty || ["partialFailure", "canceled"].contains(completed["outcome"].stringValue)
            if isBatch { self.resultSummary = LocalizedStatusMessage(self.operationHadProblems ? "queryPartial" : "queryFinished") }
            if !self.operationWarnings.isEmpty { self.errorMessage = tr("queryPartial"); self.errorDetails = self.operationWarnings.joined(separator: "\n") }
            if isBatch && !self.operationResults.isEmpty { self.showingResults = true }
            try await self.refresh()
            if self.operationResults.contains(where: { $0["status"].stringValue == "Completed" }) {
                self.detailTab = "metadata"
                for row in self.operationResults where row["status"].stringValue == "Completed" {
                    self.artworkSourceByJob.removeValue(forKey: row["jobId"].stringValue)
                }
            }
        }
    }
    func reviewSource(_ job: MovieJobSnapshot) -> String { (drafts[job.jobID] ?? [:]).isEmpty ? job.reviewSource : "" }
    func selectReviewSource(_ source: String, jobID: String) {
        guard canAct, !workspaceHasModalReview, commitEditing(), let job = jobs.first(where: { $0.jobID == jobID }), job.reviewSources.contains(source) else { return }
        perform {
            try await self.flush(jobID)
            guard let current = self.jobs.first(where: { $0.jobID == jobID }) else { return }
            let result = try await self.worker.request("jobs.selectSource", params: .object([
                "jobId": .string(jobID), "expectedReviewRevision": .number(Double(current.reviewRevision)), "source": .string(source)
            ]))
            self.update(result); self.previewStale = true
            self.artworkSourceByJob.removeValue(forKey: jobID)
        }
    }

    private func enqueueArtworkReview(jobID: String, key: String, selected: Bool,
                                      mutation: @escaping (MovieJobSnapshot) async throws -> JSONValue) {
        let token = UUID(), previous = artworkSelectionTail
        if !hasPendingArtworkSelections { artworkSelectionFailure = nil }
        pendingArtworkSelections[key] = (token, selected); previewStale = true
        // Acknowledge in order, without disabling or changing the whole window.
        artworkSelectionTail = Task {
            await previous?.value
            defer { if pendingArtworkSelections[key]?.token == token { pendingArtworkSelections.removeValue(forKey: key) } }
            do {
                try await flush(jobID)
                guard let job = jobs.first(where: { $0.jobID == jobID }), ready, !statusUnconfirmed else { return }
                update(try await mutation(job))
            } catch { artworkSelectionFailure = error; display(error); try? await refresh() }
        }
    }
    func selectArtwork(_ artwork: JSONValue, jobID: String, selected: Bool) {
        guard flushPendingQueueUpdates(), ready, !working, !statusUnconfirmed,
              discoveryReview == nil, !workspaceHasModalReview, commitEditing(),
              jobs.contains(where: { $0.jobID == jobID && !$0.busy }) else { return }
        enqueueArtworkReview(jobID: jobID, key: artworkKey(artwork, jobID: jobID), selected: selected) { job in
            try await self.worker.request("artwork.select", params: .object([
                "jobId": .string(jobID), "expectedReviewRevision": .number(Double(job.reviewRevision)),
                "artworkId": artwork["artworkId"], "selected": .bool(selected)
            ]))
        }
    }
    func artworkSelected(_ artwork: JSONValue, jobID: String) -> Bool {
        pendingArtworkSelections[artworkKey(artwork, jobID: jobID)]?.selected ?? artwork["selected"].boolValue
    }
    func artworkSource(_ job: MovieJobSnapshot) -> String { artworkSourceByJob[job.jobID] ?? "" }
    func visibleArtwork(_ job: MovieJobSnapshot) -> [JSONValue] { ArtworkReviewPolicy.filtered(job.artwork, source: artworkSource(job)) }
    func selectArtworkSource(_ source: String, jobID: String, fromViewer: Bool = false) {
        guard flushPendingQueueUpdates(), ready, !working, !statusUnconfirmed, discoveryReview == nil,
              (!workspaceHasModalReview || fromViewer && showingImage), commitEditing(),
              let job = jobs.first(where: { $0.jobID == jobID && !$0.busy }),
              source.isEmpty || ArtworkReviewPolicy.sources(job.artwork).contains(source) else { return }
        // Display filtering is immediate; its later acknowledgement must not
        // replace a newer filter chosen while the RPC was in flight.
        artworkSourceByJob[jobID] = source
        if fromViewer {
            // A source switch abandons the viewer's in-flight image, while the
            // main page keeps its own requests and completed content cache.
            if viewerSource != source { closeArtworkViewer() }
            updateViewer(job, source: source)
        }
        guard !source.isEmpty else { return }
        enqueueArtworkReview(jobID: jobID, key: jobID + "/cover-source", selected: true) { current in
            let result = try await self.worker.request("artwork.selectSource", params: .object([
                "jobId": .string(jobID), "expectedReviewRevision": .number(Double(current.reviewRevision)), "source": .string(source)
            ]))
            if fromViewer && self.showingImage && self.viewerJobID == jobID && self.viewerSource == source {
                self.updateViewer(MovieJobSnapshot(result), source: source)
            }
            return result
        }
    }
    private func updateViewer(_ job: MovieJobSnapshot, source: String) {
        let currentID = viewerArtwork.indices.contains(viewerIndex) ? viewerArtwork[viewerIndex]["artworkId"].stringValue : ""
        viewerSource = source
        viewerArtwork = ArtworkReviewPolicy.filtered(job.artwork, source: source)
        viewerIndex = ArtworkReviewPolicy.entryIndex(viewerArtwork, artworkID: currentID)
        if viewerArtwork.indices.contains(viewerIndex) {
            beginArtworkRead(viewerArtwork[viewerIndex], jobID: job.jobID, readerID: viewerReaderID, scope: "viewer")
        } else { endArtworkRead(viewerReaderID) }
    }
    func closeArtworkViewer() {
        endArtworkRead(viewerReaderID)
        for request in imageRequests.values where request.scope == "viewer" && !artworkReaders.values.contains(request.cacheKey) { cancelArtworkRequest(request) }
    }
    func selectCandidate(_ candidate: JSONValue, jobID: String) {
        perform {
            try await self.flush(jobID)
            guard let job = self.jobs.first(where: { $0.jobID == jobID }) else { return }
            let result = try await self.worker.request("jobs.selectCandidate", params: .object([
                "jobId": .string(jobID), "expectedReviewRevision": .number(Double(job.reviewRevision)),
                "field": candidate["field"], "source": candidate["source"]
            ]))
            self.update(result); self.previewStale = true
        }
    }
    var saveOptionsPayload: JSONValue {
        .object([
            "writeNfo": .bool(writeNfo), "downloadPoster": .bool(writePoster), "downloadFanart": .bool(writeFanart),
            "downloadExtrafanart": .bool(writeSamples), "replaceLocalExtrafanart": .bool(replaceLocalSamples),
            // Consent is Confirm Save on the immutable preview, or the explicit
            // skip-preview preference. Worker planning still checks all conflicts.
            "overwriteExisting": .bool(true), "includeIdInTitle": .bool(includeIDInTitle),
            "targetMode": .string(targetMode), "renameVideo": .bool(renameVideo), "customRootDirectory": .string(customRootDirectory)
        ])
    }
    func makePreview(batch: Bool = false, orderedJobIDs: [String]? = nil, confirmedParts: Bool = false, automaticallySave: Bool = false) {
        guard flushPendingQueueUpdates() else { return }
        let ids = orderedJobIDs ?? checkedJobs.map(\.jobID)
        guard !ids.isEmpty, Set(ids).isSubset(of: checkedJobIDs) else { return }
        perform {
            for id in ids { try await self.flush(id) }
            let revisions = Dictionary(uniqueKeysWithValues: self.jobs.filter { ids.contains($0.jobID) }.map { ($0.jobID, JSONValue.number(Double($0.reviewRevision))) })
            let options = self.saveOptionsPayload
            let response = try await self.worker.request("save.preview", params: .object([
                "jobIds": .array(ids.map(JSONValue.string)), "expectedReviewRevisions": .object(revisions),
                "options": options, "async": .bool(true), "confirmedParts": .bool(confirmedParts)
            ]))
            let result = response["operationId"].stringValue.isEmpty ? response : try await self.waitOperation(response)
            let plan = SavePreview(result)
            self.previewStale = self.saveOptionsPayload != options
            try await self.refresh()
            if automaticallySave && self.skipSavePreview && plan.canExecute && !self.previewStale {
                // Skipping the sheet never skips Worker preview/preflight or
                // server-owned revision/fingerprint/transaction validation.
                try await self.executeSavePlan(plan.planID)
            } else { self.preview = plan }
        }
    }
    func applySaveOutcome(_ result: JSONValue) {
        let outcome = SaveOperationOutcome(result)
        savedOutputs = outcome.outputs
        operationHadProblems = result["resultOmitted"].boolValue || !outcome.isCompleteSuccess
        operationResults = outcome.jobResults
        operationWarnings = outcome.cleanupWarnings
        operationCleanupIssues = outcome.cleanupIssues
        if result["resultOmitted"].boolValue { resultSummary = LocalizedStatusMessage("resultRequiresSnapshot", details: outcome.cleanupWarnings) }
        else if outcome.isCompleteSuccess { resultSummary = LocalizedStatusMessage("batchSavedCount", counts: [outcome.savedJobIDs.count], details: outcome.cleanupWarnings) }
        else { resultSummary = LocalizedStatusMessage("outcome." + outcome.outcome, details: outcome.cleanupWarnings) }
        if !outcome.isCompleteSuccess || outcome.jobResults.count > 1 { showingResults = true }
    }
    func save() {
        guard let preview, !previewStale, preview.canExecute, !working, !statusUnconfirmed else { return }
        confirmedSavePlanID = preview.planID
        previewStale = true
        working = true
        self.preview = nil
    }
    // Dispatch only after the native sheet has actually closed, so a synchronous
    // rejection (for example SOURCE_CHANGED) cannot hide its alert behind it.
    func previewDidDismiss() {
        guard let planID = confirmedSavePlanID else { return }
        confirmedSavePlanID = nil
        working = false
        perform {
            try await self.executeSavePlan(planID)
        }
    }
    private func executeSavePlan(_ planID: String) async throws {
        let result = try await worker.request("save.execute", params: .object(["planId": .string(planID)]))
        let completed = try await waitOperation(result)
        applySaveOutcome(completed)
        try await refresh()
    }
    func waitOperation(_ response: JSONValue) async throws -> JSONValue {
        let id = response["operationId"].stringValue
        guard !id.isEmpty else { throw RPCFailure(code: -32603, message: "Engine did not return an operation identifier.") }
        operationID = id
        defer { operationProgress.remove(id) }
        if let event = operationProgress.progress(for: id) { applyOperationProgress(event) }
        while true {
            let status: JSONValue
            do { status = try await worker.request("operations.get", params: .object(["operationId": .string(id)])) }
            catch let missing as RPCFailure where missing.reason == "OPERATION_NOT_FOUND" {
                statusUnconfirmed = true // Preserve the known ID; never replay the operation.
                throw missing
            }
            phase = status["phase"].stringValue.lowercased()
            switch status["status"].stringValue {
            case "completed": return status["result"]
            case "failed":
                let error = status["error"]
                throw RPCFailure(code: error["code"].intValue, message: error["message"].stringValue.isEmpty ? tr("operationFailed") : error["message"].stringValue, data: error["data"])
            case "canceled": throw RPCFailure(code: -32800, message: tr("operationCanceled"), data: .object(["reason": .string("CANCELED")]))
            default: try await Task.sleep(for: .milliseconds(180))
            }
        }
    }
    /// Reconcile only by reads. In particular, never resend save.execute after a timeout.
    @discardableResult func checkStatus() -> Task<Void, Never>? {
        guard !working, !engineProcessExited else { return nil }
        if showingTransactionRecovery { errorMessage = nil; errorDetails = "" }
        working = true
        return Task {
            defer { working = false; if !statusUnconfirmed { operationID = nil; phase = "" } }
            do {
                let trashSnapshot = try await worker.request("workspace.getSnapshot")
                let activeTrash = trashSnapshot["activeTrash"]
                if !activeTrash["trashToken"].stringValue.isEmpty && operationID == nil {
                    guard !trashRecycleInFlight else { return }
                    let result = try await worker.request("artwork.completeTrash", params: .object(["trashToken": activeTrash["trashToken"]]))
                    applyTrashResult(result); try await refresh(); statusUnconfirmed = false; return
                }
                var knownID = operationID
                if knownID == nil {
                    let snapshot = try await worker.request("workspace.getSnapshot")
                    knownID = snapshot["activeOperations"].arrayValue.first(where: { !$0["readOnly"].boolValue || $0["blocksWorkspace"].boolValue })?["operationId"].stringValue
                    if let knownID, !knownID.isEmpty { operationID = knownID }
                    else {
                        // A successful snapshot with no active operation confirms that the engine is idle.
                        try await refresh()
                        guard !jobs.contains(where: { $0.busy || $0.saveState == "Saving" }) else {
                            throw RPCFailure(code: -32000, message: tr("statusUnconfirmed"), data: .object(["reason": .string("WORKER_UNAVAILABLE")]))
                        }
                        statusUnconfirmed = false
                        if discoveryReview?.submitted == true { discoveryReview = nil }
                        resultSummary = LocalizedStatusMessage("statusChecked")
                        if showingTransactionRecovery {
                            try await loadTransactionRecovery()
                            transactionRecoveryMessage = tr("recoveryStatusRefreshed")
                        }
                        return
                    }
                }
                guard let knownID else { return }
                let state = try await worker.request("operations.get", params: .object(["operationId": .string(knownID)]))
                if ["completed", "failed", "canceled"].contains(state["status"].stringValue) {
                    try await refresh()
                    statusUnconfirmed = false
                    if state["status"].stringValue == "completed" {
                        try await applyReconciledOperationResult(state["result"])
                    } else {
                        let message = state["status"].stringValue == "canceled" ? tr("operationCanceled") : tr("operationFailed")
                        if discoveryReview != nil { discoveryErrorMessage = message; discoveryErrorDetails = state["error"]["message"].stringValue }
                        else { errorMessage = message; errorDetails = state["error"]["message"].stringValue }
                    }
                } else {
                    // The same known operation is still active: wait for it; do not create another.
                    statusUnconfirmed = false
                    let result = try await waitOperation(.object(["operationId": .string(knownID)]))
                    try await applyReconciledOperationResult(result)
                    try await refresh()
                }
            } catch let missing as RPCFailure where missing.reason == "OPERATION_NOT_FOUND" {
                statusUnconfirmed = true
                do {
                    if try await reconcileMissingOperationReceipt() { return }
                    if discoveryReview != nil { displayDiscoveryError(missing) } else { display(missing) }
                } catch {
                    if discoveryReview != nil { displayDiscoveryError(error) } else { display(error) }
                }
            } catch {
                statusUnconfirmed = true
                if discoveryReview != nil { displayDiscoveryError(error) } else { display(error) }
            }
        }
    }
    /// A missing historical receipt cannot prove success or cancellation. Only
    /// fresh, idle workspace state may unlock the UI; no mutation is replayed.
    private func reconcileMissingOperationReceipt() async throws -> Bool {
        let snapshot = try await worker.request("workspace.getSnapshot")
        guard snapshot["activeTrash"]["trashToken"].stringValue.isEmpty,
              !snapshot["activeOperations"].arrayValue.contains(where: { !$0["readOnly"].boolValue || $0["blocksWorkspace"].boolValue }),
              !snapshot["jobs"].arrayValue.contains(where: { $0["busy"].boolValue || $0["saveState"].stringValue == "Saving" }) else { return false }
        applySnapshot(snapshot, preserveEmptySelection: true)
        confirmedSavePlanID = nil; preview = nil; previewStale = true
        operationID = nil; statusUnconfirmed = false
        if discoveryReview?.submitted == true { discoveryReview = nil }
        discoveryErrorMessage = nil; discoveryErrorDetails = ""
        errorMessage = nil; errorDetails = ""
        savedOutputs = []; operationResults = []; operationWarnings = []; operationCleanupIssues = []
        operationHadProblems = true
        resultSummary = LocalizedStatusMessage("operationReceiptUnavailable")
        if showingTransactionRecovery {
            try await loadTransactionRecovery()
            transactionRecoveryMessage = tr("recoveryStatusRefreshed")
        }
        return true
    }
    private func applyReconciledOperationResult(_ result: JSONValue) async throws {
        if result["archived"].boolValue {
            try await loadTransactionRecovery()
            transactionRecoveryMessage = tr("recoveryArchivedReimport")
        } else if !result["recordPath"].stringValue.isEmpty && result["canArchive"] != .null && result["files"] != .null {
            transactionRecoveryInspection = result
            transactionRecoveryMessage = ""
            errorMessage = nil; errorDetails = ""
        } else if result["savedJobIds"] != .null { applySaveOutcome(result) }
        else if result["items"] != .null && !result["discoveryId"].stringValue.isEmpty { installDiscovery(result) }
        else if result["importedJobIds"] != .null { discoveryReview = nil; applyImportOutcome(result) }
        else { resultSummary = LocalizedStatusMessage("statusChecked") }
    }
    func cancel() {
        guard let operationID else { return }
        if discoveringInputs { discoveryCancellationRequested = true }
        phase = "canceling"
        Task { _ = try? await worker.request("operations.cancel", params: .object(["operationId": .string(operationID)])) }
    }
    func remove(_ job: MovieJobSnapshot) {
        guard !working && !hasPendingArtworkSelections && !statusUnconfirmed && !job.busy, commitEditing() else { return }
        if job.hasUnsavedChanges || !(drafts[job.jobID] ?? [:]).isEmpty {
            let alert = NSAlert()
            alert.messageText = tr("removeTitle"); alert.informativeText = tr("removeMessage")
            alert.addButton(withTitle: tr("keepEditing")); alert.addButton(withTitle: tr("remove"))
            alert.configureSafeCancelDefault()
            guard alert.runSafeModal() == .alertSecondButtonReturn else { return }
        }
        perform {
            self.cancelArtworkRequests(for: job.jobID)
            let result = try await self.worker.request("jobs.remove", params: .object(["jobId": .string(job.jobID)]))
            guard result["removed"].boolValue, result["removedIds"].arrayValue == [.string(job.jobID)], result["removedCount"].intValue == 1 else {
                throw RPCFailure(code: -32603, message: "The engine did not confirm the movie removal.")
            }
            try await self.refresh()
        }
    }
    @discardableResult func requestClearQueue(confirm: ((Int, Int) -> Bool)? = nil, commit: (() -> Bool)? = nil) -> Task<Void, Never>? {
        guard canClearQueue, prepareActionCommand(commit: commit), canClearQueue else { return nil }
        let ids = jobs.map(\.jobID), revisions = jobs.map(\.reviewRevision), originalDrafts = drafts
        let confirmed: Bool
        if let confirm { confirmed = confirm(ids.count, unsavedCount) }
        else {
            let alert = NSAlert()
            alert.messageText = tr("clearQueueTitle")
            alert.informativeText = String(format: tr("clearQueueMessage"), ids.count, unsavedCount)
            alert.addButton(withTitle: tr("cancel")); alert.addButton(withTitle: tr("clearQueueConfirm"))
            confirmed = alert.runSafeModal() == .alertSecondButtonReturn
        }
        // A confirmation cannot authorize a different or newly edited queue.
        guard confirmed, canClearQueue, jobs.map(\.jobID) == ids,
              jobs.map(\.reviewRevision) == revisions, drafts == originalDrafts else { return nil }
        working = true
        return Task {
            defer { working = false; if !statusUnconfirmed { operationID = nil; phase = "" } }
            do {
                let result = try await worker.request("jobs.remove", params: .object(["jobIds": .array(ids.map(JSONValue.string))]))
                guard result["removed"].boolValue, Set(result["removedIds"].arrayValue.map(\.stringValue)) == Set(ids), result["removedCount"].intValue == ids.count else {
                    throw RPCFailure(code: -32603, message: "The engine did not confirm the complete queue removal.")
                }
                // Do not discard any local review until the complete RPC has
                // succeeded. Queue removal never calls artwork trash or save.
                let removed = Set(ids)
                invalidatePendingQueueUpdates()
                for id in ids { invalidateArtworkCache(jobID: id) }
                jobs.removeAll { removed.contains($0.jobID) }
                drafts = drafts.filter { !removed.contains($0.key) }
                textSessions = textSessions.filter { !removed.contains($0.value.jobID) }
                jobLabels = jobLabels.filter { !removed.contains($0.key) }
                artworkSourceByJob = artworkSourceByJob.filter { !removed.contains($0.key) }
                navigationSelection = []; selectedID = nil; checkedJobIDs.subtract(removed)
                preview = nil; previewStale = false; confirmedSavePlanID = nil
                pendingPartsPreviewIDs = nil; partsReviewIDs = []
                viewerJobID = ""; viewerArtwork = []; viewerSource = ""; viewerIndex = 0
                searchText = ""; queueCategory = .all
                savedOutputs = []; operationResults = []; operationWarnings = []; operationCleanupIssues = []
                operationProgress.removeAll()
                operationHadProblems = false; errorMessage = nil; errorDetails = ""
                resultSummary = LocalizedStatusMessage("queueClearedCount", counts: [ids.count])
                try await refresh()
            } catch { display(error); try? await refresh() }
        }
    }
    func trashArtwork(_ artwork: JSONValue, jobID: String) {
        guard capabilities["localArtworkTrash"].boolValue && artwork["source"].stringValue == "local" else { return }
        perform {
            // A draft flush can replace the job snapshot and restart view tasks.
            // Pause new image work before flushing, through final lease release.
            self.artworkTrashFlowActive = true
            defer { self.artworkTrashFlowActive = false; self.resumeArtworkAfterTrash() }
            try await self.flush(jobID)
            guard let job = self.jobs.first(where: { $0.jobID == jobID }) else { return }
            await self.cancelAllArtworkAndWait()
            let prepared = try await self.worker.request("artwork.prepareTrash", params: .object(["jobId": .string(jobID), "expectedReviewRevision": .number(Double(job.reviewRevision)), "artworkId": artwork["artworkId"]]))
            let token = prepared["trashToken"].stringValue
            self.pendingTrashToken = token
            let paths = prepared["paths"].arrayValue.map(\.stringValue)
            guard !token.isEmpty, !paths.isEmpty else { throw RPCFailure(code: -32603, message: tr("trashFailed")) }
            let alert = NSAlert()
            alert.messageText = tr("trashTitle")
            alert.informativeText = tr("trashExplanation") + "\n\n" + paths.joined(separator: "\n")
            alert.addButton(withTitle: tr("cancel")); alert.addButton(withTitle: tr("trashConfirm"))
            if await alert.presentSafeSheet(for: self.workspaceWindow) != .alertSecondButtonReturn {
                do { _ = try await self.worker.request("artwork.abortTrash", params: .object(["trashToken": .string(token)])); self.pendingTrashToken = nil }
                catch { let result = try await self.worker.request("artwork.completeTrash", params: .object(["trashToken": .string(token)])); self.applyTrashResult(result) }
                return
            }
            self.phase = "trashingartwork"
            do {
                self.trashRecycleInFlight = true
                let result = try await FileTrashCoordinator.recycle(paths.map { URL(fileURLWithPath: $0) }) {
                    let validated = try await self.worker.request("artwork.validateTrash", params: .object(["trashToken": .string(token)]))
                    guard validated["authorized"].boolValue, validated["paths"].arrayValue.map(\.stringValue) == paths else { throw RPCFailure(code: -32010, message: tr("trashFailed")) }
                }
                self.trashRecycleInFlight = false
                let completed = try await self.worker.request("artwork.completeTrash", params: .object(["trashToken": .string(token)]))
                self.applyTrashResult(completed)
                if let error = result.error { self.errorMessage = tr("trashFailed"); self.errorDetails = error.localizedDescription }
            } catch {
                self.trashRecycleInFlight = false
                // Reconcile after the native callback or failed authorization only; never release a live recycle.
                do {
                    let completed = try await self.worker.request("artwork.completeTrash", params: .object(["trashToken": .string(token)]))
                    self.applyTrashResult(completed)
                } catch { self.statusUnconfirmed = true }
                throw error
            }
        }
    }
    private func applyTrashResult(_ result: JSONValue) {
        for snapshot in result["jobs"].arrayValue { update(snapshot) }
        releaseArtworkLeases(for: Set(artworkLeaseIDs.keys))
        artworkPaths.removeAll(); artworkDimensions.removeAll(); artworkErrors.removeAll(); artworkLowResolution.removeAll()
        artworkAccess.removeAll(); artworkFileBytes.removeAll()
        pendingTrashToken = nil; previewStale = true
        operationWarnings = result["warnings"].arrayValue.map(\.stringValue)
        operationHadProblems = !operationWarnings.isEmpty
        resultSummary = LocalizedStatusMessage("trashCompleted", counts: [result["removedPaths"].arrayValue.count])
        if !operationWarnings.isEmpty { errorMessage = operationWarnings.joined(separator: "\n") }
        resumeArtworkAfterTrash()
    }
    private func resumeArtworkAfterTrash() {
        guard !artworkTrashFlowActive, pendingTrashToken == nil, !trashRecycleInFlight else { return }
        artworkErrors.removeAll()
        // View request identity is separate from the authoritative review revision.
        artworkLoadGeneration &+= 1
    }
    var viewerImageURL: URL? {
        guard viewerArtwork.indices.contains(viewerIndex) else { return nil }
        return artworkPaths[artworkKey(viewerArtwork[viewerIndex], jobID: viewerJobID)]
    }
    var viewerImageError: String? {
        guard viewerImageURL == nil, viewerArtwork.indices.contains(viewerIndex) else { return nil }
        return artworkErrors[artworkKey(viewerArtwork[viewerIndex], jobID: viewerJobID)]
    }
    func retryViewerArtwork(invalidatePublishedPreview: Bool = false) {
        guard ready, !working, !statusUnconfirmed, showingImage, viewerArtwork.indices.contains(viewerIndex),
              let job = jobs.first(where: { $0.jobID == viewerJobID }),
              let artwork = job.artwork.first(where: { $0["artworkId"] == viewerArtwork[viewerIndex]["artworkId"] }) else { return }
        let key = artworkRequestKey(artwork, job: job)
        for request in imageRequests.values where request.key == key { cancelArtworkRequest(request) }
        if invalidatePublishedPreview {
            let publishedKey = artworkKey(artwork, jobID: job.jobID)
            releaseArtworkLeases(for: [publishedKey])
            artworkPaths.removeValue(forKey: publishedKey); artworkErrors.removeValue(forKey: publishedKey)
            artworkDimensions.removeValue(forKey: publishedKey); artworkLowResolution.remove(publishedKey)
        }
        loadArtwork(artwork, jobID: job.jobID, scope: "viewer")
    }
    func openArtwork(_ artwork: JSONValue, job: MovieJobSnapshot) {
        guard commitEditing() else { return }
        // Every entry opens the complete gallery and locates the actual tapped image.
        // Resetting the display filter does not mutate reviewed sample selections.
        viewerSource = ""
        viewerJobID = job.jobID; viewerArtwork = job.artwork
        viewerIndex = ArtworkReviewPolicy.entryIndex(viewerArtwork, artworkID: artwork["artworkId"].stringValue)
        showingImage = true
        beginArtworkRead(artwork, jobID: job.jobID, readerID: viewerReaderID, scope: "viewer")
    }
    func moveViewer(by offset: Int) {
        guard viewerArtwork.indices.contains(viewerIndex + offset) else { return }
        viewerIndex += offset
        beginArtworkRead(viewerArtwork[viewerIndex], jobID: viewerJobID, readerID: viewerReaderID, scope: "viewer")
    }
    func artworkKey(_ artwork: JSONValue, jobID: String) -> String {
        jobID + "/" + artwork["artworkId"].stringValue
    }
    func artworkRequestKey(_ artwork: JSONValue, job: MovieJobSnapshot) -> String {
        let content = artwork["contentKey"].stringValue
        return artworkKey(artwork, jobID: job.jobID) + "/" + (content.isEmpty ? "revision-\(job.reviewRevision)" : content)
    }
    private func isCurrentArtworkRequest(_ key: String, artworkID: JSONValue, jobID: String) -> Bool {
        guard let job = jobs.first(where: { $0.jobID == jobID }), let art = job.artwork.first(where: { $0["artworkId"] == artworkID }) else { return false }
        return artworkRequestKey(art, job: job) == key
    }
    private func cancelArtworkRequests(except jobIDs: Set<String>) {
        for request in imageRequests.values where request.scope != "queue" && !jobIDs.contains(request.jobID) && !artworkReaders.values.contains(request.cacheKey) { cancelArtworkRequest(request) }
    }
    private func cancelArtworkRequests(for jobID: String) {
        for request in imageRequests.values where request.jobID == jobID { cancelArtworkRequest(request) }
    }
    private func cancelArtworkRequest(_ request: ArtworkRequest) {
        request.task?.cancel()
        // The client knows the lease before the operation reply. Releasing it
        // revokes in-flight generation as well as a completed preview.
        if request.usesLease { releaseArtworkLease(request.id.uuidString) }
        if let id = request.operationID { Task { _ = try? await worker.request("operations.cancel", params: .object(["operationId": .string(id)])) } }
    }
    private func releaseArtworkLease(_ leaseID: String) {
        let previous = artworkReleaseTail
        artworkReleaseTail = Task {
            await previous?.value
            _ = try? await worker.request("artwork.releasePreview", params: .object(["leaseId": .string(leaseID)]))
        }
    }
    private func releaseArtworkLeases(for keys: Set<String>) {
        for key in keys {
            if let lease = artworkLeaseIDs.removeValue(forKey: key) {
                if let url = artworkPaths[key], artworkURLReaders.values.contains(url) {
                    // Retain exact retired IDs: another job/generation can adopt
                    // a new lease for the same immutable content URL meanwhile.
                    retiredArtworkLeases[url, default: []].insert(lease)
                } else { releaseArtworkLease(lease) }
            }
            artworkAccess.removeValue(forKey: key)
            artworkFileBytes.removeValue(forKey: key)
        }
    }
    func beginArtworkRead(_ artwork: JSONValue, jobID: String, readerID: UUID, scope: String = "detail") {
        let key = artworkKey(artwork, jobID: jobID)
        let old = artworkReaders.updateValue(key, forKey: readerID)
        if let old, old != key { cancelUnobservedArtwork(old) }
        touchArtwork(key)
        loadArtwork(artwork, jobID: jobID, scope: scope)
        trimArtworkHistory()
    }
    func endArtworkRead(_ readerID: UUID) {
        if let key = artworkReaders.removeValue(forKey: readerID) { cancelUnobservedArtwork(key) }
        trimArtworkHistory()
    }
    private func cancelUnobservedArtwork(_ key: String) {
        guard !artworkReaders.values.contains(key) else { return }
        for request in imageRequests.values where request.cacheKey == key { cancelArtworkRequest(request) }
    }
    func beginArtworkURLRead(_ url: URL) -> UUID? {
        guard artworkPaths.values.contains(url) else { return nil }
        let token = UUID()
        artworkURLReaders[token] = url
        return token
    }
    func endArtworkURLRead(_ token: UUID) {
        if let url = artworkURLReaders.removeValue(forKey: token), !artworkURLReaders.values.contains(url) {
            for lease in retiredArtworkLeases.removeValue(forKey: url) ?? [] { releaseArtworkLease(lease) }
        }
        trimArtworkHistory()
    }
    private func touchArtwork(_ key: String) {
        guard artworkPaths[key] != nil else { return }
        artworkClock &+= 1
        artworkAccess[key] = artworkClock
    }
    private var historicalArtworkKeys: [String] {
        let visible = Set(artworkReaders.values), decoding = Set(artworkURLReaders.values)
        return artworkPaths.keys.filter { !visible.contains($0) && !decoding.contains(artworkPaths[$0]!) }
    }
    var artworkPreviewRetention: (files: Int, historical: Int, historyBytes: Int, readers: Int, decoders: Int, retiredLeases: Int) {
        let history = historicalArtworkKeys
        return (artworkPaths.count, history.count, history.reduce(0) { $0 + (artworkFileBytes[$1] ?? 0) },
                artworkReaders.count, artworkURLReaders.count, retiredArtworkLeases.values.reduce(0) { $0 + $1.count })
    }
    private func trimArtworkHistory() {
        let history = historicalArtworkKeys.sorted { (artworkAccess[$0] ?? 0) < (artworkAccess[$1] ?? 0) }
        var count = history.count, bytes = history.reduce(0) { $0 + (artworkFileBytes[$1] ?? 0) }
        for key in history {
            guard count > artworkHistoryMaximumCount || bytes > artworkHistoryMaximumBytes else { break }
            count -= 1; bytes -= artworkFileBytes[key] ?? 0
            releaseArtworkLeases(for: [key])
            artworkPaths.removeValue(forKey: key)
            // Dimensions and low-resolution warnings describe reviewed content,
            // not the generated payload; keep them until content invalidation.
        }
    }
    private func cancelAllArtworkAndWait() async {
        let requests = Array(imageRequests.values)
        for request in requests { cancelArtworkRequest(request) }
        for request in requests { await request.task?.value }
    }
    private func finishArtworkCancellation(_ id: String) async {
        _ = try? await worker.request("operations.cancel", params: .object(["operationId": .string(id)]))
        for _ in 0..<100 {
            guard let state = try? await worker.request("operations.get", params: .object(["operationId": .string(id)])), state["status"].stringValue == "running" else { return }
            // The owning task is canceled; a separate short delay avoids a tight canceled-sleep loop.
            await Task.detached { try? await Task.sleep(for: .milliseconds(50)) }.value
        }
    }
    func loadArtwork(_ artwork: JSONValue, jobID: String, scope: String = "detail") {
        guard !artworkTrashFlowActive, pendingTrashToken == nil, !trashRecycleInFlight else { return }
        let id = artworkKey(artwork, jobID: jobID)
        guard let job = jobs.first(where: { $0.jobID == jobID }),
              let current = job.artwork.first(where: { $0["artworkId"] == artwork["artworkId"] }) else { return }
        let taskKey = artworkRequestKey(current, job: job)
        if let cached = artworkPaths[id] {
            // Caches can be cleared outside the app. A stale URL must re-enter
            // the leased Worker path instead of leaving the viewer loading forever.
            let attributes = try? FileManager.default.attributesOfItem(atPath: cached.path)
            if attributes?[.type] as? FileAttributeType == .typeRegular {
                touchArtwork(id); trimArtworkHistory(); return
            }
            releaseArtworkLeases(for: [id])
            artworkPaths.removeValue(forKey: id)
            artworkDimensions.removeValue(forKey: id)
            artworkErrors.removeValue(forKey: id)
            artworkLowResolution.remove(id)
        }
        guard artworkPaths[id] == nil, !imageRequests.values.contains(where: { $0.key == taskKey && $0.task?.isCancelled == false }) else { return }
        let request = ArtworkRequest(jobID: jobID, key: taskKey, cacheKey: id, scope: scope,
                                     usesLease: capabilities["artworkPreviewLeases"].boolValue)
        imageRequests[request.id] = request
        artworkRequestOwners[taskKey] = request.id
        artworkErrors.removeValue(forKey: id)
        request.task = Task {
            var adoptedLease = false
            defer {
                // Also releases after a timeout/lost reply. A canceled operation
                // must not leave a completed, unowned preview pinned forever.
                if request.usesLease && !adoptedLease { releaseArtworkLease(request.id.uuidString) }
                imageRequests.removeValue(forKey: request.id)
                if artworkRequestOwners[taskKey] == request.id { artworkRequestOwners.removeValue(forKey: taskKey) }
                trimArtworkHistory()
            }
            do {
                // Reclamation must reach the Worker before a new admission can
                // overtake it, especially with a small cache budget.
                await artworkReleaseTail?.value
                try Task.checkCancellation()
                var params: [String: JSONValue] = ["jobId": .string(jobID), "artworkId": artwork["artworkId"], "async": .bool(true)]
                if request.usesLease { params["leaseId"] = .string(request.id.uuidString) }
                let accepted = try await worker.request("artwork.getPreview", params: .object(params))
                let operation = accepted["operationId"].stringValue
                request.operationID = operation.isEmpty ? nil : operation
                var response = accepted
                if !operation.isEmpty {
                    while true {
                        try Task.checkCancellation()
                        let state = try await worker.request("operations.get", params: .object(["operationId": .string(operation)]))
                        if state["status"].stringValue == "completed" { response = state["result"]; break }
                        if state["status"].stringValue == "canceled" { throw CancellationError() }
                        if state["status"].stringValue == "failed" { throw RPCFailure(code: state["error"]["code"].intValue, message: state["error"]["message"].stringValue, data: state["error"]["data"]) }
                        try await Task.sleep(for: .milliseconds(100))
                    }
                }
                let url = URL(fileURLWithPath: response["path"].stringValue).resolvingSymlinksInPath().standardizedFileURL
                let cachePath = await worker.handshake["cacheRoot"].stringValue
                let cacheRoot = URL(fileURLWithPath: cachePath).resolvingSymlinksInPath().standardizedFileURL
                try Task.checkCancellation()
                guard !cachePath.isEmpty, url.path.hasPrefix(cacheRoot.path + "/") else { throw RPCFailure(code: -32603, message: "Rejected an image outside the engine cache.") }
                guard artworkRequestOwners[taskKey] == request.id, isCurrentArtworkRequest(taskKey, artworkID: artwork["artworkId"], jobID: jobID) else { return }
                if request.usesLease {
                    guard response["leaseId"].stringValue == request.id.uuidString else {
                        throw RPCFailure(code: -32603, message: "The engine did not confirm the preview lease.")
                    }
                    releaseArtworkLeases(for: [id])
                    artworkLeaseIDs[id] = request.id.uuidString
                    adoptedLease = true
                }
                artworkPaths[id] = url
                artworkFileBytes[id] = (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
                touchArtwork(id)
                artworkErrors.removeValue(forKey: id)
                artworkDimensions[id] = CGSize(width: response["width"].intValue, height: response["height"].intValue)
                if response["isLowResolution"].boolValue { artworkLowResolution.insert(id) } else { artworkLowResolution.remove(id) }
            } catch {
                if Task.isCancelled || error is CancellationError {
                    if let operation = request.operationID { await finishArtworkCancellation(operation) }
                } else if artworkRequestOwners[taskKey] == request.id, isCurrentArtworkRequest(taskKey, artworkID: artwork["artworkId"], jobID: jobID) { artworkErrors[id] = error.localizedDescription }
            }
        }
    }
    func display(_ error: Error) {
        errorMessage = tr("operationFailed")
        errorDetails = error.localizedDescription
        if let rpc = error as? RPCFailure {
            if rpc.reason == "WORKER_UNAVAILABLE" && (working || operationID != nil || statusUnconfirmed) { statusUnconfirmed = true; preview = nil }
            let localized = tr("error." + rpc.reason)
            if localized != "error." + rpc.reason { errorMessage = localized }
            errorDetails = rpc.reason.isEmpty ? rpc.message : rpc.reason + "\n" + rpc.message
        }
        else if let capture = error as? BrowserCaptureError {
            switch capture {
            case .staleNavigation: errorMessage = tr("browserStale")
            case .invalidScheme: errorMessage = tr("browserSchemeBlocked")
            case .tooLarge: errorMessage = tr("browserTooLarge")
            case .emptyDocument: errorMessage = tr("browserCaptureFailed")
            }
            errorDetails = ""
        }
        else { errorDetails = String(describing: error) }
    }
    func reveal(_ path: String) { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: path)]) }
    func status(_ job: MovieJobSnapshot) -> String {
        if job.busy { return tr(job.saveState == "Saving" ? "saving" : "searching") }
        if job.localNfoSaveBlocked { return tr("nfoBlocked") }
        switch job.saveState {
        case "Completed" where !(job.hasUnsavedChanges || !(drafts[job.jobID] ?? [:]).isEmpty || hasLowResolutionCover(job)): return tr("saved")
        case "SaveFailed", "Conflict": return tr("saveFailed")
        case "SaveCanceled": return tr("canceled")
        default: break
        }
        if hasLowResolutionCover(job) { return tr("readyToReview") }
        if job.hasFailedSources || job.searchState == "PartialSucceeded" { return tr("queryPartial") }
        if !(drafts[job.jobID] ?? [:]).isEmpty || job.hasUnsavedChanges { return tr("unsavedChanges") }
        switch job.searchState {
        case "NeedsId": return tr("needsID")
        case "NeedsReview": return tr("readyToReview")
        case "SearchFailed": return tr("searchFailed")
        case "Searching": return tr("searching")
        case "SearchCanceled": return tr("canceled")
        default: return tr("imported")
        }
    }
}
