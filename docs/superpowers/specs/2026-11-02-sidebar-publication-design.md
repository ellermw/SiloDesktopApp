# Stable sidebar publication

Task 2 of the approved browsing milestone. Base: Beta `6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af`, version `1.2.544-beta.1`. The current-development stable handoff is historical; this deliberately newer Beta source is authoritative.

Coalesce library, pin and customization notifications into one dispatcher publication. Reconcile library IDs, pin type/ID within a library and primary semantic keys in place. Surviving nodes retain selection, keyboard focus and expansion; current labels, icons, tags and unpin actions reflect new data. Only additions, removals and moves change vectors. Preserve hidden libraries, fixed routes, capabilities, plugin Apps and pane preferences.

A changed server/viewer/profile retires authorized library, pin and custom primary nodes synchronously before replacement reads. Pending callbacks and awaited results must validate current shell and request context. Hide and access invalidation retire old library ownership, including same-ID replacements.

Shell/node ownership uses the existing API identity generation. Request-only catch-up retains that identity; each operation still validates its captured request context. AccessChanged explicitly retires authorized library/pin data. UI-thread authority callbacks retire immediately; background notifications marshal to the owning dispatcher.

Review corrections: explicit retirement also advances callback ownership, including while playback is expanded; sidebar hydration waits until it is useful. Access-only retirement retains builtin primary shortcuts and plugin Apps, while library-dependent shortcuts wait for a fresh accepted library snapshot. Menu settings reads/save/reset/override completions commit only under their captured request context and service invalidation generation. Surviving pins regain enablement after an aborted operation. Duplicate pin keys use their first occurrence without automatic writes and recover from duplicated old native vectors. Moved-node focus restoration runs after layout and yields to newer explicit focus or shell retirement.

Verification uses synthetic APIs, isolated settings and owned offscreen MainWindow controls. No installed app, credential-store calls, real account, global input or installer. Native identity/vector/focus/selection checks prove publication behavior; they do not establish physical-device performance or complete visual parity.
