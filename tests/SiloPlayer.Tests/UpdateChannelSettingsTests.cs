using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class UpdateChannelSettingsTests : IDisposable
{
    [Theory]
    [InlineData("Views/SettingsPage.Updates.cs", "await updates.SetChannelAsync(channel, ct);", "await updates.CheckAsync(ct);")]
    [InlineData("MainWindow.Updates.cs", "await updateService.SetChannelAsync(channel, ct);", "await updateService.CheckAsync(ct);")]
    public void ChannelSelectionUsesAppLifetimeAndServiceOwnedPersistence(string file, string apply, string check)
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", file));
        var applied = source.IndexOf(apply, StringComparison.Ordinal);
        var checkedAt = source.IndexOf(check, StringComparison.Ordinal);
        Assert.True(applied >= 0 && checkedAt > applied);
        Assert.Contains("ct.ThrowIfCancellationRequested();", source[applied..checkedAt]);
        Assert.Contains("IsChangingChannel) return;", source[applied..checkedAt]);
        Assert.DoesNotContain(".UpdateChannel = channel", source);
        Assert.Contains("AppUpdateLifetimeToken", source);
        Assert.DoesNotContain("_settingsUpdateLifetime", source);
        var app = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "App.xaml.cs"));
        Assert.Contains("PersistChannel=channel =>", app);
        Assert.Contains("settings.UpdateChannel=channel;", app);
        Assert.Contains("try { preferences.Save(settings); }", app);
    }

    private readonly string root = Path.Combine(Path.GetTempPath(), "silo-channel-settings-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(AppUpdateChannel.Stable)]
    [InlineData(AppUpdateChannel.Beta)]
    public async Task CancelingTheDrainWhileTheOperationGateIsHeldKeepsRuntimeAndSavedChannelsTogether(AppUpdateChannel initial)
    {
        await WriteExistingAsync(JsonValue.Create((int)initial), true);
        var preferences = new SettingsService(root);
        using var http = new HttpClient(new Wire());
        var service = new AppUpdateService(http, Path.Combine(root, "cache"), new Version(1, 2, 517, 0))
        {
            Channel = initial,
            InstalledReleaseVersion = ReleaseVersion.Parse("1.2.517"),
            PersistChannel = channel =>
            {
                var settings = preferences.Load(); settings.UpdateChannel = channel; preferences.Save(settings);
            }
        };
        await service.CheckAsync();
        var operation = (SemaphoreSlim)typeof(AppUpdateService).GetField("_operation",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(service)!;
        await operation.WaitAsync();
        var selected = initial == AppUpdateChannel.Beta ? AppUpdateChannel.Stable : AppUpdateChannel.Beta;
        using var stop = new CancellationTokenSource();
        try
        {
            var changing = service.SetChannelAsync(selected, stop.Token);
            Assert.False(changing.IsCompleted);
            Assert.Equal(selected, service.Channel);
            Assert.Equal(service.Channel, new SettingsService(root).Load().UpdateChannel);
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => changing);
            Assert.False(service.IsChangingChannel);
            Assert.Equal(selected, service.Channel);
            Assert.Equal(service.Channel, new SettingsService(root).Load().UpdateChannel);
            Assert.Null(service.State.Release); Assert.Null(service.State.ReadyInstaller); Assert.Null(service.State.PreparedPackage);
            Assert.Contains("has not been checked", service.State.Error!);
            Assert.False(service.ShouldInstallOnExit);
            AssertPreserved(new SettingsService(root).Load());
        }
        finally { operation.Release(); }
        await service.CheckAsync();
        Assert.Null(service.State.Error);
        Assert.Equal(service.Channel, new SettingsService(root).Load().UpdateChannel);
        Assert.Equal(selected == AppUpdateChannel.Beta, service.State.Release?.IsBeta == true);
    }

    [Fact]
    public async Task FailedPreferenceSaveLeavesRuntimeStateAndAuthorityUntouched()
    {
        using var http = new HttpClient(new Wire());
        var service = new AppUpdateService(http, Path.Combine(root, "cache"), new Version(1, 2, 517, 0))
        { PersistChannel = _ => throw new IOException("Synthetic save failure") };
        await service.CheckAsync();
        var state = service.State;
        await Assert.ThrowsAsync<IOException>(() => service.SetChannelAsync(AppUpdateChannel.Beta));
        Assert.Equal(AppUpdateChannel.Stable, service.Channel);
        Assert.False(service.IsChangingChannel);
        Assert.Same(state, service.State);
    }
    private async Task WriteExistingAsync(JsonNode? channel = null, bool includeChannel = false)
    {
        Directory.CreateDirectory(root);
        var json = JsonNode.Parse("""{"deviceId":"synthetic-device","lastProfileId":"synthetic-viewer","profileLaunchMode":"ask","playerVolume":42,"playerMuted":true,"siloCastEnabled":false,"hiddenLibraryIds":[3],"updateMode":1,"servers":[{"url":"https://silo.invalid","name":"Synthetic server"}]}""")!;
        if (includeChannel) json["updateChannel"] = channel;
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), json.ToJsonString());
    }

    [Fact]
    public async Task ExistingSettingsDefaultToStableAndBetaChoiceSurvivesReopeningWithoutPreferenceLoss()
    {
        await WriteExistingAsync(); var settings = new SettingsService(root); var current = settings.Load();
        Assert.Equal(AppUpdateChannel.Stable, current.UpdateChannel);
        current.UpdateChannel = AppUpdateChannel.Beta; settings.Save(current);
        var reopened = new SettingsService(root).Load(); Assert.Equal(AppUpdateChannel.Beta, reopened.UpdateChannel);
        AssertPreserved(reopened);
        reopened.UpdateChannel = AppUpdateChannel.Stable; new SettingsService(root).Save(reopened);
        Assert.Equal(AppUpdateChannel.Stable, new SettingsService(root).Load().UpdateChannel);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("-1")]
    [InlineData("\"unknown\"")]
    [InlineData("{\"unrecognized\":\"Beta\"}")]
    [InlineData("null")]
    public async Task InvalidChannelFallsBackToStableWithoutQuarantiningUnrelatedPreferences(string value)
    {
        await WriteExistingAsync(JsonNode.Parse(value), true); var loaded = new SettingsService(root).Load();
        Assert.Equal(AppUpdateChannel.Stable, loaded.UpdateChannel); AssertPreserved(loaded);
        Assert.Empty(Directory.EnumerateFiles(root, "settings*.corrupt*"));
    }

    [Fact]
    public async Task PersistedBetaSelectionAppliesBeforeTheVeryFirstAutomaticCheck()
    {
        await WriteExistingAsync(JsonValue.Create(1), true); var preferences = new SettingsService(root).Load();
        using var http = new HttpClient(new Wire());
        var service = new AppUpdateService(http, Path.Combine(root, "cache"), new Version(1, 2, 517, 0))
        { Channel = preferences.UpdateChannel, InstalledReleaseVersion = ReleaseVersion.Parse("1.2.517") };
        await service.CheckAsync();
        Assert.Equal("1.2.518-beta.12", service.State.Release?.DisplayVersion);
        Assert.Equal(AppUpdateChannel.Beta, service.Channel);
    }

    private static void AssertPreserved(SiloPlayer.Core.Models.AppSettings settings)
    {
        Assert.Equal("synthetic-device", settings.DeviceId); Assert.Equal("synthetic-viewer", settings.LastProfileId);
        Assert.Equal("ask", settings.ProfileLaunchMode); Assert.Equal(42, settings.PlayerVolume); Assert.True(settings.PlayerMuted);
        Assert.False(settings.SiloCastEnabled); Assert.Equal(new[] { 3 }, settings.HiddenLibraryIds);
        Assert.Equal(AppUpdateMode.Automatic, settings.UpdateMode); Assert.Equal("https://silo.invalid", Assert.Single(settings.Servers).Url);
    }
    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(request.Headers.Authorization);
            var body = request.RequestUri!.AbsolutePath.EndsWith("/latest") ? UpdateChannelTests.Release("1.2.517").ToJsonString()
                : JsonSerializer.Serialize(new[] { UpdateChannelTests.Release("1.2.518-beta.12") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
