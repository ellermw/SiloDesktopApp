# Small official changes since the pages were compared: November 22, 2026

Branch `t3/official-small-changes-nov22` (from Beta `8c201c07d`; no version number changed). Official reference: `Silo-Server/silo-server` commit
`93794454b84aa818979bee19b8e192944395635a`, read with `git show` / `git grep` from the clone `D:/SiloPlayer-work/server-upstream-nov14`. Each section names
the official commit, what it changed, what the native app did before, what it does now (or why nothing changed), the request changes and what was not
verified. **No request body, URL or write behaviour changed anywhere in this record**; the only requests involved (restore-set reads, device and account
setting writes, subtitle and audio preference writes) are sent exactly as before.

| # | Official | Result |
| --- | --- | --- |
| 1 | `fd4282d0a` #4667 backup restore keeps what the environment sets | Changed |
| 2 | `5641447d6` #4741 wording for the exact number of items | Changed |
| 3 | `6f0cdab81` #5079 "By worker" chips read as separate words | Changed |
| 4 | `2c75019cb` #5074 item type in the interface language (Editor's Picks title search) | Not applicable |
| 5 | `3fd01aa66` #4534 offline setting and track-preference saves fail instead of being replayed | Already matches (no queue or replay exists) |
| 6 | `63bbb96ae` #5080 black watched check; `7936acbf2` #5083 placeholder bleed | #5080 changed; #5083 not applicable |
| 7 | Scaled-display follow-up: `SharedNamesRow` | Changed |

## 1. Backups: settings the environment sets stay out of a selective restore (`fd4282d0a`, #4667)

Official: the server now answers a setting the environment sets (or that belongs to an outside-service connection the environment sets part of) with status
`managed_by_environment` instead of refusing the whole page, and the item with reason `managed_by_environment`. The web restore flow shows
`settingValues` for it ("`{value}` (kept: environment)" in Now, the backup's value, or "Different (encrypted)" when masked; `RestoreRows.tsx:96-106`), the
blocked heading and page reason (`restoreGroups.ts:117`, `restoreModel.ts:54`), the review caveat `caveat.environmentSettings`
(`restoreModel.ts` `restoreCaveats`, after "stricter"), and shows the Restart badge only on a setting a restore writes: `differs` or `cleared`
(`RestoreRows.tsx:234-236`). No official test was added in `web`; the server tests (`internal/backup/selective_*_test.go`) are not part of this app.

Native before: the catalog already held `reason`, `blocked`, `caveat` and `setting.managedByEnvironment`, and the reason sentence and blocked heading
already resolved by the code (`AdminBackupRestoreModel.KnownReasonSentence`, `BlockedTitleKey`; test `AReasonCodeWithNoSentence…`). The status itself was
unhandled: a managed setting row fell through to the generic Now/Backup cells (so it read as a change that would be written), the review gave no caveat
for it, and every setting with `restart_required` carried the restart marker whatever its status.

Native now:

- `AdminBackupRestoreModel.SettingValues` (`AdminBackupRestoreModel.cs`, new `managed_by_environment` case): Now = the catalog "`{value}` (kept: environment)"
  with the current value, or "Set" when masked; In backup = the backup's value, or "Different (encrypted)" when masked.
- `AdminBackupRestoreModel.Caveats`: a "environment" caveat with `caveat.environmentSettings` and the count, in the official order (after stricter).
- `AdminBackupRestoreModel.ShowsRestartMarker`: only `differs` or `cleared` rows (and only with `restart_required`); `AdminBackupsPage.Restore.cs` uses it for the
  "↻" on a setting key. The item-level "↻ restart" line is unchanged (it follows the server's `restart_required`, as official `RestoreRows.tsx:315,368`).
- The response validation (`AdminBackupValidation.Require`) never listed setting statuses, so the new value does not fail the preview.

Request changes: none. Tests (`tests/SiloPlayer.Tests/AdminBackupsEnvironmentAndAmountTests.cs`):
`AManagedSettingKeepsItsCurrentValueAndSaysTheEnvironmentSetsIt`, `AManagedMaskedSettingShowsSetAndNeverAValue`, `TheReviewNamesTheSettingsTheEnvironmentKeeps`,
`OnlyASettingARestoreWritesCarriesTheRestartMarker` (7 cases), `AnItemThePreviewBlocksForTheEnvironmentReadsItsOfficialSentenceAndHeading`.
Not verified: the restart marker's one-line use on the page and the managed row on screen have no native check (the Backups native fixture has no settings
page with a managed row; the pure model carries the logic); a real server answering `managed_by_environment` was not available.

## 2. Backups: wording chosen for the exact number of items (`5641447d6`, #4741)

Official: `exactAmount(count)` (`i18n/format.ts:226-234`) gives `exactlyOne` / `exactlyTwo` / `other`; seven messages moved from a plural on `count` to a
select on `amount`: `restore.choose.hideThem`, `restore.choose.showThem`, `restore.notice.alsoChose`, `restore.notice.alsoUnchose`,
`warnings.dataUnreadable`, `warnings.partUnreadable`, `warnings.tooLarge` (callers: `RestoreChoose.tsx:301,369,500`, `restoreModel.ts:293,365`,
`warningDetails.ts:45-50`; the older local `amount()` in `BackupWarningDetails.tsx` was replaced by it). The commit also added ten
`warnings.missingFiles.*` messages that no code at the pin uses; they were not added.

Native before: the app already had the exact-amount idea for the six toggle labels of the Backups list (`AdminBackupsPage.List.cs` `Amount`), but the seven
messages above were still `{count, plural, one … other …}` and their callers passed `count`.

Native now: one helper in the app's Backups text class, `AdminBackupText.ExactAmount(count)` and `AdminBackupText.Amount(count, extra)` (same
`AdminText` ICU subset, `select` on `amount`); the seven catalog entries are the official text verbatim (rendered from the official compiled messages);
every caller passes `Amount(...)`: `AdminBackupRestoreModel.Choose/Unchoose` (the two notices), `AdminBackupPresentation.PlainSummary` (the three omission
summaries), and the three restore toggle labels in `AdminBackupsPage.Restore.cs`; the list page's own `Amount` now delegates to the helper. No English
sentence is built by hand.

