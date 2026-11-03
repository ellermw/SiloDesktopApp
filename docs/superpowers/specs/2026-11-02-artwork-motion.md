# Motion-aware artwork — Task 3

Approved scope: independent Beta change from ac51554902c252814191dd88cf4b1d991efdfea6; pipeline version 1.2.544-beta.1 remains unchanged. Preserve native controls, cache bounds, URL/identity/cancellation authority, placeholders, text, focus and Home publication.

A viewport owns motion state; fast displacement postpones new artwork fetch and decode until slow movement or a short quiet interval. Recheck after expensive asynchronous boundaries, without holding a class decoder semaphore while waiting for motion. Retain immediate decoded-cache hits. Actual LibraryPage scroll events govern bounded LibraryGridCard slots; actual ancestor scroll viewports govern shared PosterCard/LandscapeCard. No global strong control registry.

Rebinding, hiding, unloading and library navigation departure cancel pending work. Retained library return restarts missing current artwork. Competing viewports remain independent. Evidence uses controlled time and synthetic offscreen native controls with owned ChangeView. Request and native source/decode counts establish behavior only; physical input/frame pacing and historic buffering are not certified.

Final lifecycle details: the library's existing bounded warmup shares its viewport policy and retains the initiating range/API context and cancellation token through deferred work. A DTO bind captures artwork identity; native unload/reload does not authorize an outgoing DTO for a successor profile. Hidden tabs, navigation departure and direct audiobook group-to-books return use the same activity boundary. Existing bind grace periods, network/decode concurrency and decoded-cache bounds remain unchanged.

Scope trace: PR80 commit11058ededd75a8e4b4dcd5c20132dc130835b3bf builds sibling cards in ItemDetailPage.Cards.cs:17/32 via CreateEpisodeCard (ItemDetailPage.xaml.cs:7970), which creates a bespoke Border/Grid still and calls LoadEpisodeStillAsync. Its ScrollView does not own the shared card classes scheduled here. Per parent ruling, bespoke episode artwork is deliberately excluded; no unused ScrollView adapter or PR80 source is imported.
