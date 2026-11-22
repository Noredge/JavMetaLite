# JavMetaLite

<img src="windows/JavMetaLite.App/Resources/Brand/JavMetaLite-64.png" width="64" alt="JavMetaLite icon">

[简体中文](README.zh-Hans.md) · [繁體中文](README.zh-Hant.md) · **English** · [日本語](README.ja.md)

[![Windows CI](https://github.com/Noredge/JavMetaLite/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Noredge/JavMetaLite/actions/workflows/ci.yml)
[![macOS CI](https://github.com/Noredge/JavMetaLite/actions/workflows/macos.yml/badge.svg?branch=main)](https://github.com/Noredge/JavMetaLite/actions/workflows/macos.yml)

> Windows **1.3.0** · macOS **1.1.0**.

A Windows and macOS app for reviewing and organizing local movie metadata, individually or in batches, with Jellyfin-compatible output.

## Download

| Platform | Requirements | Download |
| --- | --- | --- |
| Windows **1.3.0** | Windows 10/11 x64 | [Portable ZIP](https://github.com/Noredge/JavMetaLite/releases/download/v1.3.0/JavMetaLite-v1.3.0-win-x64-portable.zip) · [Release](https://github.com/Noredge/JavMetaLite/releases/tag/v1.3.0) |
| macOS **1.1.0** | Apple Silicon · macOS 15+ | [App ZIP](https://github.com/Noredge/JavMetaLite/releases/download/macos-v1.1.0/JavMetaLite-macOS-arm64-Release-adhoc.zip) · [Release](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.1.0) |

Verify the release SHA-256 before opening. Windows needs WebView2 for the embedded browser. The macOS package is ad-hoc signed and not notarized; see the [first-launch guide](macos/README.md#first-launch). Intel is unverified.

On Windows, drop files and folders together and choose which recognized movies to import. Review the poster, full cover and metadata side by side. Hide the queue when you need more room.

![Windows 1.3.0 queue, poster, full cover and movie details](docs/images/windows-1.3.0-workspace.en.png)

![macOS 1.1.0 movie review and save settings](docs/images/macos-1.1.0-workspace.en.png)

[More screenshots](docs/SCREENSHOTS.md) · Synthetic demo data.

## Features

- Add movies or scan folders; group CD1/CD2 parts and review movies in a batch queue.
- Search with LibreDMM (recommended default), R18.dev or custom multi-source rules. JAVLibrary is manual web lookup only.
- Edit local NFO metadata, choose artwork sources and stills, and export NFO, poster, fanart and optional stills (`extrafanart`).
- Read `movie.nfo` and folder-level artwork in dedicated movie folders; retain their names when saving in place.
- Look up a catalog number directly, or drop a movie to review its local data before searching online.
- Preview file changes before saving; keep movies in place or organize them into a chosen folder. Four UI languages supported.

## Build from source

Install Git and the exact .NET SDK in [global.json](global.json), then clone this repository. Run these commands from its root:

**Windows (PowerShell)**

```powershell
dotnet build .\windows\JavMetaLite.App\JavMetaLite.App.csproj -c Release
.\scripts\New-ReleasePackage.ps1
.\scripts\Test-PortablePackage.ps1
```

**macOS (Xcode or Command Line Tools with macOS 15+ SDK)**

```sh
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
./scripts/macos/test.sh --arch arm64 --configuration Release
```

Builds restore NuGet packages on first use. Outputs: `release/` (Windows), `artifacts/macos/` (macOS). [Windows](windows/README.md) · [macOS](macos/README.md)

## Repository

| Directory | Purpose |
| --- | --- |
| [windows/](windows/README.md) | Windows application and tests |
| [macos/](macos/README.md) | Native macOS application, Worker and tests |
| [shared/](shared/README.md) | Shared Core, portable imaging and tests |
| [docs/](docs/README.md) | Current guides, technical docs and historical records |
| [scripts/](scripts/README.md) | Build, test and release tools |

[Documentation](docs/README.md) · [Changelog](CHANGELOG.md) · [MIT License](LICENSE) · [Third-party notices](THIRD_PARTY_NOTICES.md)
