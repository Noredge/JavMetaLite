# Changelog

## v0.7.0 — Stable

- Promotes RC3 without functional changes after P01–P03 and movie SHA-256 verification all passed.
- Adds three explicit destination modes: keep beside the movie, organize into a source-side number folder, or organize beneath a user-selected custom root.
- Keeps movie renaming independent while previewing every final movie, NFO, artwork, and screenshot path before writing.
- Uses atomic same-volume moves and verified cross-volume/UNC copies with target-side staging, independent SHA-256 verification, cancellation, late-conflict protection, and rollback.
- Prefers fresh non-empty online text fields after a successful search while retaining local NFO and manual candidates plus existing local artwork.
- Shows the unknown-XML preservation notice only when the loaded NFO actually contains extra XML; lossless round-trip behavior remains unchanged.
- Final acceptance confirmed standard and extended NFO preview wording, unknown XML preservation, and unchanged movie SHA-256.

## v0.7.0-rc3 — Conditional unknown-XML notice

- Records RC2 C01–C06 and movie SHA-256 verification as passed.
- Detects whether a loaded NFO actually contains XML outside the recognized Jellyfin/Kodi structure, including custom elements, attributes, comments, or processing instructions.
- Shows `更新 NFO` for a standard document and adds `（保留未知 XML）` only when such content was detected.
- Applies the same condition to the Save-button tooltip while keeping the existing lossless round-trip behavior unchanged.
- Adds standard/extended NFO reader coverage and a file-plan regression for both preview descriptions.
- Leaves online-after-search defaults, local artwork, file organization, cross-volume verification, and rollback unchanged.

## v0.7.0-rc2 — Online defaults after search

- Records the complete RC1 R01–R06 acceptance as passed before making this focused behavior correction.
- Keeps local NFO values selected when a movie is first opened, but selects the latest non-empty online text values after a successful search.
- Falls back field-by-field to the local NFO when the selected online result has no value, so searching never clears useful local metadata.
- Keeps local NFO and prior manual text values in each field menu, including when an online and local value are identical; users can switch sources at any time.
- Leaves the unified poster/fanart selection unchanged, so existing local artwork remains selected after a text search.
- Keeps the accepted custom-target, cross-volume verification, cancellation, rollback, and UNC behavior unchanged.
- Adds focused Core and WPF coverage plus a short RC2 manual retest before stable promotion.

## v0.7.0-rc1 — Release candidate

- Promotes the accepted dev3 build without functional changes and freezes the v0.7 custom-destination scope.
- Offers three explicit destinations while keeping movie renaming independent and every final movie/NFO/artwork path visible before writing.
- Retains atomic same-volume moves and verified cross-volume/UNC copies with target-side staging, independent SHA-256 verification, progress, cancellation, late-conflict protection, and rollback.
- Carries forward the passed dev2 D01–D08 and dev3 T01–T05/T08 manual checks plus the complete offline automated gate.
- Treats a live UNC-share run as optional for this RC because no test share is available; automated UNC planning and verified-transfer failure coverage remain required and passing.
- Adds a focused RC checklist for packaged startup, real cross-drive organization, Jellyfin ingestion, local metadata reloading, no-op preservation, and final movie hashes.

## v0.7.0-dev3 — Verified cross-volume transactions

- Opens different-drive and UNC custom targets through a verified copy transaction while retaining the existing atomic move path for same-volume destinations.
- Streams the movie into a target-side temporary area, hashes the source during copy, hashes the completed target copy independently, and commits only when both SHA-256 values match.
- Removes the source movie and migrated sidecars only after the target movie and metadata have committed; cancellation and failures restore metadata, preserve the source, and clean temporary targets.
- Adds live copy/verification status and a per-operation cancel button, plus a local-volume free-space preflight and late-conflict protection.
- Moves “影片重命名为番号” into the save-method row and increases the visual gap between the custom-folder picker and Save button.
- Adds offline verified-copy coverage for success, cancellation, hash mismatch, late target conflicts, post-commit rollback, progress reporting, source preservation, and movie hashes.
- Records D01–D08 as passed for dev2 and advances dev3 to focused real-drive/UNC testing.
- Passed T01–T05 and T08 manual checks for UI layout, real cross-drive preview/save/cancel/conflict behavior, and same-volume regression; T06–T07 were skipped because no UNC test share was available.

## v0.7.0-dev2 — Custom target location UI

