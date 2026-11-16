# Reader responsiveness and inline CSS implementation plan

> **For agentic workers:** Use superpowers:executing-plans; the lead implements this independent increment inline.

**Goal:** Port official Silo #4331 and the applicable Foliate portion of #4325, keeping long low-match searches cancelable and preserving HTML chapter CSS containing `>` or `&`.

**Architecture:** Add the upstream segment checkpoints and 25 ms cooperative search budget to the vendored reader, preserving desktop overlap/excerpt fixes and result bounds. Serialize HTML-parsed chapters as HTML while keeping XML serialization for XML/SVG; the desktop does not use the WebUI sanitizer, so its TypeScript-specific change is outside this port.

**Tech stack:** Local ES modules, Node test runner and native WebView2 fixtures. No new dependency.

**Reference:** Official main `abd85d0cadac28f4ab7db6821dad9634fc114790`; commits `6fccf560745b20408f406a8f7843e344182b31f2` and `3489103a2d97731b6c6664006acf3290557ce2b6`. Desktop baseline `2c33599f751e1bb7e18a56ee378ffe918710d74d`, version 1.2.1145-beta.1 unchanged.

- [x] Add failing tests to `tests/reader-fixtures/search.test.mjs` for checkpoint opt-in, time-budget yielding and cancellation in no/few-match sections using a controlled clock.
- [x] Add a deterministic malformed-XHTML/CSS fixture and exercise the real Foliate loader through native `ReaderInteropFixture` checks.
- [x] Port the minimal changes to `search.js`, `view.js` and `epub.js`; update dependency adaptation notes.
- [x] Run Node search/location tests, related main tests, Release publish/matching host and native reader interoperability under `SiloOct18Build`.
- [ ] Record exact source/payload/checks and limits, review the diff and submit a separate qualifying Beta PR.

Preserve the existing 1,000-result cap, overlapping matches across text nodes, cancellation/late-result guards, inert book frames, CSP and all CFI/annotation behavior. Use only the task's reusable output; do not replace/clean shared Configure payloads or act on the installed player. No direct merge/release actions.
