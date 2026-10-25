# Synthetic macOS fixtures

All fixtures are invented test content, not personal media. `round-trip.nfo` contains
unknown XML, actor subnodes and intentionally nonstandard raw strings.
`libredmm.json` is an offline provider-response fixture.

`synthetic-cover-5x2.png` and `synthetic-poster-2x3.png` are tiny RGBA stripe images
created directly from PNG chunks. They test dimensions, alpha and exact sidecar
retention; the Core/Skia suite independently asserts right-half crop semantics.
The Python protocol suite copies these into disposable media directories.
