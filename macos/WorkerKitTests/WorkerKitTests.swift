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
    @Test func testFramerEmptyChunksAndAdjacentFramesAcrossEverySplit() throws {
        let wire = Data("\n{}\r\n\n{\"text\":\"中文\\nsecond line\"}\nlast\n\n".utf8)
        let expected = [Data("{}\r".utf8), Data("{\"text\":\"中文\\nsecond line\"}".utf8), Data("last".utf8)]
        for split in 0...wire.count {
            var framer = LineFramer()
            #expect(try framer.append(Data()).isEmpty)
            var output = try framer.append(wire[..<split])
            #expect(try framer.append(Data()).isEmpty)
            output += try framer.append(wire[split...])
            #expect(output == expected)
            #expect(!framer.hasIncompleteMessage)
            #expect(try framer.append(Data()).isEmpty)
        }
    }
    @Test func testFramerAcceptsNonzeroDataSliceIndicesAndRetainsPartialTail() throws {
        var framer = LineFramer()
        let first = Data("ignore{}\n\n{\"a\":1}\n{".utf8).dropFirst(6)
        let second = Data("skip\"z\":2}\n\n".utf8).dropFirst(4)
        #expect(first.startIndex != 0 && second.startIndex != 0)
        #expect(try framer.append(first) == [Data("{}".utf8), Data("{\"a\":1}".utf8)])
        #expect(framer.hasIncompleteMessage)
        #expect(try framer.append(second) == [Data("{\"z\":2}".utf8)])
        #expect(!framer.hasIncompleteMessage)
    }
    @Test(arguments: [false, true])
    func testFramerAcceptsExactMaximumFrameAndFollowingFrame(separateDelimiter: Bool) throws {
        var framer = LineFramer()
        let payload = Data(repeating: 65, count: LineFramer.maximumBytes)
        let output: [Data]
        if separateDelimiter {
            for offset in stride(from: 0, to: payload.count, by: 16384) {
                #expect(try framer.append(payload[offset..<min(payload.count, offset + 16384)]).isEmpty)
            }
            #expect(framer.hasIncompleteMessage)
            output = try framer.append(Data("\n{}\n\n".utf8))
        } else {
            var chunk = payload
            chunk.append(contentsOf: "\n{}\n\n".utf8)
            output = try framer.append(chunk)
        }
        #expect(output == [payload, Data("{}".utf8)])
        #expect(!framer.hasIncompleteMessage)
    }
    @Test(arguments: [false, true])
    func testFramerRejectsOversizedTerminatedFrameAfterValidPrefix(fragmented: Bool) throws {
        var framer = LineFramer()
        #expect(try framer.append(Data("{}\n\n".utf8)) == [Data("{}".utf8)])
        do {
            if fragmented {
                #expect(try framer.append(Data(repeating: 65, count: LineFramer.maximumBytes)).isEmpty)
                _ = try framer.append(Data([65, 10]))
            } else {
                var oversized = Data(repeating: 65, count: LineFramer.maximumBytes + 1)
                oversized.append(10)
                _ = try framer.append(oversized)
            }
            Issue.record("An oversized frame must be rejected even when its newline has arrived.")
        } catch let failure as RPCFailure {
            #expect(failure.code == -32600 && failure.message == "Protocol message exceeds 8 MiB.")
        }
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
    @Test func closedWorkerInputFailsWithoutTerminatingClientOrReplayingSave() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite pipe fixture \(UUID())", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let script = directory.appendingPathComponent("closed_input.py")
        let release = directory.appendingPathComponent("release")
        let received = directory.appendingPathComponent("received.txt")
        try #"""
import json,os,sys,time
from pathlib import Path
release=Path(sys.argv[1]);received=Path(sys.argv[2])
for line in sys.stdin:
    frame=json.loads(line)
    with received.open('a') as output: output.write(frame['method']+'\n')
    if frame['method']=='initialize': result={'protocolVersion':1}
    elif frame['method']=='fixture.closeInput':
        os.close(0)
        print(json.dumps({'jsonrpc':'2.0','id':frame['id'],'result':{'closed':True}}),flush=True)
        while not release.exists(): time.sleep(.01)
        break
    else: raise RuntimeError('A failed request must not be replayed')
    print(json.dumps({'jsonrpc':'2.0','id':frame['id'],'result':result}),flush=True)
"""#.write(to: script, atomically: true, encoding: .utf8)
        let client = WorkerClient(requestTimeout: .seconds(5))
        func finish() async {
            try? Data().write(to: release)
            await client.shutdown()
            for _ in 0..<100 {
                if !(await client.isRunning()) { break }
                try? await Task.sleep(for: .milliseconds(20))
            }
        }
        do {
            _ = try await client.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [script.path, release.path, received.path])
            let result = try await client.request("fixture.closeInput")
            #expect(result["closed"].boolValue)
            #expect(await client.isRunning(), "The engine must remain alive with its input closed to exercise the write race.")
            do {
                _ = try await client.request("save.execute", params: .object(["planId": .string("must-not-replay")]))
                Issue.record("Writing to the closed pipe must fail.")
            } catch { /* EPIPE must be reported without killing this process. */ }
            #expect(try String(contentsOf: received, encoding: .utf8) == "initialize\nfixture.closeInput\n")
            await finish()
            #expect(!(await client.isRunning()))
        } catch { await finish(); throw error }
    }
    @Test func testSaveTimeoutNeverRetriesAndStatusRemainsQueryable() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite timeout fixture \(UUID())", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let script = directory.appendingPathComponent("delayed_engine.py")
        let count = directory.appendingPathComponent("save-count.txt")
        let source = #"""
import json,sys
counter=sys.argv[1]
delayed_save_id=None
def emit(id,result):
    print(json.dumps({"jsonrpc":"2.0","id":id,"result":result}),flush=True)
for line in sys.stdin:
    request=json.loads(line)
    method=request["method"]; id=request["id"]
    if method=="initialize": emit(id,{"protocolVersion":1})
    elif method=="save.execute":
        with open(counter,"a") as output: output.write("save\n")
        delayed_save_id=id
    elif method=="fixture.releaseSave":
        emit(delayed_save_id,{"operationId":"delayed-save"})
        emit(id,{"released":True})
    elif method=="operations.get": emit(id,{"operationId":"delayed-save","status":"completed","result":{"savedJobIds":[]}})
    elif method=="workspace.getSnapshot": emit(id,{"jobs":[],"activeOperations":[]})
    elif method=="shutdown":
        emit(id,{"shutdown":True});break
"""#
        try source.write(to: script, atomically: true, encoding: .utf8)
        // Allow Python's cold start on a loaded runner. The save acknowledgement
        // is withheld until after the timeout, not raced against a short timer.
        let client = WorkerClient(requestTimeout: .seconds(5))
        do {
            _ = try await client.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [script.path, count.path])
            do {
                _ = try await client.request("save.execute", params: .object(["planId": .string("fixture-plan")]))
                Issue.record("Delayed save should time out without being automatically retried.")
            } catch let error as RPCFailure { #expect(error.reason == "WORKER_UNAVAILABLE") }
            let released = try await client.request("fixture.releaseSave")
            #expect(released["released"].boolValue, "A late save response must not fulfill a different request.")
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

/// Real pipes with a deliberately stalled reader, independent of the .NET Worker.
private struct StalledPipeFixture {
    let directory: URL
    let script: URL
    let release: URL
    let received: URL
    let partial: URL
    let ready: URL
    let initializationRelease: URL
    private let holdInitializationUntilTimeout: Bool
    init(holdInitializationUntilTimeout: Bool = false) throws {
        directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite stalled pipe \(UUID())", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        script = directory.appendingPathComponent("worker.py")
        release = directory.appendingPathComponent("release")
        received = directory.appendingPathComponent("received.jsonl")
        partial = directory.appendingPathComponent("partial-bytes.txt")
        ready = directory.appendingPathComponent("ready")
        initializationRelease = directory.appendingPathComponent("initialization-release")
        self.holdInitializationUntilTimeout = holdInitializationUntilTimeout
        try #"""
import json,os,sys,time
from pathlib import Path
release,received,partial,ready,initialization_release=map(Path,sys.argv[1:6])
if sys.argv[6]=="1":
    deadline=time.monotonic()+15
    while not initialization_release.exists() and time.monotonic()<deadline: time.sleep(.005)
instance=os.urandom(16).hex()
ready.with_suffix(".tmp").write_text(instance)
ready.with_suffix(".tmp").replace(ready)
for line in sys.stdin:
    if not line.endswith('\n'):
        partial.write_text(str(len(line)));break
    request=json.loads(line)
    method=request['method'];params=request['params']
    with received.open('a') as output: output.write(json.dumps({'method':method,'id':request['id']})+'\n')
    if method=='initialize': result={'protocolVersion':1,'fixtureInstance':instance}
    elif method=='fixture.holdReply': continue
    elif method=='fixture.echo':
        payload=params['payload'];result={'index':params['index'],'length':len(payload),'first':payload[:1],'last':payload[-1:]}
    else: result={'accepted':True}
    print(json.dumps({'jsonrpc':'2.0','id':request['id'],'result':result}),flush=True)
    if method=='shutdown': break
    if method=='fixture.pause':
        # A failsafe bounds a broken-client test without supplying its expected recovery.
        deadline=time.monotonic()+8
        while not release.exists() and time.monotonic()<deadline: time.sleep(.005)
"""#.write(to: script, atomically: true, encoding: .utf8)
    }
    // Python cold start is fixture setup, not part of the deliberately short pipe
    // deadline. Only initialize may be retried, explicitly, on the same process.
    // The pause and every measured/mutating request are still submitted once.
    func start(_ client: WorkerClient, paused: Bool = true) async throws -> [String] {
        let arguments = [script.path, release.path, received.path, partial.path, ready.path, initializationRelease.path, holdInitializationUntilTimeout ? "1" : "0"]
        do {
            _ = try await client.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: arguments)
        } catch let error as RPCFailure where error.reason == "WORKER_UNAVAILABLE" && error.message.contains("timed out") {
            try Data().write(to: initializationRelease)
            let setupDeadline = ContinuousClock.now.advanced(by: .seconds(15))
            while !FileManager.default.fileExists(atPath: ready.path) {
                guard ContinuousClock.now < setupDeadline, await client.isRunning() else { throw error }
                try await Task.sleep(for: .milliseconds(10))
            }
            let instance = try String(contentsOf: ready, encoding: .utf8)
            guard await client.isRunning() else { throw error }
            let handshake = try await client.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: arguments)
            try #require(handshake["fixtureInstance"].stringValue == instance, "Fixture setup retry must use the same Python process.")
        }
        if paused { _ = try await client.request("fixture.pause") }
        let prefix = try records().map { $0["method"].stringValue }
        let initialization = paused ? Array(prefix.dropLast()) : prefix
        #expect((1...2).contains(initialization.count) && initialization.allSatisfy { $0 == "initialize" })
        if paused { #expect(prefix.last == "fixture.pause") }
        return prefix
    }
    func finish(_ client: WorkerClient) async {
        try? Data().write(to: initializationRelease)
        try? Data().write(to: release)
        await client.shutdown()
        for _ in 0..<200 {
            if !(await client.isRunning()) { break }
            try? await Task.sleep(for: .milliseconds(10))
        }
    }
    func records() throws -> [JSONValue] {
        try String(contentsOf: received, encoding: .utf8).split(separator: "\n").map { try JSONDecoder().decode(JSONValue.self, from: Data($0.utf8)) }
    }
    func remove() { try? FileManager.default.removeItem(at: directory) }
}

extension WorkerKitTests {
    @Test func cancelingAnUnwrittenQueuedFrameKeepsTheConnectionUsable() async throws {
        let fixture = try StalledPipeFixture()
        defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .seconds(5))
        do {
            let startup = try await fixture.start(client)
            let large = Task { try await client.request("browser.importPage", params: .object(["html": .string(String(repeating: "q", count: 1024 * 1024))])) }
            try await Task.sleep(for: .milliseconds(100))
            let canceledSave = Task { try await client.request("save.execute", params: .object(["planId": .string("queued-canceled")])) }
            for _ in 0..<100 {
                if (await client.requestRetention).queued == 2 { break }
                try await Task.sleep(for: .milliseconds(5))
            }
            #expect((await client.requestRetention).queued == 2)
            canceledSave.cancel()
            do { _ = try await canceledSave.value; Issue.record("A canceled queued frame must not be sent.") }
            catch let error as RPCFailure { #expect(error.reason == "WORKER_UNAVAILABLE") }
            #expect((await client.requestRetention).pending == 1)
            try Data().write(to: fixture.release)
            #expect(try await large.value["accepted"].boolValue)
            #expect(try await client.request("workspace.getSnapshot")["accepted"].boolValue)
            await fixture.finish(client)
            #expect(try fixture.records().map { $0["method"].stringValue } == startup + ["browser.importPage", "workspace.getSnapshot", "shutdown"])
        } catch { await fixture.finish(client); throw error }
    }

    @Test func unansweredRequestCountIsBoundedAndCancelReleasesEveryDeadline() async throws {
        let fixture = try StalledPipeFixture()
        defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .seconds(5))
        do {
            let startup = try await fixture.start(client, paused: false)
            let tasks = (0..<128).map { _ in Task { try await client.request("fixture.holdReply") } }
            for _ in 0..<200 {
                if (await client.requestRetention).pending == 128 { break }
                try await Task.sleep(for: .milliseconds(5))
            }
            #expect((await client.requestRetention).pending == 128)
            do { _ = try await client.request("save.execute"); Issue.record("The pending request count must be bounded.") }
            catch let error as RPCFailure { #expect(error.reason == "CLIENT_BACKPRESSURE") }
            for task in tasks { task.cancel() }
            for task in tasks { do { _ = try await task.value } catch { } }
            let state = await client.requestRetention
            #expect(state.pending == 0 && state.queued == 0 && state.bytes == 0 && state.timers == 0)
            await fixture.finish(client)
            #expect(startup == ["initialize"] || startup == ["initialize", "initialize"])
            #expect(try !fixture.records().contains { $0["method"].stringValue == "save.execute" })
        } catch { await fixture.finish(client); throw error }
    }

    @Test func stalledPipeDeadlineIncludesWritingAndDropsQueuedSaveWithoutLateTransmission() async throws {
        let fixture = try StalledPipeFixture(holdInitializationUntilTimeout: true)
        defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .seconds(2))
        do {
            let startup = try await fixture.start(client)
            #expect(FileManager.default.fileExists(atPath: fixture.initializationRelease.path), "The setup gate opens only after initialize times out, before measurement.")
            let started = ContinuousClock.now
            let large = Task { try await client.request("browser.importPage", params: .object(["html": .string(String(repeating: "x", count: 1024 * 1024))])) }
            try await Task.sleep(for: .milliseconds(100))
            let heartbeat = ContinuousClock.now
            #expect(await client.isRunning())
            #expect(heartbeat.duration(to: .now) < .seconds(1), "Actor reads must stay responsive while the pipe is full.")
            let queuedSave = Task { try await client.request("save.execute", params: .object(["planId": .string("never-send")])) }
            for task in [large, queuedSave] {
                do { _ = try await task.value; Issue.record("The stalled request must fail without transmission completing.") }
                catch let error as RPCFailure { #expect(error.reason == "WORKER_UNAVAILABLE") }
            }
            #expect(started.duration(to: .now) < .seconds(4), "The deadline must cover queue and pipe time, before the fixture's eight-second failsafe.")
            let state = await client.requestRetention
            #expect(state.pending == 0 && state.queued == 0 && state.bytes == 0 && state.timers == 0)
            #expect(await client.isRunning(), "Closing a partial frame must not pretend the Worker process has already exited.")
            await fixture.finish(client)
            #expect(try fixture.records().map { $0["method"].stringValue } == startup)
            #expect((Int(try String(contentsOf: fixture.partial, encoding: .utf8)) ?? 0) > 0, "The fixture must observe an incomplete frame, never a late completed request.")
        } catch { await fixture.finish(client); throw error }
    }

    @Test func cancelingAPartialWriteClosesFramingAndShutdownRemainsBounded() async throws {
        let fixture = try StalledPipeFixture()
        defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .seconds(2))
        do {
            let startup = try await fixture.start(client)
            let large = Task { try await client.request("browser.importPage", params: .object(["html": .string(String(repeating: "c", count: 1024 * 1024))])) }
            try await Task.sleep(for: .milliseconds(100))
            #expect((await client.requestRetention).bytes > 0)
            let started = ContinuousClock.now
            large.cancel()
            do { _ = try await large.value; Issue.record("Canceling a partially transmitted request must fail.") }
            catch let error as RPCFailure { #expect(error.reason == "WORKER_UNAVAILABLE") }
            await client.shutdown()
            #expect(started.duration(to: .now) < .seconds(1), "Cancel and shutdown must not await a stalled pipe.")
            let state = await client.requestRetention
            #expect(state.pending == 0 && state.queued == 0 && state.bytes == 0 && state.timers == 0)
            await fixture.finish(client)
            #expect(try fixture.records().map { $0["method"].stringValue } == startup)
            #expect((Int(try String(contentsOf: fixture.partial, encoding: .utf8)) ?? 0) > 0)
        } catch { await fixture.finish(client); throw error }
    }

    @Test func concurrentLargeFramesKeepRequestOrderAndReleaseDeadlineResources() async throws {
        let fixture = try StalledPipeFixture()
        defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .seconds(5))
        do {
            let startup = try await fixture.start(client, paused: false)
            try await withThrowingTaskGroup(of: Void.self) { group in
                for index in 0..<20 {
                    group.addTask {
                        let marker = String(UnicodeScalar(65 + index)!)
                        let result = try await client.request("fixture.echo", params: .object(["index": .number(Double(index)), "payload": .string(String(repeating: marker, count: 128 * 1024))]))
                        #expect(result["index"].intValue == index && result["length"].intValue == 128 * 1024)
                        #expect(result["first"].stringValue == marker && result["last"].stringValue == marker)
                    }
                }
                try await group.waitForAll()
            }
            let state = await client.requestRetention
            #expect(state.pending == 0 && state.queued == 0 && state.bytes == 0 && state.timers == 0)
            let ids = try fixture.records().map { Int($0["id"].stringValue.dropFirst("swift-".count))! }
            let firstID = try #require(ids.first)
            #expect(ids == Array(firstID..<(firstID + startup.count + 20)), "Concurrent requests must remain complete frames in their actor admission order.")
            await fixture.finish(client)
        } catch { await fixture.finish(client); throw error }
    }

    @Test func queuedPayloadBudgetRejectsBeforeSendingAndPreCanceledCallsRetainNothing() async throws {
        let fixture = try StalledPipeFixture()
        defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .seconds(2))
        do {
            let startup = try await fixture.start(client)
            let payload = JSONValue.object(["html": .string(String(repeating: "b", count: 7 * 1024 * 1024))])
            let first = Task { try await client.request("browser.importPage", params: payload) }
            for _ in 0..<100 {
                if (await client.requestRetention).queued == 1 { break }
                try await Task.sleep(for: .milliseconds(5))
            }
            let second = Task { try await client.request("browser.importPage", params: payload) }
            for _ in 0..<100 {
                if (await client.requestRetention).queued == 2 { break }
                try await Task.sleep(for: .milliseconds(5))
            }
            do { _ = try await client.request("save.execute", params: payload); Issue.record("The 16 MiB queue budget must reject another large request.") }
            catch let error as RPCFailure { #expect(error.reason == "CLIENT_BACKPRESSURE") }
            let before = await client.requestRetention
            #expect(before.pending == 2 && before.queued == 2 && before.bytes <= 16 * 1024 * 1024)
            // Cancellation before request registration must not allocate a timer,
            // leave an orphan continuation, or later send the canceled command.
            let canceled = Task {
                withUnsafeCurrentTask { $0?.cancel() }
                return try await client.request("save.execute", params: .object(["planId": .string("pre-canceled")]))
            }
            do { _ = try await canceled.value; Issue.record("A pre-canceled call must fail.") }
            catch is CancellationError { }
            first.cancel(); second.cancel()
            for task in [first, second] { do { _ = try await task.value } catch { } }
            let state = await client.requestRetention
            #expect(state.pending == 0 && state.queued == 0 && state.bytes == 0 && state.timers == 0)
            await fixture.finish(client)
            #expect(try fixture.records().map { $0["method"].stringValue } == startup)
        } catch { await fixture.finish(client); throw error }
    }
}
