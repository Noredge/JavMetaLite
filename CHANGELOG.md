# Changelog

## macOS 1.1.0

- Review and edit movie details in the main window. Drop a movie to read its local metadata, then choose a source and click Search when ready.
- Look up a catalog number directly and link the movie later. Saving preserves untouched fields in its existing NFO and respects fields cleared during review.
- Compare posters and landscape covers, choose stills, and control which artwork gets saved. Generate a high-resolution poster from the full cover when available.

[macOS release](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.1.0)

## Windows 1.2.4 and macOS 1.0.1 (build 52)

- Restore LibreDMM metadata and artwork lookup through the v1 movie API for single and batch searches.
- Preserve the selected work when importing a LibreDMM page for a shared catalog number.

[Windows release](https://github.com/Noredge/JavMetaLite/releases/tag/v1.2.4) · [macOS release](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.0.1)

## 1.2.3

- Fix movie ID recognition during discovery for filenames containing dotted prefixes such as `mirror.example@ABF-391-U.mp4`.
- Keep unrelated files without IDs unrecognized; do not infer their IDs from the parent folder.
- Add parser and discovery-selection regression coverage, including U/UC suffixes and CD parts.

[Release notes](docs/archive/releases/windows/RELEASE-NOTES-v1.2.3.md)

## 1.2.2

- Scan multiple folders and select only recognized movie IDs during discovery.
- Fix IDs containing resolution-like numbers; stop auto-detecting numeric date-style IDs.
- Add pending/problem selection buttons and category counts without changing list filtering.
- Show complete scan diagnostics and distinguish permission denial from read failures.
- Resolve duplicate batch targets explicitly: save one, confirm ordered CD parts, or skip.
- Report incomplete temporary cleanup and preserve recovery errors after cancellation.

[Release notes](docs/archive/releases/windows/RELEASE-NOTES-v1.2.2.md)

## 1.2.1

- Read folder-level metadata and artwork in dedicated movie folders.
- Preserve recognized folder-level names during single-movie in-place saves.
- Distinguish explicit NFO content IDs and preserve PNG output encoding.
- Clean up empty source still-image folders after successful moves.
- Use consistent still-image terminology across four UI languages.

[Release notes](docs/archive/releases/windows/RELEASE-NOTES-v1.2.1.md)

## 1.2.0

- Batch review, CD grouping and source-specific search.
- Safer local metadata updates, image replacement and recovery.
- Faster queue/preview handling and improved four-language UI.
- Verified portable packages built by Windows CI.

[Release notes](docs/archive/releases/windows/RELEASE-NOTES-v1.2.0.md)

## Earlier releases

See [GitHub Releases](https://github.com/Noredge/JavMetaLite/releases). Internal preview plans and acceptance logs are not part of the public documentation.
