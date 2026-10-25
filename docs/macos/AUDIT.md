# macOS portability audit — M0 / M1

Audit date: 2026-10-21 (America/Los_Angeles). This describes source review and executed checks, not a Windows or removable-volume acceptance report.

## Baseline and workspace

- Repository `Noredge/JavMetaLite`, branch created as `feature/macos-native` from clean `main` at `874b2dd524106189806e4d0c86589a0c8ee142c1` (v1.2.3). The implementation specification names exactly that commit; no baseline delta was found.
- This task uses a fresh checkout inside its working directory. The initial `git status --short` was empty. No existing developer checkout or uncommitted changes were reset, overwritten, or moved.
- No `AGENTS.md` was found in this repository or applicable parent directories. Read `README.md`, `TESTING.md`, and `docs/MAINTENANCE.zh-Hans.md` before editing.
- Keep `global.json` at SDK **10.0.400**, `rollForward: disable`. A task-local SDK was used; no SDK pin or global developer-directory setting was changed.
- Existing Windows gates remain `scripts/Test-Automated.ps1`, `New-ReleasePackage.ps1`, `Test-PortablePackage.ps1`, and `.github/workflows/ci.yml`. Tests are console applications; `dotnet test` alone is not their gate.

## Scan and disposition

Production and test C#/project files were searched for `System.Windows`, WPF bitmap/encoder types, `Dispatcher`, `Application.Current`, `WebView2`, registry/Win32/PInvoke, process launches, storage directories, root comparisons, path comparers, file sharing, replacement, cancellation, and rollback. Generated `bin/obj` files are not part of the source audit.

