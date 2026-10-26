# Worker protocol v1

UTF-8 JSON-RPC 2.0, one JSON object per LF-terminated line; 8 MiB maximum frame, string IDs only. Unknown optional fields are ignored; unknown commands return -32601. Notifications never execute commands. stdout contains protocol only; stderr is drained independently. All methods except initialize require a successful protocolVersion=1 handshake. Incompatible reinitialization disables commands.

`initialize {protocolVersion:1,clientVersion?,locale?}` returns protocolVersion, workerVersion, sessionId, cacheRoot, capabilities and discoveryLimits. readLocal/searchLibreDmm/searchR18Dev/writeNfo/writeArtwork/organizeVideo/batchSave/confirmParts/manualBrowserImport/localArtworkTrash/discoverInputs are true. `discoveryLimits` contains `maximumTokens:4,maximumItems:10000,maximumResultBytes:6291456,expiresAfterSeconds:900`. These are compatible v1 additions; clients must check the capability before using discovery confirmation. `shutdown {}` waits for cancellation/transaction cleanup, then replies and exits. EOF has the same safe cancellation semantics without a response. No automatic save replay.

## Methods and result shapes

- `workspace.discoverInputs {paths:[absolutePath],recursive:true}` → `{operationId}`. Read-only discovery result: `discoveryId,expiresAt,items,diagnostics,warnings,ignoredFileCount,skippedDirectoryCount`; see Discovery confirmation below. No jobs or local metadata are loaded before confirmation.
- `workspace.importDiscovery {discoveryId,selectedItemIds:[itemId]}` → `{operationId}`. Consumes a server-issued discovery and loads only its selected, fixed file sets. The completed result adds `discoveryId` to the import result shape below. No directory rescan expands the selection.
- `workspace.discardDiscovery {discoveryId}` → `{discarded:true}`. Idempotently releases a discovery; unknown, expired or already consumed IDs may also be dismissed. This is not cancellation of an already accepted confirmation operation.
- `workspace.importInputs {paths:[absolutePath],recursive:true}` → `{operationId}` remains available for compatibility. Operation result: importedJobIds, duplicateJobIds, warnings, diagnostics[{path,message,kind}], ignoredFileCount, skippedDirectoryCount. Diagnostic kinds are AccessDenied, ReadFailed and PathUnavailable; legacy warnings remain available. This direct import is additive and can report partial success; it does not provide the discovery-confirmation atomic admission guarantee. CD parts group automatically; ambiguous a/b names do not. Duplicate inputs are skipped. Imports and saves are mutually excluded so the protected queue input set cannot change during a transaction.
- `workspace.getSnapshot {}` → `{protocolVersion:1,jobs:[Job],activeOperations:[Operation],recoveryWarnings:[recordPath],activeTrash?,retention}`. Retention fields are documented below.
- `jobs.get {jobId}` → Job.
- `jobs.applyEdits {jobId,expectedReviewRevision,edits:{title:"..."}}` → Job. Partial string fields only: id,title,originalTitle,releaseDate,runtimeMinutes,maker,director,label,actorsText,genresText,plot,rating,sourceUrl. Empty string clears; omitted leaves untouched. Actor structures are retained unless actorsText is edited. Series/ContentId are read-only under existing NFO output rules.
- `jobs.selectCandidate {jobId,expectedReviewRevision,field,source}` → Job. Field names use the metadata camelCase keys. Selection uses Core review logic.
- `jobs.search {jobId,sourceMode?}` / `jobs.retryFailedSources {jobId,sourceMode?}` → `{operationId}`. An ordered `jobIds` array can replace `jobId`. sourceMode is `libredmm` (default), `r18dev`, `all`, or `custom`; custom requires a validated per-field `sourceProfile` (exact keys and values: contracts/worker-v1/README.md). Unknown modes/profiles reject before mutation. Existing providers/coordinator run real requests. Results contain sourceMode, outcome(completed/partialFailure/canceled), jobResults and warnings. Rows include jobId, status and, when available, attempts, outcome, warnings or error. A completed row/operation may contain partial source failure; it is never a saved-file status. Retry targets recorded failed providers within the chosen mode. If any requested job has no recorded failed source, the entire retry request rejects with INVALID_PARAMS before mutation or network work; retry never becomes a new ordinary query. Fresh queries select the latest configured provider values and keep manual values as candidates; retries preserve reviewed manual fields and selected sources. Searches use at most two concurrent slots; waiting searches remain cancelable. No production fake provider.
- `jobs.remove {jobId}` → `{removed:true}`. Busy jobs rejected; queue removal also waits for an active confirmed import. Never deletes media.
- `artwork.getPreview {jobId,artworkId}` → `{cacheId,path,mimeType,width,height,byteLength,isLowResolution}`. Only job-registered sidecars or HTTP(S) provider artwork; no arbitrary caller paths. 20 MiB encoded / 40 MP decoded limits. Cache path lies under initialization cacheRoot and expires on clean Worker shutdown. Preview fetches, reads and cache writes share a 20-second cancellation deadline. Cache files are published using a temporary file and rename. With `async:true`, the method immediately returns `{operationId}` and the completed result has this same preview shape. Native clients use this cancellable form; it has two bounded image slots and an operation-linked deadline. Cancel with `operations.cancel` when a job/revision is no longer current. It does not mark the job busy or change its revision.
- `artwork.select {jobId,expectedReviewRevision,artworkId,selected:true}` → Job. Cover selection selects the registered source pair; sample selection toggles one image. Deselecting a cover is invalid. Every changed selection advances revision and invalidates previews. Local output locations must be sidecars discovered for this imported job.
- `artwork.selectSource {jobId,expectedReviewRevision,source}` → Job. Source must be `local` with registered local artwork/samples, or an exact registered cover/provider source name on this job; unknown sources reject with ARTWORK_NOT_FOUND before mutation. A source selection selects its cover pair when present and atomically replaces the selected sample set with that source's samples, clearing hidden selections from other sources. A registered sample-only source preserves the current cover. Changed review selections advance revision and invalidate save previews; no files are written. The native gallery's all-images filter is display-only and is not a source accepted by this method.
- `save.preview {jobId,expectedReviewRevision,options,async:true}` → `{operationId}`; its completed result is Preview. Batch form uses ordered unique `jobIds` and `expectedReviewRevisions:{jobId:revision}`. Native always uses asynchronous preview so full moving-video hashing can be canceled. Omitting `async` returns Preview directly for compatibility. See save options below.
- `save.execute {planId}` → `{operationId}`. Only a server-issued preview is accepted; resubmission returns the same operation ID while its executed plan and operation receipt remain retained. After eviction it fails closed with PLAN_STALE, without another write. Queue/revision changes invalidate an unexecuted plan. All selected jobs' file stamps are revalidated before the first write, then each transaction is revalidated immediately before execution. Shared conflict preflight protects every queued input, including unselected jobs. Saves run sequentially in requested order; a failure, cancel or cleanup warning stops later jobs. A committed earlier item stays completed.
- `operations.get {operationId}` → Operation. `operations.cancel {operationId}` → `{received:true,operationId,status}`. Acknowledgement does not mean termination.
- `preferences.get {}` / `preferences.update {includeIdInTitle:Bool}` → includeIdInTitle, overwriteExisting(false), searchSource, dataRoot,cacheRoot,logRoot,warning?,canOverwrite. Overwrite consent is deliberately per-preview. Existing AppPreferencesStore schema protection is honored.

