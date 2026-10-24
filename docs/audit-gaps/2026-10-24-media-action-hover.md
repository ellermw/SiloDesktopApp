# Media-page primary button hover

Baseline: released1.2.304, desktop commit `2d64ffb16b4b6f43975d007f2543954c4d5dada2`. Official Silo main fetched directly before this focused comparison: `60cd81e034df220701a41a7c25876e8d3983c224`. Reference: `web/src/pages/ItemDetail/components/ActionBar.tsx` and `web/src/components/ui/button.tsx`. This is a hover repair, not a new full upstream audit.

## Reproduction and cause

The real compiled `ItemDetailPage.PrimaryPlayButton` used the stock WinUI Button template, while `SplitPlayButton` independently painted the accent background and progress overlay. On the exact released payload, the pill measured84x44 and the actual button84x39. Entering native `PointerOver` repainted8,408 pixel channels, covering the primary/progress paint with the generic dark hover fill but leaving an exposed strip. The user's reported partial gray overlay is reproduced without controlling their installed player or making account/network changes.

The official ActionBar explicitly keeps the primary background on hover and uses150ms compositor transforms: hover1.02, pressed0.98. Its primary labeled secondary actions share these transforms; favorite/More controls remain static glass actions. The native Play button had neither the correct template nor that feedback.

Reproduction evidence: `.codex-tmp/detail-hover-oct24/red-paint/{results.txt,detail-actions-normal.png,detail-actions-hover.png}`. The regression selector is `detail-action-hover` in `SiloPlayer.NativeRegressionTests`. The first two fixture-launch attempts exposed test-host setup issues (source asset inputs and a copied self-contained hostfxr); those are not product failures. The first realized fixture used an unrealized WrapPanel's null visual parent; removing through its owned Children collection gives the minimal reproduction above.

## Repair and verification scope

Candidate1.2.305 gives only the media primary button a transparent, full-height explicit template. `DetailActionFeedback` uses its real native common states to animate the whole painted pill (including progress) and the Watched button, centered on current geometry. It respects disabled system animations, resets on disable/unload, reattaches on reload, and leaves action handlers and playback logic intact. Keyboard focus visuals and accessible names remain native.

The native regression exercises the published controls and rendered pixels,12 label/progress combinations, hover/pressed/normal, Watched/static Favorite feedback, focus/name, disabled state and unload/reload. Native visual-state transitions are driven programmatically in an isolated offscreen host; this does not claim a physical-mouse or all-page parity pass. The installed player remains open. No commit, push, publication or installation is authorized by this repair request.

Final verification:2,120 main tests and four packaged native groups pass. Final hover evidence is `green-states`:44px pill and44px button, zero changed paint channels after normalizing the intentional compositor transform. The same normalized test still reports8,408 changed channels against released304 (`red-normalized`). An earlier candidate comparison counted the intended scale's2,130 edge/text changes as a repaint; normalizing only the composition transform separates geometry feedback from the background bug. A later fixture-only correction retains the named label before detaching its parent from the page's namescope. These are preserved test-harness refinements, not additional product fixes.

Related successful groups: `final-shared-controls`, `final-oct20-watched` and `final-media-parity` (first-navigation: series/season/episode/movie at1280px and460px, including Back revisit). All565 packaged files match the isolated mirror.1,458 final source/test/build inputs are recorded. Setup and app hashes are in [the305 release record](../releases/1.2.305.md) and `installer/output/SiloInstaller-1.2.305-verification.json`.
