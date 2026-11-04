using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

public sealed class SidebarCustomizationCloseoutTests
{
    [Theory]
    [InlineData(404)]
    [InlineData(405)]
    public async Task EffectiveSettingsFailureRetainsAcceptedLayoutAndRetriesSameOwner(int status)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = Client(http); var service = new UICustomizationService(new SettingsApi(client));
        await service.EnsureLoadedAsync();
        var acceptedMenu = service.PrimaryMenu; var acceptedCards = service.CardPresentation;
        var context = client.CaptureContext();
        wire.EffectiveStatus = status;
        service.Invalidate(); await service.EnsureLoadedAsync();

        Assert.True(service.IsSupported);
        Assert.True(service.IsUnavailable);
        Assert.Same(acceptedMenu, service.PrimaryMenu);
        Assert.Same(acceptedCards, service.CardPresentation);
        Assert.Equal("profile_client", service.PrimaryMenuSource);
        var reads = wire.EffectiveReads;
        wire.EffectiveStatus = 200; wire.MenuLabel = "Recovered";
        await service.EnsureLoadedAsync();
        Assert.Equal(context, client.CaptureContext());
        Assert.True(wire.EffectiveReads > reads);
        Assert.False(service.IsUnavailable);
        Assert.Equal("Recovered", service.PrimaryMenu!.Items.Last().Label);
    }

    [Theory]
    [InlineData(404, false)]
    [InlineData(405, false)]
    [InlineData(404, true)]
    [InlineData(405, true)]
    public async Task CapabilityFailureRetainsAcceptedLayoutAndRetriesSameOwner(int status, bool changeRequestContext)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = Client(http); var service = new UICustomizationService(new SettingsApi(client));
        await service.EnsureLoadedAsync();
        var acceptedMenu = service.PrimaryMenu; var acceptedCards = service.CardPresentation;
        var acceptedShortcuts = service.Shortcuts;
        var identity = client.CaptureIdentityContext();
        if (changeRequestContext) client.InvalidateAccessContext();
        else service.Invalidate();
        var context = client.CaptureContext();
        var effectiveReads = wire.EffectiveReads;
        wire.CapabilityStatus = status;
        await service.EnsureLoadedAsync();

        Assert.True(service.IsSupported);
        Assert.True(service.IsUnavailable);
        Assert.Same(acceptedMenu, service.PrimaryMenu);
        Assert.Same(acceptedCards, service.CardPresentation);
        Assert.Same(acceptedShortcuts, service.Shortcuts);
        Assert.Equal("profile_client", service.PrimaryMenuSource);
        Assert.Equal("profile_client", service.CardPresentationSource);
        Assert.Equal(effectiveReads, wire.EffectiveReads);
        var capabilityReads = wire.CapabilityReads;
        wire.CapabilityStatus = 200; wire.MenuLabel = "Recovered";
        await service.EnsureLoadedAsync();
        Assert.Equal(context, client.CaptureContext());
        Assert.Equal(identity, client.CaptureIdentityContext());
        Assert.Equal(capabilityReads + 1, wire.CapabilityReads);
        Assert.Equal(effectiveReads + 1, wire.EffectiveReads);
        Assert.True(service.IsSupported);
        Assert.False(service.IsUnavailable);
        Assert.Equal("Recovered", service.PrimaryMenu!.Items.Last().Label);
        await service.EnsureLoadedAsync();
        Assert.Equal(capabilityReads + 1, wire.CapabilityReads);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(405)]
    public async Task MissingCapabilityDoesNotRetainAnotherIdentitysLayout(int status)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = Client(http); var service = new UICustomizationService(new SettingsApi(client));
        await service.EnsureLoadedAsync();
        client.SetProfile("replacement");
        wire.CapabilityStatus = status;
        await service.EnsureLoadedAsync();
        Assert.False(service.IsSupported);
        Assert.False(service.IsUnavailable);
        Assert.Null(service.PrimaryMenu);
        Assert.Equal("standard", service.CardPresentation.PosterSize);
        Assert.Equal("title_metadata", service.CardPresentation.Caption);
        Assert.Empty(service.Shortcuts.Items);
        Assert.Equal(1, wire.EffectiveReads);
    }

    [Fact]
    public async Task AuthoritativeOlderManifestReplacesAcceptedLayoutWithDefaults()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = new UICustomizationService(new SettingsApi(Client(http)));
        await service.EnsureLoadedAsync();
        wire.ManifestRevision = 4;
        service.Invalidate(); await service.EnsureLoadedAsync();
        Assert.False(service.IsSupported);
        Assert.False(service.IsUnavailable);
        Assert.Null(service.PrimaryMenu);
        Assert.Equal("standard", service.CardPresentation.PosterSize);
        Assert.Equal("title_metadata", service.CardPresentation.Caption);
        Assert.Empty(service.Shortcuts.Items);
        Assert.Equal(1, wire.EffectiveReads);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(405)]
    public async Task MissingCapabilityIsUnsupportedInsteadOfARetryableEffectiveRead(int status)
    {
        using var wire = new Wire { CapabilityStatus = status }; using var http = new HttpClient(wire);
        var service = new UICustomizationService(new SettingsApi(Client(http)));
        await service.EnsureLoadedAsync();
        Assert.False(service.IsSupported);
        Assert.False(service.IsUnavailable);
        Assert.Null(service.PrimaryMenu);
        Assert.Equal(0, wire.EffectiveReads);
        await service.EnsureLoadedAsync();
        Assert.Equal(1, wire.CapabilityReads);
    }

    [Theory]
    [InlineData("save", false)]
    [InlineData("save", true)]
    [InlineData("reset", false)]
    [InlineData("reset", true)]
    [InlineData("clear-menu", false)]
    [InlineData("clear-menu", true)]
    [InlineData("clear-cards", false)]
    [InlineData("clear-cards", true)]
    public async Task MenuMutationReportsOnlyACommittedCurrentGeneration(string operation, bool retire)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = Client(http); var service = new UICustomizationService(new SettingsApi(client));
        await service.EnsureLoadedAsync();
        var accepted = service.PrimaryMenu;
        var context = client.CaptureContext();
        wire.HoldWrite = true;
        Task mutation = operation switch
        {
            "save" => service.SavePrimaryMenuAsync([PrimaryMenuItem.Builtin("home"), new() { Type = "library", LibraryId = 1, Label = "Draft" }]),
            "reset" => service.ResetPrimaryMenuAsync(),
            "clear-menu" => service.ClearDeviceOverrideAsync(UICustomizationSettingKeys.PrimaryMenu),
            _ => service.ClearDeviceOverrideAsync(UICustomizationSettingKeys.CardPresentation),
        };
        await wire.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (retire) service.Invalidate();
        var reads = wire.EffectiveReads;
        wire.WriteRelease.TrySetResult();
        await mutation.WaitAsync(TimeSpan.FromSeconds(3));

        var result = Assert.IsAssignableFrom<Task<bool>>(mutation);
        Assert.Equal(!retire, await result);
        Assert.Equal(context, client.CaptureContext());
        Assert.Equal(1, wire.Writes);
        if (retire)
        {
            Assert.Same(accepted, service.PrimaryMenu);
            Assert.Equal(reads, wire.EffectiveReads);
        }
        else Assert.Equal(operation == "save" ? "Draft" : "Accepted", service.PrimaryMenu!.Items.Last().Label);
    }

    [Theory]
    [InlineData(false, 200)]
    [InlineData(true, 200)]
    [InlineData(false, 404)]
    [InlineData(true, 404)]
    public async Task SuccessfulSaveRetiresAnInFlightEffectiveSettingsRead(bool saveMenu, int readStatus)
    {
        using var wire = new Wire { MenuSource = "profile", CardSource = "profile" };
        using var http = new HttpClient(wire);
        var client = Client(http); var service = new UICustomizationService(new SettingsApi(client));
        await service.EnsureLoadedAsync();
        var context = client.CaptureContext();
        var changes = 0;
        service.Changed += (_, _) => changes++;
        wire.HoldEffectiveRead = true;
        wire.EffectiveStatus = readStatus;
        var refresh = service.RefreshAsync();
        await wire.EffectiveReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            var save = saveMenu
                ? service.SavePrimaryMenuAsync([PrimaryMenuItem.Builtin("home"), new() { Type = "library", LibraryId = 1, Label = "Saved" }])
                : service.SaveCardPresentationAsync(new() { PosterSize = "large" });
            Assert.True(await save.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally { wire.EffectiveReadRelease.TrySetResult(); }
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(context, client.CaptureContext());
        Assert.Equal(saveMenu ? "Saved" : "Accepted", service.PrimaryMenu!.Items.Last().Label);
        Assert.Equal(saveMenu ? "compact" : "large", service.CardPresentation.PosterSize);
        Assert.Equal(saveMenu ? "profile_client" : "profile", service.PrimaryMenuSource);
        Assert.Equal(saveMenu ? "profile" : "profile_client", service.CardPresentationSource);
        Assert.True(service.IsSupported);
        Assert.False(service.IsUnavailable);
        Assert.Equal(1, changes);
        wire.EffectiveStatus = 200;
        wire.MenuLabel = "Fresh";
        wire.PosterSize = "standard";
        await service.RefreshAsync();
        Assert.Equal("Fresh", service.PrimaryMenu!.Items.Last().Label);
        Assert.Equal("standard", service.CardPresentation.PosterSize);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData(false, 200)] [InlineData(true, 200)]
    [InlineData(false, 404)] [InlineData(true, 404)]
    public async Task DiscardedBatchIsRetriedWithoutLosingTheAcceptedSave(bool saveMenu, int oldStatus)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = new UICustomizationService(new SettingsApi(Client(http)));
        await service.EnsureLoadedAsync();
        wire.HoldEffectiveRead = true; wire.EffectiveStatus = oldStatus;
        var refresh = service.RefreshAsync();
        await wire.EffectiveReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            var saved = saveMenu
                ? await service.SavePrimaryMenuAsync([PrimaryMenuItem.Builtin("home"), new() { Type = "library", LibraryId = 1, Label = "Saved" }])
                : await service.SaveCardPresentationAsync(new() { PosterSize = "large" });
            Assert.True(saved);
            // The next response reflects the committed write plus independently
            // changed fields. The held response remains the old snapshot.
            wire.EffectiveStatus = 200;
            wire.MenuLabel = saveMenu ? "Saved" : "Remote menu";
            wire.PosterSize = saveMenu ? "standard" : "large";
            wire.ShortcutLabel = "Remote shortcut";
        }
        finally { wire.EffectiveReadRelease.TrySetResult(); }
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(saveMenu ? "Saved" : "Accepted", service.PrimaryMenu!.Items.Last().Label);
        Assert.Equal(saveMenu ? "compact" : "large", service.CardPresentation.PosterSize);
        var reads = wire.EffectiveReads;
        await service.EnsureLoadedAsync();
        Assert.Equal(reads + 1, wire.EffectiveReads);
        Assert.Equal(saveMenu ? "Saved" : "Remote menu", service.PrimaryMenu!.Items.Last().Label);
        Assert.Equal(saveMenu ? "standard" : "large", service.CardPresentation.PosterSize);
        Assert.Equal("Remote shortcut", Assert.Single(service.Shortcuts.Items).Label);
        reads = wire.EffectiveReads;
        await service.EnsureLoadedAsync();
        Assert.Equal(reads, wire.EffectiveReads);
    }

    [Fact]
    public void SelectedProfileMetadataRefreshNotifiesShellWithoutChangingRequestAuthority()
    {
        using var http = new HttpClient(new Wire());
        var client = Client(http); using var auth = new AuthService(client, new AuthApi(client));
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Before" });
        var context = client.CaptureContext(); var identity = client.CaptureIdentityContext();
        var changed = 0;
        auth.ProfileChanged += () => changed++;
        auth.RefreshSelectedProfile(new() { Id = "different", Name = "Other" });
        Assert.Equal(0, changed);
        auth.RefreshSelectedProfile(new() { Id = "fixture", Name = "After", MaxContentRating = "PG" });
        Assert.Equal(1, changed);
        Assert.Equal("After", auth.SelectedProfile!.Name);
        Assert.Equal(context, client.CaptureContext());
        Assert.Equal(identity, client.CaptureIdentityContext());
    }

    private static SiloApiClient Client(HttpClient http)
    {
        var client = new SiloApiClient(http); client.SetBaseUrl("https://sidebar-closeout.invalid"); client.SetProfile("fixture"); return client;
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int CapabilityStatus = 200, ManifestRevision = 22, CapabilityReads, EffectiveStatus = 200, EffectiveReads, Writes;
        public string MenuLabel = "Accepted", ShortcutLabel = "Original shortcut";
        public string PosterSize = "compact", MenuSource = "profile_client", CardSource = "profile_client";
        public bool HoldWrite, HoldEffectiveRead;
        public readonly TaskCompletionSource EffectiveReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource EffectiveReadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource WriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource WriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/settings/contract/capabilities")
            {
                CapabilityReads++;
                return CapabilityStatus == 200 ? Json(new { api_version = 1, manifest_revision = ManifestRevision,
                    client_families = new[] { "desktop", "web" }, supports_batched_effective = true, supports_idempotent_writes = true }) : new((HttpStatusCode)CapabilityStatus);
            }
            if (path == "/api/v2/settings/values/effective")
            {
                EffectiveReads++;
                // Snapshot before the hold so a later successful write cannot
                // change the response that was already in flight.
                var response = EffectiveStatus != 200 ? new HttpResponseMessage((HttpStatusCode)EffectiveStatus) : Json(new { items = new object[]
                {
                    new { key = "nav.primary_menu", value = new { items = new object[] { new { type = "builtin", destination = "home" }, new { type = "library", library_id = 1, label = MenuLabel } } }, source = MenuSource },
                    new { key = "ui.card_presentation", value = new { poster_size = PosterSize, caption = "title_metadata" }, source = CardSource },
                    new { key = "nav.shortcuts", value = new { items = new[] { new { type = "library", library_id = 1, label = ShortcutLabel } } }, source = "profile_client" },
                } });
                if (HoldEffectiveRead)
                {
                    HoldEffectiveRead = false;
                    EffectiveReadEntered.TrySetResult();
                    await EffectiveReadRelease.Task.WaitAsync(ct);
                }
                return response;
            }
            if (request.Method != HttpMethod.Get && (path.Contains("nav.primary_menu") || path.Contains("ui.card_presentation")))
            {
                Writes++; WriteEntered.TrySetResult();
                if (HoldWrite) await WriteRelease.Task.WaitAsync(ct);
                return Json(new { });
            }
            throw new InvalidOperationException("Unexpected synthetic sidebar request: " + path);
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }
}
