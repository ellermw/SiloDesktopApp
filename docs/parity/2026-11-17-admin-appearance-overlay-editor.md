# Appearance overlay editor component — source candidate

Prepared checkout: `C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-65125707-3930a918607eed37`,
branch `t3/admin-overlay-editor`, base `248aafd62ed65b868c0e7356926e4d83204649cd`.
Version stays `1.2.1181-beta.1` / numeric `1.2.1180.1`.
Official source is pinned to `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb` in
`D:/SiloPlayer-work/server-upstream-nov14`; pinned objects were read with `git show`.
No new checkout or child agent was created.

This is an uncommitted source candidate. No .NET, restore, build, test, native,
browser, mutex, installer, installed-player, settings or generated-output operation
was run. Unit/native cases below are authored and unexecuted. Source inspection and
`git diff --check` do not establish compilation, runtime success or visual parity.

## Integration inventory

Integrate these new component-owned files:

- `src/SiloPlayer/Controls/AdminAppearanceOverlayEditor.cs`
- `src/SiloPlayer/Controls/AdminAppearanceOverlayEditor.Look.cs`
- `src/SiloPlayer/Controls/AdminAppearanceOverlayEditor.Images.cs`
- `src/SiloPlayer/Services/CardOverlayService.Editor.cs`
- `src/SiloPlayer.Core/Api/AdminAppearanceApi.Overlays.cs`
- `src/SiloPlayer.Core/Api/SiloApiClient.AdminAppearance.Overlays.cs`
- `src/SiloPlayer.Core/Services/Admin/AdminAppearanceOverlayText.cs`
- `tests/SiloPlayer.Tests/AdminAppearanceOverlayTests.cs`
- `tests/SiloPlayer.NativeRegressionTests/AdminAppearanceOverlayNativeFixture.cs`
- this integration record.

Apply these narrow existing-file hunks:

- `CardOverlayService.cs`: make the class partial; match official registry order
  and default positions (ratings left edge, advisory age bottom right, show status
  right edge); normalize show-status value-image keys like the official registry;
  guard numeric version parsing so an opaque version does not erase the document.
- `OverlayBadgeRenderer.cs`: accept canonical show-status keys as well as legacy
  lowercase keys when translating the native badge label.
- `SettingsApi.cs`: deserialize `OverlayConfigResponse.ImageUploadsAvailable` from
  `image_uploads_available`. Image upload storage is separate from manifest image
  support and from branding storage.
- `SiloPlayer.Tests.csproj`: link `CardOverlayService.Editor.cs` alongside the
  existing linked production service.
- `CardOverlayRevisionTests.cs`: update the built-in ratings default expectation
  to the official left edge / legacy top-left mapping.
- Native `Program.cs`: dispatch `SILO_NATIVE_TEST_ONLY=admin-appearance-overlays`.

Three unchanged integration prerequisites were copied from the coordinator's
preserved checkout solely so the new API partials and unit sources have their
types locally. **Do not integrate these snapshots over newer parent files**:

- `src/SiloPlayer.Core/Api/AdminAppearanceApi.cs`
- `src/SiloPlayer.Core/Api/SiloApiClient.AdminAppearance.cs`
- `src/SiloPlayer.Core/Services/Admin/AdminAppearanceAssetPolicy.cs`

The component introduces no XAML or XBF. Its C# sources are included by the app's
SDK glob. The parent owns Appearance XAML/XBF, route/service registration, approved
publish files and shell branding. Parent page/controller and sibling worktrees
were read only.

## Parent binding contract

The agreed constructor, `BuiltInDefaults`, `Bind`, `Changed`, `UploadRequested`
and `DeleteRequested` are implemented. `Changed` emits only actual staged user
edits; Bind/capability/library/preview updates emit nothing. No component method
calls `CardOverlayService.SaveAsync` or any profile writer.

Keep one editor instance for the page lifetime. On each current-authority render:

```csharp
_overlayEditor.Bind(document, overlaysEnabled, defaultsEditable,
    _overlays.ManifestRevision, state.OverlayConfig?.Images ?? []);
_overlayEditor.SetCapabilities(
    state.OverlayConfig?.ImageUploadsAvailable == true,
    shownRatingSources);
_overlayEditor.SetImageLibraryEditable(pageEditable);
_overlayEditor.SetPreviewPresentation(posterSize, viewportWidth);
```

