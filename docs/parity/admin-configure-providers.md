# Configure > Providers parity

Source candidate: branch `t3/admin-provider-settings`, merged with fresh `origin/beta` on November 20, 2026; version stamps unchanged.

Official reference: Silo Server/WebUI `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`.

## Scope

The native `/admin/settings/providers` page owns the same server configuration as the pinned WebUI:

- installed watch/scrobble-provider status and setup links;
- MDBList list-search API key, configured/environment-managed state, restart hint, guarded save, clear confirmation, and connection test;
- OpenSubtitles account plus SubDL, SubSource, and future subtitle-provider API-key editors, with strong ETags, enable preference, test, clear confirmation, and (after any failed write) a disabled "save not confirmed" tile whose own Reload re-reads that provider and discards the drafts, as the official tile does;
- marker lookup enabled, lookup priority, fetch minimum confidence, sharing enabled, automatic local sharing, sharing minimum confidence, plugin credential status/setup, and account validation statistics;
- read-only Playback marker-mode explanation; and
- the official metadata-provider Plugins ownership handoff.

Plugin credentials remain owned by the Plugins pages. This page does not duplicate plugin-configuration writes or alter the Plugin Apps shell/sidebar.

## Safety and lifecycle

Reads, response sizes, provider counts, plugin pagination, and deadlines are bounded. Every operation is bound to the current admin authority. Subtitle editors are single-use and require a strong ETag. Settings, subtitle, marker, and test writes are sent once; uncertain outcomes are not replayed. A 412 or uncertain outcome of a settings or marker write retains the page draft and requires an explicit reload/review; a subtitle tile follows the official flow (see the failed-save recovery record). Stored secrets are represented only by configured/environment-managed status; neither stored nor typed credential material is included in diagnostics or model `ToString()` output. Page suspension and authority retirement clear private drafts.

## Source inventory

- `src/SiloPlayer.Core/Api/AdminProvidersApi.cs`
- `src/SiloPlayer.Core/Api/AdminServerSettingsApi.cs`
- `src/SiloPlayer.Core/Models/Admin/AdminProviderModels.cs`
- `src/SiloPlayer.Core/Services/Admin/AdminProvidersController.cs`
- `src/SiloPlayer/Services/AdminConfigureServices.cs`
- `src/SiloPlayer/Views/Admin/AdminProvidersPage.xaml`
- `src/SiloPlayer/Views/Admin/AdminProvidersPage.cs`
- `src/SiloPlayer/Views/Admin/AdminProvidersPage.Cards.cs`
- `tests/SiloPlayer.Tests/AdminProvidersTests.cs`
- `tests/SiloPlayer.NativeRegressionTests/AdminProvidersNativeFixture.cs`
- `tests/SiloPlayer.NativeRegressionTests/Program.cs`
- `installer/build.ps1`
- `docs/parity/admin-monitor-surfaces.json`
- `docs/parity/admin-configure-providers.md`

## Review findings fixed (November 20)

Two independent source reviews reported four defects. Each is fixed and has a regression test.

1. **Disconnect confirmation after the authority was replaced.** The confirmed action re-captured whatever authority was current when the dialog closed. `ConfirmForCurrentOwnerAsync` now records the owner when the dialog opens and refuses a confirmation whose owner, controller or editor is no longer current; `ClearMdblistAsync(confirmedOwner)` re-checks it; the page hides an open confirmation when the authority changes; subtitle editors carry the account and epoch that produced their ETag and `SaveSubtitleProviderAsync` throws `AdminProviderAuthorityChangedException` before consuming the editor or sending anything. Tests: `DisconnectConfirmedForAReplacedAuthorityNeverWritesTheNewAuthority`, `SubtitleEditorFromAReplacedAuthorityIsRefusedBeforeAnyWrite`, and the native "disconnect after authority replacement" scenario.
2. **Truncation before credential redaction.** Subtitle test errors and marker validation errors were cut to 1,000 characters before typed credentials and diagnostic secrets were replaced, so a secret straddling the cut survived in part. They are now redacted (typed literals first, then diagnostic patterns) and only then bounded. Tests: `SubtitleTestRedactsCredentialsBeforeApplyingTheDisplayLimit`, `MarkerAndSubtitleTestTextIsRedactedBeforeItIsTruncated`. The MDBList connection check reuses the merged Beta implementation, which already redacts first (500 characters); the `mdblist` check kind and its secret key were added to it.
3. **Marker unknown outcome had no reachable reload.** The Reload button lived in an alert that only appeared when a page-level error was set; an unknown marker write sets only provider feedback. `AdminProvidersState.ReloadRequired` now drives the alert, the tile shows its own Reload while the outcome is unknown, and a reload only clears an unknown outcome when the marker (or subtitle) list was actually read again. Tests: `MarkerUnknownOutcomeKeepsReloadReachableAndReloadReconcilesIt` and the native markers scenario.
4. **Marker save discarded edits typed while the request was pending.** The page removed the draft after the acknowledgement. `AdminMarkerProviderDraft.BeginSave` now records what was sent; `Rebase` keeps any field that differs from the sent snapshot (including one edited back to the old saved value), the page keeps the tile open when edits remain, and a failed send leaves the draft untouched. Unchanged confidence percentages also keep the saved 0-1 value exact (float drift found by the first focused run). Tests: `MarkerEditsTypedWhileASaveIsPendingSurviveTheAcknowledgement`, `MarkerFieldsUneditedAfterTheSendTakeTheServerValueAndAFailedSendKeepsTheDraft`, `MarkerDraftSendsChangedFieldsOnlyAndConvertsPercentToWireRange`, and the native pending-save scenario.

