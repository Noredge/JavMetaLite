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

## Signing policy

The policy is **sign only, no notarization**. Do not submit to Apple, staple tickets or request notarization credentials as part of this workflow.

Ad-hoc signatures verify local integrity; they do not establish Developer ID trust or Gatekeeper acceptance. Publish such a build only as a clearly labeled preview, with those limitations in its notes. A Developer ID build requires an existing matching Application certificate/private key in Keychain:

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
  --manifest /path/package.zip.manifest.json --policy signed-only \
  --report /path/readiness.json
```

Exit 0 without `--require-distribution` means local package verification passed. `signatureReady` and `distributionReady` require verified Developer ID signing; ad-hoc previews do not satisfy them. Notarization, Gatekeeper and browser-download/manual acceptance remain separate results. Do not disable security features or remove quarantine to claim a pass.

## Publish a preview

1. Use a source commit with both platform CI gates passing. Prefer the original verified CI ZIP; check its commit, clean source state, SHA and manifest.
   Documentation-only checks do not build or validate a new release candidate. If the chosen commit has no complete platform runs and CI package, manually dispatch both full workflows for that commit before publishing.
2. Create a `macos-v<version>-preview.<n>` draft at that exact commit, marked Pre-release and not Latest. Upload only the ZIP, matching `.sha256` and manifest.
3. Compare uploaded asset sizes/digests with local files, then publish. Keep Windows Latest and its assets unchanged.
4. Verify the published tag resolves to the tested commit, public download works and the release remains a Pre-release. Keep existing release assets immutable; publish a new preview for a different binary.

See [release checklist](RELEASE-CHECKLIST.md) and [testing](TESTING.md). Update current status instead of appending development diaries; Git commits retain the history.
