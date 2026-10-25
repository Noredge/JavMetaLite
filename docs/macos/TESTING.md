# macOS build and verification

Default offline tests use synthetic media fixtures and disposable temporary directories.
Explicit opt-in live tests also download real provider metadata/artwork; their video files remain synthetic.
The scripts never scan a real media library, install a toolchain, or change `global.json`.
Run commands from the repository root with the exact pinned .NET SDK (10.0.400).

```bash
# Optional overrides for an existing private SDK installation:
export DOTNET=/absolute/path/to/dotnet
export DOTNET_ROOT=/absolute/path/to/sdk-directory
./scripts/macos/build.sh --arch arm64 --configuration Debug
python3 scripts/macos/test-localization.py
./scripts/macos/test.sh --arch arm64 --configuration Release
./scripts/macos/crossbuild-windows.sh --configuration Release
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
./scripts/macos/verify-package.sh artifacts/macos/arm64/Release/JavMetaLite.app
open artifacts/macos/arm64/Release/JavMetaLite.app
```

`build.sh` checks the pinned SDK and macOS SDK, uses the committed Swift package,
then assembles and signs a self-contained `.app` (ad-hoc by default). Apple Command Line Tools
can build the package; the Xcode project/shared scheme provide the full-Xcode path.
The script reports when full Xcode is missing and does not claim Xcode UI tests.
It never changes the global developer-directory selection.

The app resolves its helper inside its bundle and invokes it directly with `Process`.
Publishing is self-contained, nontrimmed, non-AOT and not single-file. The script
signs payload files, then Worker/helper, then the outer app. Managed DLLs and other
payload files use explicit generic signatures because they are in the helper's
MacOS code directory. It never uses `--deep` to sign. `verify-package.sh` uses deep
strict verification, checks all Mach-O architectures/dependencies and runtime
inventory, app/helper version agreement, the statically linked native text/browser/
artwork/Trash implementations, and all four localized payloads. It launches the
real Worker with no developer PATH or installed SDK. The package
script also extracts its ZIP into a path containing spaces and Chinese characters,
rechecks signatures, then runs all production Worker protocol cases (`--production-only`) from that extracted
self-contained bundle with an isolated HOME/TMPDIR and no DOTNET_ROOT.

The ad-hoc Worker has `allow-jit` and `disable-library-validation`. The latter is
required by the observed local hardened-runtime error when loading ad-hoc .NET
libraries: “mapping process and mapped file ... have different Team IDs.” It is
scoped to the ad-hoc Worker, not Swift or system configuration. Developer ID mode
uses only `allow-jit` on the Worker, requires an exact valid Application identity,
and verifies a consistent team and secure timestamp throughout the payload. It
fails before building when the identity is unavailable; no unsigned/ad-hoc fallback
exists. The current user-selected policy is **signed-only**: package/build perform
signing and verification, without notarization upload, stapling, or credential
requests. This host has 0 valid signing identities; ad-hoc local signing remains
available, while real Developer ID signing remains unverified. `RELEASING.md`
retains the optional notarized workflow for a separately authorized future run.
Its implementation and fixtures are not actual Apple approval or system trust.

Outputs:

- App: `artifacts/macos/<arch>/<configuration>/JavMetaLite.app`
- ZIP/SHA-256/payload manifest: `artifacts/macos/JavMetaLite-macOS-<arch>-<configuration>-<sign-mode>.zip*`
- Worker: `artifacts/worker/osx-<arch>/<configuration>/`
- Logs: `artifacts/test-results/macos-<arch>/`

The current discovery-confirmation continuation (build 9) uses the user-facing
delivery directory `outputs/Mac-regression-discovery-2026-10-22` under the chat
workspace. The previous Windows-case delivery, `outputs/Mac-regression-2026-10-22`, and earlier
`outputs/M6-artwork-batch-live` and `outputs/M6` artifacts and acceptance evidence
are retained separately. STATUS/evidence distinguishes each delivered build and
its actually observed native checks.

## Automated gates

`test.sh` runs these suites and preserves failures through logging pipelines:

1. `python3 scripts/macos/test-localization.py`: four-language catalog/strings key
   parity, translation state, format placeholders, literal UI keys, stable error
   reasons, SwiftPM/Xcode registrations; compiles the actual `Localization.swift`
   and checks every locale/key through Foundation Bundle lookup in both canonical
   and SwiftPM 6.1 lowercase lproj layouts, plus regional
   system-language selection and English/missing-key fallback. It does not change
   user preferences. Package verification separately compares all four bundled
   translation payloads with the reviewed catalog.
2. `test_release_tools.py` checks signing/notarization command contracts and refusal
   paths using controlled command results. Its readiness cases cover default
   signed-only versus explicit notarized policy, independent signature/ticket/
   Gatekeeper results, ad-hoc refusal for Developer ID readiness, failed integrity,
   and CLI exit codes/report-path guards. These tests never submit to Apple.
   `test_release_manifest.py` checks real filesystem edits, missing/extra payloads,
   executable permissions, generic-signature xattrs, symlink escapes, archive hash
   changes, Python optimized-mode enforcement, strict CLI failures, and a stable
   source export that validates after Chinese-path extraction without `.git`.
3. `dotnet run --project JavMetaLite.PortableTests -c Release`: actual Core/Skia
   NFO round-trip; unknown XML/actor/raw-string retention; new NFO; malformed/DTD/
   oversized input rejection; source/target changes, including a target changed
   during commit; stale revision; video conflict; cancellation; Unicode paths;
   dotted-prefix/CD discovery; symlink loops and case aliases; conservative volume
   policy; batch read/write/extrafanart conflicts and failure/cleanup stops;
   verified-copy cancellation; confirmed multipart ordering; image bounds/EXIF/crop.
4. `swift test --package-path macos`: Swift Testing framing/DTO tests and real
   `WorkerClient` integration. `test.sh` sets `JAVMETALITE_TEST_WORKER` explicitly.
   The standalone test invocation needs this variable to exercise the subprocess.
   The script explicitly loads the installed toolchain's Swift Testing macro plugin
   when present, addressing a reproduced CLT 6.4 incremental swiftbuild discovery error.
   `NativeBridgeKitTests` additionally exercises real AppKit text input/undo,
   bounded image caching/dimensions, WebKit capture with synthetic HTML and the
   Worker, and `NSWorkspace` Trash for one disposable synthetic image. The Trash
   test records the returned location and restores that image before temporary-
   directory cleanup. It first presents and cancels two consecutive real window
   sheets, releasing each Worker reservation and verifying the original remains.
   Regression tests also exercise production queue classification/checked-set
   separation, gallery source/position rules, import diagnostics and their dismissal
   without changing jobs/revisions/selections, the multiple-file/directory picker,
   and full diagnostic copying through a unique test pasteboard. The clipboard test
   never reads or writes the user's general pasteboard.
   Discovery tests present the production confirmation sheet with a real Worker,
   check that cancel preserves existing drafts/selection/revisions, and import only
   explicitly selected items, including an item without a parsed movie ID. They
   also cover expired-receipt recovery without automatic replay. A programmatic
   sheet test is separate from the recorded Finder/keyboard native acceptance.
   Programmatic marked-text API checks
   are not physical Chinese/Japanese input-method acceptance; synthetic WebKit
   page checks are not live-provider or website verification-flow acceptance.
