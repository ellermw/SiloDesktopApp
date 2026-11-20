# Manage > Autoscan > Sources parity

Source candidate: branch `t3/admin-autoscan-sources`, merged with fresh `origin/beta` on November 20, 2026; version stamps unchanged.

Official reference: Silo Server/WebUI `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`
(`web/src/pages/AdminAutoscan.tsx`, `web/src/pages/admin/autoscan/{SourcesPanel,SourceList,SourceDialog,SourceConfigForm,InlineConnectionPicker,PathMappingEditor,RewriteSuggestions,WebhookEndpoint,WebhookSetupStep}.tsx`,
`runNow.ts`, `sourceDescriptor.ts`, `sourceForm.ts`, `sourceDisplay.ts`, `sourceTargets.ts`, `webhookSetup.ts`, `useWebhookEndpoint.ts`,
`web/src/hooks/queries/useAutoscan.ts`, `internal/apiv2/admin_autoscan_*.go`, `internal/autoscan/adminform.go`).

## Scope

The native `/admin/autoscan` page (`AdminRoute.AutoscanSources`, registered by `AddAdminAutoscanSources()`, reachable from the real Admin navigation) covers:

- the header: title, subtitle, Enabled/Disabled badge, the global Autoscan switch (resends both polling defaults) and Run now (visibility rule from `runNow.ts`);
- the sources list with Source / Feeds / Status / On columns (cards when narrow), naming, subtitle, library feeds, health, per-source enable switch, Edit, Copy webhook URL, Rotate webhook URL, Delete (confirmed) and the empty state;
- Add source: descriptor-driven source choice, delivery choice, connection step, interval, descriptor config form with "use library paths" fills, path mappings (seeded webhook roots, split per type, manual rows) and label;
- inline server creation (reuse a Requests Sonarr/Radarr server or enter credentials) with Test connection;
- Edit source: Connect / Connection / Settings / Match paths / General tabs, Sync from server (rewrite suggestions: proposed, no match, already mapped, ambiguous), provider selector, Delete source;
- first webhook source: Create and continue generates the endpoint and stays in the Connect it flow with copy/rotate/generate and the supported Sonarr/Radarr triggers.

Autoscan Activity and Autoscan Settings (including connection management) are separate families and are not implemented here. Their navigation entries resolve as real routes and stay unopenable while no page is registered. The only link from this page is the Plugins page link, which is plain text with a tooltip because no Plugins page is in this build.

Deliberately not reproduced, with the reason:

- the webhook URL is masked and exported only by the explicit Copy action (it contains a token);
- toasts are shown as page feedback text; there is no toast host in this shell;
- secret and dynamic-option fields never appear in a source form: the server drops them from `scan-source-plugins` (`withoutUnsupportedFields`), so the native form drops them too and sends their stored values back unchanged if the config is edited.

## Write semantics

Each write is sent once; no authentication refresh or timeout replays it. Outcomes are classified:

- **Refused** (4xx other than 408, or refused locally before sending): the server's reason is shown (field-error detail when the problem says "see errors"), redacted and then truncated to 300 characters, nothing is re-read, writes stay open, and the editor keeps the draft so it can be corrected and sent again. A 404/409 refusal also re-reads the list. A Run now conflict (409) reports that a poll is already running.
- **Unknown** (timeout, transport failure, unusable acknowledgement, a create answered without a usable id): further writes are blocked until the whole source/settings state has been read again; the editor locks and asks the admin to review the list. Nothing is replayed.
- Source writes send the official seven fields (create adds plugin and capability); `enabled` always comes from the live row.

Reads are bounded (100 rows x 10 pages, 1,000 retained rows, 45 second reload deadline, 4 MiB responses); there is no polling timer and no media catalog read.

## Shared code touched

- `src/SiloPlayer/Controls/ProviderSchemaForm.cs`: adds an overload `(form, values, validate, nameControls = true)`. `validate` lets the owner validate only fields that are visible (including fields hidden by a section condition); `nameControls` adds accessible names and `ProviderField_<key>` ids. The existing two-argument constructor delegates with `nameControls: false` and its default validator, so existing callers (Settings provider forms) are unchanged.
- `src/SiloPlayer.Core/Models/Plugins/PluginCapability.cs`: adds the optional `FillFrom` property on `PluginAdminFormField`.
- `src/SiloPlayer.Core/Api/SiloApiClient.cs`: a transport-failure branch for Autoscan requests logs only a fixed failure category.

## Verification

Run on the final source (see `docs/current-development.md` for labels and counts): focused `AdminAutoscan` unit tests, the full unit suite, and the `admin-autoscan-sources` native fixture (real shell and Frame, synthetic server: loading, empty, failed refresh, populated, add/edit/delete, inline server creation and test, webhook create/rotate/retry, refused, unknown and lost writes, Run now conflict, leave guard, authority retirement) with `admin-manage-foundation`, `admin-configure-general` and `admin-monitor`. Captures were inspected at current DPI only.

Not verified: a live Silo server, Sonarr/Radarr deliveries, other DPI scales, screen-reader behavior, or pixel parity with the WebUI.

## Source inventory

- `src/SiloPlayer.Core/Api/AdminAutoscanApi.cs`, `SiloApiClient.AdminAutoscan.cs`
- `src/SiloPlayer.Core/Models/Admin/AdminAutoscanModels.cs`
- `src/SiloPlayer.Core/Services/Admin/AdminAutoscan{Controller,EditorDraft,Outcome,Presentation,RunNow,SourceDraft,Text}.cs`
- `src/SiloPlayer/Views/Admin/AdminAutoscanRegistration.cs`, `AdminAutoscanSourcesPage.cs/.xaml`, `AdminAutoscanSourceEditor{,.Connection,.Paths,.Webhook}.cs`
- tests: `tests/SiloPlayer.Tests/AdminAutoscan*Tests.cs`, `tests/SiloPlayer.NativeRegressionTests/AdminAutoscanSourcesNativeFixture{,.Server}.cs` (`SILO_NATIVE_TEST_ONLY=admin-autoscan-sources`)