Job: jobId, reviewRevision (Core MovieJob.ReviewRevision), metadata (every MovieMetadata field in camelCase, actors[{name,imageUrl}], screenshotUrls[]), videoPaths[], localNfoPath?, searchState, saveState, hasUnsavedChanges, localNfoSaveBlocked, busy,warnings[],candidates[{field,source,sourceDisplayName,value,selected}],canReplaceLocalExtrafanart,artwork[{artworkId,kind,source,selected,candidateSource?}],coverQuality,hasFailedSources,hasPartialSourceFailure,hasRateLimitedSource,searchAttempts[]. Search states remain NeedsId/Searchable/Searching/NeedsReview/SearchFailed/SearchCanceled; partial source success does not add a new search-state enum. Save states: Idle/Saving/Completed/SaveFailed/Conflict/SaveCanceled.

Each search attempt contains `source,displayName,success,elapsedMilliseconds,error?`; error contains `reason,message,retryAt?`. Reasons distinguish PROVIDER_RATE_LIMITED, PROVIDER_TIMEOUT, PROVIDER_NOT_FOUND, PROVIDER_HTTP_ERROR, CANCELED and PROVIDER_FAILED. retryAt is present for a known rate-limit deadline. Clients localize the reason and keep raw diagnostics separately.

`coverQuality` contains `state:unknown|ok|lowResolution` and optional width, height, source and reason. Dimensions describe the selected full cover, never a poster; low resolution uses the existing Core rule and reason `smallFullCover`. Local full covers are measured during import, before a row or gallery is viewed, without downloading remote artwork. A successful current fanart preview can update quality without changing reviewRevision; canceled/stale source or quality revisions cannot install a result. Post-save reload transfers a fresh measurement only when its local fanart path matches the currently selected local cover, using the current quality revision. It never attaches that local measurement to a retained online source. Unknown/corrupt/missing full covers remain unknown. Quality is advisory and does not block NFO saving.

