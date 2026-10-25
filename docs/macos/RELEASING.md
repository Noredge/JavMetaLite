# macOS release preparation

The first candidate targets Apple Silicon and macOS 15 or later. Only the exact host and executed gates recorded in STATUS.md are verified. Intel requires its own complete build and runtime acceptance. The Windows release workflow is retained.

The current user-selected policy is **signed-only**. Build/package scripts sign and verify the App; they do not submit to Apple, staple a ticket, or request notarization credentials. The current regression delivery is `outputs/Mac-regression-discovery-2026-10-22` (build9); earlier M6/build8 outputs remain historical evidence. See STATUS.md for the exact ZIP/input digests and executed gates. Live-provider results are recorded separately in STATUS.md and the current delivery evidence when executed.

## Reproduce a local candidate

Use the SDK pinned by global.json and the Swift/macOS SDK versions recorded in the build manifest. Scripts never install tools or change the global developer directory.

```sh
export DOTNET=/absolute/path/to/dotnet
./scripts/macos/test.sh --arch arm64 --configuration Release
./scripts/macos/crossbuild-windows.sh --configuration Release
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
```

The self-contained, untrimmed, non-AOT, multi-file Worker ships inside the signed helper. End users install no .NET runtime. NuGet restore uses committed, per-RID locks; build provenance records source hashes, toolchain, dependency/runtime versions and arguments. The external package manifest records actual payload bytes and ZIP hash. A reproducible recipe with verified inputs is distinct from a promise that signatures, timestamps and ZIPs are byte-identical across machines.

Local ad-hoc output is explicitly labeled. Signature verification demonstrates integrity of that local package; it does not demonstrate Developer ID trust, notarization, or a fresh-download Gatekeeper pass.

This host currently has **0 valid code-signing identities**. Ad-hoc local signing works without a certificate. Developer ID signing remains an independent, incomplete condition; omitting notarization does not supply a signing identity or make an ad-hoc App Developer ID signed.

## Developer ID signing

Developer ID signing requires a valid Developer ID Application certificate/private key already available in the build machine's Keychain. The command below is available when the owner has provisioned that identity; it is not a request to supply credentials in the current round. Do not put a certificate password, .p12, Apple password, private API key or authentication token in this repository or artifacts.

```sh
./scripts/macos/package.sh --arch arm64 --configuration Release \
  --sign-mode developer-id \
  --sign-identity 'Developer ID Application: Your Name (ABCDE12345)' \
  --team-id ABCDE12345
```

The signer requires exactly one matching valid Developer ID Application identity and validates the optional expected team before modifying signatures. It never falls back to ad-hoc. Nested native/data payloads are signed first, followed by the Worker apphost/helper and outer Swift App. Signing never uses `--deep`; verification does. Distribution signatures use hardened runtime and secure timestamps. The Worker alone receives `com.apple.security.cs.allow-jit`; the Swift host receives no JIT/debug entitlement. Developer ID signing does not disable library validation. All shipped native libraries are re-signed to the same team. Local ad-hoc .NET execution retains its separately documented library-validation exception.

Read-only plan for an existing app:

```sh
./scripts/macos/sign.sh /path/to/JavMetaLite.app --mode developer-id \
  --identity 'Developer ID Application: Your Name (ABCDE12345)' \
  --team-id ABCDE12345 --check-only
```

A successful command-contract fixture test does not prove real Developer ID signing. Actual identity availability, signing and JIT launch must be recorded separately. This host initially reported **0 valid identities**.

## Optional notarization: outside the current signed-only scope

The following workflow is retained for a separately authorized future notarized release. **Do not execute `--submit`, `--resume` finalization, or stapling, and do not request/configure notarization credentials for the current signed-only delivery.** Skipping these steps is intentional under the selected policy, not a successful notarization result.

For that optional future workflow, the owner would create a named Keychain profile using Apple's interactive `xcrun notarytool store-credentials` workflow. Authentication remains in Keychain. The project accepts a profile name and optional Keychain location, never a plaintext password or private-key argument.

Check an already Developer ID signed App without uploading:

```sh
./scripts/macos/notarize.sh /path/to/JavMetaLite.app --check-only
```

