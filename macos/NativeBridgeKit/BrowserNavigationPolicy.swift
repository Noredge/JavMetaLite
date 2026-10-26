import Foundation

public enum BrowserNavigationDecision: Equatable, Sendable {
    case allow
    case openInCurrentFrame
    case block
}

/// Only ordinary, user-activated HTTP(S) links may replace a popup with a
/// navigation in the current web view. Script popups and downloads stay blocked.
public enum BrowserNavigationPolicy {
    public static func decision(
        url: URL?, hasTargetFrame: Bool, targetIsMainFrame: Bool,
        userActivated: Bool, isDownload: Bool, capturing: Bool
    ) -> BrowserNavigationDecision {
        guard !isDownload else { return .block }
        // Challenge widgets legitimately initialize nested blank/srcdoc frames.
        // This does not permit them as the main page, popup or captured URL.
        if hasTargetFrame && !targetIsMainFrame && allowsInternalFrame(url) { return .allow }
        guard BrowserPageCapture.allowsNavigation(url) else { return .block }
        guard !(capturing && (targetIsMainFrame || !hasTargetFrame)) else { return .block }
        if !hasTargetFrame { return userActivated ? .openInCurrentFrame : .block }
        return .allow
    }

    public static func allowsResponse(_ url: URL?, isMainFrame: Bool) -> Bool {
        BrowserPageCapture.allowsNavigation(url) || (!isMainFrame && allowsInternalFrame(url))
    }
    private static func allowsInternalFrame(_ url: URL?) -> Bool {
        // macOS 15 Foundation encodes the fragment marker of an opaque about:
        // URL. Recognize only that representation of these two inert subframes;
        // do not generally decode URLs or expand main-page/popup permissions.
        let address = url?.absoluteString.lowercased().replacingOccurrences(of: "%23", with: "#")
            .split(separator: "#", maxSplits: 1).first.map(String.init) ?? ""
        return ["about:blank", "about:srcdoc"].contains(address)
    }

    public static func externalURL(_ address: String) -> URL? {
        let url = URL(string: address.trimmingCharacters(in: .whitespacesAndNewlines))
        return BrowserPageCapture.allowsNavigation(url) ? url : nil
    }
}
