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
            #expect(!store.canRetryEngineStart && store.retryEngineStart() == nil && store.operationID == "known-save")
            #expect(store.drafts["review"]?["title"] == "Keep unsaved review")
            #expect(try !fixture.methods().contains("save.execute"))
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
import json,sys,threading,time
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
handshake={'protocolVersion':1,'capabilities':{'discoverInputs':True,'batchSave':True}}
snapshot={'jobs':[{'jobId':'review','reviewRevision':9,'metadata':{'id':'DEMO-001','title':'Synthetic startup review'},'videoPaths':[],'artwork':[]}],'activeOperations':[],'recoveryWarnings':[]}
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
    elif method=='shutdown': emit(id,{'stopped':True}); break
"""#
}
