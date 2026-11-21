# Admin Plugin Apps — source candidate, November 17, 2026

Desktop base: `248aafd62ed65b868c0e7356926e4d83204649cd`, branch
`t3/admin-plugin-apps`, version `1.2.1181-beta.1` (unchanged).
Worktree: `C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-32509c42-d1e2f333e367dc2b`.
Official Server/WebUI pin: `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`,
read with `git show` from `D:/SiloPlayer-work/server-upstream-nov14`.

This candidate owns the dynamic **Plugin Apps** group and its native host only.
Plugins management and Providers are separate workers' pages. No pipeline PR,
installed player, settings, other checkout, source version, or release was changed.
The stopped candidate at `t3-c3dc7cd6-60cc16414f5568ec` remains untouched. Its
inventory reader and tests were read, checked against the pinned contract and
adapted here; its partial-work note is historical, not this candidate's status.

## Reachable implementation

- `App` registers `AdminPluginAppsApi`. The real `AdminShellPage` loads it under
  its captured admin lease, after entry and every authority recapture. Desktop,
  mobile drawer and scoped search render enabled installations' navigable routes
  whose `navigation_kind` is exactly `admin`, in server order. Labels and route
  patterns remain plugin data. The official icon is Puzzle for every app; the
  pinned route contract has no custom icon field. No screenshot names are hardcoded.
- The read uses `/api/v2/admin/plugins/installations`, never
  `/api/v2/settings/plugins`. Pages are limited to 100 installations, 100 pages,
  4 MiB per response, 256 routes per installation and 2,048 exposed rows, under a
  30-second total deadline. Duplicate installations, broken continuation and
  malformed pages fail the whole read; partial rows are not published. Null routes
  mean no routes. Unsafe paths/labels are excluded. Failure exposes Retry; an
  empty successful inventory omits the group.
- Every button retains its exact lease and checks current ownership both before
  and after the page's leave guard. Retirement clears visible rows and closes
  search. Late old reads cannot publish into a replacement shell/authority.
- A valid row navigates through the existing `NavigationService` to the existing
  `PluginRoutePage` using its new admin mode and the lease epoch. Launch captures
  current authority and reconciles the enabled installation/route again. The
  existing user-mode `MainWindow.RefreshPluginAppsAsync` and its origin checks
  remain the user contract; Admin does not reuse its user-filtered inventory.
- No new XAML page or XBF is introduced. The existing registered/activatable
  `Views/PluginRoutePage.xbf` is reused. It is outside the installer's forbidden
  `Admin` resource pattern, so no new installer exception is needed. The source
  inventory explicitly includes the new API, session, shell partial and host
  partial, as well as the reused page's XAML/code-behind.

## Supported native hosting boundary

Admin mode uses a separate named InPrivate WebView profile and a random virtual
HTTPS origin per page. The user plugin profile and its preferences are untouched;
failure to create the private profile fails closed. Plugin JavaScript never
runs on the connected server's browser origin. `AdminPluginContentSession` maps
only that installation's `/api/v2/plugin-content/plugins/{id}/…` and GET/HEAD
`plugin-assets/{id}/…` requests to the actual connected server. A fresh current
lease and an origin-checked API request are required before each send; results
are rechecked before publication. Browser auth/cookie/origin headers are never
forwarded, Set-Cookie is never returned, and the private HttpClient has no cookie
jar, default credentials, redirect following or retry. Writes are sent once.

The supported contract includes installation-relative scripts, styles, images,
forms, fetch endpoints, representation headers and redirects within that mount.
Only 303 may redirect a submitted write; ambiguous/preserving redirects are
refused with an outcome-review message instead of resubmitting it.
The page keeps a restrictive sandbox/CSP, disables host objects, native messages,
autofill and saved passwords, denies device permissions, popups, downloads and
external schemes, and blocks workers, nested frames, sockets and arbitrary server
APIs. Requests have a 20-second deadline, 4 MiB body and 16 MiB response bounds,
four simultaneous sends, at most eight pending sends and a 128 MiB page transfer
budget. Navigation departure and any authority loss cancel native work; role
loss also closes/hides the live WebView. No server launch cookie is minted.