Request changes: none. Tests: `TheChangedStringsAreTheOfficialTextVerbatim` (the seven messages against the official AST at the pin),
`TheAmountOfAMessageIsTheExactCountNeverAPluralCategory` (0, 1, 2, 3, 21, 101), `TheRestoreShowAndHideLabelsSelectOnTheExactAmount`,
`AutomaticallyChoosingOneOrSeveralRequirementsAgreesWithTheirNumber`, `AnOmissionSummaryAgreesWithTheNumberOfOmittedItems` (6 cases); the existing
`RestoreToggleLabelsAgreeWithTheNumberOfItemsTheyCover` was moved to `Amount`. The older verbatim-strings test (`EveryBackupsStringThatChangedSinceThePinIsInTheCatalogVerbatim`, official text at `4a1ee058e`) held the old plural form of `hideThem` and `showThem`; those two entries were removed from its data (51 to 49 keys) because the new test checks them against the newer official text. Not verified: non-English languages (the Backups catalog is English-only in
the app, so there is no translation to exercise; 21 and 101 are asserted as `other`).

## 3. Downloads > Preparation: "By worker" chips as separate words (`6f0cdab81`, #5079)

Official: the chip's label, name and count are separated by real spaces so the accessible name is "Node gpu-01 2" (`PreparationTab.tsx:265-276`).

Native before: the chip is a `ToggleButton` whose three parts are separate text blocks inside a 6 px inline panel (visible spacing is fine), but its
automation name was set once to `label + " " + name` at creation: it had no count and did not follow a renamed worker.

Native now: `AdminPreparationPresentation.WorkerChipName(worker, count)` = "`Label Name Count`" (count in the same digits as the visible count);
`AdminPreparedFilesPage.Preparation.cs` sets it whenever the name or count changes. Visible spacing is unchanged (6 px between the parts, within a
pixel or two of the official `me-0.5` + space and `ms-1` + space).

Request changes: none. Tests: `AWorkerChipIsAnnouncedAsSeparateWordsWithItsJobCount` (3 cases); native check in `admin-prepared-files`: the chip for the fixture
worker reads "Node synthetic-node 1". Not verified: an actual screen reader.

## 4. Editor's Picks title search: item type in the interface language (`2c75019cb`, #5074)

