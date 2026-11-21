# Admin Plugin Apps

Scope: the Admin sidebar/search "Plugin Apps" group and the page each entry opens. The Plugins
management pages (board, install, configure) are a separate family.

Official source: Silo-Server/silo-server `df37bfc6d57b1680c0f7a59e51e15c8d0cfa6065` (main, 2026-11-20).
Files read: `web/src/lib/adminNavigation.ts` (`buildAdminPluginNavItems`, `appendAdminPluginNavSection`,
`buildAdminCommandNavSections`), `web/src/lib/pluginRouteHref.ts`, `web/src/lib/buildPluginHref.ts`
(`navigateToPluginRoute`, `buildPluginHref`), `web/src/components/AdminSidebar.tsx`, `web/src/components/SideNav.tsx`,
`web/src/hooks/queries/admin/plugins.ts`, `web/src/api/types.ts` (`PluginRoute`, `PluginInstallation`),
`web/index.html` (`data-theme`), `internal/apiv2/plugin_launch.go`, `internal/api/plugin_access.go`,
`internal/plugins/content.go`, `internal/plugins/http_proxy.go`, `contracts/api/v2/openapi.json`
(`createPluginLaunch`, `x-silo-plugin-content`).

Changes since the earlier pin `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`: none in the navigation builder, href
helpers, launch, access resolver or proxy header filters. `AdminLayout.tsx` changed only overflow/sticky-header
styling; `internal/api/handlers/plugins.go` and `internal/apiv2/admin_plugin_*.go` changed task-binding,
auth-callback and provisioning-ownership fields that this family does not read.

## Official isolation model

- The WebUI does not embed plugin content. There is no iframe, no sandbox attribute, no postMessage bridge and
  no message contract between the WebUI and a plugin page.
- Clicking a Plugin Apps entry calls `navigateToPluginRoute`: `POST /api/v2/auth/plugin-launch` with the
  session's bearer token and profile headers. The server answers `{"expires_in":300}` and sets the
  `silo_plugin_access` cookie: a five-minute plugin access token (a separate token type, not the access token),
  `HttpOnly`, `SameSite=Lax`, `Secure` on HTTPS, host-only, `Path=/api/v2/plugin-content`. Requests through
  the cookie past half its life receive a renewed cookie while the login session lasts.
- The browser then leaves the WebUI with a full page load of
  `/api/v2/plugin-content/plugins/{installation}{route without "/*"}?theme=midnight-cinema`. If the launch fails
  it shows the toast "Unable to open the plugin. Please try again." and stays.
- What the plugin page can reach: with credentials, only the plugin-content parent path (every installation's
  pages and static assets) because that is the cookie's path; plugin script cannot read the cookie. The server
  forwards to the plugin process only `Accept`, `Accept-Language`, `Content-Type`, `If-None-Match`,
  `If-Modified-Since`, `Range`, `User-Agent`, `Origin` and `Referer` (never `Authorization` or `Cookie`), plus
  identity headers it adds itself, and passes back only `Content-Type`, `Cache-Control`, `ETag`, `Last-Modified`
  and same-origin `Location`; a plugin redirect to another origin becomes 502.
- What it cannot reach: the access or refresh token (the WebUI keeps the access token in memory and the page
  no longer runs), any server API outside the plugin-content path with credentials, other hosts with the cookie.
- No CSP or sandbox is added by the server. As an ordinary top-level browser page it can otherwise do what any
  web page can (navigate the tab anywhere, open popups, request permissions through browser prompts, download).

## Native model (no more than the official page)

`PluginRoutePage` in Admin mode (`PluginRoutePage.Admin.cs`), opened from the sidebar or search as a full-window
page in the app's main frame, like the WebUI leaving the Admin shell.

1. Re-read the admin installations inventory and require the entry still to be offered.
2. `POST /api/v2/auth/plugin-launch` through a transport that keeps no cookies and follows no redirects
   (`SiloApiClient.AdminPluginApps.cs`, `AdminPluginAppsApi.LaunchAsync`). The receipt must be valid and the
   `silo_plugin_access` Set-Cookie must have exactly the plugin-content parent path, no `Domain`, and
   `0 < Max-Age <= 3600`; otherwise nothing opens.
3. Copy that cookie, as issued (`HttpOnly`, `SameSite=Lax`, `Secure` as sent, host-only, same path and lifetime),
   into a WebView2 profile of its own (`SiloAdminPluginApps`, InPrivate: nothing persists and nothing is shared
   with the viewer's plugin profile or with the app's own stores).
4. Navigate to the official start URL. Nothing else is given to the content.

