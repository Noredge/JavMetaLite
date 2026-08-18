# Automated testing

JavMetaLite uses three sequential, offline-capable test layers. The automated release gate never reads or modifies the user's media library and never depends on a live scraper website.

## Run the complete gate

```powershell
.\scripts\Test-Automated.ps1
```

Use a specific SDK executable when `dotnet` is not on `PATH`:

```powershell
.\scripts\Test-Automated.ps1 -DotNet "C:\path\to\dotnet.exe" -NoRestore
```

The projects run sequentially because the smoke and UI projects share build outputs from `JavMetaLite.Core`.

## Test layers

| Layer | Project | Purpose |
| --- | --- | --- |
| Core smoke | `JavMetaLite.SmokeTests` | Parsers, metadata merge, v0.5 multi-source orchestration/provenance, unified poster/fanart selection, safe local sidecar/NFO/image reads, XML-preserving round-trip writing, image conversion, organization and logs |
| File regression | `JavMetaLite.RegressionTests` | File layout and target-mode matrices, custom-root validation, same-volume moves, verified cross-volume copies, preview purity, NFO no-op/update, sidecar migration, overwrite/conflict policy, exact rollback, movie hashes and input validation |
| UI smoke | `JavMetaLite.UiSmokeTests` | WPF construction, source/candidate menus, v0.6 local metadata/artwork loading, v0.7 target-mode controls and live paths, editable valid NFO state, preview action kinds, failure isolation and safe defaults |

The regression runner supports discovery and category filters:

```powershell
dotnet run --project .\JavMetaLite.RegressionTests -- --list
dotnet run --project .\JavMetaLite.RegressionTests -- --category rollback
dotnet run --project .\JavMetaLite.RegressionTests -- --category target
dotnet run --project .\JavMetaLite.RegressionTests -- --category transfer
```

Available categories are `layout`, `target`, `transfer`, `overwrite`, `roundtrip`, `conflict`, `rollback`, and `validation`.

## Isolation rules

- Every filesystem regression receives a unique directory below `%TEMP%`.
- Test movies contain only synthetic bytes; real media files are never used.
- HTTP image responses are local in-memory fixtures; the automated gate performs no website requests.
- Multi-source orchestration uses fake providers to cover two-source success, partial and total failure, ID mismatch, per-source call counts, and diagnostics without live requests.
- Local NFO fixtures cover valid and partial metadata, unknown-node preservation, malformed XML, wrong roots, oversized files, and DTD/external-entity rejection without touching a real media folder.
- Synthetic local-image fixtures verify independent poster/fanart discovery, missing-counterpart behavior, invalid-image isolation, decoded dimensions, manual full-cover poster/fanart generation, and unchanged movie/source bytes.
- Round-trip fixtures verify a pure/cancelled preview, unchanged-NFO zero writes, selective field updates, unknown XML retention, known-sidecar migration, external NFO changes, byte-exact rollback, and unchanged movie hashes.
- Target fixtures verify all three destination modes, independent movie renaming, no duplicate number folder, absolute-root validation, file-occupied roots, movie conflicts, and correct atomic-move versus verified-copy selection.
- Transfer fixtures force the verified-copy path inside isolated temporary directories and verify independent SHA-256 checks, progress, cancellation, hash mismatch, late movie conflicts, post-commit rollback, source preservation, target cleanup, and unchanged successful movie bytes.
- WPF local fixtures verify the visible NFO and artwork sources, editable valid-NFO state, blocked invalid-NFO state, local-first online composition, preview action labels, manual restoration, invalid-file logging, and complete candidate reset on the next movie.
- Each test verifies and removes temporary transaction artifacts.
- A failed test returns a non-zero process exit code and prevents the gate from continuing.
- A fixed defect should receive a regression case before the fix is considered complete.

## Manual checks kept outside the gate

The following remain manual because they depend on external state or human visual judgment:

- Live LibreDMM, R18.dev and JAVLibrary availability or browser verification.
- Jellyfin library scanning and presentation.
- Visual quality of poster, fanart and sample images.
- The native Windows image picker and visual confirmation that a real manually selected complete cover produces the expected crops.
- The native Windows target-folder picker, compact layout at the supported minimum window size, and the wording/readability of long destination paths.
- Real Windows permission prompts, antivirus interference, forced process termination and power loss.
- Real cross-drive and UNC throughput, disconnect behavior, free-space reporting, and cancellation timing with a large movie.
- Final inspection of a packaged single-file executable.

These checks complement the automated gate; they are not replaced by it.

The focused v0.7 RC checklist is [`MANUAL-ACCEPTANCE-v0.7.0-rc1.md`](MANUAL-ACCEPTANCE-v0.7.0-rc1.md). It validates a packaged cross-drive Jellyfin round trip without repeating accepted dev2/dev3 safety cases; a real UNC run is optional when no share is available. The passing v0.6 and v0.5 checklists remain in [`MANUAL-ACCEPTANCE-v0.6.0-rc1.md`](MANUAL-ACCEPTANCE-v0.6.0-rc1.md) and [`MANUAL-ACCEPTANCE-v0.5.0-rc1.md`](MANUAL-ACCEPTANCE-v0.5.0-rc1.md).
