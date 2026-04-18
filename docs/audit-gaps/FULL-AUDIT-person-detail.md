# Person Detail Page — Full Audit (Deep Re-audit)
Webui: PersonDetail.tsx (~210 lines) | Desktop: PersonDetailPage.xaml/.xaml.cs
## Status: Good parity — minor gaps
### Already matching
- Photo (2:3 aspect) + info column layout
- Name title, birth/death dates, age calculation, birthplace
- Bio text
- Filmography section with type filter tabs (All/Movies/Series)
- Refresh metadata button (all users, with spin animation)
- Edit metadata button (admin only)
- Initials fallback when no photo
### Gaps
| # | Sev | Gap | Effort |
|---|-----|-----|--------|
| 1 | P1 | EditPersonDialog | Webui has admin dialog to edit person metadata inline (name, bio, dates, photo). Desktop has button but no dialog | medium |
| 2 | P2 | Filmography virtualization | Webui uses ItemGrid with window virtualizer + pagination (60 per page). Desktop uses flat ItemsRepeater | medium |
| 3 | P2 | Metadata badge styling | Webui uses `.metadata-badge` class (pill styling). Desktop may use plain text | small |
| 4 | P2 | Skeleton loading state | Webui has PersonDetailSkeleton (photo + text skeletons). Desktop uses ProgressRing | small |
| 5 | P2 | Bio styling | Webui: no show-more toggle, just max-w-2xl. Desktop: has show-more toggle (desktop is more feature-rich here) | n/a |
