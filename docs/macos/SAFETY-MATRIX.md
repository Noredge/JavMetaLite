# M4–M6 safety evidence matrix

All automated file operations use disposable synthetic media. “Passed” below means executable software evidence, not physical-device acceptance. Exact final counts/commands and native evidence are in STATUS.md.

M6 update: 2026-10-22. Candidate 1 and final candidate 2 native observations are distinguished below and in NATIVE-QA-M6.md. Final candidate 2 was locally launched, restored the same small window, reloaded saved NFO and refused a late NFO with corrected Japanese/Chinese guidance. Both are local arm64 ad-hoc candidates, not notarized distributions.

| Risk / behavior | Executed software evidence | Remaining boundary |
| --- | --- | --- |
| Stale review / preview / duplicate execution | Core revision tests; real Worker revision/queue/plan/idempotence tests | Cross-crash exactly-once execution is not claimed |
| Unknown NFO XML, actors, raw values | Core round-trip plus Swift/Worker actual file readback | Jellyfin server scan not run |
| External source / target edits | Full SHA stamps, late-source mutation and target-appearance tests | Physical concurrent storage faults not reproduced |
| Batch write/read and shared extrafanart conflicts | Shared Core conflict tests; Worker aggregate/skipped-input preflight | Worker 1000-item timings recorded; native UI scaling not measured |
| Import changing protected inputs during save | Real active-copy Worker test rejects import JOB_BUSY | None beyond tested process lifetime |
| Sequential partial failure / cancel | 512 MiB real Worker copy tests retain first completion and leave tail NotStarted | UI interaction during long physical-volume copy not observed |
| Cleanup / post-commit failure | Core cleanup and journal-warning tests preserve Completed and stop next item; Worker recovery barrier tests | Forced process/power loss at every phase not tested |
| Verified video copy / source retirement | Core + Worker full SHA checks, same-size/same-time external edit, multipart post-commit cancellation | External drives / unplug-remount / SMB not tested |
| Existing video and missing custom root | Core + Worker hard refusal tests | TCC revocation, disk full, device I/O failure not tested |
| Symlink / case alias / Unicode path | Synthetic symlink-swap/case-alias tests and Unicode custom roots | Native case-sensitive APFS volume, hard-link identity not validated |
| Local artwork preservation / selected output | Core actual image generation + byte-exact migration; Swift/Worker selection and outputs | Live remote-image availability not claimed |
| Artwork failure / resource limits | Invalid/oversized/EXIF/alpha/crop tests; stalled body deadline; incomplete replacement preserves originals | Windows decoder runtime equivalence pending |
| No-NFO organization retains unsaved review | Core full metadata/candidate baseline test; real Worker later-save tests | None beyond tested fixture coverage |
| Multipart / duplicates | Automatic CD grouping, duplicate-group isolation, explicit ordered parts; unique secondary sidecars preserved | Arbitrary a/b files require user confirmation |
| Multi-source partial failure / retry | Injected real R18/LibreDMM clients; rate-limit partial result and selected-source/manual retention | Live provider success not claimed |

The native UI delegates all file work to the Worker. Preview hashing and save operations are asynchronous and cancelable. Core plan creation still hashes sidecars synchronously on the Worker request path, so cancellation can wait for that short plan-construction segment; it does not run on the Mac UI thread. This is a known responsiveness limit for unusually large sidecars.

## M5 additions

| Risk / behavior | Executed evidence | Remaining boundary |
| --- | --- | --- |
| Untrusted browser content | Exact source/URL/port/size guards; captured identity/readiness; isolated WebKit capture; real Worker revision check before/after asynchronous import | Live verification and website changes not accepted by fixtures |
| Native image Trash | Registered ID + canonical nonlinked path/full content fingerprint; revision lease; validation under NSFileCoordinator; actual NSWorkspace.recycle in native tests with restoration | TCC/SMB/external device errors remain unverified |
| Shared local artwork | All affected jobs locked/refreshed; reviewed metadata and original NFO baseline retained; active read-only operations also block preparation | Hard-link identity not established |
| Canceling Trash | Repeated native sheet cancellation retains file bytes/revision; visible image loading resumes after lease release without changing review revision | Full VoiceOver speech acceptance pending |
| Remote image cancellation | Stalled local HTTP response/body, queued waiter cancellation, two-slot bound, 20-second limit; no review mutation | Online remote artwork availability unverified |
| Input composition and Undo | Native marked-text/active-editor tests, per-field/job Undo/Redo, completion publishes draft, focus traversal without phantom dirty state | Physical Chinese/Japanese IME acceptance pending |
| Original-folder native save options | Real Worker receives empty unused customRootDirectory before/after Trash; overwrite refusal then actual round-trip save; custom-root mode still rejects missing root | Native UI disk readback recorded separately in STATUS |

