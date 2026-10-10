# Auth/profiles and Settings source pass — 2026-10-09

**Resumed final acceptance:** the source-stage/unrun statements below are historical. The current official reference is1a7a3970a9928efb0157460c10888832a8a74eb1. [Combined verification](2026-10-09-combined-verification.md) records the finite source/native/physical evidence, exact1.2.241 payload and native/live boundaries. Correction IDs are reconciled in the central ledger; GitHub publication awaits the user's final installer approval.


Reference: official public `Silo-Server/silo-server` GitHub `main`, pinned by the coordinator to `22e3a0ba7c1431dda77b957ed1012508d23f2b80`. Source was read with `git show` from the reference repository, rather than its dirty checkout. No private GitLab source was used.

Worktree: `C:\Users\Michael\.codex\worktrees\parity-top-three\SiloPlayer`.

## Independent difficulty statuses

- **#4 Auth/profiles: bounded source implementation finished; combined verification pending.** Existing login/OAuth/network login, taste seed, reader/tour, profile editing and accepted C095–C097 behavior remain in place. The current deltas below were applied to their existing implementations.
- **#3 Settings: bounded source implementation finished, including the current shared Home rows flow; combined verification pending.** This is a source checkpoint, with no physical or side-by-side visual acceptance claim.

The user directed one combined verification after source implementation. No tests, builds, publication, installation, app launches, browser/CUA activity, production access or credential reads were performed during this pass. Scoped `git diff --check` and XAML XML parsing were used only to check source formatting and structure.

## Current-source corrections

### Auth/profile changes

- PIN429 feedback reads the coordinator's `ApiException.RetryAfterSeconds` seam, rounds up to minutes with a minimum of one, clears the PIN, and uses the current generic copy when no delay is supplied. Selection verification rejects canceled or stale approvals, and valid results require a nonempty proof. First-profile create/verify failures distinguish a profile that was already created and reject results from a retired profile context.
- `LoginNavigationRequest.SessionEnded` supports the current signed-out and ended-session banner. The coordinator connected the expired-session navigation in MainWindow. Device approval denial/expiry copy follows current source; the existing pairing and network login lifecycle is retained.
- Local Devices settings expose Remember last profile / Ask who's watching in `AppSettings.ProfileLaunchMode`. The coordinator owns the startup selection logic, including the sole-profile shortcut and persisted PIN proof behavior.

Primary files: `ProfilePinFeedback.cs`, `ProfileSelectViewModel.cs`, `ProfileSelectPage.xaml.cs`, `LoginNavigationRequest.cs`, `LoginViewModel.cs`, `LoginPage.xaml`, `LoginPage.xaml.cs`, `ServerConfig.cs`, and PIN call sites in `SettingsPage.xaml.cs`.

### Settings directory, devices and history imports

- The directory renders sequential groups, with one/two/four columns at the current viewport breakpoints and112px desktop cards. Mobile groups share a surface with stacked cards. Current Sessions and profile-launch entries are searchable.
- Device identity includes profile ID and device ID. Initial selection prefers this profile's current device, then another current device, then the first row. Retired lists/details are ignored. Value controls adapt at512px of row width, while switch rows stay inline. Reset sits beneath the explanatory copy; inheritance and household-limit copy preserve the stored choice. Clear-all is named Use profile settings.
- Libraries settings request `includeHidden:true` through the coordinator's cache-safe Catalog API seam. Home page choices use the visible enabled libraries.
- History import targeting uses the acting-admin/profile policy. An account admin acting as a nonprimary profile cannot target another profile. The existing native server-managed Plex PIN flow remains the native counterpart: the WebUI's capability-gated browser direct Plex connection fallback has no native external browser handoff or direct URL source in this flow. Include this adaptation in final review; it is not a physical browser-flow acceptance claim.
- The generic Plugins tab remains collapsed and has no current Settings route. Its compatibility API/rendering is retained for existing callers. The older native `plugins` fixture is compatibility coverage, not acceptance of a current Settings page.

