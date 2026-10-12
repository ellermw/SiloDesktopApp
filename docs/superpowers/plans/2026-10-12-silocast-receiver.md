# Windows SiloCast Implementation Plan

**Goal:** Build the approved phone-compatible Windows receiver and a local test installer.

**Spec:** `docs/superpowers/specs/2026-10-12-silocast-receiver-design.md`.

**Execution:** Native integration with parallel specialists for independent transport, handoff, and discovery boundaries. The user explicitly approved implementation and installer construction. Keep changes uncommitted; no release/push or production changes.

## Constraints and review focus

Use the pinned official source commits in the spec. Preserve local credentials. Launch only under server-approved temporary identity. Keep network work off the dispatcher. Ship the feature off by default. Validate real-phone interoperability separately from automated checks. Bound transport input/output, callback lifetime, reconnects, and canceled handoffs. Preserve the existing checkout and running application.

## Tasks

- [x] **1. Framing and TLS.** Write failing tests for big-endian framing, fragmented reads, oversized and truncated frames, repeated TLS PSK connections, and JSON casing. Implement Core `Services/SiloCast/SiloCastProtocol`, `SiloCastFrameCodec`, and `SiloCastConnection` using pinned BouncyCastle.Cryptography. `SiloCastConnection.AcceptAsync(TcpClient,CancellationToken)` returns a receiver connection; `ReadAsync` returns `JsonObject?`, `SendAsync(JsonObject,CancellationToken)` preserves ordering, and `DisposeAsync` cancels blocked work. Run focused tests.
- [x] **2. Temporary handoff.** Write failing tests for capability/start/poll, canceled/denied/expired requests, mismatched profile proof, and identity verification. Implement `SiloCastHandoff.PrepareAsync(JsonObject offer,string deviceName,string deviceId,Func<JsonObject,Task> challenge,CancellationToken)` returning `SiloCastIdentity` with owned HTTP/API/Auth objects and absolute expiry. It never changes saved/local credentials. Run focused tests.
- [x] **3. Windows discovery.** Implement native DNS-SD advertisement `StartAsync(int port,IReadOnlyDictionary<string,string> txt,CancellationToken)` and stop/dispose, with callback ownership and interface changes. Verify private physical network eligibility and DNS-SD errors without touching firewall state. Add callback/lifetime tests where practical.
- [x] **4. Playback and receiver.** Introduce a captured local/temporary playback authority scope; test local credentials and final progress behavior. Implement launch/control/state adapter and receiver lifecycle with v2 hello, per-controller generations, heartbeats, handoff fencing, state coalescing and local takeover. Test critical session state transitions.
- [x] **5. Settings and installer.** Add off-by-default setting, receiver status, disconnect button and remote session notice. Integrate application shutdown, server/logout changes, minimized operation and fullscreen after content load. Add private-network-only optional installer firewall task. Build with distinct test version.
- [x] **6. Verification.** Run Core/native regression suites and x64 Release publish. Review changes independently, fix material defects, build Inno installer, verify output/hash and write a concise test guide. Actual iPhone/Android acceptance remains for the user unless a real connected device is available. Full default native verification remains incomplete at the documented preexisting series-action fixture mismatch; passing checks are recorded separately below.

## Execution record

Subsequent publication authorization: after installing the 1.2.243 candidate, the owner explicitly requested committing/pushing to GitHub and updating the README/download on October 12. This supersedes the original keep-uncommitted instruction for this feature. Publication retains the same installer bytes and disclosed native/device verification limits; the canonical source is now committed on main under release tag v1.2.243.

Ruling: proceed directly after creating this plan because the user explicitly approved the written design and instructed implementation and installer creation. Routine plan transcription does not require another approval question.

Ruling: use a native isolated worktree at the current HEAD, leaving the original README changes and untracked notes intact. No changes will be committed without a separate user instruction.


Obsolete artifact record: the original Windows x64 1.1.106 SiloCast installer was built from stale base `3488a4942ee0d03448bd609d04334e74b963bc7d`. Its passing checks did not establish compatibility with the current 1.2.242 app. The user identified the version discrepancy; the artifact is retained but must not be installed.

Baseline correction: native worktree `silocast-current` starts at `d5de162a9669418d072db99aa12e3fb66fe2bd1a`, which matched freshly fetched official desktop main. All eight tracked and four new files from the complete 1.2.242 candidate were copied before migrating SiloCast. Verified 1,258 unaffected source/test/build files against that candidate: byte-identical after normalizing checkout line endings by copying its original bytes. Distinct corrected test version: 1.2.243.

Migration ruling: preserve the current API context fences, library/file watch preparation, split playback modules and current Watch Together synchronization. Resolve old feature patch rejects against the current code; never replace current files wholesale with old versions. Cast handoff specialist owns PlayerService migration, review specialist owns coordinator fences, and the main agent owns UI/application/packaging integration. Changes remain uncommitted.

Verification in progress: repeat the complete regression suite and published/native checks on the corrected payload before delivering the 1.2.243 installer.

Migration verification: initial complete current-baseline main run passed 1,656/1,656. The first native-stream tests encountered an LFS pointer copied by PreserveNewest before the pinned DLL was materialized; refreshing the source binary timestamp corrected the build input, and all 13 native network fault cases passed without product changes. The first corrected publish/installer succeeded; it remains an intermediate artifact pending ownership fixes.

Review fixes: shared controller-close completion prevents a concurrent candidate cleanup from disposing lifetime while another closer is draining a send (held real transport write reproduced RED, then GREEN). Receiver suite now also proves two canceled server approvals can retain cleanup slots while a third waits and pongs remain responsive (RED, then GREEN). The global two-preparation bound includes cancellation withdrawal. Atomic disconnected-status updates preserve replacement controller status. App-owned audio/quality, subtitle-dialog restore and queued autoplay continuations are fenced by captured authority/session and canceled owner lifetime; published/native checks are pending the final rebuild.

Integration ruling: preserve the externally managed detached worktree and leave changes uncommitted, honoring the existing explicit no-commit/no-push instructions. The local test installer completes the authorized delivery; no integration approval menu is needed.

Final delivery: version 1.2.243.0; installer `D:\SiloPlayer\installer\output\SiloInstaller-1.2.243-SiloCast-Test-Setup.exe`, 172,135,207 bytes, SHA256 `16D2841FA4E65B31847715EC835F0CB9C8A2DFB9C66E6D4D338C701015D88C7B`. All 891 frozen build inputs and 561 packaged files remain unchanged after verification. Publish/Inno succeed with two unique nullable publish warnings. Main 1,657/1,657, receiver 14/14, discovery 16/16, 23 published playback groups, settings at 600/900 widths, and 42 rendered badge cases pass. Final independent product and fixture reviews report no unresolved findings.

Native verification limitation: initial account fixture failure also reproduces against the unmodified 1.2.242 payload. Test-only account/search fixture repairs initialize current authentication/scope gates and target current controls while retaining assertions; the final run passes those areas. The broader default run then stops at unchanged DetailPresentationNativeFixture line 55 because it expects a hidden unavailable series action; current 1.2.242 production intentionally displays it disabled. Relevant page/view-model and fixture hashes match 1.2.242 exactly. Preserve this failure and disclose incomplete later default cases; do not expand SiloCast scope into unrelated fixture modernization or claim a complete native pass. Real phone acceptance remains unverified. No installer was run, no source was committed, and no release was published.
