# Changelog

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
