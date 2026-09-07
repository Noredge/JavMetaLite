# Changelog

See the [1.2.1 release notes](docs/RELEASE-NOTES-v1.2.1.md) and [delivery checklist](docs/RELEASE-v1.2.1.zh-Hans.md).

## v1.2.1

- Read folder-level `movie.nfo`, `poster` and `fanart` for an unambiguous single-movie folder, while retaining filename-based lookup priority and existing multipart output rules.
- Preserve folder-level names for single-movie in-place saves; keep existing naming rules when organizing into a new directory. Preserve PNG encoding when replacing PNG artwork.
- Read an explicit NFO `cid` separately from the default movie number.
- Remove only the empty source `extrafanart` directory involved in a successful move. Keep nonempty directories, parent folders and directories needed after cancellation or failure.
- Use consistent still-image terminology across the four UI languages. No dependency or search-policy changes.

## v1.2.0

- Freezes the accepted preview.56 functionality as 1.2.0 after the user confirmed testing was complete. No new features or architecture changes are introduced.
- Updates the version identity and current reading materials; screenshots retain explicit version and synthetic-data labels.
- Tracks final-version automation, portable-package checks and any remaining manual checks separately in the finalization record. Earlier preview validation is not proof of the new package.
- Pins the reviewed .NET SDK and adds actual portable ZIP verification and artifact upload to Windows CI. Public delivery excludes private development screenshots; release assets include SHA-256 checksums.

## v1.2.0-preview.56 — Final-review safety fixes (internal)

- Applies the 50-image sample limit only to online selections; selected local originals are retained even when they appear after the limit. A local image read failure aborts preparation instead of silently committing an incomplete replacement. Preview and execution use the same selection count.
- Checks batch plans for conflicting movie/artwork output and retirement paths before confirmation; affected movies require separate ID folders instead of overwriting another movie's samples.
- Protects the viewer's applied-source refresh with the existing busy/cancellation lifecycle and rejects stale preview writes. Canceling a failed-source retry during cover comparison now restores a retryable search state.
- Includes complete third-party license/notice texts in the existing four-file portable layout, with packaging regressions. No dependency upgrade or new runtime requirement.
- Keeps artwork headings separate from their actions, wraps long queue-removal labels and sizes target selectors to their text. Clarifies CD1/CD2 grouping in the short Chinese READMEs; no video concatenation is performed.
- Adds local-original preservation, cross-plan conflict, asynchronous UI and four-language readability regressions. See the [fix and verification report](docs/FIX-v1.2.0-preview.56.zh-Hans.md); this is not an external release.

## v1.2.0-preview.55 — Bounded file-sharing recovery and deferred previews (internal)

- Retries only Windows sharing/lock violations when committing or restoring files: three asynchronous delays of 100/200/400 ms, then stops. Applies to single and multipart transactions without increasing download or movie concurrency; destination conflicts, access denial and other I/O errors are not retried, and moves never force-overwrite destinations.
- Commit waits honor cancellation. Rollback uses its own bounded waits even after cancellation; persistent recovery failures still preserve staging/old backups and stop the batch. Logs identify the affected file operation and paths. Does not remove existing recovery folders.
- Uncached artwork intentionally skipped while advancing a batch save now says “Preview not loaded”, with an explanatory tooltip in all four languages. Cached previews remain visible; missing artwork and completed failed loads keep their existing unavailable state. No new preview downloads are introduced.
- Adds policy, actual Windows file-lock, single/multipart cancellation/recovery and four-language preview-state regressions. Internal only; see the [fix report](docs/FIX-v1.2.0-preview.55.zh-Hans.md).

## v1.2.0-preview.54 — Tiny portrait and local cover detection (internal)

- Also flags full-cover/fanart images whose longest edge is below 400 px, regardless of orientation. This catches portrait fallback thumbnails such as 147×200 and tiny square images; the existing landscape-width-below-600 rule remains. Normal 800×539 and 400×539 images are not flagged.
- Reuses dimensions already read during local sidecar validation, so imported local movies can be flagged before they are opened, without an additional image read or online search. Only the full-cover/fanart role is checked, not standalone cropped posters or sample images.
- Fresh local/online full-cover previews update the same advisory from intrinsic dimensions and cancel redundant pending checks. Stale source results are rejected; failed fresh reads become unknown rather than retaining an old low-resolution verdict. Cached revisits retain the per-movie warning.
- Keeps the queue's yellow review label without dimensions/reasons, including local movies that are still eligible for searching. Search eligibility, save review revisions, save transactions and source policies are unchanged. Adds local-import, tiny-thumbnail, preview/cache, four-language and zero-HTTP regressions; internal only. See the [fix report](docs/FIX-v1.2.0-preview.54.zh-Hans.md).

## v1.2.0-preview.53 — Low-resolution cover review hints (internal)

- After successful searches, checks only the selected full-cover source in a cancellable background queue (at most two checks). Uses intrinsic dimensions of the first valid image in the existing fallback order; does not search another provider or fetch sample images. Local-only queue imports do not start online checks.
- Flags complete landscape covers narrower than 600 px by turning the queue's existing review badge yellow, without adding reasons or dimensions to the row. Includes them in the existing issue count/filter; the selected movie's main panel shows the reason and dimensions. Normal 800×539 covers, portrait crops and failed/unknown measurements are not flagged. Saving remains allowed; warnings never change review revisions or artwork selections.
- Rechecks source changes, rejects stale/removed-movie results, and clears old measurements on a new search or movie ID. Foreground operations pause new checks; the bottom action can stop background checks without confirmation. Reuses known preview dimensions with a bounded session cache; probing previously unread images adds network traffic and is not a promised search-speed improvement.
- Includes deterministic fallback, boundary, cancellation, concurrency, source/ID lifetime, cache reuse, save eligibility and four-language WPF layout regressions. Existing 404 fallback logging, source policies, file transactions and dependencies are unchanged. Internal only; see the [closeout report](docs/CLOSEOUT-v1.2.0-preview.53.zh-Hans.md).

## v1.2.0-preview.52 — Bounded sample-image downloads (internal)

- Downloads online Extra Fanart in batches of at most three within the current movie. Keeps movie transactions sequential, sample order and first-occurrence content deduplication stable, and the existing 50-sample cap. Local-only sample reads remain sequential; no cross-movie prefetch or persistent cache is added.
- Drains the current batch before returning on cancellation/fatal failure, with sibling cancellation on those paths. Ordinary recoverable failures remain skippable; full replacement still rejects any failed selected image before writing outputs or retiring local artwork. Image URL fallback and provider/search request policies are unchanged.
- Adds per-movie timing summaries for artwork reads (including validation), conversion, staging writes, verification/transfer, commit and cleanup, plus six targeted download/file-safety regression groups and an optional offline save benchmark.
- In three 12-movie synthetic latency-controlled runs, median save time fell from 13.56 s to 6.61 s, with identical output hashes/request counts and no pending requests. This is not a promised live-site speedup. No UI, language-resource, dependency or file-safety changes; internal only. See the [measurement report](docs/CLOSEOUT-v1.2.0-preview.52.zh-Hans.md).

## v1.2.0-preview.51 — Queue keyboard focus fix (internal)

- Keeps queue-originated Up/Down input within movie navigation when preview loading temporarily disables the queue and WPF moves focus to another control. Busy repeats are consumed without accumulating preview work or accidentally changing language/source selections.
- Preserves pending queue focus after key release, but cancels delayed focus restoration on a deliberate click, other keyboard command (including Tab/modifiers), or window deactivation. Intentional dropdown and text-box keyboard interaction remains available.
- Adds a reproducible slow-preview regression plus held Up/Down, cached navigation, boundary, release/restart, explicit focus change and cancellation checks. No changes to localization resources, source routing, preview concurrency, saving, dependencies or file safety. Internal only; see the [fix report](docs/FIX-v1.2.0-preview.51.zh-Hans.md).

