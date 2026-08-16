# Changelog

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
