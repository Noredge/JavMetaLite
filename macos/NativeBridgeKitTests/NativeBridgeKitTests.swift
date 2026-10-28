import Testing
import Foundation
import AppKit
import WebKit
import SwiftUI
import Combine
#if SWIFT_PACKAGE
@testable import NativeBridgeKit
@testable import JavMetaLiteMac
import WorkerKit
#else
@testable import JavMetaLiteMac
#endif

@Suite(.serialized) @MainActor struct NativeBridgeKitTests {
    private let preferences = TestPreferenceDomain()
    @Test func nativeArtworkCanvasSupportsFitActualSizeAndWheelZoom() {
        _ = NSApplication.shared
        let canvas = ArtworkScrollView(frame: NSRect(x: 0, y: 0, width: 800, height: 600))
        canvas.show(NSImage(size: NSSize(width: 1200, height: 800)), pixelSize: CGSize(width: 1200, height: 800), key: "synthetic")
        canvas.layoutSubtreeIfNeeded()
        #expect(canvas.allowsMagnification && canvas.fitsWindow && canvas.magnification > 0)
        canvas.apply(.actualSize)
        #expect(canvas.magnification == 1 && !canvas.fitsWindow)
        canvas.applyWheelDelta(1)
        #expect(canvas.magnification > 1 && !canvas.fitsWindow)
        let zoomed = canvas.magnification
        canvas.applyWheelDelta(-1)
        #expect(canvas.magnification < zoomed)
        canvas.applyWheelDelta(.infinity)
        #expect(canvas.magnification.isFinite)
        canvas.apply(.fit)
        #expect(canvas.fitsWindow && canvas.magnification.isFinite)
        canvas.apply(.actualSize); canvas.apply(.zoomIn)
        #expect(abs(canvas.magnification - 1.25) < 0.001)
        canvas.onScaleChanged = nil
    }
    @Test func nativeAppIconLoadsAndCustomProfileHasOnlySupportedFieldSources() throws {
        _ = NSApplication.shared
        let url = try #require(AppLocalization.resources.url(forResource: "JavMetaLite", withExtension: "icns"))
        let icon = try #require(NSImage(contentsOf: url))
        #expect(icon.isValid && icon.size.width >= 128 && icon.size.height >= 128)
        let profile = SourceProfileFields.normalized(["titleSource": "r18dev", "makerSource": "invalid", "unknown": "r18dev"])
        #expect(profile.count == 12 && profile["titleSource"] == "r18dev" && profile["makerSource"] == "libredmm" && profile["unknown"] == nil)
    }
    @Test func posterPreviewCropsTheRightHalfAndDoesNotPolluteFullCoverCache() async throws {
        _ = NSApplication.shared
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("JML-crop-\(UUID()).png")
        defer { try? FileManager.default.removeItem(at: url) }
        let bitmap = try #require(NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 401, pixelsHigh: 200,
            bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0))
        let bytes = try #require(bitmap.bitmapData)
        for y in 0..<200 { for x in 0..<401 {
            let offset = y * bitmap.bytesPerRow + x * 4
            bytes[offset] = x < 201 ? 255 : 0; bytes[offset + 1] = 0
            bytes[offset + 2] = x < 201 ? 0 : 255; bytes[offset + 3] = 255
        } }
        try #require(bitmap.representation(using: .png, properties: [:])).write(to: url)
        let before = try Data(contentsOf: url), cache = ArtworkThumbnailCache()
        let cropped = try #require(await cache.image(url: url, maximumPixelSize: 600, posterCrop: true))
        let full = try #require(await cache.image(url: url, maximumPixelSize: 600))
        #expect(cropped.width == 200 && cropped.height == 200)
        #expect(full.width == 401 && full.height == 200)
        let tiff = try #require(cropped.image.tiffRepresentation)
        let pixels = try #require(NSBitmapImageRep(data: tiff))
        let color = try #require(pixels.colorAt(x: 0, y: 0)?.usingColorSpace(.deviceRGB))
        #expect(color.blueComponent > 0.9 && color.redComponent < 0.1, "Right-half pixel RGB: \(color.redComponent), \(color.greenComponent), \(color.blueComponent)")
        #expect(try Data(contentsOf: url) == before)
    }
    @Test func viewerFailureRetryAndLateCanceledResponsePreserveTheCurrentReview() async throws {
        _ = NSApplication.shared
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite viewer retry 合成 \(UUID())")
        let cache = directory.appendingPathComponent("cache", isDirectory: true)
        try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let script = directory.appendingPathComponent("viewer_engine.py")
        let video = directory.appendingPathComponent("DEMO-501.mp4"), nfo = directory.appendingPathComponent("movie.nfo")
        let videoBytes = Data("Synthetic unchanged video".utf8), nfoBytes = Data("<movie><unknown>unchanged</unknown></movie>".utf8)
        try videoBytes.write(to: video); try nfoBytes.write(to: nfo)
        let source = #"""
import json,sys,os
cache=sys.argv[1]
calls=[]; methods=[]; canceled=[]; released=False; operations={}
def emit(id,result):
    print(json.dumps({"jsonrpc":"2.0","id":id,"result":result}),flush=True)
for line in sys.stdin:
    request=json.loads(line); method=request["method"]; id=request["id"]; p=request["params"]; methods.append(method)
    if method=="initialize": emit(id,{"protocolVersion":1,"cacheRoot":cache})
    elif method=="artwork.getPreview":
        calls.append(p); op="image-"+str(len(calls)); operations[op]=p; emit(id,{"operationId":op})
    elif method=="operations.cancel":
        canceled.append(p["operationId"]); emit(id,{"cancelRequested":True})
    elif method=="operations.get":
        op=p["operationId"]; item=operations[op]
        if op=="image-1" or item["jobId"]=="other":
            emit(id,{"operationId":op,"status":"failed","error":{"code":-32000,"message":"Synthetic image HTTP failure","data":{"reason":"ARTWORK_UNAVAILABLE"}}})
        elif op=="image-2" and not released: emit(id,{"operationId":op,"status":"running"})
        else:
            # The old canceled request deliberately completes late. The real
            # store must ignore it even though this fixture ignores cancellation.
            path=os.path.join(cache,"old.png" if op=="image-2" else "current.png")
            emit(id,{"operationId":op,"status":"completed","result":{"path":path,"width":2,"height":3}})
    elif method=="fixture.state": emit(id,{"calls":calls,"methods":methods,"canceled":canceled})
    elif method=="fixture.release": released=True; emit(id,{"released":True})
    elif method=="shutdown": emit(id,{"shutdown":True}); break
"""#
        try source.write(to: script, atomically: true, encoding: .utf8)
        let repository = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        for name in ["old.png", "current.png"] {
            try FileManager.default.copyItem(at: repository.appendingPathComponent("fixtures/macos/synthetic-poster-2x3.png"), to: cache.appendingPathComponent(name))
        }
        let artwork: JSONValue = .object(["artworkId": .string("same-art-id"), "kind": .string("sample"), "source": .string("local")])
        func job(_ id: String) -> MovieJobSnapshot {
            MovieJobSnapshot(.object(["jobId": .string(id), "reviewRevision": .number(17), "metadata": .object(["id": .string("DEMO-501"), "title": .string("Synthetic review")]), "artwork": .array([artwork]), "videoPaths": .array([.string(video.path)])]))
        }
        let store = WorkspaceStore(defaults: preferences.defaults), current = job("current"), other = job("other")
        store.jobs = [current, other]; store.ready = true
        store.selectNavigation([current.jobID]); store.checkedJobIDs = [current.jobID]
        store.drafts[current.jobID] = ["title": "Unflushed synthetic draft"]
        let drafts = store.drafts, checked = store.checkedJobIDs, navigation = store.navigationSelection
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [script.path, cache.path])
            store.openArtwork(artwork, job: current)
            for _ in 0..<100 where store.viewerImageError == nil { try await Task.sleep(for: .milliseconds(10)) }
            #expect(store.viewerImageURL == nil)
            #expect(store.viewerImageError?.contains("Synthetic image HTTP failure") == true, "A terminal failure must leave the loading branch used by the viewer.")
            store.retryViewerArtwork()
            #expect(store.viewerImageError == nil, "An explicit retry immediately returns the current image to loading.")
            var state: JSONValue = .null
            for _ in 0..<100 {
                state = try await store.worker.request("fixture.state")
                if state["calls"].arrayValue.count == 2 { break }
                try await Task.sleep(for: .milliseconds(10))
            }
            #expect(state["calls"].arrayValue.count == 2)
            store.closeArtworkViewer(); store.showingImage = false
            store.openArtwork(artwork, job: current)
            for _ in 0..<100 where store.viewerImageURL == nil { try await Task.sleep(for: .milliseconds(10)) }
            #expect(store.viewerImageURL?.lastPathComponent == "current.png", "Closing a pending viewer must allow a fresh request when reopened.")
            _ = try await store.worker.request("fixture.release")
            try await Task.sleep(for: .milliseconds(180))
            #expect(store.viewerImageURL?.lastPathComponent == "current.png", "A canceled older request must not replace the retried image.")
            #expect(store.viewerImageError == nil)
            store.openArtwork(artwork, job: other)
            for _ in 0..<100 where store.viewerImageError == nil { try await Task.sleep(for: .milliseconds(10)) }
            #expect(store.viewerImageURL == nil && store.viewerImageError != nil, "The same artwork ID in another job cannot reuse the first job's image.")
            #expect(store.artworkPaths[store.artworkKey(artwork, jobID: current.jobID)]?.lastPathComponent == "current.png")
            state = try await store.worker.request("fixture.state")
            #expect(state["calls"].arrayValue.map { $0["jobId"].stringValue } == ["current", "current", "current", "other"])
            #expect(state["calls"].arrayValue.allSatisfy { $0["artworkId"] == artwork["artworkId"] && $0["async"].boolValue })
            #expect(state["canceled"].arrayValue.contains(.string("image-2")))
            #expect(state["methods"].arrayValue.allSatisfy { ["initialize", "artwork.getPreview", "operations.get", "operations.cancel", "fixture.state", "fixture.release"].contains($0.stringValue) })
            #expect(store.jobs.map(\.reviewRevision) == [17, 17]); #expect(store.jobs.map(\.metadata) == [current.metadata, other.metadata])
            #expect(store.drafts == drafts && store.checkedJobIDs == checked && store.navigationSelection == navigation)
            #expect(try Data(contentsOf: video) == videoBytes); #expect(try Data(contentsOf: nfo) == nfoBytes)
            store.closeArtworkViewer(); await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }

    @Test func viewerSourceSwitchCancelsOnlyViewerOwnedRequestsAndKeepsCachedImages() async throws {
        _ = NSApplication.shared
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JML-viewer-source-\(UUID())")
        let cache = directory.appendingPathComponent("cache")
        try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        func art(_ id: String, _ source: String) -> JSONValue {
            .object(["artworkId": .string(id), "kind": .string("sample"), "source": .string(source), "contentKey": .string(id)])
        }
        let local = art("local", "local"), viewer = art("viewer", "libredmm"), detail = art("detail", "libredmm")
        let value: JSONValue = .object(["jobId": .string("current"), "reviewRevision": .number(17),
            "metadata": .object(["id": .string("DEMO-501"), "title": .string("Synthetic review")]),
            "artwork": .array([local, viewer, detail])])
        let script = directory.appendingPathComponent("engine.py"), jobFile = directory.appendingPathComponent("job.json")
        try JSONEncoder().encode(value).write(to: jobFile)
        let source = #"""
import json,sys
cache=sys.argv[1];job=json.load(open(sys.argv[2]));operations={};canceled=[]
def emit(id,result):print(json.dumps({"jsonrpc":"2.0","id":id,"result":result}),flush=True)
for line in sys.stdin:
    r=json.loads(line);m=r["method"];id=r["id"];p=r["params"]
    if m=="initialize":emit(id,{"protocolVersion":1,"cacheRoot":cache})
    elif m=="artwork.getPreview":
        op=p["artworkId"];operations[op]=p;emit(id,{"operationId":op})
    elif m=="operations.get":emit(id,{"status":"canceled" if p["operationId"] in canceled else "running"})
    elif m=="operations.cancel":canceled.append(p["operationId"]);emit(id,{"cancelRequested":True})
    elif m=="artwork.selectSource":emit(id,job)
    elif m=="fixture.state":emit(id,{"operations":operations,"canceled":canceled})
    elif m=="shutdown":emit(id,{"shutdown":True});break
"""#
        try source.write(to: script, atomically: true, encoding: .utf8)
        let store = WorkspaceStore(defaults: preferences.defaults), job = MovieJobSnapshot(value), localURL = cache.appendingPathComponent("local.png")
        let repository = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        try FileManager.default.copyItem(at: repository.appendingPathComponent("fixtures/macos/synthetic-poster-2x3.png"), to: localURL)
        store.jobs = [job]; store.ready = true; store.selectNavigation([job.jobID]); store.checkedJobIDs = [job.jobID]
        store.artworkPaths[store.artworkKey(local, jobID: job.jobID)] = localURL
        let navigation = store.navigationSelection, checks = store.checkedJobIDs
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [script.path, cache.path, jobFile.path])
            store.loadArtwork(detail, jobID: job.jobID, scope: "detail")
            store.openArtwork(viewer, job: job)
            var state: JSONValue = .null
            for _ in 0..<100 {
                state = try await store.worker.request("fixture.state")
                if state["operations"]["viewer"]["artworkId"] == viewer["artworkId"] && state["operations"]["detail"]["artworkId"] == detail["artworkId"] { break }
                try await Task.sleep(for: .milliseconds(10))
            }
            #expect(state["operations"]["viewer"]["artworkId"] == viewer["artworkId"])
            #expect(state["operations"]["detail"]["artworkId"] == detail["artworkId"])
            store.selectArtworkSource("local", jobID: job.jobID, fromViewer: true)
            for _ in 0..<100 {
                state = try await store.worker.request("fixture.state")
                if state["canceled"].arrayValue.contains(.string("viewer")) { break }
                try await Task.sleep(for: .milliseconds(10))
            }
            #expect(state["canceled"].arrayValue.contains(.string("viewer")), "Source changes must cancel the old viewer-owned download.")
            #expect(!state["canceled"].arrayValue.contains(.string("detail")), "The main page still owns its pending image.")
            #expect(store.viewerSource == "local" && store.viewerImageURL == localURL)
            #expect(store.artworkPaths[store.artworkKey(local, jobID: job.jobID)] == localURL)
            #expect(store.navigationSelection == navigation && store.checkedJobIDs == checks)
            #expect(store.jobs.first?.reviewRevision == 17 && !store.working)
            store.selectNavigation([]) // Cancel the independent detail request before shutting down.
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_SEARCH_TEST_HOST"] != nil))
    func canceledPreviewRestoresEditingAndRetryWithoutAlertOrSaveReplay() async throws {
        _ = NSApplication.shared
        let host = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_SEARCH_TEST_HOST"])
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("JML-preview-cancel-\(UUID())")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let video = root.appendingPathComponent("DEMO-501.mp4"), nfo = root.appendingPathComponent("movie.nfo")
        let videoBytes = Data("SYNTHETIC VIDEO".utf8)
        let nfoBytes = Data("<movie><id>DEMO-501</id><title>Original</title><customNode>KEEP</customNode></movie>".utf8)
        try videoBytes.write(to: video); try nfoBytes.write(to: nfo)
        let gate = root.appendingPathComponent("hold-preview"), config = root.appendingPathComponent("config.json")
        try Data("hold".utf8).write(to: gate)
        try JSONEncoder().encode(JSONValue.object(["previewReadGateFile": .string(gate.path), "ignorePreviewReadCancellation": .bool(true)])).write(to: config)
        let store = WorkspaceStore(worker: WorkerClient(requestTimeout: .seconds(2)), defaults: preferences.defaults)
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: host), arguments: ["--data-root", root.appendingPathComponent("state").path, "--fixture-config", config.path])
            _ = try await finish(try await store.worker.request("workspace.importInputs", params: .object(["paths": .array([.string(video.path)])])), client: store.worker)
            store.ready = true; try await store.refresh()
            let job = try #require(store.jobs.first)
            store.checkedJobIDs = [job.jobID]; store.selectNavigation([job.jobID]); store.includeIDInTitle = false
            store.drafts[job.jobID] = ["title": "Canceled preview draft"]
            store.makePreview()
            for _ in 0..<200 {
                let requests = try await store.worker.request("fixture.requests")
                if requests["requests"].arrayValue.contains(where: { $0["kind"].stringValue == "previewReadStarted" }) { break }
                try await Task.sleep(for: .milliseconds(10))
            }
            #expect(store.working && store.operationID != nil)
            store.cancel()
            for _ in 0..<200 where store.working { try await Task.sleep(for: .milliseconds(10)) }
            #expect(!store.working && !store.statusUnconfirmed && store.operationID == nil)
            #expect(store.errorMessage == nil && store.preview == nil && store.previewStale)
            #expect(store.resultMessage == tr("operationCanceled"))
            #expect(store.jobs.first?.metadata["title"].stringValue == "Canceled preview draft")
            #expect(store.checkedJobIDs == [job.jobID] && store.selectedID == job.jobID)
            #expect(FileManager.default.fileExists(atPath: gate.path), "Editing recovers before the slow read is released.")
            #expect(try Data(contentsOf: video) == videoBytes && Data(contentsOf: nfo) == nfoBytes)
            store.drafts[job.jobID] = ["title": "Edit after cancel"]
            try FileManager.default.removeItem(at: gate)
            for _ in 0..<200 {
                let requests = try await store.worker.request("fixture.requests")
                if requests["requests"].arrayValue.contains(where: { $0["kind"].stringValue == "previewReadReturned" }) { break }
                try await Task.sleep(for: .milliseconds(10))
            }
            store.makePreview()
            for _ in 0..<200 where store.working { try await Task.sleep(for: .milliseconds(10)) }
            #expect(store.preview != nil && store.preview?.canExecute == true, "Retry presents a fresh replacement plan for confirmation and must not write before that confirmation.")
            #expect(store.jobs.first?.metadata["title"].stringValue == "Edit after cancel")
            #expect(!store.statusUnconfirmed && store.errorMessage == nil)
            #expect(try Data(contentsOf: video) == videoBytes && Data(contentsOf: nfo) == nfoBytes)
            store.preview = nil; await store.worker.shutdown()
        } catch {
            try? FileManager.default.removeItem(at: gate)
            await store.worker.shutdown(); throw error
        }
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil)) func expiredOperationReceiptUsesReadOnlyRecoveryAndKeepsAnActiveOperationUnconfirmed() async throws {
        _ = NSApplication.shared
        let executable = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite receipt 合成 \(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let video = directory.appendingPathComponent("DEMO-301.mp4")
        let nfo = directory.appendingPathComponent("movie.nfo")
        let videoBytes = Data("Synthetic receipt video".utf8)
        let nfoBytes = Data("<movie><title>Synthetic original</title><num>DEMO-301</num><unknown>Retain</unknown></movie>".utf8)
        try videoBytes.write(to: video); try nfoBytes.write(to: nfo)
        let store = WorkspaceStore(defaults: preferences.defaults), notifications = OperationNotificationRecorder()
        await store.worker.setNotificationHandler { notifications.record($0) }
        var permittedOperations: Set<String> = []
        do {
            _ = try await store.worker.start(executable: URL(fileURLWithPath: executable), arguments: ["--data-root", directory.appendingPathComponent("state").path])
            store.ready = true
            let imported = try await store.worker.request("workspace.importInputs", params: .object(["paths": .array([.string(video.path)]), "recursive": .bool(false)]))
            permittedOperations.insert(imported["operationId"].stringValue)
            _ = try await finish(imported, client: store.worker); try await store.refresh()
            let job = try #require(store.jobs.first)
            let unchangedJob = try await store.worker.request("jobs.get", params: .object(["jobId": .string(job.jobID)]))
            store.drafts[job.jobID] = ["title": "Synthetic local draft"]
            store.checkedJobIDs = [job.jobID]
            let drafts = store.drafts, navigation = store.navigationSelection, checked = store.checkedJobIDs
            store.preview = SavePreview(.object(["planId": .string("old-preview"), "jobId": .string(job.jobID), "canExecute": .bool(true)]))
            let oldID = "synthetic-expired-operation"
            do {
                _ = try await store.waitOperation(.object(["operationId": .string(oldID)]))
                Issue.record("A missing operation receipt must not appear completed.")
            } catch let failure as RPCFailure { #expect(failure.reason == "OPERATION_NOT_FOUND") }
            #expect(store.statusUnconfirmed); #expect(store.operationID == oldID)
            let firstCheck = try #require(store.checkStatus()); await firstCheck.value
            #expect(!store.statusUnconfirmed); #expect(store.operationID == nil); #expect(store.preview == nil)
            #expect(store.resultMessage == tr("operationReceiptUnavailable")); #expect(store.operationHadProblems)
            #expect(store.drafts == drafts); #expect(store.navigationSelection == navigation); #expect(store.checkedJobIDs == checked)
            #expect(try await store.worker.request("jobs.get", params: .object(["jobId": .string(job.jobID)])) == unchangedJob)
            #expect(try Data(contentsOf: video) == videoBytes); #expect(try Data(contentsOf: nfo) == nfoBytes)
            store.selectNavigation([]); store.operationID = oldID; store.statusUnconfirmed = true
            let emptySelectionCheck = try #require(store.checkStatus()); await emptySelectionCheck.value
            #expect(store.navigationSelection.isEmpty && store.selectedID == nil, "Read-only recovery must not invent a current selection.")
            store.selectNavigation(navigation)
            // Exercise the real confirmation failure path for a consumed scan;
            // receipt recovery must close this submitted review, never resubmit it.
            store.installDiscovery(.object(["discoveryId": .string("synthetic-consumed-discovery"), "items": .array([
                .object(["itemId": .string("already-submitted"), "canImport": .bool(true), "primaryPath": .string(video.path)])
            ])]))
            let staleConfirmation = try #require(store.confirmDiscovery()); await staleConfirmation.value
            #expect(store.discoveryReview?.submitted == true)
            store.operationID = oldID; store.statusUnconfirmed = true
            let submittedDiscoveryCheck = try #require(store.checkStatus()); await submittedDiscoveryCheck.value
            #expect(store.discoveryReview == nil); #expect(store.confirmDiscovery() == nil)
            #expect(store.drafts == drafts); #expect(store.navigationSelection == navigation); #expect(store.checkedJobIDs == checked)

            // A real mutable preview hashes the complete sparse source. This is
            // not a sleep/mock operation and never calls save.execute.
            let largeDirectory = directory.appendingPathComponent("large", isDirectory: true)
            try FileManager.default.createDirectory(at: largeDirectory, withIntermediateDirectories: true)
            let largeVideo = largeDirectory.appendingPathComponent("DEMO-302.mp4")
            #expect(FileManager.default.createFile(atPath: largeVideo.path, contents: nil))
            let handle = try FileHandle(forWritingTo: largeVideo)
            try handle.truncate(atOffset: 2 * 1024 * 1024 * 1024); try handle.close()
            let attributes = try FileManager.default.attributesOfItem(atPath: largeVideo.path)
            let secondImport = try await store.worker.request("workspace.importInputs", params: .object(["paths": .array([.string(largeVideo.path)]), "recursive": .bool(false)]))
            permittedOperations.insert(secondImport["operationId"].stringValue)
            _ = try await finish(secondImport, client: store.worker); try await store.refresh()
            let largeJob = try #require(store.jobs.first(where: { $0.jobID != job.jobID }))
            let active = try await store.worker.request("save.preview", params: .object(["jobId": .string(largeJob.jobID), "expectedReviewRevision": .number(Double(largeJob.reviewRevision)), "async": .bool(true), "options": .object(["targetMode": .string("SourceNumberFolder"), "renameVideo": .bool(true)])]))
            let activeID = active["operationId"].stringValue; permittedOperations.insert(activeID)
            let before = try await store.worker.request("workspace.getSnapshot")
            #expect(before["activeOperations"].arrayValue.contains { $0["operationId"].stringValue == activeID && !$0["readOnly"].boolValue }, "The active-operation branch must be observed, not inferred from timing.")
            store.operationID = oldID; store.statusUnconfirmed = true
            let activeCheck = try #require(store.checkStatus()); await activeCheck.value
            #expect(store.statusUnconfirmed); #expect(store.operationID == oldID, "Do not guess that another active operation is the lost one.")
            #expect(store.drafts == drafts); #expect(store.navigationSelection == navigation); #expect(store.checkedJobIDs == checked)
            _ = try await store.worker.request("operations.cancel", params: .object(["operationId": .string(activeID)]))
            var terminal = false
            for _ in 0..<200 {
                let status = try await store.worker.request("operations.get", params: .object(["operationId": .string(activeID)]))
                if status["status"].stringValue != "running" { terminal = true; break }
                try await Task.sleep(for: .milliseconds(10))
            }
            #expect(terminal)
            let lastCheck = try #require(store.checkStatus()); await lastCheck.value
            #expect(!store.statusUnconfirmed && store.operationID == nil)
            #expect(store.resultMessage == tr("operationReceiptUnavailable"))
            #expect(store.drafts == drafts); #expect(store.navigationSelection == navigation); #expect(store.checkedJobIDs == checked)
            #expect(try Data(contentsOf: video) == videoBytes); #expect(try Data(contentsOf: nfo) == nfoBytes)
            let afterAttributes = try FileManager.default.attributesOfItem(atPath: largeVideo.path)
            #expect(afterAttributes[.size] as? UInt64 == attributes[.size] as? UInt64)
            #expect(afterAttributes[.modificationDate] as? Date == attributes[.modificationDate] as? Date)
            #expect(!FileManager.default.fileExists(atPath: largeDirectory.appendingPathComponent("DEMO-302").path))
            #expect(!FileManager.default.fileExists(atPath: largeDirectory.appendingPathComponent("movie.nfo").path))
            try await Task.sleep(for: .milliseconds(50))
            #expect(notifications.operationIDs == permittedOperations, "Observe every fixture operation and no replacement operation from recovery.")
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil)) func discoverySheetCancellationAndExactConfirmationPreserveTheExistingReview() async throws {
        _ = NSApplication.shared
        let executable = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite discovery 合成 \(UUID())")
        let incoming = directory.appendingPathComponent("DEMO-999", isDirectory: true)
        try FileManager.default.createDirectory(at: incoming, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let oldVideo = directory.appendingPathComponent("DEMO-100.mp4")
        let recognizedVideo = incoming.appendingPathComponent("DEMO-101.mp4")
        let unknownVideo = incoming.appendingPathComponent("vacation.mp4")
        for url in [oldVideo, recognizedVideo, unknownVideo] { try Data("Synthetic discovery bytes".utf8).write(to: url) }
        let store = WorkspaceStore(defaults: preferences.defaults)
        let parent = NSWindow(contentRect: NSRect(x: 80, y: 80, width: 920, height: 620), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        parent.isReleasedWhenClosed = false
        defer { if let sheet = parent.attachedSheet { parent.endSheet(sheet) }; parent.close() }
        do {
            let initialized = try await store.worker.start(executable: URL(fileURLWithPath: executable), arguments: ["--data-root", directory.appendingPathComponent("state").path])
            store.capabilities = initialized["capabilities"]; store.ready = true
            #expect(store.capabilities["discoverInputs"].boolValue)
            _ = try await finish(try await store.worker.request("workspace.importInputs", params: .object(["paths": .array([.string(oldVideo.path)]), "recursive": .bool(false)])), client: store.worker)
            try await store.refresh()
            let oldJob = try #require(store.jobs.first)
            let oldRaw = try await store.worker.request("jobs.get", params: .object(["jobId": .string(oldJob.jobID)]))
            store.drafts[oldJob.jobID] = ["title": "Synthetic unsaved draft"]
            store.checkedJobIDs = [oldJob.jobID]
            let oldNavigation = store.navigationSelection, oldDrafts = store.drafts
            parent.contentView = NSHostingView(rootView: WorkspaceView().environmentObject(store))
            store.workspaceWindow = parent
            parent.makeKeyAndOrderFront(nil)
            await store.importPaths([incoming.path])
            let firstReview = try #require(store.discoveryReview)
            #expect(firstReview.discovery.items.count == 2)
            #expect(firstReview.selectedIDs.count == 2)
            #expect(firstReview.discovery.items.first(where: { $0.primaryPath.hasSuffix("vacation.mp4") })?.movieID == "")
            for _ in 0..<40 where parent.attachedSheet == nil { try await Task.sleep(for: .milliseconds(25)) }
            let sheet = try #require(parent.attachedSheet, "The production WorkspaceView presents the real discovery sheet.")
            #expect(sheet.frame.width <= parent.frame.width)
            #expect(sheet.frame.height <= parent.frame.height)
            store.selectRecognizedDiscovered()
            #expect(store.discoveryReview?.selectedIDs.count == 1)
            store.deselectAllDiscovered(); #expect(store.confirmDiscovery() == nil)
            let discard = try #require(store.discardDiscovery()); await discard.value
            #expect(store.discoveryReview == nil)
            #expect(store.jobs.map(\.jobID) == [oldJob.jobID]); #expect(store.navigationSelection == oldNavigation)
            #expect(store.checkedJobIDs == [oldJob.jobID]); #expect(store.drafts == oldDrafts)
            #expect(try await store.worker.request("jobs.get", params: .object(["jobId": .string(oldJob.jobID)])) == oldRaw)
            for _ in 0..<40 where parent.attachedSheet != nil { try await Task.sleep(for: .milliseconds(25)) }
            await store.importPaths([incoming.path])
            let secondReview = try #require(store.discoveryReview)
            #expect(secondReview.discovery.discoveryID != firstReview.discovery.discoveryID)
            let manual = try #require(secondReview.discovery.items.first(where: { $0.movieID.isEmpty }))
            store.deselectAllDiscovered(); store.selectDiscovered(manual.itemID, selected: true)
            let confirming = try #require(store.confirmDiscovery())
            #expect(store.working && store.discoveryReview?.submitted == true)
            #expect(store.confirmDiscovery() == nil); #expect(store.discardDiscovery() == nil)
            store.selectAllDiscovered(); #expect(store.discoveryReview?.selectedIDs == [manual.itemID])
            await confirming.value
            #expect(store.errorMessage == nil); #expect(store.discoveryErrorMessage == nil)
            #expect(store.discoveryReview == nil)
            #expect(store.jobs.count == 2)
            #expect(store.jobs.contains { $0.videoPaths.map { URL(fileURLWithPath: $0).resolvingSymlinksInPath().path } == [unknownVideo.resolvingSymlinksInPath().path] })
            #expect(!store.jobs.contains { $0.videoPaths.map { URL(fileURLWithPath: $0).resolvingSymlinksInPath().path }.contains(recognizedVideo.resolvingSymlinksInPath().path) })
            #expect(store.navigationSelection == oldNavigation); #expect(store.checkedJobIDs == [oldJob.jobID]); #expect(store.drafts == oldDrafts)
            #expect(try await store.worker.request("jobs.get", params: .object(["jobId": .string(oldJob.jobID)])) == oldRaw)
            for url in [oldVideo, recognizedVideo, unknownVideo] { #expect(try Data(contentsOf: url) == Data("Synthetic discovery bytes".utf8)) }
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }

    @Test func repeatedNativeQueueSelectionDoesNotPublishOrCommitComposition() async throws {
        _ = NSApplication.shared
        let store = WorkspaceStore(defaults: preferences.defaults)
        let first = MovieJobSnapshot(.object(["jobId": .string("first"), "reviewRevision": .number(9), "metadata": .object(["id": .string("DEMO-001"), "title": .string("Synthetic original")])]))
        let second = MovieJobSnapshot(.object(["jobId": .string("second"), "reviewRevision": .number(13), "metadata": .object(["id": .string("DEMO-002"), "title": .string("Other synthetic original")])]))
        store.jobs = [first, second]; store.selectNavigation([first.jobID])
        store.checkedJobIDs = [second.jobID]
        store.drafts[first.jobID] = ["plot": "Existing synthetic draft"]
        let session = store.editorSession(field: "title", jobID: first.jobID)
        session.onChange = { store.edit("title", value: $0, job: first) }
        session.configure(value: first.metadata["title"].stringValue, label: "Synthetic title", editable: true, multiline: false)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 420, height: 100), styleMask: [.titled], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        session.scrollView.frame = window.contentView!.bounds; window.contentView?.addSubview(session.scrollView)
        defer { window.orderOut(nil); session.scrollView.removeFromSuperview(); window.close() }
        #expect(window.makeFirstResponder(session.textView))
        session.textView.setMarkedText("合成组合文字", selectedRange: NSRange(location: 6, length: 0), replacementRange: NSRange(location: 0, length: session.textView.string.utf16.count))
        #expect(session.textView.hasMarkedText())
        var publications = 0
        let observation = store.objectWillChange.sink { publications += 1 }
        defer { observation.cancel() }
        for _ in 0..<100 { store.selectQueueNavigation([first.jobID]); store.selectNavigation([first.jobID]) }
        #expect(publications == 0, "Repeated native List callbacks must not invalidate the SwiftUI view graph.")
        #expect(session.textView.hasMarkedText(), "A no-op selection must not prematurely commit composition.")
        #expect(store.drafts[first.jobID] == ["plot": "Existing synthetic draft"])

        // The command-line test runner cannot acquire a system key window.
        // Commit through the production native session before testing navigation;
        // automatic key-window composition handling needs separate native UI QA.
        #expect(session.commitComposition())
        #expect(!session.textView.hasMarkedText())
        #expect(store.drafts[first.jobID] == ["plot": "Existing synthetic draft", "title": "合成组合文字"])
        publications = 0
        store.selectQueueNavigation([second.jobID])
        #expect(publications == 0 && store.selectedID == first.jobID)
        await withCheckedContinuation { continuation in DispatchQueue.main.async { continuation.resume() } }
        #expect(store.drafts[second.jobID] == nil)
        #expect(store.navigationSelection == [second.jobID] && store.selectedID == second.jobID)
        #expect(publications > 0, "A real native selection commits outside the List update callback.")
        #expect(store.checkedJobIDs == [second.jobID])
        #expect(store.jobs.map(\.reviewRevision) == [9, 13])

        // Model the empty List selection with an implicit displayed review.
        store.navigationSelection = []
        publications = 0
        store.selectQueueNavigation([])
        #expect(publications == 0 && store.selectedID == second.jobID)
        store.selectNavigation([])
        #expect(publications > 0 && store.selectedID == nil, "An explicit empty selection still clears the implicit review.")
        publications = 0
        store.selectNavigation([]); store.selectQueueNavigation([])
        #expect(publications == 0)
        #expect(store.checkedJobIDs == [second.jobID])
        #expect(store.jobs.map(\.reviewRevision) == [9, 13])
        #expect(store.drafts[first.jobID]?["title"] == "合成组合文字")
    }

    @Test func deferredQueueCallbacksCoalesceAndSpaceKeepsItsIntendedTarget() async {
        _ = NSApplication.shared
        let store = WorkspaceStore(defaults: preferences.defaults); store.ready = true
        store.jobs = ["a", "b", "c"].enumerated().map { index, id in
            MovieJobSnapshot(.object(["jobId": .string(id), "reviewRevision": .number(Double(index + 7)), "metadata": .object(["title": .string("Synthetic " + id)])]))
        }
        store.selectNavigation(["a"]); store.drafts["a"] = ["title": "Unflushed original draft"]
        let drafts = store.drafts, revisions = store.jobs.map(\.reviewRevision)
        var publications = 0
        let observation = store.objectWillChange.sink { publications += 1 }
        defer { observation.cancel() }
        store.selectQueueNavigation(["b"]); store.selectQueueNavigation(["a"])
        #expect(store.queueNavigationSelection == ["a"] && publications == 0)
        await withCheckedContinuation { continuation in DispatchQueue.main.async { continuation.resume() } }
        #expect(store.selectedID == "a" && publications == 0)

        store.selectQueueNavigation(["b"]); store.toggleQueueNavigationChecks()
        store.selectQueueNavigation(["c"])
        #expect(store.selectedID == "a" && store.checkedJobIDs.isEmpty && publications == 0)
        await withCheckedContinuation { continuation in DispatchQueue.main.async { continuation.resume() } }
        #expect(store.selectedID == "c" && store.checkedJobIDs == ["b"])
        #expect(publications > 0)
        store.toggleQueueNavigationChecks(); store.toggleQueueNavigationChecks()
        await withCheckedContinuation { continuation in DispatchQueue.main.async { continuation.resume() } }
        #expect(store.checkedJobIDs == ["b"], "Two rapid Space presses must cancel each other.")

        store.selectQueueNavigation(["b"]); store.toggleQueueNavigationChecks()
        store.selectNavigation(["a"])
        await withCheckedContinuation { continuation in DispatchQueue.main.async { continuation.resume() } }
        #expect(store.selectedID == "a" && store.checkedJobIDs == ["b"], "Programmatic navigation invalidates stale UI work.")
        store.selectQueueNavigation(["b"]); store.toggleQueueNavigationChecks()
        #expect(!store.flushPendingQueueUpdates(commit: { false }))
        #expect(store.selectedID == "a" && store.queueNavigationSelection == ["a"] && store.checkedJobIDs == ["b"], "Rejected composition commit retains accepted selection and checkmarks.")

        store.searchText = "Synthetic a"
        store.selectQueueNavigation(["b"]); store.selectQueueNavigation([])
        #expect(store.queueNavigationSelection == ["b"], "Filtering cannot clear the pending review even when the accepted row remains visible.")
        #expect(store.flushPendingQueueUpdates())
        #expect(store.selectedID == "b")
        store.searchText = ""
        store.capabilities = .object(["confirmParts": .bool(true)])
        store.ready = true; store.checkedJobIDs = ["a", "b"]
        store.selectQueueNavigation(["c"])
        store.reviewParts()
        #expect(store.partsReviewIDs == ["a", "b"] && store.checkedJobIDs == ["a", "b"], "A command flushes pending navigation and Space before capturing its targets.")

        store.selectNavigation(["a"])
        store.selectQueueNavigation(["c"])
        store.jobs.removeAll { $0.jobID == "c" }
        await withCheckedContinuation { continuation in DispatchQueue.main.async { continuation.resume() } }
        #expect(!store.navigationSelection.contains("c"), "A pending callback cannot resurrect a removed job.")
        #expect(store.drafts == drafts)
        #expect(store.jobs.map(\.reviewRevision) == Array(revisions.prefix(2)))
    }

    @Test func queueSearchFollowsVisibleDraftsAndOriginalFileNamesWithoutChangingReviewScope() {
        _ = NSApplication.shared
        let store = WorkspaceStore(defaults: preferences.defaults)
        let first = MovieJobSnapshot(.object([
            "jobId": .string("first"), "reviewRevision": .number(17),
            "metadata": .object(["id": .string("DEMO-101"), "title": .string("Previously loaded title")]),
            "videoPaths": .array([.string("/private/tmp/Synthetic/DEMO-101-UC.mp4")])
        ]))
        let second = MovieJobSnapshot(.object([
            "jobId": .string("second"), "reviewRevision": .number(23),
            "metadata": .object(["id": .string("DEMO-102"), "title": .string("Another title")]),
            "videoPaths": .array([.string("/private/tmp/Synthetic/DEMO-102.mp4")])
        ]))
        store.jobs = [first, second]; store.ready = true
        store.selectNavigation([first.jobID]); store.checkedJobIDs = [second.jobID]
        store.edit("title", value: "草稿标题 Draft 北岸", job: first)
        let drafts = store.drafts
        for query in ["北岸", "draft", "  北岸\n", "-uc.mp4", "DEMO-101-UC"] {
            store.searchText = query
            #expect(store.visibleJobs.map(\.jobID) == [first.jobID], "Search must match the visible draft or original filename: \(query)")
        }
        store.searchText = "not a matching file"
        #expect(store.visibleJobs.isEmpty)
        store.selectQueueNavigation([])
        #expect(store.selectedID == first.jobID && store.checkedJobIDs == [second.jobID])
        store.searchText = "DEMO-102"
        #expect(store.visibleJobs.map(\.jobID) == [second.jobID])
        #expect(store.drafts == drafts && store.actionTargetJobs.map(\.jobID) == [second.jobID])
        #expect(store.jobs.map(\.reviewRevision) == [17, 23] && !store.working)
    }

    @Test func queueFilteringCheckingAndCurrentReviewRemainIndependent() {
        _ = NSApplication.shared
        let store = WorkspaceStore(defaults: preferences.defaults)
        let recognized = MovieJobSnapshot(.object(["jobId": .string("FNS-121"), "metadata": .object(["id": .string("FNS-121")]), "saveState": .string("Idle"), "coverQuality": .object(["state": .string("lowResolution"), "width": .number(147), "height": .number(200)])]))
        let unknown = MovieJobSnapshot(.object(["jobId": .string("no-id"), "metadata": .object(["title": .string("Synthetic unrecognized")]), "saveState": .string("Idle")]))
        let saved = MovieJobSnapshot(.object(["jobId": .string("saved"), "metadata": .object(["id": .string("DEMO-007")]), "saveState": .string("Completed")]))
        store.jobs = [recognized, unknown, saved]
        store.selectNavigation([saved.jobID]); store.checkedJobIDs = [unknown.jobID, saved.jobID]
        store.queueCategory = .issues
        store.selectQueueNavigation([]) // A filtered-out row must not clear the active editor.
        #expect(store.selectedJob?.jobID == saved.jobID)
        #expect(store.checkedJobIDs == [unknown.jobID, saved.jobID])
        #expect(store.visibleJobs.map(\.jobID) == [recognized.jobID, unknown.jobID])
        #expect(store.categoryJobs(.pending).count == 2)
        #expect(store.categoryJobs(.issues).count == 2, "Categories overlap; their counts are not forced to sum to All.")
        #expect(store.hasLowResolutionCover(recognized), "The FNS-121 synthetic 147×200 fanart is an issue before opening a preview.")
        #expect(store.artworkPaths.isEmpty)
        store.selectRecognized()
        #expect(store.checkedJobIDs == [recognized.jobID, saved.jobID])
        #expect(store.queueCategory == .issues)
        store.selectCategory(.pending)
        #expect(store.checkedJobIDs == [recognized.jobID, unknown.jobID])
        #expect(store.selectedJob?.jobID == saved.jobID)
        store.jobs = [saved]; store.selectCategory(.issues)
        #expect(store.checkedJobIDs.isEmpty, "Selecting an empty category replaces the previous checkmarks.")
        #expect(store.queueCategory == .issues)
        #expect(store.sourceMode == "libredmm")
        store.ready = true
        #expect(!store.canRetrySources, "A successful or never-searched job has no failed source to retry.")
        let failed = MovieJobSnapshot(.object(["jobId": .string("failed"), "metadata": .object(["id": .string("DEMO-008")]), "hasFailedSources": .bool(true)]))
        store.jobs = [failed]; store.selectNavigation([failed.jobID]); store.checkedJobIDs = [failed.jobID]
        #expect(store.canRetrySources)
        store.working = true; #expect(!store.canRetrySources)
    }

    @Test func scanWarningsUseCompleteClassifiedDetailsWithoutChangingReviewState() {
        _ = NSApplication.shared
        let store = WorkspaceStore(defaults: preferences.defaults)
        let job = MovieJobSnapshot(.object(["jobId": .string("synthetic"), "metadata": .object(["id": .string("DEMO-001")]), "reviewRevision": .number(5)]))
        store.jobs = [job]; store.selectNavigation([job.jobID]); store.checkedJobIDs = [job.jobID]
        store.drafts[job.jobID] = ["title": "Unsaved synthetic title"]
        let inaccessible = "/synthetic/" + String(repeating: "long-directory/", count: 400)
        let unreadable = "/synthetic/ordinary-io-failure"
        let denied = "Synthetic access error", failed = "Synthetic read error"
        store.applyImportOutcome(.object([
            "importedJobIds": .array([.string(job.jobID)]), "duplicateJobIds": .array([]),
            "diagnostics": .array([
                .object(["kind": .string("AccessDenied"), "path": .string(inaccessible), "message": .string(denied)]),
                .object(["kind": .string("ReadFailed"), "path": .string(unreadable), "message": .string(failed)])
            ]),
            "warnings": .array([.string(inaccessible + ": " + denied), .string("Synthetic extra load warning")])
        ]))
        #expect(store.errorMessage == tr("importPartial"))
        #expect(store.errorMessage?.contains(inaccessible) == false)
        #expect(store.errorDetails.contains(inaccessible)); #expect(store.errorDetails.contains(unreadable))
        #expect(store.errorDetails.contains("[AccessDenied]")); #expect(store.errorDetails.contains("[ReadFailed]"))
        #expect(store.errorDetails.contains(denied)); #expect(store.errorDetails.contains(failed))
        #expect(store.errorDetails.contains("Synthetic extra load warning"))
        #expect(store.errorDetails.components(separatedBy: inaccessible).count == 2, "Structured warnings are complete without duplicating their legacy warning line.")
        #expect(store.operationHadProblems)
        store.errorMessage = nil; store.dismissErrorDetails()
        #expect(store.errorDetails.isEmpty)
        #expect(store.jobs.map(\.jobID) == [job.jobID]); #expect(store.jobs[0].reviewRevision == 5)
        #expect(store.selectedID == job.jobID); #expect(store.checkedJobIDs == [job.jobID])
        #expect(store.drafts[job.jobID]?["title"] == "Unsaved synthetic title")
    }

    @Test func diagnosticClipboardCopiesCompleteTextUsingAnIsolatedPasteboard() {
        let pasteboard = NSPasteboard.withUniqueName()
        defer { pasteboard.releaseGlobally() }
        let details = "AccessDenied\n/synthetic/" + String(repeating: "长路径/日本語/100% intact/", count: 500) + "\nReadFailed\nSynthetic error\twith Unicode and line breaks."
        #expect(DiagnosticClipboard.copy(details, to: pasteboard))
        #expect(pasteboard.string(forType: .string) == details)
        #expect(DiagnosticClipboard.copy("Replacement synthetic details", to: pasteboard))
        #expect(pasteboard.string(forType: .string) == "Replacement synthetic details")
    }

    @Test func unchangedArtworkKeepsPixelsDimensionsAndRequestIdentityAcrossSelections() {
        let store = WorkspaceStore(defaults: preferences.defaults)
        func snapshot(_ revision: Int, selected: Bool, content: String = "same-content") -> JSONValue {
            .object(["jobId": .string("a"), "reviewRevision": .number(Double(revision)), "metadata": .object(["id": .string("DEMO-001")]),
                "reviewSource": .string("local"), "reviewSources": .array([.string("local")]),
                "artwork": .array([.object(["artworkId": .string("image"), "kind": .string("sample"), "source": .string("local"), "selected": .bool(selected), "contentKey": .string(content)])])])
        }
        store.update(snapshot(1, selected: true))
        let job = store.jobs[0], path = URL(fileURLWithPath: "/synthetic/immutable-cache.png")
        let identity = store.artworkRequestKey(job.artwork[0], job: job)
        store.artworkPaths["a/image"] = path; store.artworkPaths["b/image"] = path
        store.artworkDimensions["a/image"] = CGSize(width: 800, height: 600)
        store.artworkLowResolution = ["a/image", "b/image"]
        store.update(snapshot(2, selected: false))
        #expect(store.artworkPaths["a/image"] == path)
        #expect(store.artworkDimensions["a/image"] == CGSize(width: 800, height: 600))
        #expect(store.artworkLowResolution.contains("a/image"))
        #expect(store.artworkRequestKey(store.jobs[0].artwork[0], job: store.jobs[0]) == identity)
        store.drafts["a"] = ["title": "Pending draft"]
        #expect(store.reviewSource(store.jobs[0]).isEmpty)
        store.drafts["a"] = nil; #expect(store.reviewSource(store.jobs[0]) == "local")
        store.update(snapshot(3, selected: false, content: "replacement"))
        #expect(store.artworkPaths["a/image"] == nil && store.artworkDimensions["a/image"] == nil)
        #expect(!store.artworkLowResolution.contains("a/image"))
        #expect(store.artworkPaths["b/image"] == path && store.artworkLowResolution.contains("b/image"))
    }

    @Test func replacementArtworkClearsStaleQualityAndPreservesOtherJobs() {
        let store = WorkspaceStore(defaults: preferences.defaults)
        let art: JSONValue = .object(["artworkId": .string("cover-0-fanart"), "kind": .string("fanart"), "source": .string("libredmm"), "selected": .bool(true)])
        let original: JSONValue = .object(["jobId": .string("a"), "reviewRevision": .number(1), "metadata": .object(["id": .string("DEMO-001")]), "artwork": .array([art])])
        store.update(original)
        store.artworkLowResolution = ["a/cover-0-fanart", "b/cover-0-fanart"]
        store.artworkDimensions["a/cover-0-fanart"] = CGSize(width: 147, height: 200)
        store.artworkSourceByJob["a"] = "r18dev"
        var revised = original.objectValue
        revised["reviewRevision"] = .number(2)
        revised["coverQuality"] = .object(["state": .string("ok"), "width": .number(1600), "height": .number(900)])
        store.update(.object(revised))
        #expect(store.artworkLowResolution == ["b/cover-0-fanart"])
        #expect(store.artworkDimensions["a/cover-0-fanart"] == nil)
        #expect(store.artworkSourceByJob["a"] == nil)
        #expect(!store.hasLowResolutionCover(store.jobs[0]))
    }

    @Test func committedCleanupIssueKeepsCompletedJobAndShowsActualDiagnostics() {
        let store = WorkspaceStore(defaults: preferences.defaults)
        let issue: JSONValue = .object(["path": .string("/synthetic/source/.temporary"), "message": .string("Synthetic permission denied")])
        let result: JSONValue = .object(["outcome": .string("cleanupRequired"), "savedJobIds": .array([.string("job-a")]), "cleanupIssues": .array([issue]), "jobResults": .array([.object(["jobId": .string("job-a"), "status": .string("Completed")])])])
        store.applySaveOutcome(result)
        #expect(store.operationHadProblems)
        #expect(store.operationResults[0]["status"].stringValue == "Completed")
        #expect(store.operationCleanupIssues == [issue])
        #expect(store.showingResults)
        #expect(!SaveOperationOutcome(result).isCompleteSuccess)
    }

    @Test func nativeImportPanelAcceptsMultipleFoldersAndExposesRecursion() {
        let panel = NSOpenPanel()
        let checkbox = NSButton(checkboxWithTitle: "Synthetic recursion", target: nil, action: nil)
        checkbox.state = .off
        NativeImportPanelConfiguration.configure(panel, recursiveControl: checkbox)
        #expect(panel.canChooseDirectories); #expect(panel.canChooseFiles); #expect(panel.allowsMultipleSelection)
        #expect(panel.accessoryView === checkbox); #expect(checkbox.state == .off)
        #expect(panel.isAccessoryViewDisclosed)
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil)) func unopenedPortraitFanartIsReviewableBeforeAnyPreviewRequest() async throws {
        _ = NSApplication.shared
        let path = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite FNS-121 synthetic \(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let video = directory.appendingPathComponent("FNS-121.mp4")
        try Data("Synthetic regression video bytes".utf8).write(to: video)
        let bitmap = try #require(NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 147, pixelsHigh: 200, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 147 * 4, bitsPerPixel: 32))
        memset(bitmap.bitmapData, 127, bitmap.bytesPerRow * bitmap.pixelsHigh)
        try #require(bitmap.representation(using: .png, properties: [:])).write(to: directory.appendingPathComponent("fanart.png"))
        let client = WorkerClient()
        do {
            _ = try await client.start(executable: URL(fileURLWithPath: path), arguments: ["--data-root", directory.appendingPathComponent("state").path])
            _ = try await finish(try await client.request("workspace.importInputs", params: .object(["paths": .array([.string(video.path)]), "recursive": .bool(false)])), client: client)
            let snapshot = try await client.request("workspace.getSnapshot")
            let raw = try #require(snapshot["jobs"].arrayValue.first)
            let job = MovieJobSnapshot(raw)
            #expect(job.coverQuality["state"].stringValue == "lowResolution")
            #expect(job.coverQuality["width"].intValue == 147); #expect(job.coverQuality["height"].intValue == 200)
            let store = WorkspaceStore(defaults: preferences.defaults); store.jobs = [job]
            #expect(store.hasLowResolutionCover(job)); #expect(store.categoryJobs(.issues).map(\.jobID) == [job.jobID])
            #expect(store.artworkPaths.isEmpty, "No preview was loaded for this queue classification.")
            let plan = try await finish(try await client.request("save.preview", params: .object(["jobId": raw["jobId"], "expectedReviewRevision": raw["reviewRevision"], "async": .bool(true), "options": .object(["writeNfo": .bool(true)])])), client: client)
            #expect(plan["canExecute"].boolValue, "Low resolution is advisory and must not block NFO saving.")
            #expect(!FileManager.default.fileExists(atPath: directory.appendingPathComponent("FNS-121.nfo").path))
            await client.shutdown()
        } catch { await client.shutdown(); throw error }
    }

    @Test func completedStatusRendersInCurrentLanguageWithoutChangingItsData() {
        let status = LocalizedStatusMessage("importResult", counts: [3, 1])
        #expect(status.resolved { _ in "Imported %d movies; %d already queued." } == "Imported 3 movies; 1 already queued.")
        #expect(status.resolved { _ in "已导入 %d 部影片；%d 部已在队列中。" } == "已导入 3 部影片；1 部已在队列中。")
        #expect(status.counts == [3, 1])
        let warning = LocalizedStatusMessage("outcome.cleanupRequired", details: ["/合成影片/100% intact.nfo"])
        #expect(warning.resolved { _ in "Review cleanup" } == "Review cleanup\n/合成影片/100% intact.nfo")
        #expect(warning.resolved { _ in "清理内容を確認" } == "清理内容を確認\n/合成影片/100% intact.nfo")
    }

    @Test func restoredWorkspaceFitsAvailableScreensAndRemainsResizable() {
        _ = NSApplication.shared
        let primary = NSRect(x: 0, y: 0, width: 1065, height: 760)
        let secondary = NSRect(x: -1600, y: 0, width: 1600, height: 900)
        let minimum = NSSize(width: 920, height: 650)
        let original = NSRect(x: -1400, y: 100, width: 1000, height: 700)
        #expect(WorkspaceWindowPolicy.fittedFrame(original, visibleFrames: [primary, secondary], minimumFrameSize: minimum) == original)
        let recovered = WorkspaceWindowPolicy.fittedFrame(original, visibleFrames: [primary], minimumFrameSize: minimum)
        #expect(primary.contains(recovered)); #expect(recovered.size == original.size)
        let huge = WorkspaceWindowPolicy.fittedFrame(NSRect(x: 8000, y: -8000, width: 5000, height: 4000), visibleFrames: [primary], minimumFrameSize: minimum)
        #expect(huge == primary)
        let small = WorkspaceWindowPolicy.fittedFrame(NSRect(x: 20, y: 20, width: 100, height: 100), visibleFrames: [primary], minimumFrameSize: minimum)
        #expect(small.size == minimum); #expect(primary.contains(small))
        let invalid = WorkspaceWindowPolicy.fittedFrame(NSRect(x: CGFloat.infinity, y: 0, width: 1000, height: 700), visibleFrames: [primary], minimumFrameSize: minimum)
        #expect(primary.contains(invalid)); #expect(invalid.size == minimum)
        let window = NSWindow(contentRect: NSRect(x: 5000, y: 5000, width: 400, height: 300), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        defer { window.close() }
        WorkspaceWindowPolicy.constrainRestoredFrame(window, visibleFrames: [primary])
        #expect(window.styleMask.contains(.resizable))
        #expect(window.contentMinSize == WorkspaceWindowPolicy.minimumContentSize)
        #expect(primary.contains(window.frame))
        #expect(window.contentRect(forFrameRect: window.frame).width >= 920)
        #expect(window.contentRect(forFrameRect: window.frame).height >= 620)
    }

    @Test func markedTextIsNotReplacedBySnapshotsAndCommitsToItsOwnJob() throws {
        _ = NSApplication.shared
        let first = NativeTextSession(jobID: "job-a", field: "title")
        let second = NativeTextSession(jobID: "job-b", field: "title")
        var a = "Original A", b = "Original B", changes = 0
        first.onChange = { a = $0; changes += 1 }; second.onChange = { b = $0 }
        first.configure(value: a, label: "Title A", editable: true, multiline: false)
        second.configure(value: b, label: "Title B", editable: true, multiline: false)
        #expect(first.commitComposition()); #expect(changes == 0, "Focus changes without edits must not publish dirty drafts.")
        first.textView.setSelectedRange(NSRange(location: 0, length: first.textView.string.utf16.count))
        first.textView.setMarkedText("组合文字", selectedRange: NSRange(location: 4, length: 0), replacementRange: NSRange(location: NSNotFound, length: 0))
        #expect(first.textView.hasMarkedText())
        first.configure(value: "Background snapshot", label: "Title A", editable: true, multiline: false)
        #expect(first.textView.string == "组合文字")
        #expect(a == "Original A", "Marked composition must not overwrite the review draft early.")
        #expect(first.commitComposition())
        #expect(a == "组合文字"); #expect(b == "Original B")
        #expect(first.textView.undoManager !== second.textView.undoManager)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 480, height: 120), styleMask: [.titled], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        first.scrollView.frame = window.contentView!.bounds; window.contentView?.addSubview(first.scrollView)
        defer { window.orderOut(nil); first.scrollView.removeFromSuperview() }
        #expect(window.makeFirstResponder(first.textView))
        first.configure(value: "Stale active-edit snapshot", label: "Title A", editable: true, multiline: false)
        #expect(first.textView.string == "组合文字", "An active unmarked editor must retain the user's current content.")
        first.textView.undoManager?.removeAllActions()
        first.textView.undoManager?.beginUndoGrouping()
        first.textView.insertText("已提交", replacementRange: NSRange(location: 0, length: first.textView.string.utf16.count))
        first.textView.undoManager?.endUndoGrouping()
        #expect(a == "已提交")
        #expect(first.textView.undoManager?.canUndo == true)
        first.textView.undoManager?.undo()
        #expect(first.textView.string == "组合文字")
        #expect(a == "组合文字"); #expect(b == "Original B")
        first.textView.undoManager?.redo()
        #expect(first.textView.string == "已提交"); #expect(a == "已提交")
        #expect(b == "Original B"); #expect(second.textView.string == "Original B")
        #expect(first.textView(first.textView, doCommandBy: #selector(NSResponder.insertTab(_:))))
        #expect(first.textView(first.textView, doCommandBy: #selector(NSResponder.insertNewline(_:))))
        second.configure(value: b, label: "Plot", editable: true, multiline: true)
        #expect(!second.textView(second.textView, doCommandBy: #selector(NSResponder.insertNewline(_:))))
    }

    @Test func boundedThumbnailCacheAndNativeDimensions() async throws {
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let cover = root.appendingPathComponent("fixtures/macos/synthetic-cover-5x2.png")
        let poster = root.appendingPathComponent("fixtures/macos/synthetic-poster-2x3.png")
        let cache = ArtworkThumbnailCache(maximumCost: 256)
        let a = await cache.image(url: cover, maximumPixelSize: 5)
        let b = await cache.image(url: poster, maximumPixelSize: 5)
        #expect(a?.width == 5); #expect(a?.height == 2)
        #expect(b?.width == 2); #expect(b?.height == 3)
        #expect(await cache.retainedCost <= 256)
        let first = try #require(a), second = try #require(b)
        let evicting = ArtworkThumbnailCache(maximumCost: max(first.decodedCost, second.decodedCost))
        _ = await evicting.image(url: cover, maximumPixelSize: 5)
        _ = await evicting.image(url: poster, maximumPixelSize: 5)
        #expect(await evicting.retainedCost == second.decodedCost)
        #expect(await ArtworkDimensions.read(url: cover) == CGSize(width: 5, height: 2))
    }

    @Test func webKitCaptureIsBoundedIsolatedAndRejectsStaleNavigation() async throws {
        let (web, delegate) = try await fixtureWebView()
        defer { web.stopLoading(); web.navigationDelegate = nil; _ = delegate }
        let capture = BrowserPageCapture()
        let page = try await capture.capture(from: web, maximumUTF8Bytes: 2 * 1024 * 1024)
        #expect(page.html.contains("Synthetic Browser Title"))
        #expect(page.url == "https://www.javlibrary.com/cn/?v=synthetic")
        do { _ = try await capture.capture(from: web, maximumUTF8Bytes: 16); Issue.record("Oversized HTML must be rejected.") }
        catch let error as BrowserCaptureError { #expect(error == .tooLarge) }
        let pending = Task { try await capture.capture(from: web, maximumUTF8Bytes: 2 * 1024 * 1024) }
        await Task.yield()
        capture.invalidate()
        do { _ = try await pending.value; Issue.record("A navigation generation change must discard pending capture.") }
        catch let error as BrowserCaptureError { #expect(error == .staleNavigation) }
        #expect(!BrowserPageCapture.allowsNavigation(URL(string: "file:///tmp/private.html")))
        #expect(!BrowserPageCapture.allowsNavigation(URL(string: "https://user:secret@www.javlibrary.com/")))
        #expect(!BrowserPageCapture.allowsNavigation(URL(string: "javascript:alert(1)")))
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil)) func capturedWebKitPageImportsIntoRealWorker() async throws {
        let path = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite WebKit \(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let video = directory.appendingPathComponent("DEMO-041.mp4")
        try Data("synthetic only".utf8).write(to: video)
        let client = WorkerClient()
        let (web, delegate) = try await fixtureWebView()
        defer { web.stopLoading(); web.navigationDelegate = nil; _ = delegate }
        do {
            _ = try await client.start(executable: URL(fileURLWithPath: path), arguments: ["--data-root", directory.appendingPathComponent("state").path])
            _ = try await finish(try await client.request("workspace.importInputs", params: .object(["paths": .array([.string(video.path)]), "recursive": .bool(true)])), client: client)
            let snapshot = try await client.request("workspace.getSnapshot")
            let job = try #require(snapshot["jobs"].arrayValue.first)
            let page = try await BrowserPageCapture().capture(from: web, maximumUTF8Bytes: 2 * 1024 * 1024)
            let result = try await finish(try await client.request("browser.importPage", params: .object(["jobId": job["jobId"], "expectedReviewRevision": job["reviewRevision"], "source": .string("javlibrary"), "url": .string(page.url), "html": .string(page.html)])), client: client)
            #expect(result["job"]["metadata"]["title"].stringValue.contains("Synthetic Browser Title"))
            #expect(result["job"]["metadata"]["sourceName"].stringValue == "javlibrary")
            #expect(result["job"]["reviewRevision"].intValue > job["reviewRevision"].intValue)
            #expect(!FileManager.default.fileExists(atPath: directory.appendingPathComponent("DEMO-041.nfo").path))
            await client.shutdown()
        } catch { await client.shutdown(); throw error }
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"] != nil)) func coordinatedSystemTrashRefreshesRealWorker() async throws {
        let worker = try #require(ProcessInfo.processInfo.environment["JAVMETALITE_TEST_WORKER"])
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite Native Trash \(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let video = directory.appendingPathComponent("DEMO-042.mp4")
        try Data("synthetic only".utf8).write(to: video)
        let repository = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let artwork = directory.appendingPathComponent("DEMO-042-poster.png")
        try FileManager.default.copyItem(at: repository.appendingPathComponent("fixtures/macos/synthetic-poster-2x3.png"), to: artwork)
        let client = WorkerClient()
        do {
            _ = try await client.start(executable: URL(fileURLWithPath: worker), arguments: ["--data-root", directory.appendingPathComponent("state").path])
            _ = try await finish(try await client.request("workspace.importInputs", params: .object(["paths": .array([.string(video.path)]), "recursive": .bool(true)])), client: client)
            let snapshot = try await client.request("workspace.getSnapshot")
            let job = try #require(snapshot["jobs"].arrayValue.first)
            let art = try #require(job["artwork"].arrayValue.first)
            let previewAccepted = try await client.request("artwork.getPreview", params: .object(["jobId": job["jobId"], "artworkId": art["artworkId"], "async": .bool(true)]))
            let preview = try await finish(previewAccepted, client: client)
            #expect(preview["width"].intValue == 2); #expect(preview["height"].intValue == 3)
            let previewOperation = try await client.request("operations.get", params: .object(["operationId": previewAccepted["operationId"]]))
            #expect(previewOperation["readOnly"].boolValue)
            let window = NSWindow(contentRect: NSRect(x: 50, y: 50, width: 500, height: 260), styleMask: [.titled], backing: .buffered, defer: false)
            window.isReleasedWhenClosed = false
            window.title = "JavMetaLite synthetic confirmation test"
            window.orderFront(nil)
            defer { window.orderOut(nil); window.close() }
            let originalArtwork = try Data(contentsOf: artwork)
            for _ in 0..<2 {
                let canceled = try await client.request("artwork.prepareTrash", params: .object(["jobId": job["jobId"], "expectedReviewRevision": job["reviewRevision"], "artworkId": art["artworkId"]]))
                let alert = NSAlert(); alert.messageText = "Synthetic confirmation cancellation"
                alert.addButton(withTitle: "Cancel"); alert.addButton(withTitle: "Move to Trash")
                let presentation = Task { await alert.presentSafeSheet(for: window) }
                for _ in 0..<100 where window.attachedSheet == nil { try await Task.sleep(for: .milliseconds(10)) }
                #expect(window.attachedSheet === alert.window, "Every confirmation must be a visible child sheet, including the second one.")
                #expect(alert.buttons[0].keyEquivalent == "\r")
                #expect(alert.window.defaultButtonCell === alert.buttons[0].cell)
                alert.buttons[0].performClick(nil)
                #expect(await presentation.value == .alertFirstButtonReturn)
                for _ in 0..<100 where window.attachedSheet != nil { try await Task.sleep(for: .milliseconds(10)) }
                #expect(window.attachedSheet == nil)
                _ = try await client.request("artwork.abortTrash", params: .object(["trashToken": canceled["trashToken"]]))
                let afterCancel = try await client.request("workspace.getSnapshot")
                #expect(afterCancel["activeTrash"] == .null)
                #expect(try Data(contentsOf: artwork) == originalArtwork)
                #expect(afterCancel["jobs"].arrayValue.first?["reviewRevision"] == job["reviewRevision"])
                let reloaded = try await finish(try await client.request("artwork.getPreview", params: .object(["jobId": job["jobId"], "artworkId": art["artworkId"], "async": .bool(true)])), client: client)
                #expect(reloaded["width"].intValue == 2)
                #expect(reloaded["height"].intValue == 3)
            }
            let prepared = try await client.request("artwork.prepareTrash", params: .object(["jobId": job["jobId"], "expectedReviewRevision": job["reviewRevision"], "artworkId": art["artworkId"]]))
            let token = prepared["trashToken"]
            let urls = prepared["paths"].arrayValue.map { URL(fileURLWithPath: $0.stringValue) }
            let result = try await FileTrashCoordinator.recycle(urls) {
                let authorized = try await client.request("artwork.validateTrash", params: .object(["trashToken": token]))
                #expect(authorized["authorized"].boolValue)
            }
            defer { for (original, trashed) in result.relocated { try? FileManager.default.moveItem(at: trashed, to: original) } }
            let completed = try await client.request("artwork.completeTrash", params: .object(["trashToken": token]))
            #expect(result.error == nil)
            #expect(result.relocated.count == 1)
            #expect(!FileManager.default.fileExists(atPath: artwork.path))
            #expect(completed["removedPaths"].arrayValue.count == 1)
            #expect(completed["jobs"].arrayValue.first?["reviewRevision"].intValue ?? 0 > job["reviewRevision"].intValue)
            #expect(FileManager.default.fileExists(atPath: video.path))
            await client.shutdown()
        } catch { await client.shutdown(); throw error }
    }

    private func fixtureWebView() async throws -> (WKWebView, NavigationWaiter) {
        _ = NSApplication.shared
        let config = WKWebViewConfiguration(); config.websiteDataStore = .nonPersistent()
        let web = WKWebView(frame: NSRect(x: 0, y: 0, width: 800, height: 600), configuration: config)
        let delegate = NavigationWaiter(); web.navigationDelegate = delegate
        let html = """
        <!doctype html><html><head><title>DEMO-041 Synthetic Browser Title - JAVLibrary</title>
        <script>window.TextEncoder = function() { throw new Error('page-world override'); };</script></head>
        <body><div id="video_info"><div id="video_id"><span class="text">DEMO-041</span></div>
        <div id="video_date"><span class="text">2026-01-01</span></div></div></body></html>
        """
        try await delegate.load(web, html: html, url: URL(string: "https://www.javlibrary.com/cn/?v=synthetic")!)
        return (web, delegate)
    }
    private func finish(_ response: JSONValue, client: WorkerClient) async throws -> JSONValue {
        let accepted = response
        for _ in 0..<400 {
            let op = try await client.request("operations.get", params: .object(["operationId": accepted["operationId"]]))
            if op["status"].stringValue == "completed" { return op["result"] }
            if ["failed", "canceled"].contains(op["status"].stringValue) { throw RPCFailure(code: -1, message: String(describing: op)) }
            try await Task.sleep(for: .milliseconds(25))
        }
        throw RPCFailure(code: -1, message: "Native integration timed out.")
    }
}

private final class OperationNotificationRecorder: @unchecked Sendable {
    private let lock = NSLock()
    private var ids: Set<String> = []
    func record(_ event: JSONValue) {
        let id = event["params"]["operationId"].stringValue
        guard !id.isEmpty else { return }
        lock.lock(); defer { lock.unlock() }; ids.insert(id)
    }
    var operationIDs: Set<String> { lock.lock(); defer { lock.unlock() }; return ids }
}

@MainActor private final class NavigationWaiter: NSObject, WKNavigationDelegate {
    private var pending: CheckedContinuation<Void, Error>?
    func load(_ web: WKWebView, html: String, url: URL) async throws {
        try await withCheckedThrowingContinuation { continuation in
            pending = continuation; web.loadHTMLString(html, baseURL: url)
        }
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) { pending?.resume(); pending = nil }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { pending?.resume(throwing: error); pending = nil }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { pending?.resume(throwing: error); pending = nil }
}