Also corrected while driving the page: the MDBList secret field is cleared after a successful save or disconnect, and an authority retirement removes private controls from the tree.

## Registration and reachability

`AdminRoute.ProviderSettings` (`/admin/settings/providers`) is an official Configure > Integrations navigation leaf in `AdminRouteCatalog`; `AdminConfigureServices` registers the page, API and controller and adds `markers.mode` and the `mdblist.api_key` secret to the shared settings snapshot. `AdminProvidersRegistrationTests` pins the registration, the approved `AdminProvidersPage.xbf` resource and the surface-policy entries.

## Deviations and limits

- Page text is English constants; the official `web.admin.settings.providers.*` message catalog is not wired into this page.
- The official primary action reads "Fix" after a failed test; the native tiles keep Connect/Manage. A failed test is shown in the tile.
- The official watch-provider error row has its own Retry; the native page uses the page-level Reload.
- Subtitle editors are single-use and, after any failed save, the tile is "save not confirmed" until its own Reload re-reads that provider and discards the drafts, exactly as the official tile (owner decision, November 21; see [Providers failed-save recovery](admin-providers-webui-recovery.md)). The earlier two-step recovery and retained typed username described elsewhere in this record are superseded.
- No rendered capture was inspected, and live-server compatibility, all-DPI and screen-reader behavior are unverified.

## Independent review of the final diff (November 20)

A read-only review confirmed the four fixes above and reported four new defects, all fixed:

1. **P1, Reload unreachable after a partial load.** A failed credential-status, subtitle, marker or plugin read set no page error, so the alert with Reload stayed hidden and the MDBList tile read "Not connected" with a disabled key box and no explanation. The alert now also covers those failures, and the tile shows "Status unavailable" with the reason. Native scenario "credential status unavailable" and a Reload check in "section failures".
2. **P2, Test sent an empty key as a draft.** Erasing a typed key, or a Disconnect that failed, left an empty value staged, so Test asked the server to check "" instead of the saved key. An erased key is no longer a draft and a failed clear is not kept staged; Test sends the key only when a non-empty one is staged. Tests: `ErasedMdblistKeyIsNotADraftSoTestChecksTheSavedKey`, `AFailedDisconnectLeavesNoStagedClearForALaterTest`.
3. **P2, unchosen marker write.** For a provider that is not a submitter but has sharing stored, the draft started from gated values, so it opened dirty and Save wrote `contribute_enabled` and `contribute_auto_local` unasked. The draft now starts from and compares against the raw saved values, as the official tile does; the submitter gate only affects what is shown and enabled. Test: `MarkerDraftForANonSubmitterWithStoredSharingOpensCleanAndNeverWritesContributeFields`.
4. **P2, typed key dropped.** Save stayed enabled while the credential status was unavailable and then cleared the typed key without saving. Save is disabled in that state and a key the controller refused is kept.

## Verification status

Final run `providers-lead-final-r1` on commit `07a7bc3c208ddbc40f14fd377194af746f114311`, under the SiloOct18Build lease: nonincremental unit build; 29 focused (`AdminProviders*`) and 9,726 full unit tests with zero failures or skips; nonincremental x64 app build, self-contained publish and native host build; native selectors `admin-configure-providers` (89 passing checks), `admin-manage-foundation`, `admin-configure-general` and `admin-monitor`. ACQ `2026-11-20T15:28:52.3289031Z`, REL `2026-11-20T15:34:32.4588724Z`. Source seal: 2,481 build inputs, `d22018fbe53b61c50984d7c76cafdd07821bfd3007a08efb924769e7a38ae6d5`; payload: 582 files, `0930daf836096a5cf92e27ff21108198aade5cfcad8ca0227172a5107c41ccc7`, unchanged across the run. This note was added after that run; no other file changed.

The native selector drives: loading, load error, empty, section failures, credential status unavailable, populated at 1280 and 760 px, MDBList save/failed/unknown/test/disconnect, replaced-authority disconnect, subtitle test/failed/unknown/conflict/disconnect/connect, and marker failed/pending-edit/unknown/test.

Earlier runs: `providers-final-r2` passed 26 focused and 9,723 full unit tests and the same four selectors before the review corrections. `providers-final-r1` failed 1,187 repository-locating unit tests only because an early version of the shared script built outside the checkout; the script was corrected and no test infrastructure is changed here.

Not run: live Silo server, real provider accounts, rendered-capture inspection, Narrator/IME/all-DPI checks.
