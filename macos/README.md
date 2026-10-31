# JavMetaLite for macOS

A native editor for reviewing movie metadata and artwork, editing local NFO files and organizing media. macOS **1.0.0 (build51)** supports Apple Silicon and macOS 15 or later. Windows has its own release and download.

## Run

Download [macOS 1.0.0](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.0.0), extract the [App ZIP](https://github.com/Noredge/JavMetaLite/releases/download/macos-v1.0.0/JavMetaLite-macOS-arm64-Release-adhoc.zip) and open `JavMetaLite.app`. Apple Silicon and macOS 15+ are required; no .NET installation is needed. The package is ad-hoc signed, without Developer ID or notarization; Gatekeeper acceptance is not established. Intel is unverified. This is the project’s GitHub distribution format; it does not mean Apple has verified the publisher or scanned the app.

## First launch

1. Download the ZIP and matching SHA-256 from the project's GitHub Release, compare the checksum, extract the complete app and move `JavMetaLite.app` to Applications. Keep its internal Contents folder intact.
2. Try opening the app. If macOS blocks this trusted download because the developer cannot be verified or Apple cannot check it, use **System Settings → Privacy & Security → Open Anyway**, then confirm Open for JavMetaLite. This is an exception for this app. Follow [Apple's instructions](https://support.apple.com/en-us/102445); do not use the procedure for damaged-app or malware alerts.
3. When macOS asks for access to a folder you selected, allow access if you intend to work there. Folder privacy permission is separate from Gatekeeper approval. Start with disposable media and review the save preview before confirming changes.

Package integrity, Developer ID, notarization, system assessment and successful manual launch are recorded separately. Installation instructions are also bundled in `JavMetaLite.app/Contents/Resources/READ-ME.md` ([source](../docs/macos/PACKAGE.md)).

## Use the editor

The main editor shows the cropped poster and full cover together. Click a queue row to inspect/edit; checkboxes alone control batch query/save scope. Settings are remembered. Save previews disk changes by default; Confirm Save authorizes the listed sidecar replacements. More Options may explicitly skip the sheet, while all Worker revision/conflict/transaction checks remain required.

Manual web import captures only the explicitly requested supported page and closes after successful import. Website verification is user-controlled and isolated to this app's WebKit session. Images can be reviewed, selected and sent to macOS Trash with a separate confirmation.

If the engine process exits, the recovery screen retains the last review and unsent drafts. Export Review writes a JSON reference; quitting or closing offers export first, or a separately confirmed end without export. Canceling or failing export keeps the session open. A response timeout while the process remains alive still requires status reconciliation. Exports do not confirm disk outcomes or replay saves; reopen the app, inspect transaction recovery and reread files before reapplying edits.

Open Transaction Recovery in the save Inspector to inspect interrupted transactions. It lists transaction records and inspects the current source, output and temporary files. A compatible record can be archived only after its recorded before/after state matches the files and you confirm the result. Archival preserves the journal and never changes media. Export current review references before removing or reimporting affected movies; unsent drafts are not automatically restored from JSON. Legacy Preview records lack the necessary verification evidence and remain read-only. Follow the [manual recovery procedure](../docs/macos/TESTING.md#legacy-transaction-records) instead of deleting a warning record.

Removing movies from the queue does not delete their files. The queue and unsent drafts are not restored after normal exit; keep a review reference when you need to reapply edits. Generated image previews are cached within bounded limits and can be recreated from their sources.

## Build and test

Use the SDK pinned by [global.json](../global.json) and Xcode or Command Line Tools with a macOS 15+ SDK, then run from the repository root:

```sh
./scripts/macos/test.sh --arch arm64 --configuration Release
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
```

Outputs are under `artifacts/macos/`; test logs are under `artifacts/test-results/`. The app launches its in-bundle Worker directly and does not depend on a developer PATH. CLI tools never install a toolchain or change the global developer directory.

## Keyboard and behavior

Command-O imports, Command-F searches the queue, Command-R queries checked movies, Command-S saves, Command-Option-B opens manual import, Command-Option-I toggles the Inspector and Command-Option-L toggles the queue. Escape cancels sheets. Native text fields support Undo/Redo and composition; multiline plot keeps Return for newlines. Space toggles the viewed row's check, while all/none replaces the checked set across filters.

The image viewer offers side arrows, Close on the left, Fit and Actual Size. Mouse wheel/pinch zoom, precise trackpad pan and Command-scroll zoom are native adapters; physical-device acceptance remains separate from API tests. Cached-source selection does not start a new query, and late image results cannot replace another review.

[Release notes](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.0.0) · [Verified scope](../docs/macos/STATUS.md) · [Testing](../docs/macos/TESTING.md) · [Architecture](../docs/macos/ARCHITECTURE.md) · [Protocol](../docs/macos/PROTOCOL.md) · [Safety](../docs/macos/SAFETY-MATRIX.md) · [Releasing](../docs/macos/RELEASING.md)

[MIT license](../LICENSE) · [Third-party notices](../THIRD_PARTY_NOTICES.md).
