# Changelog

## v0.4.0-preview2 — Testing

- Changed “整理到番号文件夹” to create the standard-number folder inside the movie's current directory.
- Replaced the ambiguous overwrite option with “直接保存并覆盖（跳过预览）”.
- Direct-save mode skips the change preview and overwrites existing NFO or image outputs.
- Destination movie conflicts still block execution; movie files are never overwritten.

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
