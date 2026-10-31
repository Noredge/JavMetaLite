# Native macOS client

A SwiftUI/AppKit editor backed by a bundled, self-contained C# Worker over stdio JSON-RPC. Shared Core provides metadata, NFO and file transactions; the Windows WPF client is retained.

## Run

Download [macOS 0.4.0 Preview 2 (build47)](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v0.4.0-preview.2), extract the [App ZIP](https://github.com/Noredge/JavMetaLite/releases/download/macos-v0.4.0-preview.2/JavMetaLite-macOS-arm64-Release-adhoc.zip) and open `JavMetaLite.app`. Apple Silicon and macOS 15+ are required; no .NET installation is needed. The package is ad-hoc signed, without Developer ID or notarization; Gatekeeper acceptance is not established. Intel is unverified. The project also permits stable ad-hoc releases on GitHub after the required validation; this policy is not Apple publisher verification or a malware scan.

## First launch

1. Download the ZIP and matching SHA-256 from the project's GitHub Release, compare the checksum, extract the complete app and move `JavMetaLite.app` to Applications. Keep its internal Contents folder intact.
2. Try opening the app. If macOS blocks this trusted download because the developer cannot be verified or Apple cannot check it, use **System Settings → Privacy & Security → Open Anyway**, then confirm Open for JavMetaLite. This is an exception for this app. Follow [Apple's instructions](https://support.apple.com/en-us/102445); do not use the procedure for damaged-app or malware alerts.
3. When macOS asks for access to a folder you selected, allow access if you intend to work there. Folder privacy permission is separate from Gatekeeper approval. Start with disposable media and review the save preview before confirming changes.

No global Gatekeeper change or quarantine-removal command is needed in these instructions. Package integrity, Developer ID, notarization, system assessment and successful manual launch are recorded separately. Installation instructions are also bundled in `JavMetaLite.app/Contents/Resources/READ-ME.md` ([source](../docs/macos/PACKAGE.md)).

## Use the editor

The main editor shows the cropped poster and full cover together. Click a queue row to inspect/edit; checkboxes alone control batch query/save scope. Settings are remembered. Save previews disk changes by default; Confirm Save authorizes the listed sidecar replacements. More Options may explicitly skip the sheet, while all Worker revision/conflict/transaction checks remain required.

Manual web import captures only the explicitly requested supported page and closes after successful import. Website verification is user-controlled and isolated to this app's WebKit session. Images can be reviewed, selected and sent to macOS Trash with a separate confirmation.

If the engine process exits, the recovery screen retains the last review and unsent drafts. Export Review writes a JSON reference; quitting or closing offers export first, or a separately confirmed end without export. Canceling or failing export keeps the session open. A response timeout while the process remains alive still requires status reconciliation. Exports do not confirm disk outcomes or replay saves; reopen the app, inspect transaction recovery and reread files before reapplying edits.

The current unreleased build48 source adds Transaction Recovery in the save Inspector. It lists transaction records and inspects the current source, output and temporary files. A compatible record can be archived only after its recorded before/after state matches the files and you confirm the result. Archival preserves the journal and never changes media. Export current review references before removing or reimporting affected movies; unsent drafts are not automatically restored from JSON. Legacy Preview records lack the necessary verification evidence and remain read-only. Follow the [manual recovery procedure](../docs/macos/TESTING.md#legacy-transaction-records) instead of deleting a warning record.

Generated artwork previews have a 256 MiB / 1024-file session budget. Preview leases protect images still held by the editor; unused files can be evicted, and removing movies releases their references. Startup reclaims only recognized inactive sessions with an acquired ownership lock. Unmarked older caches are retained conservatively.

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

[Current status](../docs/macos/STATUS.md) · [Testing](../docs/macos/TESTING.md) · [Architecture](../docs/macos/ARCHITECTURE.md) · [Protocol](../docs/macos/PROTOCOL.md) · [Safety](../docs/macos/SAFETY-MATRIX.md) · [Releasing](../docs/macos/RELEASING.md)