- Replaced the legacy organization checkbox with three explicit target modes: keep beside the movie, use a number folder at the source, or use a number folder below a custom root.
- Added a native Windows folder picker plus an editable absolute-path field; the last valid custom root and selected mode remain available while the app stays open.
- Added a live final-movie path below the controls and kept movie renaming independent from target selection.
- Renamed the save-preview summary to `最终影片`; the preview continues to list the absolute destination of every planned movie and metadata change.
- Enables same-volume custom-root execution through the existing transactional save path and retains movie SHA-256.
- Blocks different-drive and UNC targets in both UI and Core until dev3 adds verified copy/delete semantics.
- Added invalid directory-segment checks plus automated same-volume execution, UI mode switching, duplicate-folder prevention, final-path preview, and cross-drive blocking coverage.

## v0.7.0-dev1 — Custom target path foundation

- Added explicit `VideoDirectory`, `SourceNumberFolder`, and `CustomRootNumberFolder` destination modes while preserving the existing two-boolean API and v0.6 UI behavior.
- Added a pure organization path planner that calculates the target movie, NFO, artwork, and sample-image base location without creating or moving files.
- Kept movie renaming independent from destination selection and avoided duplicate nesting when the selected custom root is already named after the normalized movie ID.
- Requires a fully qualified custom root and reports a file occupying that root, an occupied target folder, or an existing target movie as a blocking conflict.
- Added offline regression coverage for same-drive paths, different drive letters, UNC roots, validation, collision protection, and planning purity.
- Deliberately leaves custom-root UI selection and cross-volume/network transaction execution for later v0.7 development builds.

## v0.6.0 — Stable

- Promotes RC1 without functional changes after the complete automated gate, dev4 B01–B07 checks, and RC1 R01–R06 Jellyfin integration acceptance passed.
- Adds safe re-editing of existing local Jellyfin metadata with local values retained as the initial review choice and online sources available per field.
- Selectively updates supported NFO fields while preserving unknown XML elements, attributes, comments, provider data, and unchanged sidecar bytes.
- Loads an existing poster/fanart pair as one artwork source and supports explicit replacement from LibreDMM, R18.dev, JAVLibrary, or a manually selected complete cover.
- Extends change previews, organization, conflict detection, and transactional rollback across the movie, NFO, poster, and fanart workflow.
- Final acceptance confirmed Jellyfin rescan and second-load consistency, no-op zero writes, preserved unknown XML, and unchanged movie SHA-256.

## v0.6.0-rc1 — Release candidate

- Promotes the fully accepted dev4 build without functional changes and freezes the v0.6 feature set.
- Completes the single-movie local editing loop: load an existing NFO and artwork, review local and online candidates, preview actual changes, and save safely beside the movie.
- Retains selective NFO updates with unknown XML preservation, byte-identical local artwork retention, explicit replacement, sidecar migration, external-change detection, and transactional rollback.
- Keeps malformed NFO files blocked from every write path and keeps movie files protected from overwrite or content modification.
- Adds a focused real-Jellyfin round-trip checklist covering initial ingestion, mixed-source edits, artwork replacement, rescan, and a second JavMetaLite load.
- RC1 is feature-frozen; only defects found by final integration testing will be changed before the stable release.
- Passed R01–R06 final integration acceptance: existing Jellyfin metadata loaded correctly, mixed local/online edits and artwork replacement saved safely, Jellyfin rescanned the result, the second JavMetaLite load was lossless, movie SHA-256 stayed unchanged, and unknown XML survived.

## v0.6.0-dev4 — Safe NFO round-trip saves

- Enables saving after a valid local NFO has been loaded; malformed or unsafe NFO files remain protected from all write paths.
- Updates only supported metadata fields inside the original XML document while retaining unknown elements, attributes, comments, provider IDs, and unmanaged nested data.
- Avoids rewriting an unchanged NFO and labels the save preview as `生成`, `更新`, `保持不变`, or `替换图片` according to the actual operation.
- Preserves a selected local poster/fanart pair byte-for-byte, including its original image extensions, and safely migrates known sidecars when folder organization or movie renaming is enabled.
- Replaces local artwork only when an online or manually selected complete cover is active and the corresponding image outputs are enabled.
- Detects a loaded NFO changed by another program after review and refuses to overwrite it.
- Extends the transaction to back up and restore NFO, poster, and fanart files in reverse order if any metadata commit or final movie move fails.
- Added offline coverage for no-op and cancelled previews, unknown-XML preservation, sidecar migration, external-change conflicts, preview action labels, exact rollback hashes, and unchanged movie bytes.
- Passed B01–B07 manual acceptance, covering real NFO edits, cancelled and no-op saves, artwork replacement, explicit direct-save behavior, sidecar organization, external-change protection, and unchanged movie SHA-256.

