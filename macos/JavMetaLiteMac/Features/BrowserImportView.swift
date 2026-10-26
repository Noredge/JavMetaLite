import SwiftUI
import WebKit
import AppKit
#if SWIFT_PACKAGE
import WorkerKit
import NativeBridgeKit
#endif

@MainActor final class BrowserSession: NSObject, ObservableObject, Identifiable, WKNavigationDelegate, WKUIDelegate {
    let id = UUID()
    let jobID: String
    let targets: [JSONValue]
    let maximumHtmlBytes: Int
    let webView: WKWebView
    let capture = BrowserPageCapture()
    weak var store: WorkspaceStore?
    @Published var address = ""
    @Published var source = ""
    @Published var loading = false
    @Published var supported = false
    @Published var canBack = false
    @Published var canForward = false
    @Published var status: String?
    @Published var errorMessage: String?
    @Published var domain = ""
    @Published var diagnostics = ""
    @Published var httpStatusCode: Int?
    @Published var openedExternally = false
    var importOperationID: String?
    var capturing = false
    private var started = false
    private var closed = false
    private var activeNavigation: WKNavigation?
    private var resolutionTask: Task<Void, Never>?
    private var resolutionOperationID: String?
    private var resolutionFallbackDiagnostic: String?
    private var resolvedTargets: [String: (revision: Int, url: String)] = [:]