## v1.2.0-preview.50 — Continuous-use stability (internal)

- Prevents canceled or interrupted main-panel artwork loads from becoming reusable blank/partial previews when switching movies. Revisiting loads the selected source again; completed previews, including genuinely missing artwork, remain cacheable.
- Invalidates an old preview before loading a newly selected source, and clears the loading indicator on cancellation. No source-routing, UI-layout, concurrency, save-safety, language or dependency changes.
- Adds regression coverage for cancellation/revisit, canceled source changes, cache reuse, delayed viewer results and viewer-close cancellation. Adds an optional same-window synthetic queue soak covering import, browsing, language changes, search cancellation/failure/retry and clearing, with weak-reference and resource measurements.
- Internal verification only; no live website load, real-library writes or public release. See the [stability report](docs/CLOSEOUT-v1.2.0-preview.50.zh-Hans.md).

## v1.2.0-preview.49 — Lower-allocation queue statistics (internal)

- Removes full movie-ID parsing from presence-only queue issue/search/retry checks, checks cheap search state first, and scans retry attempts without a capturing predicate. Actual ID normalization, source routing, request concurrency, review state and save validation are unchanged; no new state cache or counter system is introduced.
- Adds old-expression parity coverage for Unicode blanks, free-form IDs, search/cancel/failure/review/save states and source-scoped retries, plus a bounded-allocation regression and repeatable targeted call-cost measurements.
- Same-machine 1,000-movie fake-HTTP search reduced cumulative managed allocations from about 2.06 GB to 27.37 MB (98.7%). This is not resident memory, and modest/variable elapsed-time changes are not a promise of faster live websites. See the [measurement report](docs/CLOSEOUT-v1.2.0-preview.49.zh-Hans.md).
- Keeps preview.48 UI, import batching, four languages, dependencies and all file safeguards. Internal only; not pushed or publicly released.

## v1.2.0-preview.48 — Search toolbar consistency (internal UI patch)

- Keeps single and batch search actions on one row at the default 1120-pixel logical window width. Shares wide single-row, compact single-row and narrow two-row layouts; does not change the window or queue/artwork column sizes.
- Shortens the web-lookup label and omits the recommendation suffix from the closed LibreDMM selector in compact layouts. The source menu keeps full names/recommendation, Custom keeps its gear spacing, and batch guidance remains below the action row. Source changes do not rearrange the toolbar.
- Adds four-language WPF checks at default/minimum widths and both sides of every layout breakpoint, with rendered toolbar previews. Search routing, concurrency, saving, image handling, preferences and dependencies are unchanged. Internal review only; no public release.

## v1.2.0-preview.47 — Measured responsiveness closeout (internal)

- Coalesces queue property-change refreshes, updates selection totals once per bulk action, and avoids resetting the list just to switch movies. Keeps problem filters, keyboard focus, source-scoped retries and save safeguards.
- Prepares imports and checks new, unattached movies on a worker in bounded batches. Uses a group-key index, skips unchanged duplicate groups, preserves multi-CD discovery and validates every local sample. Cancellation never writes to the media library.
- Uses display-sized, frozen main-panel previews with an LRU cache bounded by 12 entries and a 32 MiB estimated decoded-pixel budget. The viewer loads its own larger images and original dimensions; saved images and artwork-source resolution comparison are not reduced.
- Caches raw dimensions instead of translated text, consolidates language-switch refreshes, localizes known discovery diagnostics, adds English lookup fallback and checks placeholder contracts across all four resource dictionaries.
- Extracts focused queue-refresh, import and preview-cache/loading helpers without changing the WPF framework, metadata review model, website concurrency or dependencies. Adds repeatable synthetic 100/500/1,000-movie benchmarks and targeted WPF regressions; timings remain machine-specific, not live-site speed promises.
- Internal review build only. No public release, local database or Jellyfin integration.

## v1.2.0-preview.46 — LibreDMM-first workflow (internal)

- Makes LibreDMM the recommended default for current-movie search, batches, safe settings and missing source arguments. Removes the old Auto multi-source choice while retaining R18-only, Custom multi-source and manual mode. Legacy Auto values migrate to LibreDMM; explicit R18/Custom/manual settings and custom rules remain intact.
- Adds a non-modal batch source hint below the source toolbar. R18/Custom modes explain possible rate-limit/network delays; LibreDMM remains the speed-oriented recommendation. R18's English metadata and possible higher-resolution artwork are described without promising better images for every movie.
- Keeps four-language source labels readable at minimum width through a shared responsive breakpoint; switching sources does not rearrange the toolbar. Expands default-source, migration, 71-movie request-count and four-language WPF hint/layout coverage.
- Internal only; no local database, translation dependency or Jellyfin plugin is added. Those remain separate product discussions.

## v1.2.0-preview.45 — Respect queue search sources (internal)

- Uses the same source selection rules for current-movie search and batch search: LibreDMM-only and R18-only make no requests to the other provider; Auto/Custom query both. Captures the source mode and Custom rules when a batch starts, and reports the requested sources in the progress text/button tooltip.
- Removes JAVLibrary from automatic-source choices and automatic retries, while preserving independent manual current-movie web lookup, parsing, ratings and provenance. Legacy `javlibrary` preferences migrate to manual mode without enabling automatic requests. Manual mode disables queue search and also guards programmatic invocation.
- Scopes failed-source retry to currently selected automatic providers; retained results and failures outside that scope are not silently queried or discarded. A newly completed single-source search is a full success relative to its one requested source.
- Adds a 71-movie source matrix and actual WPF queue-button tests with fake HTTP transports, covering zero R18 requests for LibreDMM, fixed in-flight source selection, R18-only, Auto/Custom, manual mode, failure/cancellation/retry, current-movie parity and manual JAVLibrary imports.
- Internal build only. Local-database implementation is paused pending a separate necessity discussion after this routing fix is reviewed; no database option, dependency or full dump is added.

## v1.2.0-preview.44 — R18 throttling and partial-source recovery (internal)

- Shares one paced, single-flight R18 API scheduler across searches, fallback URLs and manual imports. HTTP 429 is handled before HTML detection, honors Retry-After (seconds/date), and retries the same URL at most twice with shared backoff. Longer cooldowns remain retryable without stalling the entire queue; waiting is cancellable and excluded from HTTP timeouts.
- Distinguishes complete success, partial-source failure and total failure in batch results/logs. Partial failures appear in the issue filter with an amber badge and a localized 429 explanation; feedback remains in the bottom status area.
- Adds selected-job failed-source retry in batch mode, reusing successful provider results. Recovery reapplies preferred sources only to automatic fallback fields/artwork; explicit source choices, manual edits, intentional blanks and reviewed sample selections remain intact. Canceling retry keeps prior attempts and metadata.
- Adds deterministic 71-movie scheduling/recovery fixtures, sustained-rate-limit and cancellation tests, plus WPF partial-failure/filter/retry/localization coverage. No live bulk traffic or real-library mutations are used for these tests.
- Remains an internal preview, not an external release. Next: a disabled-by-default advanced R18 offline-database feasibility option; no database is bundled or downloaded in this version. See [next-stage plan](docs/PERFORMANCE-NEXT.zh-Hans.md).

## v1.2.0-preview.43 — Internal reliability closeout (not published)

