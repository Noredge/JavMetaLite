# macOS testing

Run from the repository root with the SDK pinned by [global.json](../../global.json) and Xcode or Command Line Tools with a macOS 15+ SDK. Tests use synthetic media and disposable directories. Live network tests require explicit opt-in.

## Complete gate

```sh
./scripts/macos/test.sh --arch arm64 --configuration Release
./scripts/macos/crossbuild-windows.sh --configuration Release
```

Set `DOTNET=/absolute/path/to/dotnet` if the pinned SDK is outside PATH. Logs and per-suite exit codes are written to `artifacts/test-results/macos-arm64/`. A Windows cross-build is not a Windows runtime test.

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

`test.sh` sets `JAVMETALITE_TEST_WORKER` and builds the separate, nonpublishable `JavMetaLite.Worker.RegressionHost`. It requires fixed-HTTP provider integration; direct ad-hoc Swift/Python runs may skip Worker-dependent cases when their environment is missing. Fixture RPC, HTTP injection and regression configuration are unavailable in the shipping Worker.

## Package verification

```sh
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
./scripts/macos/verify-package.sh /path/to/JavMetaLite.app \
  --manifest /path/package.zip.manifest.json --archive /path/package.zip \
  --expected-sign-mode adhoc
```

Packaging extracts its ZIP with `ditto`, verifies payload bytes/signatures/architectures/dependencies/four languages, and runs the production Worker protocol, queue and retention suites from the extracted bundle with no SDK environment. It does not package RegressionHost. The embedded build manifest records source/toolchain/dependency inputs; the external manifest binds the signed payload and ZIP.

## Native manual regression

Use disposable synthetic videos and record actual actions, screenshots and disk hashes separately from builds:

- Import mixed files/folders; cancel discovery, then confirm an exact subset including an unknown-ID item. Preserve old drafts, checks and revisions.
- Click rows to inspect; use checkboxes for query/save scope. All/none replaces the checked set, including filtered rows.
- Edit, switch movies, Undo/Redo, compose Chinese/Japanese input and invoke toolbar/keyboard actions without losing marked text.
- Preview an existing NFO replacement, cancel without writes, then confirm and reread XML/video bytes. Confirm Save authorizes only the listed sidecar replacements. Explicit skip-preview must retain all Worker conflict/revision/transaction checks.
- Change source/target files after preview and verify refusal. Exercise batch collisions, partial cancellation, completed predecessors and unstarted tails.
- Review both artwork roles, samples, viewer navigation/zoom, source switching and slow-preview close/cancel/retry. Late results must not replace another review.
- Import an explicitly captured supported web page; exercise stale navigation, wrong movie ID and user-controlled site verification. Test local image Trash and restore; never substitute permanent deletion.
- Restart with changed settings; restore preferences without restoring transient jobs, drafts, revisions or plans.
- Check all window states in four languages, light/dark and minimum layouts, plus physical keyboard, IME, VoiceOver, gestures and display changes.

The [40-case checklist](WINDOWS-CASE-REGRESSION.md) covers product regression requirements. [STATUS](STATUS.md) records current verified and unverified scope; a controlled API test is not a physical-device or live-site pass.

## Optional measurements and live tests

[Discovery](../../scripts/macos/measure-discovery.py), [Worker lifecycle](../../scripts/macos/measure-worker-lifecycle.py) and [Worker measurement](../../scripts/macos/measure-worker.py) scripts take a shipping Worker and new output paths; use their `--help`. They have no latency/RSS acceptance thresholds and do not establish long-term leak freedom.

[Provider](../../scripts/macos/live-provider-check.py) and [artwork/batch](../../scripts/macos/live-artwork-batch-check.py) checks require `--run-live`, a synthetic video and a fresh workspace/report. They access real sources; default CI does not. Do not upload private paths, original handoff documents or media. A partial result is not an all-source pass.

Physical external volumes, SMB, power loss, full-disk behavior, complete accessibility and another CPU/OS require independent acceptance. Use [the safety requirements](SAFETY-MATRIX.md) when extending tests.
