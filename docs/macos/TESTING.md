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
| Core | Portable Core, all Swift/Worker protocol tests, four-language lookup, Xcode source membership, CI tooling, atomic queue removal, stale plans/idempotent receipts, active-operation retention and all artwork-cache lifecycle/budget tests. |
| Full | All core coverage plus the 1000-review queue case, large completed-result/pending-preview byte-budget cases, release tools/manifests and flat Xcode module typecheck. Full CI also runs signed App/ZIP/no-SDK extracted-package regression. |

Only three named Python stress cases are marked full-only. The profile contract test inventories all 103 Worker cases and requires the remaining 100 to stay in core, including all transaction-recovery cases. Core still tests real production retention count limits, stale-token refusal, cancellation and active-operation protection; full tests the large byte budgets. Reports list profile and skipped case IDs explicitly. Package mode rejects core even if a parent environment requests it. Core evidence has its own artifact name and contains no release ZIP.

Worker protocol, retention, queue and artwork-cache suites run with at most two concurrent drivers and independent temporary roots. `worker-suites.json` records suite durations and exit codes; `*.timings.json` records individual cases. Slow RPC/operation/close phases appear as `WORKER_PHASE` in logs. Unexpected shutdown failure requiring a forced kill fails the test; deliberate crash cases still explicitly kill and reap their own Worker.

GitHub selects docs/core/full on main pushes and pull requests targeting main/mac. Edited allowlisted documents use docs checks; edits to known existing C#/Swift source use core. Changes to retention/cache/import/preview pressure-sensitive implementations, packaged resources/licenses, project/SDK/dependency inputs, scripts/workflows, unknown files, additions/deletions and missing comparison bases require full. Windows keeps its existing full automated gate for every product profile. Branch pushes do not duplicate PR gates.

`macos-v*` tags force full validation in both workflows. Manual Mac dispatch defaults to full and also offers core for explicitly checking the daily profile; a core result is never release acceptance. Both named final checks always report the selected level and reject failed classification or required platform work. No scheduled job is required to retain coverage: sensitive changes and release/manual full runs execute the deferred stress tests.

The Mac gate runs:

| Suite | Coverage |
| --- | --- |
| Localization | Four-language keys, placeholders, catalog parity, Foundation lookup and canonical/lowercase lproj layouts. |
| Release tools/manifest | Signing policies, refusal paths, payload/source hashes, permissions, symlinks and source export. Tests never submit to Apple. |
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

Packaging extracts its ZIP with `ditto`, verifies payload bytes/signatures/architectures/dependencies/four languages, and runs the production Worker protocol, queue and retention suites from the extracted bundle with no SDK environment. It does not package RegressionHost. The embedded build manifest records source/toolchain/dependency inputs; the external manifest binds the signed payload and ZIP.

Packaging checks the pre-ZIP App's complete static structure, then runs dynamic startup/cache checks once on the final extracted payload and its protocol/queue/retention suites with two drivers. Package suite logs are retained under `artifacts/test-results/package-arm64/`. Standalone `build.sh` and `verify-package.sh` keep dynamic checks enabled by default; `--structure-only` on verification explicitly reports that Worker execution was deferred. Packaging never accepts structure-only verification as its final gate.

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

[Discovery](../../scripts/macos/measure-discovery.py), [Worker lifecycle](../../scripts/macos/measure-worker-lifecycle.py) and [Worker measurement](../../scripts/macos/measure-worker.py) scripts take a shipping Worker and new output paths; use their `--help`. They have no latency/RSS acceptance thresholds and do not establish long-term leak freedom.

[Provider](../../scripts/macos/live-provider-check.py) and [artwork/batch](../../scripts/macos/live-artwork-batch-check.py) checks require `--run-live`, a synthetic video and a fresh workspace/report. They access real sources; default CI does not. Do not upload private paths, original handoff documents or media. A partial result is not an all-source pass.

Physical external volumes, SMB, power loss, full-disk behavior, complete accessibility and another CPU/OS require independent acceptance. Use [the safety requirements](SAFETY-MATRIX.md) when extending tests.