Read `shownRatingSources` through the existing `CatalogApi.GetShownRatingSourcesAsync`
under the page's captured authority, check that authority after the await, and use
an empty set on unavailable capability. Only RT critic/audience controls are
source-gated in this pinned registry; IMDb/TMDB remain offered. `SetCapabilities`
copies the set. It must follow Bind, which clears old-owner capability information.
Use the current profile's `ui.card_presentation` poster size (`compact`, `standard`,
`large`) and the window viewport width; the preview applies the official
120/140/160, 140/160/185 or 170/195/220 widths at 640/1024 breakpoints.

`Bind` retains focused controls, image flyout instances, pending value keys and
movie/show/requested preview selection. Capability changes update controls in
place. A changed image owner closes flyouts and clears local pending image input;
an unloaded editor rejects further edits. The parent must disable/retire binding
when its page authority retires, as it does for its other controls.

For restore, the parent confirms and stages defaults through its admin batch.
On revision 25+ `BuiltInDefaults` is the exact registry-derived canonical built-in
document. For earlier manifests use
`AdminAppearanceOverlayEditor.DefaultsForRevision(manifestRevision)` when staging
the restore itself; this avoids storing unsupported newer IDs or edge fields.
Empty defaults and a built-in document are displayed through this revision-aware
adapter. Discard/reload simply Bind the parent's document again.

## Image workflow integration

The editor's library emits upload intent and a selected delete reference; it does
not open a file picker or send HTTP. **The parent owns delete confirmation**, so
there is no duplicate dialog in the component. Use the exact official messages
from `AdminAppearanceOverlayText`:

- `web.settings.overlays.library.deleteTitle`
- `web.settings.overlays.library.deleteDescription`
- `web.settings.overlays.library.delete`

At the time of read-only integration inspection, parent Assets.cs used a 12 MiB
overlay picker cap and sent the filename immediately as the image name. Set that
cap to `AdminAppearanceApi.MaxOverlayUploadBytes` (4 MiB), expose the official Name
field and Upload action, initialize the label from the filename without its final
extension, and enforce 1–64 Unicode code points after trimming. Keep a refused
upload's pending file/name for an explicit retry; do not automatically retry an
unknown outcome. The parent already owns that picker/pending-file UI per the
interface contract; none of those parent files were edited here.

Gate upload with `OverlayConfigResponse.ImageUploadsAvailable`, rather than
`CardOverlayService.SupportsImages` alone. The latter is manifest capability.
Existing listed images remain deletable when upload storage is unavailable.
At inspection, the parent's shared `ChangeAssetAsync` required branding storage
for every kind: keep that gate for branding uploads, but do not apply it to overlay
deletion, and use overlay-config's upload gate for overlay upload. A managed
`defaults.card_overlays` setting only disables defaults editing; parent policy
should keep independent image operations available to an otherwise editable admin.
`SetImageLibraryEditable(pageEditable)` supplies that independent gate; if never
called, the library follows Bind's editable argument. Disable it on retirement.

After one successful/uncertain write, refresh with the parent's existing
`GetOverlayConfigAsync` and Bind its images, preserving staged defaults. Do not
rewrite assignments when an image disappears: the preview filters unlisted refs
and the image selector keeps a Removed image choice so it can be cleared.

The new API partials implement:

- `POST /api/v2/admin/overlay-images`, multipart `file` and trimmed `name`, PNG /
  JPEG / WebP, at most 4 MiB, 1–64 Unicode code points. Only 201 with a valid
  content reference, requested label, canonical public URL, 1–600 x 1–300
  dimensions and matching admin Location is acknowledged. Receipt JSON is capped
  at 16 KiB. The supplied source bytes are re-encoded by the server, so the returned
  reference is validated structurally rather than compared with the source hash.
- `DELETE /api/v2/admin/overlay-images/{ref}`, validated lowercase SHA256 `.webp`
  reference, exactly 204 acknowledgement.
- The existing 30-second lease-bound transport deadline, cancellation and bounded
  error reader. Both writes disable authentication replay. Retired authority throws
  cancellation; ambiguous transport/timeout/5xx/malformed success returns the
  parent's sanitized `AdminAppearanceOutcomeUnknownException`. A refused 4xx is
  sanitized with the parent's `SafeFailure`. Already-cancelled intents never send.