- Aborts full Extra Fanart replacement if any requested sample download fails, including partial success, before retiring local files; preserves the explicit empty-selection action and the no-search/no-online-samples safeguard.
- Waits for active operations and cancellation recovery before closing the main window. Repeated close requests cannot bypass cleanup; operation errors and failed batch-save results keep the window open.
- Preserves reviewed field values, intentional blanks, provenance, manual-web candidates, actor images, artwork source and sample selection when retrying failed providers. New results still fill previously empty fields, and editable values no longer mutate cached provider results.
- Uses the app project version for the displayed version, startup log and default package name; rejects requested-version and published-EXE identity mismatches. Keeps per-version checksums and previous checksum entries.
- Adds offline regressions for all/partial download failure in single/multi-CD transactions, retry selection preservation, safe window closing, localized version text and package validation.
- This is an internal review build only. External publication is on hold until a separate measured large-library loading/search performance pass and user acceptance. See [internal closeout](docs/CLOSEOUT-v1.2.0-preview.43.zh-Hans.md) and [next-stage plan](docs/PERFORMANCE-NEXT.zh-Hans.md).

The consolidated release-candidate summary for preview.42 is available in
[docs/RELEASE-CANDIDATE-v1.2.0-preview.42.zh-Hans.md](docs/RELEASE-CANDIDATE-v1.2.0-preview.42.zh-Hans.md).

## v1.2.0-preview.42 — Artwork comparison and global viewer position

- Opens the artwork viewer in All images from View images, the poster crop, and the full-cover preview while preserving the clicked image and reporting its position in the complete candidate list.
- Adds every available artwork source as a source-labelled poster-crop/full-cover pair, so local, manual, LibreDMM, and R18.dev artwork can be compared with the same viewer controls.
- Adds a top Source selector that mirrors the main-window source menu, switches to the matching image type, and limits online candidates to the selected provider while leaving local images visible.
- Selecting a provider initially selects all of its online Extra Fanart; per-source manual adjustments are retained while switching inside the viewer, and other providers cannot leak into the applied selection.
- Replaces the ambiguous bottom source-action button with one staged Apply image settings action for both the artwork source and its online samples; Close and Esc discard unapplied changes, while confirmed local deletion remains immediate.
- Loads alternate source images through the existing bounded preview cache, tries each source's fallback cover locations, and derives poster crops without downloading the same cover again.
- Keeps manual type filtering, confirmed local-image deletion, collapsed filmstrips, and separate local/online rows intact; sources without online candidates show an explicit empty state instead of falling back to another provider.
- Adds WPF regressions for all-image entry positioning, source-scoped candidate rows, per-source selection memory, staged Apply/Close behavior, and the three main-window viewer entry points.

## v1.2.0-preview.41 — Safe Extra Fanart replacement and artwork source sets

- Gates “Replace local Extra Fanart” on online samples obtained for the current movie ID, so Save and `Ctrl+S` preserve every existing local image when no search was run or the search returned no samples.
- Adds a Custom multi-source artwork rule that compares the decoded pixel area of each provider's full cover, then selects the higher-resolution provider for the full cover, derived poster crop, and online Extra Fanart as one source-consistent set.
- Keeps both providers' image candidates available for manual review, uses a deterministic LibreDMM tie break, and falls back cleanly when one provider or image cannot be measured.
- Renames the search-toolbar label from “Automatic source” to “Source” in all four languages.
- Restores focus after a queue selection loads and handles unmodified `Up/Down` only inside the focused queue, fixing navigation that previously stopped after one move without reintroducing global `Alt+Up/Down` shortcuts.
- Adds a compact single-movie “Remove movie” action that clears the current workspace without deleting any files from disk.
- Adds Core, file-transaction, and WPF regressions for the no-search deletion guard, source-consistent resolution selection, batch behavior, source-rule UI, repeated queue navigation, and single-movie removal.

## v1.2.0-preview.40 — Minimal keyboard and focus flow

- Adds `Ctrl+S` as the only new global shortcut, routing to the visible single or batch save button so all existing validation, preview, conflict, and overwrite safeguards remain in effect.
- Lets Enter start a search only from the movie-ID field, while IME-processed Enter and Enter in other editors remain untouched.
- Removes global `Alt+Up/Down` queue navigation and leaves queue movement to the focused list's native `Up/Down` behavior; the main window does not bind Escape to cancellation.
- Keeps cancellation on the explicit bottom action as a one-click request without a confirmation dialog, and makes destructive dialogs focus Cancel by default.
- Focuses the ID field after importing one unidentified movie, restores the invoking control after modal settings, artwork, and save-preview windows, and skips global save while a dropdown or context menu is open.
- Adds WPF regressions for shortcut routing, popup guards, ID-field Enter, IME safety, direct cancellation, dialog defaults, unidentified-import focus, and localized shortcut hints.

## v1.2.0-preview.39 — Source action spacing

- Adds a clearer three-pixel visual gap between the embedded custom-source gear button and the source dropdown arrow.
- Rebalances the selector's internal left and right padding so the extra separation does not reduce the space available to localized source names.
- Keeps the divider-to-gear spacing, outer selector geometry, and responsive single/batch layouts unchanged.
- Adds a WPF geometry regression that measures the rendered gap between the gear button and dropdown arrow.

## v1.2.0-preview.38 — Stable integrated source settings

- Keeps the search toolbar geometry independent from the selected automatic source mode, preventing Custom multi-source from moving an otherwise single-row toolbar into two rows.
- Replaces the separate “Edit rules” button with a compact gear action embedded inside the source selector, separated from the dropdown arrow by a subtle divider.
- Reserves the internal action area without changing the selector's outer width or position, and hides both the gear and divider outside Custom multi-source mode.
- Preserves readable source names at minimum width and exposes the localized Edit rules label through the button tooltip and accessibility name.
- Adds four-language, single-mode, batch-mode, and minimum-window WPF regressions that verify switching source modes does not change the selector's row, column, span, width, or position.

## v1.2.0-preview.37 — Consistent pixel scrolling

- Fixes the single-movie save preview so its change list scrolls by precise pixel distance instead of snapping by whole rows.
- Centralizes wheel handling for every app-owned vertical scroll surface: metadata, movie queue, save previews, movie discovery, source rules, candidate menus, and combo-box popups now share a 24-pixel-per-notch baseline with proportional high-resolution wheel input.
- Preserves the artwork viewer's horizontal filmstrip behavior through the same shared implementation, using its existing 60-pixel-per-notch baseline.
- Lets nested scrollable text fields consume the wheel first and hands scrolling to the outer metadata view at their top or bottom boundary.
- Adds WPF regressions for the single-save preview's measured 6-pixel movement on a quarter-notch input and for consistent behavior assignments across the audited surfaces.

## v1.2.0-preview.36 — Consistent disclosure arrows

- Replaces separate right/down text glyphs with one vector chevron rotated 90 degrees, keeping its geometry and stroke identical in both states.
- Applies the shared vector chevron to both the artwork image list and Save settings disclosure headers.
- Makes unchecked artwork-thumbnail selection boxes more transparent, reducing the dark fill from roughly 65% to 40% opacity and softening the border as well.
- Keeps hovered and checked states stronger so pointer feedback and selected images remain easy to recognize.
- Adds WPF regressions for chevron rotation and stable dimensions, plus the lighter unchecked selection-box alpha.

## v1.2.0-preview.35 — Unified disclosure controls

- Collapses the artwork viewer's filmstrip by default behind a full-width “Image list” header that reports filtered local and online counts.
- Keeps the filmstrip's expanded/collapsed state for the current viewer session while image filters change; previous/next buttons and keyboard navigation remain available when it is collapsed.
- Reworks Save settings into the same left-chevron disclosure pattern, removing the isolated far-right arrow and decorative square.
- Uses a shared muted chevron style that gains an accent color on hover or keyboard focus, while keeping each complete header row clickable.
- Adds WPF regressions for the default-collapsed filmstrip, its summary and expansion, plus Save settings chevron placement and direction.

## v1.2.0-preview.34 — Translucent filmstrip selection