Remote samples shared by providers have one registered artwork instance per source/URL pair, with the same selection state for the shared URL. Clients treat artworkId as opaque. Source-specific galleries therefore retain the shared candidate under each provider, while output URL/byte deduplication prevents duplicate writes.

Operation: operationId,jobId?,readOnly,status(running/completed/failed/canceled),sequence,phase,result?,error?. `readOnly:true` identifies asynchronous artwork previews and input discovery; confirmed imports and other operations are false. Clients must not treat these read-only operations as unknown saves when reconciling interrupted writes. Read-only here means no queue admission, review-revision change or media writes; artwork previews may still update advisory quality/generated cache, and discovery records token bookkeeping. Notification `operation.finished` params use Operation. `operation.progress` params contain operationId, jobId?, sequence, phase, canCancel and optional details (`jobId,currentIndex,totalJobs,completedBytes,totalBytes` for batch/copy progress). Sequences increase per operation. Query operation status after gaps/timeouts; never retry saves blindly. Notifications may precede the initiating response, so retain them by operationId. Operation history is bounded; absence of a receipt does not establish that an earlier write never happened.

Errors use `{code,message,data:{reason,retryable,requiresNewPreview,detail?}}`. Revision conflict detail may contain the current Job; clients must also handle an absent detail, including Trash revision conflicts. Defined reasons include REVISION_CONFLICT, PLAN_STALE, PATH_CONFLICT, SOURCE_CHANGED, ACCESS_DENIED, VOLUME_UNAVAILABLE, PROVIDER_RATE_LIMITED, RECOVERY_REQUIRED, WORKER_UNAVAILABLE, DISCOVERY_STALE, DISCOVERY_CAPACITY and OPERATION_NOT_FOUND plus protocol/validation reasons. The current generic retry flags are true only for REVISION_CONFLICT/PLAN_STALE/SOURCE_CHANGED, with requiresNewPreview true only for PLAN_STALE/SOURCE_CHANGED. Discovery clients must explicitly handle DISCOVERY_STALE by rescanning; it does not acquire a generic retry flag. Client localizes reason; diagnostics may contain paths and must not be uploaded automatically.

Recovery journal is written and fsynced under Application Support/recovery before media writes. Phase/staging paths are persisted synchronously. Unresolved records block further saves on restart; they are never replayed or auto-deleted. This is inspection/recovery evidence, not a cross-crash exactly-once guarantee. Stationary source videos use size/mtime; moving videos and all sidecars use full SHA-256 at preview. Every macOS move follows copy, full hash verification, commit, source revalidation, then source retirement. A source modified during copy is retained; unresolved committed/recovery paths block subsequent saves. Power-loss/real device removal remain separate tests.


Capacity is checked before importing jobs, applying edits, selecting candidates and installing provider results. The serialized `edits` object is limited to 1 MiB (`EDIT_TOO_LARGE`); the workspace metadata budget is 7 MiB (`WORKSPACE_CAPACITY`) beneath the 8 MiB frame limit. Rejection leaves submitted fields unchanged. Direct import can report per-item rejection; confirmed discovery admission checks the whole staged addition and rejects without adding any of it when capacity is exceeded. Oversized operation summaries preserve completion state and return `resultOmitted:true,requiresSnapshot:true`; fetch the workspace rather than replaying a save. Discovery results have their own smaller bound and fail rather than silently truncating the item list.

