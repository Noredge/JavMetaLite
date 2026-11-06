# Historical records

These records describe the versions and synthetic test conditions named in each file. They are retained for comparison and release history, not as the current build or release procedure.

## Windows release notes

- [1.2.3](releases/windows/RELEASE-NOTES-v1.2.3.md)
- [1.2.2](releases/windows/RELEASE-NOTES-v1.2.2.md)
- [1.2.1](releases/windows/RELEASE-NOTES-v1.2.1.md)
- [1.2.0](releases/windows/RELEASE-NOTES-v1.2.0.md)

## Windows performance evidence

| Snapshot | Preserved measurements |
| --- | --- |
| preview47 | [Before](performance/preview47/before.json), [after](performance/preview47/after.json), [search before](performance/preview47/search-before.json), [search after](performance/preview47/search-after.json) |
| preview49 | [Hotspots before](performance/preview49/hotspots-before.json), [after](performance/preview49/hotspots-after.json), [search before](performance/preview49/search-before.json), [after](performance/preview49/search-after.json) |
| preview50 | [Continuous operations](performance/preview50/continuous.json) |
| preview52 | [Sequential](performance/preview52/sequential.json), [zero-delay sequential](performance/preview52/sequential-zero-delay.json), [three downloads](performance/preview52/three-downloads.json), [zero-delay three downloads](performance/preview52/three-zero-delay.json) |

JSON files are preserved byte-for-byte, including original test labels and paths. Reproduce new measurements with the [optional scripts](../../scripts/README.md#optional-measurements-and-manual-checks); record version, data and cache conditions separately. Earlier macOS development reports remain in Git history; untracked local reports are not published by this cleanup.

[Current documentation](../README.md) · [Changelog](../../CHANGELOG.md)
