# Configure > Providers parity

Source candidate: branch `t3/admin-provider-settings`, merged with fresh `origin/beta` on November 20, 2026; version stamps unchanged.

Official reference: Silo Server/WebUI `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`.

## Scope

The native `/admin/settings/providers` page owns the same server configuration as the pinned WebUI:

- installed watch/scrobble-provider status and setup links;
- MDBList list-search API key, configured/environment-managed state, restart hint, guarded save, clear confirmation, and connection test;
- OpenSubtitles account plus SubDL, SubSource, and future subtitle-provider API-key editors, with strong ETags, enable preference, test, clear confirmation, and retained drafts after unconfirmed writes;
- marker lookup enabled, lookup priority, fetch minimum confidence, sharing enabled, automatic local sharing, sharing minimum confidence, plugin credential status/setup, and account validation statistics;
- read-only Playback marker-mode explanation; and
- the official metadata-provider Plugins ownership handoff.

Plugin credentials remain owned by the Plugins pages. This page does not duplicate plugin-configuration writes or alter the Plugin Apps shell/sidebar.

## Safety and lifecycle

Reads, response sizes, provider counts, plugin pagination, and deadlines are bounded. Every operation is bound to the current admin authority. Subtitle editors are single-use and require a strong ETag. Settings, subtitle, marker, and test writes are sent once; uncertain outcomes are not replayed. A 412 or uncertain outcome retains the page draft and requires an explicit reload/review. Stored secrets are represented only by configured/environment-managed status; neither stored nor typed credential material is included in diagnostics or model `ToString()` output. Page suspension and authority retirement clear private drafts.

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
- Unlike the official page, a subtitle editor is single-use: after a failed, conflicted or unknown save the tile must be reloaded before saving again.
- No rendered capture was inspected, and live-server compatibility, all-DPI and screen-reader behavior are unverified.

## Verification status

Observed on this source in the lane `D:/SiloPlayer-build/admin-completion-lane-a` (single build lease, task `admin-families-nov20`): focused unit run `providers-focused-r2` (26 `AdminProviders*` tests, 0 failures) and native selector `admin-configure-providers` through `providers-host-r3` (loading, load error, empty, section failures, populated at 1280 and 760 px, MDBList save/failed/unknown/test/disconnect, replaced-authority disconnect, subtitle test/failed/unknown/conflict/disconnect/connect, marker failed/pending-edit/unknown/test). The final acceptance run (full unit suite plus the Providers, Manage foundation, General and Monitor selectors) is recorded in its lane receipts under label `providers-final-r1` and in the delivery report.

Not run: live Silo server, real phone or provider accounts, rendered-capture inspection, narrator/IME/all-DPI checks.