## Discovery confirmation (compatible v1 addition)

`workspace.discoverInputs` accepts 1–10,000 absolute input file/folder paths; `recursive` defaults to true. It runs Core discovery and existing ID parsing/grouping, without loading local NFOs, decoding local artwork, querying providers, adding jobs or changing existing review revisions/drafts. Explicit CD parts may form one item during discovery; a/b and same-ID files are not automatically merged. The result is:

```json
{
  "discoveryId": "opaque-session-token",
  "expiresAt": "2030-01-01T00:15:00+00:00",
  "items": [{
    "itemId": "opaque-item-id",
    "movieId": "DEMO-101",
    "primaryPath": "/chosen/DEMO-101.mp4",
    "videoPaths": ["/chosen/DEMO-101.mp4"],
    "alreadyImported": false,
    "canImport": true
  }],
  "warnings": [],
  "diagnostics": [],
  "ignoredFileCount": 0,
  "skippedDirectoryCount": 0
}
```

`movieId` is the filename/base-name result, not a metadata/provider lookup; it is an empty string when unrecognized. An unrecognized but verifiable item can still have `canImport:true` and be manually selected. Optional `existingJobId` identifies a queue duplicate. Optional `unavailableReason` is ALREADY_IMPORTED, SOURCE_UNAVAILABLE or IDENTITY_UNAVAILABLE. Null optional fields are omitted. Clients disable unavailable items; “select recognized” replaces the selected set with exactly the items whose canImport is true and movieId is nonempty. That action performs no network check and never borrows an ID from a parent directory.

There are at most four live discoveries per session. Each result contains at most 10,000 movie items and at most 6 MiB of serialized result JSON. Exceeding a bound returns DISCOVERY_CAPACITY; it does not silently omit items. The current count/byte rejection happens after Core discovery returns, so this is a retained-result/wire bound, not a guarantee of constant filesystem-scan memory. Tokens expire 900 seconds after result creation; expiresAt is an absolute UTC timestamp. Expired tokens are pruned on workspace snapshot, discovery, confirmation and discard requests. They are neither durable nor valid in another Worker session.

Confirmation accepts 1–10,000 distinct, nonempty item IDs belonging to that discovery. Empty, repeated, wrong-type or foreign IDs return INVALID_PARAMS before consuming a valid token. A selected item whose file stamps could not be recorded returns SOURCE_CHANGED before acceptance. Unknown, expired or previously consumed tokens return DISCOVERY_STALE. Once an operation is accepted, the token is consumed even if that operation later fails or is canceled; a new attempt requires discovery again, not replay of importDiscovery.

Only the selected registered file sets are loaded, in the caller's selectedItemIds order. Confirmation never reruns directory discovery to add newly appearing movies or CD siblings. Existing duplicates are reevaluated and reported in duplicateJobIds rather than replacing an existing job or its review. Local NFO/artwork loading uses the same loader as direct import, so ordinary local-read diagnostics can remain on an imported Job; they do not manufacture clean metadata or imply that every sidecar was readable. The confirmation result contains `discoveryId,importedJobIds,duplicateJobIds,warnings,diagnostics,ignoredFileCount,skippedDirectoryCount`. Its warnings/diagnostics/counts retain discovery diagnostics; job-specific local-load warnings are available through jobs.get/workspace.getSnapshot.

The Worker validates every selected canonical video path, macOS device/file identity, size, creation time and modified time before loading and again afterward. A registered path redirected through a file/ancestor symlink, replaced identity, missing input or changed recorded attributes causes SOURCE_CHANGED. Queue duplicate identity hints are rechecked rather than trusted across external replacement. This is an identity/attribute check, not full-video hashing: it cannot prove absence of in-place byte edits deliberately preserving inode, size and timestamps, or make an external filesystem race atomic. Destructive save protection and full moving-video SHA verification remain separate.