An ad-hoc App is rejected. Check-only does not verify credentials. Only if a future notarized release is explicitly authorized, use a new output directory to intentionally upload the App to Apple:

```sh
./scripts/macos/notarize.sh /path/to/JavMetaLite.app \
  --keychain-profile JavMetaLiteNotary --team-id ABCDE12345 \
  --output /path/to/new-notary-run --submit
```

The script stages a copy, leaving the input App unchanged, submits one ZIP, persists its SHA-256 and submission ID, and queries status once. Pending is reported as pending, not notarized success. Resume the existing request instead of submitting again:

```sh
./scripts/macos/notarize.sh --resume /path/to/new-notary-run
```

Only Apple's `Accepted` state permits stapling. The Apple log is retained and its request/archive identity checked; warnings are recorded for review. Stapling and validation target the staged `.app`, because ZIP itself cannot hold a stapled ticket. Gatekeeper assessment and code-signature verification must pass before a new distribution ZIP, manifest and SHA file are produced. ZIP and payload hashes are regenerated after stapling. No step uploads a GitHub Release.

A timeout or unparseable upload response is recorded as `submission-unknown`; the tool does not upload again automatically. Inspect Apple history using the same Keychain profile, then recover the matching request:

```sh
./scripts/macos/notarize.sh --resume /path/to/new-notary-run \
  --adopt-submission-id 12345678-1234-1234-1234-123456789abc
```

Adoption requires the Apple log's SHA-256 to match the exact submitted ZIP. Otherwise stop and inspect the saved evidence. A directory lock prevents concurrent finalization. Rejected, unknown, failed-staple and failed-policy states never produce a claimed notarized distribution.

## Read-only readiness and acceptance

```sh
python3 scripts/macos/release-readiness.py /path/to/JavMetaLite.app \
  --manifest /path/to/package.manifest.json --policy signed-only \
  --report /path/to/readiness.json
```

`signed-only` is the default policy. Without `--require-distribution`, exit 0 still means only that local package verification passed. Add `--require-distribution` to require the selected policy: under `signed-only`, this requires a verified Developer ID package. The independent `signatureReady`, `notarizationVerified` (valid stapled ticket), and `gatekeeperAccepted` fields retain their actual results. `distributionReady` expresses the selected policy; `notarizedDistributionReady` always retains the stricter Developer ID + stapled ticket + Gatekeeper conjunction. A signed-only success does not claim system trust or notarization.

For the previous strict policy, explicitly choose `--policy notarized --require-distribution`. This read-only check does not initiate notarization, request credentials, bypass security warnings, or remove quarantine. The report keeps command exit codes and `notarizationCredentialsVerified: false`. No private key is needed merely to verify an already signed artifact.

The current read-only run found a locally verified **ad-hoc** App and **0 valid identities**: `localSignatureVerified` is true; `signatureReady`, `distributionReady`, `notarizationVerified`, and `gatekeeperAccepted` are false. The six readiness-policy contract cases bring `test_release_tools.py` to **36 passing cases**; fixture results are not Developer ID or Apple-service acceptance.

Before public distribution, complete RELEASE-CHECKLIST.md. In particular, extract/install the actual distribution artifact and launch it through Finder, ideally on another clean Mac with no SDK; validate original NFO/video protection with synthetic media. Do not turn local policy assessment into a claim that a fresh browser download was accepted. Physical IME/VoiceOver, macOS 15, Intel, Windows and external-storage acceptance need their own evidence.

## Reference checks

Reviewed during M6 preparation:

- [Apple: notarizing macOS software](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution) — Developer ID, hardened runtime, secure timestamp and no debug entitlement.
- [Apple: custom notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow) — notarytool status/logs, stapling the App and rebuilding ZIP.
- [Apple: packaging Mac software](https://developer.apple.com/documentation/xcode/packaging-mac-software-for-distribution) — verify the actual distribution container.
- [Microsoft: publish .NET apps for macOS](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos) — non-AOT Worker requires allow-jit.

If official requirements change, verify the toolchain/policy again. Do not widen entitlements merely to make an unexplained failure disappear.
