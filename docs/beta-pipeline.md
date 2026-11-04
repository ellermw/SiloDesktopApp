# Beta pipeline

Pull requests into `beta` go through an automated pipeline that runs on the OpenClaw LXC. Nothing on a developer's PC takes part.

1. **Discovery.** Every minute the LXC looks for open, non-draft pull requests into `beta` by `ellermw` from this repository. Other pull requests are ignored.
2. **Review.** Claude Sonnet, Claude Opus, DeepSeek V4.1 Flash and MiniMax M3 review the exact head commit, each with its own focus. They can read the code but cannot change it. All four are required, and a reviewer that fails retries until it finishes.
3. **Tests during review.** When it's free, the Windows QA VM tests the head commit at the same time. It runs the unit test projects, plus the native and GPU or playback tests that match the changed paths.
4. **Decisions.** Findings are handled by severity:
   - **P0 and P1** block the pull request for the owner. Other reviewers, including Kimi K3, verify each one, and the fixer proposes a fix that all of them check. Claude Fable adds an advisory opinion. The owner approves or denies.
   - **P2** findings are judged by Claude Fable. A deny needs quotes that exist in the code and at least 80% confidence, and it lasts for that review round. Anything else goes to the fix stage. The owner can overrule either way.
   - **P3** findings are advisory.
5. **Fix.** GPT-6.1 Sol, with Claude Opus as its fallback, edits a confined copy of the source, and the pipeline commits the change to the pull request branch. The new head gets a full review again. There is no limit on fix attempts.
6. **Tests.** Tests that have not already passed on this head run in the QA VM. A failing unit test is rerun alone, and one that then passes is reported as flaky and doesn't block the merge.
7. **Merge.** The pull request is squash-merged into `beta`. If it conflicts with `beta`, the pipeline merges `beta` into the pull request first, and that new head gets a full review.
8. **Beta release.** Merged pull requests wait until 5 are waiting, one of them changed 40 or more files, the oldest has waited 24 hours, or nothing has been in progress for 10 minutes. The next version is then stamped onto `beta`, the QA VM builds the installer and update assets from that commit, and the pipeline publishes a GitHub pre-release tagged `v<version>-beta.<n>`. The app offers pre-releases only to users who opt in to the Beta channel, so stable users are not offered it.
9. **README.** After publishing the beta tag, the pipeline opens a pull request against `main` to update the badges and Beta download section of the README. Merging it into `main` and syncing it back into `beta` require the owner's approval; the pipeline does not automatically push or merge changes to `main`. The README update is not part of the already-published beta tag.

The pipeline assigns all version numbers, so pull requests should not change them.

Stable releases and automatic changes to `main` are outside this pipeline. Promoting a beta to stable needs the owner's approval and follows the release-delivery requirements in [AGENTS.md](../AGENTS.md).
