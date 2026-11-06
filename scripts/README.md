# Script inventory

Run public entry points from the repository root. Paths and CLI options are retained so CI and release commands remain stable. This inventory distinguishes automated callers, internal dependencies and intentional manual tools; a script outside CI is not necessarily obsolete.

[Windows commands](../docs/windows/TESTING.md) · [macOS commands](../docs/macos/TESTING.md) · [Release procedure](../docs/macos/RELEASING.md)

## CI and release entry points

| File | Caller or purpose |
| --- | --- |
| [Test-Automated.ps1](Test-Automated.ps1) | Windows CI → Core smoke, file transactions, WPF UI and package-definition gates. |
| [New-ReleasePackage.ps1](New-ReleasePackage.ps1) | Windows CI and release guide → self-contained portable EXE, ZIP and checksums. |
| [Test-PortablePackage.ps1](Test-PortablePackage.ps1) | Windows CI → verify the actual portable ZIP, identity and notices. |
| [Test-ReleasePackage.ps1](Test-ReleasePackage.ps1) | Called by `Test-Automated.ps1`; negative tests for release/package rules. |
| [ci/scope.py](ci/scope.py) | Called by `.github/workflows/ci-scope.yml`; select docs/core/full conservatively. |
| [ci/check_docs.py](ci/check_docs.py) | Called by `ci-scope.yml`; validate tracked Markdown links. |
| [ci/test_ci_tools.py](ci/test_ci_tools.py) | Called by `ci-scope.yml` and macOS `test.sh`; scope, link and suite-runner regressions. |
| [macos/test.sh](macos/test.sh) | macOS CI → localization, portable, Swift, protocol, queue, retention and cache tests. |
| [macos/build.sh](macos/build.sh) | Called by `package.sh`; locked Worker restore, Swift build, bundle, sign and verify. |
| [macos/package.sh](macos/package.sh) | macOS CI and release guide → signed ZIP, extracted-payload checks and checksums. |
| [macos/verify-package.sh](macos/verify-package.sh) | Called by build/package/notarization tools and used for independent verification. |
| [macos/sign.sh](macos/sign.sh) | Called by `build.sh`; signing entry point. |
| [macos/notarize.sh](macos/notarize.sh) | Documented optional Developer ID release flow; not used by ad-hoc CI. |
| [macos/release-readiness.py](macos/release-readiness.py) | Release guide and release-tool tests; explicit signing/Gatekeeper release preflight. |
| [macos/source-archive.py](macos/source-archive.py) | Source export entry point, covered by manifest tests; preserves source hashes and executable modes. |
| [macos/crossbuild-windows.sh](macos/crossbuild-windows.sh) | Documented macOS compile-only Windows check; does not replace Windows runtime CI. |

## Internal helpers and regression suites

