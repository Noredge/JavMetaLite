import Testing
import Foundation
import AppKit
#if SWIFT_PACKAGE
@testable import JavMetaLiteMac
import WorkerKit
#else
@testable import JavMetaLiteMac
#endif

@Suite(.serialized) @MainActor struct EngineStartupRecoveryTests {
    private let preferences = TestPreferenceDomain()
    @Test func timedOutInitializeNeedsExplicitRetryAndKeepsTheSameProcess() async throws {
        _ = NSApplication.shared
        let fixture = try StartupPipeFixture(mode: "hold-first-initialize"); defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .milliseconds(150))
        let store = WorkspaceStore(worker: client, engineExecutable: { URL(fileURLWithPath: "/usr/bin/python3") }, engineArguments: fixture.arguments, defaults: preferences.defaults)
        store.drafts["review"] = ["title": "Keep unsaved review"]
        do {
            await store.start()
            #expect(!store.ready && !store.engineStarting && store.engineStartupFailed && store.canRetryEngineStart)
            #expect(!store.statusUnconfirmed && store.operationID == nil)
            try await fixture.waitFor("initialize-received")
            try fixture.release("initialize-release")
            try await fixture.waitFor("initialize-replied")
            _ = try await client.request("fixture.barrier")
            #expect(await client.handshake == .null, "A late response to a timed-out request is not a completed handshake.")
            let retry = try #require(store.retryEngineStart())
            #expect(store.engineStarting && store.retryEngineStart() == nil)
            await retry.value
            #expect(store.ready && !store.engineStarting && !store.engineStartupFailed)
            #expect(store.capabilities["discoverInputs"].boolValue && store.logRoot == "fixture-logs" && store.dataRoot == "fixture-data")
            #expect(store.jobs.map(\.jobID) == ["review"] && store.drafts["review"]?["title"] == "Keep unsaved review")
            let handshake = try await client.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: fixture.arguments)
            #expect(handshake["protocolVersion"].intValue == 1)
            #expect(try fixture.methods() == ["initialize", "fixture.barrier", "initialize", "preferences.get", "workspace.getSnapshot"])
            #expect(try fixture.processCount() == 1)
            // A previously usable engine losing its connection is an unknown
            // operation/session state, never permission to restart or replay.
            store.operationID = "known-save"
            store.receive(.object(["method": .string("worker.unavailable")]))
            #expect(!store.ready && store.statusUnconfirmed && !store.engineStartupFailed)
            #expect(!store.engineProcessExited && !store.canEndExitedEngineSession, "A lost response is not proof that the engine has exited.")
            #expect(!store.canRetryEngineStart && store.retryEngineStart() == nil && store.operationID == "known-save")
            #expect(store.drafts["review"]?["title"] == "Keep unsaved review")
            #expect(throws: (any Error).self) { try store.reviewRecoveryDocument() }
            let rejectedExport = fixture.directory.appendingPathComponent("unconfirmed-session.json")
            #expect(throws: (any Error).self) { try store.exportReviewRecovery(to: rejectedExport) }
            #expect(!FileManager.default.fileExists(atPath: rejectedExport.path))
            #expect(try !fixture.methods().contains("save.execute"))
            await client.shutdown()
        } catch { await client.shutdown(); throw error }
    }

    @Test func liveResponseTimeoutKeepsExitBlockedAndStatusReadCanReconcile() async throws {
        _ = NSApplication.shared
        let fixture = try StartupPipeFixture(mode: "ordinary"); defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .milliseconds(150))
        let store = WorkspaceStore(worker: client, engineExecutable: { URL(fileURLWithPath: "/usr/bin/python3") }, engineArguments: fixture.arguments, defaults: preferences.defaults)
        do {
            await store.start()
            #expect(store.ready)
            store.drafts["review"] = ["title": "Keep across a live timeout"]
            store.working = true
            do {
                _ = try await client.request("fixture.hold")
                Issue.record("The held fixture response must time out.")
            } catch let failure as RPCFailure {
                #expect(failure.reason == "WORKER_UNAVAILABLE")
                store.display(failure)
            }
            store.working = false
            #expect(await client.isRunning())
            #expect(store.statusUnconfirmed && !store.engineProcessExited && !store.canEndExitedEngineSession)
            #expect(store.engineExitCode == nil)
            #expect(throws: (any Error).self) { try store.reviewRecoveryDocument() }
            let exportURL = fixture.directory.appendingPathComponent("still-running.json")
            #expect(throws: (any Error).self) { try store.exportReviewRecovery(to: exportURL) }
            #expect(!FileManager.default.fileExists(atPath: exportURL.path))
            let check = try #require(store.checkStatus())
            await check.value
            #expect(!store.statusUnconfirmed && !store.engineProcessExited && !store.canEndExitedEngineSession)
            #expect(store.drafts["review"]?["title"] == "Keep across a live timeout")
            #expect(try fixture.methods().filter { $0 != "workspace.getSnapshot" } == ["initialize", "preferences.get", "fixture.hold"], "Reconciliation may read snapshots, but must not replay a mutation.")
            #expect(try fixture.processCount() == 1)
            await client.shutdown()
        } catch { await client.shutdown(); throw error }
    }

    @Test(arguments: [false, true])
    func confirmedProcessExitAllowsRecoveryExportWithoutReplayingOrClearingReview(knownSave: Bool) async throws {
        _ = NSApplication.shared
        let fixture = try StartupPipeFixture(mode: "exit-after-startup-signal"); defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .seconds(5))
        let store = WorkspaceStore(worker: client, engineExecutable: { URL(fileURLWithPath: "/usr/bin/python3") }, engineArguments: fixture.arguments, defaults: preferences.defaults)
        do {
            await store.start()
            #expect(store.ready && !store.engineProcessExited && !store.canEndExitedEngineSession)
            let sourceVideo = fixture.directory.appendingPathComponent("DEMO-001.mp4")
            let sourceNfo = fixture.directory.appendingPathComponent("movie.nfo")
            let originalVideo = try Data(contentsOf: sourceVideo), originalNfo = try Data(contentsOf: sourceNfo)
            store.drafts["review"] = ["title": "保留未保存标题 / Keep this draft", "plot": "Line one\n第二行"]
            store.setChecked("review", checked: true)
            store.operationID = knownSave ? "known-save-diagnostic-only" : nil
            store.working = knownSave
            store.preview = SavePreview(.object(["planId": .string("must-not-be-exported-or-replayed")]))
            try fixture.release("exit-release")
            for _ in 0..<500 {
                if store.engineProcessExited { break }
                try await Task.sleep(for: .milliseconds(10))
            }
            #expect(store.engineProcessExited && store.engineExitCode == 17, "The real child process must exit; an injected unavailable notification is insufficient.")
            #expect(!(await client.isRunning()))
            #expect(!store.ready && !store.working && store.statusUnconfirmed && store.preview == nil)
            #expect(store.canEndExitedEngineSession && !store.canRetryEngineStart && store.retryEngineStart() == nil)
            #expect(store.operationID == (knownSave ? "known-save-diagnostic-only" : nil))

            let beforeExportDrafts = store.drafts
            let document = try store.reviewRecoveryDocument()
            #expect(document["formatVersion"].intValue == 1)
            #expect(document["kind"].stringValue == "JavMetaLite.review-recovery")
            #expect(document["engineProcessExited"].boolValue && document["engineExitCode"].intValue == 17)
            #expect(document["operationOutcome"].stringValue == "unknown", "Process exit never establishes whether a prior save completed.")
            #expect(document["operationID"] == (knownSave ? .string("known-save-diagnostic-only") : .null))
            #expect(document["drafts"]["review"]["title"].stringValue == beforeExportDrafts["review"]?["title"])
            #expect(document["drafts"]["review"]["plot"].stringValue == beforeExportDrafts["review"]?["plot"])
            #expect(document["checkedJobIDs"].arrayValue == [.string("review")])
            #expect(document["selectedJobID"].stringValue == "review")
            let job = try #require(document["jobs"].arrayValue.first)
            #expect(job["jobID"].stringValue == "review" && job["reviewRevision"].intValue == 9)
            #expect(job["metadata"]["title"].stringValue == "Synthetic startup review", "The last engine snapshot and later native drafts must both survive export.")
            #expect(job["videoPaths"].arrayValue == [.string(sourceVideo.path)])
            #expect(job["localNfoPath"].stringValue == sourceNfo.path)
            #expect(document["planId"] == .null && document["planID"] == .null && document["preview"] == .null)
            let encodedDocument = try JSONEncoder().encode(document)
            #expect(!String(decoding: encodedDocument, as: UTF8.self).contains("must-not-be-exported-or-replayed"))

            let exportURL = fixture.directory.appendingPathComponent("恢复审查.json")
            try store.exportReviewRecovery(to: exportURL)
            let exported = try JSONDecoder().decode(JSONValue.self, from: Data(contentsOf: exportURL))
            #expect(exported["jobs"] == document["jobs"] && exported["drafts"] == document["drafts"])
            #expect(exported["operationOutcome"].stringValue == "unknown")
            #expect(store.drafts == beforeExportDrafts && store.statusUnconfirmed && store.canEndExitedEngineSession)

            let unsafeExtension = fixture.directory.appendingPathComponent("do-not-replace.nfo")
            #expect(throws: (any Error).self) { try store.exportReviewRecovery(to: unsafeExtension) }
            #expect(!FileManager.default.fileExists(atPath: unsafeExtension.path))
            let failedExport = fixture.directory.appendingPathComponent("missing-parent/recovery.json")
            #expect(throws: (any Error).self) { try store.exportReviewRecovery(to: failedExport) }
            #expect(store.drafts == beforeExportDrafts && store.statusUnconfirmed && store.canEndExitedEngineSession)

            #expect(store.checkStatus() == nil, "A confirmed dead engine cannot reconcile the old session through status polling.")
            #expect(store.engineProcessExited && store.statusUnconfirmed && store.canEndExitedEngineSession)
            #expect(store.operationID == (knownSave ? "known-save-diagnostic-only" : nil))
            #expect(store.drafts == beforeExportDrafts)
            #expect(try fixture.methods() == ["initialize", "preferences.get", "workspace.getSnapshot"], "Recovery export and status checks must not create a new process or replay a mutation.")
            #expect(try fixture.processCount() == 1)
            #expect(try Data(contentsOf: sourceVideo) == originalVideo)
            #expect(try Data(contentsOf: sourceNfo) == originalNfo)
            await client.shutdown()
        } catch { await client.shutdown(); throw error }
    }

    @Test func preferencesAndSnapshotMustFinishBeforeReadinessAndRetryNeverImports() async throws {
        _ = NSApplication.shared
        let fixture = try StartupPipeFixture(mode: "fail-snapshot-then-hold"); defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .seconds(5))
        let store = WorkspaceStore(worker: client, engineExecutable: { URL(fileURLWithPath: "/usr/bin/python3") }, engineArguments: fixture.arguments, defaults: preferences.defaults)
        store.drafts["review"] = ["title": "Keep through snapshot failure"]
        do {
            await store.start()
            #expect(!store.ready && store.engineStartupFailed && store.canRetryEngineStart)
            #expect(store.capabilities == .null && store.logRoot == nil, "Partial startup reads must not advertise a usable engine.")
            let retry = try #require(store.retryEngineStart())
            try await fixture.waitFor("snapshot-received")
            #expect(store.engineStarting && !store.ready && store.retryEngineStart() == nil)
            #expect(store.drafts["review"]?["title"] == "Keep through snapshot failure")
            try fixture.release("snapshot-release")
            await retry.value
            #expect(store.ready && !store.engineStartupFailed && !store.engineStarting)
            await store.start() // SwiftUI appearance cannot repeat successful setup.
            #expect(try fixture.methods() == ["initialize", "preferences.get", "workspace.getSnapshot", "preferences.get", "workspace.getSnapshot"])
            #expect(try fixture.processCount() == 1)
            #expect(store.drafts["review"]?["title"] == "Keep through snapshot failure")
            await client.shutdown()
        } catch { await client.shutdown(); throw error }
    }

    @Test func concurrentInitializationRejectsTheSecondCallerWithoutAnotherSpawn() async throws {
        let fixture = try StartupPipeFixture(mode: "hold-first-initialize"); defer { fixture.remove() }
        let client = WorkerClient(requestTimeout: .seconds(5))
        let first = Task { try await client.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: fixture.arguments) }
        do {
            try await fixture.waitFor("initialize-received")
            do {
                _ = try await client.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: fixture.arguments)
                Issue.record("Concurrent startup must not return an uninitialized handshake.")
            } catch let failure as RPCFailure { #expect(failure.reason == "ENGINE_START_IN_PROGRESS") }
            try fixture.release("initialize-release")
            let handshake = try await first.value
            #expect(handshake["protocolVersion"].intValue == 1)
            #expect(try fixture.methods() == ["initialize"] && fixture.processCount() == 1)
            await client.shutdown()
        } catch { try? fixture.release("initialize-release"); _ = try? await first.value; await client.shutdown(); throw error }
    }
}

