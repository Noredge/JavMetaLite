# M5 acceptance boundaries

M5 uses the M4 transaction engine and revisioned preview flow. The new browser is a metadata source, never a file-operation endpoint. Moving a local image to Trash is a separate explicitly confirmed action, not part of the NFO transaction and not covered by its rollback.

## Executable gates

- Browser routing rejects unsupported hosts/schemes/credentials/ports and oversized HTML; captured movie ID must match the bound job. Import checks revision before and after parsing/network work and preserves manual candidates/local round-trip state while explicit source promotion selects the imported nonempty fields.
- Actual WKWebView capture with synthetic HTML exercises the production bridge in a test-only target. Successful fixture capture/import does not demonstrate live website verification, cookies or online availability.
- Trash preparation only accepts a registered local artwork ID. A lease reserves file mutation; post-confirmation validation rechecks original content. Completion reads disk state, refreshes affected jobs/revisions and invalidates plans. No permanent-delete fallback.
- AppKit text tests exercise marked text, unsent drafts, undo isolation and stale model-update handling. These are composition API tests, not proof of a physical Chinese/Japanese IME session.
- Four locale resources have matching keys/placeholders, explicit language resolution and English fallback. Native screenshots provide visual evidence for languages actually shown; linguistic expert review is separate.
- Existing Core/Swift/protocol save gates remain mandatory. No metadata import or image selection bypasses save.preview/save.execute.

## Native observations at delivery

Use only generated movie/image/NFO fixtures. Record the exact delivered app, observed UI actions, disk readback, screenshots and failures. Distinguish actual Trash movement from protocol tests which emulate the native side. Record keyboard actions separately from AX set-value and composition API tests. Do not call an accessibility-tree inspection a full VoiceOver acceptance test.

Actual executed results are recorded in NATIVE-QA-M5.md and consolidated in STATUS.md. Minimum-width layout and full physical IME/VoiceOver acceptance remain open; implementation completion does not imply those acceptance gates passed. Performance reports clearly separate Worker timings, local native test timings and unmeasured UI/online/hardware behavior.
