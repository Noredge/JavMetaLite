import Testing
import Foundation
import AppKit
import WebKit
#if SWIFT_PACKAGE
@testable import NativeBridgeKit
@testable import JavMetaLiteMac
import WorkerKit
#else
@testable import JavMetaLiteMac
#endif

@Suite(.serialized) @MainActor struct BrowserInteractionTests {
    private let preferences = TestPreferenceDomain()
    @Test func ordinaryNewTabLinksStayInTheCurrentViewWhileScriptPopupsRemainBlocked() {
        let detail = URL(string: "https://www.javlibrary.com/cn/?v=synthetic")
        func decision(_ url: URL?, frame: Bool = false, main: Bool = false,
                      activated: Bool = true, download: Bool = false,
                      capturing: Bool = false) -> BrowserNavigationDecision {
            BrowserNavigationPolicy.decision(url: url, hasTargetFrame: frame,
                targetIsMainFrame: main, userActivated: activated,
                isDownload: download, capturing: capturing)
        }
        #expect(decision(detail) == .openInCurrentFrame)
        #expect(decision(detail, frame: true, main: true) == .allow)
        #expect(decision(detail, frame: true, main: false) == .allow)
        #expect(decision(detail, activated: false) == .block)
        #expect(decision(detail, download: true) == .block)
        #expect(decision(detail, capturing: true) == .block)
        #expect(decision(detail, frame: true, main: true, capturing: true) == .block)
        for address in ["about:blank", "about:srcdoc", "about:blank#widget"] {
            // Explicit components preserve the fragment on macOS 15 as well;
            // URL(string:) there encodes '#' into the opaque about: path.
            let url = URLComponents(string: address)?.url
            #expect(decision(url, frame: true, main: false, activated: false) == .allow)
            #expect(decision(url, frame: true, main: true) == .block)
            #expect(decision(url) == .block)
            #expect(BrowserNavigationPolicy.allowsResponse(url, isMainFrame: false))
            #expect(!BrowserNavigationPolicy.allowsResponse(url, isMainFrame: true))
            #expect(BrowserNavigationPolicy.externalURL(address) == nil)
        }
        let encodedLookalike = URL(string: "about:blank%23widget")
        #expect(decision(encodedLookalike, frame: true, main: false, activated: false) == .block)
        #expect(!BrowserNavigationPolicy.allowsResponse(encodedLookalike, isMainFrame: false))
        for address in ["file:///tmp/private.html", "javascript:alert(1)",
                        "https://user:password@www.javlibrary.com/", "data:text/html,private"] {
            #expect(decision(URL(string: address)) == .block)
            #expect(BrowserNavigationPolicy.externalURL(address) == nil)
        }
        #expect(BrowserNavigationPolicy.externalURL("  https://www.javlibrary.com/cn/  ")?.host == "www.javlibrary.com")
    }

