import Testing
import Foundation
#if SWIFT_PACKAGE
@testable import WorkerKit
#else
@testable import JavMetaLiteMac
#endif

struct WorkerKitTests {
    @Test func primaryArtworkKeepsTheReviewedPairAndNeverBorrowsSamplesOrOtherSources() {
        func art(_ id: String, _ kind: String, _ source: String, _ selected: Bool) -> JSONValue {
            .object(["artworkId": .string(id), "kind": .string(kind), "source": .string(source), "selected": .bool(selected)])
        }
        let other = art("other", "poster", "other", false), sample = art("sample", "sample", "local", true)
        let fanart = art("cover", "fanart", "libredmm", true), poster = art("poster", "poster", "libredmm", true)
        #expect(ArtworkReviewPolicy.primary([other, sample, fanart, poster], kind: "poster") == poster)
        #expect(ArtworkReviewPolicy.primary([other, sample, fanart, poster], kind: "fanart") == fanart)
        #expect(ArtworkReviewPolicy.primary([other, sample, fanart], kind: "poster") == fanart)
        let local = art("local", "fanart", "local", true)
        #expect(ArtworkReviewPolicy.primary([other, sample, local], kind: "poster") == nil)
        #expect(ArtworkReviewPolicy.primary([other, sample], kind: "fanart") == nil)
        #expect(ArtworkReviewPolicy.primary([], kind: "poster") == nil)
    }
    @Test func discoverySelectionIsIndependentAndFreezesTheExactOrderedSubset() {
        func item(_ id: String, movie: String, allowed: Bool) -> JSONValue {
            .object(["itemId": .string(id), "movieId": .string(movie), "primaryPath": .string("/synthetic/DEMO-999/\(id).mp4"), "videoPaths": .array([.string("/synthetic/DEMO-999/\(id).mp4")]), "canImport": .bool(allowed)])
        }
        let discovery = InputDiscovery(.object(["discoveryId": .string("server-token"), "items": .array([
            item("unknown", movie: "", allowed: true), item("recognized", movie: "DEMO-101", allowed: true), item("duplicate", movie: "DEMO-102", allowed: false)
        ])]))
        var review = InputDiscoveryReview(discovery)
        #expect(review.selectedIDs == ["unknown", "recognized"])
        #expect(discovery.items[0].movieID.isEmpty, "The client must not borrow a movie ID from a parent path.")
        review.selectRecognized()
        #expect(review.selectedIDs == ["recognized"], "Recognized selection replaces the whole set; it is not additive.")
        review.deselectAll(); #expect(!review.canConfirm); #expect(review.beginConfirmation() == nil)
        review.setSelected("duplicate", true); review.setSelected("arbitrary", true)
        #expect(review.selectedIDs.isEmpty)
        review.setSelected("unknown", true); #expect(review.canConfirm)
        let submitted = review.beginConfirmation()
        #expect(submitted == ["unknown"])
        review.selectAll(); review.deselectAll(); review.setSelected("recognized", true)
        #expect(review.selectedIDs == ["unknown"]); #expect(review.beginConfirmation() == nil)
    }

    @Test func discoveryWithoutRecognizedIDsStillAllowsManualSelection() {
        var review = InputDiscoveryReview(InputDiscovery(.object(["discoveryId": .string("token"), "items": .array([
            .object(["itemId": .string("manual"), "movieId": .string(""), "canImport": .bool(true)])
        ])])))
        #expect(review.recognizedIDs.isEmpty)
        review.selectRecognized(); #expect(review.selectedIDs == ["manual"])
        review.deselectAll(); #expect(!review.canConfirm)
        review.setSelected("manual", true); #expect(review.beginConfirmation() == ["manual"])
    }

