import SwiftUI
import Foundation

// Explicit alias selects the stable State wrapper when newer SDKs also expose a State macro.
typealias ViewState<Value> = SwiftUI.State<Value>

enum AppLanguage: String, CaseIterable, Identifiable {
    case system, english = "en", simplifiedChinese = "zh-Hans", traditionalChinese = "zh-Hant", japanese = "ja"
    var id: String { rawValue }
    var nativeName: String {
        switch self {
        case .system: return tr("followSystem")
        case .english: return "English"
        case .simplifiedChinese: return "简体中文"
        case .traditionalChinese: return "繁體中文"
        case .japanese: return "日本語"
        }
    }
    static func normalizePreference(_ value: String) -> String { AppLanguage(rawValue: value)?.rawValue ?? "system" }
    static func resolve(preference: String, preferredLanguages: [String] = Locale.preferredLanguages) -> String {
        if let choice = AppLanguage(rawValue: preference), choice != .system { return choice.rawValue }
        for language in preferredLanguages {
            let parts = language.replacingOccurrences(of: "_", with: "-").lowercased().split(separator: "-").map(String.init)
            switch parts.first {
            case "en": return "en"
            case "ja": return "ja"
            case "zh":
                if parts.contains("hant") { return "zh-Hant" }
                if parts.contains("hans") { return "zh-Hans" }
                return parts.contains(where: { ["tw", "hk", "mo"].contains($0) }) ? "zh-Hant" : "zh-Hans"
            default: continue
            }
        }
        return "en"
    }
}

enum AppLocalization {
    static var resources: Bundle {
        #if SWIFT_PACKAGE
        return Bundle.module
        #else
        return Bundle.main
        #endif
    }
    static func string(_ key: String, preference: String, preferredLanguages: [String] = Locale.preferredLanguages, resources: Bundle) -> String {
        let language = AppLanguage.resolve(preference: preference, preferredLanguages: preferredLanguages)
        let missing = "__JavMetaLite_missing_translation__"
        for code in language == "en" ? ["en"] : [language, "en"] {
            // SwiftPM 6.1 lowercases lproj names; Xcode preserves their source
            // spelling. Resolve literal directories, without Bundle's implicit
            // language fallback hiding an explicitly selected Chinese locale.
            guard let root = resources.resourceURL else { continue }
            for directory in Set([code, code.lowercased()]).sorted() {
                if let localized = Bundle(url: root.appendingPathComponent(directory + ".lproj", isDirectory: true)) {
                    let value = localized.localizedString(forKey: key, value: missing, table: "Localizable")
                    if value != missing { return value }
                }
            }
        }
        return key
    }
}

func tr(_ key: String) -> String {
    AppLocalization.string(key, preference: UserDefaults.standard.string(forKey: "interfaceLanguage") ?? "system", resources: AppLocalization.resources)
}
