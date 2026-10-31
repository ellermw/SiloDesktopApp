# Beta pipeline

Pull requests into `beta` go through an automated pipeline that runs on the OpenClaw LXC. Nothing on a developer's PC takes part.

1. **Discovery.** Every five minutes the LXC looks for open, non-draft pull requests into `beta` by `ellermw` from this repository.
2. **Review.** Claude Sonnet, Claude Opus, GLM-5.3-Flash and MiniMax M3 review the exact head commit. They can read the code but cannot change it.
3. **Gate.** P0 and P1 findings block the pull request for the owner. P2 findings go to the fix stage. P3 findings are advisory.
4. **Fix.** Claude Opus edits a confined copy of the source and the pipeline commits the change to the pull request branch. The new head is reviewed again, up to three attempts.
5. **Tests.** The Windows QA VM builds the head commit and runs the unit test projects.
6. **Merge and stamp.** The pull request is squash-merged into `beta`, and the next patch version is stamped onto `beta`.
7. **Installer.** The QA VM builds the installer and update assets from the stamped commit.
8. **Beta release.** The pipeline publishes a GitHub pre-release tagged `v<version>-beta.<n>`. The app's updater ignores pre-releases, so stable users are not offered it.

Stable releases and `main` are outside this pipeline. Promoting a beta to stable needs the owner's approval.
