# Skip-marker details

Implements approved server-alignment report D against official main `63ef34b236b88dc77acca1a9a3ed90235377691f`; desktop Beta base `a15621a0d6d5960d6b31a620a23ae259d734dedb`.

Preserve optional marker source/provider/confidence/algorithm through watch DTOs, live replacement and current-version/repeated-occurrence selection. Support current timestamp wire fields while retaining older range payload compatibility.

Show marker confidence/source switches only for the typed revision24 contract with effective entries present. Both default off and resolve for the active profile/device; never send unsupported keys or carry a prior profile's values into playback. Existing typed save gates remain.

Native and OSC skip pills display a separate muted line with confidence then source, matching `web/src/lib/markerProvenance.ts`. Missing confidence stays absent; finite values clamp to0..1 and round to whole percentages. Source labels are Detected, Edited, TheIntroDB, Online, or the unknown provider's name. Algorithm identifiers remain diagnostic metadata. Labels are text, never ASS markup.

Approaches: reuse a shared C# label formatter and send a separate cosmetic OSC message (chosen), versus duplicate provenance logic in Lua (rejected due drift), or append labels to marker payloads (rejected because those messages own skip/undo identity). Cosmetic changes cannot reset timing, pause, room authority, auto-skip or undo state.

Read/setting results and queued rendering belong to one authority/context/load generation. Metadata for a previous file/version must not leak to its replacement. Repeated occurrences use the singular range's provenance for that kind.

Verification uses actual API DTO reads, unit reducers/formatters, Lua runtime cases and synthetic native controls. No installed-player or production setup actions. Delivery is a focused Beta PR; pipeline owns release stamps and artifacts.