5. `python3 scripts/macos/test_worker.py <worker-executable>`: real Worker fragmented/
   consecutive JSON, stderr drainage, incompatible handshake, malformed/oversized
   frames, import/edit/preview/commit, partial-edit atomicity, stale revision/plan,
   source/target changes, duplicate-save idempotence, malformed/DTD NFO protection,
   overwrite consent, duplicate/partial import, safe queue removal, restart/recovery
   barriers and EOF during save. M4 cases cover three organization destinations,
   same-size/same-timestamp video changes detected with SHA-256, missing custom roots,
   existing-video refusal, ordered/idempotent batch results, aggregate conflicts and
   skipped-input protection, recursive/multipart imports, explicit part confirmation,
   real local-artwork/sample migration, revisioned artwork selection, partial batch
   cancellation, late-source-change partial failure, asynchronous preview, active-
   transaction import exclusion and retained unsaved metadata when NFO is disabled.
   Cancellation/failure fixtures each use a disposable sparse 512 MiB video; they
   observe real copy progress before cancellation or an external edit. These cases
   check that completed predecessors stay saved and the tail remains NotStarted.
   M5 cases exercise captured HTML parsing/import for all three supported sources,
   source/URL/number/size/revision guards, shared-file Trash reservations, stale or
   changed-file refusal, failed-removal refresh, and preservation of manual fields
   and selected online artwork. Python simulates the external removal result;
   actual macOS Trash is covered separately by the native integration test.
   A local HTTP fixture stalls image bodies to verify two-request concurrency,
   responsive snapshots, cancellation of active and queued previews, temporary-
   cache cleanup, a successful subsequent preview, and oversized-image refusal.
   The native client's full default options payload, including an unused empty
   custom-root field, is tested before and after Trash; preview/save still require
   overwrite consent, while an actually selected empty custom root remains blocked.
   Build 9 adds discovery without loading jobs, exact selected-file-set import,
   explicit CD grouping, unknown-ID selection, cancellation before queue admission,
   changed identity/link refusal, late-arriving sibling exclusion, single-use and
   expired tokens, bounded discovery receipts, and controlled ReadFailed diagnostics.
   The I/O failure fixture injects an IOException at the discovery-service boundary;
   it does not demonstrate a physical disk read failure.
6. `python3 scripts/macos/test_worker_retention.py <worker-executable>`: five real
   protocol cases exercise completed-operation and pending-preview count/byte
   limits, protection of running operations, cancellation, recent execute
   idempotence, and refusal of evicted plans without new writes. `test.sh` runs this
   separately from the 63-case protocol suite. The 15-minute preview and 30-minute
   operation age limits have not been verified by waiting their full wall-clock
   durations. Discovery token expiry has a separate injected-clock regression.

Build 9's executed gates contain **50 portable groups / 559 assertions**, including
the opt-in **16-name private parsing matrix** as one group; **63 Python protocol
cases** (49 shipping-Worker + 14 separate regression-host cases); **34 Swift tests**;
**357 localization keys / 1458 runtime checks**; **5 retention cases**; **36 release
tool contracts**; and **15 manifest/CLI tests**. The default portable suite without
the private matrix has 49 groups. No private source names are printed in these logs.
The supplied case-sensitive local image volume previously added another group
(51 groups / 578 assertions); it was detached and was not present in this build 9
full gate. These counts do not imply manual hardware, accessibility or Apple acceptance.

The complete gate records suite exit codes in
`artifacts/test-results/macos-arm64/results.tsv` and per-suite logs beside it.
Final Swift and localization reruns after the expired-receipt change are recorded in
`artifacts/test-results/discovery-swift-final-full.log` (17 + 17 tests, 34 total) and
`artifacts/test-results/discovery-localization-final.log` (357 / 1458).
Keep those final reruns distinct from the earlier complete-gate snapshot.

Historical Windows-case/build 8 evidence remains **53 Python cases** (44 shipping
Worker + 9 regression-host), **29 Swift tests**, and **330 localization keys / 1350
runtime checks**. Those older counts describe that source snapshot, not build 9.

The previous policy-only phase reran the affected **36 command/policy contracts** and
**15 manifest/CLI tests**, both passing. The prior Core, Worker and Swift counts
above identify retained suite evidence; they were not rerun for a policy/docs-only
change. Focused logs are `artifacts/test-results/m6-signed-only-{contracts,manifest}.log`.
The subsequent artwork/batch continuation actually reran Core41/252, Worker41,
Swift20, contracts36 and manifest15, including the final extracted signed bundle.
See STATUS.md and LIVE-ARTWORK-BATCH-QA.md for this round's exact scope.

