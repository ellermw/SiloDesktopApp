# November 13, 2026 upstream update: Auto ranking and multipart intent

Official upstream pin: 4448e401b30a1828a34a1e2676f83b9b0c04d8e0 (43 commits
since 7edef3dd). Desktop base: 6f9f87f70439a327677e851cc64afbf717417a73,
1.2.1017-beta.1 / 1.2.1016.1. This independent increment implements #4070
(85d1c166); it does not extend an already submitted PR.

Within a cut, Auto ranks resolution, HDR and larger file size, retaining the
server's top-level order for complete ties. With no remembered/picked cut, each
cut's Auto file competes by cap fit, resolution and HDR, then edition preference
and server order. File size is excluded across cuts. Native explicit/effective/
history edition authority, low-resolution tiers and card/next-up Resume policy
are preserved.

A saved later multipart file is retained only when its position is positive,
including rewatch with Played=true. Start Over resolves the selected presentation
from its first part. File-specific audio/subtitle indexes, codec and label are
not carried into a different part; subtitle Off and portable language/source/
flags remain. Downloads follow part order, retaining the selected file in its
own part.

Seven new regression cases failed the old behavior. Final related unit run:
67 passed, zero failed/skipped. Release publish and native host build passed.
Actual oct24-track-choices and detail-action-hover native fixtures passed,
including saved later-part Resume. Core tests verify restart file/track transfer;
the native fixture does not launch a new full playback session for Start Over.
An independent Claude review found no P0-P2 findings; its static review did not
run tests. No live-server, physical-network or exhaustive visual-parity claim.
Evidence: D:/SiloPlayer-build/4448-ranking-*.log and native results directories.

The audit identified further independent increments: connection-sensitive
quality, marker-save watch-cache invalidation, and mounted library scan refresh.
Those remain pending and are not included here. API inspection found no new or
removed native operations; plugin signature fields and node frame rate do not
change client contracts used by Desktop. Browser HDR/font/CORS and server GPU/
transcode fixes need no native protocol change. Metadata close/count layout is
already separated natively. Viewer locale keys did not change; admin keys are
excluded from the pending localization catalog. Full dispositions will be
recorded after the remaining increments. Submitted PR145 stays pipeline-owned.

Product DLL SHA256: 3017A43E1620331C10B7C531D139F75CF1340AB4DB32E544E894CB65CA593A1E.
