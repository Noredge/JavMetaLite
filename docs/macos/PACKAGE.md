# JavMetaLite 1.0 for macOS

Verify the ZIP against the SHA-256 on the project's GitHub Release, extract the
complete ZIP and move JavMetaLite.app to Applications. Keep its internal
Contents folder intact. The app includes its own .NET runtime and does not require
an installed .NET SDK, Homebrew, or a developer PATH. The minimum deployment
target is macOS 15. This release is for Apple Silicon (arm64); Intel is not
verified. The release notes identify the actual tested systems and limitations.

The GitHub package uses ad-hoc signing without Developer ID or notarization.
Ad-hoc signing verifies local integrity; it does not establish Apple publisher
verification, a malware scan or automatic Gatekeeper acceptance. Manual approval
may be needed on first launch.

## First launch / 首次启动 / 首次啟動 / 初回起動

- **English:** Try opening the app first. For a trusted download blocked because
  the developer cannot be verified or Apple cannot check it, use **System Settings
  → Privacy & Security → Open Anyway**, then confirm Open for JavMetaLite.
- **简体中文：** 先尝试打开。可信的下载若因“无法验证开发者”或“Apple 无法检查”被阻止，
  到 **系统设置 → 隐私与安全性 → 仍要打开**，确认打开 JavMetaLite。
- **繁體中文：** 先嘗試開啟。可信的下載若因「無法驗證開發者」或「Apple 無法檢查」遭阻擋，
  到 **系統設定 → 隱私權與安全性 → 強制打開**，確認開啟 JavMetaLite。
- **日本語：** 一度起動を試みます。信頼できるアプリが開発元の未確認や Apple による確認不可を
  理由にブロックされた場合は、**システム設定 → プライバシーとセキュリティ → このまま開く**
  で JavMetaLite の起動を確認します。

This grants a per-app exception, not a global security change. Do not use it for
damaged-app or malware alerts. 此流程仅针对单个 App，不适用于损坏或恶意软件警报。
此流程僅針對單一 App，不適用於損毀或惡意軟體警告。
この例外はこのアプリだけに適用されます。破損やマルウェアの警告には使用しないでください。
[Apple's instructions](https://support.apple.com/en-us/102445) · [Apple 说明](https://support.apple.com/zh-cn/102445) · [Apple 說明](https://support.apple.com/zh-tw/102445) · [Apple の手順](https://support.apple.com/ja-jp/102445).

Folder access prompts are separate privacy permissions. Allow access to the
folders you intend to edit, and test with disposable media before real files.
文件夹访问授权与启动放行是两件事；请先用临时影片验证。
資料夾存取授權與啟動放行是兩件事；請先用暫存影片驗證。
フォルダーへのアクセス許可は別の設定です。まず不要なテスト用動画で確認してください。

## Package contents and verification

The Worker is launched from Contents/Helpers/JavMetaLiteWorker.bundle. App data,
diagnostics and recovery journals belong to the normal user Library locations,
not the app folder. Save previews must be reviewed before writing metadata or
organizing media. Moving images to macOS Trash uses its own confirmation.

BUILD-MANIFEST.json records the exact source-input hashes, pinned toolchain,
resolved dependency hashes and requested build/signing mode. The external ZIP
manifest records the signed payload and archive hashes. These are reproducibility
records, not a promise of byte-identical signed ZIPs across machines or dates.
The archive SHA-256 detects changes when compared with a separately trusted copy;
it is not a substitute for validating the code-signing identity.

The project license and full macOS dependency licenses/notices are included here.
Upstream website metadata is not bundled as product content or covered by those
licenses. No updater, telemetry upload, or automatic GitHub Release publishing is
enabled by these packaging scripts.
