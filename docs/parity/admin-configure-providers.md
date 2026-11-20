# Configure > Providers parity

Source candidate: Desktop Beta `248aafd62ed65b868c0e7356926e4d83204649cd`, version `1.2.1181-beta.1`.

Official reference: Silo Server/WebUI `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`.

## Scope

The native `/admin/settings/providers` page owns the same server configuration as the pinned WebUI:

- installed watch/scrobble-provider status and setup links;
- MDBList list-search API key, configured/environment-managed state, restart hint, guarded save, clear confirmation, and connection test;
- OpenSubtitles account plus SubDL, SubSource, and future subtitle-provider API-key editors, with strong ETags, enable preference, test, clear confirmation, and retained drafts after unconfirmed writes;
- marker lookup enabled, lookup priority, fetch minimum confidence, sharing enabled, automatic local sharing, sharing minimum confidence, plugin credential status/setup, and account validation statistics;
- read-only Playback marker-mode explanation; and
- the official metadata-provider Plugins ownership handoff.

Plugin credentials remain owned by the Plugins pages. This page does not duplicate plugin-configuration writes or alter the Plugin Apps shell/sidebar.

## Safety and lifecycle

Reads, response sizes, provider counts, plugin pagination, and deadlines are bounded. Every operation is bound to the current admin authority. Subtitle editors are single-use and require a strong ETag. Settings, subtitle, marker, and test writes are sent once; uncertain outcomes are not replayed. A 412 or uncertain outcome retains the page draft and requires an explicit reload/review. Stored secrets are represented only by configured/environment-managed status; neither stored nor typed credential material is included in diagnostics or model `ToString()` output. Page suspension and authority retirement clear private drafts.

## Source inventory

- `src/SiloPlayer.Core/Api/AdminProvidersApi.cs`
- `src/SiloPlayer.Core/Api/AdminServerSettingsApi.cs`
- `src/SiloPlayer.Core/Models/Admin/AdminProviderModels.cs`
- `src/SiloPlayer.Core/Services/Admin/AdminProvidersController.cs`
- `src/SiloPlayer/Services/AdminConfigureServices.cs`
- `src/SiloPlayer/Views/Admin/AdminProvidersPage.xaml`
- `src/SiloPlayer/Views/Admin/AdminProvidersPage.cs`
- `src/SiloPlayer/Views/Admin/AdminProvidersPage.Cards.cs`
- `tests/SiloPlayer.Tests/AdminProvidersTests.cs`
- `tests/SiloPlayer.NativeRegressionTests/AdminProvidersNativeFixture.cs`
- `tests/SiloPlayer.NativeRegressionTests/Program.cs`
- `installer/build.ps1`
- `docs/parity/admin-monitor-surfaces.json`
- `docs/parity/admin-configure-providers.md`

## Verification status

The focused unit tests and production-shell native fixture were authored in this source-only lane. They were not run, and no build, app, browser, native host, mutex, or generated output was started. Visual parity and live-server compatibility therefore remain unverified until the coordinator runs acceptance.

Suggested acceptance commands, under the repository's serialized runtime procedure:

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj --filter FullyQualifiedName~AdminProvidersTests
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj
dotnet build src/SiloPlayer/SiloPlayer.csproj -c Release --no-incremental
$verifiedPublishDirectory = 'D:\SiloPlayer-build\admin-configure-providers\publish'
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishReadyToRun=false -o $verifiedPublishDirectory
& tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory $verifiedPublishDirectory -Only admin-configure-providers
```

Acceptance must inspect the fixture captures/automation results at wide and compact sizes, exercise a real compatible server separately, and retain the pinned-source distinction if upstream advances.
