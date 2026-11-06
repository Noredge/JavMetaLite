# Documentation

Start with the platform guides for installation and builds. Maintained technical documents describe current behavior; dated evidence and release notes live in the archive.

## Using and building

| Platform | Guide | Tests | Release information |
| --- | --- | --- | --- |
| Windows | [Installation and build](../windows/README.md) | [Development and testing](windows/TESTING.md) | [Published releases](https://github.com/Noredge/JavMetaLite/releases/tag/v1.2.3) |
| macOS | [Installation and build](../macos/README.md) · [Bundled first-launch guide](macos/PACKAGE.md) | [Core/full and manual checks](macos/TESTING.md) | [Current verification limits](macos/STATUS.md) · [Published release](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.0.0) |

[Screenshots](SCREENSHOTS-v1.2.0.md) use synthetic data and state the version captured.

## Maintained technical documents

- [Repository layout](maintainer/REPOSITORY.md) · [Script inventory](../scripts/README.md) · [Maintenance workflow](maintainer/MAINTENANCE.zh-Hans.md)
- [Shared Core and imaging](../shared/README.md)
- macOS: [Architecture](macos/ARCHITECTURE.md) · [Worker protocol](macos/PROTOCOL.md) · [Protocol contract and fixtures](../contracts/worker-v1/README.md)
- Safety: [Requirements](macos/SAFETY-MATRIX.md) · [Cross-platform regression requirements](macos/WINDOWS-CASE-REGRESSION.md)
- macOS releases: [Procedure](macos/RELEASING.md) · [Checklist](macos/RELEASE-CHECKLIST.md)
- Licensing: [Project license](../LICENSE) · [Shared/Windows notices](../THIRD_PARTY_NOTICES.md) · [macOS notices](macos/THIRD_PARTY_NOTICES.md)

## Historical records

[Archive index](archive/README.md): Windows version notes and earlier performance measurements. These are evidence for their original versions, not current acceptance results. Current macOS verification limits stay in [STATUS](macos/STATUS.md); older development status is available in Git history.

Add ongoing requirements beside the relevant maintained guide. Put dated test reports and release records under `archive/`, link them from its index, and keep synthetic fixtures in `fixtures/`. Packaged license texts and first-launch instructions remain at the paths used by release tooling.