## M6 additions and corrections

| Risk / behavior | Executed evidence | Remaining boundary |
| --- | --- | --- |
| NFO appears after import or preview | Worker tests 37–38 reject late source NFO and an existing target NFO without a loaded round-trip baseline; explicit reimport then save retains unknown XML. Candidate 1 GUI refuses late DEMO-004/movie.nfo; final candidate 2 refuses late DEMO-005/movie.nfo and displays corrected Japanese/Chinese reimport guidance | Fresh path checks run before preview, whole-batch execution and each item transaction; they do not create an atomic snapshot against arbitrary external writers |
| Loaded NFO content changed externally | Existing baseline SHA-256 checks plus Worker test 39 reject changed content, including newly added unknown XML. The original reviewed NFO still supports explicit overwrite consent | Reimport is required to adopt the changed source; a new preview alone does not reload its XML baseline |
| Registered video path redirected | Worker test 40 covers source parent and video-file symlink replacements, with/without local NFO, and an unselected job's redirected parent; SOURCE_CHANGED preserves reviewed values, revision, source bytes and unrelated files | User-chosen aliases may be resolved on initial import; physical hard-link identity and arbitrary last-instant external replacement remain outside these tests |
| Fresh discovery versus scan performance | Core fresh-sidecar/link-alias tests and Worker 37–40 pass. Normal directory entries reuse canonical-parent spelling only within one lookup; directory contents remain freshly enumerated and import duplicate checks remain unchanged | No cross-operation filesystem cache; 1,000-file Worker timings do not establish native 1,000-row UI or sustained-memory acceptance |
| Native overwrite consent and round-trip | Candidate 1: blocked preview preserved all 13 original fixture file hashes; explicit consent plus fresh preview changed only DEMO-001/movie.nfo, preserved the other 12 files, and retained unknown XML, actor extension, resume and raw N/A value. Candidate 2 reimported and displayed the saved title | Candidate 2 readback does not repeat candidate 1's complete save interaction; no live Jellyfin scan |
| Native Undo/Redo and small window | Candidate 1 observed Cmd-Z / Shift-Cmd-Z, Window → Move & Resize → Left / Top Left, approximately 921×673 outer window / 920×620 content, scrollable metadata and gallery. Candidate 2 restored the same small window on launch | This is one display/host observation; physical IME, full VoiceOver, multi-display disconnect and other desktop geometries remain open |
| Completed result relocalization | Candidate 1 changed to Japanese/Dark and observed the completed import summary change language while preserving 0 newly imported / 3 existing counts | Does not establish professional Japanese/Traditional Chinese review or physical IME operation |
| Release identity and signing scope | 30 release command-contract tests include exact executable-name/containment refusal, identity/team/no-fallback policy, inside-out ordering and minimal entitlements; real ad-hoc signing/verification and bundled Worker launch also passed | No valid Developer ID identity on this host; fixture responses do not prove real Developer ID timestamps, JIT launch, Apple acceptance or trust |
| Ambiguous notarization upload and final archive | Contract tests cover timeout/no repeat upload, killed submitting state adoption with matching request ID/SHA, mandatory Apple log identity, and final ZIP extraction with manifest/archive/ticket/policy checks before complete | No actual submit, Apple Accepted response, stapling or successful Gatekeeper distribution assessment has occurred |
| Build and package provenance | Release manifest tests 15/15, exact source/dependency/toolchain inventory, real ad-hoc package extraction and no-SDK Worker checks; source-export rebuild evidence records success without .git | Rebuild provenance does not promise byte-identical signed binaries/ZIPs; local CI-shaped execution does not establish a remote GitHub CI run |

M6 automated evidence: Portable Core **41/41, 252 assertions**; real Worker **40/40**; Swift **20/20**; localization **309 keys × 4, 1,266 runtime checks**; release command contracts **30/30**; release manifest tests **15/15**. The four Windows projects cross-compiled with exit 0; Windows execution remains unverified. Candidate 2 package extraction ran the 40 Worker tests successfully. Exact logs are listed in NATIVE-QA-M6.md and STATUS.md.

