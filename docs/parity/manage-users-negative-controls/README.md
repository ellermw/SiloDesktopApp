# Users semantic negative controls

All controls are unapplied/unexecuted. They preserve types/signatures and are
intended to demonstrate behavioral assertions, not compiler failures. Controls
01-07 preserve the earlier read-only worktree's authored experiments. Control
08 covers the stricter retained-action recovery in this integration. Controls
09-10 reverse the two bounded-review corrections; they are source-authored only.

| Control | Exact FullyQualifiedName filter |
| --- | --- |
| 01-android-completed.patch | AdminUsersPresentationTests.AndroidRequestedIsNotProofOfStorage |
| 02-exact-device-identity.patch | AdminUsersRulesTests.PreferenceIdentityRoundTripsEveryQualifier |
| 03-stale-account-validator.patch | AdminUsersTransportTests.ChangedRenderedAccountRequiresReviewBeforeAnyWrite |
| 04-admin-self-profile-protection.patch | AdminUsersRulesTests.NonOwnerSelfCannotChangeAdminProfilesOrPins |
| 05-home-hidden-retention.patch | AdminUsersPresentationTests.ReorderingCannotUnhideAnInheritedHiddenSection |
| 06-write-auth-refresh.patch | AdminUsersTransportTests.MutationErrorsNeverReplayOrRefresh |
| 07-avatar-preview-origin.patch | AdminUserAvatarTests.PreviewUsesOnlyKnownPresetOriginsWithoutAccountHeaders |
| 08-unknown-write-replay.patch | AdminUsersWorkspaceTests.UnknownWriteOutcomeCannotUnlockTheRetainedActionAfterReadback |
| 09-temporary-password-flag.patch | Native selector `admin-users`: unchanged temporary-password selection submission |
| 10-home-sparse-order.patch | AdminUsersPresentationTests.ReorderingSparsePositionsPreservesUnrelatedSectionsAndRemovedOverrides |

After the root scheduling/capacity grant: establish a compiling baseline, record
source hashes, apply exactly one patch, compile and run only its named test,
observe a behavioral assertion failure, reverse that exact patch, compare source
hashes and rebuild/rerun GREEN. Stop rather than weakening assertions. Never
combine controls or use negative product bytes in the native/reference payload.
Control 09 requires an isolated app/host rebuild followed by the normal
`admin-users` native entry point; it is not an xUnit filter. Restore and rebuild
the original product bytes before any subsequent positive/native verification.
