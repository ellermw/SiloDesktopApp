# Admin Users + User Detail — Full Audit

## Section 1: AdminUsers list page

### 1.1 Page header & layout
**Webui (107-154)**: Title "Users", subtitle, two buttons "User Defaults" + "Add User", top-level Tabs (Users / Invite Codes).
**Desktop (xaml 55-94, code 84-94)**: Same title/subtitle/buttons, line-style manual tab Visibility toggling.
**Gap**: None (functionally identical).

### 1.2 Search functionality
**Webui (162-186)**: Search box, case-insensitive on username+email, **X clear button**, resets page to 0.
**Desktop (xaml 140-151, code 99-117)**: Same placeholder + logic. **No clear button.**
**Gap**: Missing search clear X button. **P1.**

### 1.3 Users table
Columns match (Username / Email / Role / Status / Actions).
- Username: clickable Link (web, font-medium) vs HyperlinkButton (desktop, SemiBold, Accent).  **P0 minor** — weight mismatch.
- Email: match.
- Role badge: default/secondary (web) ≈ Accent/Surface (desktop). Match.
- Status badge: outline/destructive (web) ≈ outline/Error (desktop). Match.
- Actions: History + Edit + Delete, ghost h-7 w-7 ≈ 28x28. Match.
- History target: web `/admin/history?user_id={id}` (filtered) vs desktop `AdminPlaybackHistoryPage` (separate page). **P2.**

### 1.4 Pagination
**Webui (250-295)**: "Showing X-Y of Z", page size dropdown 25/50/100, Previous/Next with disabled states.
**Desktop**: **NO pagination. Lists all users.**
**Gap**: Missing pagination. **P1** (breaks at 100+ users).

### 1.5 Empty state
Both implement. Match.

### 1.6 Create User dialog
- Title/layout/Tab structure: match.
- **Account tab**: username, email, password (required create / optional edit), role dropdown, enabled (edit only). Match.
- **Access tab**: LibraryAccessSelector vs per-library checkbox list + Downloads/Transcode toggles. Functionally equivalent.
- **Limits tab**: MaxStreams/MaxTranscodes/MaxProfiles (0=unlimited) + MaxPlaybackQuality dropdown. Match.
- **Submit**: webui sends `create_default_profile: true`; desktop doesn't. **P1.**

### 1.7 User Defaults dialog
**Webui (570-701)**: Dialog with default limits pre-loaded from `useAdminServerSettings()`.
**Desktop (828-947)**: Same dialog structure but **hardcodes defaults (6, 2, 0) — does NOT load from server.**
**Gap**: Desktop ignores actual server defaults. **P1.**

### 1.8 Invite Codes tab
**Webui (298-300)**: Delegates to InviteCodesTab component.
**Desktop (xaml 203-278, code 1002-1043)**: Full inline table — Code, Label, Max Uses, Used, Status, Created, Actions. Lazy-loads on tab click.
**Gap**: None.

---

## Section 2: AdminUserDetail page

### 2.1 Page layout & navigation
**Webui (79-95)**: Breadcrumb Admin › Users › {username}.
**Desktop (xaml 71-129, code 84-161)**: Back button (32x32).
**Gap**: None (platform-appropriate).

### 2.2 Action buttons (Impersonate / Edit / Delete)
- Edit, Delete: match.
- **Impersonate**: webui full flow (lines 73, 173-185). **Desktop: button is wired but `OpenImpersonateDialogAsync()` is NEVER IMPLEMENTED.**
- **Gap**: **P0 CRITICAL** — desktop impersonate is non-functional.

### 2.3 Tab: Overview
Two-column Account + Permissions/Limits. Both match fully.

### 2.4 Tab: Profiles
Grid layout, UserCircle icon, empty state. Match.

### 2.5 Tab: Watch History
Table of 6 cols (Media / Profile / Method / Watch Time / Status / Ended), 50 items, status badges. Match.

### 2.6 Tab: IP History
Table of 4 cols (IP / First Seen / Last Seen / Requests right-aligned), IP in monospace, requests comma-formatted. Match.

### 2.7 Edit User dialog
Match (3 tabs: Account / Access / Limits).

### 2.8 Delete Confirmation
Match.

---

## Section 3: View Models & State Management
Functionally equivalent patterns. No gap.

---

## Feature Completeness Matrix

| Feature | Web | Desktop | Status |
|---------|-----|---------|--------|
| User List Table | ✓ | ✓ | Parity |
| Search | ✓ | ✓ | Parity |
| Search Clear X | ✓ | ✗ | **P1** |
| Pagination 25/50/100 | ✓ | ✗ | **P1** |
| Create User | ✓ | ✓ | Parity |
| Create User Dialog | ✓ | ✓ | Parity |
| Edit User | ✓ | ✓ | Parity |
| Delete User | ✓ | ✓ | Parity |
| User Defaults Dialog | ✓ | ✓ hardcoded | **P1** |
| Default Profile Creation flag | ✓ | ✗ | **P1** |
| Load Server Defaults | ✓ | ✗ | **P1** |
| User Detail Page | ✓ | ✓ | Parity |
| Overview Tab | ✓ | ✓ | Parity |
| Profiles Tab | ✓ | ✓ | Parity |
| Watch History Tab | ✓ | ✓ | Parity |
| IP History Tab | ✓ | ✓ | Parity |
| **Impersonate User** | ✓ | ✗ | **P0** |
| Edit from Detail | ✓ | ✓ | Parity |
| Delete from Detail | ✓ | ✓ | Parity |
| Invite Codes Tab | ✓ | ✓ | Parity |

---

## Prioritized Fix List

### P0 — Blocks Functionality
1. **Implement Impersonate User dialog** — `AdminUserDetailPage.xaml.cs` OpenImpersonateDialogAsync, follow `AdminUserDetail.tsx:173-185`. ~2-3h.

### P1 — Missing Core Features
2. **Search clear X button** — add to SearchBox, wire → clear + reset page. ~30m.
3. **Pagination controls** — page size 25/50/100, Previous/Next, disabled states, slicing in BuildUserRows. ~2-3h.
4. **`create_default_profile: true`** flag in CreateUserRequest. ~15m.
5. **Load User Defaults from server** — hook UserDefaultsForm to GetAdminServerSettings, populate form, save → update settings. ~1-2h.

### P2 — Polish
6. Username font weight alignment (Medium vs SemiBold).

## Summary
Desktop ~80% parity. 5 gaps to fix, 1 critical (impersonate non-functional).