Hardening set before the cookie is installed: host objects off, web messages off, developer tools off,
default context menus off, browser accelerator keys off, status bar off, built-in error page off, password and
general autofill off, swipe navigation off. `NewWindowRequested` handled (no window), `PermissionRequested`
denied (camera, microphone, location, clipboard read, notifications, all others), `DownloadStarting` cancelled,
`LaunchingExternalUriScheme` cancelled, basic-auth and client-certificate prompts cancelled. Developer tools
and context menus are off in every build, not only release.

### Navigation and request decisions

The allowed space is the connected server's scheme, host and port, and within it only
`/api/v2/plugin-content/plugins/{id}` (and below) and `/api/v2/plugin-content/plugin-assets/{id}/` (below)
for this installation (`AdminPluginContentScope`). A path with a backslash, any `%2f`, `%5c`, `%2e`, `%00` or
`%25`, a dot segment, embedded credentials, another scheme, or longer than 8 KB is outside.

| Case | Official browser | Native |
| --- | --- | --- |
| Top-level navigation or redirect inside the space | follows | follows |
| Top-level navigation to another origin, another installation, a server API, `file:`, `data:`, app schemes | tab leaves to it | cancelled; the page stays as it was (a refused redirect of the page being opened shows the load-failed state) |
| Frame navigation inside the space, `about:blank`, `about:srcdoc` | loads | loads |
| Frame navigation outside the space | loads | cancelled |
| `window.open`, `target=_blank` | popup | suppressed (no window, no request) |
| fetch/XHR/form/img/script/style/worker request inside the space | sent with the cookie | sent by the browser unchanged (cookie only) |
| Asset mount with a method other than GET/HEAD | server refuses | 403 before sending |
| Any request outside the space (other origin, server API, other installation) | sent (with the cookie only if under the parent path) | 403 before sending |
| Download | browser download | cancelled |
| External protocol | browser prompt | cancelled |
| Permission request | browser prompt | denied |
| `window.print()` | browser print | left as WebView2 provides (the official page can print); accelerator keys are off |

External links are not opened in the user's browser: the official page does not open them externally (it
navigates its own tab away), so the native page cancels them instead.

### Credentials

The administrator's access and refresh tokens are used only for the launch request to the Silo server. They are
never placed in a URL, a script, a message, a header on a plugin request, a log line or a diagnostic file. The
launch cookie value is never formatted (`AdminPluginLaunchCookie.ToString()` is redacted) and lives only in the
InPrivate profile, deleted on every reload, removal, failure and teardown. With a server base path the cookie
path the server issues (`/api/v2/plugin-content`) does not match the content path; the native page then refuses
to open rather than widen the cookie (the WebUI has the same limit).

### Messages

The official host has no message contract, so the native host has none: web messages are disabled and no
`WebMessageReceived` handler exists. Plugin text shown natively (navigation label, plugin id in the description,
window title) is reduced to plain single-line text: control, format (all bidi embeddings, overrides, isolates,
marks and zero-width characters), private-use and unassigned characters removed, whitespace collapsed, at most
80 text elements with an ellipsis (`AdminPluginText.Plain`).

### Lifetime

