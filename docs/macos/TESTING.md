# macOS testing

Run from the repository root with the SDK pinned by [global.json](../../global.json) and Xcode or Command Line Tools with a macOS 15+ SDK. Tests use synthetic media and disposable directories. Live network tests require explicit opt-in.

## Core and full gates

```sh
./scripts/macos/test.sh --profile core --arch arm64 --configuration Release
./scripts/macos/test.sh --profile full --arch arm64 --configuration Release
./scripts/macos/crossbuild-windows.sh --configuration Release
```

Set `DOTNET=/absolute/path/to/dotnet` if the pinned SDK is outside PATH. Omitting `--profile` keeps full validation. Core logs use `artifacts/test-results/macos-arm64-core/`; full logs use `artifacts/test-results/macos-arm64/`. A Windows cross-build is not a Windows runtime test.

| Profile | Required coverage |
| --- | --- |
| Core | Portable Core, all Swift/Worker protocol tests, four-language lookup, Xcode source membership, atomic queue removal, stale plans/idempotent receipts, active-operation retention and all artwork-cache lifecycle/budget tests. |
| Full | All core coverage plus the 1000-review queue case, large completed-result/pending-preview byte-budget cases, CI tooling, release tools/manifests and flat Xcode module typecheck. Full CI also runs signed App/ZIP/no-SDK extracted-package regression. |

Core defers three pressure tests covering a thousand queued reviews and the byte budgets for completed results and pending previews. File conflicts, cancellation, recovery and limits on retained plans run in both profiles.

Worker protocol, retention, queue and artwork-cache suites run with at most two concurrent drivers and independent temporary roots. `worker-suites.json` records suite durations and exit codes; `*.timings.json` records individual cases. Slow RPC/operation/close phases appear as `WORKER_PHASE` in logs. Unexpected shutdown failure requiring a forced kill fails the test; deliberate crash cases still explicitly kill and reap their own Worker.

GitHub runs core tests for Mac and shared changes on main pushes and pull requests to main. Windows runs for Windows and shared changes. Documentation edits run the link checker. Platform workflows also track their build inputs, including packaged licenses and the Windows fixture used by the Mac portable tests.

A `macos-v*` tag or manual Mac dispatch with `validation=full` runs full tests and builds the candidate ZIP. The uploaded `macos-arm64-candidate` artifact contains the ZIP, SHA-256 and manifest. Failed tests upload their logs. Publishing is a separate decision.

The Mac gate runs:

| Suite | Coverage |
| --- | --- |
| Localization | Four-language keys, placeholders, catalog parity, Foundation lookup and canonical/lowercase lproj layouts. |
| Release tools/manifest | `github-adhoc`, explicit `signed-only`/`notarized`, refusal paths, payload/source hashes, permissions, symlinks and source export. Tests never submit to Apple. |
| Lifecycle measurement helper | Six deterministic tests for the canceled-preview read-only retry window, non-busy failures and normal operation timeouts. |
| Portable Core | NFO round-trip, raw XML, parsing, image processing, file identity, conflicts, cancellation and transactions. |
| Swift/native adapters | JSON framing, Worker transport, broken pipes, timeout/retry, review state, AppKit editing/Undo, WebKit, image cache and Trash. |
| Xcode sources | Project membership and non-SwiftPM compile layout; separate from full Xcode/hosted testing. |
| Worker protocol | Import/edit/preview/save, revisions, source isolation, file changes, batch scope, cancellation, recovery and manual import. |
| Queue/retention | Atomic removal, protected inputs, bounded receipts/plans/notifications and stale-token refusal. |
| Artwork cache | Byte/file/lease admission limits, reader lifetime, canceled late results, job removal, active-session protection and owned-orphan recovery. |

`test.sh` sets `JAVMETALITE_TEST_WORKER` and builds the separate, nonpublishable `JavMetaLite.Worker.RegressionHost`. It requires fixed-HTTP provider integration; direct ad-hoc Swift/Python runs may skip Worker-dependent cases when their environment is missing. Fixture RPC, HTTP injection and regression configuration are unavailable in the shipping Worker.

## Package verification

```sh
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
./scripts/macos/verify-package.sh /path/to/JavMetaLite.app \
  --manifest /path/package.zip.manifest.json --archive /path/package.zip \
  --expected-sign-mode adhoc
```

Packaging extracts the signed ZIP with `ditto` and verifies its files, signatures, architectures, dependencies and four languages against the manifest. It launches the extracted App with a disposable data root, waits for its bundled Worker to start, then runs the import/edit/preview/save round-trip against the packaged Worker without an installed SDK. The test reads the NFO back and checks that the video bytes are intact. The full protocol, cache, queue and retention suites run once before packaging.