Recovering a changed NFO baseline requires actual reload. In the current native workflow, first resolve or separately preserve unsaved edits, quit the App, relaunch and import again. Reimporting the same file within the current session is duplicate detection and does not refresh the old baseline; repeatedly generating a new preview cannot resolve SOURCE_CHANGED.

The first M6 package attempt failed test 35 because its fixture assumed Task.Run scheduling was FIFO: the third submitted operation was not necessarily the queued waiter. The test was corrected to wait until two HTTP requests occupy both slots, then submit and cancel the known waiter. Five focused reruns and both later complete package suites passed. The initial failure remains in `artifacts/test-results/macos-arm64/package-m6-initial.log`; it is not presented as an initial all-green run. This correction changed test synchronization, not production cancellation behavior.


## Live artwork and batch continuation (2026-10-22)

The metadata-only evidence above remains historical. This continuation actually ran Core 41/252, Worker **41**, Swift20, release contracts36 and manifest15; it does not relabel historical Windows runtime/localization evidence as freshly executed. Exact signed App identities and GUI-versus-final-Worker scope are in LIVE-ARTWORK-BATCH-QA.md.

| Risk / behavior | New executed evidence | Remaining boundary |
| --- | --- | --- |
| Live artwork selection / full replacement | Three real LibreDMM IDs with synthetic video: 3 fanart + 3 right-half posters + all 37 selected samples, no cap truncation. Six independent ImageIO numerical comparisons and 37 source JPEG SHA/order matches; final signed self-contained Worker actually redownloaded save inputs | No manual image appearance or Windows decoder equivalence acceptance; no R18 live artwork coverage |
| Primary cover HTTP404 | First run retained partial (42/43 references), no harness batch write. Same-candidate bounded preview fallback then fetched the 800×535 STARS cover. Regression41 covers actual parser/HTTP fallback, caller URL rejection, job isolation, terminal size/decode limits and unchanged revision/media | Three tested IDs do not prove catalog-wide or continuous availability |
| Review/plan and full-queue protection | Final bundle run03 rejects stale expectedReviewRevisions and old plan, refuses overwrite and an unselected registered job's inputs; all input hashes unchanged on refusal. Repeated execute returns same operation without writes | Not an atomic snapshot against arbitrary last-instant external writers |
| NFO / protected bytes / transaction | Final bundle three ordered Completed results, unknown XML/actor extension/resume/raw N/A retained, NFO references match output; 16 protected files unchanged, three completed journals, no cleanup/recovery warning or media temp. Native GUI separately saves same three IDs with before/after disk readback and actual screenshots | GUI ran the prior candidate; final Worker was separately tested. Physical-volume failures and cross-crash exactly-once not claimed |
| Reference reuse versus real download | Final run validates all 43 earlier reference files by registered primary URL/ID/role/path/SHA/dimensions; saves independently download 3 cover sources and37 sample sources | Final reference reuse is not represented as 43 new HTTP fetches |

Signature policy remains signed-only. Final local ad-hoc integrity passes, but zero Developer ID identities and a rejected Gatekeeper assessment mean distributionReady=false. No notarization submit/staple was performed.


## Windows 历史案例回归（2026-10-22）

新增真实历史案例对应的解析、文件身份、旁车归属、来源隔离、重试、取消/清理故障和原生队列检查，结果见 [40项矩阵](WINDOWS-CASE-REGRESSION.md) 与 [STATUS](STATUS.md)。独立不可发布 RegressionHost 必须不在最终包内；完整 test.sh 强制固定HTTP集成，ZIP解包门禁仅运行生产 Worker。发布时须保留 .NET 签名 xattrs（使用 ditto）；复制后重新验证，不用文件字节相同替代签名检查。签名政策仍为 signed-only；ad-hoc不是 Developer ID，公证不执行。

## build9 discovery and bounded history

Normal selection/drop uses a read-only discovery token before queue admission. Confirmation validates the registered membership and input identity twice, stages local loading, and admits the entire selection atomically. Cancellation, unknown IDs and changed inputs leave existing revisions and queue state intact (Worker54–61, real Swift discovery sheet integration). No directory rescan adds unselected files/CDs.

Completed operation and executed-plan receipts expire together; pending previews have separate count/DTO-byte/age budgets. Old plan IDs fail closed. The native explicit status recovery only refreshes an idle workspace snapshot, preserves drafts/checks/navigation, warns about missing receipts and never replays a mutation. Recovery journals are not part of retention deletion. Count/byte/active/idempotency tests ran; actual 15/30-minute waiting and whole-process resource bounds remain unverified.
