# JavMetaLite for macOS

A native editor for reviewing movie metadata and artwork, editing local NFO files and organizing media. The **1.1.0** candidate supports Apple Silicon and macOS 15 or later. Windows has its own release and download.

## Run

Download the published [macOS 1.0.1](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.0.1), extract the [App ZIP](https://github.com/Noredge/JavMetaLite/releases/download/macos-v1.0.1/JavMetaLite-macOS-arm64-Release-adhoc.zip) and open `JavMetaLite.app`. Apple Silicon and macOS 15+ are required; no .NET installation is needed. The package is ad-hoc signed, without Developer ID or notarization; Gatekeeper acceptance is not established. Intel is unverified. This is the project’s GitHub distribution format; it does not mean Apple has verified the publisher or scanned the app.

## First launch

1. Download the ZIP and matching SHA-256 from the project's GitHub Release, compare the checksum, extract the complete app and move `JavMetaLite.app` to Applications. Keep its internal Contents folder intact.
2. Try opening the app. If macOS blocks this trusted download because the developer cannot be verified or Apple cannot check it, use **System Settings → Privacy & Security → Open Anyway**, then confirm Open for JavMetaLite. This is an exception for this app. Follow [Apple's instructions](https://support.apple.com/en-us/102445); do not use the procedure for damaged-app or malware alerts.
3. When macOS asks for access to a folder you selected, allow access if you intend to work there. Folder privacy permission is separate from Gatekeeper approval. Start with disposable media and review the save preview before confirming changes.

Package integrity, Developer ID, notarization, system assessment and successful manual launch are recorded separately. Installation instructions are also bundled in `JavMetaLite.app/Contents/Resources/READ-ME.md` ([source](../docs/macos/PACKAGE.md)).

## Use the redesigned editor (local build)

Dropping a movie reads its ID, local NFO and images. Choose a **Query source** and click **Search Metadata** (Command-R) to fetch online results. A folder or multipart scan opens a local selection sheet. **Look Up ID** (Command-K) lets you enter a number, choose a source and click **Search Metadata** without a file. Use **Link Movie** to attach the reviewed result before saving.

The **Result source** menu below the title switches between local and previously fetched metadata, preserving manual fields and selected images. The menu next to **Edit details** opens manual web import and custom source settings. The main review shows cast, maker, release date and runtime above the synopsis, followed by director, label, genres and rating. Click a field value or **Edit details** to edit in place and compare field candidates. **Done** applies the edits to the review. **Choose images** lets you compare covers and select stills. Apply the selection to the review, then use the bottom save action to preview the actual files. Save settings sit beside that action. The sidebar offers batch processing when there are multiple movies. Batch commands affect the checked rows.

Manual web import captures only the explicitly requested supported page and closes after successful import. Website verification is user-controlled and isolated to this app's WebKit session. Images can be reviewed, selected and sent to macOS Trash with a separate confirmation.

If the engine process exits, the recovery screen retains the last review and unsent drafts. Export Review writes a JSON reference; quitting or closing offers export first, or a separately confirmed end without export. Canceling or failing export keeps the session open. A response timeout while the process remains alive still requires status reconciliation. Exports do not confirm disk outcomes or replay saves; reopen the app, inspect transaction recovery and reread files before reapplying edits.

The Diagnostic Tools menu (•••) at the bottom right of the window includes **Transaction Records…** and **Export Review Drafts…**. Open **Transaction Records…** to inspect interrupted transactions. It lists transaction records and inspects the current source, output and temporary files. A compatible record can be archived only after its recorded before/after state matches the files and you confirm the result. Archival preserves the journal and never changes media. Export current review references before removing or reimporting affected movies; unsent drafts are not automatically restored from JSON. Legacy Preview records lack the necessary verification evidence and remain read-only. Follow the [manual recovery procedure](../docs/macos/TESTING.md#legacy-transaction-records) instead of deleting a warning record.

Removing movies from the queue does not delete their files. The queue and unsent drafts are not restored after normal exit; keep a review reference when you need to reapply edits. Generated image previews are cached within bounded limits and can be recreated from their sources.

## Build and test

Use the SDK pinned by [global.json](../global.json) and Xcode or Command Line Tools with a macOS 15+ SDK, then run from the repository root:

```sh
./scripts/macos/test.sh --arch arm64 --configuration Release
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
```

Outputs are under `artifacts/macos/`; test logs are under `artifacts/test-results/`. The app launches its in-bundle Worker directly and does not depend on a developer PATH. CLI tools never install a toolchain or change the global developer directory.

## Keyboard and behavior

Command-O imports, Command-F searches the queue, Command-R queries the current movie or the checked batch, Command-S previews that same scope (or saves when skip-preview is enabled), Command-Option-B opens manual import, Command-Option-I toggles the Inspector and Command-Option-L toggles the queue. Escape cancels sheets. Native text fields support Undo/Redo and composition; multiline plot keeps Return for newlines. In Batch edit, Check All and Uncheck All apply across filters.

The image viewer offers side arrows, Close on the left, Fit and Actual Size. Mouse wheel/pinch zoom, precise trackpad pan and Command-scroll zoom are native adapters; physical-device acceptance remains separate from API tests. Cached-source selection does not start a new query, and late image results cannot replace another review.

[Release notes](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.0.1) · [Verified scope](../docs/macos/STATUS.md) · [Testing](../docs/macos/TESTING.md) · [Architecture](../docs/macos/ARCHITECTURE.md) · [Protocol](../docs/macos/PROTOCOL.md) · [Safety](../docs/macos/SAFETY-MATRIX.md) · [Releasing](../docs/macos/RELEASING.md)

[MIT license](../LICENSE) · [Third-party notices](../THIRD_PARTY_NOTICES.md).

## Source layout

`JavMetaLiteMac/` contains the Swift app; `WorkerKit/` and `NativeBridgeKit/` provide native integration. `JavMetaLite.Worker/` contains the bundled .NET JSON-RPC engine and `JavMetaLite.Worker.RegressionHost/` is a nonpublishable test host. Shared Core and Skia code live in [shared/](../shared/README.md). SwiftPM and Xcode entry points remain here; run release scripts from the repository root. See the [script inventory](../scripts/README.md).
