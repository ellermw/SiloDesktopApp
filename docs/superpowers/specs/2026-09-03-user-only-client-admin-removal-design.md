# User-Only Desktop Client and Missing-File Playback Parity Design

## Status

Approved in conversation on 2026-09-03.

## Reference

- Desktop repository: `D:\SiloPlayer`
- Authoritative Silo Server/WebUI repository: `https://github.com/Silo-Server/silo-server`
- WebUI/server commit used for the missing-file behavior analysis: `7c1cb2d3f34e7a37d2864b63735386300e81ef31`
- Existing unrelated working-tree changes must be preserved. This work must not reset or overwrite installer, OSC, auto-skip, or playback changes already present before implementation begins.

## Goal

Turn Silo for Windows into a user-facing media client with no general server-administration workspace, while retaining two narrowly scoped, permission-gated media-maintenance actions on item detail pages:

1. Match Item, including correcting an existing match.
2. Refresh Metadata, including the server-supported Quick and Complete modes.

At the same time, make initial missing-file playback behavior match the current WebUI exactly: fail the selected attempt, show the WebUI-equivalent error with a Go Back action, refresh stale watch data for a later user-initiated attempt, and do not automatically cycle through files.

## Product Boundary

### Included user-facing capabilities

- Authentication, profile selection, and normal use by both administrator and non-administrator accounts.
- Home, search, libraries, catalogs, recommendations, requests, notifications, favorites, watchlist, collections, history, downloads, settings, people, ebooks, audiobooks, manga, playback, subtitles, and Watch Together.
- Plugin-provided navigation and experiences intended for ordinary users.
- Read-only technical playback and media information that the server permits the selected profile to see.
- Match Item/Fix Match and Quick/Complete Refresh Metadata on item detail surfaces when `AuthorizationPolicy.CanCurateMetadata` permits them.

### Removed administrative capabilities

- The complete Admin shell, navigation rail, command palette, pages, and view models.
- Dashboard, activity, tasks, logs, nodes, plugins, recommendations administration, home-section administration, subtitle-provider administration, libraries/scanning, collections administration, maintenance, users, access groups, devices, Autoscan, policy, API keys, invitations, playback history administration, marker history, history import, and server settings.
- The main-shell Admin button and every deep link into an Admin page.
- The administrator server-activity control and its background polling/event path. The ordinary user Notifications item and unread badge remain.
- User impersonation and stored impersonation administrator sessions.
- Server setup/configuration from the desktop client.
- Item-level View Play History, Re-detect Intro Markers, Edit Metadata, Edit Markers, Split Versions, image administration, media deletion, person administration, and other curation tools not explicitly retained above.
- Administrator-only fields in collection/setup/settings experiences when those fields mutate server configuration.

## Architectural Approach

### 1. Remove the Admin application surface completely

Delete the `Views/Admin` and `ViewModels/Admin` trees, `AdminCommandPaletteDialog`, Admin navigation and title mappings, Admin dependency-injection registrations, and Admin-only controls such as `ServerActivityButton`. Remove the corresponding tests rather than leaving source-presence tests for code that no longer ships.

This is source removal, not a visibility flag or compile-time feature switch. A release build must not contain Admin XBF resources or page types.

### 2. Replace the oversized Admin API with a narrow maintenance seam

Create a focused `MediaMaintenanceApi` containing only the operations required by the retained detail actions:

- Search match candidates for an item.
- Apply a selected match to an item.
- Queue a Quick or Complete metadata refresh for an item.

Move only the match request/response/candidate DTOs and the minimal metadata-refresh acknowledgement DTO into a neutral media-maintenance namespace. `MatchItemDialog`, `RefreshMetadataDialog`, and `ItemDetailPage` consume this focused API. They must not depend on `AdminApi`, `Models.Admin`, or Admin page types.

The server may still authorize these endpoints as administrative or curation operations. Desktop visibility remains permission-gated using the existing effective curation policy; a direct call must still rely on server authorization and surface a normal error if permission changes after the page loads.

After all consumers are migrated or removed, delete `AdminApi`, unused Admin models, and `AdminLogStreamClient`. Shared API classes that contain both user and administrative methods are pruned method-by-method; user-facing request, notification, playback, settings, and plugin contracts remain intact.

### 3. Preserve user-facing behavior for administrator accounts

An account whose server role is `admin` still signs in and uses the same Home, library, detail, and playback experiences as any other account. Role and capability parsing remain because the retained maintenance actions and server-provided field visibility depend on them. No Admin destination appears merely because the account is an administrator.

### 4. Move server setup to the browser

If the connected server reports that initial server setup is incomplete, the desktop app must not host the current administration wizard. It presents a focused “Complete setup in the Silo WebUI” state with:

- An Open WebUI action targeting the connected server origin.
- A Retry action that rechecks setup/authentication state after browser setup is complete.
- No server settings, library creation, or administrator credentials beyond the ordinary desktop sign-in flow.

### 5. Remove embedded administrator actions without harming ordinary detail actions

The item-detail More menu continues to expose ordinary actions such as Play from Beginning, Watchlist, Collection, Download, subtitle search, and permitted read-only Media Info. Its curation group contains only:

- Refresh Metadata.
- Match Item for supported item types.

The Match Item action continues to mean both initial identification and correction of an existing match. Refresh Metadata continues to use the existing Quick/Complete modal. Successful operations refresh the current item safely and reject stale asynchronous results after navigation, following the page’s existing content-identity guards.

