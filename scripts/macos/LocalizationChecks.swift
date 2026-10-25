import Foundation

@main
struct LocalizationChecks {
    static func main() throws {
        let resourcePath = CommandLine.arguments[1]
        let catalogPath = CommandLine.arguments[2]
        let fallbackPath = CommandLine.arguments[3]
        let lowercasePath = CommandLine.arguments[4]
        guard let resources = Bundle(path: resourcePath), let fallback = Bundle(path: fallbackPath),
              let lowercaseResources = Bundle(path: lowercasePath) else {
            fatalError("Localization fixture bundle could not be loaded")
        }
        let catalog = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: catalogPath))) as! [String: Any]
        let strings = catalog["strings"] as! [String: [String: Any]]
        var checks = 0
        func check(_ condition: @autoclosure () -> Bool, _ message: String) {
            guard condition() else { fatalError(message) }
            checks += 1
        }
        let languages = ["en", "zh-Hans", "zh-Hant", "ja"]
        check(AppLanguage.allCases.map(\.rawValue) == ["system"] + languages, "Picker choices must include system and all four languages")
        for language in languages {
            for (key, entry) in strings {
                let localizations = entry["localizations"] as! [String: [String: Any]]
                let unit = localizations[language]!["stringUnit"] as! [String: Any]
                let expected = unit["value"] as! String
                let actual = AppLocalization.string(key, preference: language, preferredLanguages: ["en-US"], resources: resources)
                check(actual == expected, "Actual Bundle lookup mismatch: \(language)/\(key): \(actual)")
                let lowercase = AppLocalization.string(key, preference: language, preferredLanguages: ["en-US"], resources: lowercaseResources)
                check(lowercase == expected, "SwiftPM lowercase bundle lookup mismatch: \(language)/\(key): \(lowercase)")
            }
            check(AppLanguage.normalizePreference(language) == language, "Saved explicit language changed")
            check(AppLanguage.resolve(preference: language, preferredLanguages: ["fr-FR"]) == language, "Explicit preference did not win")
        }
        let regionalCases: [(String, String)] = [("en-US", "en"), ("en_GB", "en"), ("ja-JP", "ja"), ("ja", "ja"),
            ("zh-Hant", "zh-Hant"), ("zh-TW", "zh-Hant"), ("zh-HK", "zh-Hant"), ("zh-MO", "zh-Hant"), ("zh-Hant-US", "zh-Hant"),
            ("zh-Hans", "zh-Hans"), ("zh-CN", "zh-Hans"), ("zh-SG", "zh-Hans"), ("zh", "zh-Hans"), ("zh-Hans-TW", "zh-Hans")]
        for (input, expected) in regionalCases {
            check(AppLanguage.resolve(preference: "system", preferredLanguages: [input]) == expected, "Regional language resolution failed: \(input)")
        }
        check(AppLanguage.resolve(preference: "system", preferredLanguages: ["fr-FR", "ja-JP"]) == "ja", "System preference order ignored")
        check(AppLanguage.resolve(preference: "system", preferredLanguages: ["fr-FR"]) == "en", "Unsupported system locale must fall back to English")
        check(AppLanguage.resolve(preference: "invalid", preferredLanguages: ["zh-TW"]) == "zh-Hant", "Invalid saved preference must follow system")
        check(AppLanguage.normalizePreference("invalid") == "system", "Invalid preference normalization failed")
        check(AppLocalization.string("englishFallback", preference: "ja", resources: fallback) == "English fallback value", "Missing selected-language key must use English")
        check(AppLocalization.string("__missing_key__", preference: "ja", resources: fallback) == "__missing_key__", "Unknown key fallback failed")
        check(AppLanguage.traditionalChinese.nativeName == "繁體中文" && AppLanguage.japanese.nativeName == "日本語", "Language names must be self-identifying")
        print("LOCALIZATION RUNTIME PASS: \(checks) checks; \(strings.count) keys × 4 languages × 2 resource layouts; regional selection and English fallback")
    }
}