## v0.6.0-dev3 — Local artwork and manual complete covers

- Detects and previews same-name local poster and fanart files when a movie is selected, with `本地图片` as the default unified artwork source.
- Keeps local poster and fanart as one review candidate while preserving each file independently; a missing counterpart remains visibly absent and is never fabricated from the other image.
- Keeps local artwork selected after online metadata searches while adding LibreDMM and R18.dev covers to the same compact source menu.
- Adds `选择本地完整封套…` through the Windows image picker; the selected JPG, JPEG, PNG, or WEBP drives both poster and fanart previews and can be used by the existing output pipeline.
- Reads manual covers directly from disk, validates their image content and size, and leaves the source cover and movie bytes unchanged.
- Ignores invalid local sidecars independently, reports them in the status/log, and continues loading available NFO metadata or the valid counterpart.
- Prevents an existing poster/fanart sidecar pair from being reused as a complete-cover output source, avoiding silent cross-role conversion.
- Added offline Core and WPF coverage for local discovery, partial/invalid pairs, unified selection, manual cover output dimensions, UI previews, online composition, logging, and movie-byte preservation.
- Passed A01–A05 manual acceptance, including native Windows file selection, real artwork quality, invalid-image isolation, and unchanged movie SHA-256.

## v0.6.0-dev2 — Local NFO review in the main window

- Automatically detects and safely loads a same-name NFO when a movie is selected.
- Shows loaded values with the existing `本地 NFO` per-field source badges and reports the complete NFO path in the status bar and log.
- Keeps non-empty local values selected when online sources are searched, while allowing online metadata to fill local blank fields.
- Places local, LibreDMM, R18.dev, JAVLibrary, and subsequent manual values into the existing candidate menus without adding a second editor.
- Rebuilds the review session for every new movie and online search so candidates from an earlier movie or earlier response cannot leak forward.
- Treats malformed or unsafe local NFO files as read-only failures with a clear status and log entry, leaving the original file untouched.
- Disables saving whenever a pre-existing local NFO was detected; lossless round-trip updates remain intentionally deferred to dev4.
- Added Core and WPF smoke coverage for local defaults, online blank filling, actor-source consistency, manual restoration, stale-candidate clearing, read-only UI state, and invalid-NFO handling.

## v0.6.0-dev1 — Local NFO read-only foundation

- Added case-insensitive detection of same-name NFO, poster, and fanart sidecars beside the selected movie.
- Added a bounded, DTD-disabled NFO reader that rejects malformed XML, external entities, oversized files, and roots other than `<movie>`.
- Parsed the current editable movie fields, multiple directors/genres/actors, actor thumbnail URLs, and `Label:` / `Series:` tags into a local metadata source snapshot.
- Preserved an independent clone of the complete original XML document, including unknown elements, attributes, comments, and whitespace, for the later round-trip save stage.
- Kept dev1 isolated from the main window and all write paths: selecting and saving behavior remains identical to v0.5.0.
- Added offline tests proving successful and partial reads, sidecar matching, zero file mutations, unknown XML preservation, and security rejection behavior.

## v0.5.0 — Stable

- Promoted RC1 without functional changes after all six focused integration checks passed.
- Adds independent LibreDMM and R18.dev searching with reviewable per-field candidates and recoverable manual edits.
- Keeps one lightweight shared source selector for poster and fanart while leaving screenshots tied to the overall search result.
- Preserves the accepted v0.4 preview, overwrite protection, optional organization, rollback, logging, JAVLibrary fallback, and Jellyfin-compatible output.
- Acceptance confirmed final metadata and artwork selection, safe cancel/save behavior, Jellyfin ingestion, and unchanged movie SHA-256.

## v0.5.0-rc1 — Release candidate

- Promoted the fully tested dev5-r5 build without adding new functionality.
- Freezes the lightweight single-movie scope: independent LibreDMM/R18.dev search, per-field review, manual-value restoration, and one shared poster/fanart source selector.
- Keeps screenshots tied to the selected search result instead of adding cross-source image merging or separate screenshot controls.
- Retains the v0.4 preview, overwrite, organization, rollback, logging, and Jellyfin-compatible output behavior.
- Adds a focused RC acceptance checklist covering final integration without repeating the complete v0.4 acceptance suite.

## v0.5.0-dev5-r5 — Stable artwork spacing

