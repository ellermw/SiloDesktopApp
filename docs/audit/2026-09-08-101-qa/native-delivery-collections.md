# Native playback, delivery, and collections audit

Baseline: installed 1.1.101-multigpu-preview.1, source matched by its PDB. Findings are source-established unless explicitly labeled observed or historical. This audit changes no application code.

## Decision

Keep C#, native Windows UI, libmpv, hardware decoding, native subtitle rendering, the direct-play-first product goal, protocol-v3 server planning, explicit timeline conversion, strict byte-range validation and the existing small tested policies. Keep the separate native video window: removing WinUI composition from the video presentation path addresses a real architectural constraint. Do not replace the player with a browser video element, introduce a separate local database/server merely for architecture fashion, or force transcoding to mask transport faults.

Replace the lifetime/command coordination around those foundations. One playback session should own its cancellation, manager plan, mpv load epoch, HTTP transport, realtime connection, subtitles, heartbeat and shutdown. Prepare candidate resources independently, then commit only if their session/epoch is still current. Native window presentation and transport mechanisms can survive this change. The present design spreads authority across several semaphores, mutable fields and booleans; that makes overlapping operations harder to reason about than their individual methods suggest.

## Native playback findings

### N01 — Playback mutations do not share one owner (high impact)

`src/SiloPlayer/Services/PlayerService.cs:4114` audio changes, `:5466` quality changes, version changes, seek restarts, recovery and `:6928` plan invalidation use different gates and flags. Protocol-v3 preparation at `:3140` can call `PrepareDirectStreamForMpv(:3388)` or `PrepareHlsStreamForMpv(:3405)` and dispose the globally active proxy before the caller's final session check. Core's replan gate does not exclude start/stop (B01). A delayed replan/preparation can therefore overwrite or dispose resources belonging to a newer session. A final check after destructive preparation does not establish ownership.

Replace this coordination with a session command owner and owned candidate transports. Test replan versus close/new title, quality versus audio, rapid seeks versus invalidation, and stale mpv callbacks. This is a substantive redesign recommendation, not a request to divide a large file into arbitrary smaller files.

### N02 — Failure after accepted v3 replan has incomplete recovery

Audio and quality v3 paths accept the server's new plan, then prepare/reload locally. Their catch paths mostly reset flags/report the error; older paths explicitly track whether the server already changed transport. Local setup failure after an accepted v3 replan lacks explicit rollback/recovery. If the server retired the old transport, preserving its display does not restore it. This finding concerns synchronous preparation/setup failures before a successful native load is established: successful BeginMpvLoad already arms a watchdog, and native error and later stall handling can recover some cases. A setup exception can cancel the watchdog after pausing playback, which ordinary unpaused-stall recovery does not repair. Model prepare/commit/failure states and recover or enter the existing explicit retry/exit terminal state. Do not describe every native load failure as unrecovered. See the independent challenge in [native-validation-notes.md](D:/SiloPlayer/docs/audit/2026-09-08-101-qa/native-validation-notes.md).

### N03 — Audiobook premature EOF can advance or mark the whole book complete

`PlayerService.cs:3590` handles the audiobook branch before the video's media-end validation. A premature `end-file` or `eof-reached` signal can switch to the next file; on the last part it assigns total-book duration and forces a progress report. The mpv wrapper correctly distinguishes END_FILE reasons, but an EOF caused by an upstream truncation is still not proof that a book ended. Use per-part end tolerance and recovery, then compute aggregate book completion only after a genuine final-part end. Keep multi-part timeline and sticky speed features.

### N04 — Sleep-timer and disposal lifetimes are incomplete

The audiobook sleep timer is cleared on Close (`:6161`) but not at the beginning of unrelated new-content playback or PlayerService.Dispose. A timer from an audiobook can survive a content transition. Cache immutable chapter timelines per book rather than repeatedly constructing them in accessors. `CloseAsync(:6087)` sets `_closing` before awaits but lacks a surrounding try/finally; an exceptional drain can strand cleanup and `_closing`. It cancels the playback lifetime late and does not await every independent mutation path. Native disposal also joins threads synchronously before deferred fallback; keep it off the UI path and establish a strict no-more-native-calls boundary before handle destruction. No native crash was reproduced by this audit.

### N05 — Subtitle command and state boundaries need improvement