| Area / actual call sites | Disposition and resulting boundary |
| --- | --- |
| Core `.csproj` had Windows TFM and `UseWPF` | Changed to only `net10.0`. Core has no Windows UI reference. |
| `PosterImageProcessor` WPF bitmap decoding, `Int32Rect` right-half crop, JPEG95, original-JPEG fanart | Moved unchanged, including namespace and static surface, to `JavMetaLite.Imaging.Windows`. `WindowsArtworkImageProcessor` adapts it to the new narrow interface. The moved source was compared byte-for-byte against baseline and is identical. |
| `OutputService` static image calls | Required `IArtworkImageProcessor` constructor dependency; no default empty-image implementation. All Windows application and existing test construction sites inject the Windows adapter. |
| `ArtworkLocationHelper` → `LocalArtworkDiscovery` → `MovieJobLoader`; WPF preview callers | Explicit image dependency threaded through real import/preview code. Local and remote image streams are bounded to 100 MiB before decoding. The Mac Worker can impose a lower cache limit. |
| Mac image implementation | `JavMetaLite.Imaging.Skia` pins SkiaSharp and native macOS assets to 3.119.0. Decoding is capped at 40 million pixels. Raw pixel orientation, floor(width/2) right-half crop and JPEG passthrough match the original contract. PNG alpha is retained; new JPEG conversions explicitly flatten alpha on white. This last choice is documented portable behavior, not a claim of byte-identical WPF output. Mac P0 artwork **writing** remains disabled by capability. |
| `App/BatchSavePathConflicts` | Algorithm moved to Core using `BatchSavePlanItem(MovieJob, SavePlan, ReviewRevision)`. App contains only a conversion adapter. Read/write conflicts, protected skipped jobs and shared `extrafanart` namespace remain in one implementation. |
| `OrganizationPathPlanner.RequiresVerifiedCopy` | Root comparison is confined to the existing Windows policy. macOS/Unix volume identity is **Unknown**; every actual video move requires copy and full SHA-256. Same-path NFO editing does not copy video. File-size-only mode is upgraded to full hash on non-Windows. The Windows root-based free-space estimate is skipped on Unix until reliable mount lookup exists; transactional writes still detect capacity failure. Custom-root availability is rechecked at execution and before target-directory creation, so a missing selected root is not recreated. |
| `FileOrganizationService`, `MovieFileSet`, discovery | Shared `FileSystemPathSemantics` supplies exact portable comparisons and existing symlink resolution. Real names are not Unicode-normalized on disk. Path grouping does not silently merge case-distinct files. Symlink directories are not recursively followed; hidden/system directories are skipped. Explicit video aliases resolve to their original paths before grouping. |
| Case / Unicode / sidecar aliases | Unknown volume case behavior never authorizes a destructive alias operation. Case-folded/NFC equivalence is used conservatively to **detect conflicts**, not to rename or merge. Ambiguous sibling sidecars and direct symlink sidecars block writes. Canonical parent aliases such as macOS `/var` → `/private/var` are resolved; returned display paths can therefore be canonical absolute paths. Prepared canonical paths are revalidated during execution, before commit moves, and before rollback/cleanup; a newly introduced symlink ancestor causes refusal and recovery preservation. |
| `OrdinalIgnoreCase` elsewhere | Identifier, source, hostname, extension and metadata comparisons were retained. Local artwork URL/path collections still follow baseline normalization; P0 does not expose artwork output or organizing. Native volume case probing and full hard-link identity are not implemented; unknown identity is explicitly not proof of a same-file move. |
| NFO parsing / writing | Existing bounded reader already prohibits DTDs and has `XmlResolver = null`. Preserve `NfoRoundTripWriter`, `LocalMetadataBundle`, local-NFO blocked state, original filenames, unknown XML, actor attributes, and raw date/runtime/rating strings. Swift never writes NFO. |
| Series / ContentId | Baseline tests deliberately treat Series tags as opaque and do not author new Series values. Keep that output rule. These fields are carried for inspection, not offered as falsely writable P0 fields. Provider ContentId mapping also retains the existing writer semantics. |
| `MovieJob`, review and search | Retain `ReviewRevision`, manual/source candidates, `BeginSave(expectedReviewRevision)` and existing retry rules. Initial `ApplyOnlineSources` selects online values while retaining manual candidates; `ApplyRetriedOnlineSources` restores reviewed choices. Mac orchestration chooses the correct existing entry point. |
| Transaction / rollback / cleanup | Preserve existing file engine, staging, backups, source hash checks, never-overwrite-video protection, recovery exceptions, and cleanup-warning batch stop. Add progress observation of staging paths before directory creation. Optional `SavePlan.PreviewTargetExpectations` checks preview target existence/content at entry, after output generation, and immediately before each target commit; default empty keeps Windows caller behavior. |
| Preferences / logging | Existing `AppPreferencesStore` explicit directory and `AppLog.ConfigureDirectory` are usable unchanged. Worker supplies Application Support / Logs paths. Future-schema overwrite protection is retained. |
| WPF `MainWindow`, preview windows, dialogs, browser, Dispatcher, Win32 theme | Retained in the Windows client. Only image-service injection and shared batch-check forwarding changed. Import/search/retry/review orchestration was inspected; native controls, browser and visual behavior were not extracted into Core. |
| Old test programs | Retain their Windows/WPF target and assertions; update processor references only. New portable runner covers actual Core behavior and Skia without using WPF. |

## Executed validation

All commands below used the exact pinned .NET 10.0.400 SDK on arm64 macOS. `EnableWindowsTargeting` was a command argument only.

| Command | Outcome |
| --- | --- |
| `dotnet build JavMetaLite.Imaging.Skia/JavMetaLite.Imaging.Skia.csproj -c Release` | Exit 0; Core + Skia build, zero warnings/errors. |
| `dotnet build JavMetaLite.App/JavMetaLite.App.csproj -c Release -p:EnableWindowsTargeting=true` | Exit 0; WPF and Windows image adapter cross-compile, zero warnings/errors. |
| `dotnet build JavMetaLite.SmokeTests/JavMetaLite.SmokeTests.csproj -c Release -p:EnableWindowsTargeting=true` | Exit 0, zero warnings/errors. |
| `dotnet build JavMetaLite.RegressionTests/JavMetaLite.RegressionTests.csproj -c Release -p:EnableWindowsTargeting=true` | Exit 0, zero warnings/errors. |
| `dotnet build JavMetaLite.UiSmokeTests/JavMetaLite.UiSmokeTests.csproj -c Release -p:EnableWindowsTargeting=true` | Exit 0 after resolving the introduced shared/App batch-check type-name ambiguity with explicit test aliases; zero warnings/errors. |
| `dotnet run --project JavMetaLite.PortableTests/JavMetaLite.PortableTests.csproj -c Release` | Executed on macOS. See `STATUS.md` and current test output for the final count as coverage is extended. |