Primary files: `SettingsPage.Account.cs`, `SettingsPage.xaml.cs`, `SettingsViewModel.cs`, `DeviceSelection.cs`, `DeviceSettingDisplay.cs`, `HistoryImportScope.cs`.

### Signed-in sessions

- Capability lookup gates list access; unavailable, failed, loading and empty states are distinct. The list uses50-item cursor pages, validates cursor contracts, deduplicates appended rows and supports60-second refresh.
- Models follow `current`, `current_session`, `device_platform`, `last_seen_at` and `has_more`. The separate current session is rendered even when absent from list items. Device/user-agent display and expandable session details are provided.
- Sign-out confirmations disable pending commands, retain rejection for retry and capture request context. Deletes explicitly do not refresh/replay. Current-session success invokes the existing logout flow. Navigation retires list callbacks/timers/dialogs.

Primary files: `AuthApi.cs`, `AuthSession.cs`, new `SettingsPage.Sessions.cs`, new `LoginSessionDisplay.cs`, directory/navigation regions of `SettingsPage.xaml.cs`.

### Shared profile Home rows

- `HomeSectionWritePolicy.Build` accepts a saved baseline and touched IDs. Untouched server fields remain inherited; existing pinned fields retain their stored values; restoring `default_title` removes only the rename. Appending a personal row does not pin server order. An actual relative reorder stores positions. Unknown configuration fields and stable override IDs are preserved. Deleted personal rows are not resurrected from raw overrides; removed server rows use tombstones.
- `SettingsViewModel` snapshots each desired page state and serializes/coalesces rapid whole-page writes. Success updates the saved baseline for the next queued state. Writes reconcile with a server read; failure rolls back through that read, and read failure holds editing disabled until Reload rows. Pending saves refuse page selection changes; retired profile contexts do not apply results. Reset and transfer actions cannot race an active row write.
- Rows expose show/hide, edit, original-name restore, hero, move-to-top/bottom and remove/delete with confirmation. Hidden rows collapse their metadata/art. Saved-row poster peeks use Home/library item endpoints, three concurrent requests, profile/page/revision guards, cancellation and the existing image-byte cache. A failed peek retains the row icon and edit controls.
- Add row uses one picker card per offered row kind, the current six groups, variant choices, a row name and collapsed More options. Retired kinds are excluded from new choices. Profile rule rows do not require the obsolete custom-section flags request. Collection rows use visible accessible collections, and rule rows retain the existing grouped rule editor. Server row content/kind and legacy Trakt content are locked. Editing applies only locally changed fields, preserving newer row toggles and reloads.
- Profile draft preview and copying to other library pages remain unavailable, matching the current profile adapter's `draftPreview:false` and `libraryCopies:false`; no admin preview route is called. The profile form explains when the added/saved row will appear.
- The form uses a centered880px desktop shell and full viewport below1024px, with a persistent scroll/footer and restored opener focus. The existing admin section sheet is unchanged.
- Hide-watched writes disable the control while pending and restore the prior display on rejection. Import/export reuse the existing mapped transfer implementation and are accessed through the page's More menu. The coordinator's CustomizeHome collection seed and editor launch were preserved.

Primary files: `SectionSettings.cs`, `HomeSectionWritePolicy.cs`, `HomeLayoutTransferService.cs`, Home regions of `SettingsViewModel.cs`, `SettingsPage.xaml`, `SettingsPage.xaml.cs`, `SettingsPage.HomeLayout.cs`, `RecipeGalleryDialog.cs`, new `HomeRowDialog.cs`. Transfer preview also permits profile rule rows without the obsolete flags read; actual import writes remain server-authorized.

## Collection editor seam

New core `CollectionHomeRowUsageService(CatalogApi, SettingsApi).GetAsync(collectionId, ct)` returns `IReadOnlyList<CollectionHomeRowUsage>`. The DTO is in its own file with `Scope`, nullable integer `LibraryId`, `PageName`, `SectionId`, `RowTitle`, `Hidden`.