New entries are staged and the workspace capacity is checked before one queue admission. Cancellation or failure before admission disposes all staged jobs and leaves the existing queue/review/drafts unchanged. If admission has already committed, the operation reports completed; a later cancellation acknowledgement does not undo it. Progress phases are discovering for scan, then validatingDiscovery/loadingLocal for confirmation, with optional loading details `{loaded,total}`. Cancel the operation with operations.cancel and wait for its terminal status.

Only one discovery/confirmed import is active at a time; starting discovery or confirmation during an import or save returns JOB_BUSY, and Trash leases return TRASH_PENDING. While discovery is running, direct import and save execution are blocked. During confirmation, other imports, save execution and job removal are blocked. Existing draft edits are not overwritten by confirmation. A completed, unconsumed discovery is only a bounded result token, not a mutation lease: other queue actions may occur, and confirmation must revalidate. Discard is safe/idempotent but cannot cancel an already accepted import; clients must disable duplicate confirmation and use operation cancellation for in-flight work.

Consumption, discard or expiry releases the stored discovery and replaces its originating operation's large result with a small receipt: `{discoveryId,discoveryState:consumed|discarded|expired,requiresRediscovery:true}`. Operation status/sequence are not rewritten and no extra completion notification is emitted for this replacement. A later operations.get may therefore return this receipt instead of items, or OPERATION_NOT_FOUND after normal history eviction. Clients must not reconstruct item authorization from cached paths.

## Bounded operation and preview history

History is session-local and pruned opportunistically at operation start/completion, workspace snapshot, operations.get/cancel, preview creation and save execution. No pruning action writes media, replays an operation, deletes recovery journals or changes job review state. Limits are maximum retained-history targets, not a guaranteed minimum retry window; count/byte pressure can evict an older entry before its age limit.

| Retained state | Count | Age | Serialized payload budget |
| --- | --- | --- | --- |
| Terminal operations whose Task has actually finished | 128 | 1,800 seconds from completion | 33,554,432 bytes (32 MiB), including linked executed-preview DTO bytes |
| Unexecuted save previews | 64 | 900 seconds from creation | 16,777,216 bytes (16 MiB) |
| One save preview result | — | As above | At most 7 MiB; a larger preview returns WORKSPACE_CAPACITY before storage |

Running operations and terminal operations whose Task is still finishing are protected from eviction. Their records and linked executed plans are not counted as evictable completed history; aggregate counts/bytes can temporarily exceed the listed completed-history limits. Time-based history pruning uses a monotonic clock. The budgets measure serialized protocol DTO sizes, not process RSS, managed allocations, image-cache storage or total filesystem-scan memory. A bounded history report is not long-running UI/memory acceptance evidence.

Eligible operations are removed oldest-completion first when too old or over the count/byte budget. Their executed plan tokens are removed in the same step: an executed token cannot remain capable of returning an operation ID whose receipt was evicted. Unexecuted previews are considered stale-first and then oldest-creation-first under pressure; being stale does not itself promise immediate deletion. A cached preview DTO or an operation result that still mentions a planId does not prove that plan remains executable.

While a save's executed plan/receipt remain retained, repeating save.execute returns its existing operationId and performs no second transaction. After pruning, that plan returns PLAN_STALE without replay; operations.get/cancel for the removed ID returns OPERATION_NOT_FOUND. The latter is an absent history record, not evidence of failure, cancellation, rollback or no prior write. Refresh current workspace state, inspect preserved recovery evidence if relevant, and obtain a new user-reviewed preview for any further save; do not automatically resubmit a timed-out write.

`workspace.getSnapshot.retention` exposes:

```json
{
  "limits": {
    "completedOperations": 128,
    "pendingPreviews": 64,
    "operationReceiptBytes": 33554432,
    "pendingPreviewBytes": 16777216,
    "operationReceiptSeconds": 1800,
    "pendingPreviewSeconds": 900
  },
  "operations": 0,
  "completedOperations": 0,
  "protectedOperations": 0,
  "operationReceiptBytes": 0,
  "pendingPreviews": 0,
  "pendingPreviewBytes": 0,
  "executedPreviews": 0
}
```

`completedOperations` counts all eviction-eligible terminal records, including failed/canceled ones, not just status completed. `protectedOperations` includes running/still-finishing records. `operationReceiptBytes` sums only eligible receipts and their linked executed preview payloads; pendingPreviewBytes covers unexecuted plans. Discovery receipts participate in operation history, while the separate four-token/6 MiB-per-result discovery store has its own lifecycle above. Unknown IDs after restart fail closed; neither history pruning nor restart triggers automatic replay.