    init(jobID: String, response: JSONValue, store: WorkspaceStore) {
        self.jobID = jobID; self.store = store
        targets = response["targets"].arrayValue
        maximumHtmlBytes = response["maximumHtmlBytes"].intValue
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .default()
        configuration.defaultWebpagePreferences.allowsContentJavaScript = true
        configuration.preferences.javaScriptCanOpenWindowsAutomatically = false
        webView = WKWebView(frame: .zero, configuration: configuration)
        super.init()
        webView.navigationDelegate = self; webView.uiDelegate = self
        source = targets.first?["source"].stringValue ?? ""
        address = targets.first?["url"].stringValue ?? ""
        status = tr("browserEmpty")
    }
    func start() {
        guard !started, !closed else { return }
        started = true
        openSourceTarget()
    }
    func chooseSource(_ value: String) {
        guard !closed, !capturing, store?.working != true,
              let target = targets.first(where: { $0["source"].stringValue == value }) else { return }
        source = value; invalidateCapture()
        address = target["url"].stringValue
        openedExternally = false
        openSourceTarget()
    }
    private func openSourceTarget() {
        guard let store, let target = targets.first(where: { $0["source"].stringValue == source }),
              let job = store.jobs.first(where: { $0.jobID == jobID }), target["requiresResolution"].boolValue else { navigate(); return }
        let revision = job.reviewRevision, selectedSource = source
        if let resolved = resolvedTargets[source], resolved.revision == revision {
            address = resolved.url; navigate(); return
        }
        invalidateCapture(); webView.stopLoading(); activeNavigation = nil
        let generation = capture.generation
        loading = true; httpStatusCode = nil; diagnostics = ""; domain = URL(string: address)?.host ?? ""
        status = tr("browserResolvingTarget")
        resolutionTask = Task {
            do {
                let response = try await store.worker.request("browser.resolveTarget", params: .object([
                    "jobId": .string(jobID), "expectedReviewRevision": .number(Double(revision)), "source": .string(selectedSource)
                ]))
                let operation = response["operationId"].stringValue
                guard !operation.isEmpty else { throw BrowserCaptureError.staleNavigation }
                guard !Task.isCancelled, !closed, generation == capture.generation, source == selectedSource else {
                    _ = try? await store.worker.request("operations.cancel", params: .object(["operationId": .string(operation)])); return
                }
                resolutionOperationID = operation
                while true {
                    try Task.checkCancellation()
                    let state = try await store.worker.request("operations.get", params: .object(["operationId": .string(operation)]))
                    guard !Task.isCancelled, !closed, generation == capture.generation, source == selectedSource else { return }
                    if state["status"].stringValue == "completed" {
                        guard store.jobs.first(where: { $0.jobID == jobID })?.reviewRevision == revision,
                              let url = BrowserNavigationPolicy.externalURL(state["result"]["url"].stringValue) else { throw BrowserCaptureError.staleNavigation }
                        resolutionOperationID = nil; resolutionTask = nil
                        resolvedTargets[selectedSource] = (revision, url.absoluteString)
                        address = url.absoluteString; navigate(); return
                    }
                    if ["failed", "canceled"].contains(state["status"].stringValue) {
                        throw RPCFailure(code: -32010, message: state["error"]["message"].stringValue.isEmpty ? tr("browserTargetNotFound") : state["error"]["message"].stringValue)
                    }
                    try await Task.sleep(for: .milliseconds(120))
                }
            } catch {
                guard !Task.isCancelled, !closed, generation == capture.generation, source == selectedSource else { return }
                resolutionOperationID = nil; resolutionTask = nil
                // Keep manual site search available after an unavailable lookup.
                address = target["url"].stringValue
                navigate()
                diagnostics = tr("browserTargetNotFound") + "\n" + error.localizedDescription
                resolutionFallbackDiagnostic = diagnostics
            }
        }
    }
    func navigate() {
        guard !closed, !capturing, store?.working != true else { return }
        guard let url = BrowserNavigationPolicy.externalURL(address) else {
            invalidateCapture(); loading = false; status = tr("browserSchemeBlocked"); return
        }
        beginNavigation(URLRequest(url: url))
    }
    func reopenSource() {
        guard let target = targets.first(where: { $0["source"].stringValue == source }) else { return }
        address = target["url"].stringValue
        openSourceTarget()
    }
    func reload() {
        guard !closed, !capturing, store?.working != true else { return }
        if webView.url == nil { navigate(); return }
        invalidateCapture(); httpStatusCode = nil; diagnostics = ""
        loading = true; status = tr("browserLoading")
        activeNavigation = webView.reload()
    }
    func goBack() {
        guard !closed, !capturing, store?.working != true, webView.canGoBack else { return }
        invalidateCapture(); httpStatusCode = nil; diagnostics = ""
        loading = true; status = tr("browserLoading")
        activeNavigation = webView.goBack()
    }
    func goForward() {
        guard !closed, !capturing, store?.working != true, webView.canGoForward else { return }
        invalidateCapture(); httpStatusCode = nil; diagnostics = ""
        loading = true; status = tr("browserLoading")
        activeNavigation = webView.goForward()
    }
    func openExternally() {
        guard !closed, !capturing, store?.working != true,
              let url = BrowserNavigationPolicy.externalURL(address) else { return }
        if NSWorkspace.shared.open(url) { openedExternally = true }
    }
    var canOpenExternally: Bool { BrowserNavigationPolicy.externalURL(address) != nil && !capturing }
    private func beginNavigation(_ request: URLRequest) {
        invalidateCapture()
        webView.stopLoading()
        httpStatusCode = nil; diagnostics = ""; domain = request.url?.host ?? ""
        resolutionFallbackDiagnostic = nil
        loading = true; status = tr("browserLoading")
        address = request.url?.absoluteString ?? address
        activeNavigation = webView.load(request)
    }
    func invalidateCapture() {
        capture.invalidate(); supported = false
        resolutionTask?.cancel(); resolutionTask = nil
        if let operation = resolutionOperationID, let store {
            resolutionOperationID = nil
            Task { _ = try? await store.worker.request("operations.cancel", params: .object(["operationId": .string(operation)])) }
        }
        if let operation = importOperationID, let store {
            Task { _ = try? await store.worker.request("operations.cancel", params: .object(["operationId": .string(operation)])) }
        }
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        let decision = BrowserNavigationPolicy.decision(
            url: navigationAction.request.url,
            hasTargetFrame: navigationAction.targetFrame != nil,
            targetIsMainFrame: navigationAction.targetFrame?.isMainFrame == true,
            userActivated: navigationAction.navigationType == .linkActivated || navigationAction.navigationType == .formSubmitted,
            isDownload: navigationAction.shouldPerformDownload,
            capturing: capturing
        )
        switch decision {
        case .block:
            if navigationAction.targetFrame?.isMainFrame != false { status = tr("browserSchemeBlocked") }
            decisionHandler(.cancel); return
        case .openInCurrentFrame:
            decisionHandler(.cancel)
            beginNavigation(navigationAction.request)
        case .allow:
            if navigationAction.targetFrame?.isMainFrame == true { invalidateCapture(); activeNavigation = nil }
            decisionHandler(.allow)
        }
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse, decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
        if resolutionTask != nil && navigationResponse.isForMainFrame { decisionHandler(.cancel); return }
        if navigationResponse.isForMainFrame {
            httpStatusCode = (navigationResponse.response as? HTTPURLResponse)?.statusCode
            if let code = httpStatusCode, code >= 400 {
                supported = false
                status = String(format: tr("browserHttpFailed"), code)
                diagnostics = "HTTP \(code) · \(navigationResponse.response.url?.absoluteString ?? address)"
            }
        }
        decisionHandler(navigationResponse.canShowMIMEType && BrowserNavigationPolicy.allowsResponse(navigationResponse.response.url, isMainFrame: navigationResponse.isForMainFrame) ? .allow : .cancel)
    }
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        let decision = BrowserNavigationPolicy.decision(
            url: navigationAction.request.url, hasTargetFrame: navigationAction.targetFrame != nil,
            targetIsMainFrame: navigationAction.targetFrame?.isMainFrame == true,
            userActivated: navigationAction.navigationType == .linkActivated || navigationAction.navigationType == .formSubmitted,
            isDownload: navigationAction.shouldPerformDownload, capturing: capturing
        )
        if decision == .openInCurrentFrame { beginNavigation(navigationAction.request) }
        else { status = tr("browserSchemeBlocked") }
        return nil
    }
    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        guard !closed, resolutionTask == nil, activeNavigation == nil || activeNavigation === navigation else { return }
        activeNavigation = navigation
        loading = true; httpStatusCode = nil; diagnostics = resolutionFallbackDiagnostic ?? ""; status = tr("browserLoading"); invalidateCapture()
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard !closed, resolutionTask == nil, activeNavigation == nil || activeNavigation === navigation else { return }
        loading = false; address = webView.url?.absoluteString ?? address; domain = webView.url?.host ?? ""
        canBack = webView.canGoBack; canForward = webView.canGoForward
        if let code = httpStatusCode, code >= 400 {
            supported = false; status = String(format: tr("browserHttpFailed"), code)
            return
        }
        validateSupport()
    }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { failed(navigation, error) }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { failed(navigation, error) }
    private func failed(_ navigation: WKNavigation?, _ error: Error) {
        guard !closed, resolutionTask == nil, activeNavigation == nil || activeNavigation === navigation else { return }
        // A superseded request is not a failure of the currently selected source.
        if (error as NSError).code == NSURLErrorCancelled { return }
        loading = false; supported = false; status = tr("browserLoadFailed"); diagnostics = error.localizedDescription
    }
    func validateSupport() {
        guard let store, let url = webView.url?.absoluteString else { return }
        let generation = capture.generation, source = source
        Task {
            do {
                let result = try await store.worker.request("browser.getSupport", params: .object(["source": .string(source), "url": .string(url)]))
                guard generation == capture.generation, self.source == source else { return }
                supported = result["supported"].boolValue
                let isR18Home = source == "r18dev" && URL(string: url)?.host == "r18.dev" && URL(string: url)?.path == "/"
                status = supported ? String(format: tr("browserReady"), store.jobLabel(jobID)) : tr(isR18Home ? "browserManualSearch" : "browserUnsupported")
            } catch { guard generation == capture.generation else { return }; supported = false; status = tr("browserCaptureFailed"); diagnostics = error.localizedDescription }
        }
    }
    func close() { closed = true; invalidateCapture(); webView.stopLoading(); activeNavigation = nil; store?.browser = nil }
    func importPage() {
        guard let store, supported, !loading, !capturing, !store.working, !store.statusUnconfirmed else { return }
        capturing = true
        store.perform {
            defer { self.capturing = false; self.importOperationID = nil }
            do {
            try await store.flush(self.jobID)
            guard store.selectedID == self.jobID, let job = store.jobs.first(where: { $0.jobID == self.jobID }) else { throw BrowserCaptureError.staleNavigation }
            let revision = job.reviewRevision, source = self.source, generation = self.capture.generation
            let page = try await self.capture.capture(from: self.webView, maximumUTF8Bytes: self.maximumHtmlBytes)
            guard store.selectedID == self.jobID, store.jobs.first(where: { $0.jobID == self.jobID })?.reviewRevision == revision,
                  generation == self.capture.generation, source == self.source else { throw BrowserCaptureError.staleNavigation }
            let response = try await store.worker.request("browser.importPage", params: .object([
                "jobId": .string(self.jobID), "expectedReviewRevision": .number(Double(revision)), "source": .string(source), "url": .string(page.url), "html": .string(page.html)
            ]))
            self.importOperationID = response["operationId"].stringValue
            if generation != self.capture.generation || source != self.source || self.webView.url?.absoluteString != page.url {
                _ = try? await store.worker.request("operations.cancel", params: .object(["operationId": response["operationId"]]))
            }
            let result = try await store.waitOperation(response)
            store.update(result["job"]); store.previewStale = true
            self.diagnostics = ""
            self.status = String(format: tr("browserImportDone"), result["promotedFieldCount"].intValue)
            try await store.refresh()
            store.detailTab = "metadata"
            store.artworkSourceByJob.removeValue(forKey: self.jobID)
            self.importOperationID = nil
            self.close()
            } catch {
                store.display(error)
                self.errorMessage = store.errorMessage
                self.diagnostics = store.errorDetails
                self.status = store.errorMessage
                store.errorMessage = nil
                try? await store.refresh()
            }
        }
    }
}