    @Test func openingAndSwitchingSourcesLoadsImmediatelyAndHttpFailureCannotEnableImport() async throws {
        _ = NSApplication.shared
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("JavMetaLite browser navigation \(UUID())")
        let cache = directory.appendingPathComponent("cache")
        try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let script = directory.appendingPathComponent("browser_engine.py")
        let python = #"""
import json,sys,threading
from http.server import ThreadingHTTPServer,BaseHTTPRequestHandler
calls=[];resolutions=[];cancellations=[];polls={};behavior="success"
job={'jobId':'fixture','reviewRevision':17,'metadata':{'id':'DEMO-701','title':'Before browser import'},'videoPaths':[]}
class Handler(BaseHTTPRequestHandler):
    def log_message(self,*args): pass
    def do_GET(self):
        status=403 if self.path=='/library' else 200
        text=('<!doctype html><html><head><title>Synthetic browser navigation</title></head><body>'+self.path+'<iframe src="about:blank"></iframe><iframe srcdoc="nested challenge setup"></iframe></body></html>').encode()
        self.send_response(status);self.send_header('Content-Type','text/html; charset=utf-8');self.send_header('Content-Length',str(len(text)));self.end_headers();self.wfile.write(text)
server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
threading.Thread(target=server.serve_forever,daemon=True).start()
base='http://127.0.0.1:'+str(server.server_address[1])
def emit(id,result): print(json.dumps({'jsonrpc':'2.0','id':id,'result':result}),flush=True)
for line in sys.stdin:
    r=json.loads(line);m=r['method'];p=r['params'];id=r['id']
    if m=='initialize':emit(id,{'protocolVersion':1,'cacheRoot':sys.argv[1],'fixtureBaseUrl':base})
    elif m=='browser.getSupport':calls.append(p);emit(id,{'supported':not p['url'].endswith('/r18home')})
    elif m=='fixture.calls':emit(id,{'calls':calls,'resolutions':resolutions,'cancellations':cancellations})
    elif m=='fixture.behavior':behavior=p['value'];emit(id,{'ok':True})
    elif m=='browser.resolveTarget':
        assert p['expectedReviewRevision']==job['reviewRevision'];resolutions.append(p);emit(id,{'operationId':'resolve-'+str(len(resolutions))})
    elif m=='operations.cancel':cancellations.append(p['operationId']);emit(id,{'received':True})
    elif m=='fixture.job':emit(id,job)
    elif m=='workspace.getSnapshot':emit(id,{'jobs':[job]})
    elif m=='browser.importPage':
        assert p['expectedReviewRevision']==job['reviewRevision'];job['reviewRevision']+=1;job['metadata']['title']='Imported synthetic webpage';emit(id,{'operationId':'browser-import'})
    elif m=='operations.get':
        op=p['operationId']
        if op.startswith('resolve-'):
            polls[op]=polls.get(op,0)+1
            if behavior=='hold' or polls[op]<2:emit(id,{'status':'running'})
            elif behavior=='fail':emit(id,{'status':'failed','error':{'message':'Synthetic provider unavailable'}})
            else:emit(id,{'status':'completed','result':{'url':base+'/r18','source':'r18dev'}})
        else:emit(id,{'status':'completed','result':{'job':job,'promotedFieldCount':1}})
    elif m=='shutdown':emit(id,{'shutdown':True});break
server.shutdown();server.server_close()
"""#
        try python.write(to: script, atomically: true, encoding: .utf8)
        let store = WorkspaceStore(defaults: preferences.defaults)
        do {
            let initialized = try await store.worker.start(executable: URL(fileURLWithPath: "/usr/bin/python3"), arguments: [script.path, cache.path])
            store.ready = true
            store.jobs = [MovieJobSnapshot(try await store.worker.request("fixture.job"))]; store.selectNavigation(["fixture"])
            let base = initialized["fixtureBaseUrl"].stringValue
            // Keep each inference unit small for the CI's Swift 6.1 compiler.
            let libreTarget: [String: JSONValue] = [
                "source": .string("libredmm"), "displayName": .string("LibreDMM"), "url": .string(base + "/libre")]
            let r18Target: [String: JSONValue] = [
                "source": .string("r18dev"), "displayName": .string("R18.dev"),
                "url": .string(base + "/r18home"), "requiresResolution": .bool(true)]
            let libraryTarget: [String: JSONValue] = [
                "source": .string("javlibrary"), "displayName": .string("JAVLibrary"), "url": .string(base + "/library")]
            let targets: [JSONValue] = [.object(libreTarget), .object(r18Target), .object(libraryTarget)]
            let maximumHtmlBytes = 2 * 1024 * 1024
            let response = JSONValue.object([
                "maximumHtmlBytes": .number(Double(maximumHtmlBytes)), "targets": .array(targets)])
            let session = BrowserSession(jobID: "fixture", response: response, store: store)
            func settle(_ path: String, supported: Bool) async throws {
                for _ in 0..<500 {
                    if !session.loading && session.webView.url?.path == path && session.supported == supported { return }
                    try await Task.sleep(for: .milliseconds(10))
                }
                Issue.record("Browser navigation did not settle: \(path), status=\(session.status ?? ""), current=\(session.webView.url?.absoluteString ?? "")")
            }
            session.start()
            try await settle("/libre", supported: true)
            #expect(session.webView.url?.path == "/libre")
            #expect(session.httpStatusCode == 200)
            // Repeated SwiftUI task lifetimes must not reset the page.
            session.start()
            let firstGeneration = session.capture.generation
            session.chooseSource("r18dev")
            #expect(session.capture.generation > firstGeneration && session.source == "r18dev" && session.loading && !session.supported)
            session.importPage()
            #expect(!session.capturing && !store.working, "Switching sources must synchronously invalidate the old page before another import action can run.")
            try await settle("/r18", supported: true)
            #expect(session.source == "r18dev" && session.webView.url?.path == "/r18")
            let capturingGeneration = session.capture.generation
            session.capturing = true; session.chooseSource("javlibrary")
            #expect(session.source == "r18dev" && session.capture.generation == capturingGeneration, "An active capture cannot change sources midway.")
            session.capturing = false
            session.chooseSource("javlibrary")
            try await settle("/library", supported: false)
            #expect(session.httpStatusCode == 403 && !session.supported)
            #expect(session.diagnostics.contains("HTTP 403"))
            // A rejected request may arrive after changing sources. It cannot
            // clear the current HTTP error or enable import of the old source.
            session.webView(session.webView, didFailProvisionalNavigation: nil,
                withError: NSError(domain: NSURLErrorDomain, code: NSURLErrorCancelled))
            #expect(session.httpStatusCode == 403 && !session.supported && !session.loading)
            let requests = try await store.worker.request("fixture.calls")["calls"].arrayValue
            #expect(requests.map { $0["source"].stringValue } == ["libredmm", "r18dev"], "HTTP failure must not be marked supported from URL alone.")
            session.chooseSource("r18dev")
            let supersededGeneration = session.capture.generation
            session.chooseSource("libredmm")
            #expect(session.source == "libredmm" && session.capture.generation > supersededGeneration && !session.supported)
            try await settle("/libre", supported: true)
            #expect(session.source == "libredmm" && session.webView.url?.path == "/libre", "Rapid source actions retain their order; an older navigation cannot replace the final source.")
            session.address = "file:///tmp/private.html"
            session.navigate()
            #expect(!session.supported && session.webView.url?.path == "/libre")
            session.address = base + "/libre"; session.navigate()
            try await settle("/libre", supported: true)
            let raw = try await store.worker.request("fixture.job")
            store.jobs = [MovieJobSnapshot(raw)]; store.selectNavigation(["fixture"])
            store.browser = session; store.detailTab = "artwork"
            session.importPage()
            for _ in 0..<500 where store.working { try await Task.sleep(for: .milliseconds(10)) }
            #expect(store.browser == nil && store.detailTab == "metadata")
            #expect(store.jobs.first?.metadata["title"].stringValue == "Imported synthetic webpage")
            #expect(store.jobs.first?.reviewRevision == 18)
            let resolvedCalls = try await store.worker.request("fixture.calls")["resolutions"].arrayValue
            #expect(resolvedCalls.count == 1, "Repeated source switches reuse this revision's resolved URL.")
            session.close()
            let closedGeneration = session.capture.generation
            session.chooseSource("r18dev")
            #expect(session.source == "libredmm" && session.capture.generation == closedGeneration)
            // A superseded or closed resolver cannot navigate the new page.
            _ = try await store.worker.request("fixture.behavior", params: .object(["value": .string("hold")]))
            let pending = BrowserSession(jobID: "fixture", response: response, store: store)
            pending.chooseSource("r18dev")
            for _ in 0..<200 {
                if try await store.worker.request("fixture.calls")["resolutions"].arrayValue.count >= 2 { break }
                try await Task.sleep(for: .milliseconds(5))
            }
            pending.chooseSource("libredmm")
            for _ in 0..<300 where pending.loading { try await Task.sleep(for: .milliseconds(10)) }
            #expect(pending.source == "libredmm" && pending.webView.url?.path == "/libre")
            pending.chooseSource("r18dev")
            for _ in 0..<200 {
                if try await store.worker.request("fixture.calls")["resolutions"].arrayValue.count >= 3 { break }
                try await Task.sleep(for: .milliseconds(5))
            }
            pending.close()
            for _ in 0..<200 {
                if try await store.worker.request("fixture.calls")["cancellations"].arrayValue.count >= 2 { break }
                try await Task.sleep(for: .milliseconds(5))
            }
            #expect(try await store.worker.request("fixture.calls")["cancellations"].arrayValue.count >= 2)
            _ = try await store.worker.request("fixture.behavior", params: .object(["value": .string("fail")]))
            let unavailable = BrowserSession(jobID: "fixture", response: response, store: store)
            unavailable.chooseSource("r18dev")
            for _ in 0..<300 where unavailable.loading { try await Task.sleep(for: .milliseconds(10)) }
            #expect(unavailable.webView.url?.path == "/r18home" && !unavailable.supported)
            #expect(unavailable.diagnostics.contains("Synthetic provider unavailable"))
            unavailable.close()
            await store.worker.shutdown()
        } catch { await store.worker.shutdown(); throw error }
    }

    @Test func browserRetainsItsOwnWebsiteStateWithoutSpoofingTheUserAgent() async throws {
        _ = NSApplication.shared
        let store = WorkspaceStore(defaults: preferences.defaults)
        let session = BrowserSession(jobID: "cookies", response: .object([:]), store: store)
        #expect(session.webView.configuration.websiteDataStore.isPersistent)
        #expect(session.webView.configuration.defaultWebpagePreferences.allowsContentJavaScript)
        #expect(session.webView.customUserAgent?.isEmpty != false)
        let name = "JML-test-" + UUID().uuidString
        let cookie = try #require(HTTPCookie(properties: [.domain: "127.0.0.1", .path: "/", .name: name, .value: "synthetic"] ))
        let cookies = session.webView.configuration.websiteDataStore.httpCookieStore
        await cookies.setCookie(cookie)
        let next = BrowserSession(jobID: "cookies", response: .object([:]), store: store)
        let retained = await next.webView.configuration.websiteDataStore.httpCookieStore.allCookies()
        #expect(retained.contains { $0.name == name && $0.value == "synthetic" })
        await cookies.deleteCookie(cookie)
        session.close(); next.close()
    }
}
