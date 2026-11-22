# Admin Activity and Dashboard Now Playing: an episode is led by its show

Branch `t3/admin-activity-show-title` from `origin/beta` `26f432433`. Official source `c90e406db71ee468bffdfa5733cd97d1d50b4019` (read-only copy at `D:/SiloPlayer-work/server-upstream-nov14`).
Version numbers unchanged. Presentation only: no request, URL, polling, store or realtime behaviour changed (see "Requests").

## The official rule

Commits: `79e62369d` (#5106) leads episode cards with the show title; `72b452646` (#5105) removes the movie subtitle; `c90e406db` (#5109) corrects the test that still expected it.
Files at the pin: `web/src/pages/adminActivityPresentation.ts:703-745` (`isEpisodeSession`, `getPlaybackSessionTitle`, `getPlaybackSessionSubtitle`), `web/src/pages/AdminActivity.tsx`
(`StreamRow` stacked card 916-925, `TitleLink` 1001-1018, `MediaCell` 1021-1033, `getDisplayTitle` 1891-1900 which also feeds the Stream-column sort), `web/src/components/admin/dashboard/widgets/NowPlayingWidget.tsx:227-297`,
test `web/src/pages/adminActivityPresentation.test.ts`.

An *episode session* is one with a non-empty `series_title` and a non-null `season_number` and `episode_number` (`media_type` is not looked at; season 0 and episode 0 count).

| Session | First line (link to `/item/{media_item_id}` when the row names one, else plain) | Second line (muted, one line) |
|---|---|---|
| Episode session with an episode title | the show (`series_title`) | `S{season}E{episode} · {media_title}` (`web.common.activity.subtitle.episodeTitle`) |
| Episode session with no episode title | the show | `S{season}E{episode}` |
| `media_type` series or episode but not an episode session (no show, or a number missing) | `media_title`, else `File #{id}` | `Series` |
| Movie | `media_title`, else `File #{id}` | none (the "Movie" line is gone) |
| Track, audiobook chapter, empty or any other type | `media_title`, else `File #{id}` | none |

- The year is not part of the line: official puts none on either surface.
- Wide Activity row (`MediaCell`) and the Now Playing card show the subtitle whenever there is one; the stacked Activity card (`StreamRow`) shows it only for an episode session, so "Series" is not shown there.
- The Now Playing fallback key is `web.admin.dashboard.widgets.nowPlaying.fileFallback`, Activity's is `web.common.activity.title.file`; both read `File #{id}`. Now Playing's old episode-code fallback (`nowPlaying.episodeFallback`) is gone; Recent activity is unchanged by these commits.
- Truncation: the title (`truncate text-[13px]` on Activity, `truncate text-sm font-bold` on the card) and the subtitle (`truncate text-[12px]` / `text-xs`) are each one line with an ellipsis. Nothing in these lines wraps.
- The Now Playing title and subtitle are one block with 6px (`mb-1.5`) below it, with or without a subtitle.
- Accessible names: the title link is named by the first line (official `title={title}`); the subtitle is plain text. Sorting by Stream sorts on the first line, so a show's episodes group.

## Native, before and after

| | Before (beta `26f432433`) | After |
|---|---|---|
| `AdminActivityPresentation.GetPlaybackSessionTitle` (Activity row, sort) | episode: its own title, else the code | episode session: the show; else title, else `File #{id}` |
| `GetPlaybackSessionSubtitle` (Activity, Now Playing) | episode: `S3E13 · Show`; movie: `Movie`; series/episode: `Series` | episode session: `S1E2 · Title` or `S1E2`; series/episode type: `Series`; movie and everything else: none |
| `DashboardPresentation.NowPlayingTitle` (new; Recent activity keeps `SessionTitle`) | `SessionTitle(.., "nowPlaying")`: episode title else code | the show for an episode session, else title, else `File #{id}` |
| Stacked Activity card subtitle condition | `SeriesTitle` non-empty | `IsEpisodeSession` (as official) |
| Now Playing gap under a title with no subtitle | none (title touched the tags) | 6px, as official |
| Plain text | titles went straight to the text block | `AdminCollectionNames.Plain` (the shared one-line neutraliser: line breaks and tabs become a space, direction and invisible characters removed, cut at 300 characters with an ellipsis); a title that shows nothing falls back to `File #{id}`, a show that shows nothing does not lead |

Code: `src/SiloPlayer.Core/Services/Admin/AdminActivityPresentation.cs` (`IsEpisodeSession`, `GetPlaybackSessionTitle`, `FormatEpisodeLine`, `GetPlaybackSessionSubtitle`), `DashboardPresentation.cs` (`NowPlayingTitle`),
`AdminText.cs` (key `web.common.activity.subtitle.episodeTitle` = `{code} · {title}`), `Views/Admin/AdminActivityPage.Rows.cs` (stacked condition), `Controls/Admin/DashboardWidgets.Operations.cs` (`StreamCard`: title call and the 6px gap; the
paused-badge radius and link lines belong to other pull requests and are untouched).

The Activity page has no neutralisation helper of its own and the pages' texts are English only (`AdminText`), so the shared `AdminCollectionNames.Plain` was used and the new catalog key was added to `AdminText` only; the generated app catalogs
(`Localization/Generated/*.json`) were not regenerated, so a non-English interface shows the English line.

## Two leftover scaled-display items (same unit)

- `LogsNativeUi.cs` `LogsWrapPanel`: a growing child is measured a second time only when the allocated width is more than its desired width plus 0.01 (`GrowRemeasureTolerance`); in a full row it used to be offered exactly its own rounded desired width again.
- `AdminActivityPage.Rows.cs` the "Open ffmpeg logs" text button: padding 6,2, margin -6,-2, 4px radius (as `SortHeader`), so the rounded corners no longer clip the letters.

## Requests

None changed. No request, URL, query, polling interval, store, realtime channel or write was touched; the session fields were already read (`media_item_id`, `series_title`, `season_number`, `episode_number`, `media_title`, `media_type`).

## Checks

- Unit: `AdminActivityShowTitleTests` ports the three official cases one for one (episode with title, episode without a title, movie) and runs every case against both an Activity session and a dashboard session; it adds the edges (missing show, missing numbers, season 0, series/episode types, track, chapter, empty type, no title, neutralisation, a long title, sort grouping). Existing expectations of the old rule were updated in `AdminActivityPresentationTests`, `AdminDashboardNowPlayingTests`, `AdminMonitorParityRefreshTests`.
- Native `admin-monitor`: six title cases (movie, episode, episode that lost its show, untitled episode, very long show and episode, track) at 1440, 1024, 800 and 560 with captures; the wrap-panel growth check (a stand-in child that needs 100.4 and reports 100); the Open ffmpeg logs button check.
  Proven against the old code (run `acttitle-proof-r1`, `acttitle-proof-r2`): the wrap check fails with the old `LogsWrapPanel` ("slot 100x40", the child wrapped by the second measure); the ffmpeg check fails with the old button ("inset 0, padding 0, radius 10").
- Native `admin-dashboard`: tiles 0-3 (episode with a show, audiobook, episode without a show, movie with a very long title) check the first and second line against the shared rule, one-line titles, the 6px gap and the ellipsis.

## Review pass (lead)

- An independent read-only review confirmed the title and subtitle table, the links, the Stream sort key, the string and the text safety against official, and found one regression and one missed surface; both are fixed.
- **Wrap panel.** The first version of the `LogsWrapPanel` change skipped the second measure of a growing child whenever it was not given more than its desired width, including when it was given clearly less. A child with a `MinWidth` (the restart banner's text, the Diagnostics filter fields) is first measured at the panel's width, so it kept that wide measure and its text was cut off instead of wrapping. Now a growing child is measured again when it gets more than its desired width or less than it by more than the wrap tolerance, and only left alone at its own rounded desired width. Native check "a growing child given less than it measured at the panel's width is measured again at its share and wraps"; against the first version it fails with "offered 250, desired 250x40" (run `acttitle-final-r1`, whose app was built before the correction).
- **Household streams.** The household streams panel in Settings (`SessionDisplayText.GetTitle` / `GetSubtitle`, official `HouseholdStreamsPanel.tsx` uses the shared functions) now follows the same rule: the show leads an episode with "S1E3 · Episode title" under it (the code alone without an episode title), and a movie has no second line. The older-server fallback (`series_name`, `episode_name`) is kept. `SessionDisplayTextTests` and `AuthV2ContractTests` state the new expectations.
- Left as it is: for a show or episode title that is only whitespace or invisible characters official prints the blank text; native falls back (the episode's own title, the code alone, or "File #id").

## Not verified

- Non-English interfaces (see above). Phone/real server data: only synthetic sessions were rendered; no poster images were present in the captures (placeholders only). Display scaling of 125%/150% was not run on a scaled display; the two items are proven with a stand-in at 100%.
