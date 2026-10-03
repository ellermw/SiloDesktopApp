# Conditional editor verification

Reference: official public Silo-Server/silo-server `8e2e840474a085c6df6571a5a2850f7eb996810c`. Candidate remains unpublished and uninstalled. These refinements belong to original D01; they do not certify all conditional dialogs.

## Image type after a pending tab switch

The native editor captures the selected image but originally stores a completed Apply under the mutable visible tab type. `native-image-tab-red24.log` reproduces a real deferred poster Apply, enabled Backdrops tab navigation, completion, then return to Posters. The new poster is not Current because it was recorded under backdrop. Pinned ImageSelectorTab uses the selected image's type for both the request and completion.

Candidate now captures that selected type and uses it for the wire request and applied map, preserving enabled tabs. The native probe also requires exactly one write and immediate image/metadata-lock behavior. Published GREEN remains pending.

## Exact translation results

`native-translation-result-red24.log` invokes actual Translate on isolated metadata-AI/job endpoints for zero, one and three descriptions. The native generic success toast fails all three current source messages. Candidate now reports the zero-work result or exact singular/plural count from the completed job and clears the active progress line. Cancel does not undo immediate translation or write an unrelated metadata patch. Published GREEN remains pending.

The fixtures use fake authorities and no live tokens or production mutations. Earlier editor wide/narrow, field-diff/null/lock, pending422/retry and permission gates remain part of the final combined run.