## Save options and previews

All options are explicit per preview; unknown keys reject. Defaults: `writeNfo:true`, `includeIdInTitle` from preferences; `downloadPoster`, `downloadFanart`, `downloadExtrafanart`, `replaceLocalExtrafanart`, `renameVideo`, `overwriteExisting` are false. `targetMode` is exactly `VideoDirectory`, `SourceNumberFolder`, or `CustomRootNumberFolder`; the last requires an existing absolute `customRootDirectory`. Missing roots are never silently recreated. Full SHA-256 copy verification cannot be disabled by the client. Existing videos are never overwritten; overwrite consent applies to the planned sidecar replacements only.

Preview: `planId,canExecute,jobs:[{jobId,reviewRevision,sourceJobIds,changes,warnings,blockingConflicts}],sourceJobIds,confirmedParts,warnings,blockingConflicts`. For one result, top-level `jobId,reviewRevision,changes` remain available. Changes contain Core operation names plus `source?,target,reason,conflict`; unchanged videos/secondary sidecars appear as KeepFile. Some preserved artwork migrations retain Core's KeepFile classification but show distinct source and target paths. Sidecar migration destinations are included in stamps, conflicts and completed outputs. Settings changes advance review revision.

`confirmedParts:true` requires an explicitly ordered list of at least two single-video jobs sharing normalized ID and source directory, with `renameVideo:true`. Only the first job's reviewed metadata/artwork is used. All participating revisions are frozen; unique secondary sidecars remain protected at their original locations. The ordered videos become CD1/CD2/etc. Secondary queue jobs are removed only after success; failure keeps the original jobs. Ordinary ambiguous a/b imports are never silently merged.

Save result: `outcome,savedJobIds,notStartedJobIds,failedJobId?,cleanupWarnings,cleanupIssues:[{path,message}],outputs,jobResults:[{jobId,sourceJobIds,status,outputs,error?}]`. cleanupWarnings retains the legacy list of affected paths; cleanupIssues adds the actual failure message for each path. `outcome` is `completed`, `partialFailure`, `canceled`, `cleanupRequired`, or `recoveryRequired`; row status is `Completed`, `Failed`, `Canceled`, or `NotStarted`. **Operation status completed means the report is available, not that every save succeeded.** Clients must inspect outcome and rows. A committed row remains Completed when subsequent cleanup fails, with cleanupRequired and unstarted later rows; the recovery journal retains cleanup paths/errors. Accepted preflight source changes are reported without any writes. Immediate stale/blocked/busy requests still return RPC errors. Unexpected engine failures may fail the operation itself.

Committed NFOs are reloaded to form the next round-trip baseline. With NFO output disabled, the baseline is refreshed independently: full metadata, actors' image URLs, selected source candidates and manual decisions stay intact, while migrated artwork paths update. Such unsaved metadata remains dirty. A post-commit reload/journal failure is a cleanup/recovery warning, never a false claim of rollback.


## Explicit browser import

- `browser.getTargets {jobId}` returns `targets:[{source,displayName,url}],maximumHtmlBytes:2097152`. It uses existing Core routes and does not perform network resolution.
- `browser.getSupport {source,url}` returns `supported,reason?`, without fetching anything. Only supported HTTP(S) detail URLs are eligible: exact provider host or www variant; no credentials, nondefault ports or unsupported schemes. JAVLibrary requires its video detail query.
- `browser.importPage {jobId,expectedReviewRevision,source,url,html}` returns `{operationId}`. Its result contains `job,promotedFieldCount`. HTML is nonempty and at most 2 MiB UTF-8, parsed as untrusted data. JAVLibrary imports captured HTML with an authoritative page ID; missing/challenge/search content is rejected. LibreDMM derives the page ID then uses its real client; R18.dev first requires the existing captured-page readiness/not-found checks, then uses the existing real detail-page import path. This retains Windows source-specific semantics.

