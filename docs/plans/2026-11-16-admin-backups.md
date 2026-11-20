# Configure > Server > Backups — native plan and contract record

Status: In progress for pipeline and visual acceptance. Local verification and review
corrections are complete; see [the parity record](../parity/admin-backups.md) for exact
receipts and the unchanged Downloads neighbor failure.

- Desktop base: Beta `542dec13c7d45c7520116c559166023ecb4ee987` (fresh fetch of `origin/beta`
  matched the assigned SHA; the clean task worktree fast-forwarded to it). Version stays
  pipeline-owned at `1.2.1168-beta.1`.
- Official source: Silo Server/WebUI git objects at
  `92ee051878c2a791940ec9598e2bdd0f04890b3e`, read with `git show` from the existing
  `D:/SiloPlayer-work/server-upstream-nov14` object store (not its working HEAD).

## Scope boundary

One complete route family, registered together: `/admin/backups`,
`/admin/backups/:id/restore`, `/admin/backups/uploads/:uploadId/restore`. The three were
not split because registering the root without its official Upload and Restore controls
would ship a page whose real server capabilities are disabled for client reasons. Other
Admin sections are out of scope.

## Contract facts verified in Go handlers and OpenAPI (not docs)

| Operation | Verified behavior |
| --- | --- |
| `GET /admin/backups/capabilities` | `state` available/disabled/not_configured/unsupported; `destinations`, `encryption`, `upload` (configured AND uploads available), `max_upload_bytes` (2 GiB), `selective_restore`, `download` (= configured). |
| `GET /admin/backups` | `busy`, `active{operation backup/delete/restore/upload, backup_id?, upload_id?, progress 0–100, message?}`, `warnings[code,message]`, `key_check`, `key_confirmed`, `encrypted`, `destination`, `keep`, `enabled`, `items`. |
| `POST /admin/backups` | 202 `{id, kind:"manual", status:"in_progress"}`; `RetrySafetyNonRetryable`; 409 `conflict` (busy, SQLite store, key not confirmed) or 409 `capability_not_configured` (no storage / S3 bucket missing). |
| `DELETE /admin/backups/{id}` | 204; 404; 409 busy or "a restore reads this backup". Id pattern `^[0-9]{8}T[0-9]{6}(\.[0-9]{3})?Z-[a-z-]+$`, ≤64. |
| `POST /admin/backups/{id}/download-links` | body `{scope: full|restore-set}`; items `{name,size_bytes,url,expires_at}`, TTL 15 min. URL is either a presigned absolute storage URL or the server-relative signed path `/api/v2/admin/backups/{id}/objects/{name}?exp=&sig=`. Full scope adds `manifest.json` (no SHA in `objects`); an encrypted restore-set link streams decrypted and is *smaller* than `size_bytes`, with no plaintext digest. |
| `GET …/objects/{name}` (public) | name enum `manifest.json, db.dump, blobs.tar, restore-set.tar` and `.age` variants; `Cache-Control: no-store`; aborts the connection on a mid-stream failure. |
| `POST /admin/backups/s3/test` | 200 `{ok, step?, message}` even on failure; empty `secret_key` reuses the stored one only for the stored endpoint+access key. |
| `POST /admin/backups/s3/acknowledge-key` | `{key_check}` pattern `xxxx-xxxx-xxxx-xxxx`; 409 `conflict` when the key changed. Only this endpoint writes `backup.s3.key_acknowledged`. |
| `GET /admin/backups/{id}/restore-set` | `accept_unauthenticated` query; 409 `backup_unauthenticated` before anything is read; 409 no restore set / newer Silo. |
| `POST /admin/backups/{id}/restore` | body `{digest(64 hex), selection, accept_unauthenticated?}`; 202 AdminTaskJob + `Location: /api/v2/admin/jobs/{id}` + `Retry-After: 5`; non-retryable; 409 `restore_set_changed`; 409 `conflict` with `Location` of the active restore job; 422 selection validation. |
| Upload create | 201; `size_bytes ≥1 ≤ 2 GiB` (413 above), optional `sha256`, `part_size` 1–16 MiB (default 4); 409 `upload_limit_reached` at 3; key not confirmed → 409. |
| Part PUT | `application/octet-stream`, part 0–4095, `RetrySafetyNaturalIdempotent` (resend replaces until completed); 408 after 2 min; 409 while the same part / 4 parts are received. |
| Complete | `RetrySafetyNaturalIdempotent`; finished upload answers as it is; 409 while the lock is held; runs `WithoutCancel` up to 20 min. |
| Upload expiry | unfinished/rejected 24 h after last activity; complete 7 days after last use. |
| `backup.*` settings | through the shared guarded settings PUT (strong ETag, 412). `backup.keep` 1–60 (default 7), `backup.time` HH:MM, destination `default|s3`. Switching to s3 without the acknowledged key → `backup_key_not_confirmed` 409; s3 without endpoint/bucket → 400. `backup.s3.secret_key` is write-only. |
| Jobs | `GET /admin/jobs?kind=backup_restore&limit=20`, `GET /admin/jobs/{id}`, `POST /admin/jobs/{id}/cancel` (200/202). Report `before_restore_backup_id`, per-item outcomes, `restart_required`/`restart_keys`, `error_code`. |

