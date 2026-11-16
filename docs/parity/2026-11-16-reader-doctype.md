# Reader HTML doctype preservation

Reference: official Silo Server main abd85d0cadac28f4ab7db6821dad9634fc114790.
Desktop base: 40ab7889ef1d015a8ee07ef1962870763f95f66c (1.2.1151-beta.1).

PR177 correctly uses HTML serialization for invalid-XHTML chapters to preserve
raw-text style content, but its doctype reconstruction retained only the name.
A legacy HTML/XHTML public or system identifier therefore disappeared. The
reader now serializes the existing DocumentType node with XMLSerializer and
continues to serialize the document element as HTML.

The inline-styles EPUB fixture now has the XHTML 1.0 Transitional public/system
identifiers and still forces the invalid-XHTML fallback with a bare ampersand.
The real Chromium loader verifies both identifiers, the child selector and CSS
ampersand rendering, and inert book scripts. The initial run failed specifically
on the missing public identifier; the final run passed all 42 renderer checks.
All 26 Node location/search tests pass unchanged.

Task-local evidence (lead worktree):
- .codex-tmp/reader-doctype-red.log
- .codex-tmp/reader-doctype-green.log
- .codex-tmp/reader-doctype-node.log
- .codex-tmp/reader-doctype/receipt.json

No C# or native engine change; no .NET/native build or physical-device/live-server
claim. This check uses the actual packaged reader sources in Chromium and its
script-disabled sandbox. It does not certify every EPUB or all visual layouts.
