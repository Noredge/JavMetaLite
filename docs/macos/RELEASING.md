# macOS releases

Current preview: [macOS 0.4.0 Preview 2 (build47)](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v0.4.0-preview.2), Apple Silicon, macOS 15+. Verified scope and limitations are maintained in [STATUS](STATUS.md).

## Build and verify

```sh
./scripts/macos/test.sh --arch arm64 --configuration Release
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
./scripts/macos/verify-package.sh /path/to/JavMetaLite.app \
  --manifest /path/package.zip.manifest.json --archive /path/package.zip \
  --expected-sign-mode adhoc
```

Use the SDK pinned by `global.json`. The self-contained Worker needs no user-installed .NET runtime. NuGet restores use per-RID locks. The embedded build manifest records exact source/toolchain/dependencies; the external manifest and SHA bind the signed payload and archive. Extract with `ditto` to preserve signature attributes. Signed binaries/ZIPs are not promised byte-identical across toolchains or signing dates.

## GitHub distribution policy

The project's default is **`github-adhoc`: GitHub distribution with local ad-hoc signing and per-app user approval**. This policy permits both preview and stable versions after the selected automated checks and required manual acceptance are complete. Developer ID and notarization are not required by this project policy. Do not submit to Apple, staple tickets or request signing/notarization credentials as part of this workflow.

Ad-hoc signing verifies local package integrity. It does not identify the publisher through Apple, establish an Apple malware scan or guarantee automatic Gatekeeper acceptance. Every GitHub release must state the signing mode and include [first-launch instructions](../../macos/README.md#first-launch). The same instructions are bundled in `Contents/Resources/READ-ME.md` from [PACKAGE.md](PACKAGE.md).

The explicit `signed-only` and `notarized` policies remain available without lowering their requirements. If Developer ID distribution is deliberately selected, use an existing matching Application certificate/private key in Keychain:

```sh
./scripts/macos/package.sh --arch arm64 --configuration Release \
  --sign-mode developer-id \
  --sign-identity 'Developer ID Application: Your Name (ABCDE12345)' \
  --team-id ABCDE12345
```

Missing/mismatched identities fail without fallback. Sign nested payload, Worker/helper and outer App in order; never sign with `--deep`. Verification uses strict/deep inspection. Developer ID mode checks consistent teams, hardened runtime and secure timestamps, with JIT entitlement on the Worker only. Keep credentials and private keys outside source, logs and artifacts.

Read-only readiness:

```sh
python3 scripts/macos/release-readiness.py /path/to/JavMetaLite.app \
  --manifest /path/package.zip.manifest.json --policy github-adhoc --require-distribution \
  --report /path/readiness.json
```

| Policy | Automated requirements |
| --- | --- |
| `github-adhoc` (default) | Verified local package and a valid local ad-hoc or Developer ID signature |
| `signed-only` | Verified package with Developer ID signing |
| `notarized` | All `signed-only` requirements, plus a valid stapled ticket and accepted Gatekeeper assessment |

`distributionReady` means only that the selected automated policy passed. `localPackageVerified` and `localSignatureVerified` report integrity; `signatureReady` still requires Developer ID. `notarizationVerified` and `gatekeeperAccepted` remain independent observations. Thus a verified ad-hoc package can pass `github-adhoc` while all three trust fields remain false. The tool always reports `manualAcceptanceVerified: false`; it does not assess browser-download installation, the user's Open Anyway action or native workflows. Record those results separately. Exit 0 without `--require-distribution` continues to mean only that local package verification passed.

For a trusted download blocked as an unidentified developer or because Apple cannot check it, document Apple's per-app **System Settings → Privacy & Security → Open Anyway**, followed by the confirmation to open. This creates an exception for that app; it is not a global security change or a claim of Apple verification. Do not use it to bypass damaged-app or malware alerts. See [Apple's guidance](https://support.apple.com/en-us/102445). The readiness tool never changes Gatekeeper or quarantine.

## Prepare a 1.0 candidate

Keep the candidate on a review branch until rollback ownership, transport backpressure and transaction-recovery regression tests pass. A stable ad-hoc 1.0 release is permitted under `github-adhoc`; changing the version or label alone does not complete validation.

Freeze one implementation commit and run full Mac and Windows gates for it. Record the branch head and actual CI checkout revision separately when CI uses a PR merge ref. Verify the final ZIP and manifest, then use that same packaged binary for native acceptance: import, review, cancel, save/readback, partial failure, recovery inspection and verified archival. Include a fresh browser download, any per-app approval and an offline launch. Name the Mac and macOS version used; a development-machine check is not clean-machine or minimum-system coverage. Record remaining hardware/system checks explicitly; physical volumes/SMB need their own results when advertised as supported.

Under `github-adhoc`, a stable candidate needs verified ad-hoc package integrity, Worker launch checks, clear installation instructions and completed required native acceptance. Record Gatekeeper's original decision and the user's per-app approval separately; neither changes `signatureReady`. Stable publication uses the exact `macos-v1.0.0` tag and new immutable ZIP/SHA/manifest assets, with Pre-release cleared and Latest left unchanged. Keep the Windows Latest release and its assets unchanged.

## Publish a preview

1. Use a source commit with the full macOS profile (including signed ZIP and extracted-package tests) and Windows automated gate passing. Prefer the original verified CI ZIP; check its commit, clean source state, SHA and manifest.
   Docs/core results do not validate a release candidate. If the chosen commit has no full platform runs and CI package, manually dispatch the Mac workflow with `validation=full` and the Windows workflow for that commit before publishing. New `macos-v*` tags also request full validation; wait for those gates before publishing.
2. Create a `macos-v<version>-preview.<n>` draft at that exact commit, marked Pre-release and not Latest. Upload only the ZIP, matching `.sha256` and manifest.
3. Compare uploaded asset sizes/digests with local files, then publish. Keep Windows Latest and its assets unchanged.
4. Verify the published tag resolves to the tested commit, public download works and the release remains a Pre-release. Keep existing release assets immutable; publish a new preview for a different binary.

See [release checklist](RELEASE-CHECKLIST.md) and [testing](TESTING.md). Update current status instead of appending development diaries; Git commits retain the history.
