# Admin Manage foundation

Official navigation and catalog reference: Silo-Server/silo-server
1452ea2d10efbe7d3ea5ce7140fb49756b493670. A fresh official-main fetch found
fd0d8c6c09af11aa2b165034e6c553a45334f2fd; its two additional commits change
watchsync and player markers, with no changes to this foundation's references.

This increment provides six Manage topics and 21 route identities. It does not
implement or register any production Manage page. Only finished, registered
pages appear in the sidebar or search. Existing Monitor pages remain built in.

## Domain integration

- Register a completed WinUI Page using an AdminPageRegistration singleton in
  App.xaml.cs. The shell validates Page inheritance and every route text key.
  Do not enable routes with placeholder pages or incomplete workflows.
- Pages receive an immutable AdminRouteRequest. Segments and query dictionaries
  are copied, use ordinal equality and remain stable in Back history. Use
  AdminShellPage.TryReplace for in-page URL changes; Open pushes navigation.
  Navigation swaps in place with no slide. Unknown server cross-links return
  Unsupported; direct route resolution retains the WebUI catch-all.
- Use AdminAuthorityLifetime, backed by the existing AdminMonitorAuthority.
  Open captures the active page owner; CaptureAction binds an action to it.
  Authority changes raise Retired, allowing an active page to clear data and
  reopen if available. Explicit Retire suspends without raising that event;
  call Resume only on actual page activation before Open. A queued callback
  cannot reopen a suspended lifetime. Dispose ends the subscription.
- Implement IAdminPageSuspension for outer Admin departure. Implement
  IAdminNavigationGuard with AdminLeaveGuard for staged edits. Logging Settings
  retains its existing guard and takes precedence.
- AdminCursorPager.Capture records request scope, revision and cursor. Pass that
  position to TryAdvance after a response; late responses from a different
  filter or earlier navigation cannot advance the pager.
- AdminCollectionWalk requires an explicit AdminCollectionScope identifying the
  resource and any filter. Complete filtered results are display data and never
  receive an AdminCompleteSnapshot. Only a complete unfiltered walk can produce
  that snapshot. Before a whole-set/order write, require its IsCurrentFor check
  using the exact target scope and action authority. Reaching a cap is partial
  data and cannot authorize replacement. Do not walk a whole media catalog.
- Guarded resource writes require their own strong ETag; never substitute list
  validators or automatically replay a 412. Temporary dependency_unavailable is
  a server failure eligible for the page's Retry state, not absent capability.
  Current v2 demo refusals are permission_denied and remain Forbidden.
- Badge sources own their authority-bound reads and clear on retirement. The
  shell ignores sources for unregistered routes and folds badges onto closed
  topics. Count badges use a radius bounded by their actual native size.
- Add new Admin source paths to docs/parity/admin-monitor-surfaces.json and
  approved published page resources to installer/build.ps1. Keep domain APIs
  separate from AdminMonitorApi. Shared source files have one integration owner.

Navigation labels and settings-search keys use the 206 official catalog keys,
including the pending-count ICU pattern. The 16 added icons preserve lucide
0.576.0 geometry; independent SVG paths each begin at their own origin.

## Evidence and limits

The lead reproduced the departure-restart and mutable-query regressions before
fixing them. Focused checks cover route registration, redirects, all route text,
search, scope ownership, cursor retirement, validators and error states. The
native foundation fixture checks replacement/Back, unregistered routes, search,
registered badges, generic dirty guards and outer-page suspension. Its test-only
page is assigned as Frame content after a published page is activated; the host
has no XAML metadata for activating its own Page type through Frame.Navigate.
The existing admin-monitor fixture also exercises the changed shell.

Local reports and logs: D:/SiloPlayer-build/server-update-nov14/manage-foundation/.
No production Manage page, live-server interoperability, complete visual parity
or all-DPI verification is established by these foundation checks.