- Reserved a fixed, text-free status row between poster and fanart before search.
- Kept the poster/fanart spacing identical after the labeled complete-cover dimensions appear, for example `横板封套：2184×1468`.
- Added a UI layout regression that compares both states.

## v0.5.0-dev5-r4 — Simplified cover status

- Removed the “waiting for cover” and “search to show cover” placeholder messages from the artwork area.
- Show only the complete cover dimensions after the fanart preview loads successfully.
- Kept the unified poster/fanart source selector and all save behavior unchanged.

## v0.5.0-dev5-r3 — Cover header alignment

- Moved the shared poster/fanart source badge to the upper-right of the cover header, matching the label-and-source layout used by metadata fields.
- Kept the poster and fanart source locked together; screenshots and save behavior are unchanged.
- Added a UI smoke assertion for the source badge position.

## v0.5.0-dev5-r2 — Unified cover source menu

- Added one compact source badge between the poster and fanart previews, using the same dark two-line candidate menu as metadata fields.
- Locked poster and fanart to a single selected source and refreshed both previews together after a switch.
- Kept screenshots outside this selector: no cross-source screenshot merge and no separate screenshot-source control.
- Hid the selector when no cover exists and made it read-only when only one source has a usable cover.
- Added offline coverage for default source matching, unified switching, immutable source snapshots, screenshot isolation, and the WPF candidate menu.

## v0.5.0-dev5-r1 — Lightweight artwork flow

- Retired the independent cover/screenshot source selectors introduced in dev5 after hands-on testing showed that they added unnecessary product weight.
- Stopped combining LibreDMM and R18.dev screenshot collections into a new user-visible output mode.
- Restored the simple rule that poster, fanart, and screenshots follow the current search result; users can select another main source and search again when its artwork is unsuitable.
- Kept dev4 multi-source text-field candidates, manual text editing, existing Jellyfin filenames, save preview, overwrite protection, and rollback behavior unchanged.
- Kept duplicate-image suppression as an internal safety detail when a single source returns repeated screenshot content.

## v0.5.0-dev5 — Artwork source candidates

- Separated artwork selection from text-field provenance: changing a title or actor source no longer changes the chosen images.
- Added independent `封套` and `剧照` selectors after a search.
- Kept poster and fanart on one explicit cover source because both are derived from the same complete cover.
- Made the default screenshot choice combine LibreDMM and R18.dev candidates while preserving per-source alternatives.
- Displayed the selected cover's actual dimensions after its first preview download and cached that preview for later switches.
- Fixed the selected artwork into the save plan so preview and execution use the same URLs even if editable metadata changes later.
- Added cover and screenshot source summaries to the save preview and completion status.
- Continued to deduplicate downloaded screenshots by image-content SHA-256 and report candidate/unique counts in local logs.
- Added offline coverage for candidate construction, independent switching, immutable selection, selected-cover output, content deduplication, UI controls, and save-preview source summaries.

## v0.5.0-dev4 — Multi-source search loop

- Renamed the recommended mode from “automatic completion” to “multi-source search”.
- Made the recommended mode query LibreDMM and R18.dev independently exactly once per search, even when LibreDMM already has complete metadata.
- Preserved LibreDMM as the default Japanese primary source and used R18.dev only to fill blank selected values.
- Kept both complete source snapshots so every overlapping text field can expose real candidates.
- Allowed either source to fail without discarding a successful result from the other source; blocked the result only when both fail or their movie IDs conflict.
- Added per-source success/failure, elapsed time, candidate field count, and screenshot count to local logs.
- Kept explicit LibreDMM, R18.dev, and JAVLibrary selections as single-source searches.
- Added offline coverage for dual success, either-side failure, total failure, mismatched IDs, one-call-per-source behavior, and diagnostics.
- Passed all five manual checks for real multi-source search, field selection, single-source behavior, diagnostics, and save-preview compatibility.

## v0.5.0-dev3-r1 — Full dark candidate menu

- Recorded dev3 manual results: D02, D03 and D04 passed; D01 failed because the system menu left a white gutter.
- Replaced the complete WPF `ContextMenu` template with an application-owned dark container.
- Removed the system drop shadow and checkmark gutter that could retain light Windows theme colors.
- Added UI smoke coverage that verifies the custom dark root template is active.
- Passed the R01 manual retest; together with D02–D04, the complete dev3 field-candidate flow is accepted.

## v0.5.0-dev3 — Per-field source selection

