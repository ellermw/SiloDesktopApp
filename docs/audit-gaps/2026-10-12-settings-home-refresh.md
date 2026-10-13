# Settings layout, toggle contrast and periodic Home reconstruction

Local candidate: **1.2.245**, based on published desktop main `6413dc4c6717c54b0696d74db3b150d4b918cd6c` and the preserved uncommitted 1.2.244 diagnostic changes. Official Silo server main was fetched directly for this pass: `eecb7f5024249377948b50d8d65b98b4f41e9876`. The owner's installed app was observed running 1.2.244.0; it was left open.

## Settings entry

`SettingsPage` initially exposed `SettingsLayoutGrid` and hid its intended overview. `OnNavigatedTo` waited for preferences and request availability before selecting the overview or requested tab. That exposed the raw detail layout during loading, and a late completion could reset a tab chosen while loading.

The constructor now builds and presents the overview immediately. Navigation selects the requested tab before its first network await. Loaded capabilities update the overview without resetting the selected route. The old 1.2.244 native constructor fixture failed on the initial visibility; the packaged 1.2.245 page passes. A gated HTTP response also verifies Playback appears before loading finishes and a subsequently selected Subtitles tab remains selected at 600, 1920, 3440 and 3840 logical pixels.

## Enabled toggle thumb

The app supplied `ToggleSwitchKnobFillOn`, but left the hover and pressed thumb resources to Windows. Its track uses the app's pale primary color, so a white platform thumb can lose contrast. The initial native test on this machine's ordinary fallback stayed visible; that did **not** establish the user's exact live colors. A controlled fallback of white for the two unowned states then reproduced the missing contrasting thumb pixels on hover. This is a controlled reproduction of the failure mechanism, not a capture of the installed app's particular Windows resource values.

The theme now owns all three enabled thumb resources, and `ThemeService` refreshes all three from the primary foreground color. The packaged native control has contrasting thumb pixels in normal, hover and pressed states. Rendered PNGs are retained with the test results. No unrelated geometry or playback behavior was changed.

## Periodic Home reconstruction

Retained notification logs show read invalidation at roughly five-minute intervals while playback/account identity stays at generation 3. `EventChannelClient` classified transport reconnection as `AccessChanged`. The shell's access handler called `NavigationService.RefreshCurrentEntry`, which disabled page caching and navigated to a new Home page. `HomeViewModel` also cleared its rows for any new read generation. Thus an ordinary notification reconnect destroyed the mounted cards even when their content and owner had not changed.

Transport reconnection now publishes a separate `ReadCatchUpRequested` event. It still retires old requests, clears event snapshots and refreshes authoritative account data. A visible Home page adopts the new read context only for the same identity, invalidates its data cache, and reconciles fresh results into existing rows/cards. Explicit access changes, including server close 4001, retain the strict permission-refresh route; another profile never inherits mounted content.

The old 1.2.244 mounted-card fixture failed after read invalidation. The packaged 1.2.245 native fixture preserves the same populated section and actual scrolled `LandscapeCard` through ten catch-ups. Explicit permission invalidation and a profile change both retire the old surface. These checks establish card retention; they do not claim a live network soak or measure real artwork requests.

## SiloCast installation defaults

Fresh/missing preferences now enable the existing receiver. An explicitly saved false preference remains an opt-out. The installer no longer exposes the SiloCast task checkbox and always installs its existing Private-network/local-subnet firewall rule; Public-network access remains excluded. The Settings toggle still permits disabling the receiver. Native settings checks confirm the default enabled control fits 600/900-pixel layouts. Phone interoperability is not newly certified.

## Evidence and limits

- Old failure evidence: `.codex-tmp/home-dismissal-oct12/red-v2-settings-entry`, `red-home-catchup`, and `red-toggle-realkeys`.
- Final candidate native evidence: `.codex-tmp/settings-home-oct12/final-settings-entry`, `green-v3-toggle`, and `final-v3-home-catchup`.
- Release tests: 1,662 main, 14 receiver, 16 discovery; 23 checks using the actual published playback service.
- The test copy's 561 packaged files match the installer payload; extra source assets required by the fixture host were added only to the test copy.
- The original intermittent Next Up removal exception remains an unresolved investigation documented in the 1.2.244 record. This release preserves its diagnostics and does not claim to fix that root cause.
- The full default native suite has the documented older hidden-versus-disabled detail assertion limitation. These targeted checks do not certify complete visual parity.

No Git commit, GitHub publication, production modification, user-account mutation or replacement of the running player was performed.
