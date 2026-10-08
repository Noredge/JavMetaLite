# Development and testing

Use Windows and the .NET SDK pinned in `global.json`.

## Automated gate

```powershell
./scripts/Test-Automated.ps1
```

This runs Core smoke tests, file-transaction regressions, WPF UI tests and package-definition checks. Tests use synthetic media and mocked website responses, not your media library or live scraper services.

The current suite includes 46 Core tests and 65 transaction cases, plus WPF checks for source selection, queue navigation, image preview, four-language layout and save safety. Cleanup regressions cover single/CD saves, both verification modes, cancellation, simulated device loss, locked temporary files and committed saves with cleanup warnings. Confirmed-part tests cover explicit ordering, unchanged automatic discovery, atomic saving, cancellation and existing-video protection. WPF checks cover choosing one duplicate, skipping, canceling, forced save preview, part ordering and dark list colors. Run a transaction category separately with:

```powershell
dotnet run --project JavMetaLite.RegressionTests -c Release -- --category folder-sidecar
```

## Portable package

```powershell
./scripts/New-ReleasePackage.ps1
./scripts/Test-PortablePackage.ps1
```

The package check verifies the actual ZIP inventory, EXE version, README, licenses and SHA-256. CI runs both scripts and uploads the verified package. Validate the artifact from the release commit rather than substituting an older local ZIP.

## Optional performance checks

- `scripts/Measure-Performance.ps1`: synthetic import/search measurements.
- `scripts/Test-SavePerformance.ps1`: save/download timing.
- `scripts/Test-ContinuousStability.ps1`: repeated operations in one WPF window.

Use each script's parameters to select a local SDK or output directory. Compare identical data and cache conditions; synthetic timings are not live-site speed guarantees.

## Manual checks

Live source availability, browser verification, Jellyfin scanning, image quality, real cross-drive/UNC behavior, permissions, antivirus and power-loss recovery require separate checks. Before release, inspect the packaged app and use disposable copies for save tests. Keep recovery files after an incomplete rollback.

See the [maintenance workflow](docs/MAINTENANCE.zh-Hans.md).
