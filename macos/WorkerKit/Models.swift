import Foundation

public struct DiscoveredMovie: Identifiable, Sendable {
    public var id: String { itemID }
    public let itemID: String
    public let movieID: String
    public let primaryPath: String
    public let videoPaths: [String]
    public let alreadyImported: Bool
    public let canImport: Bool
    public let unavailableReason: String
    public init(_ json: JSONValue) {
        itemID = json["itemId"].stringValue; movieID = json["movieId"].stringValue
        primaryPath = json["primaryPath"].stringValue
        videoPaths = json["videoPaths"].arrayValue.map(\.stringValue)
        alreadyImported = json["alreadyImported"].boolValue; canImport = json["canImport"].boolValue
        unavailableReason = json["unavailableReason"].stringValue
    }
}

public struct InputDiscovery: Identifiable, Sendable {
    public var id: String { discoveryID }
    public let discoveryID: String
    public let expiresAt: String
    public let items: [DiscoveredMovie]
    public let raw: JSONValue
    public init(_ json: JSONValue) {
        raw = json; discoveryID = json["discoveryId"].stringValue
        expiresAt = json["expiresAt"].stringValue
        items = json["items"].arrayValue.map(DiscoveredMovie.init)
    }
}

/// This review is independent of queue navigation, batch checkmarks and drafts.
/// Selection is frozen synchronously when confirmation starts, before any await.
public struct InputDiscoveryReview: Sendable {
    public let discovery: InputDiscovery
    public private(set) var selectedIDs: Set<String>
    public private(set) var submitted = false
    public init(_ discovery: InputDiscovery) {
        self.discovery = discovery
        selectedIDs = Set(discovery.items.filter(\.canImport).map(\.itemID))
    }
    public var availableIDs: Set<String> { Set(discovery.items.filter(\.canImport).map(\.itemID)) }
    public var recognizedIDs: Set<String> { Set(discovery.items.filter { $0.canImport && !$0.movieID.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }.map(\.itemID)) }
    public var canConfirm: Bool { !submitted && !selectedIDs.isEmpty }
    public mutating func selectAll() { guard !submitted else { return }; selectedIDs = availableIDs }
    public mutating func deselectAll() { guard !submitted else { return }; selectedIDs = [] }
    public mutating func selectRecognized() { guard !submitted, !recognizedIDs.isEmpty else { return }; selectedIDs = recognizedIDs }
    public mutating func setSelected(_ itemID: String, _ selected: Bool) {
        guard !submitted, availableIDs.contains(itemID) else { return }
        if selected { selectedIDs.insert(itemID) } else { selectedIDs.remove(itemID) }
    }
    public mutating func beginConfirmation() -> [String]? {
        guard canConfirm else { return nil }
        submitted = true
        return discovery.items.filter { selectedIDs.contains($0.itemID) }.map(\.itemID)
    }
}

public struct MovieJobSnapshot: Identifiable, Sendable {
    public var id: String { jobID }
    public let jobID: String
    public let reviewRevision: Int
    public let metadata: JSONValue
    public let videoPaths: [String]
    public let localNfoPath: String?
    public let searchState: String
    public let saveState: String
    public let hasUnsavedChanges: Bool
    public let localNfoSaveBlocked: Bool
    public let warnings: [String]
    public let candidates: [JSONValue]
    public let artwork: [JSONValue]
    public let reviewSources: [String]
    public let reviewSource: String
    public let coverQuality: JSONValue
    public let hasFailedSources: Bool
    public let searchAttempts: [JSONValue]
    public let busy: Bool
    public init(_ json: JSONValue) {
        jobID = json["jobId"].stringValue; reviewRevision = json["reviewRevision"].intValue
        metadata = json["metadata"]; videoPaths = json["videoPaths"].arrayValue.map(\.stringValue)
        localNfoPath = json["localNfoPath"].stringValue.isEmpty ? nil : json["localNfoPath"].stringValue
        searchState = json["searchState"].stringValue; saveState = json["saveState"].stringValue
        hasUnsavedChanges = json["hasUnsavedChanges"].boolValue; localNfoSaveBlocked = json["localNfoSaveBlocked"].boolValue
        warnings = json["warnings"].arrayValue.map(\.stringValue)
        candidates = json["candidates"].arrayValue; artwork = json["artwork"].arrayValue; busy = json["busy"].boolValue
        reviewSources = json["reviewSources"].arrayValue.map(\.stringValue); reviewSource = json["reviewSource"].stringValue
        coverQuality = json["coverQuality"]; hasFailedSources = json["hasFailedSources"].boolValue
        searchAttempts = json["searchAttempts"].arrayValue
    }
}

public enum QueueCategory: String, CaseIterable, Sendable {
    case all, pending, issues
}

/// Display filtering, checked batch membership, and keyboard navigation remain
/// independent. These decisions never trigger provider requests or edit metadata.
public enum QueueReviewPolicy {
    public static func hasIssue(_ job: MovieJobSnapshot, knownLowResolution: Bool = false) -> Bool {
        job.metadata["id"].stringValue.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ||
        job.localNfoSaveBlocked || job.hasFailedSources || knownLowResolution ||
        job.coverQuality["state"].stringValue == "lowResolution" ||
        ["SearchFailed", "PartialSucceeded"].contains(job.searchState) || ["SaveFailed", "Conflict"].contains(job.saveState)
    }
    public static func matches(_ job: MovieJobSnapshot, category: QueueCategory, hasDraft: Bool = false, knownLowResolution: Bool = false) -> Bool {
        switch category {
        case .all: return true
        case .pending: return job.saveState != "Completed" || job.hasUnsavedChanges || hasDraft
        case .issues: return hasIssue(job, knownLowResolution: knownLowResolution)
        }
    }
    public static func recognizedIDs(_ jobs: [MovieJobSnapshot]) -> Set<String> {
        Set(jobs.filter { !$0.metadata["id"].stringValue.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }.map(\.jobID))
    }
}

