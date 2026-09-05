# Automated testing

JavMetaLite uses three sequential, offline-capable test layers plus read-only package-version and bundled-notice checks. The automated release gate never reads or modifies the user's media library and never depends on a live scraper website.

Current version: **1.2.0**, with accepted preview.56 functionality and preferences schema v12. The user confirmed testing was complete and approved finalization on 2026-09-05, without reporting every individual test or environment. The [finalization record](docs/FINAL-v1.2.0.zh-Hans.md) records the new version's gate, package identity and outstanding checks; prior preview passes do not prove the new package. The [delivery checklist](docs/RELEASE-v1.2.0.zh-Hans.md) records acceptance and publication checks separately. The pre-optimization preview.46 checkpoint is `152e7df`; older preview-specific notes below are historical coverage, not current defaults or pending work. Offline-index implementation remains paused.

## CI and SDK identity

`global.json` pins the reviewed .NET SDK 10.0.400 without roll-forward. Install that SDK for source builds; update the SDK and bundled runtime notices together when upgrading. Windows CI runs the same three test layers, builds the portable package, checks its ZIP contents, executable version, README, full licenses and SHA-256 files, then uploads the verified package as a seven-day artifact. CI does not create a public Release automatically.

To repeat package validation after a successful gate:

```powershell
.\scripts\New-ReleasePackage.ps1
.\scripts\Test-PortablePackage.ps1
```

Layout tests pin and verify their own window's actual width so a small CI desktop cannot silently narrow the requested 1120-DIP layout. Original clipping, source-switch stability and breakpoint assertions remain enabled. The full gate includes a constrained-host sizing regression; `dotnet run --project JavMetaLite.UiSmokeTests -- --layout` additionally repeats both four-language layout matrices with a 1040-DIP host limit, without changing display settings or production UI.

## Run the complete gate

```powershell
.\scripts\Test-Automated.ps1
```

Use a specific SDK executable when `dotnet` is not on `PATH`:

```powershell
.\scripts\Test-Automated.ps1 -DotNet "C:\path\to\dotnet.exe" -NoRestore
```

The projects run sequentially because the smoke and UI projects share build outputs from `JavMetaLite.Core`.

If an older test process holds the default build output, use a separate per-project output directory without reinstalling dependencies:

```powershell
.\scripts\Test-Automated.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -Configuration Release -NoRestore -BuildOutputDirectory bin/release-validation/
```

Do not pass a shared absolute output directory: the relative path keeps each project's output separate. UI-test exceptions are reported with a non-zero exit code instead of escaping into Windows crash reporting.

## Repeatable performance measurements

```powershell
.\scripts\Measure-Performance.ps1 -DotNet .\.dotnet-sdk\dotnet.exe
.\scripts\Measure-Performance.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -SearchOnly -Output artifacts/performance/search-current.json
.\scripts\Measure-Performance.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -SearchHotspots -Output artifacts/performance/hotspots-current.json
```

These are optional measurements, not timing-based pass/fail gates. They create synthetic fixtures under the output directory, reuse the installed SDK/packages with no restore, and make no live HTTP requests. Run sequentially; results include all three repetitions at 100/500/1,000 movies, UI heartbeat gaps, view resets, allocations and working set. Samples remain for reuse. See the [preview.47 closeout and raw evidence](docs/CLOSEOUT-v1.2.0-preview.47.zh-Hans.md) for interpretation, limitations and baseline reproduction.

`-SearchHotspots` is a separate fixed-size call-cost test: 1,000 jobs, 100 direct calls per measurement, three repetitions across searchable/completed/mixed-failure states. It measures UI-thread allocations without pumping the dispatcher inside measured intervals; it does not use `Sizes`/`Repetitions` and cannot be combined with `-SearchOnly`. Use the end-to-end search mode to confirm overall impact. See the [preview.49 report and raw before/after records](docs/CLOSEOUT-v1.2.0-preview.49.zh-Hans.md).

## Current regression additions

