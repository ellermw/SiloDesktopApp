# IME composition guard: viewer-surface follow-up

Builds on PR #195 (`docs/parity/2026-11-16-ime-composition.md`), which added
`ImeCompositionState` (Core) and `ImeCompositionGuard` (WinUI) and applied them
to global search, the Search page, the collection editor's manual search and
the metadata tags input. This pass finishes the viewer-facing part of official
Silo Server #4287 (`9eb3df6f3`) and the follow-ups that document listed.

Baseline: PR #195 head `ee9a858d5` on Beta `212333915`. Admin surfaces
(`Admin*`, `admin-*`: AdminSectionCommandDialog, FolderBrowser,
AdminAccessGroups, PolicyDocumentList, RequestRuleConditions) are outside this
user-only client and are not touched.

## Rule applied

A site needs the guard only when a **custom** handler acts on a key from a text
input: a `KeyDown`/`PreviewKeyDown`/`AddHandler` that submits, searches, joins,
selects, navigates or commits on Enter, arrows, Escape or a comma. Native
`ComboBox`, `AutoSuggestBox`, `Slider` and `ContentDialog` default buttons
already respect IME composition and need nothing. Where a guard is added it
follows PR #195 exactly:

- `ImeCompositionGuard.Attach(textBox)` once, in the constructor (or where the
  box is created), so the tracker exists before any composition can start.
- `if (ImeCompositionGuard.Attach(box).ShouldIgnore(e)) return;` as the first
  statement of the handler. An ignored Enter or Escape is marked handled, so a
  parent `ContentDialog` cannot take it for its default or close action.
- Shared handlers whose `sender` may be a `PasswordBox` (login, signup) guard
  `sender is TextBox box && ...`. `PasswordBox` has no composition events and
  IME is normally off in password fields.

## Inventory: upstream #4287 viewer surfaces

| Upstream surface | Desktop equivalent (file:line) | Custom key handler? | Result |
| --- | --- | --- | --- |
| `collections/fields/ArtworkFields` | `CollectionEditorPage.Presentation.cs` artwork controls (pickers and buttons; no text box). Name and description boxes: `CollectionEditorPage.xaml:229`, `:245` | No | Not applicable (no text-commit handler) |
| `collections/fields/OrderBlock` | `CollectionEditorPage.xaml:277` `ImportedDefaultSortCombo` and the `ComboBox` sort/direction pickers (`CollectionEditorPage.SyncedPresentation.cs:160`) | No (native `ComboBox`) | Not needed (native control) |
| `settings/LanguageSelect` | `SettingsPage.xaml:1116` `SpokenLanguageComboBox`, `:1128` `MetadataLanguageComboBox` | No (non-editable `ComboBox`) | Not needed (native control) |
| `settings/SubtitleAppearancePanelView` | `Controls/SubtitleAppearanceDialog.xaml` (sliders, combos, buttons only) | No | Not needed (native controls) |
| `ui/facet-search-select` | `QueryRulesEditor.cs:403` (`AutoSuggestBox`, `TextChanged` fetch with `UserInput` reason); `LibraryPage.xaml:698-752` person/series facet `AutoSuggestBox`es | No (`TextChanged` fetches suggestions; `QuerySubmitted` is native) | Not needed (native control) |
| `ui/facet-value-picker` | `QueryRulesEditor.cs:428-431` (`AutoSuggestBox` + `QuerySubmitted`); `QueryFilterEditor.cs:290`, `:337` | No | Not needed (native control) |
| `ui/person-search-select` | `QueryRulesEditor.cs:353` `AutoSuggestBox`; `LibraryPage.xaml:698-717` `ActorBox`/`DirectorBox`/`WriterBox`/`ProducerBox` | No | Not needed (native control) |
| `ui/searchable-select` | `LibraryPage.xaml.cs:1486` option-list filter box (`TextChanged` re-renders the list; no Enter or arrow handler); `RecipeGalleryDialog.cs:412` and `:903` filters | No | Not needed (no key handler; filtering is idempotent) |
| `settings/DeviceSettings` (rename) | `SettingsPage.Account.cs:1018` rename `TextBox` with Save/Cancel buttons; `:44` device search | No (Enter does not commit; the Save button does) | Not applicable (no handler) |
| `settings/NotificationsSettings` | `NotificationSettingsControl.xaml:107` email `TextBox`, handler `NotificationSettingsControl.xaml.cs:149` (Enter submits the address); `NotificationSeriesControl.xaml:12` series search (`TextChanged` only) | Email: yes. Series search: no | **Guarded** (email). Series search not applicable |
| `settings/SubtitleAppearanceSettings` | `SubtitleAppearanceDialog` (above) and the settings page subtitle panel | No | Not needed (native controls) |

## Inventory: PR #195 follow-ups

