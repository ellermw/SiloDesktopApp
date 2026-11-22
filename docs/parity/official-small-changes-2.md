# Official small changes, batch 2 (November 22, 2026)

Six small changes the official WebUI made, compared with the native app. Official source: `Silo-Server/silo-server` read at pin
`c90e406db71ee468bffdfa5733cd97d1d50b4019` (read-only copy `D:/SiloPlayer-work/server-upstream-nov14`). Native base: Beta `bdc249d09`.
Versions are unchanged. Nothing was pushed.

| # | Official | Result |
|---|---|---|
| 1 | `2b5425c02` (#5081) red buttons and badges keep the shared focus ring | **Already matches.** Evidence and captures below; one fixture and two source tests added. |
| 2 | `1d6a023cf` (#4646) Enter in the Match Item search fields | **Changed** (one real difference: Enter while the Search button is disabled). |
| 3 | `6156e522c` (#5070) tall alert dialogs keep their actions reachable | **Changed** (body of a tall confirmation was clipped and could not be scrolled). |
| 4 | `381d72003` (#5077) a typed Max runtime stays as typed | **Changed.** |
| 5 | `43fcb5a28` (#4537) a failed preference save shows the action's own message | **Changed** at six native sites; the admin pages **already match in intent**, left alone. |
| 6 | `f7d9edb06` (#5103) plugin `ConfigurationChecks.TestConfiguration` | **Not applicable** (server behaviour only; no contract change native depends on). |

Requests that changed: **none.** No request body, URL, method or retry behaviour was touched by any item.

## 1. Red buttons and badges: shared focus ring (#5081)

Official: `web/src/components/ui/button.tsx:13` and `badge.tsx:14` dropped `focus-visible:ring-destructive/20` from the destructive variants, so
they use the shared ring (4.54:1 on the page, 5.28:1 in high contrast) instead of a 20% red (1.25:1).

Native: already matches, nothing to change.
- `src/SiloPlayer/Themes/DarkTheme.xaml:698` `DestructiveButtonStyle` and its template (`:469` `WebUiDestructiveButtonTemplate`) set
  `UseSystemFocusVisuals=True` and never set a focus brush, thickness, margin or secondary brush; they have no red focus override. The
  other four styles (accent, secondary, ghost, outline) are built the same way. The code-built destructive buttons use the same style
  (`CollectionEditorPage.Departure.cs`) or their own template that also asks for the system visual (`AdminNotificationSettingsPage.Channels.cs`
  `DestructiveConfirmStyle`, `AdminBackupsPage.Dialogs.cs`); neither sets a red ring.
- Badges: native status badges are plain `Border`s (`ExternalTitleCard.StatusBadge`), not focusable, so the badge half has no native counterpart.
- Observed (native fixture `official-small-changes-2`, run `small2-final-r4`): all five styles, focused with the keyboard, report
  `FocusVisualPrimaryBrush #FFFFFF`, thickness 2, secondary `#000000`, margin -3, identical on the destructive button and on the accent,
  outline, secondary and ghost buttons. This is WinUI's own system focus rectangle, the existing native choice
  (`docs/parity/admin-manage-libraries.md` row 24: the 3px 50% ring cannot be matched exactly); it is white on black, not red.
- Captures opened (run `small2-native-new-r4`, display on): `small2-focus-outline-vs-destructive.png` (left: a focused accent button with the white
  double ring; right: the ghost button with the same ring, one focus step behind, see below) and `small2-match-video-after-enter-window.png`
  (a frame in which the red destructive button is focused: the same white outline around the red fill). In the final run the 800x500
  confirm capture shows the focused red Delete button with the same white outline.
- Added: `OfficialSmallChanges2SourceTests.NoDestructiveStyleOrTemplateOverridesTheSharedFocusVisual` (fails if a focus visual setter is added to the
  destructive style or template), and the fixture's per-style property comparison.
- Not verified: pixel contrast of the ring (the final run did not present the button strip, see "Verification limits"); the white ring on the
  page is far above 3:1 by construction.

## 2. Match Item dialog: Enter runs the search (#4646)

Official: `web/src/components/MatchItemDialog.tsx:325` wraps the fields, library picker and Search button in a `<form>`; `:330` the submit
handler does nothing when `searchDisabled` (`:172`: a search is pending, `searchBlocked`, or the hints load). The rest of the 115/109-line
diff is re-indentation; the Add and Remove provider ID buttons stay `type="button"`. No change to focus after a search, the empty or error
states, or the Search button's disabled rule. Nothing else in the diff needed porting.

Native: `Controls/MatchItemDialog.xaml` gives the title, year, IMDb, TMDB and TVDB boxes (and the generic title and year) `KeyDown`, and
`MatchItemDialog.xaml.cs:667,679` the dynamic provider and provider-ID boxes; the dialog has `DefaultButton="None"` and the handler marks Enter
handled, so Enter never closes the dialog or presses another button, and the IME guard (#4287) is kept. `DoSearchAsync` ignores a second
call while a search runs. **One difference:** Enter did not check the Search button's state. While the hints were loading the button was disabled, yet
Enter waited for the hints and then started the search the admin had not asked for.

Changed: `MatchItemDialog.xaml.cs` `SearchFromEnter()` (`:744`) returns when `SearchButton.IsEnabled` is false, so Enter follows exactly the
button's disabled conditions (hints loading, running, applying, unconfirmed apply, library scope).
- **Proved against the old code:** run `small2-native-old-r2` (old Enter path, otherwise the same dialog):
  `FAIL: Match: that Enter is not queued to start a search when the hints arrive`. Fixed code, `small2-native-new-r3` and `small2-final-r4`: passes.
- Checks that already held and now stay guarded (14 real Enter key messages delivered to the focused fields): Enter in title, year, IMDb, TMDB,
  TVDB sends one search with that field's text; in the generic dialog Enter in title, year, provider and provider-ID sends the provider
  identifiers; further Enter presses and the Search button while a search runs add no second request; with a candidate chosen Enter
  searches again and never presses Apply Match; the dialog stays open; Add provider ID searches nothing.
- Added: `MatchItemDialogParitySourceTests.EnterInEverySearchFieldRunsTheSearchOnlyWhileTheSearchButtonIsEnabled`.
- Capture opened: `small2-match-video-after-enter.png` (video dialog after Enter in the five fields: typed values, Search button, one result).
- Request changes: none (the search body is built by the unchanged `DoSearchAsync`).

## 3. Tall confirmation dialogs (#5070)

Official: `web/src/components/ui/alert-dialog.tsx:58` caps the dialog's height (`max-h-[calc(100dvh-4rem)]`) and scrolls, so the title and the
Move and Cancel buttons stay reachable on a short screen; `ConfirmDialog.tsx:115-128`, `DeleteCollectionDialog.tsx` and `SaveFirstDialog.tsx`
stack and wrap the footer on a narrow screen.

Native: every confirmation is a `ContentDialog` on the shared template in `DarkTheme.xaml` (`WebUiContentDialogStyle`, `:734`). Its command
buttons sit outside the scrolled region and the dialog is bounded by the window, so the **actions** were already on screen and focusable at
800x500 and 560x420 for: a plain destructive confirm with a long title and a 70-line body, `CollectionDeleteDialog` (also after its error line
grew to many lines), and `AdminSettingsUi.ConfirmDiscardAsync` (actions inside its content). **The defect:** the template's content scroller had
`VerticalScrollBarVisibility="Disabled"`, so a body taller than the window was only clipped (measured `ScrollableHeight` 0, the 14th line cut
off, nothing could scroll to the rest), run `small2-native-old-r2`: `FAIL: shared confirm 800x500/560x420: the long body scrolls (0 px to scroll)`.

Changed (shared place, as asked; no page file touched, `AdminSettingsUi.cs` untouched): new `Helpers/DialogBodyScroll.cs` attached property set on the
shared style (`DarkTheme.xaml:736`). On open it turns the content scroller on for a dialog whose content has no scroller of its own
(editor and picker dialogs, which size themselves, keep the old behaviour), keeps it on if the dialog resets it, and makes the scroller a
keyboard stop only while there is something to scroll to.
- Observed (`small2-final-r4`): body scrolls (933 px at 800x500, 1013 px at 560x420), the end can be scrolled to, the scroller takes keyboard
  focus, Delete and Cancel stay inside the window before and after scrolling and take keyboard focus; the discard confirmation's body
  fits and adds no stop; the collection delete confirmation keeps both buttons inside the window with a long error.
- Captures opened: `small2-confirm-shared-800x500-window.png` and `-560x420-window.png` (title, a scrollbar on the right, the red focused Delete
  and Cancel under the clipped body). The collection-delete and discard window captures in the same folder are stale copies of the first
  frame (the hidden window did not present a new frame), so those two dialogs are evidenced by the measured bounds only.
- Added: `OfficialSmallChanges2SourceTests.TheSharedDialogStyleLetsATallBodyScrollUnderItsActions`; native checks in `official-small-changes-2`.
- Not changed, on purpose: the footer stacking and label wrapping the official added for 320 px phones (native dialogs have a minimum window
  width, and `EditorDialogPresentation.ReflowCommands` already stacks the editor dialogs' buttons when narrow).
- Not verified: dialogs that other open pull requests own were not opened at a short window; they use the same template.

## 4. Scheduled Tasks trigger form: Max runtime stays as typed (#5077)

Official: `web/src/pages/AdminTaskTriggerForm.tsx:106` `maxRuntimeToDraft` (stored limit in minutes rounded to five decimals), the box keeps the
typed text, a negative entry shows 0, `:253` `onBlur` shows 0 for an entry that rounds to no limit. Test: `AdminTaskDetail.v2.test.tsx`
"keeps a typed Max runtime as typed, and shows a stored one rounded".

Native: `AdminTaskScheduleDraft` already kept the typed text, but showed a stored limit as `((double)ms/60000).ToString("R")` (74075 ms became
`1.2345833333333334`; 1 ms became `1.6666666666666667E-05`), did not show 0 for a negative entry, and had no blur rule.

Changed: `Core/Services/Admin/AdminTaskScheduleDraft.cs` `RuntimeText` (five decimals, plain digits), `SetRuntime` (negative shows and sends 0),
new `LeaveRuntime`; `Views/Admin/AdminScheduledTasksPage.Schedule.cs` calls it on `LostFocus` and puts the caret at the end when it rewrites the box.
- **Proved against the old code:** run `small2-draft-old-r1` (new tests, old behaviour): 6 failures, including the ported official test
  (`Expected "1.23458", Actual "1.2345833333333334"`). Fixed code: all pass.
- Ported official cases (all in `AdminTaskScheduleDraftTests`): stored 74075 shows 1.23458 and still sends 74075 untouched; `-5` shows 0 and is 0 ms;
  `0.000008` stays while typing and shows 0 after leaving; typing `1.23456` one character at a time keeps every prefix and saves 74074 ms; plus theories
  for the rounding round trip, `-0`, `0.0`, `-0.0000001`, an empty box and malformed text, and "leaving an untouched box changes nothing".
- Native check (`admin-configure-scheduled-tasks`, same run): the real editor shows a stored 74075 ms limit as 1.23458, `-5` becomes 0, `0.000008`
  becomes 0 on leaving, typing 1.23456 keeps each prefix, ends at 74074 ms. Capture opened: `tasks-schedule-max-runtime-560.png` and `-1024.png`
  (two trigger rows: Max runtime 1.23458 minutes stored, and 1.23456 typed).
- Request changes: none. An untouched editor still sends nothing; the saved body for 1.23456 is `max_runtime_ms` 74074, as the official test expects.

## 5. A failed preference save shows the action's own message (#4537)

Official: `readableErrorText` (`web/src/api/v2/request.ts:358`) keeps an `Error`'s message except developer English (failed fetch, timeout,
non-JSON body, stale context) and uses the action's own message instead; `toastSettingWriteError` (`settingWriteOffline.ts:61`) is used by the
admin user setting and device override saves and resets, the subtitle and audio preference saves and resets; the subtitle download, upload,
detect and search messages use it too. `transportFailureToasts.test.tsx` lists the cases.

Native counterpart: new `SiloPlayer.Core.Localization.ReadableErrorText.Get(exception, fallback)` (developer English = `HttpRequestException`,
cancellation and timeouts, `SocketException`, `IOException`, `JsonException`, and an `ApiException` that is only an "HTTP 502" status line; a server
problem detail keeps its text through `ApiProblemText`). Unit tests: `ReadableErrorTextTests` (the official's four developer-text cases plus the
native ones, problem details kept, purposeful errors kept, empty text, and a source guard that the old raw-message lines are gone).

| Official action (message) | Native site | Before | Now |
|---|---|---|---|
| Audio preference save / reset ("Failed to save/reset audio preference") | `ItemDetailPage.xaml.cs` `SelectPrePlayAudioAsync` (`:5578`) | `ex.Message`; a timeout (an `OperationCanceledException`) was swallowed | Readable text with the catalog fallback; only the page's own cancellation is silent |
| Subtitle preference save / reset ("Failed to save/reset subtitle preference") | `ItemDetailPage.xaml.cs` pre-play subtitle save and reset (`:6166`, `:6194`) | `ex.Message`; timeouts swallowed | same |
| Subtitle upload ("Upload failed") | `SubtitleSearchDialog.xaml.cs` upload (`:410-411`) | `"Upload failed: " + ex.Message` | Readable text, fallback "Upload failed" |
| Subtitle language detect ("Failed to detect subtitle language") | `SubtitleSearchDialog.xaml.cs` (`:347`) | `"Language detection failed: " + ex.Message` | Readable text, fallback from the catalog |
| Subtitle search / download (item detail and player) | `SubtitleSearchDialog.xaml.cs` search and download errors | `ex.Message` | Readable text; fallback by mode (`web.subtitles.search.*` in the player, `web.detail.subtitleSearch.failed` and `web.common.toast.subtitles.downloadFailed` on the item page) |
| Player subtitle choice save | `PlayerService.cs:1801` | already the fixed message "Failed to save subtitle preference" | already matches |
| Admin user setting save / reset, device override save / reset, device reset-all | `Admin/Users/AdminUsersPage.cs:193` `ShowError`; `AdminUserResourceText.Failure` (`:67`), `AdminDevicesPage.Detail.cs:330` | a transport failure already shows a fixed native sentence ("The request failed or its outcome is unknown. Read back and review..." / "The request did not finish. Check the connection and reload."), never the exception text | **Already matches in intent, not changed.** The wording differs from the official one-liner by design (a failed write after sending is an unknown outcome that tells the admin to reload), and `AdminUserResource*.cs` belongs to an open pull request. The reset-all message therefore ends with that sentence where the official one ends at the count. |
| Audio preference save in the player | `PlayerService.PersistAudioPreferenceAsync` (`:9223`) | logs only; the official toasts | **Not changed**: adding a toast there is a new behaviour, not part of #4537 |
| Offline notice shown once for a burst of saves | none | native has no online manager or offline refusal | **Not applicable** |

Proved against old code: not run for this item (the helper is new; the unit tests prove the mapping, the source guard proves the old lines are gone). The
timeout-no-longer-silent change in `ItemDetailPage` is covered by the source guard only.
Request changes: none.

## 6. Plugins: `ConfigurationChecks.TestConfiguration` (#5103)

Official change: server only (Go: `internal/plugins/service_connection.go`, `internal/pluginhost/client.go`, docs, OpenAPI description). A plugin whose
manifest declares `configuration_check: true` is checked by calling its own `TestConfiguration` with every global config entry (the staged one
in place of its saved value); it takes precedence over the built-in probes. `connection_check_supported` is true for such a plugin. The web
change is one description line in `schema.ts:20606`. `POST /api/v2/admin/plugins/installations/{id}/config/test` keeps its request body and its
answer (`success`, `message`); a failed check is still a 200 with `success` false.

Native: no change needed, no code changed. `AdminPluginsPage.Detail.cs:369` offers the test only when the server's `connection_check_supported`
is true (so a self-checking plugin is offered it as soon as the server says so); `AdminPluginsApi.TestConfigAsync` (`:195`) posts the unchanged
`AdminPluginConfigWrite` and reads `AdminPluginCheck`; `AdminPluginsController.TestConfigAsync` shows the server's message for a completed
failed check and treats a transport failure as an unknown outcome (`:371`). The call is already non-retryable and carries staged secrets.
Not verified: no live server with a self-checking plugin; `admin-plugin-apps` (embedded browser) was not run.

## Verification

Final run `small2-final-r4` on commit `937547f00` (see `docs/current-development.md`): focused unit 110 passed, full unit 12,044 passed, 0 failed; app
build, publish and native host build passed; native selectors `official-small-changes-2`, `admin-configure-scheduled-tasks`, `match-split-scope`,
`conditional-editors`, `native-subtitle-dialogs`, `shared-destructive-theme`, `shared-controls`, `admin-manage-foundation`, `admin-monitor`
all passed.

Verification limits, recorded as found:
- `conditional-dialogs` was left out: it fails at `ConditionalDialogsNativeFixture.cs:35` with "No service for type SiloApiClient has been
  registered" because the fixture builds `MatchItemDialog` without that service; the constructor line that asks for it
  (`MatchItemDialog.xaml.cs:76`) is unchanged by this work (the diff to Beta touches only the Enter path).
- In the final run the hidden window presented no pixels for its page content (the button strip; dialog frames were presented), so it could not sample the rendered focus ring; the
  fixture then compares the focus properties only and says so. The ring pictures above come from the earlier run `small2-native-new-r4`, where
  the hidden window presented frames one focus step late (the focus visual of the previous button appeared in the capture), so a
  "focused outline" image shows the previous button. They show the ring on each style, not an exact side-by-side of two simultaneously focused buttons
  (only one element can hold focus).
- Pixel capture of dialogs from the hidden window repeats the first frame for later dialogs in the same run; geometry was measured from the layout tree instead.
- A real Tab walk through the dialogs was not run; keyboard reachability was checked with `Focus(Keyboard)` on each command and the scroller, and the Enter key
  with posted key messages.