`release-readiness.py` defaults to `--policy signed-only`. Without
`--require-distribution`, exit 0 means local package verification; with that flag,
signed-only requires a verified Developer ID package. `--policy notarized` retains
the former Developer ID + stapled ticket + Gatekeeper requirement. The report always
separates `signatureReady`, `notarizationVerified` (stapled-ticket validation),
`gatekeeperAccepted`, and `notarizedDistributionReady`; `distributionReady` follows
the selected policy. Read-only evidence in `m6-signed-only-readiness.json` records
local verification true, sign mode ad-hoc, 0 valid identities, and all those readiness/
trust booleans false. No actual signing, notarization submission or packaging was
performed by that policy check. It neither queries an Apple submission nor verifies
notarization credentials.

The Python suite uses only the standard library. The portable runner follows this
repository's console-test convention: `dotnet test` alone is not the gate.
Fixture-backed LibreDMM cases use the real client/parser with injected HTTP, and
are not live provider availability tests. Cross-language golden data lives under
`contracts/worker-v1`.

Existing Windows gates remain available:

```powershell
./scripts/Test-Automated.ps1
./scripts/New-ReleasePackage.ps1
./scripts/Test-PortablePackage.ps1
```

The transaction runner prints each case before executing it and terminates with
an error after a two-minute case timeout; remaining cases are not counted as
passed. Windows CI retains its transcript as `windows-automated-gate-evidence`
on success or failure. Its gated HTTP response stream is shared with the portable
suite, which checks cancellation and fatal sibling failure for both single and
multipart transactions. Source-retirement tests separately cover readable locks
(complete rollback) and exclusive locks (reported, retained verified recovery copies).

