# JavMetaLite

<img src="JavMetaLite.App/Resources/Brand/JavMetaLite-64.png" width="64" alt="JavMetaLite icon">

[简体中文](README.zh-Hans.md) · [繁體中文](README.zh-Hant.md) · **English** · [日本語](README.ja.md)

[![CI](https://github.com/Noredge/JavMetaLite/actions/workflows/ci.yml/badge.svg)](https://github.com/Noredge/JavMetaLite/actions/workflows/ci.yml)

> Current version: **1.2.3**.

A Windows app for reviewing and organizing local movie metadata, individually or in batches, with Jellyfin-compatible output.

![Single-movie workspace, v1.2.0](docs/images/javmetalite-v1.2.0-main-single.en.png)

Synthetic demo data. [More screenshots](docs/SCREENSHOTS-v1.2.0.md).

## Features

- Add movies or scan folders; group CD1/CD2 parts and review movies in a batch queue.
- Search with LibreDMM (recommended default), R18.dev or custom multi-source rules. JAVLibrary is manual web lookup only.
- Edit local NFO metadata, choose artwork sources and stills, and export NFO, poster, fanart and optional stills (`extrafanart`).
- Read `movie.nfo` and folder-level artwork in dedicated movie folders; retain their names when saving in place.
- Compare and delete local images in the viewer; the main window flags low-resolution covers for review.
- Preview file changes before saving; keep movies in place or organize them into a chosen folder. Four UI languages supported.

## Quick start

Windows 10/11 x64. No separate .NET Runtime installation; the embedded browser requires Microsoft Edge WebView2 Runtime.

1. Get a published portable ZIP from [Releases](https://github.com/Noredge/JavMetaLite/releases), or use the supplied local build. Verify its SHA-256 and extract it.
2. Run `JavMetaLite.exe`, then choose or drop movies/folders.
3. Check IDs, search and adjust metadata, images and save settings as needed.
4. Click Save and review the changes. Batch saving does not require opening every movie's preview.

## Keep in mind

- LibreDMM is recommended for batches. R18 rate limits can slow searches.
- The queue is not restored after closing. Removing queue entries does not delete movie files; confirmed local-image deletion is immediate.
- Back up important media. Saves stop on failure or cancellation; keep recovery folders if restoration fails.
- Sources may contain adult material. Respect their terms and applicable age/legal requirements.

## Build from source

On Windows 10/11 x64, install Git and the exact .NET SDK pinned in [global.json](global.json) (currently 10.0.400). In PowerShell:

```powershell
git clone https://github.com/Noredge/JavMetaLite.git
cd JavMetaLite
dotnet build .\JavMetaLite.App\JavMetaLite.App.csproj -c Release
dotnet run --project .\JavMetaLite.App\JavMetaLite.App.csproj -c Release --no-build
```

The build restores NuGet packages automatically; the first build needs network access. To create and verify a self-contained portable package from the repository root:

```powershell
.\scripts\New-ReleasePackage.ps1
.\scripts\Test-PortablePackage.ps1
```

The EXE folder, ZIP and SHA-256 files are written to `release/`. See [Development and testing](TESTING.md) for the full test suite.

## Native macOS preview

A separate SwiftUI/AppKit client is available as a local Apple Silicon build for macOS 15+.
It uses the same portable Core through a bundled, self-contained C# Worker. The current
M6 local candidate supports recursive import, local NFO/artwork, LibreDMM and R18.dev queries,
revisioned review/editing, artwork output, explicit multipart ordering, and batch saving
with whole-batch conflict preflight. Video organization uses full SHA-256 verified copies.
Every save requires a Worker-owned preview plan. The confirmation sheet is shown by default;
Confirm Save authorizes the listed NFO/artwork replacements without an extra overwrite checkbox.
An explicit skip-preview preference can skip only that sheet; video and cross-job conflicts,
revisions, fingerprints and file transactions remain enforced. Output, naming, destination,
query and panel settings are remembered automatically. The native client includes a bounded native WebKit manual import,
separately confirmed local-image Trash operations, four languages and native text Undo.
Browser content cannot call native file operations. M6 adds locked restores, source/build/payload
provenance, archive round-trip checks, CI and fail-closed signing/notarization preparation.
Late or externally modified NFO baselines and redirected input paths are refused before saving.
The original WPF client and Windows release scripts remain available.

```sh
./scripts/macos/build.sh --arch arm64 --configuration Debug
./scripts/macos/test.sh --arch arm64 --configuration Release
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
```

Builds require the SDK in `global.json` and Apple developer tools. Packaged apps include
their runtime. The current release policy is signed-only; no notarization submission or
stapling is performed. Local ad-hoc builds are not Developer ID signed. Three real IDs
have been checked against both live providers using isolated synthetic H.264 clips;
see [metadata acceptance](docs/macos/LIVE-PROVIDER-QA.md). The same three synthetic movies
also passed live cover/poster/37-sample output and ordered batch saving; the tested preview
fallback fix and exact candidate boundaries are recorded in
[artwork and batch acceptance](docs/macos/LIVE-ARTWORK-BATCH-QA.md).
Normal import now opens a discovery review sheet before queue admission, with exact-item confirmation and selectable scan diagnostics. Completed operation/preview history has bounded retention; expired save receipts are recovered by an explicit read-only status check without replay. See [discovery and native regression evidence](docs/macos/REGRESSION-DISCOVERY-QA.md).
Windows historical cases are tracked individually in [the Mac regression matrix](docs/macos/WINDOWS-CASE-REGRESSION.md), including remaining gaps and actual native evidence.
See [actual implementation and verification status](docs/macos/STATUS.md),
[Mac build and test instructions](docs/macos/TESTING.md), and
[native project notes](macos/README.md), and [release preparation/checklist](docs/macos/RELEASING.md).
Only the local Apple Silicon candidate has been exercised; Developer ID trust, Intel,
macOS 15 runtime and Windows runtime acceptance remain open. Windows version remains 1.2.3.

## More

[Release notes](docs/RELEASE-NOTES-v1.2.3.md) · [Docs](docs/README.md) · [Changelog](CHANGELOG.md) · [Development and testing](TESTING.md)

Logs and preferences are under `%LOCALAPPDATA%\JavMetaLite`.

[MIT License](LICENSE) · © 2026 Noredge · [Third-party notices](THIRD_PARTY_NOTICES.md). Not affiliated with source sites; their data is not covered by this project's license.