- Gives artwork-thumbnail checkboxes a dedicated translucent dark surface so more of the underlying image remains visible.
- Keeps selected thumbnails highly legible with a stronger translucent accent fill, white check mark, hover outline, and keyboard-focus state.
- Limits the visual change to the artwork filmstrips; standard checkboxes elsewhere in the app remain unchanged.
- Adds a WPF regression that verifies the scoped style and its non-opaque unchecked background.

## v1.2.0-preview.33 — Separate local and online artwork queues

- Splits the artwork filmstrip into a local-image row and an online-candidate row so deletion and saving no longer share one ambiguous selection surface.
- Adds checkbox-based batch deletion for local poster, fanart, and Extra Fanart files, with filtered Select all/Clear actions and a live deletion count.
- Keeps online Extra Fanart selection and Apply actions independent from local deletion, including when both rows are visible.
- Replaces the native Windows deletion prompt with an in-app dark confirmation dialog and confirms a batch only once before deleting it directly.
- Raises the artwork viewer's minimum height to preserve a useful main preview while both filmstrip rows are present, and adds WPF regressions for the separated rows, cancel path, batch deletion, and dialog styling.

## v1.2.0-preview.32 — Direct local artwork management

- Removes selection and Apply actions from local artwork; online Extra Fanart remains selectable in the viewer.
- Allows local poster, fanart, and Extra Fanart files to be deleted directly from the viewer after a mandatory warning confirmation, then refreshes the current movie state immediately.
- Adds an opt-in “Replace local Extra Fanart” save setting. When enabled, saving removes the existing local `extrafanart` set and writes only the selected online images; selecting none clears the set.
- Removes the duplicated provider-result strip from the metadata editor, keeps search feedback in the bottom status bar, and preserves failed-source retry there as a compact contextual action.
- Advances saved preferences to schema 12 and adds Core, transaction, migration, and WPF regressions for the new behavior.

## v1.2.0-preview.31 — Unobstructed artwork dimensions

- Moves the artwork viewer's original pixel dimensions out of the image canvas and places them below the preview, right-aligned in the same lightweight style as the main cover preview.
- Adds a WPF layout regression to ensure the dimension label remains outside the image at minimum window size while updating correctly during navigation.

## v1.2.0-preview.30 — Aspect-aware previews and confirmed local deletion

- Preserves each image's full aspect ratio in the bottom filmstrip and varies thumbnail width so portrait, square, and landscape artwork no longer share a cropped preview.
- Shows the current image's pixel dimensions in a compact badge at the lower-right of the main preview.
- Adds an explicit local-image deletion action for local Extra Fanart and distinguishes local retention from downloading an online sample.
- Requires a warning confirmation before any individual or bulk action can mark local images for deletion; rejecting the dialog restores the previous selection.
- Defers confirmed deletion until Apply selection and movie save, then routes it through the existing save plan, source fingerprint, backup, rollback, and transactional commit path.
- Honors explicit confirmed deletion even when Extra Fanart output is disabled, while continuing to preserve every other local still byte for byte.
- Adds Core, transaction, and WPF regressions for pending-deletion persistence, disabled-output deletion, aspect-aware thumbnails, dimensions, confirmation rejection, and confirmed deletion results.

## v1.2.0-preview.29 — Dark artwork selector and thumbnail filmstrip

- Gives the artwork-type selector the same dark closed and popup styling as the main workspace, preventing unreadable white dropdown surfaces.
- Adds a Windows Photos-inspired bottom filmstrip that follows the active artwork filter and lets users jump directly to any image.
- Highlights the currently displayed thumbnail with an accent border and places direct selection checkboxes on Extra Fanart thumbnails.
- Keeps the top selection checkbox, filmstrip checkboxes, bulk selection actions, and final applied selection synchronized.
- Supports horizontal pixel scrolling in the filmstrip with the mouse wheel and keeps the active thumbnail in view during keyboard or button navigation.
- Adds WPF regression coverage for the dark selector, popup surface, filmstrip population, active-image outline, and thumbnail selection synchronization.

## v1.2.0-preview.28 — Local extrafanart management and artwork filters

- Discovers and validates existing local `extrafanart` images when a movie is loaded, while isolating invalid files so valid artwork remains usable.
- Adds All, Poster, Fanart, and Extra fanart filters to the artwork viewer, with local/online source labels and per-image selection for both existing and fetched stills.
- Replaces navigation-only loading with a local-first, four-worker background preload that is limited to the current viewer session and shares work with foreground navigation.
- Reconciles deselected local stills through the existing save preview and transactional backup/rollback path instead of deleting files immediately.
- Preserves local `extrafanart` byte for byte when its output option is disabled, including when a movie is moved into a new ID folder.
- Adds Core, file-transaction, and WPF regressions for discovery, validation, selection persistence, type filtering, background preload, removal preview, rollback-safe synchronization, and disabled-output preservation.

## v1.2.0-preview.27 — Selectable extrafanart viewer

- Extends the artwork viewer from poster and fanart to the current movie's complete Sample Images / `extrafanart` candidate set.
- Loads sample images only when the user navigates to them, caches successfully viewed images for the current viewer session, and keeps navigation responsive when many images are available.
- Adds per-sample inclusion, Select all, Clear selection, a live selected-count summary, and an explicit Apply selection action.
- Applies the confirmed list directly to the movie's `ScreenshotUrls`, so the existing `extrafanart` save path downloads only selected images; closing or pressing Escape discards viewer-only selection changes.
- Rebuilds the available list from retained source results, allowing a previously excluded sample to be selected again without repeating the metadata search.
- Adds WPF regressions for lazy loading, single-image selection, bulk selection, selection summary, and dialog confirmation.

## v1.2.0-preview.26 — Artwork viewer and source feedback

- Makes the poster crop and full-cover fanart previews clickable, opening a focused dark viewer with previous/next controls plus Left, Right, and Escape keyboard navigation.
- Separates no-movie, loading, unavailable, and loaded artwork states so a missing or delayed image is no longer presented as the file-drop placeholder.
- Adds an in-context search result strip that reports each provider's success or failure, usable-field count, elapsed time, and detailed error without relying on the footer alone.
- Adds “Retry failed sources”, which preserves successful provider results and only queries providers that failed during the previous multi-source attempt.
- Restyles queue search states as compact color-coded pills without adding per-movie confirmation or readiness steps.
- Adds Core and WPF regressions for non-throwing retry attempts, viewer navigation state, per-source feedback, retry visibility, and the new empty artwork states.

## v1.2.0-preview.25 — Responsive minimum-window workspace

- Replaces the search `WrapPanel` with deterministic responsive layouts: one row whenever the metadata panel can fit it, otherwise two deliberately aligned rows instead of arbitrary control wrapping.
- Keeps movie ID and Search together on the first compact row, with Manual web lookup, automatic source, and Edit rules aligned on the second row.
- Uses tighter 220-pixel queue and 250-pixel artwork defaults in batch mode, giving the metadata editor more useful room at the 930-pixel minimum window width while retaining both draggable splitters.
- Hides only the redundant “Automatic source” caption in constrained layouts; the selected source, its strategy tooltip, and all controls remain available.
- Adds WPF regressions for wide single-row placement, compact two-row grouping, custom-rule alignment, and batch column allocation.

## v1.2.0-preview.24 — Aligned queue controls and compact source toolbar

- Replaces the content-sized queue action wrap with an equal-width two-column grid, keeping Select all, Clear selection, Remove selected, and Clear queue aligned in every language.
- Returns the automatic metadata-source selector to the main search row and shortens its visible label, while keeping manual web lookup and automatic scraping independent.
- Moves the selected automatic strategy explanation into the source selector tooltip so it remains available without consuming a second toolbar row.
- Adds four-language resources and WPF regressions for the compact source toolbar and aligned queue actions.

