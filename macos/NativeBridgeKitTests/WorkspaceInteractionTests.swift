import Testing
import Foundation
import AppKit
import Combine
#if SWIFT_PACKAGE
import NativeBridgeKit
@testable import JavMetaLiteMac
import WorkerKit
#else
@testable import JavMetaLiteMac
#endif

@Suite(.serialized) @MainActor struct WorkspaceInteractionTests {
    private let preferences = TestPreferenceDomain()
    @Test func workspaceSettingsRestoreWithoutRestoringQueueDraftsOrPlans() {
        let store = WorkspaceStore(defaults: preferences.defaults)
        store.writeNfo = false; store.writePoster = true; store.writeFanart = true; store.writeSamples = true
        store.replaceLocalSamples = true; store.renameVideo = true; store.includeIDInTitle = true
        store.skipSavePreview = true; store.targetMode = "CustomRootNumberFolder"; store.customRootDirectory = "/tmp/合成 root"
        store.sourceMode = "custom"; store.queueVisible = false; store.inspectorVisible = false
        store.moreOptionsExpanded = true; store.importRecursively = false
        store.sourceProfile["titleSource"] = "r18dev"
        store.drafts["not-persistent"] = ["title": "Do not restore unsaved work as preferences"]
        store.checkedJobIDs = ["not-persistent"]
        let restored = WorkspaceStore(defaults: preferences.defaults)
        #expect(!restored.writeNfo && restored.writePoster && restored.writeFanart && restored.writeSamples)
        #expect(restored.replaceLocalSamples && restored.renameVideo && restored.includeIDInTitle && restored.skipSavePreview)
        #expect(restored.targetMode == "CustomRootNumberFolder" && restored.customRootDirectory == "/tmp/合成 root")
        #expect(restored.sourceMode == "custom" && restored.sourceProfile["titleSource"] == "r18dev")
        #expect(!restored.queueVisible && !restored.inspectorVisible && restored.moreOptionsExpanded && !restored.importRecursively)
        #expect(restored.jobs.isEmpty && restored.drafts.isEmpty && restored.checkedJobIDs.isEmpty && restored.preview == nil)
        #expect(!restored.previewStale && restored.saveOptionsPayload["overwriteExisting"].boolValue)
        restored.targetMode = "SourceNumberFolder"
        #expect(WorkspaceStore(defaults: preferences.defaults).targetMode == "SourceNumberFolder")
    }
    @Test func preferenceMigrationAndInvalidValuesKeepSafeDefaults() {
        preferences.defaults.set(true, forKey: "skipSavePreview")
        let legacy = WorkspaceStore(defaults: preferences.defaults)
        #expect(legacy.skipSavePreview && legacy.writeNfo && legacy.inspectorVisible && legacy.queueVisible)
        #expect(!legacy.writePoster && !legacy.renameVideo)
        preferences.defaults.set(["targetMode": "unrecognized", "sourceMode": "combined-removed", "customRootDirectory": "relative/path", "writeNfo": "bad"], forKey: WorkspaceStore.workspacePreferencesKey)
        let invalid = WorkspaceStore(defaults: preferences.defaults)
        #expect(invalid.targetMode == "VideoDirectory" && invalid.sourceMode == "libredmm" && invalid.customRootDirectory.isEmpty && invalid.writeNfo)
    }
    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil))
    func engineStartupDoesNotResetRestoredSaveControls() async throws {
        _ = NSApplication.shared
        let path = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JML-prefs-\(UUID())")
        defer { try? FileManager.default.removeItem(at: directory) }
        let first = WorkspaceStore(defaults: preferences.defaults)
        first.writePoster = true; first.writeFanart = true; first.writeSamples = true; first.includeIDInTitle = false
        first.targetMode = "CustomRootNumberFolder"; first.customRootDirectory = directory.path; first.renameVideo = true
        let store = WorkspaceStore(engineExecutable: { URL(fileURLWithPath: path) }, engineArguments: ["--data-root", directory.appendingPathComponent("state").path], defaults: preferences.defaults)
        await store.start()
        #expect(store.ready && store.writePoster && store.writeFanart && store.writeSamples && !store.includeIDInTitle)
        #expect(store.targetMode == "CustomRootNumberFolder" && store.customRootDirectory == directory.path && store.renameVideo)
        await store.worker.shutdown()
    }
    @Test func removedJobsReleaseReviewStateWhileRemainingDraftAndUndoSurvive() async throws {
        _ = NSApplication.shared
        let f = try QueueInteractionFixture(); defer { f.remove() }; let store = WorkspaceStore(defaults: preferences.defaults)
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [f.script.path, f.jobs.path, f.cache.path])
            store.ready = true; try await store.refresh()
            let removed = try #require(store.jobs.first { $0.jobID == "b" })
            store.artworkSourceByJob["b"] = "local"
            weak var discarded = store.editorSession(field: "title", jobID: "b")
            let kept = store.editorSession(field: "title", jobID: "c")
            kept.configure(value: "Keep this draft", label: "Title", editable: true, multiline: false)
            kept.textView.undoManager?.beginUndoGrouping()
            kept.textView.insertText("!", replacementRange: NSRange(location: 15, length: 0))
            kept.textView.undoManager?.endUndoGrouping()
            store.drafts["c"] = ["title": "Keep this draft!"]
            store.remove(removed); try await idle(store)
            #expect(!store.jobs.contains { $0.jobID == "b" })
            #expect(store.jobLabel("b") == tr("unknownID") && store.artworkSourceByJob["b"] == nil)
            #expect(discarded == nil)
            #expect(store.drafts["c"]?["title"] == "Keep this draft!" && kept.textView.undoManager?.canUndo == true)
            kept.textView.undoManager?.undo()
            #expect(kept.textView.string == "Keep this draft")
            // Snapshot removal also occurs after confirmed-part saves; it must
            // retire the old editor rather than retain an invisible job forever.
            weak var retired = store.editorSession(field: "plot", jobID: "unknown")
            store.drafts["unknown"] = ["plot": "Retired synthetic draft"]
            store.artworkSourceByJob["unknown"] = "local"
            _ = try await store.worker.request("jobs.remove", params: .object(["jobIds": .array([.string("unknown")])]))
            try await store.refresh()
            #expect(retired == nil && store.drafts["unknown"] == nil && store.artworkSourceByJob["unknown"] == nil)
            #expect(store.jobLabel("unknown") == tr("unknownID"))
            #expect(store.drafts["c"]?["title"] == "Keep this draft!")
            #expect(try f.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func snapshotReconciliationPublishesOneCompleteListAndSkipsIdenticalJobs() async throws {
        _ = NSApplication.shared
        let fixture = try QueueInteractionFixture(); defer { fixture.remove() }
        let store = WorkspaceStore(defaults: preferences.defaults)
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [fixture.script.path, fixture.jobs.path, fixture.cache.path])
            var rows: [JSONValue] = (0..<1000).map { number in .object([
                "jobId": .string("bulk-\(number)"), "reviewRevision": .number(1),
                "metadata": .object(["id": .string("DEMO-\(10000 + number)"), "title": .string("合成标题 \(number)")]),
                "videoPaths": .array([.string(fixture.video.path)]), "artwork": .array([])
            ]) }
            _ = try await store.worker.request("fixture.installJobs", params: .object(["jobs": .array(rows)]))
            var publishedCounts: [Int] = []
            let updates = store.$jobs.dropFirst().sink { publishedCounts.append($0.count) }; defer { updates.cancel() }
            try await store.refresh()
            #expect(publishedCounts == [1000], "Subscribers must not receive a succession of partial queues.")
            publishedCounts = []
            try await store.refresh()
            #expect(publishedCounts.isEmpty)

            // Native orphan state still needs retirement even when the job array is identical.
            weak var orphan = store.editorSession(field: "plot", jobID: "local-orphan")
            store.drafts["local-orphan"] = ["plot": "Retire only this local draft"]
            store.artworkSourceByJob["local-orphan"] = "local"
            try await store.refresh()
            #expect(publishedCounts.isEmpty && orphan == nil)
            #expect(store.drafts["local-orphan"] == nil && store.artworkSourceByJob["local-orphan"] == nil)

            var changed = rows[500].objectValue
            changed["reviewRevision"] = .number(2)
            changed["metadata"] = .object(["id": .string("DEMO-10500"), "title": .string("Only this authoritative title changed")])
            rows[500] = .object(changed)
            _ = try await store.worker.request("fixture.installJobs", params: .object(["jobs": .array(rows)]))
            try await store.refresh()
            #expect(publishedCounts == [1000])
            #expect(store.jobs[500].metadata["title"].stringValue == "Only this authoritative title changed")
            #expect(store.jobs[500].reviewRevision == 2)
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func snapshotReorderAndRetirementKeepRemainingDraftUndoAndSelection() async throws {
        _ = NSApplication.shared
        let fixture = try QueueInteractionFixture(); defer { fixture.remove() }
        let store = WorkspaceStore(defaults: preferences.defaults)
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [fixture.script.path, fixture.jobs.path, fixture.cache.path])
            var rows = try JSONDecoder().decode([JSONValue].self, from: Data(contentsOf: fixture.jobs))
            var c = rows[2].objectValue
            c["artwork"] = .array([.object(["artworkId": .string("c-cover"), "source": .string("local"), "contentKey": .string("stable-content")])])
            rows[2] = .object(c)
            _ = try await store.worker.request("fixture.installJobs", params: .object(["jobs": .array(rows)]))
            store.ready = true; try await store.refresh(); store.selectNavigation(["c"])
            store.checkedJobIDs = ["b", "c"]
            store.artworkSourceByJob["c"] = "local"
            weak var retired = store.editorSession(field: "plot", jobID: "b")
            store.drafts["b"] = ["plot": "Authoritatively retired later"]
            let retained = store.editorSession(field: "title", jobID: "c")
            retained.configure(value: "Keep this draft", label: "Title", editable: true, multiline: false)
            retained.textView.undoManager?.beginUndoGrouping()
            retained.textView.insertText("!", replacementRange: NSRange(location: 15, length: 0))
            retained.textView.undoManager?.endUndoGrouping()
            store.drafts["c"] = ["title": "Keep this draft!"]
            var publications = 0
            let updates = store.$jobs.dropFirst().sink { _ in publications += 1 }; defer { updates.cancel() }

            let reordered = [rows[2], rows[0], rows[1], rows[3]]
            _ = try await store.worker.request("fixture.installJobs", params: .object(["jobs": .array(reordered)]))
            try await store.refresh()
            #expect(publications == 1 && store.jobs.map(\.jobID) == ["c", "a", "b", "unknown"])
            #expect(store.selectedID == "c" && store.navigationSelection == ["c"] && store.checkedJobIDs == ["b", "c"])
            #expect(store.editorSession(field: "title", jobID: "c") === retained && retained.textView.undoManager?.canUndo == true)
            #expect(store.drafts["c"]?["title"] == "Keep this draft!" && store.artworkSourceByJob["c"] == "local")
            #expect(store.jobLabel("c") == "DEMO-103 · Synthetic c")

            _ = try await store.worker.request("fixture.installJobs", params: .object(["jobs": .array([rows[2], rows[0]])]))
            try await store.refresh()
            #expect(publications == 2 && retired == nil && store.drafts["b"] == nil)
            #expect(store.selectedID == "c" && store.checkedJobIDs == ["c"])
            #expect(store.jobLabel("b") == tr("unknownID") && store.artworkSourceByJob["c"] == "local")
            #expect(store.drafts["c"]?["title"] == "Keep this draft!")
            retained.textView.undoManager?.undo()
            #expect(retained.textView.string == "Keep this draft")
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func equalSingleJobUpdatesAreSkippedButAdvisoryChangesStillPublish() async throws {
        _ = NSApplication.shared
        let fixture = try QueueInteractionFixture(); defer { fixture.remove() }
        let store = WorkspaceStore(defaults: preferences.defaults)
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [fixture.script.path, fixture.jobs.path, fixture.cache.path])
            try await store.refresh()
            let raw = try JSONDecoder().decode([JSONValue].self, from: Data(contentsOf: fixture.jobs))[0]
            var publications = 0
            let updates = store.$jobs.dropFirst().sink { _ in publications += 1 }; defer { updates.cancel() }
            store.update(raw)
            #expect(publications == 0)
            var changed = raw.objectValue
            changed["coverQuality"] = .object(["state": .string("lowResolution"), "width": .number(100), "height": .number(50)])
            changed["warnings"] = .array([.string("Synthetic advisory")])
            changed["hasFailedSources"] = .bool(true)
            changed["searchAttempts"] = .array([.object(["source": .string("libredmm"), "success": .bool(false)])])
            store.update(.object(changed))
            let job = try #require(store.jobs.first { $0.jobID == "a" })
            #expect(publications == 1 && job.reviewRevision == raw["reviewRevision"].intValue)
            #expect(store.hasLowResolutionCover(job) && job.warnings == ["Synthetic advisory"] && job.hasFailedSources)
            #expect(job.searchAttempts.count == 1)
            store.update(.object(changed))
            #expect(publications == 1)
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func menuRetryUsesRecordedFailedSourcesAfterChangingQueryMode() async throws {
        _ = NSApplication.shared
        let f = try QueueInteractionFixture(); defer { f.remove() }; let store = WorkspaceStore(defaults: preferences.defaults)
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [f.script.path, f.jobs.path, f.cache.path])
            store.ready = true; try await store.refresh(); store.selectNavigation(["b"]); store.checkedJobIDs = ["b"]
            store.sourceMode = "r18dev"
            #expect(store.canRetrySources)
            store.search(retry: true); try await idle(store)
            let state = try await store.worker.request("fixture.state")
            #expect(state["searches"].arrayValue.last?["sourceMode"].stringValue == "libredmm", "Retry the recorded failed source even when the new-query menu has changed.")
            #expect(state["searches"].arrayValue.last?["jobIds"].arrayValue == [.string("b")])
            #expect(store.sourceMode == "r18dev" && store.checkedJobIDs == ["b"])
            #expect(try f.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func completedNotificationsReleasePayloadsAndProgressIsBounded() {
        let store = WorkspaceStore(defaults: preferences.defaults)
        for i in 0..<200 {
            let payload = String(repeating: "X", count: 262144) + "-\(i)"
            store.receive(.object(["method": .string("operation.finished"), "params": .object([
                "operationId": .string("completed-\(i)"), "sequence": .number(5), "phase": .string("completed"),
                "result": .object(["syntheticPreview": .string(payload)])
            ])]))
        }
        #expect(store.operationProgressRetention.count == 0 && store.operationProgressRetention.bytes == 0)
        for i in 0..<300 {
            store.receive(.object(["method": .string("operation.progress"), "params": .object([
                "operationId": .string("early-\(i)"), "sequence": .number(1), "phase": .string("preparingPreview"),
                "details": .object(["currentIndex": .number(2), "totalJobs": .number(3), "ignoredPayload": .string(String(repeating: "Y", count: 262144))])
            ])]))
        }
        #expect(store.operationProgressRetention.count <= 128 && store.operationProgressRetention.bytes <= 64 * 1024)
        store.operationID = "current"
        store.receive(.object(["method": .string("operation.progress"), "params": .object([
            "operationId": .string("current"), "sequence": .number(4), "phase": .string("copyingmovie"),
            "details": .object(["currentIndex": .number(2), "totalJobs": .number(3)])
        ])]))
        #expect(store.phase == "copyingmovie" && store.currentJobIndex == 2 && store.totalOperationJobs == 3)
        store.receive(.object(["method": .string("worker.unavailable"), "params": .object([:])]))
        #expect(store.operationProgressRetention.count == 0 && store.operationProgressRetention.bytes == 0)
    }
    @Test func artworkPreviewLeaseSurvivesUnchangedRevisionAndReleasesAfterRemoval() async throws {
        _ = NSApplication.shared
        let fixture = try ArtworkLeasePipeFixture(mode: "ordinary"); defer { fixture.remove() }
        let store = fixture.makeStore(defaults: preferences.defaults)
        do {
            await store.start()
            let job = try #require(store.selectedJob), artwork = try #require(job.artwork.first)
            let key = store.artworkKey(artwork, jobID: job.jobID)
            store.loadArtwork(artwork, jobID: job.jobID)
            try await artworkCondition { store.artworkPaths[key] != nil }
            let adoptedURL = try #require(store.artworkPaths[key])
            var state = try await fixture.state(store.worker)
            let lease = try #require(state["previews"].arrayValue.first?["leaseId"].stringValue)
            #expect(UUID(uuidString: lease) != nil && state["activeLeases"].arrayValue == [.string(lease)])
            #expect(state["releases"].arrayValue.isEmpty && adoptedURL == fixture.preview.resolvingSymlinksInPath())
            #expect(FileManager.default.fileExists(atPath: adoptedURL.path))

            _ = try await store.worker.request("fixture.bumpRevision")
            try await store.refresh()
            let refreshed = try #require(store.selectedJob)
            #expect(refreshed.reviewRevision > job.reviewRevision)
            #expect(store.artworkPaths[key] == adoptedURL)
            store.loadArtwork(try #require(refreshed.artwork.first), jobID: refreshed.jobID)
            state = try await fixture.state(store.worker)
            #expect(state["previews"].arrayValue.count == 1 && state["releases"].arrayValue.isEmpty)
            #expect(state["activeLeases"].arrayValue == [.string(lease)], "A review revision change with identical artwork content must not revoke the displayed URL.")

            store.remove(refreshed)
            try await idle(store)
            state = try await fixture.waitForState(store.worker) { $0["releases"].arrayValue.contains(.string(lease)) }
            #expect(store.jobs.map(\.jobID) == ["other"] && store.artworkPaths[key] == nil)
            #expect(state["activeLeases"].arrayValue.isEmpty)
            #expect(state["previews"].arrayValue.count == 1)
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func historicalArtworkLRUReleasesOldLeasesAndReopensWithoutChangingDrafts() async throws {
        _ = NSApplication.shared
        let fixture = try ArtworkLeasePipeFixture(mode: "history"); defer { fixture.remove() }
        let store = WorkspaceStore(engineExecutable: { URL(fileURLWithPath: "/usr/bin/python3") },
            engineArguments: [fixture.script.path, fixture.directory.path], defaults: preferences.defaults,
            artworkHistoryMaximumCount: 2)
        do {
            await store.start()
            let jobs = store.jobs, first = try #require(jobs.first)
            store.drafts[first.jobID] = ["title": "Keep this unsent draft"]
            store.checkedJobIDs = [first.jobID]
            for job in jobs {
                store.selectNavigation([job.jobID])
                let artwork = try #require(job.artwork.first), reader = UUID()
                let key = store.artworkKey(artwork, jobID: job.jobID)
                store.beginArtworkRead(artwork, jobID: job.jobID, readerID: reader)
                try await artworkCondition { store.artworkPaths[key] != nil }
                #expect(store.artworkPreviewRetention.historical <= 2)
                store.endArtworkRead(reader)
            }
            var state = try await fixture.waitForState(store.worker) { $0["activeLeases"].arrayValue.count == 2 }
            #expect(store.artworkPreviewRetention.files == 2 && store.artworkPreviewRetention.historical == 2)
            #expect(state["previews"].arrayValue.count == 8 && state["releases"].arrayValue.count == 6)
            let firstArt = try #require(first.artwork.first), key = store.artworkKey(firstArt, jobID: first.jobID)
            #expect(store.artworkPaths[key] == nil)
            #expect(store.artworkDimensions[key] == CGSize(width: 32, height: 24) && store.artworkLowResolution.contains(key), "Eviction must retain reviewed dimensions and quality warnings.")
            let reader = UUID()
            store.beginArtworkRead(firstArt, jobID: first.jobID, readerID: reader)
            try await artworkCondition { store.artworkPaths[key] != nil }
            state = try await fixture.state(store.worker)
            #expect(state["previews"].arrayValue.count == 9)
            #expect(state["previews"].arrayValue.first?["leaseId"] != state["previews"].arrayValue.last?["leaseId"])
            #expect(store.drafts[first.jobID]?["title"] == "Keep this unsent draft" && store.checkedJobIDs == [first.jobID])
            store.endArtworkRead(reader)
            #expect(store.artworkPreviewRetention.historical == 2)
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func visibleReadersAndRetiredDecoderKeepExactLeaseAcrossSameURLReadoption() async throws {
        _ = NSApplication.shared
        let fixture = try ArtworkLeasePipeFixture(mode: "history"); defer { fixture.remove() }
        let store = WorkspaceStore(engineExecutable: { URL(fileURLWithPath: "/usr/bin/python3") },
            engineArguments: [fixture.script.path, fixture.directory.path], defaults: preferences.defaults,
            artworkHistoryMaximumCount: 0)
        do {
            await store.start()
            var job = try #require(store.jobs.first), artwork = try #require(job.artwork.first)
            let key = store.artworkKey(artwork, jobID: job.jobID), first = UUID(), second = UUID()
            store.beginArtworkRead(artwork, jobID: job.jobID, readerID: first)
            store.beginArtworkRead(artwork, jobID: job.jobID, readerID: second)
            try await artworkCondition { store.artworkPaths[key] != nil }
            let url = try #require(store.artworkPaths[key]), decoder = try #require(store.beginArtworkURLRead(url))
            let oldLease = try #require((try await fixture.state(store.worker))["activeLeases"].arrayValue.first)
            store.endArtworkRead(first)
            #expect(store.artworkPaths[key] == url && store.artworkPreviewRetention.readers == 1)
            store.endArtworkRead(second)
            #expect(store.artworkPaths[key] == url && store.artworkPreviewRetention.decoders == 1)
            _ = try await store.worker.request("fixture.bumpContent")
            try await store.refresh()
            #expect(store.artworkPaths[key] == nil && store.artworkPreviewRetention.retiredLeases == 1)
            #expect((try await fixture.state(store.worker))["activeLeases"].arrayValue.contains(oldLease))
            job = try #require(store.jobs.first); artwork = try #require(job.artwork.first)
            let replacementReader = UUID()
            store.beginArtworkRead(artwork, jobID: job.jobID, readerID: replacementReader)
            try await artworkCondition { store.artworkPaths[key] != nil }
            #expect(store.artworkPaths[key] == url, "The fake engine intentionally reuses a URL to exercise exact retired lease IDs.")
            let newLease = try #require((try await fixture.state(store.worker))["previews"].arrayValue.last?["leaseId"])
            #expect(newLease != oldLease)
            store.endArtworkURLRead(decoder)
            let state = try await fixture.waitForState(store.worker) { !$0["activeLeases"].arrayValue.contains(oldLease) }
            #expect(state["activeLeases"].arrayValue == [newLease])
            #expect(store.artworkPreviewRetention.retiredLeases == 0 && store.artworkPaths[key] == url)
            store.endArtworkRead(replacementReader)
            _ = try await fixture.waitForState(store.worker) { $0["activeLeases"].arrayValue.isEmpty }
            #expect(store.artworkPaths.isEmpty && store.artworkPreviewRetention.readers == 0 && store.artworkPreviewRetention.decoders == 0)
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func closingViewerAndNavigatingDoNotCancelAnotherVisibleReadersSharedRequest() async throws {
        _ = NSApplication.shared
        let fixture = try ArtworkLeasePipeFixture(mode: "hold-result"); defer { fixture.remove() }
        let store = fixture.makeStore(defaults: preferences.defaults)
        do {
            await store.start()
            let job = try #require(store.jobs.first), artwork = try #require(job.artwork.first), queueReader = UUID()
            let key = store.artworkKey(artwork, jobID: job.jobID)
            store.openArtwork(artwork, job: job)
            store.beginArtworkRead(artwork, jobID: job.jobID, readerID: queueReader, scope: "queue")
            _ = try await fixture.waitForState(store.worker) { $0["pendingResult"].boolValue }
            store.closeArtworkViewer(); store.showingImage = false
            store.selectNavigation(["other"])
            var state = try await fixture.state(store.worker)
            #expect(state["cancellations"].arrayValue.isEmpty && state["releases"].arrayValue.isEmpty)
            #expect(state["activeLeases"].arrayValue.count == 1 && store.artworkPreviewRetention.readers == 1)
            _ = try await store.worker.request("fixture.complete")
            try await artworkCondition { store.artworkPaths[key] != nil }
            state = try await fixture.state(store.worker)
            #expect(state["previews"].arrayValue.count == 1 && state["cancellations"].arrayValue.isEmpty)
            store.endArtworkRead(queueReader)
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func artworkHistoryByteBudgetEvictsPayloadButKeepsQualityMetadata() async throws {
        _ = NSApplication.shared
        let fixture = try ArtworkLeasePipeFixture(mode: "history"); defer { fixture.remove() }
        let store = WorkspaceStore(engineExecutable: { URL(fileURLWithPath: "/usr/bin/python3") },
            engineArguments: [fixture.script.path, fixture.directory.path], defaults: preferences.defaults,
            artworkHistoryMaximumCount: 100, artworkHistoryMaximumBytes: 1)
        do {
            await store.start()
            let job = try #require(store.jobs.first), artwork = try #require(job.artwork.first), reader = UUID()
            let key = store.artworkKey(artwork, jobID: job.jobID)
            store.beginArtworkRead(artwork, jobID: job.jobID, readerID: reader)
            try await artworkCondition { store.artworkPaths[key] != nil }
            #expect(store.artworkPreviewRetention.historical == 0, "Visible files do not count against history budget.")
            store.endArtworkRead(reader)
            #expect(store.artworkPaths[key] == nil && store.artworkPreviewRetention.historyBytes == 0)
            #expect(store.artworkDimensions[key] == CGSize(width: 32, height: 24) && store.artworkLowResolution.contains(key))
            _ = try await fixture.waitForState(store.worker) { $0["activeLeases"].arrayValue.isEmpty }
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_SEARCH_TEST_HOST"] != nil))
    func historicalPreviewReclamationPrecedesAdmissionWithRealTwoFileCache() async throws {
        _ = NSApplication.shared
        let executable = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_SEARCH_TEST_HOST"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JML bounded history \(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let config = directory.appendingPathComponent("config.json")
        try Data(#"{"artworkCacheLimits":{"maximumBytes":1048576,"maximumFiles":2,"maximumLeases":16}}"#.utf8).write(to: config)
        var originals: [URL: Data] = [:], videos: [String] = []
        for index in 0..<8 {
            let movie = directory.appendingPathComponent("DEMO-\(700 + index)")
            try FileManager.default.createDirectory(at: movie, withIntermediateDirectories: true)
            let video = movie.appendingPathComponent("DEMO-\(700 + index).mp4"), image = movie.appendingPathComponent("poster.png")
            let bytes = Data("Synthetic protected source \(index)".utf8)
            let bitmap = try #require(NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 32 + index, pixelsHigh: 24, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: (32 + index) * 4, bitsPerPixel: 32))
            memset(bitmap.bitmapData, Int32(60 + index), bitmap.bytesPerRow * bitmap.pixelsHigh)
            let png = try #require(bitmap.representation(using: .png, properties: [:]))
            try bytes.write(to: video); try png.write(to: image)
            originals[video] = bytes; originals[image] = png; videos.append(video.path)
        }
        let store = WorkspaceStore(engineExecutable: { URL(fileURLWithPath: executable) },
            engineArguments: ["--data-root", directory.appendingPathComponent("state").path, "--fixture-config", config.path],
            defaults: preferences.defaults, artworkHistoryMaximumCount: 1)
        do {
            await store.start()
            let found = try await store.waitOperation(try await store.worker.request("workspace.discoverInputs", params: .object(["paths": .array(videos.map(JSONValue.string)), "recursive": .bool(false)])))
            _ = try await store.waitOperation(try await store.worker.request("workspace.importDiscovery", params: .object(["discoveryId": found["discoveryId"], "selectedItemIds": .array(found["items"].arrayValue.map { $0["itemId"] })])))
            try await store.refresh()
            #expect(store.jobs.count == 8)
            for job in store.jobs {
                let art = try #require(job.artwork.first), reader = UUID(), key = store.artworkKey(art, jobID: job.jobID)
                store.beginArtworkRead(art, jobID: job.jobID, readerID: reader)
                try await artworkCondition { store.artworkPaths[key] != nil || store.artworkErrors[key] != nil }
                #expect(store.artworkPaths[key] != nil && store.artworkErrors[key] == nil, "New admission must wait for prior history release, never fail capacity.")
                store.endArtworkRead(reader)
            }
            #expect(store.artworkPreviewRetention.files == 1)
            var state = try await store.worker.request("workspace.getSnapshot")
            for _ in 0..<100 where state["artworkCache"]["leases"].intValue > 1 {
                try await Task.sleep(for: .milliseconds(5)); state = try await store.worker.request("workspace.getSnapshot")
            }
            #expect(state["artworkCache"]["leases"].intValue == 1 && state["artworkCache"]["files"].intValue <= 2)
            for (url, bytes) in originals { #expect(try Data(contentsOf: url) == bytes) }
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func canceledArtworkPreviewReleasesBeforeLateResultAndNeverAdoptsIt() async throws {
        _ = NSApplication.shared
        let fixture = try ArtworkLeasePipeFixture(mode: "hold-result"); defer { fixture.remove() }
        let store = fixture.makeStore(defaults: preferences.defaults)
        do {
            await store.start()
            let job = try #require(store.selectedJob), artwork = try #require(job.artwork.first)
            let key = store.artworkKey(artwork, jobID: job.jobID)
            store.loadArtwork(artwork, jobID: job.jobID, scope: "detail")
            var state = try await fixture.waitForState(store.worker) { $0["pendingResult"].boolValue }
            let lease = try #require(state["previews"].arrayValue.first?["leaseId"].stringValue)
            #expect(state["activeLeases"].arrayValue == [.string(lease)] && store.artworkPaths[key] == nil)
            store.selectNavigation(["other"])
            state = try await fixture.waitForState(store.worker) { $0["releases"].arrayValue.contains(.string(lease)) }
            #expect(state["pendingResult"].boolValue && state["activeLeases"].arrayValue.isEmpty, "Cancellation must release the known lease before the held response arrives.")
            #expect(store.artworkPaths[key] == nil)
            _ = try await store.worker.request("fixture.complete")
            state = try await fixture.waitForState(store.worker) { $0["releases"].arrayValue.filter { $0 == .string(lease) }.count >= 2 }
            #expect(state["completed"].boolValue && !state["pendingResult"].boolValue)
            #expect(state["activeLeases"].arrayValue.isEmpty && !state["cancellations"].arrayValue.isEmpty)
            #expect(store.selectedID == "other" && store.artworkPaths[key] == nil && store.artworkErrors[key] == nil)
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func missingArtworkPayloadRequestsNewLeaseAndRebuildsTheSameURL() async throws {
        _ = NSApplication.shared
        let fixture = try ArtworkLeasePipeFixture(mode: "ordinary"); defer { fixture.remove() }
        let store = fixture.makeStore(defaults: preferences.defaults)
        do {
            await store.start()
            let job = try #require(store.selectedJob), artwork = try #require(job.artwork.first)
            let key = store.artworkKey(artwork, jobID: job.jobID)
            store.loadArtwork(artwork, jobID: job.jobID)
            try await artworkCondition { store.artworkPaths[key] != nil }
            let originalURL = try #require(store.artworkPaths[key])
            let originalPayload = try Data(contentsOf: originalURL)
            var state = try await fixture.state(store.worker)
            let originalLease = try #require(state["previews"].arrayValue.first?["leaseId"].stringValue)
            #expect(state["activeLeases"].arrayValue == [.string(originalLease)])

            // Simulate system/user cleanup of generated cache only. The registered
            // source artwork, video and NFO must survive this recovery unchanged.
            #expect(originalURL == fixture.preview.resolvingSymlinksInPath())
            try FileManager.default.removeItem(at: originalURL)
            #expect(!FileManager.default.fileExists(atPath: originalURL.path))
            store.loadArtwork(artwork, jobID: job.jobID)
            #expect(store.artworkPaths[key] == nil && store.artworkDimensions[key] == nil)
            try await artworkCondition { store.artworkPaths[key] != nil }
            state = try await fixture.waitForState(store.worker) {
                $0["previews"].arrayValue.count == 2 && $0["releases"].arrayValue.contains(.string(originalLease))
            }
            let renewedLease = try #require(state["previews"].arrayValue.last?["leaseId"].stringValue)
            #expect(UUID(uuidString: renewedLease) != nil && renewedLease != originalLease)
            #expect(state["activeLeases"].arrayValue == [.string(renewedLease)])
            #expect(store.artworkPaths[key] == originalURL && store.artworkErrors[key] == nil)
            #expect(store.artworkDimensions[key] == CGSize(width: 32, height: 24))
            #expect(try Data(contentsOf: originalURL) == originalPayload)
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func artworkPreviewWithWrongLeaseEchoIsRejectedAndReleased() async throws {
        _ = NSApplication.shared
        let fixture = try ArtworkLeasePipeFixture(mode: "wrong-lease"); defer { fixture.remove() }
        let store = fixture.makeStore(defaults: preferences.defaults)
        do {
            await store.start()
            let job = try #require(store.selectedJob), artwork = try #require(job.artwork.first)
            let key = store.artworkKey(artwork, jobID: job.jobID)
            store.loadArtwork(artwork, jobID: job.jobID)
            let state = try await fixture.waitForState(store.worker) { !$0["releases"].arrayValue.isEmpty }
            let lease = try #require(state["previews"].arrayValue.first?["leaseId"].stringValue)
            #expect(UUID(uuidString: lease) != nil)
            #expect(state["releases"].arrayValue.contains(.string(lease)) && state["activeLeases"].arrayValue.isEmpty)
            #expect(store.artworkPaths[key] == nil && store.artworkErrors[key] != nil)
            #expect(try fixture.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    private func artworkCondition(_ predicate: () -> Bool) async throws {
        for _ in 0..<400 {
            if predicate() { return }
            try await Task.sleep(for: .milliseconds(5))
        }
        throw RPCFailure(code: -1, message: "Synthetic artwork did not reach the expected state.")
    }
    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil))
    func previewConfirmsSidecarReplacementAndQuickSaveCannotBypassVideoConflicts() async throws {
        _ = NSApplication.shared
        let old = preferences.defaults.object(forKey: "skipSavePreview")
        defer { if let old { preferences.defaults.set(old, forKey: "skipSavePreview") } else { preferences.defaults.removeObject(forKey: "skipSavePreview") } }
        let path = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JML quick-save 合成 \(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let video = directory.appendingPathComponent("DEMO-991.mp4"), bytes = Data("Synthetic unchanged video".utf8)
        try bytes.write(to: video)
        let store = WorkspaceStore(defaults: preferences.defaults)
        do {
            let initialized = try await store.worker.start(executable: URL(fileURLWithPath: path), arguments: ["--data-root", directory.appendingPathComponent("state").path])
            store.ready = true; store.capabilities = initialized["capabilities"]
            _ = try await store.waitOperation(try await store.worker.request("workspace.importInputs", params: .object(["paths": .array([.string(video.path)]), "recursive": .bool(false)])))
            try await store.refresh()
            var job = try #require(store.selectedJob)
            store.checkedJobIDs = [job.jobID]
            store.edit("title", value: "Saved through a checked plan", job: job)
            store.skipSavePreview = false; store.saveActionTargets(); try await idle(store)
            #expect(store.preview?.canExecute == true && store.savedOutputs.isEmpty)
            #expect(!FileManager.default.fileExists(atPath: directory.appendingPathComponent("DEMO-991.nfo").path))
            store.preview = nil
            store.skipSavePreview = true; store.saveActionTargets(); try await idle(store)
            #expect(store.preview == nil && !store.operationHadProblems)
            let nfo = try #require(store.savedOutputs.first { URL(fileURLWithPath: $0).pathExtension == "nfo" })
            let saved = try Data(contentsOf: URL(fileURLWithPath: nfo))
            #expect(String(decoding: saved, as: UTF8.self).contains("Saved through a checked plan"))
            #expect(try Data(contentsOf: video) == bytes)
            job = try #require(store.selectedJob)
            store.edit("title", value: "Reviewed replacement", job: job)
            store.skipSavePreview = false; store.saveActionTargets(); try await idle(store)
            #expect(store.preview?.canExecute == true, "The preview itself is the replacement confirmation; no extra checkbox is required.")
            #expect(try Data(contentsOf: URL(fileURLWithPath: nfo)) == saved)
            store.preview = nil // Cancel keeps edits and leaves disk untouched.
            store.saveActionTargets(); try await idle(store)
            store.save(); store.previewDidDismiss(); try await idle(store)
            #expect(try String(contentsOfFile: nfo, encoding: .utf8).contains("Reviewed replacement"))
            job = try #require(store.selectedJob)
            store.edit("title", value: "Explicit quick replacement", job: job)
            store.skipSavePreview = true; store.saveActionTargets(); try await idle(store)
            #expect(store.preview == nil && !store.operationHadProblems)
            let quickSaved = try Data(contentsOf: URL(fileURLWithPath: nfo))
            #expect(String(decoding: quickSaved, as: UTF8.self).contains("Explicit quick replacement"))
            let root = directory.appendingPathComponent("destination"), existing = root.appendingPathComponent("DEMO-991/DEMO-991.mp4")
            try FileManager.default.createDirectory(at: existing.deletingLastPathComponent(), withIntermediateDirectories: true)
            let targetBytes = Data("Other synthetic video must never be overwritten".utf8); try targetBytes.write(to: existing)
            store.targetMode = "CustomRootNumberFolder"; store.customRootDirectory = root.path; store.renameVideo = true
            store.saveActionTargets(); try await idle(store)
            #expect(store.preview?.canExecute == false, "Skip-preview cannot bypass a real video collision.")
            #expect(try Data(contentsOf: URL(fileURLWithPath: nfo)) == quickSaved)
            #expect(try Data(contentsOf: existing) == targetBytes)
            #expect(try Data(contentsOf: video) == bytes)
            store.preview = nil
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil))
    func sampleSelectionAndSnapshotRefreshReuseLoadedPreviewsAndLocalSourceRestoresFields() async throws {
        _ = NSApplication.shared
        let executable = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JML stable artwork \(UUID())")
        try FileManager.default.createDirectory(at: directory.appendingPathComponent("extrafanart"), withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let video = directory.appendingPathComponent("DEMO-994.mp4"), nfo = directory.appendingPathComponent("movie.nfo")
        try Data("Synthetic original video".utf8).write(to: video)
        let xml = "<movie><id>DEMO-994</id><title>Local source title</title><customNode keep=\"yes\">untouched</customNode></movie>"
        try Data(xml.utf8).write(to: nfo)
        let bitmap = try #require(NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 32, pixelsHigh: 24, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 128, bitsPerPixel: 32))
        memset(bitmap.bitmapData, 127, bitmap.bytesPerRow * bitmap.pixelsHigh)
        let png = try #require(bitmap.representation(using: .png, properties: [:]))
        for name in ["poster.png", "fanart.png", "extrafanart/fanart1.png"] { try png.write(to: directory.appendingPathComponent(name)) }
        let store = WorkspaceStore(defaults: preferences.defaults)
        do {
            let initialized = try await store.worker.start(executable: URL(fileURLWithPath: executable), arguments: ["--data-root", directory.appendingPathComponent("state").path])
            store.ready = true; store.capabilities = initialized["capabilities"]
            _ = try await store.waitOperation(try await store.worker.request("workspace.importInputs", params: .object(["paths": .array([.string(video.path)]), "recursive": .bool(false)])))
            try await store.refresh()
            var job = try #require(store.selectedJob)
            store.checkedJobIDs = [job.jobID]
            #expect(job.reviewSources == ["local"])
            for art in job.artwork { store.loadArtwork(art, jobID: job.jobID) }
            for _ in 0..<300 where store.artworkPaths.count < job.artwork.count { try await Task.sleep(for: .milliseconds(10)) }
            #expect(store.artworkPaths.count == 3)
            let paths = store.artworkPaths, dimensions = store.artworkDimensions
            let sample = try #require(job.artwork.first { $0["kind"].stringValue == "sample" })
            let oldRevision = job.reviewRevision
            var busyTransitions: [Bool] = []
            let busy = store.$working.dropFirst().sink { busyTransitions.append($0) }
            store.selectArtworkSource("local", jobID: job.jobID)
            #expect(!store.working && store.hasPendingArtworkSelections)
            // An immediate All Images choice must survive the older source ACK.
            store.selectArtworkSource("", jobID: job.jobID)
            try await idle(store)
            #expect(store.artworkSource(try #require(store.selectedJob)).isEmpty)
            #expect(store.artworkPaths == paths && busyTransitions.isEmpty)
            job = try #require(store.selectedJob)
            store.selectArtwork(sample, jobID: job.jobID, selected: !sample["selected"].boolValue)
            #expect(!store.working && store.hasPendingArtworkSelections)
            #expect(store.artworkSelected(sample, jobID: job.jobID) == !sample["selected"].boolValue)
            try await idle(store)
            #expect(busyTransitions.isEmpty, "A sample checkbox must not disable and flash the entire workspace.")
            for n in 0..<12 { store.selectArtwork(sample, jobID: job.jobID, selected: n % 2 == 0) }
            #expect(!store.artworkSelected(sample, jobID: job.jobID))
            try await idle(store)
            #expect(store.selectedJob?.artwork.first(where: { $0["kind"].stringValue == "sample" })?["selected"].boolValue == false)
            #expect(store.errorMessage == nil && busyTransitions.isEmpty)
            busy.cancel()
            // A save requested immediately after a queued toggle waits for its
            // acknowledgement, and previews the latest revision.
            store.selectArtwork(sample, jobID: job.jobID, selected: true)
            store.saveActionTargets(); try await idle(store)
            #expect(store.preview != nil && store.errorMessage == nil)
            store.preview = nil
            job = try #require(store.selectedJob)
            #expect(job.reviewRevision > oldRevision)
            #expect(store.artworkPaths == paths && store.artworkDimensions == dimensions)
            try await store.refresh()
            #expect(store.artworkPaths == paths && store.artworkDimensions == dimensions)
            store.edit("title", value: "Recoverable manual edit", job: job)
            #expect(store.reviewSource(job).isEmpty)
            store.selectReviewSource("local", jobID: job.jobID); try await idle(store)
            job = try #require(store.selectedJob)
            #expect(job.metadata["title"].stringValue == "Local source title")
            #expect(job.candidates.contains { $0["source"].stringValue == "manual" && $0["value"].stringValue == "Recoverable manual edit" })
            #expect(store.artworkPaths == paths)
            #expect(try String(contentsOf: nfo, encoding: .utf8) == xml)
            #expect(try String(contentsOf: video, encoding: .utf8) == "Synthetic original video")
            // A rejected checkbox must not be silently followed by a save.
            _ = try await store.worker.request("jobs.applyEdits", params: .object([
                "jobId": .string(job.jobID), "expectedReviewRevision": .number(Double(job.reviewRevision)),
                "edits": .object(["title": .string("A newer review outside the stale UI")])
            ]))
            store.selectArtwork(sample, jobID: job.jobID, selected: false)
            store.saveActionTargets(); try await idle(store)
            #expect(store.preview == nil && store.errorMessage != nil)
            #expect(try String(contentsOf: nfo, encoding: .utf8) == xml)
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }

    @Test func checkAllAndOverviewNeverChangeTheViewedMovieOrDrafts() {
        _ = NSApplication.shared
        let store = WorkspaceStore(defaults: preferences.defaults)
        store.jobs = ["a", "b", "c"].map { MovieJobSnapshot(.object(["jobId": .string($0), "reviewRevision": .number(9), "metadata": .object(["id": .string("DEMO-" + $0), "title": .string("Title " + $0)])])) }
        store.ready = true; store.capabilities = .object(["batchSave": .bool(true)])
        store.selectNavigation(["b"]); store.drafts["b"] = ["title": "Unsaved current title"]
        let drafts = store.drafts
        store.searchText = "Title a"
        #expect(store.visibleJobs.count == 1)
        #expect(!store.canSearchActionTargets && !store.canPreviewActionTargets)
        store.searchActionTargets(); store.saveActionTargets()
        #expect(!store.working && store.preview == nil)
        store.toggleAllChecks()
        #expect(store.allMoviesChecked && store.checkedJobIDs == ["a", "b", "c"])
        #expect(store.selectedID == "b" && store.drafts == drafts)
        store.showBatchOverview()
        #expect(store.showingBatchOverview && store.selectedID == "b")
        store.selectNavigation(["c"])
        #expect(!store.showingBatchOverview && store.checkedJobIDs == ["a", "b", "c"])
        store.toggleChecked("b")
        #expect(store.actionTargetJobs.map(\.jobID) == ["a", "c"] && store.selectedID == "c")
        store.toggleAllChecks(); store.toggleAllChecks()
        #expect(store.checkedJobIDs.isEmpty && !store.canPreviewActionTargets && store.drafts == drafts)
        store.working = true; store.toggleAllChecks(); store.toggleChecked("a"); store.toggleQueueNavigationChecks()
        #expect(store.checkedJobIDs.isEmpty)
        #expect(store.jobs.map(\.reviewRevision) == [9, 9, 9])
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil))
    func checkedBatchSaveExcludesViewedThirdMovieAndKeepsItsDraftAndNfo() async throws {
        _ = NSApplication.shared
        let executable = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JML checked batch \(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        var videos: [URL] = [], nfos: [URL] = [], originals: [Data] = []
        for i in 1...3 {
            let folder = directory.appendingPathComponent("DEMO-99\(i)")
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let video = folder.appendingPathComponent("DEMO-99\(i).mp4"), nfo = folder.appendingPathComponent("movie.nfo")
            try Data("Unchanged synthetic video \(i)".utf8).write(to: video)
            let xml = Data("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie custom=\"kept\"><id>DEMO-99\(i)</id><title>Original \(i)</title><customNode>untouched</customNode><uniqueid type=\"javnumber\" default=\"true\">DEMO-99\(i)</uniqueid></movie>".utf8)
            try xml.write(to: nfo); videos.append(video); nfos.append(nfo); originals.append(xml)
        }
        let oldSkip = preferences.defaults.object(forKey: "skipSavePreview")
        defer { if let oldSkip { preferences.defaults.set(oldSkip, forKey: "skipSavePreview") } else { preferences.defaults.removeObject(forKey: "skipSavePreview") } }
        let store = WorkspaceStore(defaults: preferences.defaults); store.skipSavePreview = false; store.includeIDInTitle = false
        do {
            let initResult = try await store.worker.start(executable: URL(fileURLWithPath: executable), arguments: ["--data-root", directory.appendingPathComponent("state").path])
            store.ready = true; store.capabilities = initResult["capabilities"]
            _ = try await store.waitOperation(try await store.worker.request("workspace.importInputs", params: .object(["paths": .array(videos.map { .string($0.path) }), "recursive": .bool(false)])))
            try await store.refresh()
            let jobs = store.jobs, third = try #require(jobs.last)
            for (i, job) in jobs.enumerated() { store.edit("title", value: i < 2 ? "Checked \(i+1)" : "Unmarked draft stays", job: job) }
            store.checkedJobIDs = Set(jobs.prefix(2).map(\.jobID)); store.selectNavigation([third.jobID])
            store.saveActionTargets(); try await idle(store)
            #expect(store.preview?.jobIDs == Array(jobs.prefix(2).map(\.jobID)))
            #expect(store.preview?.canExecute == true)
            for i in 0..<3 { #expect(try Data(contentsOf: nfos[i]) == originals[i]) }
            store.preview = nil
            store.saveActionTargets(); try await idle(store)
            #expect(store.preview?.canExecute == true && store.preview?.jobIDs.count == 2)
            store.save(); store.previewDidDismiss(); try await idle(store)
            #expect(store.operationResults.count == 2 && !store.operationHadProblems)
            for i in 0..<2 {
                let expected = String(decoding: originals[i], as: UTF8.self).replacingOccurrences(of: "<title>Original \(i+1)</title>", with: "<title>Checked \(i+1)</title>")
                let actual = try String(contentsOf: nfos[i], encoding: .utf8)
                #expect(actual == expected)
            }
            #expect(try Data(contentsOf: nfos[2]) == originals[2])
            #expect(store.selectedID == third.jobID && store.drafts[third.jobID]?["title"] == "Unmarked draft stays")
            #expect(store.jobs.last?.reviewRevision == third.reviewRevision)
            for i in 0..<3 { #expect(try String(contentsOf: videos[i], encoding: .utf8) == "Unchanged synthetic video \(i+1)") }
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }

    private func idle(_ store: WorkspaceStore) async throws {
        await store.waitForArtworkSelections()
        for _ in 0..<400 where store.working { try await Task.sleep(for: .milliseconds(5)) }
        #expect(!store.working)
    }
    @Test func nativeSheetLifecyclePublishesActionRecoveryAndForwardsTheOriginalDelegate() async throws {
        _ = NSApplication.shared
        let store = WorkspaceStore(defaults: preferences.defaults)
        store.jobs = [MovieJobSnapshot(.object(["jobId": .string("review"), "reviewRevision": .number(17), "hasFailedSources": .bool(true), "metadata": .object(["id": .string("DEMO-001")])]))]
        store.ready = true; store.capabilities = .object(["batchSave": .bool(true), "manualBrowserImport": .bool(true), "discoverInputs": .bool(true)])
        store.selectNavigation(["review"]); store.checkedJobIDs = ["review"]; store.drafts["review"] = ["title": "Keep the current review"]
        let window = NSWindow(contentRect: NSRect(x: -10000, y: -10000, width: 420, height: 300), styleMask: [.titled], backing: .buffered, defer: false)
        let sheet = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 240, height: 100), styleMask: [.titled], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false; sheet.isReleasedWhenClosed = false
        let previous = SheetLifecycleDelegate(), coordinator = WindowBridge.Coordinator(store: store)
        coordinator.previousDelegate = previous; window.delegate = coordinator; store.workspaceWindow = window
        var transitions: [Bool] = []
        let subscription = store.$workspaceSheetPresented.dropFirst().sink { transitions.append($0) }
        defer {
            subscription.cancel()
            if window.attachedSheet != nil { window.endSheet(sheet) }
            window.delegate = nil; sheet.orderOut(nil); window.orderOut(nil); sheet.close(); window.close()
        }
        #expect(store.canImportFiles && store.canRetrySources && store.canSearchActionTargets && store.canPreviewActionTargets && store.canClearQueue && store.canOpenBrowser)
        var completed = false
        window.beginSheet(sheet) { _ in completed = true }
        for _ in 0..<100 where !store.workspaceSheetPresented { try await Task.sleep(for: .milliseconds(10)) }
        #expect(window.attachedSheet === sheet && store.workspaceSheetPresented)
        #expect(!store.canImportFiles && !store.canRetrySources && !store.canSearchActionTargets && !store.canPreviewActionTargets && !store.canClearQueue && !store.canOpenBrowser)
        window.endSheet(sheet)
        for _ in 0..<200 where store.workspaceSheetPresented || !completed { try await Task.sleep(for: .milliseconds(10)) }
        #expect(completed && !store.workspaceSheetPresented)
        #expect(transitions == [true, false] && previous.events == ["begin", "end"])
        #expect(store.canImportFiles && store.canRetrySources && store.canSearchActionTargets && store.canPreviewActionTargets && store.canClearQueue && store.canOpenBrowser, "Ending the actual AppKit sheet restores actions without another navigation or model edit.")
        #expect(store.selectedID == "review" && store.jobs.first?.reviewRevision == 17 && store.drafts["review"]?["title"] == "Keep the current review")
    }
    @Test func inspectorStartsOpenAndActionScopesAreExplicit() {
        _ = NSApplication.shared
        let old = preferences.defaults.object(forKey: "inspectorVisible")
        preferences.defaults.set(false, forKey: "inspectorVisible")
        defer { if let old { preferences.defaults.set(old, forKey: "inspectorVisible") } else { preferences.defaults.removeObject(forKey: "inspectorVisible") } }
        let store = WorkspaceStore(defaults: preferences.defaults)
        #expect(store.inspectorVisible)
        store.jobs = ["a", "b", "c", "unknown"].map { id in MovieJobSnapshot(.object(["jobId": .string(id), "reviewRevision": .number(7), "hasFailedSources": .bool(true), "metadata": .object(["id": .string(id == "unknown" ? "" : "DEMO-101")])])) }
        store.ready = true; store.capabilities = .object(["batchSave": .bool(true), "manualBrowserImport": .bool(true), "discoverInputs": .bool(true)])
        store.selectNavigation(["a"])
        #expect(store.canOpenBrowser)
        #expect(store.actionTargetScope == "checked" && store.actionTargetJobs.isEmpty)
        #expect(!store.canSearchActionTargets && !store.canPreviewActionTargets)
        store.checkedJobIDs = ["c"]
        #expect(store.actionTargetScope == "checked" && store.actionTargetJobs.map(\.jobID) == ["c"])
        store.selectNavigation(["a", "b"])
        #expect(store.canOpenBrowser && store.navigationSelection.count == 1)
        #expect(store.actionTargetScope == "checked" && store.actionTargetJobs.map(\.jobID) == ["c"])
        #expect(store.canSearchActionTargets && store.canPreviewActionTargets)
        store.checkedJobIDs = []; store.selectNavigation(["unknown"])
        #expect(!store.canSearchActionTargets && !store.canOpenBrowser)
        store.drafts["unknown"] = ["id": "DEMO-104"]
        #expect(!store.canSearchActionTargets && store.recognizedSearchJobs.count == 4 && store.canOpenBrowser)
        store.checkedJobIDs = ["unknown"]
        #expect(store.canSearchActionTargets)
        #expect(store.canImportFiles && store.canRetrySources)
        store.statusUnconfirmed = true
        #expect(!store.canSearchActionTargets && !store.canPreviewActionTargets && !store.canSearchAllRecognized && !store.canClearQueue)
        store.statusUnconfirmed = false; store.preview = SavePreview(.object(["planId": .string("open-review")]))
        #expect(!store.canClearQueue && !store.canSearchActionTargets && !store.canOpenBrowser && !store.canImportFiles && !store.canRetrySources)
        #expect(store.jobs.map(\.reviewRevision) == [7, 7, 7, 7])
    }
    @Test func pendingNavigationAndBatchSearchKeepTheirTargetsAndRevisions() async throws {
        _ = NSApplication.shared
        let f = try QueueInteractionFixture(); defer { f.remove() }; let store = WorkspaceStore(defaults: preferences.defaults)
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [f.script.path, f.jobs.path, f.cache.path])
            store.ready = true; store.capabilities = .object(["manualBrowserImport": .bool(true), "discoverInputs": .bool(true)])
            try await store.refresh(); store.selectNavigation(["a"])
            store.detailTab = "artwork"; store.artworkSourceByJob["b"] = "local"
            store.checkedJobIDs = ["b"]
            store.selectQueueNavigation(["b"]); store.searchActionTargets(); try await idle(store)
            #expect(store.detailTab == "metadata" && store.artworkSourceByJob["b"] == nil)
            var state = try await store.worker.request("fixture.state")
            #expect(state["searches"].arrayValue.last?["jobIds"].arrayValue == [.string("b")])
            store.checkedJobIDs = ["c"]; store.selectNavigation(["b", "a"])
            store.searchActionTargets(); try await idle(store)
            state = try await store.worker.request("fixture.state")
            #expect(state["searches"].arrayValue.last?["jobIds"].arrayValue == [.string("c")])
            #expect(store.checkedJobIDs == ["c"])
            store.showingResults = true
            store.openBrowser()
            #expect(store.browser == nil && !store.working, "An open result sheet cannot silently replace its review.")
            store.showingResults = false; store.selectNavigation(["b"]); store.checkedJobIDs = ["b"]
            #expect(store.canImportFiles && store.canRetrySources)
            let browsing = BrowserSession(jobID: "b", response: .object(["targets": .array([]), "maximumHtmlBytes": .number(1024)]), store: store)
            store.browser = browsing
            store.openBrowser()
            store.chooseFiles(); await store.importPaths([f.video.path]); store.search(retry: true)
            #expect(!store.canImportFiles && !store.canRetrySources)
            #expect(store.browser === browsing && !store.working, "A shortcut cannot replace an existing browser review session.")
            store.browser = nil
            store.preview = SavePreview(.object(["planId": .string("reviewing-save")]))
            store.chooseFiles(); await store.importPaths([f.video.path]); store.search(retry: true)
            #expect(!store.working && store.preview?.planID == "reviewing-save", "Direct commands must preserve an open save review and its revision.")
            store.preview = nil
            state = try await store.worker.request("fixture.state")
            #expect(!state["methods"].arrayValue.contains(.string("browser.getTargets")))
            #expect(!state["methods"].arrayValue.contains(.string("workspace.discoverInputs")) && !state["methods"].arrayValue.contains(.string("jobs.retryFailedSources")))
            #expect(store.jobs.map(\.reviewRevision) == [11, 12, 13, 14])
            store.checkedJobIDs = ["unknown"]; store.selectNavigation(["unknown"])
            store.drafts["unknown"] = ["id": "DEMO-104"]
            store.searchActionTargets(); try await idle(store)
            state = try await store.worker.request("fixture.state")
            #expect(state["searches"].arrayValue.last?["jobIds"].arrayValue == [.string("unknown")])
            #expect(state["edits"].arrayValue.last?["expectedReviewRevision"].intValue == 14)
            #expect(store.jobs.first(where: { $0.jobID == "unknown" })?.reviewRevision == 15)
            let before = state["searches"].arrayValue.count
            store.selectQueueNavigation(["a"]); store.searchActionTargets(commit: { false })
            await withCheckedContinuation { c in DispatchQueue.main.async { c.resume() } }
            state = try await store.worker.request("fixture.state")
            #expect(state["searches"].arrayValue.count == before && store.selectedID == "unknown")
            store.selectRecognized(); store.searchActionTargets(); try await idle(store)
            state = try await store.worker.request("fixture.state")
            #expect(state["searches"].arrayValue.last?["jobIds"].arrayValue == ["a", "b", "c", "unknown"].map(JSONValue.string))
            #expect(store.jobs.map(\.reviewRevision) == [11, 12, 13, 15])
            #expect(try f.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
    @Test func clearRequiresConfirmationAndCompleteAcknowledgement() async throws {
        _ = NSApplication.shared
        let f = try QueueInteractionFixture(); defer { f.remove() }; let store = WorkspaceStore(defaults: preferences.defaults)
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [f.script.path, f.jobs.path, f.cache.path])
            store.ready = true; try await store.refresh(); store.selectNavigation(["a"])
            store.drafts["b"] = ["title": "Unflushed synthetic draft"]; store.checkedJobIDs = ["a", "b"]
            store.searchText = "Synthetic"; store.queueCategory = .issues; store.artworkPaths["a/cover"] = f.video
            let session = store.editorSession(field: "title", jobID: "a")
            let ids = store.jobs.map(\.jobID), drafts = store.drafts
            var confirms = 0
            #expect(store.requestClearQueue(confirm: { _, _ in confirms += 1; return true }, commit: { false }) == nil)
            #expect(confirms == 0)
            var confirmedCounts: [Int] = []
            let canceled = store.requestClearQueue(confirm: { n, dirty in confirms += 1; confirmedCounts = [n, dirty]; return false })
            #expect(canceled == nil && confirmedCounts == [4, 2])
            store.statusUnconfirmed = true
            #expect(store.requestClearQueue(confirm: { _, _ in confirms += 1; return true }) == nil)
            store.statusUnconfirmed = false; #expect(confirms == 1)
            #expect(store.requestClearQueue(confirm: { _, _ in store.drafts["b"]?["title"] = "Newer edit"; return true }) == nil)
            store.drafts = drafts
            var state = try await store.worker.request("fixture.state"); #expect(state["removals"].arrayValue.isEmpty)
            _ = try await store.worker.request("fixture.failRemoval", params: .object(["enabled": .bool(true)]))
            let rejected = try #require(store.requestClearQueue(confirm: { _, _ in true })); await rejected.value
            #expect(store.jobs.map(\.jobID) == ids && store.drafts == drafts && store.artworkPaths["a/cover"] == f.video)
            #expect(store.errorMessage != nil)
            _ = try await store.worker.request("fixture.failRemoval", params: .object(["enabled": .bool(false)]))
            _ = try await store.worker.request("fixture.holdRemoval", params: .object(["enabled": .bool(true)]))
            store.receive(.object(["method": .string("operation.progress"), "params": .object([
                "operationId": .string("old-progress"), "sequence": .number(1), "phase": .string("loadingArtworkPreview")
            ])]))
            #expect(store.operationProgressRetention.count == 1)
            let clearing = try #require(store.requestClearQueue(confirm: { _, _ in true }))
            for _ in 0..<100 { state = try await store.worker.request("fixture.state"); if state["pendingRemoval"].boolValue { break }; try await Task.sleep(for: .milliseconds(5)) }
            #expect(state["pendingRemoval"].boolValue)
            #expect(store.jobs.map(\.jobID) == ids && store.drafts == drafts, "A request is not an acknowledgement.")
            _ = try await store.worker.request("fixture.releaseRemoval"); await clearing.value
            #expect(store.jobs.isEmpty && store.drafts.isEmpty && store.navigationSelection.isEmpty && store.checkedJobIDs.isEmpty)
            #expect(store.selectedID == nil && store.artworkPaths.isEmpty && store.searchText.isEmpty && store.queueCategory == .all)
            #expect(store.editorSession(field: "title", jobID: "a") !== session)
            #expect(!store.canClearQueue && store.preview == nil && store.viewerArtwork.isEmpty)
            #expect(store.operationProgressRetention.count == 0 && store.operationProgressRetention.bytes == 0)
            state = try await store.worker.request("fixture.state")
            #expect(state["removals"].arrayValue.count == 2)
            #expect(state["removals"].arrayValue.allSatisfy { $0["jobIds"].arrayValue == ids.map(JSONValue.string) })
            #expect(state["methods"].arrayValue.allSatisfy { ["initialize", "workspace.getSnapshot", "jobs.remove", "fixture.state", "fixture.failRemoval", "fixture.holdRemoval", "fixture.releaseRemoval"].contains($0.stringValue) })
            #expect(try f.mediaUnchanged())
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }
}

@MainActor private final class SheetLifecycleDelegate: NSObject, NSWindowDelegate {
    var events: [String] = []
    func windowWillBeginSheet(_ notification: Notification) { events.append("begin") }
    func windowDidEndSheet(_ notification: Notification) { events.append("end") }
}

private struct ArtworkLeasePipeFixture {
    let directory: URL, script: URL, preview: URL, video: URL, nfo: URL, sourceImage: URL
    private let videoBytes = Data("SYNTHETIC LEASE TEST VIDEO".utf8)
    private let nfoBytes = Data("<movie><title>Unchanged lease fixture</title></movie>".utf8)
    private let sourceImageBytes = Data("SYNTHETIC ORIGINAL ARTWORK SOURCE".utf8)
    init(mode: String) throws {
        directory = FileManager.default.temporaryDirectory.appendingPathComponent("JML artwork lease 合成 \(UUID())")
        let cache = directory.appendingPathComponent("cache")
        try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
        script = directory.appendingPathComponent("engine.py")
        preview = cache.appendingPathComponent("preview.png")
        video = directory.appendingPathComponent("DEMO-001.mp4")
        nfo = directory.appendingPathComponent("movie.nfo")
        sourceImage = directory.appendingPathComponent("source-poster.png")
        try Data("Synthetic cache bytes; no decoder is invoked by this pipe test".utf8).write(to: preview)
        try videoBytes.write(to: video); try nfoBytes.write(to: nfo); try sourceImageBytes.write(to: sourceImage)
        try mode.write(to: directory.appendingPathComponent("mode"), atomically: true, encoding: .utf8)
        let source = #"""
import json,sys
from pathlib import Path
root=Path(sys.argv[1]);mode=(root/'mode').read_text();previews=[];releases=[];active=set();cancellations=[];pending=[];completed=False
jobs=[{'jobId':'review','reviewRevision':1,'metadata':{'id':'DEMO-001','title':'Synthetic lease review'},'videoPaths':[str(root/'DEMO-001.mp4')],'localNfoPath':str(root/'movie.nfo'),'artwork':[{'artworkId':'cover','kind':'fanart','source':'local','selected':True,'contentKey':'unchanged-content'}]}, {'jobId':'other','reviewRevision':1,'metadata':{'id':'DEMO-002','title':'Other review'},'videoPaths':[],'artwork':[]}]
if mode=='history':
 jobs=[{'jobId':'history-'+str(i),'reviewRevision':1,'metadata':{'id':'DEMO-'+str(i),'title':'Synthetic history '+str(i)},'videoPaths':[str(root/'DEMO-001.mp4')],'artwork':[{'artworkId':'cover','kind':'fanart','source':'local','selected':True,'contentKey':'content-'+str(i)}]} for i in range(8)]
def emit(id,result):print(json.dumps({'jsonrpc':'2.0','id':id,'result':result}),flush=True)
def result():
 lease=previews[-1]['leaseId']
 path=root/'cache'/((previews[-1]['jobId']+'.png') if mode=='history' else 'preview.png')
 if not path.exists():path.write_bytes(b'Synthetic cache bytes; no decoder is invoked by this pipe test')
 return {'leaseId':'wrong-lease-echo' if mode=='wrong-lease' else lease,'path':str(path),'width':32,'height':24,'isLowResolution':mode=='history'}
for line in sys.stdin:
 r=json.loads(line);id=r['id'];m=r['method'];p=r['params']
 if m=='initialize':emit(id,{'protocolVersion':1,'cacheRoot':str(root/'cache'),'capabilities':{'artworkPreviewLeases':True}})
 elif m=='preferences.get':emit(id,{'logRoot':str(root),'dataRoot':str(root/'state')})
 elif m=='workspace.getSnapshot':emit(id,{'jobs':jobs,'activeOperations':[],'recoveryWarnings':[]})
 elif m=='artwork.getPreview':
  assert p['async'] is True and p['leaseId'];previews.append(p);active.add(p['leaseId']);completed=mode!='hold-result';emit(id,{'operationId':'preview-operation'})
 elif m=='operations.get':
  if completed:emit(id,{'status':'completed','result':result()})
  else:pending.append(id)
 elif m=='artwork.releasePreview':
  releases.append(p['leaseId']);active.discard(p['leaseId']);emit(id,{'released':True})
 elif m=='operations.cancel':cancellations.append(p['operationId']);emit(id,{'accepted':True})
 elif m=='fixture.complete':
  completed=True
  for old in pending:emit(old,{'status':'completed','result':result()})
  pending.clear();emit(id,{'completed':True})
 elif m=='fixture.bumpRevision':jobs[0]['reviewRevision']+=1;jobs[0]['metadata']['title']='Updated metadata, identical image';emit(id,{'ok':True})
 elif m=='fixture.bumpContent':jobs[0]['reviewRevision']+=1;jobs[0]['artwork'][0]['contentKey']+='-changed';emit(id,{'ok':True})
 elif m=='fixture.state':emit(id,{'previews':previews,'releases':releases,'activeLeases':sorted(active),'cancellations':cancellations,'pendingResult':bool(pending),'completed':completed})
 elif m=='jobs.remove':
  ids=p.get('jobIds') or [p['jobId']];jobs=[j for j in jobs if j['jobId']not in ids];emit(id,{'removed':True,'removedIds':ids,'removedCount':len(ids)})
 elif m=='shutdown':emit(id,{'shutdown':True});break
 else:raise Exception('Unexpected lease fixture method '+m)
"""#
        try source.write(to: script, atomically: true, encoding: .utf8)
    }
    @MainActor func makeStore(defaults: UserDefaults) -> WorkspaceStore {
        WorkspaceStore(engineExecutable: { URL(fileURLWithPath: "/usr/bin/python3") }, engineArguments: [script.path, directory.path], defaults: defaults)
    }
    func state(_ worker: WorkerClient) async throws -> JSONValue { try await worker.request("fixture.state") }
    @MainActor func waitForState(_ worker: WorkerClient, matching predicate: (JSONValue) -> Bool) async throws -> JSONValue {
        for _ in 0..<400 {
            let current = try await state(worker)
            if predicate(current) { return current }
            try await Task.sleep(for: .milliseconds(5))
        }
        throw RPCFailure(code: -1, message: "The synthetic artwork lease fixture did not reach the expected state.")
    }
    func mediaUnchanged() throws -> Bool {
        try Data(contentsOf: video) == videoBytes && Data(contentsOf: nfo) == nfoBytes && Data(contentsOf: sourceImage) == sourceImageBytes
    }
    func remove() { try? FileManager.default.removeItem(at: directory) }
}

private struct QueueInteractionFixture {
    let directory: URL, script: URL, jobs: URL, cache: URL, video: URL, nfo: URL
    private let videoBytes = Data("Synthetic unchanged video".utf8), nfoBytes = Data("<movie><unknown>Keep</unknown></movie>".utf8)
    init() throws {
        directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite interaction 合成 \(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        script = directory.appendingPathComponent("engine.py"); jobs = directory.appendingPathComponent("jobs.json"); cache = directory.appendingPathComponent("cache"); video = directory.appendingPathComponent("synthetic.mp4"); nfo = directory.appendingPathComponent("movie.nfo")
        try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
        try videoBytes.write(to: video); try nfoBytes.write(to: nfo)
        var initial: [[String: Any]] = []
        for (index, id) in ["a", "b", "c", "unknown"].enumerated() {
            let metadata = ["id": id == "unknown" ? "" : "DEMO-10\(index + 1)", "title": "Synthetic " + id]
            initial.append(["jobId": id, "reviewRevision": index + 11, "metadata": metadata, "busy": false, "hasFailedSources": id == "b", "hasUnsavedChanges": id == "a", "videoPaths": [video.path], "searchAttempts": id == "b" ? [["source": "libredmm", "success": false]] : []])
        }
        try JSONSerialization.data(withJSONObject: initial).write(to: jobs)
        let source = #"""
import json,sys
jobs=json.load(open(sys.argv[1]));cache=sys.argv[2];searches=[];edits=[];removals=[];methods=[];failed=False;hold=False;pending=None
def emit(id,result):print(json.dumps({'jsonrpc':'2.0','id':id,'result':result}),flush=True)
def remove(id,p):
 global jobs
 ids=p.get('jobIds') or [p['jobId']];jobs=[j for j in jobs if j['jobId']not in ids];emit(id,{'removed':True,'removedIds':ids,'removedCount':len(ids)})
for line in sys.stdin:
 r=json.loads(line);id=r['id'];m=r['method'];p=r['params'];methods.append(m)
 if m=='initialize':emit(id,{'protocolVersion':1,'cacheRoot':cache})
 elif m=='workspace.getSnapshot':emit(id,{'jobs':jobs})
 elif m=='fixture.installJobs':jobs=p['jobs'];emit(id,{'ok':True})
 elif m=='jobs.applyEdits':
  edits.append(p);j=next(j for j in jobs if j['jobId']==p['jobId']);assert j['reviewRevision']==p['expectedReviewRevision'];j['metadata'].update(p['edits']);j['reviewRevision']+=1;j['hasUnsavedChanges']=True;emit(id,j)
 elif m in ('jobs.search','jobs.retryFailedSources'):searches.append(p);emit(id,{'operationId':'query-'+str(len(searches))})
 elif m=='operations.get':emit(id,{'status':'completed','result':{'warnings':[],'jobResults':[{'jobId':j,'status':'Completed'}for j in searches[-1]['jobIds']]}})
 elif m=='jobs.remove':
  removals.append(p)
  if failed:print(json.dumps({'jsonrpc':'2.0','id':id,'error':{'code':-32010,'message':'Synthetic busy rejection','data':{'reason':'JOB_BUSY'}}}),flush=True)
  elif hold:pending=(id,p)
  else:remove(id,p)
 elif m=='fixture.state':emit(id,{'searches':searches,'edits':edits,'removals':removals,'methods':methods,'pendingRemoval':pending is not None})
 elif m=='fixture.failRemoval':failed=p['enabled'];emit(id,{'ok':True})
 elif m=='fixture.holdRemoval':hold=p['enabled'];emit(id,{'ok':True})
 elif m=='fixture.releaseRemoval':
  if pending:remove(*pending);pending=None
  emit(id,{'ok':True})
 elif m=='shutdown':emit(id,{'shutdown':True});break
 else:raise Exception('Unexpected method '+m)
"""#
        try source.write(to: script, atomically: true, encoding: .utf8)
    }
    func mediaUnchanged() throws -> Bool { try Data(contentsOf: video) == videoBytes && Data(contentsOf: nfo) == nfoBytes }
    func remove() { try? FileManager.default.removeItem(at: directory) }
}