Library rows are capped at 256 and value-image authoring at 32 entries / 64 code
points per key. Choice thumbnails are created for mounted/selected options and
closed flyouts release their choice controls; Bind retains an open flyout.
Existing opaque JSON is retained through unrelated edits, including
unknown root/items/style/value-map members and stored legacy positions. Explicit
look reset removes style; removing the last known value image removes that map
unless it still carries opaque entries. Image fetch/decode uses the existing
authenticated, hash-checked native renderer and its bounded caches/concurrency.

## Authored acceptance

Unit sources exercise the production serializer/registry and actual scripted HTTP
transport: defaults and legacy mapping, opaque JSON and legacy migration, clearing
image/style overrides, manifest/ratings visibility, normalized status keys,
multipart contents, exact acknowledgements, no replay on 401/408/5xx, upload
admission, Unicode names, oversize receipts, sanitization, authority retirement,
pre-cancelled intents, successful delete and explicit overlay-config refresh.

The native fixture enters the production Appearance route through
`AdminConfigureServices.Add`, `AdminShellPage` and its Frame. It exercises actual
mounted toggle/preset/size controls, native show preview, open image flyout focus
and pending-key retention, value-image assignment and decoded preview, deleted
image fallback, old-manifest controls, storage/ratings gating, read-only and retired
owner rejection, and zero profile writes. It closes popups and waits for page unload
before restoring global services. It intentionally requires the final parent
registration; it is not a synthetic substitute page. Runtime captures are authored
in the fixture but none have been generated or inspected.

Coordinator acceptance, only in its assigned runtime slot after the storage/free
space checks and one nonblocking `SiloOct18Build.WaitOne(0)` acquisition:

```powershell
$artifacts = '.codex-tmp/admin-acceptance-artifacts'
$publish = 'D:/SiloPlayer-build/admin-completion-appearance/publish'
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path $artifacts --filter "FullyQualifiedName~AdminAppearanceOverlayTests|FullyQualifiedName~CardOverlay|FullyQualifiedName~AdvancedOverlayTests|FullyQualifiedName~OverlayPreferencePreservationTests"
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path $artifacts
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true --artifacts-path $artifacts -o $publish
dotnet build tests/SiloPlayer.NativeRegressionTests/SiloPlayer.NativeRegressionTests.csproj -c Release -r win-x64 --artifacts-path $artifacts -p:SiloPlayerAssemblyDirectory=$publish
```

These are acceptance commands, not commands executed by this worker. Use the
coordinator's task-owned deadline/containment wrapper for every process. Reuse only
its assigned output root and preserve frozen outputs. Use the existing native
payload staging/binding procedure to verify final source/PDB document checksums and
the same product/Core/host bytes before launching the resulting host with
`SILO_NATIVE_TEST_ONLY=admin-appearance-overlays` and a task-owned
`SILO_NATIVE_TEST_RESULT_DIRECTORY`; also run `advanced-overlays` and
`overlay-revisions`. Hold the same lease through staging/binding/native execution
and release it in finally. Do not run the installed app or an installer. Inspect
the produced capture at wide/narrow layouts and add final parent save/discard,
restore confirmation, picker/upload/delete, unknown-outcome reconciliation and
retirement cases to the coordinator's full Appearance fixture.

No test counts, build pass, native pass, all-DPI, live-server or full visual parity
claim is made for this candidate. No commit, push or PR was created.

## Exact official references

All paths below are at the pinned server commit above:

- `web/src/pages/admin-settings/AppearanceSettings.tsx`
- `web/src/components/overlays/settings/OverlayLookControls.tsx`
- `web/src/components/overlays/settings/BadgeImagesControl.tsx`
- `web/src/components/overlays/settings/OverlayImageLibrary.tsx`
- `web/src/components/overlays/OverlayPreviewCard.tsx`
- `web/src/lib/overlays/{registry,schema.ts,sample-data.ts,ui-constants.ts}`
- `web/src/lib/uiCustomization.ts`
- `web/src/hooks/queries/admin/overlayImages.ts`
- `web/src/locales/en.json`
- `internal/apiv2/overlay_images.go`
- `internal/overlays/images.go`
- `contracts/api/v2/fixtures/get_overlay_config_ok.json`
- `contracts/settings/v1/schemas/card-overlays.json`