## v1.2.0-preview.23 — Clearer review workspace

- Splits the search area into explicit metadata-search and automatic-source rows, keeps manual web lookup independent, and shows a concise explanation of the selected automatic strategy.
- Resets the metadata viewport to the title only when switching movies, while preserving the current position during field edits and source-candidate changes.
- Separates the queue's blue “currently viewing” state from batch inclusion, replaces the play-like marker with a neutral movie marker, and gives previous/next navigation visible labels.
- Renames the artwork area and labels the poster crop and full-cover fanart separately, while enlarging field-source badges for easier reading and selection.
- Collapses output, naming, and preview preferences into a live save-settings summary by default, leaving the target location and primary save action immediately available.
- Adds resizable queue/artwork boundaries and stops batch mode from forcing the window to 1230 pixels wide, preserving the 930-pixel minimum for high-DPI and smaller screens.
- Adds four-language UI resources and WPF regressions for the new hierarchy, collapse behavior, queue focus semantics, responsive width, and metadata scroll reset.

## v1.2.0-preview.22 — Remember the current search mode

- Renames “Remember save preferences” to the clearer “Remember current settings” and expands its tooltip to list the restored categories.
- Persists and restores all six automatic-search modes together with the existing output, target, preview, and verification settings when the user explicitly enables memory.
- Keeps custom per-field source rules independent, while disabled memory, schema-v10 migration, and invalid source values safely return to the recommended multi-source mode.
- Advances preferences to schema v11 and adds Core/WPF regressions for every supported mode, safe normalization, disabled memory, translated copy, and UI restoration.

## v1.2.0-preview.21 — Reliable manual R18 routing and promoted imports

- Reuses R18.dev's proven automatic provider lookup to resolve the authoritative `content_id` before opening a manual browser, fixing catalog numbers such as `ABF-193` whose internal IDs cannot be guessed reliably.
- Keeps route discovery read-only: no metadata changes until the user confirms “Read current movie”, and failed discovery still opens the pre-filled R18.dev manual search.
- Treats that explicit confirmation as choosing the page result: every non-empty imported field and its artwork become current immediately, while fields missing from the page keep their prior values.
- Preserves local NFO, scraper, and manual field candidates so the user can still switch individual choices back after import.
- Adds offline regressions for real-ID route resolution, resolved-target reuse, selected imported fields/artwork, missing-field retention, and reversible local/manual candidates.

## v1.2.0-preview.20 — Independent manual web lookup

- Renames the action to “Manual web lookup” so it is clearly distinct from automatic metadata search and manual field editing.
- Makes the action permanently independent from the automatic source-mode selector; its label and behavior no longer change when LibreDMM, R18.dev, JAVLibrary, multi-source, Custom, or manual-entry mode is selected.
- Always opens the three-provider website menu, allowing the user to choose LibreDMM, R18.dev, or JAVLibrary again for every lookup.
- Removes the obsolete source-mode-to-browser routing rule and adds WPF coverage proving that a single-source search selection still exposes all three manual websites.

## v1.2.0-preview.19 — Reliable R18.dev web routing

- Stops guessing R18.dev's internal `content_id` from a catalog number when no authoritative R18.dev result exists; `FNS-121`, for example, uses `1fns121` rather than the generic `fns00121` guess.
- Opens the R18.dev home page and pre-fills the requested catalog number when the real `content_id` is unknown, while existing automatic-search results still open their exact human-readable detail page.
- Recognizes R18.dev's custom “Sorry, this page does not exist” response even when the server returns HTTP 200, and treats it as a missing title rather than a maintenance failure.
- Adds regressions for unknown-content-ID routing and HTTP-200 missing-page classification without changing the automatic metadata search path.

## v1.2.0-preview.18 — R18.dev missing-title fallback

- Detects an explicit R18.dev 404 as a possibly unlisted movie instead of leaving the user on a dead page.
- Shows a non-blocking amber explanation, returns to the R18.dev home page, and pre-fills the requested movie ID for manual correction or another lookup.
- Distinguishes HTTP/server/navigation failures with a separate unavailable state and red treatment rather than claiming the movie is missing.
- Keeps “Read current movie” disabled until the browser confirms a trusted R18.dev human-readable detail page containing the expected movie-information structure.
- Adds Core state-classification regressions and WPF coverage for missing, unavailable, ready, message, color, and read-button states.

## v1.2.0-preview.17 — Clear web lookup and human-readable R18.dev pages

- Renames the browser action to “Web lookup” and uses “Search with {provider}” in its menu so it is clearly distinct from the automatic multi-source search strategy beside it.
- Opens R18.dev's normal human-readable `id=...` movie page instead of exposing its raw `/json` endpoint in the embedded browser.
- Reads the matching official R18.dev JSON endpoint only in the background after the user confirms the visible movie page.
- Converts existing R18.dev result URLs and content IDs to the normal movie page, validates the trusted host and human detail shape, and rejects raw JSON pages as browser import targets.
- Adds Core and WPF regressions for the new copy, normal-page routing, exact background JSON conversion, import parsing, and malicious lookalike-host rejection.

## v1.2.0-preview.16 — Source-aware browser import

- Makes the browser entry follow LibreDMM, R18.dev, or JAVLibrary in explicit source modes instead of always opening JAVLibrary.
- Opens the sole successful provider directly in automatic multi-source mode; multiple successful sources, no results, and Custom mode use an explicit dark provider menu.
- Uses the exact result URL when available, validates provider domains and detail-page shapes, and dispatches each page to its matching importer.
- Adds imported web data as another review candidate, fills only blank fields, preserves current field and artwork choices, and rejects mismatched movie IDs.
- Adds Core routing/candidate regressions and WPF coverage for dynamic labels, the three-provider menu, dark styling, and provider-specific browser copy.

## v1.2.0-preview.15 — Precise mouse-wheel scrolling

- Replaces WPF's default multi-line wheel movement in the discovery window with explicit physical-pixel offsets.
- Moves 24 pixels per standard wheel notch and scales high-resolution partial deltas proportionally instead of snapping by paths or movie rows.
- Applies the same wheel behavior to both the multi-input path viewport and logical-movie result list, while allowing boundary events to remain unhandled.
- Adds an actual routed mouse-wheel regression that verifies a quarter-notch delta produces a six-pixel offset.

## v1.2.0-preview.14 — Scrollable inputs and unified safe preview skipping

- Makes the multi-path section in movie discovery independently pixel-scrollable instead of clipping long input lists.
- Replaces the separate single-save and batch-save preview options with one always-visible “Skip save preview” preference shared by both workflows.
- Skips only clean save plans; existing sidecars, blocking destination conflicts, and batch issue items still force the safety preview and explicit overwrite confirmation.
- Removes silent overwrite authority from the skip-preview setting while leaving movie-file overwrite protection and transactional rollback unchanged.
- Migrates either legacy preview-skip choice into the unified schema-v10 preference and adds Core/WPF regression coverage.

## v1.2.0-preview.13 — Unified smart import

- Accepts ordinary folders, nested folder trees, multiple folders, and mixed file/folder drops without requiring a pre-arranged ID-folder structure.
- Recursively discovers supported videos by default, while keeping discovery read-only, cancellable, deterministic, and isolated from reparse-point subtrees.
- Presents folder results as logical movies, merges `CD1/CD2` parts into one selectable item, and marks duplicates or filenames that need ID confirmation.
- Selects every new logical movie by default and adds select-all/select-none controls, while a folder containing one logical movie still opens directly in the single-movie workspace.
- Deduplicates overlapping input roots and files before reusing the existing queue, local-sidecar, search, and safe-save workflows.

## v1.2.0-preview.12 — Custom-source artwork and dark selectors

