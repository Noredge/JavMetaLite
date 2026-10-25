import Testing
import Foundation
@testable import JavMetaLiteMac

struct SaveReviewLocalizationTests {
    @Test func packagedConflictTranslationsPreserveCompletePathsAndUnknownDiagnostics() throws {
        let explanations = [
            "en": "Multiple movies use the same input or output path: ",
            "zh-Hans": "多部影片的输入或输出路径冲突：",
            "zh-Hant": "多部影片的輸入或輸出路徑衝突：",
            "ja": "複数の作品の入力または出力パスが競合しています："
        ]
        let path = "/合成 e\u{301}/100% %@/" + String(repeating: "長い名前-", count: 100) + "/DEMO-611.nfo "
        let original = "Cross-job input/output conflict: " + path
        for (language, explanation) in explanations {
            let localize: (String) -> String = {
                AppLocalization.string($0, preference: language, resources: AppLocalization.resources)
            }
            let display = SaveReviewText.conflict(original, localize: localize)
            #expect(Data(display.utf8) == Data((explanation + path).utf8), "Translate the explanation, never normalize, truncate or interpret the path.")
            let permission = SaveReviewText.conflict("Existing sidecar replacement requires explicit confirmation.", localize: localize)
            #expect(permission == localize("conflict.sidecarConsent"))
            #expect(permission != "conflict.sidecarConsent" && permission != original)
            let unknown = "Unrecognized diagnostic: " + path
            #expect(Data(SaveReviewText.conflict(unknown, localize: localize).utf8) == Data(unknown.utf8))
            #expect(SaveReviewText.conflict("Existing sidecar replacement requires explicit confirmation. " + path, localize: localize).hasSuffix(path))
        }
    }
}