Corrections to the research map: there is no separate "preview" endpoint (preview is the
restore-set GET); "key not confirmed" for backups/uploads is a plain `conflict` problem.
The settings guard raises `backup_key_not_confirmed` internally, but the v2 settings PUT
maps it through `serviceProblem` into a generic 409 `conflict` with its message
(`admin_settings_write.go` / `problem` helpers), so the client keys on 409 for the
backup settings, never on that code.

## Native design

- `AdminBackupsApi` (Core/Api) over a new bounded sender partial that exposes status and
  `Location`; GET reads and the explicitly idempotent part PUT may refresh expired tokens,
  while non-idempotent commands never refresh or replay.
- `AdminBackupSignedDownload`: separate unauthenticated `SocketsHttpHandler` (no redirects,
  no cookies, no default headers); accepts only the exact server-relative signed path for
  the backup and object, or an absolute `https` presigned URL; streams with bounds into
  `DiagnosticsFileTransfer`; verifies exact size and SHA-256 where the manifest records them.
- `AdminBackupsController` and `AdminBackupRestoreController` own authority lifetimes,
  serialized commands with unknown-outcome blocks, polling (list 3 s busy / 30 s idle,
  capabilities 60 s stale, uploads 3 s while checking, restore job 2 s until terminal),
  coalesced foreground reads and stale-response rejection.
- `AdminBackupUploadSession`: chunked upload over a caller-owned stream; bounded part
  buffers; three attempts per part only for retryable failures; completion re-sent only
  on explicit "Check again".
- Presentation/model ports: `AdminBackupPresentation`, `AdminBackupRestoreModel`.
- Pages: `AdminBackupsPage` list/restore partials (programmatic WinUI like the other
  Configure pages), registered in `AdminConfigureServices`; the three Unsupported patterns
  are removed only for Backups.

## Honest limits

Observed verification: source proof 1/1, focused 226/226 and full 7852/7852; nonincremental
app/host builds and Release publish; source, payload, engine and XBF binding; Backups native
30 PASS records and foundation 36 (both include completion). Six review regressions and
the stale-preview regression have recorded RED/GREEN evidence; native terminal download
status also has RED/GREEN evidence. The original feature was not implemented test-first.

Durable per-source/epoch restore uncertainty, foreign-source conflict checks, coalesced
capabilities/uploads reads, checking-poll resume and page-independent known-upload cleanup
address the independent review findings. An unidentified upload-create race remains for the
server sweep and is not hidden.

The unchanged Downloads neighbor timed out at fixture line 316; it is not claimed passing
or runtime-proven pre-existing. OS pickers use internal reflection seams confined to
fixture-owned paths, not real OS automation. Live server, visual, DPI, IME and Narrator
verification remain outstanding. No installed player, updater, installer or release action.
