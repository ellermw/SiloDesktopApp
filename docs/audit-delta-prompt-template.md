# Delta Audit — Agent Prompt Template

Use this template when `audit-delta.ps1` reports an AFFECTED page. Fill in the
`{{placeholders}}` and dispatch as a general-purpose agent.

---

## Template

```
Delta audit for {{PAGE_NAME}}. The server updated and the following webui files
changed since the last verified commit ({{LAST_SHA}}):

{{CHANGED_FILES_LIST}}

**Webui sources — read 100%:**
- {{CHANGED_FILE_1}} (read the FULL file, not just the diff — the diff tells you
  WHAT changed, but understanding the context requires the surrounding code)
- {{CHANGED_FILE_2}} (if applicable)
- Any NEW components or hooks these files now import that didn't exist before

**Desktop source — read 100%:**
- {{DESKTOP_FILE_1}}
- {{DESKTOP_FILE_2}}
- (list all from audit-page-map.json desktop_files for this entry)

**Existing audit — read first:**
- {{AUDIT_FILE}} — read the existing full audit so you know what gaps were
  ALREADY catalogued. Your job is to identify NEW gaps introduced by this
  server update, not re-discover old ones.

**Output:**

Try Write tool: append a new section at the TOP of {{AUDIT_FILE}} with this
format:

```markdown
<!-- delta: {{NEW_SHA}} ({{DATE}}) -->
## Delta update — {{NEW_SHA}}

### Changed files
- {{file}}: {{one-line summary of what changed}}

### New gaps introduced
1. [severity] {{gap description}} ({{file:line}})
2. ...

### Existing gaps resolved by this update
- (list any, or "None")

### Existing gaps made worse by this update
- (list any, or "None")
```

If Write is blocked, return the delta section as your final message.

**Requirements:**
- Read the FULL changed files, not just diffs — diffs miss context.
- Cross-reference against the existing audit doc to avoid duplicating known gaps.
- Cite file:line for every new gap.
- Mark severity: visual / functional / critical.
- If the update introduces an entirely new section/feature on the webui page
  that the desktop doesn't have, flag it as CRITICAL with a full description
  of what needs to be built.
```

---

## Usage example

After running `pwsh scripts/audit-delta.ps1 -Pull` and seeing:

```
AFFECTED PAGES (1) — re-audit needed:

  [admin] web/src/pages/AdminLibraries.tsx
    Desktop: Views/Admin/AdminLibrariesPage.xaml, ...
    Audit:   docs/audit-gaps/FULL-AUDIT-admin-libraries.md
    Changed (1 file(s)):
      ~ web/src/pages/AdminLibraries.tsx
```

Fill in the template and dispatch:

```
Delta audit for AdminLibraries. Server updated, webui changed since 3760d00:
- web/src/pages/AdminLibraries.tsx

Webui sources — read 100%:
- F:\continuum-server\web\src\pages\AdminLibraries.tsx

Desktop source — read 100%:
- F:\ContinuumPlayer\src\ContinuumPlayer\Views\Admin\AdminLibrariesPage.xaml
- F:\ContinuumPlayer\src\ContinuumPlayer\Views\Admin\AdminLibrariesPage.xaml.cs
- F:\ContinuumPlayer\src\ContinuumPlayer\ViewModels\Admin\AdminLibrariesViewModel.cs

Existing audit — read first:
- F:\ContinuumPlayer\docs\audit-gaps\FULL-AUDIT-admin-libraries.md

Output: append delta section at TOP of FULL-AUDIT-admin-libraries.md.
...
```
