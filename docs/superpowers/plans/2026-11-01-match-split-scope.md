# Match and Split implementation plan

1. Reproduce the missing inventory scope lifecycle with a failing regression, then implement a small core scope state with exact revision snapshots and tests for inference, choices, invalid inventory and retirement.
2. Add one reusable native library selector/refresh control. Integrate captured context, navigation and scope revalidation into Match search, selection and apply; preserve curator fallback.
3. Integrate selected-file scope and stale candidate/preview/commit guards into Split; align its authorization with the server.
4. Add an actual-control synthetic fixture covering scoped bodies, multi-library choices, membership changes, delayed replies and owner/permission departure. Publish Release under a fresh worktree output and provide runner route to parent.
5. Run full unit suite, diff check and self review, commit assigned branch, record exact commands/results/limits in report and append current-development handoff.

Review corrections: reproduce each reported behavior on the first final publish (review-red, review-red-02), apply focused fixes, then run core/API tests and a fresh app publish. The full match-split-scope route now includes the review regressions and existing media-tools fixtures. Finish with a final full unit run, source review, new receipts, report responses to each finding, and parent re-review; no remote actions.

Spec-axis follow-up: freeze 8a325bd1 artifacts, reproduce the three findings in six actual-control cases (spec-red-02), change the three owning UI paths only, then focused/full unit checks, a fresh Release publish and the complete native route. Parent source-reviews this correction and its red/green evidence; no generic additional review loop or remote action.

External PR #67 follow-up: pin remote head before mutations; reproduce scoped-empty, four actual loaded-card rebind paths, and accepted Split preview path/root drift against frozen F6AF bytes. Add each contract field's unit regression; make focused scope/card-lifetime corrections only. Run focused API/policy/unit green, full unit, fresh Release publish and the complete native route including online-only cases. Record source fingerprints and exact artifact hashes; commit locally for parent review and delivery. No push, replies or thread resolution.
