# Changelog

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