- Preview.56: single/multipart preservation of 51 local samples plus 50 online downloads, local choices after the online limit, and local read failures aborting without changing originals (`localsamples` category). Batch UI preflight rejects overlapping output/retirement paths before confirmation; viewer-source refresh rejects stale writes, and canceled/failed comparison during retry restores a usable state. Four-language default/minimum-width checks measure actual heading, removal-button and target-selection text. Package regressions reject incomplete or mismatched dependency notices. See the [final-review fix report](docs/FIX-v1.2.0-preview.56.zh-Hans.md) for the new gate and package evidence.

- Preview.55: Windows sharing/lock-only retry classification, bounded attempts and cancellation; single/multipart transient commit locks, canceled commit followed by successful recovery, and persistent recovery locks retaining backups with unchanged movie bytes. The `sharing` category covers these cases. Four-language WPF checks distinguish deferred, missing, failed and cached previews; actual batch completion advances without extra preview HTTP requests.
  - Existing validation on 2026-09-04: Core 46/46, file transactions 40/40, complete WPF and package-script checks PASS. The final transaction rerun passed 40/40; the corrected sharing-test observer passed five repeated rounds of 4/4. See the [preview.55 report](docs/FIX-v1.2.0-preview.55.zh-Hans.md) for log paths and independent portable extraction/hash evidence. Documentation preparation does not rerun or replace the future final gate.
- Preview.53/54: cover-size thresholds (including 147x200 portrait fallbacks), local-import dimension reuse without additional reads/HTTP, first-valid fallback, bounded background checks, cancellation, stale source/ID results, unknown versus low resolution, yellow queue badges and save eligibility. Ordinary 800x539 and 400x539 images are not flagged.
- Preview.52: up to three current-movie online sample downloads; stable order/deduplication, cancellation drain, full-replacement failure protection and output/hash parity. Optional offline benchmark: `./scripts/Test-SavePerformance.ps1 -DotNet ./.dotnet-sdk/dotnet.exe`. Synthetic speedups are not live-site guarantees.
- Preview.51: real WPF slow-preview and held Up/Down navigation, boundary behavior, release/restart, explicit focus changes and cancellation; queue-originated input cannot change language while the queue is temporarily disabled.
- Preview.50: canceled/revisited previews, missing-image cache and delayed viewer results; optional same-window continuous-use checks are described below.

- Preview.49: compares queue issue, batch-search and retry eligibility against the preview.48 expressions over empty/Unicode whitespace/normalized/free-form IDs, selected states, searching/canceled/failed/reviewed jobs, save failures/conflicts and all current source modes (including case-insensitive provider names and excluded JAVLibrary/unknown sources). Checks ID edits during search and bounded allocation for 10,000 successful-path predicate rounds. No elapsed-time assertions or changed website/save policies.
  - Validated locally on 2026-09-04: Core 46/46, file transactions 30/30, complete WPF gate and independent portable extraction/version/checksum PASS. Logs: `artifacts/performance49/final-gate.log` and `package.log`; hashes and measured limitations are recorded in the preview.49 report.

- Preview.48: four-language single/batch search-toolbars at 930/1000/1120/1320 logical window widths and immediately around each toolbar breakpoint (540/650/720). Checks default-window single-row alignment, narrow fallback, full source-menu labels, compact selected text, stable source switches, text/gear/arrow clearance and batch hints below actions. WPF-rendered toolbar crops are written to `artifacts/ui-preview48/` for review; these are renderings at 96 DPI, not captures from separately configured 125%/150% Windows desktops.
  - Validated locally on 2026-09-04: Core 46/46, file transactions 30/30, complete WPF gate and package-version validation PASS. Command: `./scripts/Test-Automated.ps1 -DotNet ./.dotnet-sdk/dotnet.exe -Configuration Release -NoRestore -BuildOutputDirectory bin/preview48-final/`. Gate log: `artifacts/ui-preview48/final-gate.log`; packaging log: `artifacts/ui-preview48/package.log`. No live site traffic or user-library changes.
  - Portable ZIP independently extracted: four expected files, EXE version `1.2.0-preview.48`, matching staging EXE and archive checksum. ZIP SHA-256: `4A64DAD03B62CDD67E79982B915B0BCB14B42520F1A7305FD20D92469DE7A4FB`.