The lookup reads visible libraries plus effective Home/library row settings with four concurrent reads. It matches `section_type:collection` and `config.user_collection_id`, includes hidden usages, and performs no mutation. Cancellation or a changed API context rejects the whole result. The coordinator owns CollectionEditor Where it shows / Add to Home rendering and can instantiate the service directly; no app DI registration is required.

Library-seeded Add as row continuation: `CustomizeHomeNavigationArgs(CollectionId, Name, int? LibraryId = null)` preserves two-argument Home callers. `CreateCustomizeHomeContentAsync(int? libraryId = null)` selects `home` or `library:{ID}` before loading saved rows; that selector becomes API `scope=library&library_id={ID}`. It waits prior writes and guards API context/page generation. Cached navigation, retry and `OpenCollectionRowEditorAsync` retain the requested library ID and reject retired drafts. Include Home→library→Home cached-navigation and retry/stale-result cases in combined verification. No tests/builds were run for this continuation.

## Staged verification

No assertions have been executed. Added or updated:

- `AccountCurrentBehaviorTests`: PIN delay copy, device selection identity, nonprimary-admin import targeting, inheritance/constraint copy, session wire fields and no-refresh deletes.
- `HomeRowsOverrideBehaviorTests`: inherited fields, restoring the original name, append versus reorder, configuration equality/deep snapshots, and deleted-row nonresurrection.
- `CollectionHomeRowUsageBehaviorTests`: visible/read-only page lookup, hidden usage, cancellation and profile-context rejection.
- `AccountLatestNativeFixture` selector `current-settings`: current directory at1400/1700/460, local launch choices, session capability/page/current/append/refresh and retained revoke rejection/retry, plus production PIN429/wrong/canceled approval behavior.
- `AccountAdvancedNativeFixture` `home-save`: current row editor rejection rollback/retry plus held first save and a newer hero/visibility edit serialized into the second write. Its fake server now persists successful Home writes for reconciliation. `recipe` expects the current HomeRowForm shell and Row name copy. Import calls the actual transfer handler now reached by the More menu. Existing source expectations were updated for current picker vocabulary in `CurrentSettingsParitySourceTests`.

## Cases still requiring the coordinator's combined verification

1. Compile all changed WinUI bindings/types and run staged core/native assertions with the final source tree. Review historical session/PIN fixtures for old wire/copy assumptions.
2. Run source-relative visual and physical comparisons for profile select/create/PIN, ended-session and signed-out banners, launch remember/ask including one PIN profile, Devices settings row-width behavior, responsive directory and Sessions states/actions at narrow and desktop sizes.
3. Verify existing title-art two-step partial failure/profile-switch behavior; stored404/read failure and household lock; provider readiness/automatic directory request; device-pairing cancellation/stale approval; taste, tour, reader and C095–C097 regression paths.
4. Exercise shared Home row add/edit/variant/rules/collection, original-name restore, reorder/hidden/hero/top/bottom/remove/reset, rapid queued saves, failure followed by failed read/retry, page/profile switching, saved poster peeks, empty states, Home hide-watched rejection, transfer More actions and seeded Add to Home. Compare row descriptions, badges, picker/form spacing and responsiveness with current source. Verify modal keyboard/focus behavior physically.
5. Integrate and verify the coordinator's CollectionEditor usage/Add to Home UI against the new read-only seam. Review the documented native Plex adaptation. No visual/physical acceptance or production behavior is implied by this source checkpoint.

Ledger correction proposals for the coordinator: PIN429 + stale create/verification; ended-session banner; local launch choice; current Settings directory; device identity/container layout; include-hidden settings libraries; acting-profile import scope; current signed-in sessions; shared Home rows sparse writes/queue/UI; read-only collection Home usage. Correction IDs and accepted status remain coordinator-owned.
