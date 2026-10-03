# Requests poster hover and Back navigation

User report: Requests search posters disappear while hovered, reappear on exit, and Back from an opened result refreshes the same title. User was trying to request Season 2 of Scrubs (2026).

Worktree: `C:\Users\Michael\.codex\worktrees\parity-top-three\SiloPlayer`. Re-fetched the official GitHub Silo Server `main`; reference remains `8e2e840474a085c6df6571a5a2850f7eb996810c`. No production infrastructure or installed app operation.

## Reproduction and causes

The isolated native host loads actual published WinUI pages and exercises their real button visual states and Frame history:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory .codex-tmp/parity-queue-publish -Only request-interactions
```

1. `RequestsPage.BuildMediaPosterCard` puts a contentless GhostButton over the poster and caption. The stock Button template changes its background to an opaque hover/press brush. The bitmap remains loaded underneath. Native RenderTargetBitmap measurements on a red fixture poster: Normal 50,708 red pixels, PointerOver **0**, Pressed **0**, Normal after exit 50,708. This reproduces the user's disappearing/reappearing poster. Yours cards use the same faulty overlay pattern.
2. Opening a result with an accessible library copy navigates Requests → RequestDetail → ItemDetail. Back revisits RequestDetail, whose load redirects to ItemDetail again. Native history before the fix contained `RequestsPage,RequestDetailPage`; Back ended on ItemDetail rather than the original search instance.

Red evidence: `.codex-tmp/request-interactions-red.log`, with both exact failures. No credentials, signed artwork URLs or live requests are needed for this reproduction.

## Repairs and focused verification

- A dedicated `PosterHitTargetButtonStyle` keeps its background transparent in every interaction state, provides hover/press outlines and retains system keyboard focus visuals. Applied to both search/discovery and Yours overlay buttons. Other GhostButtons are unchanged.
- `NavigationService.NavigateReplacingCurrentEntry` records the redirect's origin/target and removes only the matching intermediate Back entry when the Frame commits. It supports the shell's deferred navigation and clears the pending replacement on other navigation. Ordinary detail navigation still retains normal history.
- Request-detail promotion uses that replacement operation. Tests verify immediate and shell-staged promotion, normal outside-library details, original cached Requests page identity and preserved search text.
- The actual promoted series exposes More → Request seasons. The native test opens the real dialog, verifies available Season 1 is disabled, selects missing Season 2 and activates Request selected. The isolated HTTP handler receives `seasons: [2]`. This is a fixture submission; no real Scrubs request was created.

Green command uses `.codex-tmp/request-interactions-publish`. Red poster pixels become Normal 50,708; hover 49,996; press 49,994; exit 50,708 (only the outline changes). Both promoted paths retain only `RequestsPage` in Back history. Normal external details also return to the same search. Evidence: `.codex-tmp/request-interactions-green.log`.

## Requesting Season 2

From Requests search, open the 2026 series. If it resolves to the library copy, use **More (•••) → Request seasons**, select **Season 2** only, then **Request selected**. Outside-library titles use **Request series** to open the same picker. Available/already-requested seasons remain disabled; future seasons can be selected explicitly rather than being included automatically in Missing aired.

The live server's current Scrubs season list was not inspected: access tokens are memory-only, and this investigation did not refresh or replace the running app's credentials. Public source metadata is not proof of the response shown to this profile. If Season 2 is absent from the picker, inspect that title's actual request-detail response before attributing it to the desktop or server.

## Packaging and final checks

Candidate version is **1.2.0**, reflecting the new-feature batch included from 1.1.107. Final verification completed October 1:

- All 1,267 automated tests passed (zero failures or skipped tests).
- Focused request interaction checks and the full native WinUI regression host passed against the published candidate. Logs: `.codex-tmp/request-interactions-green.log` and `.codex-tmp/request-interactions-full-native.log`.
- All six published playback-service checks passed: `.codex-tmp/request-interactions-service.log`.
- x64 Release publish succeeded without warnings; executable file version is `1.2.0.0`. Installer compilation succeeded. `git diff --check` passed.
- Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.0-Setup.exe`, 169,478,054 bytes.
- SHA-256: `BE5BFE9C8379B7F76204E4C9270398EAC1DC81426648DA5BC9B41A58EC486195`.

No installed-app replacement or GitHub publication is part of this repair. These checks verify the interaction repairs, not resolution of the separate recurring-buffering investigation or complete visual parity.