- Preview.47: 1,000-row select/deselect without list resets, coalesced notifications, issue-filter changes, LRU and decoded-pixel limits, original versus preview dimensions, full-size viewer loading, cross-movie four-language cache restoration, placeholder formatting/fallback, localized discovery errors, background multipart/duplicate import, corrupt sample exclusion and pre/mid-batch cancellation. Existing source-routing, save and actual WPF layout checks remain enabled.

- Preview.46: LibreDMM is the default for fresh settings and omitted source arguments; old Auto values migrate to LibreDMM, while explicit R18/Custom/manual choices and Custom rules persist. The 71-movie matrix independently asserts the expected provider list. WPF verifies four selector entries, no Auto/JAVLibrary automatic option, 71 DMM requests with zero R18, and localized batch hints (normal DMM, amber R18/Custom, hidden single/manual) within the 930x650 layout.

- Preview.45: 71-movie automatic-source matrix, single-source complete/failure/cancellation semantics, current-source-only retries, preserved excluded attempt history/manual JAVLibrary provenance, and legacy JAVLibrary preference migration to manual mode. Custom preferences apply only to Custom mode.
- Preview.45 WPF: clicks the real queue search button with fake HTTP transports: 71 LibreDMM requests, zero R18/JAVLibrary requests, even if the selector is changed programmatically during the run. Also covers R18-only, Auto/Custom, current-movie parity, disabled/guarded manual mode, source-scoped retry, retained manual current-movie JAVLibrary menu/import, and five automatic-source selector entries. No live bulk requests are made.

- Preview.44: deterministic 71-movie R18 fixtures cover a transient 429 (72 requests, 71 complete results) and a long shared cooldown (one R18 request, 71 partial results); selected retries skip successful providers. Tests include HTTP-date clock skew, HTML 429 classification, two-retry budget, request spacing, shared manual-import cooldown, cancel during queue/cooldown/network waits, per-request timeouts, retained retry eligibility, and automatic fallback versus explicit field/artwork/sample review (including preloaded local samples).
- Preview.44 WPF: partial-source badge and 429 tooltip, issue filtering, selection-scoped batch retry, correct partial totals, recovered-state clearing and four-language minimum-width retry layout. These are synthetic fixtures, not a claim of live 71-movie site throughput.

- Preview.23–42: precise scrolling across owned surfaces; minimal keyboard and repeated queue navigation; no-search Extra Fanart preservation; resolution-based, source-consistent artwork; source-scoped viewer rows, staged image selection, immediate confirmed local deletion and global viewer positioning.
- Preview.43: all/partial replacement-download failure preserves single/multi-CD files; retry preserves manual/local/provider selections, intentional blanks, actor images and manually imported provenance; absent selections can still receive recovered data.
- Preview.43 WPF: close requests wait for cancellation recovery or an operation that finishes without observing cancellation; repeated close cannot bypass waiting; version text follows the assembly and all four languages.
- Package validation runs before the .NET projects. It checks default/explicit project versions, malformed/mismatched metadata and EXE identity without building, deleting or downloading. `New-ReleasePackage.ps1 -ValidateOnly` is also read-only; omit `-Version` to derive the package version from the app project. Actual packaging verifies the published EXE and retains version-specific checksums.

## Test layers

| Layer | Project | Purpose |
| --- | --- | --- |
| Core smoke | `JavMetaLite.SmokeTests` | Parsers including current and legacy JAVLibrary rating markup, CD1/CD2 sibling discovery and grouping, multipart folder-sidecar preference, movie-file/ID-folder input resolution, unified mixed file/folder discovery, isolated per-movie working contexts, unified batch selection, bounded batch search, revision-backed stale-preview rejection, ordered batch-save coordination, metadata merge, custom per-field metadata/artwork selection and fallback, multi-source provenance, unified poster/fanart selection, safe local sidecar/NFO/image reads, reversible Jellyfin ID-title formatting, canonical ID output, unmanaged `Series:` preservation, image conversion, schema-v12 preference storage, organization and logs |
| File regression | `JavMetaLite.RegressionTests` | File layout and target-mode matrices, custom-root validation, same-volume and multi-CD moves, full-SHA and fast cross-volume copies, multi-CD verified copies, preview purity, NFO no-op/update, preview.6-to-preview.7 multipart sidecar migration, overwrite/conflict policy, exact single/multi-part rollback, movie hashes and input validation |
| UI smoke | `JavMetaLite.UiSmokeTests` | WPF construction, single/batch mode switching, scrollable multi-path discovery, recursive logical-movie import preview and one-movie direct folder routing, unified safe preview skipping, multi-movie queue add/switch/select/remove/clear/filter/navigation, automatic CD1/CD2 queue merging and ID-folder targeting, Series-field removal, reviewable Community rating and source switching, 50-item queue scale, batch search, per-job save settings, mixed valid/issue batch preview, issue-only blocking and overwrite confirmation, source/candidate menus, local metadata/artwork loading, target-mode controls and live paths, remembered verification preferences, four-language switching, transfer warnings, failure isolation and safe defaults |

