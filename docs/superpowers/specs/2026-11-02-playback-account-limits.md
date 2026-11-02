# Account playback limit presentation

Owner-approved server parity design: distinguish `stream_limit_reached` and `transcode_limit_reached` from server capacity. Match official WebUI titles **Streaming limit reached** and **Video conversion limit reached**; use trimmed nonblank server guidance, otherwise **Stop anything else playing or paused on your account, then try again.** Preserve terminal retryability and fresh-attempt starts after a refusal. Existing capacity errors retain their current wording. No new quotas, automatic retries or protocol fields.

Reference: official server/WebUI bf53b65450244751013a2965b39da4111d9a0f72, web/src/player/playback-errors.ts and docs/architecture/playback-protocol-v3.md. Owner approved full implementation and Beta PR delivery November 2, 2026. Start from freshly fetched origin/beta; pipeline owns version stamps and publication. No installed-player or production control.