public enum ArtworkReviewPolicy {
    /// Preview only the reviewed cover pair. Never mix in an unselected source
    /// or a sample when one member of that pair is missing.
    public static func primary(_ artwork: [JSONValue], kind: String) -> JSONValue? {
        let selected = artwork.filter { $0["selected"].boolValue && $0["kind"].stringValue != "sample" }
        if let exact = selected.first(where: { $0["kind"].stringValue == kind }) { return exact }
        // Remote full covers may also supply the generated right-half poster.
        if kind == "poster" { return selected.first { $0["kind"].stringValue == "fanart" && $0["source"].stringValue != "local" } }
        return nil
    }
    public static func source(_ artwork: JSONValue) -> String {
        artwork["source"].stringValue == "local" ? "local" : artwork["source"].stringValue
    }
    public static func sources(_ artwork: [JSONValue]) -> [String] {
        var seen: Set<String> = []
        return artwork.map(source).filter { !$0.isEmpty && seen.insert($0).inserted }
    }
    public static func filtered(_ artwork: [JSONValue], source: String?) -> [JSONValue] {
        guard let source, !source.isEmpty else { return artwork }
        return artwork.filter { self.source($0) == source }
    }
    public static func entryIndex(_ artwork: [JSONValue], artworkID: String) -> Int {
        artwork.firstIndex { $0["artworkId"].stringValue == artworkID } ?? 0
    }
    public static func previewState(hasArtwork: Bool, loaded: Bool, failed: Bool) -> String {
        !hasArtwork ? "noArtwork" : loaded ? "loaded" : failed ? "artworkPreviewFailed" : "artworkNotLoaded"
    }
}

public struct SavePreviewJob: Identifiable, Sendable {
    public var id: String { jobID }
    public let jobID: String
    public let reviewRevision: Int
    public let changes: [JSONValue]
    public let warnings: [String]
    public let blockingConflicts: [String]
    public init(_ json: JSONValue) {
        jobID = json["jobId"].stringValue; reviewRevision = json["reviewRevision"].intValue
        changes = json["changes"].arrayValue
        warnings = json["warnings"].arrayValue.map(\.stringValue)
        blockingConflicts = json["blockingConflicts"].arrayValue.map { $0.stringValue.isEmpty ? $0["message"].stringValue : $0.stringValue }
    }
}

public struct SavePreview: Identifiable, Sendable {
    public var id: String { planID }
    public let planID: String
    public let jobID: String
    public let reviewRevision: Int
    public let canExecute: Bool
    public let changes: [JSONValue]
    public let warnings: [String]
    public let blockingConflicts: [String]
    public let jobs: [SavePreviewJob]
    public let sourceJobIDs: [String]
    public var jobIDs: [String] { sourceJobIDs.isEmpty ? jobs.map(\.jobID) : sourceJobIDs }
    public var combinesParts: Bool { sourceJobIDs.count > jobs.count }
    public var containsVideoTransfer: Bool { jobs.contains { $0.changes.contains { ["MoveVideo", "RenameVideo", "MoveAndRenameVideo", "CopyAndVerifyVideo", "CopyVideo"].contains($0["operation"].stringValue) } } }
    public init(_ json: JSONValue) {
        planID = json["planId"].stringValue; jobID = json["jobId"].stringValue
        reviewRevision = json["reviewRevision"].intValue; canExecute = json["canExecute"].boolValue
        changes = json["changes"].arrayValue
        warnings = json["warnings"].arrayValue.map(\.stringValue)
        blockingConflicts = json["blockingConflicts"].arrayValue.map { $0.stringValue.isEmpty ? $0["message"].stringValue : $0.stringValue }
        jobs = json["jobs"].arrayValue.isEmpty ? [SavePreviewJob(json)] : json["jobs"].arrayValue.map(SavePreviewJob.init)
        sourceJobIDs = json["sourceJobIds"].arrayValue.map(\.stringValue)
    }
}

public struct SaveOperationOutcome: Sendable {
    public let outcome: String
    public let savedJobIDs: [String]
    public let notStartedJobIDs: [String]
    public let cleanupWarnings: [String]
    public let cleanupIssues: [JSONValue]
    public let outputs: [String]
    public let jobResults: [JSONValue]
    public var isCompleteSuccess: Bool { outcome == "completed" && notStartedJobIDs.isEmpty && cleanupWarnings.isEmpty && cleanupIssues.isEmpty && jobResults.allSatisfy { $0["status"].stringValue == "Completed" } }
    public init(_ json: JSONValue) {
        outcome = json["outcome"].stringValue.isEmpty ? "completed" : json["outcome"].stringValue
        savedJobIDs = json["savedJobIds"].arrayValue.map(\.stringValue)
        notStartedJobIDs = json["notStartedJobIds"].arrayValue.map(\.stringValue)
        cleanupWarnings = json["cleanupWarnings"].arrayValue.map(\.stringValue)
        cleanupIssues = json["cleanupIssues"].arrayValue
        outputs = json["outputs"].arrayValue.map(\.stringValue)
        jobResults = json["jobResults"].arrayValue
    }
}
