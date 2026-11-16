# Reader cooperative search and HTML styles

Official reference: `abd85d0cadac28f4ab7db6821dad9634fc114790`, with #4331
`6fccf560745b20408f406a8f7843e344182b31f2` and the applicable Foliate part of
#4325 `3489103a2d97731b6c6664006acf3290557ce2b6`. Independent desktop baseline:
Beta `2c33599f751e1bb7e18a56ee378ffe918710d74d`, version 1.2.1145-beta.1 /
numeric 1.2.1144.1, unchanged.

Long segmenter searches now expose opt-in checkpoints every 500 segments. The
view yields after 25 ms as well as every 100 matches, then checks cancellation.
A section with few or no matches can no longer hold cancellation until its scan
finishes. Checkpoints never become search hits, CFIs or outlines. The existing
1,000-result bound, excerpt windows, overlapping cross-node match correction,
late-result guards and ordinary substring search are preserved.

Chapters parsed as HTML, including invalid XHTML, now serialize as HTML. Their
raw-text CSS keeps child selectors and ampersands. XML/SVG retain XML
serialization. This desktop reader does not use the WebUI TypeScript sanitizer;
only the applicable vendored loader change is ported. Existing scripts-disabled
frames, CSP and host/network boundaries remain in force.

## Verification

- Three new Node regressions failed on the original search implementation:
  missing checkpoint opt-in, and no time-budget yield with zero/one matches.
  Final Node search/location suite: 26 passed, zero failed/skipped.
- The deterministic new EPUB failed in actual native WebView2 on the earlier
  payload's unchanged loader: `HTML chapter serialization preserves CSS child
  selectors`. The existing unequal-chapter EPUB and four-page PDF retain their
  original bytes after fixture regeneration.
- Final related main suite: 43 passed. Release publish and matching native host
  build passed under `SiloOct18Build`.
- Native reader interoperability passed: serialized CSS text and computed CSS
  child-selector/string values, disabled book scripts, no-match cancellation,
  EPUB CFIs/annotations/legacy locations, PDF rendering/resume and stale-profile
  progress-save rejection. The frame checks use the actual packaged Foliate
  loader and native WebView2; the search clock is deliberately controlled to
  avoid machine-speed-dependent assertions.

Final app DLL SHA256:
`9C26BA933CF59CBF313F1177F597206239D606B77A93B9EC31C6AA1C10B82297`.
Source and published `search.js`, `view.js` and `epub.js` hashes match and are
bound in task-local `.codex-tmp/reader-final-receipt.json`. Logs are
`reader-search-{red,green,final}.log`, `reader-css-red.log`, `reader-focused.log`,
`reader-publish.log`, `reader-native-build.log`, `reader-native.log` and
`native-results/reader-final/renderer/results.txt` under that task worktree.

No live account or complete publication-format/visual/accessibility parity is
claimed. Shared Configure outputs and the installed player/settings were not
touched. The task uses one reusable build/publish/reference area; no installer,
direct merge or release action was performed.