`crossbuild-windows.sh` compiles the Windows app and all three Windows test
projects, preserving each exit code in `artifacts/test-results/windows-crossbuild-results.tsv`.
Cross-compilation of WPF on macOS does not prove Windows runtime behavior. Run
these gates on Windows before merging shared-Core changes. The separate Mac CI
preserves the Windows workflow. The current [GitHub runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
identifies `macos-15` as arm64; CI additionally checks its actual architecture.
Actions are pinned to full commit IDs resolved from the official
[checkout](https://github.com/actions/checkout),
[setup-dotnet](https://github.com/actions/setup-dotnet) and
[upload-artifact](https://github.com/actions/upload-artifact) repositories. The
workflow has read-only repository permissions and uploads test evidence and local
packages; it does not publish a GitHub Release or use signing secrets.

## Reproducing a package

Mac publishing restores RID-specific NuGet lock files in locked mode. It records
both NuGet's normalized content hash and the actual signed `.nupkg` archive hash,
which are different values. Full runtime/native licenses are compared with the
restored upstream package bytes before bundling. Normal developer/Windows restores
do not rewrite the separate `packages.osx-*.lock.json` files.

Before signing, `Contents/Resources/BUILD-MANIFEST.json` captures source file hashes,
baseline commit/dirty state, SDK/Swift/macOS SDK versions, selected architecture,
resolved packages/runtime, license hashes and all four translation payloads. After
signing, `<zip>.manifest.json` records every distributed file's bytes/mode/signature
xattrs, internal symlinks, native architectures and ZIP hash. Verify a historical
package against its own manifest rather than a newer checkout:

```bash
./scripts/macos/verify-package.sh /path/JavMetaLite.app \
  --manifest /path/package.zip.manifest.json --archive /path/package.zip \
  --expected-sign-mode adhoc
```

`source-archive.py /path/source.zip` exports tracked and untracked source plus
`SOURCE-MANIFEST.json`, excluding ignored SDKs/build products. It refuses to replace
an existing output unless `--overwrite` is explicit. ZIP entry ordering, timestamps
and modes are stable for the same source snapshot. An extracted source tree needs
no `.git`: building verifies the export manifest first. Intentional edits require
`--allow-modified-source-export`; their actual hashes and changed paths are then
recorded explicitly, never described as an unchanged verified export.

These records support reproducing and auditing the build inputs. Signed binaries,
secure timestamps, notarization tickets, compiler paths and ZIP metadata may differ
between builds; byte-identical signed output is not promised. Full Xcode and CI
execution require their own observed logs; preparing a workflow does not run it.

## Native manual acceptance

Use disposable synthetic video bytes next to a copy of `fixtures/macos/round-trip.nfo`.

- Launch from Finder and from a path containing Chinese characters and spaces.
- Discover mixed files/folders without adding jobs; cancel and verify old drafts,
  revisions, checks and selection stay intact. Rescan, select a subset (including a
  no-ID item), confirm and verify only those bound files enter the queue. Inspect
  duplicate/unavailable items and diagnostics; expired results must require rescan.
- Switch among English, Simplified Chinese, Traditional Chinese and Japanese;
  inspect long alerts/preview paths in a narrow window and language-specific text.
- Edit fields, switch tasks and return; use native undo/redo and Chinese/Japanese IME.
- Cmd-S opens the actual preview. Confirm Save itself authorizes the listed sidecar
  replacements; no prerequisite overwrite checkbox is needed from build45 onward.
  Cancel first and verify no writes; then confirm, reread the NFO, and verify unknown
  XML/actor subnodes/video bytes remain intact. Explicit skip-preview includes sidecar
  replacement but must still block video/cross-job conflicts. Change output, destination,
  naming/source and panel controls, quit/relaunch, and verify restored settings without
  restoring queue checkmarks, drafts or server plans.
- Change NFO externally after preview: confirmation must fail without overwriting.
- Select artwork/samples, preview all outputs, organize to a sibling or custom number
  folder, then inspect actual output files and the updated queue paths.
- Enlarge an image, inspect pixel dimensions, fit/100% zoom and previous/next;
  switch movies while an image request is pending and check that an old result
  cannot replace the current selection.
- In the private browser, verify a supported current page and import it explicitly;
  navigate or switch movies before capture completes and check stale-result refusal.
- Move a disposable local image to Trash through its explicit confirmation; inspect
  the refreshed image selection and restore it from Trash. A failed recycle must
  retain the original, without any permanent-delete fallback.
- Preview selected movies together, check aggregate conflicts, save sequentially,
  and cancel a later large synthetic item; completed predecessors must stay saved.
- Inspect empty/single/multiple/artwork/preview/failure/settings states in light/dark
  and at 920 × 620 pt; check inspector collapse and text focus.
- Check Cmd-O/R/S/./F, settings, VoiceOver labels, close/reopen and unsaved/save-in-
  progress exit choices.

Record screenshots and observed actions separately from builds and protocol tests.
Unavailable checks remain explicitly unverified in `STATUS.md`.

## Independent hardware/release checks

The recorded local, mounted case-sensitive APFS sparse image tests exercised real
case-sensitive file identities and distinct device IDs, including verified copy
and cancellation. That evidence is not a physical external-drive test. Removable-
volume unplug/remount, SMB, permission revocation, disk-full/device-loss and
power-loss recovery still need specific hardware evidence; controlled process
termination/recovery tests do not establish those failure modes. Also separate: live providers, browser verification,
Jellyfin scanning, Developer ID JIT, optional notarization, Gatekeeper, Intel packages, macOS 15
minimum-version execution, performance, IME and VoiceOver. Temporary-directory
fault injection is not physical-disk or SMB validation.

## Bounded discovery and lifecycle measurements

These opt-in scripts use disposable synthetic fixtures and real Worker processes.
They have **no latency, throughput or RSS pass thresholds**. Their assertions check
protocol/file safety and measurement completion; `completed: true` is not a
performance acceptance result. Both require a new output path and refuse to
replace existing evidence. A development apphost requires `DOTNET_ROOT`; a
published self-contained Worker can run without the installed SDK.

```sh
JML_WORKER='/absolute/path/JavMetaLite.app/Contents/Helpers/JavMetaLiteWorker.bundle/Contents/MacOS/JavMetaLite.Worker'
python3 scripts/macos/measure-discovery.py "$JML_WORKER" \
  --counts 8 71 362 425 \
  --output /absolute/path/new-discovery-measurement.json
python3 scripts/macos/measure-worker-lifecycle.py "$JML_WORKER" \
  --label build9-local-lifecycle \
  --jobs 16 --cycles 100 --maximum-cycles 2000 \
  --duration-seconds 300 --interval-seconds 1 --cold-runs 3 \
  --output /absolute/path/new-lifecycle-measurement.json
```

`measure-discovery.py` accepts counts from 3 through 1000 (default 8, 71, 362, 425).
Each size uses a fresh process/session and one synthetic video, local NFO and
image per job. It separately times discovery, selected-item confirmation/local
NFO loading, a loaded snapshot, first/repeated local image preview, and a three-job
NFO-only batch preview/execute. It verifies zero jobs before confirmation,
unchanged media before execution, exactly three changed target NFOs afterward,
unknown XML/comment retention, unchanged videos/images/other NFOs, and session-cache
removal after shutdown. The report includes Worker identity, fixture sizes,
host/volume information and RSS after confirmation.

Optional `--synthetic-video /path/synthetic.mp4` and
`--artwork-fixture /path/synthetic.png` use caller-supplied files (PNG/JPEG artwork);
their original hashes are checked for preservation. Without them, tiny nonplayable
video bytes and a generated 600 × 400 PNG are used. `--temporary-parent` chooses
an existing disposable parent directory. The script does not verify supplied video
playback/duration, issue HTTP requests, drive UI, or establish cross-volume transfer
performance. Counts alone must not be presented as a native UI or large-media test.

`measure-worker-lifecycle.py` performs fresh-process initialization runs, then
repeated import/duplicate-import, local image preview/cache reuse, asynchronous
save-preview cancellation, a subsequent preview, and queue removal. It checks
source SHA retention, an empty queue after each cycle and cache removal on graceful
shutdown. It does **not execute media saves**. RSS is sampled with `ps`; cache sizes,
operation/preview retention and the cancellation race's actual terminal status
are recorded. A fast preview may complete before cancellation, so both completed
and canceled outcomes are reported. The loop must satisfy both `--cycles` and
`--duration-seconds`; reaching `--maximum-cycles` first is a failure. Limits are
1–1000 jobs and 1–10000 maximum cycles. Fresh-process results do not mean a cold OS
filesystem cache: neither measurement script purges that cache.

For separately operated native UI checks, the lifecycle script can only sample
existing process RSS, without launching a Worker or sending UI actions:

```sh
python3 scripts/macos/measure-worker-lifecycle.py \
  --label separately-operated-ui --observe-pid 12345 --observe-pid 12346 \
  --duration-seconds 300 --interval-seconds 1 \
  --output /absolute/path/new-ui-rss-observation.json
```

Use actual app/helper PIDs; these example numbers are placeholders. Record the
operator's actions and screenshots separately. RSS includes managed runtime and
native allocations, so its growth or decline alone is not a leak diagnosis.
The saved `artifacts/test-results/discovery-lifecycle-comparison.json` compares
one approximately 300-second run per binary: build 8 (280 cycles) and a retention/
discovery candidate (282 cycles), with other builds/tests running on the host.
It does not establish a speedup, absence of leaks, long-term stability or final
build 9 native performance. Those five-minute runs did not wait for the 15/30-minute
retention age limits. Keep component hashes and conditions with each result; use
new evidence paths for final-bundle measurements rather than relabeling a candidate.


## Live artwork and batch acceptance (explicit opt-in)

`live-artwork-batch-check.py` uses SSIS-001/STARS-804/IPX-177, a user-supplied synthetic MP4, registered production provider artwork, and one unselected synthetic protection job. It validates all selected source samples (refuses >50 rather than mislabeling truncation), source-byte deduplication, PNG full-cover output and right-half posters using the independent ImageIO/CoreGraphics `compare-image-output.swift`, then saves an ordered three-job batch. Remote reference files stay in the disposable workspace; reports contain URLs/dimensions/hashes, not rendered artwork.

```sh
python3 scripts/macos/live-artwork-batch-check.py --run-live \
  --worker '/path/to/JavMetaLite.app/Contents/Helpers/JavMetaLiteWorker.bundle/Contents/MacOS/JavMetaLite.Worker' \
  --video-template '/path/to/synthetic-2s.mp4' \
  --workspace '/path/to/new-artwork-run' \
  --report '/path/to/new-artwork-report.json'
```

Optional `--reference-report` reuses only prior successful assets whose current registered URL/ID/role match and whose file SHA/dimensions revalidate; the report explicitly distinguishes fresh acquisition from reused references. Actual output saving still performs live downloads. A partial source result is not an all-pass batch report. See LIVE-ARTWORK-BATCH-QA.md for exact candidate boundaries, the retained initial failure, and recorded results. No live requests are added to default CI.

Protocol case 41 covers registered-source fallback, actual resolved URL, unchanged review/media, terminal size/decode limits and no caller URL substitution. Existing cancellation/two-slot tests remain. This is separate from physical removable media, Windows runtime, or visual image acceptance.


## Windows historical-case continuation

The private handoff is input evidence, not proof of Mac acceptance. Its 40 primary
case IDs and actual automated/native scope are mapped in WINDOWS-CASE-REGRESSION.md.
The original private document and exact-name JSON stay outside the repository.
Equivalent public fixtures exercise the same parser boundaries; the opt-in exact
16-name matrix has been executed locally without logging the original names.

`test.sh` builds `JavMetaLite.Worker.RegressionHost` and sets
`JAVMETALITE_SEARCH_TEST_HOST`, then runs Python with `--require-search-host`.
This is a separate, nonpublishable test executable using the actual Workspace,
provider parsers, scheduler and file transactions. HTTP handlers, test clock,
cleanup-failure delegates and a controlled discovery-service substitute are
injected at their service boundaries. The host alone accepts `--fixture-config`
and its regression configuration/data-root environment fallbacks; production
Worker has no fixture RPC or provider-configuration CLI and does not read those
regression environment variables. The default package never contains the host.
A complete gate cannot silently skip its 14 integration cases; direct Python
runs without the host report skips. ZIP extraction explicitly selects
`--production-only`: all 49 shipping-process cases run with no SDK environment.

Optional local-only extensions:

```sh
JAVMETALITE_PRIVATE_PARSING_CASES=/absolute/private/parsing-cases.json \
  DOTNET=/absolute/path/to/dotnet ./scripts/macos/test.sh --arch arm64 --configuration Release
# Add JAVMETALITE_CASE_SENSITIVE_ROOT only for a newly mounted disposable image volume.
```

The volume fixture asserts actual device-ID difference, full-SHA copied output and
source retirement, plus source-hash retention and clean staging after cancellation.
The recorded run used a local sparse APFS image, then detached normally; it does
not validate a physical external drive, unplug, SMB, permission revocation or power loss.
Private fixture data, raw media paths, or the attached document must not be uploaded
as a side effect of these local tests.

The first new Core run retained a real case-alias duplicate failure plus a test-only
/var-versus-/private/var key mismatch. An initial build5 custom arrow handler passed
model tests but failed actual list scrolling/accessibility selection; it was removed
and the unused navigation-only model test deleted. Builds 6 and 7 restored native
List key routing and were checked separately in the actual app. Build 7 added the
explicit Copy Details action and its unique-pasteboard test, bringing the final
Swift suite to 29 tests. Native build 7 acceptance actually copied and pasted both
complete long diagnostic paths; scrollable/selectable text remains available.
These final tests are not the same 29 tests reported before the build 5 navigation
failure. Do not relabel the initial all-gate success as proof that the initial native
UI passed.

Native build 7 also exposed a post-save quality gap: an unopened 147 × 200 full
cover was correctly flagged, but its advisory disappeared after a successful
NFO-only save. The Worker reload now carries the newly measured matching local
cover into the current quality revision. Extended protocol case 47 checks six
local-cover states through real NFO saves, byte retention and stable reads, plus
a retained online-source case that must not inherit local dimensions. The focused
case passed in `artifacts/test-results/regression-post-save-quality.log`; two earlier
test-fixture assertion/setup failures are retained separately. Build 8 native
acceptance remains a separate observed check recorded in STATUS/evidence, not an
inference from this test.

Final build8 native retest: NFO preview/confirmation succeeded on the same synthetic FNS fixture; 147×200 low-cover warning and review status remained after save. Disk readback changed only the target NFO among153 baseline files, preserving152 SHA values and unknown XML. Full53 Worker tests passed in13.199s; final ZIP44 production tests passed in11.990s, independent rebuild44 passed in11.602s. See STATUS and the historical matrix; earlier build7 failure evidence remains.
