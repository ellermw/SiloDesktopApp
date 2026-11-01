# Match and Split implementation plan

1. Reproduce the missing inventory scope lifecycle with a failing regression, then implement a small core scope state with exact revision snapshots and tests for inference, choices, invalid inventory and retirement.
2. Add one reusable native library selector/refresh control. Integrate captured context, navigation and scope revalidation into Match search, selection and apply; preserve curator fallback.
3. Integrate selected-file scope and stale candidate/preview/commit guards into Split; align its authorization with the server.
4. Add an actual-control synthetic fixture covering scoped bodies, multi-library choices, membership changes, delayed replies and owner/permission departure. Publish Release under a fresh worktree output and provide runner route to parent.
5. Run full unit suite, diff check and self review, commit assigned branch, record exact commands/results/limits in report and append current-development handoff.
