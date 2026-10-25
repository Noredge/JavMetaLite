# Running this macOS package

Extract the complete ZIP, then open JavMetaLite.app in Finder. Keep its internal
Contents folder intact. The app includes its own .NET runtime and does not require
an installed .NET SDK, Homebrew, or a developer PATH. The current minimum deployment
target is macOS 15; actual tested systems are listed in the accompanying status.

This build is architecture-specific. An arm64 build is for Apple Silicon; an x64
build requires separate Intel execution evidence. A successful cross-build alone
does not verify another CPU or macOS version.

An ad-hoc package is a local development build. It has not established Developer ID,
notarization or Gatekeeper acceptance. Do not disable macOS security features to
run it. A Developer ID signature alone also does not prove notarization: retain the
separate notarization, staple and Gatekeeper results when distributing a release.

The Worker is launched from Contents/Helpers/JavMetaLiteWorker.bundle. App data,
diagnostics and recovery journals belong to the normal user Library locations,
not the app folder. Save previews must be reviewed before writing metadata or
organizing media. Moving images to macOS Trash uses its own confirmation.

BUILD-MANIFEST.json records the exact source-input hashes, pinned toolchain,
resolved dependency hashes and requested build/signing mode. The external ZIP
manifest records the signed payload and archive hashes. These are reproducibility
records, not a promise of byte-identical signed ZIPs across machines or dates.
The archive SHA-256 detects changes when compared with a separately trusted copy;
it is not a substitute for validating the code-signing identity.

The project license and full macOS dependency licenses/notices are included here.
Upstream website metadata is not bundled as product content or covered by those
licenses. No updater, telemetry upload, or automatic GitHub Release publishing is
enabled by these packaging scripts.
