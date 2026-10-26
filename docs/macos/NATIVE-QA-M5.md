# M5 native observations — 2026-10-21

Host: Apple M5 / 16 GiB, macOS 27.0.1. GUI operations were performed through native accessibility/keyboard automation against the actual app, with screenshots of generated nonplayable video/NFO/image fixtures. This is neither XCUI nor human VoiceOver/IME acceptance.

Final delivered ZIP SHA-256: `e2994536cebcde8ed3acfefc9cb350edfcd58452ffa961a516d72771ad2e9573` (candidate 3, 0.3.0-dev / build 3).

## Final delivered package

- Launched `outputs/M5/JavMetaLite.app`, imported three synthetic movies with Command-O and the native chooser. Existing NFO titles, raw rating and local artwork were read.
- Entered an unsaved title, opened the artwork tab, and invoked Trash on sample-002. Escape canceled the visible sheet; repeating the operation and pressing Return canceled again. All three thumbnails and their dimensions remained usable after both cancellations, including the draft-flush case that had failed in candidate 2.
- Explicitly confirmed sample-002 in the system Trash flow. The image disappeared from the refreshed gallery, metadata draft remained, and the app reported one recycled image. Earlier candidate 2 had explicitly recycled sample-001. Neither operation used permanent deletion.
- Command-S with original-folder defaults produced a real plan. Existing NFO replacement was blocked and Confirm Save disabled until explicit overwrite consent. Escape dismissed the blocked preview. Disk hashing at this point proved NFO/video bytes unchanged, with only the two explicitly recycled images absent.
- Enabled overwrite, generated a new preview, then pressed Return to confirm. UI reached Saved. Disk readback found `DEMO-001 · M5 Keyboard Test`, retained the unknown root attribute/element, actor extension, resume position, original title and raw `N/A` rating. Sixteen other original fixture files, including all videos and the two untouched jobs, remained byte-identical.
- Command-F filtered the queue to DEMO-002. Command-Option-I toggled the Inspector. The final toolbar AX tree exposes localized `Search Metadata` / `Batch · 0`, then `查询资料` / `批量 · 0` after switching language.
- Restored pre-test Simplified Chinese and light appearance; captured saved metadata. Command-Q exited cleanly. A process check found no delivered App or Worker process afterward.

## Earlier native candidate checks, retained as scoped evidence

Candidate 2 SHA-256: `768292d33e2932da2139e2024626be58eed5e9a2bdf70`.

- Actual Command-Z returned edited title and dirty state to baseline; Command-Shift-Z restored the draft; Tab moved to original title. Earlier candidate 1 also exercised queue Up/Down with draft isolation. Final automated text tests cover the same production editor.
- Viewer Left/Right changed the current image; 100% display and Escape closure worked.
- Native WKWebView opened ordinary `https://example.com`, displayed its domain and unsupported-source state, and kept import disabled. A `file:` address was refused. No live movie provider was queried in this GUI pass.
- English, Simplified Chinese, Traditional Chinese and Japanese settings switched in the real window. Japanese dark artwork/Inspector layout was observed. Screenshots 01–07 show candidate 2; 08–11 show final candidate 3. Completed historical status messages retain the language in which they were created; current controls update immediately.
- Escape and Return repeatedly canceled Trash; all 19 original fixture files were byte-identical before explicit recycling. Final candidate 3 repeats the cancel flow after the thumbnail fix.

## Corrections found by native checks

1. Candidate 1: repeated asynchronous `NSAlert.runModal` entered an invisible modal loop. No recycle had been authorized and every input hash was unchanged. The synthetic test App was terminated; its Worker exited on EOF. It now uses a parented asynchronous sheet with safe Return/Escape cancellation.
2. Viewer initial keyboard focus, toolbar menu AX names, stable local/manual source labels and browser failure presentation were corrected.
3. Candidate 2: draft flush restarted image requests while the Trash lease was held, leaving a busy error after cancellation. A separate display generation now reloads images after lease release without modifying reviewRevision.
4. Native options always carried an unused empty custom root; Worker parsed it even in original-folder mode. Validation now applies only to active custom-root mode. Regression 36 covers default options before/after Trash, overwrite refusal/consent and actual NFO round-trip save.

## Unverified

- Physical Chinese/Japanese input-method composition and full keyboard-only traversal of every workflow.
- VoiceOver speech/order/rotor behavior. AX names and native composition API tests are narrower evidence.
- Minimum-width layout: CUA edge/corner drag attempts did not change the observed window; no narrow-window acceptance is claimed. The displayed window and unchanged frame across relaunch were observed; changed-frame/multi-display restoration was not.
- Live provider import, verification/cookies, online remote images, 1,000-row native scrolling and sustained memory profiling.
- Windows runtime, Intel/minimum macOS 15, full Xcode/XCUI, external-volume/SMB/TCC/device-loss/power-loss behavior and signed-download Gatekeeper/notarization acceptance.
