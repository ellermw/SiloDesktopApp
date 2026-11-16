# Edit Metadata write ownership and fresh opening

Viewer audit sections 6a and 6b.

## Source and upstream

- Desktop base: Beta `bdd033c1c`. Version numbers are left to the pipeline.
- Upstream: official Silo Server main `8f93594ce`.
  - [#4329](https://github.com/Silo-Server/silo-server/pull/4329) (`5007dd4`):
    shared mutation keys, item predicates, Save/Reset guards, and the Reset
    job and refetch lifecycle (`EditMetadataDialog.tsx`, `ImageSelectorTab.tsx`,
    `hooks/queries/items.ts`).
  - [#4241](https://github.com/Silo-Server/silo-server/pull/4241) (`1459f6c`):
    opening-specific form state and local edits over current item values and
    locks.

## Behavior

`MediaMaintenanceApi` already has app-lifetime singleton registration. It now
owns `ItemMetadataWriteRegistry`, keyed by the complete `ApiIdentityContext`
(server URL, profile, authentication and identity generations) and content ID.
The registry counts overlapping writes and shares a completion task with every
opening. Closing an observer does not release or cancel another operation.
Entries are removed after the last write and its refetch settle. Only a refetch
started after the latest write completion may supply the settled item; a slower
older read cannot replace newer values or locks.

Image Apply, metadata Save, and Reset to Provider use that registry. The dialog
lifetime token admits a write and cancels its UI observation; once admitted, a
write uses its own two-minute budget. Identity/access retirement cancels the
background operation. A separate 30-second item-read budget reconciles even a
failed or ambiguous request; mutations are never replayed by the registry.
Editor resets follow their queued job within that budget before the refetch,
using identity-bound reads and transient read retries that respect Retry-After.
After an accepted POST, budget expiry, forbidden/missing job reads or unknown job
states return an explicit queued outcome, not a failed write. The editor reports
"Metadata reset queued. Provider changes will appear when the job completes."
and closes, preserving the base queued-reset behavior. Controls remain guarded
through the bounded wait and reconciliation, not indefinitely for an unreadable
or long-running server job. Reopening after that release does not guarantee the
job has completed; there is no background job monitor or automatic POST replay.
Identity/access retirement remains cancellation, and known failed/canceled jobs
remain errors. The existing general Refresh Metadata action is unchanged (audit 6d).

Reopened editors disable Save, Reset and new image applies while a matching
operation/refetch is pending. Existing `web.item.dialog.saving` copy provides
the visible pending state. A failed item reconciliation keeps the form disabled
and provides existing localized load-error/Retry copy, separately from the write
outcome. Accepted writes publish their change before refetch, and Save/Apply/Reset
still report success when only reconciliation fails. Image Apply retains its
Current badge and local artwork lock. A subsequent successful read restores the
controls. Unedited fields adopt refetched values, while this
opening's edits and explicit lock overrides survive; full lock submissions use
the refetched server lock set. Programmatic control updates do not create edits.

Both card and detail openings capture the registry revision before the item
read. A write settling between that read and the dialog opening forces another
reconciliation. The detail menu now fetches current item detail, then checks the
captured request authority, navigation instance, current content ID and curation
permission before constructing the dialog. Its read is cancelable and bounded;
the action disables while opening and uses the existing error-toast mapping.
Read-retirement subscriptions are detached before showing the editor. Post-save
reload checks identity, access and navigation instead of the opening request
context, so a same-owner transport reconnect during editing cannot suppress it.
Existing retained page state and navigation animation policy are unchanged.

## Changed files

- `src/SiloPlayer.Core/Services/ItemMetadataWriteRegistry.cs`: shared ownership,
  overlap counting, completion observers, revision and bounded write/refetch.
- `src/SiloPlayer.Core/Api/MediaMaintenanceApi.cs`: singleton registry and the
  editor-specific reset job wait.
- `src/SiloPlayer.Core/Api/CatalogApi.cs`: identity-bound detail read overload
  for post-write reconciliation.
- `src/SiloPlayer.Core/Services/MetadataEditState.cs`: rebase unedited fields.
- `src/SiloPlayer/Views/Dialogs/EditMetadataDialog.Ownership.cs`: dialog registry
  observation, account/access cancellation, refresh/retry and control rebasing.
- `src/SiloPlayer/Views/Dialogs/EditMetadataDialog.cs` and
  `EditMetadataDialog.Localization.cs`: guarded writes, pending UI and locale.
- `src/SiloPlayer/Controls/MetadataTagsInput.cs`: refresh displayed tag values.
- `src/SiloPlayer/Controls/MediaItemMenu.cs`: opening revision.
- `src/SiloPlayer/Views/ItemDetailPage.xaml.cs`: fresh guarded opening read.
- `tests/SiloPlayer.Tests/ItemMetadataWriteRegistryTests.cs`: lifecycle,
  canceled observer, identity isolation, overlapping writes, failed write
  reconciliation, authority retirement, revision and canceled admission.
- `tests/SiloPlayer.Tests/MetadataResetOwnershipTests.cs`: real API path with a
  gated queued job and gated refetch after close; failed/canceled jobs do not
  replay the POST.
- `tests/SiloPlayer.Tests/EditMetadataOwnershipContractTests.cs`: fresh detail
  opening source contract and refreshed values/locks with retained local edits.
- `tests/SiloPlayer.Tests/CurrentItemCardParityTests.cs`: updated constructor
  source assertion for the revision argument.
- `tests/SiloPlayer.NativeRegressionTests/ConditionalEditorsNativeFixture.cs`:
  image Apply → Close → reopen → HTTP completion → gated item refetch; checks
  actual Save/Reset disable/enable, pending copy, fresh title and image lock.
- `tests/SiloPlayer.NativeRegressionTests/HouseholdContractsNativeFixture.cs`
  and `ProfilesMetadataNativeFixture.cs`: synthetic reset jobs now return the
  terminal result consumed by the editor-specific job wait.
- `docs/current-development.md` and this source handoff.

## Verification

Original PR verification (before the review fixes below):

- **Unit tests:** all 7,489 pass. They include `ItemMetadataWriteRegistryTests`,
  `MetadataResetOwnershipTests`, `EditMetadataOwnershipContractTests`,
  `MetadataEditStateTests`, `MediaMaintenanceApiTests` and
  `CurrentItemCardParityTests`.
- **`conditional-editors` native:** passes all 20 checks, including the new
  image close-and-reopen case.
- **`household-contracts` native:** passes all 12 checks.
- **`profiles-metadata` native:** passes all 11 checks. Both of these cover the
  updated Reset wiring.

Review-fix verification: source inspected only; builds and tests are reserved for
the pipeline. Added unit coverage for out-of-order refetches, reconciliation-only
failure, queued reset on forbidden/missing/unknown job reads, expired job waits
(including long Retry-After), and identity retirement. Native fixtures cover
accepted Save/Apply publication despite failed refetch and curator-forbidden job
reads. Source contracts cover reconnect-safe detail reload and queued-reset copy.

Not certified against a real server: curator job-read permissions, jobs running
past the bounded wait, a server-side unknown write outcome, and full visual parity.