Official: `CuratedTitlesEditor.tsx:132-151` shows each result as "year · type" (and the bare type without a year) and now runs the type through
`itemTypeLabel`; before it printed the API value (`movie`, `series`).

Native: **not applicable.** The native Editor's Picks editor (`AdminHomeSectionsPage.Sources.cs` `CuratedEditor`/`ChoicePicker`) lists each result by its title
only (`AdminHomeSectionsPage.Sources.cs:341`, `AdminHomeSectionChoice.Label` = title, `AdminHomeSectionsModels.cs:79`); `AdminHomeSectionChoice.Type` is never rendered,
so there is no untranslated type to translate. (Neither the year nor the type line the official result shows is present; that is a visual gap, not this
defect, and nothing was invented for it.) The native kind is also not offered for new rows (`AdminHomeSectionsKinds.cs:40`), as at the pin
(`catalog.test.ts:60`). Observation, not changed (the file belongs to another open pull request): `CollectionEditorPage.TitleSearch.cs:55-57` shows the raw `item.Type`
for types other than movie, series, season and episode (for example `ebook`); that is the collection editor's own title search, which #5074 does not touch.

Request changes: none. Not verified: n/a.

## 5. Offline setting and track-preference saves (`3fd01aa66`, #4534)

Official: five admin user setting hooks (`hooks/queries/admin/users.ts`) and the subtitle and audio preference hooks (`subtitles.ts`, `trackPreferences.ts`)
now use `settingWriteOptions` (`settingWriteOffline.ts`): TanStack Query's default would pause an offline mutation and run it on reconnect, replacing what
another browser saved meanwhile; now an offline write is refused before it is sent with the localized "You're offline, so this change wasn't saved", and
`usePrePlayTrackSelection.ts` shows an earlier pick that lands after a refused one.

Native: **already matches in the sense of the change: there is no queue, no pause and no replay of any of these writes.** There is no
offline-mutation mechanism at all (no persisted outbox, no network-state gate, no reconnect trigger). `SiloApiClient.SendWithRetryAsync`
(`SiloApiClient.cs:911-1030`) re-sends only once after a 401 and a token refresh (a request the server rejected unauthenticated), never after a transport failure.
For the four official call sites:

1. **Admin user setting and device override writes.** Account settings: `AdminUsersApi.cs:167-171,210` sends one `PUT`/`DELETE` after re-reading and comparing
   its baselines; an answer lost in transit ends as the "write outcome is unknown, reload and choose again" review state, never a re-send. Device overrides:
   `AdminUserResourcesApi.Devices.cs:38-62` (`SetDeviceSettingAsync`, `DeleteDeviceSettingAsync`, `ResetDeviceProfileAsync`) send once; a failure or lost
   answer goes through `AdminUserResourceController.TryWriteAsync` (`AdminUserResourceController.cs:70-97`), which shows the failure, blocks further
   writes until the page is reloaded when the outcome is unknown, and does not re-send. The controls follow what the server holds after the write.
2. **Subtitle preference** (set/delete): `PlaybackApi.TrackPreferences.cs:5-30` sends one request through a one-at-a-time semaphore; the pre-play
   caller (`ItemDetailPage.xaml.cs:6126-6192`) updates the detail's effective values only after a confirmed save and otherwise shows a failure toast (the
   exception's own message, or "Failed to save subtitle preference"); the in-player caller (`PlayerService.cs:1794-1801`) shows "your preference could not
   be saved. Select the subtitle again to retry." Nothing is retried.
3. **Audio preference** (set/delete): `CatalogApi.TrackPreferences.cs:5-28`, same single request behind a semaphore; the in-player caller
   (`PlayerService.cs:9213-9224`) logs a failure and sends nothing again.
4. **Pre-play track selection**: `PrePlayAudioSelection.SelectAsync` (`PrePlayAudioSelection.cs:24-52`) processes choices strictly in click order behind one
   semaphore and, when the newest choice fails, puts the selector back to the last *confirmed* index; because it waits for the earlier request to settle
   first, an earlier pick can never land after a refused one, which is the case the official `rolledBackChoiceRef` handles. A choice waits only for the
   earlier request of the same session (bounded by the HTTP timeout) and is cancelled when the page is left (`ItemDetailPage.xaml.cs:5569`); it is not kept
   for a later connection.