On-demand subtitle loading is the correct approach; retain it. However the active remux/HLS path loads a 600-second embedded-text sidecar window at `:4706`, while `TickSubtitleWindows(:4770)` has only one caller: the **inactive** PlayerOverlay UI timer. MainWindow always deactivates that overlay. The intended window advance therefore never runs in normal native playback; after exhausting the selected window, the sidecar can have no relevant cues. HLS local seeks outside it also lack maintenance; transport-restart seeks may reinitialize subtitle state. Direct embedded subtitles, full PGS sidecars, external/downloaded subtitles and burn-in are outside this finding. Move window maintenance into the active session lifetime and test uninterrupted playback past the window plus a local out-of-window seek. This is source-established missing wiring, not an observed subtitle session in this audit.

`MpvPlayer.AddSubtitle` also ignores the command success result; a subsequent sid query can associate an existing track with a failed new load. Make load completion explicit and bind the returned native track ID only after success. The active selection path invokes synchronous native commands while holding a state lock. Avoid holding the UI or state lock during potentially slow native commands. mpv documents asynchronous command completion support in its [command interface](https://github.com/mpv-player/mpv/blob/master/DOCS/man/input.rst).

`AppendPositionDuration(:4758)` formats a decimal query position using current culture; a decimal-comma locale produces a different wire value. Use invariant formatting, as the other seek paths already do. `SendMarkersToOsc(:5109)` reads the watch-level markers while other UI paths use active-version markers; realtime marker application also needs to update OSC. Treat subtitle selection, burn-in replan and preference persistence as a session-scoped operation so a late completion cannot persist against a replacement title.

### N06 — Slow audiobook playback can falsely trigger stall recovery

Core B10 is reachable through the one-second timer at PlayerService:1926. The detector compares each sample with the immediately preceding sample and requires strictly more than 0.5 seconds. Regular 0.5x playback advances exactly 0.5 seconds per sample and can be classified stalled after 30 seconds. Timer jitter may interrupt that pattern, so this is not a claim it fails on every run. Accumulate progress relative to the last significant advance, and test supported slow speeds with realistic sampling.

### N07 — Custom HLS transport needs explicit resource budgets

HlsProxy correctly handles playlist-relative URLs, URI attributes, nested playlists, range streaming and cancellation, and uses pooled buffers rather than buffering entire media files. Retain those mechanisms. Its custom TCP HTTP parser accepts unbounded connection tasks, reads an entire line before checking length, and relies on socket timeout properties that do not establish all asynchronous read deadlines. Playlist text is buffered without an explicit size cap. Add bounded connection concurrency, pre-allocation header/line limits, body/playlist budgets and explicit async deadlines. Coordinate its long retry sequence with the player's 30-second stall decision instead of allowing unrelated retry clocks to compete. A large web framework is not automatically necessary; compare maintenance cost of a minimal supported HTTP host with hardening this small loopback server.

### N08 — Websocket acknowledgement is not execution completion

PlaybackWebSocket handles bounded reconnect delay and abort-based disconnect well. It buffers messages and command IDs without an explicit size/count budget. Commands such as seek/stop/plan invalidation can report completion when asynchronous work was merely queued. Preserve distinct accepted/completed/failed semantics, and scope deduplication across reconnects if the server retries commands. General EventChannelClient and WatchTogether have additional ownership issues in their separate reports; do not assume this one class establishes a uniform realtime contract.

### N09 — Remove the abandoned software rendering path

MpvVideoHost and the unused software render/init/frame-copy path in MpvPlayer are retained alongside the actual native HWND presentation. Call-site inspection found no shipping consumer of the software frame event. PlayerOverlay is also always deactivated; only its subtitle-AI dialog is reused through MainWindow. Extract that live dialog before removing the obsolete overlay and moving necessary subtitle-window maintenance to the session. Keep one active presentation path. Do not remove the native video window solely to make the class graph look cleaner. Report initialization option failures after logging/event hooks are installed; currently early mpv option failures can escape the eventual observer.

### N10 — Multi-GPU upscaling is sensibly gated, but remains experimental

Keep disabled-by-default policy, SDR/size/aspect eligibility, limited scale, resize settling, processor failure fallback, actual shader pass diagnostics, and distinction between a hardware candidate and an activated backend. FSRCNNX is a real spatial shader; adapter discovery is not proof of NVIDIA RTX Video enhancement. Do not market an adapter name as successful vendor SDK activation. Existing frame-pacing notes explicitly withdrew an earlier flawed measurement; they support caution, not an established NVIDIA stutter diagnosis. The native upscaling harness was read but not rerun during this audit; AMD/Intel physical hardware, HDR, display switching and long duration remain validation requirements.

## Collections and recipe findings

### C01 — Editing changes grouped query meaning (high impact)

`SmartCollectionWizardViewModel.cs:141` flattens all query groups into one Rules list. `BuildQueryDefinition(:299)` reconstructs one group using the outer match mode. A query `(A OR B) AND C` becomes `A AND B AND C` when saved, even if the user only changes its title. CollectionsPage routes existing smart collections to this wizard at `:1475`. The older CollectionEditor also flattens groups and forces all/all. Imported collection display filters are rebuilt from only watched/type options, discarding other existing refinements.

Preserve a full query AST, including unknown supported server fields, and change only explicitly edited properties. Share one typed query model between browse, preview, recipe and save; do not create another parallel simplified serializer. Add no-change roundtrip fixtures and mixed group/operator cases before refactoring UI.

### C02 — Preview and save have different library/scope semantics

Wizard preview `:194` sends only the first selected library, while saved QueryDefinition contains all SelectedLibraryIds. Preview also maps video scope to null whereas saved queries retain it. At minimum the multiple-library difference is definite and can mislead the user about saved membership. Run preview against the same complete query definition/endpoint as save. The preview lifecycle gate and 400ms debounce are good mechanisms worth keeping.

### C03 — Rule deletion does not consistently refresh preview/UI

Wizard's remove button calls RemoveRule, but the collection-change subscriber only rebuilds controls; it does not schedule preview. Removing the final rule mutates its plain properties without collection notification, so visible controls can still show the old filter while save uses the reset one. Make query edits one observable transaction that invalidates preview. Existing tests assert names such as SchedulePreview and between are present, not that each editing action produces the correct query.

### C04 — Template layout contains contradictory fixed widths

CollectionsPage computes a compact dialog width but uses a 780px one-column card (`:402`) and an 810px configuration stack (`:633`). Those cannot fit a compact window's content area. Recipe gallery similarly fixes its content at 752px. Replace fixed child widths with viewport-constrained layout. This is source geometry, not a claim of completed screenshot parity validation.

### C05 — Recipe generation guard is after the mutations

RecipeGalleryDialog's BuildParamsAsync captures a generation at `:157`, but checks it at `:177` only after awaited builders append controls and callbacks to the shared host/config. Selecting another section type while an earlier collection/library fetch is pending can append stale fields to the new editor. Build into detached per-generation state and publish once current. Avoid fetching every library's entire collection list concurrently just to build an eager dropdown; use searchable bounded data for large installations.

### C06 — Collection rendering repeats earlier scalability problems

CollectionsPage builds full control trees before assigning them to ItemsRepeater (`items.Select(BuildCollectionCard).ToList()`), so the repeater cannot defer construction. Server collection rows similarly create every image/control eagerly, and direct BitmapImage URLs bypass the shared byte cache. Manual collection loading emits one CollectionChanged per item and queues a complete UI rebuild per notification, producing quadratic construction work. Use item data/templates, coalesced reconciliation and shared artwork lifetimes. Keep group ordering, per-viewer sort persistence, creation switching to edit mode before follow-up item requests, and dialog deferrals that preserve errors rather than claiming success.

## App and delivery findings

### D01 — Blanket exception handling hides unrecovered state

App.xaml.cs:63 marks every UI exception handled before writing a synchronous crash file. The UI may remain alive with partly applied state, as current Home collection COMExceptions demonstrate. Isolate recoverable operations, attach context to errors, and recover or terminate cleanly when invariants cannot be restored. Keep redaction and diagnostics, but use a bounded structured writer and date/build/session correlation. ui_lag currently lacks sufficient date/build context to attribute historical entries reliably.

### D02 — Project boundaries do not match shipped assembly boundaries

SiloPlayer.csproj:96–97 compiles Core and Player source directly into the UI assembly while standalone Core/Player projects also exist. Tests principally exercise standalone Core plus selected linked source. This is not three enforceable production modules. Prefer actual assembly references once WinUI packaging constraints are validated; alternatively describe it honestly as one assembly with namespaces. Preserve useful pure seams. Reflection and trimming constraints justify the current no-trimming setting; do not enable AOT/trimming blindly.

### D03 — Packaging cleanup can delete an arbitrary caller-supplied directory

installer/build.ps1 accepts PublishDirectory, resolves it, then recursively deletes it at :95 without proving it is a dedicated output directory. A mistaken path naming the repository or another existing directory is destructive. Restrict cleanup to a canonical dedicated output root and refuse repo/root/ancestor paths. This audit did not run that cleanup. Runtime options also advertise architectures despite forcing x64 build/installer/native DLL assumptions; accept only validated x64 or implement true per-architecture packaging.

### D04 — Build traceability and supported runtime need modernization

Source declares .100 while installed QA identifies .101 preview built with overrides and dirty source. This audit recovered correspondence through exact DLL/PDB hashes, but releases should ship a source/asset/dependency manifest rather than require forensic reconstruction. Keep runtime SHA checks, native PE/LFS validation and signing hooks. Add reproducible SDK/dependency inputs and automated test/native smoke gates; tests are not included in the main solution and packaging does not establish their success. Avoid indiscriminate package upgrades.

For a fresh build today use a validated .NET 10 LTS toolchain: .NET 8 support ends November 10, 2026, while .NET 10 is supported through November 14, 2028 ([Microsoft policy](https://dotnet.microsoft.com/en-us/platform/support/policy)). Migration needs WinUI/native regression checks, not just a target-framework edit. Current installer removes prior app files before extraction; a versioned staging/rollback approach would improve update resilience. No installer rollback failure was reproduced here.

### D05 — Parity automation is not a completeness oracle

audit-delta skips missing upstream paths and can miss deleted files in additional-directory scans. Historical baseline/status records are not proof that current native layout and behavior match current WebUI. Record per-area source references and distinguish source, behavioral and visual evidence. Remove historical admin backlog from the current product completion denominator: admin removal is deliberate in README/current source.

## Runtime evidence and boundaries

* Audit command: dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release -p:Platform=x64. Result: 919 passed, 0 failed, 0 skipped. See test-results/audit-101.trx.
* Audit command: native artwork regression against `.codex-tmp/upscaling-multigpu-test`. Result: 12 transitions, 22/22 visible images each, 139 stale unload callbacks handled; detached source released. This tests a narrow but valuable actual WinUI lifecycle.
* Additional probes executed against the compiled current Core assembly reproduced two missed edge cases: 15 seconds of healthy slow playback over 30 seconds of wall time was reported stalled; the mixed-attribute version list selected 2160p despite a 1080p preference. See helper-behavior-probes.json. These are actual helper executions, not full end-to-end playback reproductions.
* NuGet vulnerability check for the shipping UI project, including transitive packages, returned no known vulnerable packages from the configured nuget.org advisory source. This does not scan the manually bundled native mpv binary or prove the absence of unknown vulnerabilities.
* Installed running app observed around 1.20GB working set, 1.87GB private bytes, 2,931 handles and 136 threads after extended use. Managed lag snapshots are much smaller. This is not an idle baseline, a leak demonstration or a measured ceiling; native image/video buffers and GPU/runtime allocations require separate attribution.
* Current home_error log contains COMException 0x80004005 from MediaItemCollectionReconciler.Apply during Home section refresh. Crash log contains Library guided/advanced filter style lookup failure; browse reviewer located the global resource versus page-local lookup mismatch. These are observed failures with source corroboration, not merely speculative patterns.
* Historical same-day The Runner diagnostic: an open-ended upstream stream failed after 1,107,341,792 bytes at 3MiB/s; sequential validated 32MiB ranges transferred 1,258,291,200 bytes successfully. These results predate this audit and were not rerun. They support bounded progressive ranges and header deadlines plus upstream investigation. They do not identify an exact live CDN configuration or prove a GPU decoder fault.
* Official server main freshly fetched at aeb82e1c935336eba7a4a7b22233134dbee13c4c. Its RollingDeadlineWriter uses a rolling 180-second default and bounded forwarding slices; that is source context, not production configuration verification. No complete server, database, Redis, S3, GPU-node or CDN audit occurred here.

Full source-read coverage is tracked separately. Source review does not equal every-page runtime/visual verification or every dependency's internal source audit.