Leaving the page (Back, another page, another plugin), unloading, signing out, an account, role, profile or
server change (the Admin authority lease retires) all stop the load, delete the cookie and close the web view;
an authority change shows "Admin access changed" with "Return to Admin" and never reloads under the new
authority. A Back/Forward entry from an earlier authority shows the same state. While open, the page re-reads
the inventory every 30 seconds (the WebUI's admin query stale time) and on every Reload; when the installation
is disabled or removed or no longer offers the route, the content is torn down and "This plugin app is not
available" is shown.

### Failure states

Every state has Reload (which re-reads the inventory and launches again) and never shows plugin or server text:

| Cause | Title | Native explanation |
| --- | --- | --- |
| Route missing, installation disabled/removed, page 404/410 | This plugin app is not available | may have been disabled or removed |
| Launch or page 401 | Unable to open the plugin. Please try again. (official) | session not accepted |
| Launch or page 403 | (official title) | account not allowed |
| Launch or page 429/5xx | (official title) | server could not provide it |
| Connection failure, invalid receipt or cookie | (official title) | could not be loaded |
| No completion within 30 seconds, or an inventory/launch timeout | (official title) | took too long |

The page status in this table decides the page being opened. Once a page is shown, its own later top-level
navigations inside the space keep it, as the browser tab does: a same-document navigation (fragment, history;
it completes without an HTTP status), a cancelled navigation, and any HTTP response, the plugin's own 4xx/5xx
page included. Only a 401 (the launch cookie is no longer accepted, so only a new launch continues) or a
navigation that got no response from the server (connection failure, timeout) shows the native state. A
same-document navigation made while the page is still opening (a plugin setting its fragment at startup) does
not decide the state; the navigation that opens the page still does.

Web view processes: when the browser process exits, or the page's render process exits or stops responding,
the session is dropped and the load-failed state is shown. After a browser process exit nothing of the old web
view can be used, so Reload replaces the control and builds a new web view with the same InPrivate profile and
hardening. GPU, utility and frame process failures, which the browser recovers from by itself, leave the page
as it is.

## Navigation entries

`AdminPluginAppsApi.GetAppsAsync` drains `GET /api/v2/admin/plugins/installations` (limit 100, cursor
continuation, at most 100 pages; a duplicate installation or repeated cursor rejects the read, as the WebUI does).
For each enabled installation in order, each route in order with `navigable` and `navigation_kind == "admin"`
becomes an entry labelled `navigation_label` or else the plugin id, with the Puzzle icon and the description
"{plugin} plugin app.". The sidebar shows them as the last section "Plugin Apps" only when there is at least one
(label clamped to two lines, no external marker, as `SideNavItem`); command search appends a "Plugin Apps" group
matching label, description, plugin id, "plugin" and "plugin app". Native additions: a route path that is not a
plain absolute path (traversal, escapes, `?`, `#`, whitespace, an inner `*`) is not offered, a duplicate
(installation, path) pair is listed once, and a failed inventory read shows "Plugin Apps could not be loaded."
with Reload instead of silently hiding the section. There is no `/admin/...` deep link for plugin apps in the
WebUI (entries are plugin-content URLs), so the native route catalog is unchanged.

## Viewer plugin pages (security fix)

The existing viewer `PluginRoutePage` attached the account's bearer, profile and device headers to every
same-origin request a plugin page made, so plugin script could call any server API (admin APIs for an admin
account) with the user's authority. The official page's only credential is the launch cookie on the
plugin-content parent path. The viewer now attaches those headers only to requests inside that path
(`PluginContentPaths.IsWithinContentParent`); nothing else in the viewer changed (it still uses the bearer
header rather than the launch cookie and still passes `theme=dark`, which differs from the WebUI's
`midnight-cinema`; neither widens what the content can reach).

## Not implemented / limits

- WebSocket connections and WebRTC from plugin script are not intercepted by WebView2 resource filtering; the
  plugin-content proxy does not tunnel WebSockets, and no credential is available to such a connection.
- Same-origin subresource redirects are followed by the browser without a second filter check; the cookie's
  path still confines credentials, and the server refuses plugin redirects to other origins.
- Screen capture (`getDisplayMedia`) and printing were not exercised.
- Compatibility with named third-party plugins, visual parity of plugin content and behavior behind a reverse
  proxy base path are not verified.

## Verification

`-Stage final` run `pluginapps-final-r1` (2026-11-21T02:06Z–02:12Z UTC) on source `fa25ccc7c` passed.

- Unit: focused 91/91 (`AdminPluginAppsTests`, `PluginWebViewHardeningSourceTests`,
  `UserOnlyClientArchitectureTests`), full suite 10351/10351.
- Native `admin-plugin-apps` (real shell, sidebar, drawer, search, Frame and WebView2 against a synthetic
  loopback server that records every request line and header): entries from the inventory; opening a page via
  the launch; HttpOnly cookie on `/api/v2/plugin-content` only and empty storage; navigation to another origin,
  another installation and a server API cancelled with no request; `window.open` suppressed; foreign frame,
  fetch and image blocked; server-API and other-installation fetches blocked; an unknown-type message and a
  wrong-origin message reach nothing native; the plugin form posts once; the request log shows the admin token
  only on the launch request and only the launch cookie on plugin-content requests; removal and disable while
  open; 401, 403, 404, 500, dropped connection, refused launch (403, 503) and a hanging page each with Reload;
  role change while open closes the page with no later request; stale and detached shell entries; failed
  inventory retry; leaving the page tears it down. Captures: `admin-plugin-apps-desktop.png`,
  `admin-plugin-apps-drawer.png`, `admin-plugin-apps-state.png` (inspected).
- Added after the runs recorded here and not yet run: `admin-plugin-apps` steps for a fragment navigation, the
  plugin's own 404 on a later navigation, and a killed browser process followed by Reload (new web view, same
  profile and hardening, credential log re-checked). A render process exit has no step.
- Native `plugin-cutover` (viewer) including the new check that a plugin fetch outside the plugin-content mount
  carries no bearer, profile or device header; `shell-localization`; `admin-manage-foundation`;
  `admin-configure-general`; `admin-monitor`.
