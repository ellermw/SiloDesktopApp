# November 13 - Marker edits retire watch prefetch

Independent branch codex/marker-cache-4448-nov13 starts from Beta6f9f87f7,
version1.2.1017-beta.1 /1.2.1016.1. Official upstream audit pin4448e401;
40b1adb0 motivated checking stale marker/watch ordering. Native does not re-read
/watch on reconnect and its marker editor already reads fresh markers on open.
The confirmed gap was a cached pre-save /watch result remaining current after a
successful local marker save or redetection request.

MarkerEditorDialog now publishes ItemMetadataRefreshed for a nonempty accepted
edit plan after authority revalidation. Accepted redetection publishes the same
context-scoped event. The existing UserStateRevision/WatchDetailFreshness layer
retires stale playback prefetches. No-change saves and revoked permissions send
nothing. This does not claim asynchronous server redetection has completed or
that updates from another client now have an additional native signal.

Verification:44 cache/marker unit cases passed, including an obsolete marker
snapshot being rejected and fetched again after metadata invalidation. An actual
marker-dialog save first reproduced the missing notification. Release publish,
native host build and marker-cache native fixture then passed, checking exactly
one notification for a changed save and accepted redetection, none for unchanged
markers or revoked authority, and the existing media-tool pending/lifecycle/
narrow-layout behavior. No live server or physical-client interoperability claim.
Evidence:D:/SiloPlayer-build/4448-marker-*.log and results/marker-cache.

This second increment is separate from submitted ranking PR148. Connection
quality and mounted-library scan refresh remain pending; submitted PR145 stays
pipeline-owned. No version stamps or installed-player files were changed.
Product DLL SHA256: D3B57A606DECCFC6022D64E39DDD4811783B43C27B42EE0467E637C57520BF89
