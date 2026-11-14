# Admin Monitor — November 14, 2026

## Source and scope

Official WebUI main was fetched and rechecked at 1a99f7beffcea3850f067df000c319f2704fd23b. Desktop started at Beta 8f0808361faf2f6a4923f28c11dc2f22173a06a7 and incorporated d96759b42b0ef7a511edb57bf3c3aab20cafbb47 before final checks. Version stays 1.2.1017-beta.1 / 1.2.1016.1; the pipeline owns stamping and packaging.

Monitor includes Dashboard (all 28 widgets), Activity, Server Log, Client Diagnostics and Logging Settings. The acting-admin-only footer link is above Profile. The Admin shell replaces the ordinary shell, has desktop/narrow navigation and scoped search, and swaps pages without sliding. Manage/Configure destinations remain outside this increment; related labels are plain text.

A shared activity store owns one admin channel lease and supplies the indicator, Activity and Dashboard. The main-shell indicator follows audited WebUI eligibility, idle and player-cover rules; Admin shows it even while idle. Dashboard supports responsive placement, ranges, customization and validator-guarded persistence. Logs support live ticketed streams, bounded history, filtering/details, diagnostics actions and logging draft/save/restart behavior.

## Review corrections and native discoveries

Independent reviews covered shell/activity authority, Dashboard polling/layout and Logs controllers. Corrections retire queued old snapshots, cached scans, dialogs/drafts and nested page subscriptions; settle initial failures; gate polling waves; release detail mounts; reject stale drag/drop owners; track per-library actions; accept successful diagnostics receipts without ETags; and recover 400 invalid_cursor by restarting history.

Actual native runs reproduced a narrow-layout cycle in Playback Activity. Arranged bar heights plus gaps increased the next desired height under infinite-height measurement. Narrow plots now have explicit height, segment gaps fit the budget, and decorative tracks use non-measuring canvases. The final fixture always exercises all 28 widgets; its temporary isolation switch was removed.

Screenshot comparison corrected reserved glow height, empty restart/summary spacing, Dashboard live-region spacing, Logs tab grouping and the activity button's white background. Another native red run reproduced delayed TextChanged events marking untouched Logging values dirty; model echoes are now ignored while real edits remain drafts.

## Evidence

Full main units after Beta integration: 6,367 passed, zero failed/skipped, 2m22s. Final focused Admin/source/architecture checks: 268 passed, zero failed/skipped. Build hashes and individual native outcomes are recorded in D:/SiloPlayer-build/admin-monitor-final-receipt.json.

Release publish/native build logs: admin-integrated-publish.log and admin-integrated-native-build.log. Unit logs: admin-integrated-full-units.log and admin-integrated-final-focused.log. Native results: results/admin-monitor-admin-final, results/admin-dashboard-admin-final and results/admin-monitor-logs-admin-final, covering five sizes (1440x900, 1280x800, 1024x768, 1023x768, 800x900), loading/error/populated states and interactions. Logging checks include initial clean hydration, actual navigation Cancel/Discard, one-batch saves and retirement during confirmation. The runner verifies published/reference/host product hashes and the bundled engine.

Red evidence is preserved in results/admin-dashboard-probe-playback and results/admin-monitor-logs-draft-red. Independent review reports retain their original scope. Profile-menu-navigation and hud-completion-scope passed. The adjacent upstream-shell fixture repeatedly fails with COM 0x80004005 at NavigationView.IsPaneOpen during narrow resizing. A controlled overlay of exact Beta MainWindow.xaml/.cs with the new Admin shell partial disabled reproduced the same failure; Admin source was restored and hash-checked. This is a shell-baseline comparison, not a complete independent baseline build. See admin-shell-baseline-comparison.json and results/upstream-shell-admin-beta-shell-baseline. Architecture/installer exceptions are scoped by admin-monitor-surfaces.json. No Admin version stamp changes.

## Limits and storage

The official extracted WebUI ran locally with synthetic responses; native used midnight-cinema and representative synthetic data. Reference PNGs are in results/admin-webui-reference. Uptime, session times, connection state and some row types differ between captures. This verifies source/contract alignment and exercised native states, not exhaustive pixel equivalence or live-server interoperability. Native input/table rendering differs; Manage/Configure is intentionally absent. Admin catalogs currently resolve English keys pending the separate localization foundation. Physical pointer/DPI, every server theme/locale and long-running live traffic were not certified.

The installed player, credentials, settings and unrelated worktrees remain untouched. Output reuses D:/SiloPlayer-build. Earlier automatic approval review rejected generated-output cleanup with 'blocked by policy'; deletion was not retried or bypassed. Actual recovered space is 0 GiB on C: and D:. Final free-space/output measurements are in the receipt.
