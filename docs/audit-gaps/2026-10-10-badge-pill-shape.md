# Oval poster and media-detail badges

The user reported that quality/HDR labels on poster cards have oval backgrounds instead of the WebUI's straight edges and round ends, and supplied an Eternally Yours detail screenshot showing the same problem for year, content rating, season count and episode count. This is a desktop rendering defect, not an unavoidable native difference.

Official public Silo server main was freshly fetched for this comparison: `c32feb16d6a4373216527e8ebaf0107ebedadad9`. `web/src/components/overlays/CardOverlays.tsx` uses CSS `borderRadius: "9999px"` for full-round presets. The overlay source and preset definitions are unchanged from the previous reference. No legacy GitLab source was used.

## Cause and original reproduction

`PosterCard.BuildBadge` passed uniform radius9999 directly to a WinUI Border. Six ItemDetailPage metadata Borders used999. CSS reduces an oversized uniform radius equally in both axes to at most half the shorter side. WinUI paints those raw oversized values with elliptical corners, leaving an oval top instead of a straight segment. A radius appropriate for the actual measured size is required.

The native fixture invokes the actual production poster builder and constructs ItemDetailPage from its real compiled XAML. Before the repair its rendered flat-edge checks failed, including all six metadata Borders. The original metadata render had top-edge alpha0 for the longer labels; after the repair it is255 across the full expected straight segment. Original and repaired images and failure logs are preserved in `.codex-tmp/overlay-pill-oct10/red`, `red-media` and `green`.

The prior fixture tested fonts, padding, colors, dimensions and raw fixed radii. It did not test the painted straight edge of full-round badges, so it missed a visible front-page defect. Passing that fixture was insufficient evidence of complete visual parity.

## Repair

`RoundedBadge` clamps the uniform configured radius to half the actual shorter side and updates it on SizeChanged. The shared poster builder uses it for all presets. This also handles fixed presets when scaling would otherwise make their radii too large. The six media-detail badges opt into the same normalization: year, content rating, advisory age, runtime, season count and episode count. Fonts, padding, colors, placement and playback behavior are unchanged.

The attached handler belongs to each Border and uses a static callback; it has no global subscription, timer or visual-tree scan. Size changes update CornerRadius only when the normalized value differs.

## Verification boundary

The focused fixture checks36 actual poster variants: classic/pill, three scales, icons on/off and4K/1080P/DV HDR10. It tests the opaque straight top edge and circular ends after enlarging the text. It also checks the six actual metadata Borders. All42 pass after repair, and the colored rendered grid was visually inspected. Final installer verification and checks are recorded in the1.2.242 release note.

This repairs the reported badge shape. It does not complete the paused remaining parity work or claim every screen and interaction matches the WebUI. Other uses of large corner radii outside these poster and media-detail badges have not been certified by this narrow repair.
