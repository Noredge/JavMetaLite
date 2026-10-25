# Native macOS client

This is a SwiftUI/AppKit macOS 15+ client using the bundled C# Worker. The M5 client supports NFO and selected artwork output, verified video organization, batch previews and writes, and explicitly ordered multipart confirmation. LibreDMM / R18.dev queries use the Worker; manual browser import uses a separate WKWebView sheet with explicit capture and source/movie checks. All output writes require a Worker-owned preview plan. Local image Trash is separately confirmed, revalidated and refreshed through a Worker lease; it has no permanent-delete fallback and is not part of NFO rollback.

From the repository root:

```sh
./scripts/macos/build.sh --arch arm64 --configuration Debug
./scripts/macos/test.sh --arch arm64
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
```

`macos/JavMetaLiteMac.xcodeproj` contains the app, native protocol test target and a shared `JavMetaLiteMac` scheme. Full Xcode was unavailable on the initial development machine; the equivalent `Package.swift` build and Swift Testing suite were run with Command Line Tools. The Xcode project was structurally checked, not built with Xcode. No App Sandbox is enabled. Run `scripts/macos/build.sh` once for the same architecture and configuration before pressing Run in Xcode. The Xcode build phase copies that already signed, self-contained Worker helper into its app and verifies the helper signature. It fails clearly if the bootstrap artifact is missing; it does not recursively invoke builds.

For Swift-only development (the executable needs the Worker helper assembled by `build.sh` to run):

```sh
swift build --package-path macos
JAVMETALITE_TEST_WORKER="$PWD/artifacts/worker/osx-arm64/Debug/JavMetaLite.Worker" swift test --package-path macos
```

Native process integration tests use disposable synthetic media and isolate Worker state with `--data-root`. The single-movie test fills stderr with 1 MiB before handshake, imports, edits, previews, saves, checks unknown XML nodes and original video bytes, resubmits the same plan and checks shutdown. The M4 test selects local artwork, previews a two-movie batch, and checks verified organization, migrated local images, original video bytes and preserved opaque XML. M5 tests additionally exercise real WKWebView capture into the Worker, actual coordinated NSWorkspace Trash, per-field composition/Undo and image-cache bounds. Worker-dependent tests are explicitly disabled without the environment variable. These synthetic tests do not establish live website, physical IME or full VoiceOver acceptance. `scripts/macos/test.sh` supplies the Worker and the Swift Testing macro plugin required by the current CLT runtime.

The checked-in String Catalog is authoritative for Xcode. Matching English, Simplified Chinese, Traditional Chinese and Japanese `.strings` files permit the CLT fallback, because CLT does not include `xcstringstool`. Tests and product code use the same JSON codec and line framer; Swift treats metadata string fields and unknown optional fields without reinterpreting dates or ratings.

Normal app execution locates only `Contents/Helpers/JavMetaLiteWorker.bundle/Contents/MacOS/JavMetaLite.Worker` inside its resolved bundle. It does not use shell interpolation or a developer's `dotnet` path. Explicit QA launch arguments are available:

```sh
open -n /path/to/JavMetaLite.app --args --worker-data-root /absolute/disposable/state --import-fixture /absolute/synthetic/movie.mp4 --appearance dark
```

No files are imported automatically unless a path is explicitly passed. Appearance changes affect this process only. Queue removal never removes disk files. Drafts are kept per job, pending edits are flushed before a query, source selection, or save preview, and save confirmation submits only the Worker-owned plan token. Quit and close checks cover every edited job and ongoing operations. Navigation selection and batch checkboxes are independent; Space toggles only the highlighted rows’ checkboxes. Batch results retain completed, failed, canceled and unstarted rows, and cleanup warnings never become a generic success. A timeout blocks further mutation and offers read-only status reconciliation without resending the save. Multipart review requires explicit file ordering; its first movie supplies metadata and artwork, and no disk change occurs before the final plan is confirmed.

`Series` and `ContentId` are not displayed in the editor because existing Core NFO output rules do not support direct edits to them. Existing opaque XML remains under the Core round-trip writer's control. Artwork previews are restricted to the Worker cache and keyed by job, artwork ID and current revision. Native requests use cancellable read-only operations; switching jobs cancels stale work. A 128 MiB decoded-thumbnail LRU and background image reads bound normal gallery work. Text views and Undo stacks are bound to a stable job/field identity, preserve marked text during snapshots, and commit composition before actions or close/quit checks.

See `docs/macos/STATUS.md` for actual executed gates and remaining hardware, Xcode, accessibility, input method and distribution checks.

