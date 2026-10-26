# Native macOS client

A SwiftUI/AppKit editor backed by a bundled, self-contained C# Worker over stdio JSON-RPC. Shared Core provides metadata, NFO and file transactions; the Windows WPF client is retained.

## Run

Download [macOS 0.4.0 Preview 1](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v0.4.0-preview.1), extract the App ZIP and open `JavMetaLite.app`. Apple Silicon and macOS 15+ are required; no .NET installation is needed. The preview is ad-hoc signed, without Developer ID or notarization; Gatekeeper acceptance is not established. Intel is unverified.

The main editor shows the cropped poster and full cover together. Click a queue row to inspect/edit; checkboxes alone control batch query/save scope. Settings are remembered. Save previews disk changes by default; Confirm Save authorizes the listed sidecar replacements. More Options may explicitly skip the sheet, while all Worker revision/conflict/transaction checks remain required.

Manual web import captures only the explicitly requested supported page and closes after successful import. Website verification is user-controlled and isolated to this app's WebKit session. Images can be reviewed, selected and sent to macOS Trash with a separate confirmation.

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