struct BrowserWebView: NSViewRepresentable {
    let webView: WKWebView
    func makeNSView(context: Context) -> WKWebView { webView }
    func updateNSView(_ view: WKWebView, context: Context) {}
}

struct BrowserImportView: View {
    @ObservedObject var session: BrowserSession
    @EnvironmentObject var store: WorkspaceStore
    var body: some View {
        VStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    Label(tr("manualBrowser"), systemImage: "globe").font(.title3.weight(.semibold))
                    Spacer()
                    if let store = session.store { Text(store.jobLabel(session.jobID)).font(.callout).foregroundStyle(.secondary) }
                }
                HStack {
                    HStack(spacing: 6) {
                        ForEach(Array(session.targets.enumerated()), id: \.offset) { _, target in
                            let source = target["source"].stringValue
                            Button { session.chooseSource(source) } label: {
                                Text(target["displayName"].stringValue).frame(maxWidth: .infinity)
                            }
                            .buttonStyle(.bordered)
                            .tint(session.source == source ? Color.accentColor : Color.secondary)
                            .accessibilityAddTraits(session.source == source ? .isSelected : [])
                        }
                    }.frame(maxWidth: 480).accessibilityElement(children: .contain).accessibilityLabel(tr("browserSource"))
                    Spacer()
                    Button(tr("browserExternal")) { session.openExternally() }.disabled(!session.canOpenExternally)
                }
                Text(tr("browserSourceHint." + session.source)).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }.padding(16).background(Color(nsColor: .controlBackgroundColor)).disabled(store.working)
            Divider()
            HStack {
                Button { session.goBack() } label: { Image(systemName: "chevron.left") }.disabled(!session.canBack).help(tr("browserBack")).accessibilityLabel(tr("browserBack"))
                Button { session.goForward() } label: { Image(systemName: "chevron.right") }.disabled(!session.canForward).help(tr("browserForward")).accessibilityLabel(tr("browserForward"))
                Button { session.reload() } label: { Image(systemName: "arrow.clockwise") }.help(tr("browserReload")).accessibilityLabel(tr("browserReload"))
                TextField(tr("browserAddress"), text: $session.address).textFieldStyle(.roundedBorder).onSubmit { session.navigate() }.accessibilityLabel(tr("browserAddress"))
                Button(tr("browserOpen")) { session.navigate() }
            }.padding(12).disabled(store.working)
            HStack(alignment: .top, spacing: 12) {
                if session.loading { ProgressView().controlSize(.small) }
                Text(session.status ?? "").font(.caption).foregroundStyle(session.httpStatusCode.map { $0 >= 400 } == true ? .orange : .secondary).fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 8)
                Text(session.domain).font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
                if !session.diagnostics.isEmpty { Button(tr("browserReopenSource")) { session.reopenSource() }.font(.caption).disabled(store.working) }
            }.padding(.horizontal, 12).padding(.bottom, 8)
            if !session.diagnostics.isEmpty { DisclosureGroup(tr("technicalDetails")) { Text(session.diagnostics).font(.caption).textSelection(.enabled) }.font(.caption).padding(.horizontal, 12).padding(.bottom, 8) }
            if session.openedExternally { Text(tr("browserExternalNote")).font(.caption).foregroundStyle(.secondary).padding(.horizontal, 12).padding(.bottom, 8) }
            Divider()
            BrowserWebView(webView: session.webView).accessibilityLabel(tr("manualBrowser"))
            Divider()
            HStack {
                Text(tr("browserSessionNote")).font(.caption).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading)
                Button(tr("close")) { session.close() }.keyboardShortcut(.cancelAction).disabled(store.working)
                Button(tr("browserImport")) { session.importPage() }.buttonStyle(.borderedProminent).disabled(!session.supported || session.loading || store.working || store.statusUnconfirmed)
            }.padding(12)
        }.frame(width: 900, height: 640).interactiveDismissDisabled(store.working)
        .task { session.start() }
        .alert(tr("attention"), isPresented: Binding(get: { session.errorMessage != nil }, set: { if !$0 { session.errorMessage = nil } })) { Button(tr("ok"), role: .cancel) { session.errorMessage = nil } } message: { Text(session.errorMessage ?? "") }
    }
}
