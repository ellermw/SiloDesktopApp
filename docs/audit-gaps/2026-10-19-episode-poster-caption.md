# Episode poster caption clipping — October 19

The owner reported clipped metadata beneath episode names in multiple Home sections and TV/Anime libraries on released 1.2.271.

## Reproduction and cause

An isolated native fixture loads the exact published 1.2.271 controls without the owner's account or running player. A real SectionRow inside a vertically scrolled ScrollViewer reproduces the issue: Tracker's S04E01 line ends at345.86 while the card and horizontal viewport end at334. A separate LibraryGridCard reproduces the same deficit: episode-code bottom347.36 exceeds card334. Both failures and PNG captures are preserved under `.codex-tmp/poster-caption-oct19`.

The shared metadata caption budget is56px, enough for title and one metadata line. Episode cards add a third line. Library's virtual grid independently duplicates the56px budget. This is deterministic sizing failure, independent of network requests and scroll position.

## Repair and verification plan

- Reserve76px for the complete metadata caption and use that same shared budget in Library's virtual grid. Keep artwork-only and title-only budgets unchanged. Uniform grids retain consistent card/row heights.
- Verify the original two native failures against the new payload; check Home and library sections across narrow,1080p, ultrawide and4K viewport widths, all poster sizes, all caption preferences, mixed movie/episode rows, recycling and horizontal/vertical scroll.
- Run the main suite and relevant existing native grid/Home checks. Prepare local installer1.2.272 for owner testing; keep source uncommitted and GitHub/download at1.2.271.

Official Silo Server main was fetched directly for this scoped reference check: `365e3d436fc2fac8e8b3f4f6d42111ff1baeb48b`. Current `SectionItemCard.tsx` and `ItemCard.tsx` render series title, optional episode title and metadata naturally; no client API changes are needed for this repair. This scoped layout repair is not a new full upstream/parity audit.

## Verified result

Both original failing native cases pass on the exact1.2.272 payload. The Home episode code still ends at345.86, now inside its354px card/viewport; Library's code ends at347.36 inside its354px card. The text itself, poster dimensions and existing margins are unchanged; the metadata caption budget increases by20px across shared poster layouts. Title-only and artwork-only budgets remain36/0px.

Nine native groups passed with exit0 and completion sentinels: the two original reproductions,45 responsive carousel configurations, four mixed-grid groups covering48 page/size/width configurations, existing mounted caller grids and current Home regressions. Carousel coverage includes500/1080/1920/3440/3840 widths, three poster sizes, three caption preferences, TV/Anime section labels, end/back horizontal paging, vertical scroll and reuse of a card across movie/episode payloads. Actual Catalog, Library, Search and Recommendation pages were mounted against isolated APIs at1080/1920/3440/3840 widths; painted caption bounds and actual next-row gaps were checked. Existing movie grid checks also passed at500/900/1400 widths.

The expanded mixed-grid fixture initially assumed Library's900px-width header rhythm at1080 and inspected unpainted overscan text as if it were visible. Those fixture failures are preserved. The final fixture measures the responsive toolbar bottom, checks painted text bounds and requires a painted episode code. No additional product changes were made for those fixture corrections. The full main suite passed1,920 tests, zero failures/skips. Representative original/fixed Home,1080 and4K captures were inspected.

Local installer: `installer/output/SiloInstaller-1.2.272-Setup.exe`. SHA256: `03672C662DC18626AA4F191332F805E231229E02B9C4EADEFE91319148BBB114`. The supported build retained native dependency/signing policy; only the two preexisting PlayerService nullable warnings remain. Receipt: `installer/output/SiloInstaller-1.2.272-verification.json`. No running installed app, account, production state or GitHub release was changed.