The embedded manifest records build inputs. The external manifest binds the signed payload to the ZIP. Native acceptance, notarization and first launch on a clean Mac have separate evidence. See [the release policy](RELEASING.md).

## Native manual regression

Use disposable synthetic videos and record actual actions, screenshots and disk hashes separately from builds:

- Import mixed files/folders; cancel discovery, then confirm an exact subset including an unknown-ID item. Preserve old drafts, checks and revisions.
- Click rows to inspect; use checkboxes for query/save scope. All/none replaces the checked set, including filtered rows.
- Edit, switch movies, Undo/Redo, compose Chinese/Japanese input and invoke toolbar/keyboard actions without losing marked text.
- Preview an existing NFO replacement, cancel without writes, then confirm and reread XML/video bytes. Confirm Save authorizes only the listed sidecar replacements. Explicit skip-preview must retain all Worker conflict/revision/transaction checks.
- Change source/target files after preview and verify refusal. Exercise batch collisions, partial cancellation, completed predecessors and unstarted tails.
- During cancellation/rollback, replace a committed NFO, artwork or target video externally. Preserve that changed version and report recovery instead of deleting it. Cover both single and multipart transactions, including a replacement appearing while an output is being isolated for rollback.
- Review both artwork roles, samples, viewer navigation/zoom, source switching and slow-preview close/cancel/retry. Late results must not replace another review.
- Import an explicitly captured supported web page; exercise stale navigation, wrong movie ID and user-controlled site verification. Test local image Trash and restore; never substitute permanent deletion.
- Restart with changed settings; restore preferences without restoring transient jobs, drafts, revisions or plans.
- Terminate only an isolated QA engine while idle, with drafts and after a deliberately withheld save response. Verify Keep Open, canceled/failed export, JSON readback and explicit end-session/close. A live response timeout must retain status reconciliation. Record actual disk outcomes separately from the frontend's unknown result.
- Pause an isolated engine's stdin reader and send a request larger than pipe capacity. The request deadline must include writing, the transport must stay responsive, and a canceled/expired partial frame must never be followed by another frame or automatically replayed.
- Inspect recovery records, open their directories and verify current file state. Archive only an eligible, unchanged record after explicit confirmation. Check stale verification, changed files, active owners and unsafe paths; archival must not modify media or execute an old save plan.
- Check all window states in four languages, light/dark and minimum layouts, plus physical keyboard, IME, VoiceOver, gestures and display changes.

The [40-case checklist](WINDOWS-CASE-REGRESSION.md) covers product regression requirements. [STATUS](STATUS.md) records current verified and unverified scope; a controlled API test is not a physical-device or live-site pass.

## Legacy transaction records

Old Preview journals do not contain the before/after evidence required for automatic verification. An unreadable record, unavailable volume, unknown result or unverified temporary file must remain blocked. A programmatic "mark resolved" button cannot establish that the files are safe.

For an operator-reviewed migration, first export the current review reference and preserve the original journal. Inspect every listed source, target and temporary path against trusted backups and record what actually exists, what was saved and what was restored. Keep all uncertain versions; do not run an old plan or delete temporary media to remove a warning. If any result cannot be established, stop and retain the record.

Only after the complete result is independently established, quit every JavMetaLite instance, preserve the written inspection alongside the original journal, and move that journal into a separate `recovery/manual-reviewed/` folder. This is a manual acknowledgement with retained evidence, not an application-verified committed/restored result. Restart and import the actual files again to obtain new baselines and previews; reapply reviewed fields deliberately from the reference export. The application does not perform this migration or replay the old transaction.

## Optional measurements and live tests

[Discovery](../../scripts/macos/measure-discovery.py), [Worker lifecycle](../../scripts/macos/measure-worker-lifecycle.py) and [Worker measurement](../../scripts/macos/measure-worker.py) scripts take a shipping Worker and new output paths; use their `--help`. They have no latency/RSS acceptance thresholds and do not establish long-term leak freedom. The lifecycle driver retries only a read-only preview that reports `JOB_BUSY` after cancellation, with a 10-second admission window and normal timeouts for already-started operations; it never retries a save. Its six boundary tests run in the full gate. Record the shipping Worker hashes, duration, cycles and OS-cache assumptions when reporting a measurement.

[Provider](../../scripts/macos/live-provider-check.py) and [artwork/batch](../../scripts/macos/live-artwork-batch-check.py) checks require `--run-live`, a synthetic video and a fresh workspace/report. They access real sources; default CI does not. Do not upload private paths, original handoff documents or media. A partial result is not an all-source pass.

APFS disk-image results do not establish physical external-volume, SMB, power-loss or other-filesystem behavior. Complete accessibility and another CPU/OS require independent acceptance. Use [the safety requirements](SAFETY-MATRIX.md) when extending tests.