The regression runner supports discovery and category filters:

```powershell
dotnet run --project .\JavMetaLite.RegressionTests -- --list
dotnet run --project .\JavMetaLite.RegressionTests -- --category rollback
dotnet run --project .\JavMetaLite.RegressionTests -- --category target
dotnet run --project .\JavMetaLite.RegressionTests -- --category transfer
```

Available categories are `layout`, `target`, `transfer`, `overwrite`, `roundtrip`, `conflict`, `rollback`, `validation`, `downloads`, and `sharing`.

## Isolation rules

- Every filesystem regression receives a unique directory below `%TEMP%`.
- Test movies contain only synthetic bytes; real media files are never used.
- HTTP image responses are local in-memory fixtures; the automated gate performs no website requests.
- Multi-source orchestration uses fake providers to cover two-source success, partial and total failure, ID mismatch, per-source call counts, and diagnostics without live requests.
- Local NFO fixtures cover valid and partial metadata, standard-versus-unknown XML detection, unknown-node preservation, malformed XML, wrong roots, oversized files, and DTD/external-entity rejection without touching a real media folder.
- Synthetic local-image fixtures verify independent poster/fanart discovery, missing-counterpart behavior, invalid-image isolation, decoded dimensions, manual full-cover poster/fanart generation, and unchanged movie/source bytes.
- Round-trip fixtures verify a pure/cancelled preview, unchanged-NFO zero writes, selective field updates, unknown XML retention, known-sidecar migration, multipart `movie.nfo / poster.jpg / fanart.jpg` migration, external NFO changes, byte-exact rollback, and unchanged movie hashes.
- Target fixtures verify all three destination modes, independent movie renaming, no duplicate number folder, absolute-root validation, file-occupied roots, movie conflicts, and correct atomic-move versus verified-copy selection.
- Transfer fixtures force both cross-volume paths inside isolated temporary directories. They verify independent full SHA-256 checks, fast-mode size checks without a target reread, distinct progress/preview states, cancellation, hash mismatch, late movie conflicts, post-commit rollback, source preservation, target cleanup, and unchanged successful movie bytes.
- WPF local fixtures verify the visible NFO and artwork sources, editable valid-NFO state, blocked invalid-NFO state, online-after-search composition, preview action labels, manual restoration, invalid-file logging, and complete candidate reset on the next movie.
- WPF queue fixtures verify that single mode hides queue complexity, multiple files switch to batch mode, switching preserves unsaved per-movie edits, one checkbox controls batch search and save, state filters and previous/next navigation select the expected jobs, selected items can be removed, the queue can be cleared without deleting files, and a 50-item synthetic queue remains addressable with recycling virtualization and lightweight placeholders.
- Movie-job fixtures verify that two independent movie contexts do not share metadata or artwork state, replaced review sessions are disposed, manual candidates survive an online refresh, and a reset clears stale sources and save blocks.
- Input-discovery fixtures verify default-recursive and optional top-level-only scans, mixed files and folders, overlapping-root deduplication, nested videos, logical `CD1/CD2` grouping, supported-extension filtering, ignored-file counts, missing-path diagnostics, deterministic paths, and no writes outside isolated temporary directories.
- Batch-search fixtures verify a two-job concurrency ceiling for each provider, per-job source attempts and failure isolation, manual-candidate retention, ID-mismatch diagnostics, exclusion, retryable cancellation, and untouched not-started jobs without live network access.
- Batch-save fixtures verify selection persistence across edits, stale-preview rejection before execution, strict sequential ordering, first-error stop, completed/not-started result preservation, completed-item deselection, cancellation, mixed valid/issue and issue-only grouped previews, and explicit overwrite approval.
- Preview 3 UI smoke coverage verifies successful items leave the visible queue, the queue viewport stays explicitly dark, scrolling uses pixel units, and the batch save action uses the primary blue treatment.
- Preview 4 UI smoke coverage verifies pixel scrolling in the grouped batch preview, the safe skip-preview policy and remembered preference, plus immediate queue removal/clearing without a confirmation parameter.
- Preview 5 UI smoke coverage verifies that successful batch searches invalidate stale empty artwork previews while failed items retain their prior preview cache.
- Preview 7 coverage verifies automatic ID-folder targeting for multi-part movies, folder-level sidecar preference, safe preview.6 sidecar retirement after commit, updated NFO artwork references, and unchanged single-file naming.
- Preview 8 UI coverage verifies visible Community rating values, local/scraper/manual source badges, manual editing, and restoring a scraper candidate.
- Preview 9 coverage verifies JAVLibrary's current `#video_review .text .score` markup takes priority, parenthesized ratings are normalized without scale conversion, legacy selectors still work, and the UI shows the `JAVLibrary` source.
- Preview 10 coverage verifies the default-on global title option, all-queue synchronization, remembered schema-v7 persistence, idempotent `<ID> · <Title>` generation, reversible opt-out, canonical `javnumber` output, provider-ID preservation, and unknown-XML-safe migration of existing NFO files.
- Preview 11 coverage verifies the default title-from-R18.dev profile, per-field LibreDMM/R18.dev selection, actual selected-source provenance, missing-preferred-value fallback, single/batch consistency, four-language configuration UI, and schema-v8 profile persistence even when save-preference memory is disabled.
- Preview 12 coverage verifies the all-LibreDMM default, artwork source selection and missing-cover fallback, preview.11 profile migration, customized-profile preservation, and an explicit dark popup template with readable text in the configuration window.
- Preview 13 coverage verifies default-recursive mixed input discovery, logical CD grouping, default-all selection, duplicate and unknown-ID states, pixel-scrolled discovery results, and direct single-mode routing for a folder tree containing one logical multi-CD movie.
- Preview 14 coverage verifies a pixel-scrolled multi-input path viewport, one shared single/batch skip-preview control, clean-plan skipping, forced previews for issues and overwrite conflicts, and schema-v9 legacy-option migration.
- Preview 15 coverage raises a real high-resolution mouse-wheel event and verifies a quarter-notch moves the discovery path viewport by exactly six physical pixels; the same handler is attached to logical-movie results.
- Preview 16 coverage verifies explicit-source routing, automatic single-success direct opening, ambiguous/custom provider menus, trusted provider URLs, detail-page recognition, candidate-only imports, blank-field filling, preserved field/artwork choices, mismatched-ID rejection, localized button states, and provider-specific browser copy.
- Preview 17 coverage verifies the distinct “Web lookup” action/menu copy, R18.dev `id=...` human-page routing, conversion of prior JSON result URLs, rejection of raw JSON and lookalike hosts as visible detail pages, exact background `combined=.../json` retrieval, parsed candidates, and provider-specific WPF state.
- Preview 18 coverage verifies R18.dev page-state classification for 404, server failure, navigation failure, trusted valid details, site home, and lookalike hosts; WPF checks the non-blocking missing/unavailable banners, distinct colors, requested-ID text, and disabled-until-ready import action.
- Preview 19 coverage verifies that R18.dev web lookup uses an authoritative result `content_id` when available, otherwise opens a pre-filled home-page search, and recognizes the site's HTTP-200 “page does not exist” body as missing rather than unavailable.
- Preview 20 coverage verifies that “Manual web lookup” keeps the same label and three-provider menu across automatic single-source, multi-source, Custom, and manual-entry modes, with no source-mode-to-browser shortcut remaining.
- Preview 21 coverage verifies that unresolved R18.dev manual lookups reuse provider discovery to route `ABF-193` through its authoritative `118abf193` content ID without re-requesting resolved targets; confirmed browser imports select every non-empty web field and artwork, retain missing local fields, and preserve local and manual candidates for rollback.
- Preview 22 coverage verifies the “Remember current settings” copy and tooltip, persistence/restoration of all six search modes, schema-v10 migration to the recommended multi-source mode, invalid-value normalization, disabled-memory isolation, and WPF source-selector restoration.
- Online/local composition fixtures verify local values and artwork before search, online non-empty text and artwork defaults after search, local fallback for missing online fields, identical-value online provenance, and retained manual/local candidates that can be selected again.
- Input fixtures verify a directly dropped movie, one top-level movie inside an ID folder, rejection of empty and multi-movie folders, and no recursive scan into nested folders.
- Preference fixtures verify missing, valid, damaged, and future-version JSON; legacy schema migrations through v12; bounded and deduplicated recent roots; persistent history clearing; atomic replacement cleanup; explicit opt-in; remembered unified preview skipping, title-ID output, and fast transfer; globally persisted custom source profiles; and safe defaults for legacy, disabled, or invalid configuration.
- v0.8 recent-root UI fixtures verify the compact dark menu, selection, current-entry removal, full clearing, retained current paths, unavailable-root blocking, and zero directory creation.
- Localization fixtures verify four complete and matching resource dictionaries, immediate switching of primary controls and custom source configuration, schema-v12 language, source-profile, search-mode, and save-option persistence, disabled setting-memory isolation, and safe migration of older users. A cancellable slow-provider fixture also verifies the 10-second production safeguard with a short test timeout and confirms that a successful source is retained.
- Each test verifies and removes temporary transaction artifacts.
- A failed test returns a non-zero process exit code and prevents the gate from continuing.
- A fixed defect should receive a regression case before the fix is considered complete.