### 6. Match current WebUI missing-file behavior

The desktop start path follows this state machine:

1. Fetch or consume current `/watch/{content_id}` data.
2. Select one requested file using the current version-selection rules.
3. Send one protocol-v3 start request. The server remains responsible for adaptation of a valid source, including a compatible alternate selected by the server planner.
4. If initial start returns HTTP 404 because the physical source is missing, do not issue another start request for another local version.
5. Retire partial playback state, hide the native video surface, and show the same transport-level presentation as the WebUI:
   - Title: `This item is no longer available`
   - Message: `The file needed to play this item can't be found right now. Go back and try another version if one is available.`
   - Action: `Go Back`
6. Evict watch-detail prefetch/cache state for that content. This mirrors the WebUI’s `metadata.updated` query invalidation after the server marks the file missing.
7. Remain on the error surface until the user chooses Go Back. Do not automatically restart when refreshed data arrives.
8. On a later user-initiated Play action, fetch fresh watch detail. A missing file already marked by the server is absent from the returned versions, allowing the normal selection rules to choose a remaining version when the new request is not explicitly pinned to the unavailable file.

The special retry that waits for a retiring session after `too_many_streams` remains because it is session-transition handling, not alternate-file selection. Runtime recovery after an already established playback session also remains separate from initial missing-file behavior.

## Data and Dependency Cleanup

- Remove dedicated Admin services and registrations from `App.xaml.cs`.
- Remove Admin page types from `DocumentTitle` and navigation/back-stack handling.
- Remove Admin ownership branches from shell visibility, responsive navigation, and keyboard shortcuts.
- Remove the Admin button, impersonation banner, and server-activity overlays from `MainWindow.xaml`.
- Remove impersonation persistence and restoration code from `AuthService` and its UI integration while preserving ordinary token refresh, profile selection, and role/capability state.
- Remove Admin-only methods from shared API classes only after proving no retained user-facing caller uses them.
- Keep server DTO fields that are part of user-facing response contracts even when their names contain `admin` (for example, a server-provided `admin_only` recipe flag) if removing the field would break deserialization or ordinary filtering. Naming alone is not a deletion criterion.
- Remove Admin-only test files and audit/parity assertions that require the deleted surface.
- Historical audit documents remain historical records unless the README links to them as current functionality.

## Error Handling

- Match and refresh controls disable only while their own operation is pending and always re-enable in `finally`.
- Server authorization failures show the existing error toast and do not navigate into an Admin surface.
- A match or refresh completion that arrives after navigation cannot repaint a reused detail page.
- Browser-launch failure on the setup-required screen leaves the Retry path available and shows an actionable message.
- Missing-file playback never leaves the mpv child window visible above the error surface.

## Tests and Verification

### Test-first automated coverage

- A playback regression test proves an HTTP 404 missing-source response results in exactly one start request, the WebUI transport copy, and no alternate-file attempt.
- A cache regression test proves the failed content’s watch-detail prefetch is evicted before a later user-initiated attempt.
- Existing server-planner/adaptation tests continue proving valid-source fallback behavior remains untouched.
- Media-maintenance API tests cover match search, match apply, Quick refresh, Complete refresh, and server authorization failures.
- Detail-page source/behavior tests prove only Match Item and Refresh Metadata remain from the former administrator action group.
- Architecture guard tests fail if `Views/Admin`, `ViewModels/Admin`, `AdminApi`, Admin DI registrations, Admin navigation, impersonation UI, or the server-activity control returns.
- Tests protect ordinary notifications, requests, collections, plugin user navigation, and role-aware media-detail visibility from accidental deletion.
- Setup-required tests prove the app directs the user to the browser and can retry after setup.

### Build and runtime verification

- Run the complete test suite.
- Publish `Release` for `win-x64`.
- Build the normal multi-file QA installer using the established signing/packaging path; do not change packaging architecture as part of this milestone.
- Verify an administrator account lands in the ordinary user shell with no Admin navigation or server-activity overlay.
- Verify a non-administrator account has unchanged user navigation.
- Verify Match Item and both metadata-refresh modes with an authorized account.
- Verify those controls are absent without curation permission.
- Verify Home, search, library, movie/series/episode details, and playback still open.
- Verify the produced package contains no Admin page XBF files.
- Record installer size and application startup measurements before and after, reporting observed changes without promising a predetermined improvement.

## Documentation

- Update the README to define the desktop app as a user-facing Silo client.
- State that server administration is performed in the Silo WebUI.
- Document Match/Fix Match and Refresh Metadata as the only permission-gated maintenance exceptions.
- Remove current-feature and parity claims for desktop Admin pages.
- Do not rewrite historical release notes into claims about the current build.

## Non-Goals

- No changes to Silo Server behavior or server repositories.
- No pull request to the Silo Server project.
- No new automatic alternate-file retry beyond the server planner’s existing adaptation.
- No feature flag for restoring desktop Admin pages.
- No unrelated redesign of the user-facing shell.
- No installer architecture change.

## Completion Criteria

This milestone is complete only when:

1. No general administrative page or navigation route ships in the desktop application.
2. No background administrator activity polling starts from the user shell.
3. Match/Fix Match and Quick/Complete Refresh Metadata remain functional and permission-gated.
4. Initial missing-file playback behavior matches the referenced WebUI flow and copy.
5. The full automated suite, x64 Release publish, installer build, and runtime smoke checks pass.
6. Documentation accurately describes the new user-only product boundary.