- Recorded the user's successful manual acceptance of the dev2 source badges.
- Made a field's source badge clickable whenever that field has multiple candidates.
- Added a compact dark candidate menu that shows both the source name and a preview of its value.
- Allowed switching one field at a time between LibreDMM, R18.dev, and the latest manual edit.
- Kept single-candidate badges read-only so they remain informative without suggesting an unavailable action.
- Added UI smoke coverage for candidate menus, per-field switching, and returning from a manual edit to a scraper value.

## v0.5.0-dev2 — Field source badges

- Connected real single-source and automatic-completion searches to `MetadataReviewSession`.
- Preserved the individual LibreDMM and R18.dev results behind the merged editable metadata.
- Added unobtrusive source badges beside every visible metadata field.
- Updated a field badge to “手动编辑” immediately after the user changes its value.
- Added UI smoke coverage for mixed-source badges and manual-edit tracking.

## v0.5.0-dev1 — Metadata provenance foundation

- Added immutable per-source metadata snapshots for review without changing the selected editable result.
- Added field candidates for titles, dates, runtime, maker, director, label, series, actors, genres, plot, and rating.
- Added selected-source tracking, source switching, and automatic manual-edit candidates.
- Preserved actor image data when switching the actor field between sources.
- Kept all v0.4 NFO, artwork, preview, organization, and rollback behavior unchanged.

## v0.4.0 — Stable

- Promoted RC2 without functional changes after the complete automated gate and manual acceptance passed.
- Provides the lightweight single-movie workflow: search, review/edit, preview, and Jellyfin-compatible NFO and artwork output.
- Adds safe optional organization into a standard-number folder and optional movie renaming, with conflict blocking and rollback protection.
- Uses LibreDMM for Japanese metadata, R18.dev for English metadata and fallback, and JAVLibrary as the browser-assisted manual source.
- Preserves the original movie contents; acceptance confirmed that movie SHA-256 remains unchanged after organization and renaming.

## v0.4.0-rc2 — START-237 retest candidate

- Fixed R18.dev lookup when the guessed content ID differs from the site's actual `content_id`, restoring detailed English/Japanese fields and Gallery images for titles such as START-237.
- Kept the English R18.dev title while preserving the Japanese title in `originaltitle`.
- Continued to the next artwork candidate when a preview response is not a decodable image instead of showing a premature preview failure.
- Added successful-source and failed-preview-candidate details to the local diagnostic log.
- Added a regression test for the compact-record to actual-content-ID fallback.
- Passed the complete RC2 manual retest: R18.dev, LibreDMM switching, diagnostics, and movie SHA-256 preservation.
- Documented the upstream START-237 director value without applying a title-specific correction.

## v0.4.0-rc1 — Release candidate

- Promoted the tested v0.4 workflow to its first manual acceptance candidate.
- Added a numbered acceptance checklist for real sources, overwrite behavior, file safety, logs, and Jellyfin ingestion.
- No new file-operation behavior was introduced after preview2.

## v0.4.0-preview2 — Testing

- Changed “整理到番号文件夹” to create the standard-number folder inside the movie's current directory.
- Replaced the ambiguous overwrite option with “直接保存并覆盖（跳过预览）”.
- Direct-save mode skips the change preview and overwrites existing NFO or image outputs.
- Destination movie conflicts still block execution; movie files are never overwritten.
- Added a deterministic v0.4 filesystem regression suite and a sequential automated release gate.

## v0.4.0-preview1 — Testing

- Added mandatory save preview for metadata creation, overwrite, folder creation, video move, and video rename operations.
- Added optional standard movie folders and optional movie filename normalization; both remain disabled by default.
- Added blocking protection for destination movie conflicts. Movie files are never overwritten.
- Added staged metadata generation, backup of overwritten metadata, and best-effort rollback when commit fails.
- Added local 14-day logs for source failures, image downloads, saves, and recovery operations.
- Added an “Open logs” entry and separated metadata output from file organization controls in the UI.
- Added smoke coverage for organization planning, execution, overwrite refusal, confirmed overwrite, and logging.

## v0.3.0-r4 — Baseline

This is the first usable Git baseline for future JavMetaLite development.

- Single-movie metadata editing workflow.
- LibreDMM Japanese metadata import with description, cover, sample images, and actor image URLs.
- R18.dev English metadata import with Japanese original title, high-resolution cover, and Gallery images.
- JAVLibrary browser-assisted manual fallback.
- Jellyfin-compatible NFO, poster, fanart, and optional extrafanart output.
- Existing-file detection with per-save overwrite confirmation.
- Original movie files are never moved, renamed, or modified.