## Continuous-use stability (preview.50)

`scripts/Test-ContinuousStability.ps1 -DotNet .\.dotnet-sdk\dotnet.exe` runs five rounds of 1,000 synthetic movies in the **same** WPF window. Override `-Count`, `-Rounds` and `-OutputDirectory` for shorter probes. It requires existing restored dependencies and makes no live HTTP requests; fixtures/settings/logs stay under its output directory for reuse.

Each round imports synthetic NFO/artwork, checks duplicate import, visits up to 36 movies through queue selection, opens the full-image viewer, switches all four languages, cancels a simulated DMM batch, then exercises 10% HTTP 500 failures and source-scoped retry before clearing. Cache bounds, zero remaining requests and collection of removed MovieJob objects are assertions. `continuous.json` also records post-GC managed memory, working set and handles; working set is diagnostic, not a leak threshold or performance promise. GC occurs outside timing. This test does not cover long live-network sessions or real-media saves.

The regular gate separately runs fast cancellation/revisit and canceled-source-change regressions, checks reuse of completed/missing-artwork previews, and verifies that a delayed viewer result cannot replace the current image and closing cancels outstanding loads.

## Manual checks kept outside the gate

The following remain manual because they depend on external state or human visual judgment:

- Live LibreDMM, R18.dev and JAVLibrary availability or browser verification.
- Jellyfin library scanning and presentation.
- Visual quality of poster, fanart and sample images.
- The native Windows image picker and visual confirmation that a real manually selected complete cover produces the expected crops.
- The native Windows target-folder picker, compact layout at the supported minimum window size, and the wording/readability of long destination paths.
- Real Windows permission prompts, antivirus interference, forced process termination and power loss.
- Real cross-drive and UNC throughput in both full and fast modes, disconnect behavior, free-space reporting, and cancellation timing with a large movie.
- Final inspection of a packaged single-file executable.

These checks complement the automated gate; they are not replaced by it.

Before publishing a release, run the complete automated gate and inspect the packaged executable on Windows. The short release acceptance should cover startup, one live metadata search, save-preview clarity, a real metadata save, unchanged movie SHA-256, language switching, and archive checksum verification. Test a real cross-drive or UNC destination when that environment is available.