- Gives every selector in the custom-source window the same explicit dark popup and item templates as the main interface.
- Changes the new-profile default to LibreDMM for every metadata field instead of preselecting R18.dev for title.
- Adds artwork to the custom-source profile so cover and fanart can prefer LibreDMM or R18.dev alongside text fields.
- Falls back to the other provider when the preferred artwork source has no usable cover, while retaining manual source switching after search.
- Migrates the untouched preview.11 default profile to the new all-LibreDMM default without replacing already customized preview.11 profiles.

## v1.2.0-preview.11 — Custom multi-source field profiles

- Adds a Custom multi-source mode that lets each text metadata field prefer LibreDMM or R18.dev independently.
- Uses an explicit, reviewable default profile: title from R18.dev and all remaining text fields from LibreDMM.
- Still queries both providers and automatically keeps the other non-empty candidate when the preferred source fails or does not provide a field.
- Applies the same profile to single-movie and batch searches while keeping artwork on the existing independent cover-source workflow.
- Shows the source actually selected for every field and persists the custom profile globally, independent of save-preference memory.

## v1.2.0-preview.10 — Jellyfin ID-searchable titles

- Adds a global, default-on “Include ID in title” option that writes NFO titles as `<ID> · <Title>` so Jellyfin's normal text search can find a movie by ID.
- Applies the option consistently to every queued movie and remembers it only when save-preference memory is enabled.
- Keeps title generation idempotent and reversible: repeated saves never duplicate the ID prefix, and turning the option off removes the exact JavMetaLite-generated prefix.
- Writes the normalized movie ID as the default `javnumber` unique ID while preserving a distinct provider content ID as a non-default secondary value.
- Preserves the existing single-file basename sidecars and multi-part `movie.nfo / poster.jpg / fanart.jpg` folder sidecars.

## v1.2.0-preview.9 — JAVLibrary community rating import

- Reads the current JAVLibrary rating markup from `#video_review .text .score` before trying the legacy `#video_rating` selectors.
- Extracts numeric ratings robustly from parenthesized text, so `(8.90)` is displayed and written as `8.9` without changing the source rating scale.
- Keeps `JAVLibrary` as the visible field source and adds parser/UI regression coverage for the real markup and legacy fallback.

## v1.2.0-preview.8 — Reviewable community rating

- Adds a visible, editable Community rating field to the shared single-movie and batch review editor.
- Shows the selected rating source using the same field badge and candidate menu as the other metadata fields.
- Supports switching among local NFO, scraper, and retained manual rating candidates without changing the existing NFO `<rating>` output behavior.
- Explains in the field tooltip that the current source value is written as-is and rating scales are not converted yet.

## v1.2.0-preview.7 — Jellyfin multi-part folder sidecars

- Writes multi-part movies with folder-level `movie.nfo`, `poster.jpg`, and `fanart.jpg`, while single-file movies keep their existing basename sidecars.
- Automatically places a multi-part movie in a dedicated ID folder when “movie location” was selected; an already selected ID folder is never nested again.
- Prefers Jellyfin folder sidecars when loading multi-part movies and safely migrates preview.6 `<ID>.nfo`, `<ID>-poster.*`, and `<ID>-fanart.*` files during the next save.
- Updates migrated NFO artwork references, preserves unknown XML and legacy `Series:` tags, and retires old sidecar names only after the transaction commits.
- Keeps Jellyfin's normal stacked playback behavior: the first part is the main item and later parts remain under Additional Parts.

## v1.2.0-preview.6 — Jellyfin multi-part movies and Series retirement

- Recognizes sibling movie files ending in `CD1`, `CD2`, and later numeric CD suffixes as one queue item, including when the user selects only one part.
- Searches and reviews metadata once for the combined movie, while preserving every video part in stable numeric order.
- Organizes renamed parts as Jellyfin-compatible `<ID>-cd1`, `<ID>-cd2`, and so on, with one shared `<ID>.nfo`, poster, fanart, and extrafanart set.
- Extends same-volume moves, verified cross-volume copies, conflict detection, and rollback to treat all parts as one transaction.
- Removes the Series field and source candidates from the interface and stops providers and new NFO files from creating `Series:` tags.
- Treats an existing `Series:` tag as unmanaged XML so editing an old NFO preserves the original tag unchanged.

## v1.2.0-preview.5 — Batch artwork preview refresh

- Invalidates stale artwork preview caches for every movie that completes a batch metadata search successfully.
- Reloads the active movie immediately and lazily loads each other movie from its new artwork candidate when first selected.
- Keeps failed and canceled movies' existing local previews intact.

## v1.2.0-preview.4 — Batch preview workflow polish

- Changes the batch-save preview plan list to pixel-based scrolling while keeping virtualization enabled.
- Adds a remembered “Skip batch save preview” preference for clean batches; issue-only, mixed-issue, and unapproved-overwrite batches still open the safety preview.
- Removes confirmation dialogs from “Remove selected” and “Clear queue”; these actions only discard in-memory queue work and never delete movie or sidecar files.
- Corrects the visible preview version label to match the packaged application version.

## v1.2.0-preview.3 — Queue completion and visual polish

- Removes each successfully saved movie from the working queue immediately while keeping failed, canceled, and not-started movies available for retry.
- Keeps a completed movie open in single-movie mode without allowing it to reappear when switching back to the batch queue.
- Replaces logical item jumps with pixel-based queue scrolling while retaining UI virtualization.
- Gives the queue an explicit dark `ListBox`/`ScrollViewer` template so disabled or busy states cannot fall back to a white system background.
- Uses the same blue primary treatment for batch “Save selected/all” as the single-movie save action.
- Removes the obsolete completed filter and completed count now that completed items leave the queue.

## v1.2.0-preview.2 — Simplified single and batch workflows

- Adds explicit Single movie and Batch workflow entry points; the single mode hides queue complexity while batch mode keeps its controls visible even when empty.
- Replaces per-movie ready confirmation with one `IsSelectedForBatch` choice shared by batch search, preview, and save.
- Selects newly added movies by default and adds select all, select none, remove selected, and clear queue commands without deleting media or sidecar files.
- Replaces Ready filters and counts with selected/total, pending, issues, and completed views.
- Adds Save selected/all, which validates every selected movie together and shows valid plans and movies needing attention in one grouped preview.
- Keeps revision-backed stale-preview rejection, exact previewed plans, explicit overwrite approval, strict queue-order execution, first-error stop, per-movie transactions, SHA-256 verification, and rollback.
- Completed movies leave the batch selection automatically; editing them returns the save state to idle without silently selecting them again.
- Adds offline coverage for mode switching, unified selection, removal, clearing, issue-only previews, mixed valid/issue previews, 50-item queues, and sequential save coordination.

## v1.2.0-preview.1 — Review workbench preview