The initial Windows baseline test programs were not executed here because WindowsDesktop runtime is unavailable on macOS. Cross-compilation is **not** WPF UI, Windows image output, Windows transaction, or Windows packaging acceptance. CI retains those gates, and they must execute on Windows before release.

## M4 Core safety follow-up

The P0 boundaries above describe the initial delivery. M4 now exercises artwork generation, sidecar migration, verified video organization, confirmed multipart order and sequential batch execution through the same Core transaction engine. Current public capabilities and client delivery status are recorded in `STATUS.md`.

- Verified-copy execution rehashes the source before retiring it. If a source changes after the target was committed, rollback retains the verified target as recovery evidence instead of assuming any file at the original path is a redundant copy. Both single-video and multipart cases are covered.
- Sidecar source expectations are rechecked after potentially long video copying and immediately before retirement. Preserved sidecar copies must match their recorded SHA-256. Backup bytes are also checked after the move into the transaction backup, so a file changed between the earlier check and backup is restored on failure.
- Existing target-video protection, conservative Unix full-SHA copying, custom-root availability checks, cancellation-independent rollback and unresolved-cleanup batch stop remain active. `OrganizedSaveResult.WithAdditionalCleanupIssues` lets Worker journal/reload problems preserve a real Completed result while stopping the next batch item.
- `OutputService` now maintains its download deadline through response-body reads, including when using `ResponseHeadersRead`. The existing 45-second default, explicit encoded/pixel bounds and three-image remote sample batches remain. Failed complete sample replacement preserves original local samples.
- `MovieJob.RefreshLocalMetadataBaseline` refreshes a moved/preserved NFO baseline after an artwork-only save without rebuilding metadata, actor structures, manual review or provider candidates. An actual later NFO save verifies that retained edits and unknown XML survive this sequence.
- Offline R18.dev coverage exercises guessed-ID failure, discovered content-ID fallback, structured actors, rate-limit partial failure and targeted retry while retaining manual text and the selected artwork source. Test HTTP is injected; no live endpoint claim is made.
- A newly introduced local-artwork symlink is rejected before path canonicalization. Video-path additions use the shared portable comparer. Duplicate CD part groups produce a diagnostic while unrelated valid groups remain importable; no ambiguous group is silently merged.
- Portable validation: `dotnet run --project JavMetaLite.PortableTests/JavMetaLite.PortableTests.csproj -c Release --no-restore` exited 0, **37/37 cases, 208 assertions**. `artifacts/test-results/m4-core.log` records the run. Added cases use only temporary synthetic videos, generated JPEG images, local XML and injected HTTP handlers; they include actual outputs, late source changes, multipart cancellation after commit, second-movie batch cancellation and post-commit warnings.

These tests do not claim external-drive/SMB removal, power-loss, Windows runtime, or case-sensitive APFS hardware acceptance. The final gate records any subsequent cross-builds and expanded protocol coverage separately.

## Remaining platform verification

Native case-sensitive APFS volume behavior, hard-link identity, real external-volume disconnects, SMB, permissions/TCC, low disk space, forced power loss and recovery, and Intel native assets require separate checks. M4 video organization and artwork output are implemented and covered by synthetic transaction tests; those tests do not substitute for physical-volume acceptance. Windows runtime tests and package verification remain pending on a Windows machine. The current M0–M4 delivery status belongs in `STATUS.md`.