Differences left as they are (not defects of this change): the native app does not know it is "offline" for these writes, so a failure shows the
transport or server text instead of the official "You're offline" notice (`web.settings.offlineChangeNotSaved`), and the in-player audio failure is only logged.

Request changes: none. Tests: none added (no behaviour changed). Not verified: a real disconnect during a save.

## 6. Viewer: black watched check (`63bbb96ae`, #5080) and poster placeholders (`7936acbf2`, #5083)

**#5080.** Official: the poster card's watched check is black on `bg-emerald-500` (`CardWatchStatus.tsx:53-62`; the remaining-episodes count stays white on
its dark circle) and the series page's completed-season check is black on `bg-green-500/90` (`SeasonCarousel.tsx:80-84`).

Native before: `CardWatchStatusIndicator` drew the "✓" in white on the emerald (#10B981) circle for every poster and library card, and the season poster's
completed badge (`ItemDetailPage.xaml.cs` `UpdateSeasonPosterState`) drew a white glyph on its 90% green circle.

Native now: `CardWatchStatusIndicator` sets the text colour with the state: black when played, white for the count. The season badge moved to
`SeasonCompletedBadge.Create()` (`src/SiloPlayer/Controls/SeasonCompletedBadge.cs`: same size, position and fill as before, black glyph). The episode caption's small
outline "watched" mark (`EpisodeCardCaption.cs`) and the landscape card's inline mark are not the filled circle the official change touches and are unchanged.
Native check in `overlay-revisions`: a played poster's status text is black, a remaining-episodes poster's white, and the season badge's glyph black.
Not verified: the contrast ratios were taken from the official commit message, not measured here; no screenshot comparison.

**#5083.** Official: CSS `background-repeat: no-repeat` on `.media-card-image` and the episode still box so a thumbhash placeholder (an inline `background-size: cover`
that repeats by default) does not tile its opposite edge under the translucent 1 px border.

Native: **not applicable.** Placeholders are decoded bitmaps shown in an `Image` with `Stretch="UniformToFill"` (`PosterCard.xaml:62-66`,
`DefaultArtwork.cs:19`, `ThumbhashBitmaps.cs`); a WinUI `Image` has no repeat, so there is nothing that can tile into the border, and no code paints a placeholder
as a repeating brush. Request changes: none.

## 7. `SharedNamesRow`: scaled-display follow-up (`AdminUserResourceUi.cs`)

Defect: the Add users dialog's two-name row measured both cells a second time at exactly their natural (rounded) widths even when they fit, and compared
"both fit" exactly. A desired width is rounded to the device pixel grid, so on a display scaled to 125% or 150% a name that fits was offered a fraction less than its
text needs and showed an ellipsis. The same defect was fixed in `ShareRowPanel` on `t3/scaled-display-hardening`.

Fix (`src/SiloPlayer/Views/Admin/AdminUserResourceUi.cs`): a cell is measured again only when its share is below its natural width; "both fit" is
`first + second <= room + 1` with `room = available - gap` (a local `FitTolerance = 1`, commented; `LayoutTolerance.Fit` does not exist on this branch). Nothing
else in the row changed, and the file's other panels were left alone.

Native check (`AdminUserResourcesNativeFixture.ScaledDisplay.cs`, run by `admin-user-resources`): lays the real `SharedNamesRow` out in the live window (a
popup on the window's XamlRoot) with probe children that need 100.4 and 60.4 and report rounded desired widths of 100 and 60 (the check first asserts that
precondition), at the exactly-fitting width 172 and the one-pixel-short width 171, and shows neither probe was offered less than it needs and both are
arranged whole; a row of 130 still trims both and keeps them inside. This machine runs at 100% scale; the fraction stands in for the 125%/150% rounding, as
on the hardening branch.

Request changes: none.

## Verification

Acceptance label `small22-final-r14` on commit `254591a7d` (focused 143/143, full 11,681/11,681; native admin-prepared-files, admin-user-resources 619 PASS, overlay-revisions 94 PASS,
admin-manage-foundation 36, admin-monitor 1,071 PASS) and the details of the earlier runs, the two selectors that fail on unchanged beta, the `admin-configure-backups` crash
code and the proofs against the old code are in the entry at the top of [../current-development.md](../current-development.md). Items 5, 4 and 6b changed no code.