    @Test func gallerySourceFilteringPreservesGlobalEntryPositionAndUnloadedState() {
        let items: [JSONValue] = [
            .object(["artworkId": .string("local-poster"), "kind": .string("poster"), "source": .string("local")]),
            .object(["artworkId": .string("libre-fanart"), "kind": .string("fanart"), "source": .string("libredmm")]),
            .object(["artworkId": .string("libre-poster"), "kind": .string("poster"), "source": .string("libredmm")]),
            .object(["artworkId": .string("libre-sample"), "kind": .string("sample"), "source": .string("libredmm")]),
            .object(["artworkId": .string("r18-sample"), "kind": .string("sample"), "source": .string("r18dev")])
        ]
        #expect(ArtworkReviewPolicy.sources(items) == ["local", "libredmm", "r18dev"])
        #expect(ArtworkReviewPolicy.filtered(items, source: "").count == 5)
        #expect(ArtworkReviewPolicy.entryIndex(items, artworkID: "libre-poster") == 2, "Poster entry uses its global position, not a poster-only subset.")
        let libre = ArtworkReviewPolicy.filtered(items, source: "libredmm")
        #expect(libre.count == 3); #expect(libre.allSatisfy { $0["source"].stringValue == "libredmm" })
        #expect(ArtworkReviewPolicy.filtered(items, source: "r18dev").count == 1)
        #expect(ArtworkReviewPolicy.previewState(hasArtwork: true, loaded: false, failed: false) == "artworkNotLoaded")
        #expect(ArtworkReviewPolicy.previewState(hasArtwork: false, loaded: false, failed: false) == "noArtwork")
        #expect(ArtworkReviewPolicy.previewState(hasArtwork: true, loaded: false, failed: true) == "artworkPreviewFailed")
    }

    @Test func qualityAndPartialSourceFailuresRemainAdvisoryAndDoNotInventDimensions() {
        let low = MovieJobSnapshot(.object(["jobId": .string("FNS-121"), "reviewRevision": .number(11), "metadata": .object(["id": .string("FNS-121")]), "coverQuality": .object(["state": .string("lowResolution"), "width": .number(147), "height": .number(200)])]))
        #expect(QueueReviewPolicy.hasIssue(low)); #expect(!low.localNfoSaveBlocked); #expect(low.reviewRevision == 11)
        let unknown = MovieJobSnapshot(.object(["metadata": .object(["id": .string("DEMO-001")]), "coverQuality": .object(["state": .string("unknown")])]))
        #expect(!QueueReviewPolicy.hasIssue(unknown)); #expect(unknown.coverQuality["width"] == .null)
        let partial = MovieJobSnapshot(.object(["metadata": .object(["id": .string("IPX-081")]), "hasFailedSources": .bool(true), "searchState": .string("NeedsReview")]))
        #expect(QueueReviewPolicy.hasIssue(partial))
    }