Keyboard entry points: Command-O imports, Command-F focuses queue search, Command-R queries, Command-S saves (opens the preview by default), Command-Option-B opens manual browser import, Command-Option-I toggles the Inspector, Command-Option-L toggles the queue, and Escape cancels sheets. Text fields use native Undo/Redo and Tab navigation; the multiline plot editor keeps Return for newlines. Actual observed keyboard coverage and remaining accessibility checks are listed in STATUS.md.

The `NativeBridgeKitTests` suite directly exercises production AppKit/WebKit adapters: marked text protection, active editor snapshot protection, separate job Undo/Redo, keyboard traversal, byte-bounded thumbnail caching, isolated-world WKWebView capture with stale-navigation/size guards, captured synthetic page import into the actual Worker, and `NSWorkspace.recycle` under file coordination followed by actual Worker rediscovery. Its disposable image is restored from the Trash before fixture cleanup. These automated native API tests do not claim actual Chinese/Japanese input-method interaction or VoiceOver acceptance.

The browser uses an app-specific persistent WebKit website store so verification and sign-in state can survive closing its sheet. It uses WebKit’s standard User-Agent and enables JavaScript. Top-level pages and capture accept only HTTP(S); child frames additionally permit only about:blank and about:srcdoc, which challenge widgets require. Popups, downloads and other external schemes remain blocked, with no JavaScript message handlers. Successful Worker import closes the sheet; failures leave it open. These settings do not guarantee that a third-party challenge accepts embedded WebKit. The 2 MiB HTML capture occurs only on an explicit import; Worker support checks and review revisions remain authoritative. Browser failures are reported in the browser sheet, so parent-window alerts cannot hide them. Local image removal displays canonical paths before confirmation, revalidates a Worker lease immediately before system recycling, and never falls back to permanent deletion. The lease is completed only after the recycle callback returns; partial failures remain visible.

Decoded thumbnails have a 128 MiB LRU budget. Image metadata and decoding run away from the UI actor. Switching jobs or changing revisions cancels owned async Worker preview operations; queue thumbnail requests remain separately scoped. Late responses never populate a different job or revision. Large viewer images are decoded to at most 8192 pixels with an explicit notice; 100% maps image pixels to display backing pixels. Window position/size and Inspector visibility are restored as nonsensitive UI preferences.

Save is the primary toolbar and Command-S action. Preview remains enabled by default. Confirm Save itself authorizes the listed NFO/artwork replacements; no prerequisite overwrite checkbox is needed. More Options can explicitly skip that confirmation sheet, including sidecar replacement, but still calls asynchronous `save.preview`, checks current options/revisions, and submits only an executable Worker plan. Blocked or stale plans stay visible for correction; videos are never overwritten and cross-job conflicts remain blocked. Output/naming/destination/root, query source and panel controls are remembered automatically. A fresh query promotes the latest configured sources and retains prior manual values as candidates; retry preserves existing reviewed choices. Custom source profiles select LibreDMM or R18.dev independently for twelve supported metadata/artwork fields and are validated before mutation.

The image viewer places Close on the left and navigation arrows on the image edges. Actual Size maps one source pixel to one backing pixel; the adjacent percentage exposes the current scale. Native pinch and mouse-wheel zoom preserve an anchor, precise trackpad scrolling pans, and Command-scroll zooms. Physical trackpad acceptance remains distinct from adapter tests. The existing Windows brand artwork supplies the bundled ICNS and Dock image.

JAVLibrary manual import accepts legacy `?v=` links and canonical `/cn|tw|en|ja/jav….html` detail addresses. Provider host/scheme/port checks, captured HTML size, actual page ID, and review revision checks all remain required. Search and challenge pages cannot be imported.

The main artwork-header Source picker now applies all available fields and artwork from a cached/local review source (local NFO, LibreDMM, R18.dev), without an additional query. Source identity, source URL and content ID follow the selected cached source so a return to Local does not retain foreign provider IDs in the NFO. Missing editable fields keep their current values; mixed/manual values have an explicit placeholder, including unsent drafts. Gallery/viewer source filters remain scoped to artwork. The query menu has only individual providers and Custom Sources; Configure Custom Sources edits its per-field profile. More Options is a full-row button with expanded/collapsed accessibility state.

Loaded and in-flight previews survive review-only selection changes when their job/artwork/content identity remains the same. A replaced local image, changed URI/fallback, removed artwork or obsolete request owner still invalidates the result. Legacy Worker snapshots fall back to revision invalidation. This preview identity does not authorize file writes.
