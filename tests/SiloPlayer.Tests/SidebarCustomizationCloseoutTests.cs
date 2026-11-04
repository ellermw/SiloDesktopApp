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
        public int CapabilityStatus = 200, EffectiveStatus = 200, EffectiveReads, Writes;
        public string MenuLabel = "Accepted";
        public bool HoldWrite;
        public readonly TaskCompletionSource WriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource WriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/settings/contract/capabilities")
                return CapabilityStatus == 200 ? Json(new { api_version = 1, manifest_revision = 22,
                    client_families = new[] { "desktop", "web" }, supports_batched_effective = true, supports_idempotent_writes = true }) : new((HttpStatusCode)CapabilityStatus);
            if (path == "/api/v2/settings/values/effective")
            {
                EffectiveReads++;
                if (EffectiveStatus != 200) return new((HttpStatusCode)EffectiveStatus);
                return Json(new { items = new object[]
                {
                    new { key = "nav.primary_menu", value = new { items = new object[] { new { type = "builtin", destination = "home" }, new { type = "library", library_id = 1, label = MenuLabel } } }, source = "profile_client" },
                    new { key = "ui.card_presentation", value = new { poster_size = "compact", caption = "title_metadata" }, source = "profile_client" },
                } });
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