This is a deliberately narrower contract than upstream's full-page server-origin
browser navigation. Plugins using absolute server-origin assets, other server
APIs, external resources/navigation, browser sessions, eval, downloads,
or additional capabilities are blocked. Intercepted navigation/request refusals
retain the host's explanation; CSP-blocked subresources can instead surface as
the plugin's own load/fetch failure. Broadening the host to
grant those privileges requires an explicit, reviewed per-plugin capability or
trust contract; this candidate grants none implicitly. **Mediux and Artwork
Manager compatibility is unverified and is not claimed.** The remaining choice
is precisely whether and how individual plugins may receive privileges beyond
their installation-scoped content mount; it does not block the supported group
or scoped plugin contract implemented here.

## Authored coverage and acceptance

All tests are **authored but unrun**, including their initial red states. No
restore, build, test, native/browser/app launch, mutex acquisition, generated
output, capture, installer or publication occurred in this source-only lane.
`git diff --check` and manual source/contract inspection are the only local
verification; they do not establish compilation or runtime correctness.

`AdminPluginAppsTests` covers admin-only pagination, custom labels/routes and
fixed icons, fallback labels, unsafe paths, invalid/repeated continuation,
authority/server/profile retirement, virtual-origin/installation/API isolation,
cookie stripping, one-send writes, redirect boundaries, asset methods, response
bounds, distinct page origins, delayed content after regrant and denied principals.
The tests were started before production changes; none is a claimed observed RED.

`AdminPluginAppsNativeFixture` is registered as `admin-plugin-apps` in the real
native harness. It enters the registered Admin shell in a Frame, inspects desktop
and mobile rows and scoped search, invokes an actual sidebar button, runs a
synthetic admin-only plugin through the real WebView, checks scoped script/fetch/
asset/form traffic, forbidden API/origin requests, live role loss, old buttons,
held responses across primary-profile changes and Retry. It authors desktop and
drawer PNG captures and waits for pages to unload before restoring globals. The
captures have not been generated or visually inspected. Synthetic fixtures do
not certify a live server, all DPI, Narrator or complete visual parity.

The coordinator must run exact-source acceptance in its granted serialized slot,
with the existing free-space/output inventory, one nonblocking `SiloOct18Build`
ACQ/REL, continuous ownership, process deadlines/containment and source/PDB/
publish/host byte binding. The following are acceptance commands, **not executed
receipts** (the shared paths must first be assigned by that lane without
overwriting frozen evidence):

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path D:/SiloPlayer-build/artifacts --filter "FullyQualifiedName~AdminPluginAppsTests|FullyQualifiedName~PluginPublicResourcesTests|FullyQualifiedName~PluginWebViewHardeningSourceTests|FullyQualifiedName~UserOnlyClientArchitectureTests|FullyQualifiedName~AdminNavigationTests"
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -r win-x64 -p:Platform=x64 --artifacts-path D:/SiloPlayer-build/artifacts -o D:/SiloPlayer-build/publish
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory D:/SiloPlayer-build/publish -Only admin-plugin-apps
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory D:/SiloPlayer-build/publish -Only plugin-cutover
```

`run.ps1` builds/stages its host and reference, so it too needs the coordinator's
deadline/containment and inclusive output accounting. The coordinator may use its
existing bound-host launcher to avoid regenerating those references. Inspect both
new captures, run existing Monitor shell coverage on the integrated shell, and run
the full unit suite before acceptance. Keep the installed player open throughout.

## Exact official references

- [Admin selection, labels, icons and command groups](https://github.com/Silo-Server/silo-server/blob/217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb/web/src/lib/adminNavigation.ts)
- [Admin sidebar inventory source and full-page entry](https://github.com/Silo-Server/silo-server/blob/217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb/web/src/components/AdminSidebar.tsx)
- [Bounded installations pagination](https://github.com/Silo-Server/silo-server/blob/217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb/web/src/hooks/queries/admin/plugins.ts)
- [Installation response and acting-admin API](https://github.com/Silo-Server/silo-server/blob/217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb/internal/apiv2/admin_plugin_installations.go)
- [Wildcard-to-content href](https://github.com/Silo-Server/silo-server/blob/217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb/web/src/lib/pluginRouteHref.ts)
- [Web launch-cookie behavior](https://github.com/Silo-Server/silo-server/blob/217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb/web/src/lib/buildPluginHref.ts)
- [Dynamic mount policy and explicit plugin compatibility limits](https://github.com/Silo-Server/silo-server/blob/217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb/internal/apiv2/plugin_content.go)
- [Official strings](https://github.com/Silo-Server/silo-server/blob/217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb/web/src/locales/en.json)
