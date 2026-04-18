# Admin Dashboard — Full Audit

## 1. Page Header & Title
**Webui (66-99)**: Title clamp(2rem-3.25rem), subtitle, Refresh + Scan All buttons
**Desktop (xaml 73-104)**: Title fixed FontSize=42, same buttons (SecondaryButtonStyle vs outline variant)
**Gap**: Minor styling. **Severity**: Visual (minor)

## 2. Stats Row (5 cards)
**Webui (146-203)**: 5 cards in responsive grid — Active Streams, Total Movies, Total Shows, Users, Storage. Font sizes label=11, value=28, sub=11. CharacterSpacing -30. Storage TB/GB split.
**Desktop (106-280, code-behind 44-65)**: Same 5 cards, same sizes, same storage logic.
**Gap**: None

## 3. Now Playing Section
**Webui (105-130)**: Conditional render `sessions.length > 0`. Grid `grid-cols-1 lg:grid-cols-2`. Top 4 sessions via StreamCard. **"+{remaining} more active streams"** overflow link.
**Desktop (282-313)**: NowPlayingSection, 2-col grid, top 4 via BuildStreamCard. **Overflow link NOT IMPLEMENTED.**
**Gap**: Missing "+X more" overflow link. **Severity**: Functional (minor)

## 4. Stream Card Component
**Webui (206-310)**: Poster 70px, title clickable Link to `/item/{content_id}`, play-method badge (direct/remux/transcode), node badge, profile badge, avatar + username + elapsed time.
**Desktop (100-359)**: Same visuals, same badges. **Title NOT CLICKABLE** (no content_id navigation).
**Gap**: Stream Card title not a navigation link. **Severity**: Functional (UX)

## 5. Libraries Card
**Webui (313-378)**: Header "Libraries" + "Manage ›". Empty state. Row: poster/icon 40x40 + name + `{type}·{path_count} path(s)` + scan button + enabled status dot. Row styling `hover:bg-surface-hover`.
**Desktop (323-348, BuildLibraryRow 382-503)**: Same card/empty-state/row. **No explicit hover styling.**
**Gap**: Hover state missing. **Severity**: Visual (minor UX polish)

## 6. Users Card / Table
**Webui (380-442)**: Card header "Users" + "Manage ›". Table: User | Role | Status. Top 8. User cell = avatar + username + email. Role: default/secondary badge. Status: outline "Active" / destructive "Disabled". **Rows clickable** → `/admin/users/{u.id}`.
**Desktop (351-376, BuildUserRows 529-599, BuildUserRow 602-733)**: Same card, same table headers, same top-8, same badges. **Rows NOT clickable.**
**Gap**: Missing row-click navigation. **Severity**: Functional

## 7. Recent Activity Section
**Webui (445-500)**: Conditional on sessions. Header + "View all ›" to `/admin/activity`. Up to 10 items. Play icon + rich text "{username} started watching {title}" with **clickable Link** to `/admin/history?user_id={id}&profile_id={id}`. Timestamp. **Trailing AdminSessionActions component (compact=true)**.
**Desktop (380-405, BuildActivityItems 735-751, BuildActivityItem 754-849)**: Same header (but "View all ›" NOT clickable). Same icon + rich text. **Title NOT clickable. NO AdminSessionActions.**
**Gap**: Two gaps — titles not clickable, no session actions dropdown. **Severity**: Functional (critical for admin)

## 8. AdminSessionActions Component
**Webui (491)**: Dropdown with Pause/Resume, Stop, Message (dialog), Terminate (destructive). Toast feedback. Optimistic UI.
**Desktop**: **Component entirely missing.** No dropdown, no playback controls, no message, no termination UI.
**Gap**: **CRITICAL** — Desktop lacks all session control UI.

## 9. Loading & Error States
**Webui (46-61)**: Skeleton grid during load (5 stat skeletons + 2 row skeletons + 1 activity skeleton).
**Desktop (37-63)**: ProgressRing + error + Retry button.
**Gap**: Spinner vs skeleton — both valid. **Severity**: Visual (different UX)

## 10. Command/Binding Architecture
**Webui**: React hooks (useAdminStats, useAdminSessions, etc.) + mutations.
**Desktop**: MVVM ObservableCollections + RelayCommands.
**Gap**: None (platform-appropriate)

---

## Prioritized Fix List

### P0 — Critical
1. **AdminSessionActions in Recent Activity** — pause/resume/stop/terminate/message dropdown. File: `AdminDashboardPage.xaml.cs` BuildActivityItem 754-849.
2. **Stream Card title navigation** — clickable → item detail page. BuildStreamCard 100-359.

### P1 — Important
3. **User Row navigation** — clickable → user detail. BuildUserRow 602-733.
4. **Activity title navigation** — clickable → history filter for user/profile. BuildActivityItem.
5. **Overflow sessions link** — "+X more active streams" below grid when sessions>4. NowPlayingSection 282-313.

### P2 — Polish
6. **Library row hover state** — PointerEntered/Exited. BuildLibraryRow line 385.
7. **Responsive title sizing** — AdaptiveTriggers for clamp-like behavior (optional).

## Summary
- **10 sections audited**
- **3 exact matches** (Stats Row, Loading/Error Architecture, Command Architecture)
- **3 visual gaps** (header, hover, title sizing)
- **5 functional gaps** (stream/user/activity nav, overflow link, session actions)
- **1 critical** (missing AdminSessionActions)
- Desktop **~70%** feature-complete vs web
