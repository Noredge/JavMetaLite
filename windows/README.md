# Windows

Windows 10/11 x64 · WPF · .NET SDK pinned in [global.json](../global.json).

## Quick start (Windows)

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


## Build and run

From the repository root, in PowerShell:

```powershell
dotnet build .\windows\JavMetaLite.App\JavMetaLite.App.csproj -c Release
dotnet run --project .\windows\JavMetaLite.App\JavMetaLite.App.csproj -c Release --no-build
.\scripts\Test-Automated.ps1
.\scripts\New-ReleasePackage.ps1
.\scripts\Test-PortablePackage.ps1
```

The first build restores NuGet packages and needs network access. The portable EXE folder, ZIP and checksums remain in `release/`. Logs and preferences are under `%LOCALAPPDATA%\JavMetaLite`.

## Projects

| Directory | Purpose |
| --- | --- |
| `JavMetaLite.App/` | WPF application, UI resources and WebView2 integration |
| `JavMetaLite.Imaging.Windows/` | Windows image processing |
| `JavMetaLite.SmokeTests/` | Core behavior smoke tests |
| `JavMetaLite.RegressionTests/` | File transactions and save regressions |
| `JavMetaLite.UiSmokeTests/` | WPF behavior, layout and performance checks |

[Shared components](../shared/README.md) · [Testing](../docs/windows/TESTING.md) · [Maintenance](../docs/maintainer/MAINTENANCE.zh-Hans.md) · [Scripts](../scripts/README.md)