The job ID/revision are checked before and after asynchronous work. Parsed metadata must have a nonempty matching movie ID and matching source. No fallback to the selected movie ID can disguise a missing page ID. MovieJob.ApplyManualWebSource retains prior manual candidates and the local XML round-trip baseline; this explicit import selects the imported source for its nonempty fields. It does not promise that prior manual selections remain selected. No disk save occurs; every later write needs a new preview. Errors include PAGE_UNSUPPORTED, PAGE_TOO_LARGE, PAGE_NOT_READY and PAGE_ID_MISMATCH.

The native WKWebView uses its own nonpersistent website store, with no native message handlers, no other-browser cookies and no shell/file bridge. HTML capture happens only on explicit import; the client binds the page capture to job/revision/navigation generation and checks its byte bound again. Live website verification is performed by the user; fixture tests do not prove site availability.

## Local image Trash lease

This is a separate native Trash operation, not an NFO transaction. Worker never permanently deletes a user image and never accepts an arbitrary path to trash.

1. `artwork.prepareTrash {jobId,expectedReviewRevision,artworkId}` returns `trashToken,paths:[canonical],reviewRevision,affectedJobIds`. Only a registered local image is eligible; the image must decode, and its full fingerprint is checked across decoding. It freezes file fingerprints and reserves all mutations/import/save while the native confirmation is open; affected jobs become busy and their previews expire. All existing operations, including read-only artwork previews, must finish first; the native app cancels and waits for its image requests before preparing Trash.
2. Show the actual returned path and ask for explicit native confirmation. On cancel, `artwork.abortTrash {trashToken}` verifies unchanged files and releases a prepared lease.
3. After confirmation, `artwork.validateTrash {trashToken}` rechecks hashes, paths and revisions and returns `trashToken,paths,authorized:true`. Only then may the native app call NSWorkspace.recycle. No permanent-delete fallback is allowed.
4. Wait for the native recycle callback, including failure, before `artwork.completeTrash {trashToken}`. It accepts no caller paths, reads actual disk outcomes, rediscovers artwork for all affected jobs, preserves edited metadata and surviving choices, increments revisions, releases the reservation, and returns `jobs,removedPaths,warnings`. A still-present file is reported as not moved. A changed/unverifiable file produces a warning, not false success. After validation/abort fails because an external file changed, completeTrash can refresh/release the prepared lease without recycling it.

workspace.getSnapshot exposes `activeTrash:{trashToken,phase,paths,affectedJobIds}` when active; phases are preparing/prepared/validating/authorized/refreshing. There is no authorization timeout that could unlock a save while native recycling still runs. An authorized lease cannot abort; completion must wait for the native callback. TRASH_PENDING blocks mutation, TRASH_PHASE rejects invalid transitions. This lease does not make native Trash movement atomic against uncoordinated third-party edits or power loss; those remain separate physical/race checks.

Artwork preview dimensions are measured from decoded images; fanart low-resolution advice reuses Core CoverResolutionCheck, without a new Mac threshold or review-revision mutation.


## Registered artwork preview fallback (compatible v1 addition)

`artwork.getPreview` continues to accept a registered `artworkId`, never a caller-selected URL. Remote fanart may try up to three addresses from that same cover candidate; poster tries its registered poster address first, then that candidate's other cover addresses. Local artwork and samples retain their own single source. HTTP/transport failure may advance the candidate; cancellation, 20 MiB encoded bound, invalid decoding and 40 MP limit are terminal. The existing 20-second total budget (including slot wait), two-request bound and read-only revision semantics remain.

Successful replies add optional `resolvedLocation`, `usedFallback`, and `attempts` (location/status/httpStatus/resolvedLocation). The resolved URL is the actual HTTP result, including redirects. `usedFallback` means another registered candidate address was used. Existing clients ignore optional fields. All-source failure reports bounded attempt diagnostics rather than falsely attributing fallback bytes to the primary URL.

`jobs.selectSource` atomically applies an available cached/local review source with expectedReviewRevision; exact semantics are in contracts/worker-v1/README.md. `reviewSources`/`reviewSource` describe the current picker. Artwork `contentKey` permits stable preview reuse independently of review revision, but never replaces save fingerprints, revision checks or transaction preflight. The native query menu offers LibreDMM, R18.dev and Custom Sources; legacy protocol mode `all` remains accepted.
