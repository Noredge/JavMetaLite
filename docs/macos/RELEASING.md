# macOS release procedure

The stable macOS release is **1.0.0 (build50)**, tagged [`macos-v1.0.0`](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.0.0), for Apple Silicon and macOS 15+. Windows **v1.2.3** keeps its own assets and GitHub Latest status. See [verified scope](STATUS.md) and the [release checklist](RELEASE-CHECKLIST.md).

## Freeze and validate

1. Freeze the intended source revision and version. Run the **full** Mac and Windows gates; docs/core checks are insufficient for a release. Record branch HEAD and actual CI checkout separately when a pull request uses a merge ref.
2. Build from a clean checkout of the revision to be tagged. Do not treat a package built from a different merge revision as the same artifact. The embedded source manifest, external payload manifest and ZIP SHA-256 must agree with the chosen source and archive.
3. Test the final extracted package and record native import, review, cancel, save/readback and recovery workflows using disposable media. Name the actual machine, OS, package hash and any isolated QA wrapper. Record first-launch approval and offline scope separately; do not substitute a developer-machine check for clean-machine coverage.
4. Preserve logs and concise evidence with the release. Keep untested hardware, OS, accessibility and source-site combinations explicit in [STATUS](STATUS.md); do not append development diaries.

Use the SDK pinned by [global.json](../../global.json). From the repository root:

```sh
./scripts/macos/test.sh --profile full --arch arm64 --configuration Release
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
./scripts/macos/verify-package.sh /path/to/JavMetaLite.app \
  --manifest /path/package.zip.manifest.json --archive /path/package.zip \
  --expected-sign-mode adhoc
python3 scripts/macos/release-readiness.py /path/to/JavMetaLite.app \
  --manifest /path/package.zip.manifest.json --policy github-adhoc --require-distribution \
  --report /path/readiness.json
```

The self-contained Worker needs no user-installed .NET. Packaging uses locked per-RID dependencies, signs the complete payload, extracts the final ZIP with `ditto` and verifies the extracted Worker without an SDK environment. It excludes the regression host. Signed ZIPs are not promised byte-identical across machines, toolchains or dates.

## Distribution policies

The default **`github-adhoc`** policy permits stable GitHub releases with local ad-hoc signing and per-app user approval. Developer ID and notarization are not required by this project policy. This does not establish Apple publisher verification, malware scanning or automatic Gatekeeper acceptance.

| Policy | Automated requirements |
| --- | --- |
| `github-adhoc` (default) | Verified local package and a valid local ad-hoc or Developer ID signature |
| `signed-only` | Verified package with Developer ID signing |
| `notarized` | All `signed-only` requirements, a valid stapled ticket and accepted Gatekeeper assessment |

`distributionReady` reports only the selected automated policy. `localPackageVerified` and `localSignatureVerified` report integrity; `signatureReady` still requires Developer ID. `notarizationVerified` and `gatekeeperAccepted` are independent observations. A valid ad-hoc package may pass the default policy with all three trust fields false. The tool always reports `manualAcceptanceVerified: false`; native/installation evidence is separate. Without `--require-distribution`, exit 0 means local package verification only. The tool does not change Gatekeeper or quarantine.

Every release must identify the signing mode and link [first-launch instructions](../../macos/README.md#first-launch). [PACKAGE.md](PACKAGE.md) is copied into `Contents/Resources/READ-ME.md`. For a trusted app blocked as an unidentified developer or because Apple cannot check it, follow [Apple's per-app Open Anyway procedure](https://support.apple.com/en-us/102445). Do not apply it to damaged-app or malware alerts.

When a stricter policy is deliberately selected, use an existing matching Developer ID Application certificate/private key:

```sh
./scripts/macos/package.sh --arch arm64 --configuration Release \
  --sign-mode developer-id \
  --sign-identity 'Developer ID Application: Your Name (ABCDE12345)' \
  --team-id ABCDE12345
```

Missing or mismatched identities fail without fallback. Signing proceeds inside-out, never with `--deep`; strict/deep verification checks consistent teams, hardened runtime and secure timestamps, with JIT entitlement on the Worker only. Keep credentials out of source, logs and artifacts. Ad-hoc releases do not require Apple submission or notarization credentials.

## Publish the verified assets

1. Confirm the intended commit is merged and tag it with `macos-v<version>`; 1.0 uses exactly `macos-v1.0.0`. Mac release tags force full validation in both workflows. Wait for both final checks to pass at the tag before publishing. If needed, manually dispatch the full workflows for the exact revision.
2. Create a draft GitHub Release at that tag. For a stable release, clear Pre-release and explicitly leave **Latest unchanged**. Keep Windows v1.2.3 and its assets untouched. Previews use `macos-v<version>-preview.<n>` and remain marked Pre-release.
3. Upload the verified ZIP, matching `.sha256` and `.manifest.json`. Include version/build, minimum OS/architecture, source revision, signing mode, installation instructions, evidence links and known limitations in the release notes.
4. Compare uploaded asset sizes and server digests with the local files, then publish. Verify the public tag target, download, checksums, stable/pre-release flag and unchanged Windows Latest.
5. Keep published assets immutable. A different binary requires a new version/tag; do not overwrite a released ZIP or reuse its validation record.
