# Sidebar and menu pin titles

## Baseline and scope

- Desktop base: Beta `bdd033c1c`. Version numbers are left to the pipeline.
- Upstream: official Silo Server #4326 (`4af01e8`), checked against official
  main `17d5376ce`. It uses the existing library layout and Collections tab
  contracts.

## Behavior

Current translated layout titles and library collection titles name sidebar
pins, customized primary-menu pins, their navigation destinations, the
Settings Interface editor/action accessibility, and pinned collection row
headings. Display resolution never changes the persisted label or draft.
Pending/failed reads, missing targets and personal collections retain the
saved fallback. Only currently accessible libraries start sidebar title reads.

Optional reads wait for the shell's existing first-content gate. Pin title
snapshots use the existing five-minute freshness convention and remain outside
library/filter media invalidation. An already-mounted pin scope starts only
once per shell owner/metadata language, so later media-driven publications do
not turn stale time into periodic metadata requests. Page reads fetch again after a
completed read, refreshing titles across retained surfaces; overlapping reads
share one request per library/type and normalized authority/metadata language.
Individual caller cancellation cannot cancel another consumer, while the last
consumer leaving still cancels the transport. Access/profile/device/proof or
metadata-language retirement rejects old completions; transport-only catch-up
does not create a second read. Keyed section titles translate on display.

Retained sidebar/menu nodes, selection, expansion, editor actions and keyboard
focus are updated in place. Pinned collection headings update without
rebuilding cards or touching their scroll offset. Existing reduced-motion and
retained route behavior are unchanged, not newly certified.

## Verification

Local results on this source:

- **Unit tests:** all 7,497 pass, including the 24 `LibraryPinReadSharingTests`
  cases.
- **New native fixture:** `sidebar-review-pin-labels` passes all 18 checks. They
  cover the startup deferral, one bounded read per accessible library and
  type, current labels and accessible names, retained sidebar identity,
  selection and expansion, and Settings editor labels and focus through
  renames. They also cover the removed-target fallback and unchanged saved
  labels.
- **Existing native sidebar selectors:** `sidebar-detached-publication`,
  `sidebar-review-home-pin-load`, `sidebar-review-home-library-load`,
  `sidebar-review-pin-owner-read`, `sidebar-review-pin-owner-write`,
  `sidebar-review-interface-load`, `sidebar-review-nonhome-catchup` and
  `sidebar-review-closeout` all pass.
- **Already failing on Beta:** `sidebar-review-profile-use` (a timeout after 9
  passes) and `sidebar-publication` (a COMException after 53 passes) fail
  identically on clean Beta `bdd033c1c`. This change does not cause them.

The tests use a synthetic server. Keyboard and Narrator behavior with a live
server, reduced motion and full visual parity are not certified.