private struct StartupPipeFixture {
    let directory: URL
    var arguments: [String] { [directory.appendingPathComponent("engine.py").path, directory.path] }
    init(mode: String) throws {
        directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite startup 合成 \(UUID())", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try mode.write(to: directory.appendingPathComponent("mode"), atomically: true, encoding: .utf8)
        try Self.script.write(to: directory.appendingPathComponent("engine.py"), atomically: true, encoding: .utf8)
        try Data("SYNTHETIC NONPLAYABLE VIDEO".utf8).write(to: directory.appendingPathComponent("DEMO-001.mp4"))
        try Data("<movie><title>Original fixture</title></movie>".utf8).write(to: directory.appendingPathComponent("movie.nfo"))
    }
    func remove() { try? FileManager.default.removeItem(at: directory) }
    func release(_ name: String) throws { try Data().write(to: directory.appendingPathComponent(name)) }
    func waitFor(_ name: String) async throws {
        for _ in 0..<500 {
            if FileManager.default.fileExists(atPath: directory.appendingPathComponent(name).path) { return }
            try await Task.sleep(for: .milliseconds(10))
        }
        throw RPCFailure(code: -1, message: "The real pipe fixture did not reach \(name).")
    }
    func methods() throws -> [String] { try String(contentsOf: directory.appendingPathComponent("methods"), encoding: .utf8).split(separator: "\n").map(String.init) }
    func processCount() throws -> Int { try String(contentsOf: directory.appendingPathComponent("processes"), encoding: .utf8).split(separator: "\n").count }
    private static let script = #"""
import json,os,sys,threading,time
from pathlib import Path
root=Path(sys.argv[1]); mode=(root/'mode').read_text(); initializes=0; snapshots=0
with (root/'processes').open('a') as stream: stream.write('process\n')
lock=threading.Lock()
def emit(id,result=None,error=None):
    message={'jsonrpc':'2.0','id':id}
    message['error' if error else 'result']=error if error else result
    with lock: print(json.dumps(message),flush=True)
def wait_emit(id,name,result):
    while not (root/(name+'-release')).exists(): time.sleep(.005)
    emit(id,result); (root/(name+'-replied')).touch()
def exit_when_released():
    while not (root/'exit-release').exists(): time.sleep(.005)
    os._exit(17)
if mode=='exit-after-startup-signal': threading.Thread(target=exit_when_released,daemon=True).start()
handshake={'protocolVersion':1,'capabilities':{'discoverInputs':True,'batchSave':True}}
snapshot={'jobs':[{'jobId':'review','reviewRevision':9,'metadata':{'id':'DEMO-001','title':'Synthetic startup review'},'videoPaths':[str(root/'DEMO-001.mp4')],'localNfoPath':str(root/'movie.nfo'),'artwork':[]}],'activeOperations':[],'recoveryWarnings':[]}
for line in sys.stdin:
    request=json.loads(line); method=request['method']; id=request['id']
    with (root/'methods').open('a') as stream: stream.write(method+'\n')
    if method=='initialize':
        initializes+=1
        if mode=='hold-first-initialize' and initializes==1:
            (root/'initialize-received').touch()
            thread=threading.Thread(target=wait_emit,args=(id,'initialize',handshake),daemon=True); thread.start()
        else: emit(id,handshake)
    elif method=='preferences.get': emit(id,{'logRoot':'fixture-logs','dataRoot':'fixture-data','includeIdInTitle':True})
    elif method=='workspace.getSnapshot':
        snapshots+=1
        if mode=='fail-snapshot-then-hold' and snapshots==1: emit(id,error={'code':-32000,'message':'Synthetic first snapshot failed','data':{'reason':'FIXTURE_SNAPSHOT'}})
        elif mode=='fail-snapshot-then-hold':
            (root/'snapshot-received').touch()
            thread=threading.Thread(target=wait_emit,args=(id,'snapshot',snapshot),daemon=True); thread.start()
        else: emit(id,snapshot)
    elif method=='fixture.barrier': emit(id,{'received':True})
    elif method=='fixture.hold': pass
    elif method=='shutdown': emit(id,{'stopped':True}); break
"""#
}
