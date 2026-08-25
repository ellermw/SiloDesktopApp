# Automated review and remediation

This repository uses a guarded pull-request loop for CodeRabbit review and Codex
remediation. It never merges a pull request or pushes directly to `main`.

## Execution environments

- CodeRabbit and Codex run on the repository-scoped self-hosted runner selected
  by `self-hosted`, `linux`, `x64`, and `app-review`.
- Windows x64 build and tests run separately on GitHub-hosted Windows.
- Generated patches are applied and pushed by a GitHub-hosted Ubuntu job. That
  job has repository write permission but no access to the self-hosted runner's
  Codex or CodeRabbit authentication files.

The self-hosted runner is intentionally limited to trusted, same-repository pull
requests targeting `main`. Forks and untrusted actors are rejected before an AI
job is scheduled.

## Review cycle

1. A trusted owner pushes a pull-request commit.
2. The workflow resolves and records the exact head and base SHAs.
3. Windows validation and CodeRabbit review run against that head.
4. CodeRabbit NDJSON is validated and converted to a versioned JSON report.
5. One marker-based PR comment is created or updated with the review summary.
6. If Windows validation passes, no critical issue exists, and fewer than five
   fixes have been attempted, Codex verifies and remediates the findings.
7. Codex emits one structured disposition per finding and a binary-safe patch.
8. A separate hosted job rejects stale PR state, applies the patch, pushes it to
   the PR branch, and explicitly dispatches the next review.
9. The loop ends when review is clean, a critical or blocked condition requires
   owner attention, validation fails, no safe patch is produced, or five
   remediation cycles have completed.

The automation branch itself is review-only: it cannot automatically modify its
own workflow implementation.

## Authentication

The self-hosted service account uses protected persistent authentication at:

- `/home/silo-review/.codex`
- `/home/silo-review/.coderabbit`

Those directories are host configuration and must never be copied into a
checkout, workflow artifact, log, comment, or repository secret. Treat both
authentication files as passwords. Repository setup/build scripts must run in
jobs that do not receive AI credentials.

## Manual dispatch

After the workflow exists on `main`, an owner can review a particular current PR
head with:

```bash
gh workflow run automated-review.yml \
  --repo ellermw/SiloDesktopApp \
  --ref main \
  -f pr_number=1 \
  -f expected_head_sha=<exact-current-head-sha> \
  -f cycle=0
```

The dispatch is rejected if the supplied SHA is stale.

## Validation commands

Automation fixtures and parsers:

```bash
node tests/automation/automation.test.mjs
node --check scripts/automation/guard-context.mjs
node --check scripts/automation/parse-coderabbit.mjs
node --check scripts/automation/validate-remediation.mjs
```

Workflow YAML is validated with `actionlint`; `.github/actionlint.yaml` declares
the repository's custom `app-review` runner label.

## Operational limitations

- The repository's current GitHub plan does not enforce branch protection for
  this private repository. Automation therefore never merges and the owner must
  keep `main` changes manual.
- GitHub-hosted Windows availability and package restoration remain external
  dependencies. A failed Windows job blocks remediation rather than guessing.
- ChatGPT-managed Codex authentication is rate-limited by the authenticated
  account. Exhausted limits stop the workflow safely.
- CodeRabbit service or authentication errors fail the review and prevent any
  patch from being generated.