- Introduces an independent `MovieJob` working context for one movie's editable metadata, local NFO context, source results, review sessions, and artwork candidates.
- Moves metadata/artwork review lifecycle and online/local composition out of `MainWindow` without changing the single-movie interface or provider behavior.
- Keeps save planning and execution on the existing `FileOrganizationService.BuildPlan(...)` to `ExecuteAsync(...)` path.
- Adds offline coverage for two-job isolation, complete reset between movies, retained manual candidates, and disposal of replaced review sessions.
- Adds multiple-file selection and drag-and-drop, plus an in-memory movie queue that keeps each movie's unsaved review state isolated while switching.
- Adds an explicit read-only folder-discovery preview with top-level scanning by default, optional recursion, reparse-point skipping, duplicate filtering, and no metadata search or file writes.
- Keeps the familiar compact single-movie layout until more than one movie is queued; removing the active movie selects an adjacent job and returns to the compact layout when only one remains.
- Deliberately defers batch search, ready-state workflow, and batch save so every network and filesystem mutation remains an explicit per-movie action in this phase.
- Adds offline discovery coverage and WPF queue coverage for add, switch, unsaved-edit isolation, remove, and compact-layout restoration.
- Adds an explicit “search searchable” command that uses only LibreDMM and R18.dev, runs at most two movie jobs concurrently, and never saves files or marks results ready to save.
- Tracks per-job search state, source attempts, failure details, retry eligibility, cancellation, and an explicit include/exclude choice directly in the queue.
- Keeps completed results when a batch is canceled, returns in-flight jobs to a retryable canceled state, and leaves jobs that never started searchable.
- Wraps source-ID merge failures with their provider attempts so one mismatched result fails only its movie job while the rest of the batch continues.
- Adds offline coverage for concurrency limits, manual-candidate retention, partial and complete failures, ID mismatch, exclusion, cancellation, and not-started items.
- Adds explicit per-movie “ready to save” confirmation backed by a review revision; metadata, source, artwork, or save-setting changes immediately invalidate a stale confirmation.
- Gives each queued movie an independent snapshot of output, target, rename, overwrite, and cross-volume verification settings and restores that snapshot when switching jobs.
- Adds a grouped batch preview that lists the exact `SavePlan` paths and actions for every confirmed movie; unconfirmed movies are never included.
- Executes the same previewed plans strictly in queue order, rechecks the confirmed revision before each item, and stops after the first failure or cancellation while preserving completed and not-started states.
- Scopes overwrite approval to conflicts that were visible in the preview so a late, unpreviewed conflict cannot inherit another movie's approval.
- Adds offline coverage for review invalidation, stale-plan rejection, ordered first-error stop, cancellation, per-job settings, grouped preview, and explicit overwrite confirmation.
- Moves batch search and ready-item preview into the queue workspace so the primary header stays focused on the active movie.
- Adds queue filters for all, needs-review, ready, failed, and completed items; the summary keeps total, ready, review, and failed counts visible.
- Adds previous/next controls and `Alt+Up` / `Alt+Down` navigation for fast keyboard review.
- Enables recycling virtualization and a lightweight cover placeholder so a 50-item in-memory queue remains inexpensive to display.
- Marks this milestone as `1.2.0-preview.1`; the stable download links remain on v1.1.1 until live-provider, real-media, and packaged-build acceptance is complete.

## v1.1.1 — Stable

- When “Save all stills” is enabled but the selected source has no usable Sample Images, saving now skips `extrafanart` automatically instead of interrupting the entire operation.
- The completion status and local log explicitly report that the still-image output was skipped.

## v1.1.0 — Stable

- Promotes RC1 without functional changes after the complete automated gate and final Windows acceptance passed.
- Adds an explicit faster cross-volume transfer mode while retaining full target-side SHA-256 verification as the safe default.
- Makes successful online searches select their new artwork by default while preserving local images as selectable candidates.
- Accepts a single-movie ID folder through drag and drop, with non-recursive and ambiguity-blocking input rules.

## v1.1.0-rc1 — Release candidate

- Freezes the accepted dev1–dev3 behavior without additional functional changes.
- Carries forward optional cross-volume verification, post-search online artwork defaults, and single-movie ID-folder drag and drop.
- Requires the complete offline automated gate and a final compact Windows acceptance pass before stable promotion.

## v1.1.0-dev3 — ID folder drag and drop

- Accepts either one supported movie file or one single-movie ID folder through drag and drop.
- Resolves only a folder's top-level movie file so metadata subfolders and unrelated nested media are never scanned.
- Rejects empty or multi-movie folders with a clear localized message instead of guessing which movie to open.

## v1.1.0-dev2 — Online artwork becomes the post-search default

- Switches poster and fanart to the preferred successful online source after a search, matching the existing metadata-field behavior.
- Keeps previously loaded local images and a manually chosen cover in the artwork source menu so the user can switch back explicitly.
- Leaves initial movie loading unchanged: local sidecar images remain the default until a successful search supplies new artwork.

## v1.1.0-dev1 — Optional cross-volume verification

- Keeps full target-side SHA-256 verification as the safe default for cross-volume and UNC transfers.
- Adds an explicit fast-transfer option that skips the second complete target read and checks copy completion plus file size only.
- Preserves target-side staging, late-conflict protection, cancellation, source retirement after commit, and rollback in both modes.
- Shows the selected transfer policy in the target hint and save preview, with a clear at-your-own-risk warning for fast mode.
- Persists the choice only when the user enables remembered save preferences and migrates schema-v4 settings to full verification.
- Adds four-language UI copy plus offline preference, transaction, preview, and UI regression coverage.

## v1.0.0 — First public stable release

- Establishes the accepted v0.9.0 feature set as the first public, feature-frozen release.
- Adds the MIT License under `Copyright (c) 2026 Noredge` and preserves separate third-party notices.
- Replaces the development-history-first README with matching Simplified Chinese, Traditional Chinese, English, and Japanese public guides covering quick start, safety, source roles, limits, and build instructions.
- Adds a repeatable release script that produces one clean Windows x64 portable ZIP plus a SHA-256 checksum file.
- Adds compact portable instructions and a durable public testing guide without changing metadata or file-operation behavior.
- Uses English when a first-run Windows display language is outside the four supported interface languages; saved user choices remain unchanged.
- Clarifies that searches send the detected ID to selected sources and that the embedded WebView2 browser may retain site-verification cookies.

## v0.9.0 — Stable

- Promotes RC1 without functional changes after R01–R05 and movie SHA-256 verification all passed.
- Adds immediate Simplified Chinese, Traditional Chinese, English, and Japanese interface switching with safe language persistence.
- Unifies the main window, save preview, and embedded browser under one responsive dark theme.
- Introduces the final multi-size movie-folder application icon, matched native title bars, and dark scrollbars.
- Keeps the lightweight single-movie workflow and every existing search, preview, file-transaction, rollback, and preference safeguard unchanged.

## v0.9.0-rc1 — Release candidate

- Freeze the four-language interface, shared dark theme, final application icon, dark scrollbars, and precisely matched native title bar.
- Carry forward all passed dev1–dev3 manual checks without adding functional scope.
- Pass the complete offline automated gate before final release acceptance.

## v0.9.0-dev3-r2 — Matched native title bar

- Set the supported Windows native caption to the exact JavMetaLite chrome color instead of inheriting the user's system accent tint.
- Match native caption text and window-border colors to the existing dark theme while retaining standard system controls.
- Fall back to the normal native dark-title-bar request when exact DWM colors are unavailable.

## v0.9.0-dev3-r1 — Dark native chrome

- Request the native Windows dark title bar for the main window, save preview, and embedded browser while preserving standard window controls.
- Replace the remaining light WPF scrollbars with shared dark tracks, muted blue-gray thumbs, and theme-blue dragging feedback.
- Preserve keyboard, mouse-wheel, arrow, page, and thumb scrolling behavior.
- Keep every metadata and file behavior unchanged.

## v0.9.0-dev3 — Application icon

- Add a neutral movie-folder icon built from the approved film card, metadata card, and folder composition.
- Package 16, 24, 32, 48, 64, 128, and 256 px images in the Windows ICO; 16/24 px use a simplified small-size drawing.
- Apply the icon to the EXE, taskbar, every window title bar, and the main-window brand area.
- Keep all search, metadata, artwork, save, path, overwrite, transfer, rollback, and preference behavior unchanged.

## v0.9.0-dev2-r1 — Dropdown clarity

- Add a clear gap between combo boxes and their menus.
- Inset menu items and round their selection backgrounds so they no longer collide with the popup corners.
- Keep all behavior and selection logic unchanged.

## v0.9.0-dev2 — Interface consistency