| File | Caller or purpose |
| --- | --- |
| [ReleasePackage.Common.ps1](ReleasePackage.Common.ps1) | Imported by Windows release, portable validation and package tests; version/EXE identity rules. |
| [ReleaseNotices.Common.ps1](ReleaseNotices.Common.ps1) | Imported by Windows release/validation tests; dependency and license coverage. |
| [macos/common.sh](macos/common.sh) | Sourced by native shell entry points; arguments, toolchain and shared output paths. |
| [macos/LocalizationChecks.swift](macos/LocalizationChecks.swift) | Compiled/executed by `test-localization.py`. |
| [macos/test-localization.py](macos/test-localization.py) | Called by `test.sh`; catalog/resources/translated diagnostic validation. |
| [macos/test_xcode_sources.py](macos/test_xcode_sources.py) | Called by `test.sh`; SwiftPM/Xcode source registration and compile checks. |
| [macos/release_manifest.py](macos/release_manifest.py) | Imported/called by source export, build, package, signing verification and release tools. |
| [macos/sign_bundle.py](macos/sign_bundle.py) | Called by `sign.sh`; signing implementation and identity preflight. |
| [macos/notarize_release.py](macos/notarize_release.py) | Called by `notarize.sh`; resumable notarization and stapling implementation. |
| [macos/verify_package.py](macos/verify_package.py) | Called by `verify-package.sh`; bundle, licenses, manifest and Worker checks. |
| [macos/test_release_tools.py](macos/test_release_tools.py) | Called by full `test.sh`; failure-oriented signing/notarization/readiness tests. |
| [macos/test_release_manifest.py](macos/test_release_manifest.py) | Called by full `test.sh`; provenance, tamper detection and source-export regressions. |
| [macos/test_lifecycle_measurement.py](macos/test_lifecycle_measurement.py) | Called by full `test.sh`; lifecycle-driver boundary tests. |
| [macos/run_worker_suites.py](macos/run_worker_suites.py) | Called by test/package scripts; bounded suite execution with isolated fixtures. |
| [macos/test_worker.py](macos/test_worker.py) | Loaded by suite runner and related tests; protocol/controlled-provider harness. |
| [macos/test_worker_retention.py](macos/test_worker_retention.py) | Called by suite runner; retention limits and cleanup safety. |
| [macos/test_queue_interactions.py](macos/test_queue_interactions.py) | Called by suite runner; batch selection/queue atomicity and pressure cases. |
| [macos/test_worker_artwork_cache.py](macos/test_worker_artwork_cache.py) | Development suite runner and package verification; artwork cache safety. |
| [macos/test_diagnostics.py](macos/test_diagnostics.py) | Imported by Worker test suites; timing and HTTP shutdown diagnostics. |

## Optional measurements and manual checks

| File | Caller or purpose |
| --- | --- |
| [Measure-Performance.ps1](Measure-Performance.ps1) | Windows testing guide; synthetic import/search benchmarks. Not part of default CI. |
| [Test-SavePerformance.ps1](Test-SavePerformance.ps1) | Windows testing guide; save/download timing. Not part of default CI. |
| [Test-ContinuousStability.ps1](Test-ContinuousStability.ps1) | Windows testing guide; repeated operations in one WPF window. Not part of default CI. |
| [macos/measure-discovery.py](macos/measure-discovery.py) | macOS testing guide; synthetic discovery/import measurements with explicit output paths. |
| [macos/measure-worker-lifecycle.py](macos/measure-worker-lifecycle.py) | macOS testing guide; shipping-Worker lifecycle measurements. Driver tests are in full CI. |
| [macos/measure-worker.py](macos/measure-worker.py) | macOS testing guide; synthetic Worker performance measurements. |
| [macos/live-provider-check.py](macos/live-provider-check.py) | macOS testing guide; explicit `--run-live` provider checks, outside default CI. |
| [macos/live-artwork-batch-check.py](macos/live-artwork-batch-check.py) | macOS testing guide; explicit `--run-live` artwork/batch checks, outside default CI. |
| [macos/compare-image-output.swift](macos/compare-image-output.swift) | Standalone numerical image acceptance aid, outside default CI; compares a reference image to poster/fanart output. |

## Signing configuration

| File | Caller or purpose |
| --- | --- |
| [macos/worker-adhoc.entitlements](macos/worker-adhoc.entitlements) | Read by `sign_bundle.py` for ad-hoc Worker signing. |
| [macos/worker-release.entitlements](macos/worker-release.entitlements) | Read by `sign_bundle.py` for Developer ID Worker signing. |

The standalone image comparison tool has no automated caller but remains a reusable manual check. Example (synthetic images):

```sh
swift scripts/macos/compare-image-output.swift --source reference.jpg --output poster.jpg --mode poster
```

Measurement/live tools accept explicit inputs; inspect their `--help` before running. They may access real providers when opted in and are not release-pass evidence by themselves. The synthetic UI demo generator remains in [fixtures/macos](../fixtures/macos/README.md).

## Removal decision

All 46 existing files under `scripts/` are retained. No script is confirmed obsolete: build/release helpers have callers, optional measurements and live checks have documented uses, and the image comparison tool supplies a distinct manual check. No script has been moved merely to reduce the top-level file count, avoiding wrapper duplication and changes to release entry points.