| Follow-up | Desktop equivalent (file:line) | Custom key handler? | Result |
| --- | --- | --- | --- |
| Requests search | `RequestsPage.xaml.cs:44` attach, `:214` guard in `SearchBox_KeyDown` | Yes (Enter starts the search) | **Guarded** |
| Watch Together join | `WatchTogetherJoinPage.xaml.cs:36` attach, `:324` guard in `RoomCodeBox_KeyDown` (Enter joins) | Yes | **Guarded** |
| Watch Together room host search | `WatchTogetherRoomPage.xaml.cs:77` attach, `:817` guard in `HostSearchBox_KeyDown` | Yes (Enter searches) | **Guarded** |
| Login | `LoginPage.xaml.cs:25` attach `UsernameBox`, `:128` guard in `InputBox_KeyDown` (Enter signs in) | Yes | **Guarded** (username `TextBox`; `PasswordBox` ignored) |
| Signup | `SignupPage.xaml.cs:22` attach `UsernameBox`, `EmailBox`, `InviteCodeBox`, `:102` guard in `InputBox_KeyDown` | Yes | **Guarded** (`TextBox`es; password boxes ignored) |
| Device activation code | `ActivateDevicePage.xaml.cs:37` attach, `:99` guard in `CodeBox_KeyDown` (Enter submits the code) | Yes | **Guarded** |
| Invite claim | `InviteClaimPage.xaml.cs:119` `Input_KeyDown` on two `PasswordBox`es only (email box is read-only) | Yes, but no text box | Not applicable (no composing text box) |
| Match dialog | `MatchItemDialog.xaml.cs:103` attach the seven XAML boxes, `:666`/`:678` attach the dynamic provider boxes, `:731` guard in `SearchField_KeyDown` (Enter searches) | Yes | **Guarded** |
| Allowed titles | `AllowedTitlesSection.cs:47` `_search.KeyDown` only marks Enter handled so the profile form does not Save; it never submits or searches | Yes, but swallow-only | Not applicable (cannot act on a composing key; the debounced `TextChanged` search is unchanged and matches upstream) |
| Collection group name: add | `CollectionsPage.xaml.cs:38` attach, `:873` guard in `AddGroupNameBox_KeyDown` (Enter adds, Escape closes) | Yes | **Guarded** |
| Collection group name: rename | `CollectionsPage.xaml.cs:577` attach, guard first in the `renameBox.KeyDown` lambda (Enter saves, Escape cancels) | Yes | **Guarded** |
| Ebook reader book search | `EbookReaderPage.xaml.cs:76` attach, `:1108` guard in `SearchBox_KeyDown` (Enter searches). `Page_KeyDown` already ignores `TextBox` sources | Yes | **Guarded** |
| Collection limit boxes | `CollectionEditorPage.Smart.cs:51`, `CollectionEditorPage.Synced.cs:106`, `CollectionEditorPage.SavedSynced.cs:91` (Enter commits a limit) | Yes | Not applicable (numeric limit; IME is off for digits in practice, as PR #195 recorded). Left unguarded on purpose; revisit if full-width digit entry is reported |
| Settings search boxes | `SettingsPage.xaml:126` and `MainWindow.xaml:145` | `TextChanged` filter only | Not applicable (no key handler) |

Other `KeyDown` handlers in the app (cards, carousels, rails, player overlay,
playback menus, tabs, letter strip, star rating, drag handles) are on non-text
elements and out of scope.

## Files changed

Source (guard added, same pattern as PR #195):

- `src/SiloPlayer/Controls/MatchItemDialog.xaml.cs`
- `src/SiloPlayer/Controls/NotificationSettingsControl.xaml.cs`
- `src/SiloPlayer/Views/ActivateDevicePage.xaml.cs`
- `src/SiloPlayer/Views/CollectionsPage.xaml.cs`
- `src/SiloPlayer/Views/EbookReaderPage.xaml.cs`
- `src/SiloPlayer/Views/LoginPage.xaml.cs`
- `src/SiloPlayer/Views/RequestsPage.xaml.cs`
- `src/SiloPlayer/Views/SignupPage.xaml.cs`
- `src/SiloPlayer/Views/WatchTogetherJoinPage.xaml.cs`
- `src/SiloPlayer/Views/WatchTogetherRoomPage.xaml.cs`

Tests:

- `tests/SiloPlayer.Tests/ImeCompositionParitySourceTests.cs`: the two existing
  theories gain a row per new handler (guard consulted first; tracker attached
  in the constructor before the handler exists), plus
  `GroupRenameBoxIsTrackedBeforeItsKeyHandlerIsRegistered` and
  `MatchDialogTracksTheDynamicProviderBoxesBeforeTheirHandlers`.

No change to `ImeCompositionState` or `ImeCompositionGuard`.

## Manual IME checks

With a real Japanese IME and Chinese Pinyin, for each guarded field:

1. Type a reading, press Enter to confirm the candidate: nothing is submitted,
   searched, joined, added, renamed or saved; any parent dialog stays open.
2. Press Enter again: the action runs exactly once.
3. Candidate arrows pick a candidate and do not move other selection.
4. Escape cancels only the composition. In the match dialog, add/rename group
   and notification email it must not close the dialog or cancel the edit.

Fields: Requests search; Watch Together room code and host search; sign-in
username; signup username, email and invite code; device activation code; match
dialog title, IDs and provider fields; notification email address; collection
group add and rename; ebook book search.

## Verification status

Not built or run in this worktree (the lead verifies under the shared lock).
The unit filter and native selectors are in the hand-off message.