- Introduces one shared dark theme for the main window, save preview, and embedded browser.
- Gives buttons, text fields, checkboxes, combo boxes, and tooltips consistent sizing, rounded geometry, and hover, focus, pressed, and disabled states.
- Rebalances the main card widths, search toolbar, form spacing, output area, and footer without moving or reordering any feature.
- Makes the save-preview footer and browser toolbar adapt safely to long localized text.
- Adds automated four-language minimum-window bounds checks plus shared-theme and secondary-window layout checks.
- Preserves all search, source, metadata, artwork, save, path, overwrite, transfer, rollback, and preference behavior.

## v0.9.0-dev1-r2 — Localization polish

- Shortens the English multi-source label so it remains fully visible in the fixed-width selector, while a localized tooltip retains the complete recommendation.
- Refines Japanese interface terminology and punctuation, including native labels for metadata, poster, fanart, complete cover, and sidecar files.
- Localizes poster and fanart role names in the save preview for every language.
- Adds four-language UI checks for the source label and tooltip without changing search, metadata, artwork, save, or file behavior.

## v0.9.0-dev1-r1 — Source timeout safeguard

- Caps each metadata source at 10 seconds so an unavailable site cannot keep a search waiting indefinitely.
- Keeps the successful source in multi-source mode when the other source times out; single-source mode shows a localized timeout message.
- Preserves user cancellation, source priority, merge rules, metadata fields, artwork selection, and every save/file safeguard.
- Adds an offline slow-provider regression fixture.

## v0.9.0-dev1 — Four-language foundation

- Adds immediate UI switching between Simplified Chinese, Traditional Chinese, English, and Japanese.
- Localizes the main window, embedded browser, save preview, common status/error messages, artwork/source labels, and file-operation progress without changing metadata behavior.
- Uses the supported Windows display language on first launch and safely falls back to Simplified Chinese for other locales.
- Advances preferences to schema v4 so the display language persists independently of save-preference memory; schema-v1/v2/v3 users keep the existing Simplified Chinese interface.
- Verifies all four resource dictionaries expose the same keys and covers live language switching plus settings migration in the offline automated gate.
- Leaves search, scraper priority, NFO/image output, target paths, overwrite policy, movie transfer, rollback, and file hashes unchanged.

## v0.8.1 — Stable

- Promotes dev1 without functional changes after the complete automated gate and P01–P03 real restart acceptance all passed.
- Remembers `直接保存并覆盖（跳过预览）` only when the user explicitly enables `记住保存偏好`.
- Uses preference schema v3 while safely migrating schema-v1 and schema-v2 files with direct overwrite disabled.
- Keeps direct overwrite disabled for first launch, missing or damaged settings, unsupported future settings, and disabled preference memory.
- Clearly reports a restored direct-overwrite choice in the status bar and log without changing preview, movie protection, transaction, or rollback behavior.

## v0.8.1-dev1 — Remember direct overwrite

- Persists `直接保存并覆盖（跳过预览）` when the user explicitly enables `记住保存偏好`, matching the behavior of the other remembered save choices.
- Advances the preference file to schema v3; schema-v1 and schema-v2 files migrate with direct overwrite disabled because they contain no prior opt-in.
- Keeps direct overwrite disabled for first launch, absent or damaged settings, unsupported future settings, and every session where preference memory is not enabled.
- Updates restored-preference status and logs to state when direct overwrite has been restored, without adding another confirmation or changing the existing save transaction.
- Adds Core and WPF coverage for schema-v3 persistence, safe legacy migration, explicit restoration, and safe defaults.
- Passed P01–P03 real restart acceptance for remembered on, remembered off, and preference-memory removal.

## v0.8.0 — Stable

- Promotes RC1 without functional changes after R01–R05, movie SHA-256, and no-op sidecar SHA-256 verification all passed.
- Opens one supported movie directly from the command line or Windows Open with while reusing the existing local-load workflow without automatic network access or writes.
- Persists only explicitly enabled safe target, rename, and metadata-output preferences; direct overwrite always starts disabled.
- Keeps up to five recent custom target roots with selection, individual removal, and full clearing in the compact dark menu.
- Retains unavailable roots without creating them and blocks preview or saving until the selected root is available again.
- Preserves the lightweight single-movie scope and all existing preview, transaction, rollback, source-selection, local NFO, and artwork safeguards.
- Final packaged acceptance confirmed startup entry consistency, restart persistence, unavailable-root isolation, a real custom-target save, local reloading, and unchanged hashes.

## v0.8.0-rc1 — Release candidate

- Promotes the accepted dev3 build without functional changes and freezes the v0.8 daily-efficiency scope.
- Combines startup movie loading, explicitly enabled safe-preference persistence, and up to five recent custom target roots.
- Keeps direct overwrite non-persistent and blocks unavailable custom roots without creating directories or writing files.
- Carries forward the passed E01–E08, P01–P02, and H01–H03 manual checks plus the complete offline automated gate.
- Adds one focused RC checklist for packaged startup, restart persistence, unavailable-root isolation, a real custom-target save, local metadata reloading, and final hashes.
- Passed R01–R05 final integration acceptance with unchanged movie and no-op sidecar SHA-256 values.

## v0.8.0-dev3 — Recent custom roots

- Keeps up to five recent custom target roots inside the explicitly enabled safe-preference file.
- Adds one compact dark `最近目录` menu beside the existing path field for selection, removal of the current history entry, and clearing all history.
- Keeps the current custom path separate from history, so removing or clearing history does not erase the current selection and does not silently add it back on close.
- Migrates the v0.8.0-dev2 schema-v1 settings to schema v2 and seeds its prior custom root as the first recent entry.
- Preserves unsupported future settings before deserializing newer enum values, preventing an older build from erasing a newer configuration.
- Treats missing, disconnected-drive, and unavailable UNC roots as retained but blocked choices with a visible warning; preview and history management never create the root.
- Adds offline Core and WPF coverage for normalization, five-entry bounds, deduplication, migration, future-version protection, dark-menu actions, persistent clearing, and unavailable-root zero writes.
- Passed H01–H03 manual acceptance for recent-root switching, unavailable-root zero creation, individual removal, full clearing, and restart persistence.

## v0.8.0-dev2 — Safe preference persistence

- Adds an explicit `记住保存偏好` switch; nothing is persisted unless the user enables it.
- Remembers only target location, custom root, movie renaming, and NFO/poster/fanart/extrafanart output choices.
- Never persists `直接保存并覆盖（跳过预览）`; every launch restores that risk-bearing option to off.
- Stores a versioned JSON file below the current user's local application-data folder using a same-directory temporary file and atomic replacement.
- Falls back to safe defaults when the settings file is absent or damaged, and preserves an unsupported future-version file instead of overwriting it.
- Adds offline Core and WPF coverage for persistence, cleanup, safe fallback, future-version protection, and the non-persistent overwrite option.
- Passed P01–P02 manual restart acceptance for remembered safe choices, explicit reset, and permanent direct-overwrite opt-out.

## v0.8.0-dev1 — Open one movie at startup

- Accepts exactly one movie path from the process command line, including the path supplied by Windows Open with.
- Reuses the existing movie-selection pipeline for ID parsing, local NFO/artwork discovery, review candidates, and target-path refresh without automatic network access or writes to the movie and its metadata.
- Rejects blank or multiple arguments, directories, missing files, and unsupported extensions with a clear warning while leaving the main window usable.
- Centralizes supported movie extensions and the Windows file-picker filter so startup, drag-and-drop, and manual selection follow one rule.
- Logs startup argument classification and accepted startup movie paths for diagnostics.
- Adds Core resolver coverage and WPF smoke coverage for the real startup-movie handling path.
- Passed E01–E08 manual acceptance for normal startup, command-line and Windows Open with loading, invalid-argument isolation, existing picker/drop regression, and unchanged movie SHA-256.

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