    @Test func testSplitMessagesAndEscapedNewlines() throws {
        var framer = LineFramer()
        let message = Data("{\"jsonrpc\":\"2.0\",\"id\":\"测试-1\",\"result\":\"line 1\\nline 2\"}\n".utf8)
        var output: [Data] = []
        for byte in message { output += try framer.append(Data([byte])) }
        #expect(output.count == 1)
        let json = try JSONDecoder().decode(JSONValue.self, from: #require(output.first))
        #expect(json["result"].stringValue == "line 1\nline 2")
        #expect(!framer.hasIncompleteMessage)
    }
    @Test func testConsecutiveMessagesAndPartialTail() throws {
        var framer = LineFramer()
        #expect(try framer.append(Data("{}\n{}\n{\"a\":".utf8)).count == 2)
        #expect(framer.hasIncompleteMessage)
        #expect(try framer.append(Data("1}\n".utf8)) == [Data("{\"a\":1}".utf8)])
    }
    @Test func testOversizedMessageRejected() throws {
        var framer = LineFramer()
        #expect(throws: (any Error).self) { try framer.append(Data(repeating: 65, count: LineFramer.maximumBytes + 1)) }
    }
    @Test func testJSONKeepsRawValuesAndActorStructures() throws {
        let json = JSONValue.object(["releaseDate": .string("Unknown / 2026"), "runtimeMinutes": .string("90+"), "rating": .string("N/A"), "actors": .array([.object(["name": .string("合成角色"), "imageUrl": .string("https://example.invalid/actor.png")])]), "plot": .string("line one\nline two"), "unknownFutureField": .bool(true)])
        #expect(try JSONDecoder().decode(JSONValue.self, from: JSONEncoder().encode(json)) == json)
    }
    @Test func testSharedProtocolGoldenFixture() throws {
        let source = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let data = try Data(contentsOf: source.appendingPathComponent("contracts/worker-v1/golden.json"))
        let fixture = try JSONDecoder().decode(JSONValue.self, from: data)
        let metadata = fixture["metadata"]
        #expect(metadata["releaseDate"].stringValue == "legacy-date")
        #expect(metadata["rating"].stringValue == "N/A")
        #expect(metadata["actors"].arrayValue.first?["name"].stringValue == "Demo Actor")
        #expect(fixture["edit"]["params"]["edits"]["plot"].stringValue == "第一行\nSecond line")
        #expect(try JSONDecoder().decode(JSONValue.self, from: JSONEncoder().encode(metadata)) == metadata)
    }
    @Test func testJobSnapshotDoesNotTruncateMetadata() {
        let json = JSONValue.object(["jobId": .string("job-synthetic"), "reviewRevision": .number(17), "metadata": .object(["title": .string("合成資料"), "futureOptional": .string("keep")]), "videoPaths": .array([.string("/tmp/日本語/DEMO-001.mp4")]), "saveState": .string("Completed"), "searchState": .string("NeedsReview")])
        let job = MovieJobSnapshot(json)
        #expect(job.reviewRevision == 17)
        #expect(job.metadata["futureOptional"].stringValue == "keep")
        #expect(job.videoPaths.count == 1)
        #expect(!job.localNfoSaveBlocked)
    }
    @Test func testMissingBundledEngineIsExplicitError() {
        #expect(throws: (any Error).self) { try WorkerClient.bundledExecutable(bundle: Bundle.main) }
    }
    @Test func testAggregatePreviewRetainsEveryRevisionAndVideoTransfer() {
        let preview = SavePreview(.object([
            "planId": .string("batch-plan"), "canExecute": .bool(false),
            "sourceJobIds": .array([.string("job-a"), .string("job-b")]),
            "jobs": .array([
                .object(["jobId": .string("job-a"), "reviewRevision": .number(9), "changes": .array([.object(["operation": .string("CopyAndVerifyVideo"), "target": .string("/tmp/destination/DEMO-001.mp4")])])]),
                .object(["jobId": .string("job-b"), "reviewRevision": .number(12), "changes": .array([]), "blockingConflicts": .array([.string("Protected input belongs to another movie")])])
            ])
        ]))
        #expect(preview.jobIDs == ["job-a", "job-b"])
        #expect(preview.jobs.map(\.reviewRevision) == [9, 12])
        #expect(preview.containsVideoTransfer)
        #expect(!preview.canExecute)
        #expect(preview.jobs[1].blockingConflicts.count == 1)
    }
    @Test func testPartialAndCleanupOutcomesNeverBecomeFullSuccess() {
        for reason in ["partialFailure", "canceled", "cleanupRequired", "recoveryRequired"] {
            let result = SaveOperationOutcome(.object([
                "outcome": .string(reason), "savedJobIds": .array([.string("first")]),
                "notStartedJobIds": .array([.string("third")]),
                "cleanupWarnings": reason == "cleanupRequired" ? .array([.string("Retained staging folder")]) : .array([]),
                "jobResults": .array([
                    .object(["jobId": .string("first"), "status": .string("Completed")]),
                    .object(["jobId": .string("second"), "status": .string(reason == "canceled" ? "Canceled" : "Failed")]),
                    .object(["jobId": .string("third"), "status": .string("NotStarted")])
                ])
            ]))
            #expect(!result.isCompleteSuccess)
            #expect(result.savedJobIDs == ["first"])
            #expect(result.notStartedJobIDs == ["third"])
            #expect(result.jobResults.count == 3)
        }
    }
    @Test func testConfirmedPartsPreviewKeepsAllSourceIDs() {
        let preview = SavePreview(.object([
            "planId": .string("parts-plan"), "canExecute": .bool(true),
            "sourceJobIds": .array([.string("first"), .string("second")]),
            "jobs": .array([.object(["jobId": .string("first"), "reviewRevision": .number(4), "changes": .array([])])])
        ]))
        #expect(preview.combinesParts)
        #expect(preview.jobIDs.contains("second"))
        #expect(preview.jobs.count == 1)
    }
    @Test func testSaveTimeoutNeverRetriesAndStatusRemainsQueryable() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite timeout fixture \(UUID())", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let script = directory.appendingPathComponent("delayed_engine.py")
        let count = directory.appendingPathComponent("save-count.txt")
        let source = #"""
import json,sys,threading
counter=sys.argv[1]
def emit(id,result):
    print(json.dumps({"jsonrpc":"2.0","id":id,"result":result}),flush=True)
for line in sys.stdin:
    request=json.loads(line)
    method=request["method"]; id=request["id"]
    if method=="initialize": emit(id,{"protocolVersion":1})
    elif method=="save.execute":
        with open(counter,"a") as output: output.write("save\n")
        delayed=threading.Timer(1.0,lambda request_id=id:emit(request_id,{"operationId":"delayed-save"}))
        delayed.daemon=True; delayed.start()
    elif method=="operations.get": emit(id,{"operationId":"delayed-save","status":"completed","result":{"savedJobIds":[]}})
    elif method=="workspace.getSnapshot": emit(id,{"jobs":[],"activeOperations":[]})
    elif method=="shutdown":
        emit(id,{"shutdown":True});break
"""#
        try source.write(to: script, atomically: true, encoding: .utf8)
        let client = WorkerClient(requestTimeout: .milliseconds(250))
        do {
            _ = try await client.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [script.path, count.path])
            do {
                _ = try await client.request("save.execute", params: .object(["planId": .string("fixture-plan")]))
                Issue.record("Delayed save should time out without being automatically retried.")
            } catch let error as RPCFailure { #expect(error.reason == "WORKER_UNAVAILABLE") }
            let state = try await client.request("operations.get", params: .object(["operationId": .string("delayed-save")]))
            #expect(state["status"].stringValue == "completed")
            let snapshot = try await client.request("workspace.getSnapshot")
            #expect(snapshot["activeOperations"].arrayValue.isEmpty)
            #expect(try String(contentsOf: count, encoding: .utf8) == "save\n")
            await client.shutdown()
        } catch { await client.shutdown(); throw error }
    }
    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil)) func testNativeClientRealWorkerNfoRoundTrip() async throws {
        guard let path = ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"], !path.isEmpty else {
            throw RPCFailure(code: -1, message: "Set JAVMETALITE_TEST_WORKER to run the actual bundled Worker integration test.")
        }
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite Swift 测试 \(UUID())", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let video = directory.appendingPathComponent("DEMO-001.mp4")
        let nfo = directory.appendingPathComponent("DEMO-001.nfo")
        let originalVideo = Data("synthetic-video-preserve-bytes".utf8)
        try originalVideo.write(to: video)
        try Data("<?xml version=\"1.0\"?><movie custom=\"preserved\"><title>Before</title><originaltitle>原始名称</originaltitle><id>DEMO-001</id><runtime>90+</runtime><rating>N/A</rating><custom-extension token=\"abc\">Keep me</custom-extension></movie>".utf8).write(to: nfo)
        let client = WorkerClient()
        do {
            let handshake = try await client.start(executable: URL(fileURLWithPath: path), arguments: ["--data-root", directory.appendingPathComponent("state").path, "--diagnostic-stderr-bytes", "1048576"])
            #expect(handshake["protocolVersion"].intValue == 1)
            let imported = try await client.request("workspace.importInputs", params: .object(["paths": .array([.string(video.path)]), "recursive": .bool(true)]))
            _ = try await finish(imported, client: client)
            let snapshot = try await client.request("workspace.getSnapshot")
            let initial = try #require(snapshot["jobs"].arrayValue.first)
            let jobID = initial["jobId"].stringValue
            #expect(initial["metadata"]["title"].stringValue == "Before")
            let edit = try await client.request("jobs.applyEdits", params: .object(["jobId": .string(jobID), "expectedReviewRevision": initial["reviewRevision"], "edits": .object(["title": .string("Swift 原生编辑\n第二行")])]))
            #expect(edit["reviewRevision"].intValue > initial["reviewRevision"].intValue)
            let preview = try await client.request("save.preview", params: .object(["jobId": .string(jobID), "expectedReviewRevision": edit["reviewRevision"], "options": .object(["overwriteExisting": .bool(true), "includeIdInTitle": .bool(false)])]))
            #expect(preview["canExecute"].boolValue)
            let saved = try await client.request("save.execute", params: .object(["planId": preview["planId"]]))
            _ = try await finish(saved, client: client)
            let repeated = try await client.request("save.execute", params: .object(["planId": preview["planId"]]))
            #expect(saved["operationId"] == repeated["operationId"])
            let xml = try String(contentsOf: nfo, encoding: .utf8)
            #expect(xml.contains("Swift 原生编辑"))
            #expect(xml.contains("custom-extension")); #expect(xml.contains("preserved")); #expect(xml.contains("Keep me"))
            #expect(xml.contains("原始名称")); #expect(xml.contains("90+"))
            #expect(try Data(contentsOf: video) == originalVideo)
            await client.shutdown()
            for _ in 0..<40 { if !(await client.isRunning()) { break }; try await Task.sleep(for: .milliseconds(50)) }
            let running = await client.isRunning(); #expect(!running, "Normal shutdown must reap the Worker process.")
        } catch { await client.shutdown(); throw error }
    }
    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil)) func testNativeClientBatchArtworkAndVerifiedOrganization() async throws {
        let workerPath = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite M4 複数 \(UUID())", isDirectory: true)
        let destination = directory.appendingPathComponent("整理 目标", isDirectory: true)
        try FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let repository = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let cover = try Data(contentsOf: repository.appendingPathComponent("fixtures/macos/synthetic-cover-5x2.png"))
        var videoPaths: [String] = []
        var expectedBytes: [String: Data] = [:]
        for number in 21...22 {
            let id = "DEMO-0\(number)"
            let folder = directory.appendingPathComponent("source \(number)", isDirectory: true)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let video = folder.appendingPathComponent(id + ".mp4")
            let bytes = Data(repeating: UInt8(number), count: 17001)
            try bytes.write(to: video); expectedBytes[id] = bytes; videoPaths.append(video.path)
            try Data("<movie custom=\"keep\"><id>\(id)</id><title>Synthetic \(number)</title><opaque>Preserved</opaque></movie>".utf8).write(to: folder.appendingPathComponent(id + ".nfo"))
            try cover.write(to: folder.appendingPathComponent(id + "-fanart.png"))
        }
        let client = WorkerClient()
        do {
            let handshake = try await client.start(executable: URL(fileURLWithPath: workerPath), arguments: ["--data-root", directory.appendingPathComponent("state").path])
            #expect(handshake["capabilities"]["writeArtwork"].boolValue)
            #expect(handshake["capabilities"]["organizeVideo"].boolValue)
            #expect(handshake["capabilities"]["batchSave"].boolValue)
            let imported = try await client.request("workspace.importInputs", params: .object(["paths": .array(videoPaths.map(JSONValue.string)), "recursive": .bool(true)]))
            _ = try await finish(imported, client: client)
            var snapshot = try await client.request("workspace.getSnapshot")
            #expect(snapshot["jobs"].arrayValue.count == 2)
            for job in snapshot["jobs"].arrayValue {
                let artwork = try #require(job["artwork"].arrayValue.first)
                _ = try await client.request("artwork.select", params: .object(["jobId": job["jobId"], "expectedReviewRevision": job["reviewRevision"], "artworkId": artwork["artworkId"], "selected": .bool(true)]))
            }
            snapshot = try await client.request("workspace.getSnapshot")
            let jobs = snapshot["jobs"].arrayValue
            let ids = jobs.map { $0["jobId"] }
            let revisions = Dictionary(uniqueKeysWithValues: jobs.map { ($0["jobId"].stringValue, $0["reviewRevision"]) })
            let response = try await client.request("save.preview", params: .object([
                "jobIds": .array(ids), "expectedReviewRevisions": .object(revisions), "async": .bool(true),
                "options": .object(["writeNfo": .bool(true), "downloadPoster": .bool(true), "downloadFanart": .bool(true), "downloadExtrafanart": .bool(false), "replaceLocalExtrafanart": .bool(false), "overwriteExisting": .bool(true), "includeIdInTitle": .bool(false), "targetMode": .string("CustomRootNumberFolder"), "renameVideo": .bool(true), "customRootDirectory": .string(destination.path)])
            ]))
            let planJSON = try await finish(response, client: client)
            let plan = SavePreview(planJSON)
            #expect(plan.jobs.count == 2); #expect(plan.canExecute); #expect(plan.containsVideoTransfer)
            #expect(videoPaths.allSatisfy { FileManager.default.fileExists(atPath: $0) }, "Preview must not move videos.")
            let operation = try await client.request("save.execute", params: .object(["planId": .string(plan.planID)]))
            let result = SaveOperationOutcome(try await finish(operation, client: client))
            #expect(result.isCompleteSuccess); #expect(result.savedJobIDs.count == 2); #expect(result.jobResults.count == 2)
            #expect(result.outputs.count >= 6)
            snapshot = try await client.request("workspace.getSnapshot")
            for job in snapshot["jobs"].arrayValue {
                let video = try #require(job["videoPaths"].arrayValue.first?.stringValue)
                #expect(URL(fileURLWithPath: video).resolvingSymlinksInPath().path.hasPrefix(destination.resolvingSymlinksInPath().path + "/"))
                #expect(try Data(contentsOf: URL(fileURLWithPath: video)) == expectedBytes[job["metadata"]["id"].stringValue])
            }
            #expect(videoPaths.allSatisfy { !FileManager.default.fileExists(atPath: $0) })
            for output in result.outputs where output.hasSuffix(".nfo") {
                let xml = try String(contentsOfFile: output, encoding: .utf8)
                #expect(xml.contains("opaque")); #expect(xml.contains("Preserved")); #expect(xml.contains("custom=\"keep\""))
            }
            await client.shutdown()
        } catch { await client.shutdown(); throw error }
    }
    private func finish(_ response: JSONValue, client: WorkerClient) async throws -> JSONValue {
        let id = response["operationId"].stringValue
        for _ in 0..<400 {
            let operation = try await client.request("operations.get", params: .object(["operationId": .string(id)]))
            if operation["status"].stringValue == "completed" { return operation["result"] }
            if ["failed", "canceled"].contains(operation["status"].stringValue) { throw RPCFailure(code: -1, message: String(describing: operation)) }
            try await Task.sleep(for: .milliseconds(50))
        }
        throw RPCFailure(code: -1, message: "Integration operation timed out without retrying its save.")
    }
}

@Suite struct OperationProgressCacheTests {
    func event(_ id: String, _ sequence: Int = 1, phase: String = "preparingPreview") -> JSONValue {
        .object(["operationId": .string(id), "sequence": .number(Double(sequence)), "phase": .string(phase),
                 "details": .object(["currentIndex": .number(2), "totalJobs": .number(7), "path": .string(String(repeating: "Z", count: 10000))]),
                 "result": .object(["large": .string(String(repeating: "X", count: 262144))])])
    }
    @Test func earlyProgressKeepsOnlyNeededFieldsAndCompletionReleasesIt() throws {
        var cache = OperationProgressCache()
        let recorded = cache.receive(method: "operation.progress", params: event("early", 3))
        let progress = try #require(recorded)
        #expect(progress["result"] == .null && progress["details"]["path"] == .null)
        #expect(cache.progress(for: "early")?["phase"].stringValue == "preparingPreview")
        #expect(progress["details"]["totalJobs"].intValue == 7)
        let outdated = cache.receive(method: "operation.progress", params: event("early", 2))
        #expect(outdated == nil)
        let finished = cache.receive(method: "operation.finished", params: event("early", 4, phase: "completed"))
        #expect(finished != nil)
        #expect(cache.count == 0 && cache.retainedBytes == 0)
        let unknown = cache.receive(method: "unknown", params: event("unknown"))
        let oversized = cache.receive(method: "operation.progress", params: event(String(repeating: "a", count: 129)))
        #expect(unknown == nil && oversized == nil)
    }
    @Test func countAndByteLimitsEvictOldProgressWhileActiveProgressSurvives() {
        var countLimited = OperationProgressCache(maximumCount: 3)
        _ = countLimited.receive(method: "operation.progress", params: event("active"))
        for i in 0..<30 { _ = countLimited.receive(method: "operation.progress", params: event("queued-\(i)"), protecting: "active") }
        #expect(countLimited.count == 3 && countLimited.progress(for: "active") != nil)
        #expect(countLimited.progress(for: "queued-0") == nil && countLimited.progress(for: "queued-29") != nil)
        var bytesLimited = OperationProgressCache(maximumCount: 100, maximumBytes: 700)
        for i in 0..<30 { _ = bytesLimited.receive(method: "operation.progress", params: event("queued-\(i)")) }
        #expect(bytesLimited.retainedBytes <= 700 && bytesLimited.count < 30)
        bytesLimited.removeAll()
        #expect(bytesLimited.count == 0 && bytesLimited.retainedBytes == 0)
    }
}
