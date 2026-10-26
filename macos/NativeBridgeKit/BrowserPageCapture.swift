import Foundation
import WebKit

public struct CapturedBrowserPage: Sendable, Equatable {
    public let url: String
    public let html: String
}
public enum BrowserCaptureError: Error, Equatable {
    case staleNavigation, invalidScheme, tooLarge, emptyDocument
}

/// No script message handlers or native bridge are installed in the web content.
/// This object evaluates one bounded, read-only script only on an explicit import.
@MainActor public final class BrowserPageCapture {
    public private(set) var generation = 0
    public init() {}
    public func invalidate() { generation += 1 }
    public nonisolated static func allowsNavigation(_ url: URL?) -> Bool {
        guard let url, ["http", "https"].contains(url.scheme?.lowercased() ?? ""),
              url.host != nil, url.user == nil, url.password == nil else { return false }
        return true
    }
    public func capture(from webView: WKWebView, maximumUTF8Bytes: Int) async throws -> CapturedBrowserPage {
        let expectedGeneration = generation
        let expectedURL = webView.url?.absoluteString
        guard Self.allowsNavigation(webView.url) else { throw BrowserCaptureError.invalidScheme }
        let limit = max(1, maximumUTF8Bytes)
        let script = """
        (() => {
          const html = document.documentElement ? document.documentElement.outerHTML : '';
          if (new TextEncoder().encode(html).byteLength > \(limit)) return {error: 'tooLarge'};
          return {url: location.href, html};
        })()
        """
        let raw = try await webView.evaluateJavaScript(script, in: nil, contentWorld: .defaultClient)
        guard expectedGeneration == generation, webView.url?.absoluteString == expectedURL else { throw BrowserCaptureError.staleNavigation }
        guard let result = raw as? [String: Any] else { throw BrowserCaptureError.emptyDocument }
        if result["error"] as? String == "tooLarge" { throw BrowserCaptureError.tooLarge }
        guard let url = result["url"] as? String, let html = result["html"] as? String, !html.isEmpty else { throw BrowserCaptureError.emptyDocument }
        guard url == expectedURL, Self.allowsNavigation(URL(string: url)) else { throw BrowserCaptureError.staleNavigation }
        guard html.utf8.count <= limit else { throw BrowserCaptureError.tooLarge }
        return CapturedBrowserPage(url: url, html: html)
    }
}
