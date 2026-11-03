using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class RetainedLibraryStateTests
{
    [Fact]
    public async Task RetiredReadCannotBlockNewOwnerBehindIgnoredCancellation()
    {
        using var wire = new Wire { ReadGate = new(), HoldFirstReadOnly = true };
        var old = wire.Store.ReadAsync(11); await wire.ReadStarted.Task;
        wire.Client.SetProfile("next"); wire.Libraries[11] = "next";
        var next = wire.Store.ReadAsync(11);
        try { Assert.Same(next, await Task.WhenAny(next, Task.Delay(500))); Assert.Equal("next", await next); }
        finally { wire.ReadGate.SetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
    }

    [Fact]
    public async Task CanceledVisitReadReleasesSameOwnerForSuccessor()
    {
        using var wire = new Wire { ReadGate = new(), HoldFirstReadOnly = true };
        using var visit = new CancellationTokenSource();
        var old = wire.Store.ReadAsync(11, visit.Token); await wire.ReadStarted.Task;
        visit.Cancel(); var next = wire.Store.ReadAsync(11);
        try { Assert.Same(next, await Task.WhenAny(next, Task.Delay(500))); Assert.Equal("old", await next); }
        finally { wire.ReadGate.SetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
    }

    [Fact]
    public async Task MissingAndDisabledAreDifferentSnapshots()
    {
        using var wire = new Wire();
        Assert.Equal(new(true, null), await wire.Store.ReadSnapshotAsync(99));
        wire.Remember = false; wire.Settings.NotifyLocalizedSettingChanged("ui.remember_library_page_state");
        Assert.Equal(new(false, null), await wire.Store.ReadSnapshotAsync(99));
    }

    [Fact]
    public async Task StoreWrappersShareOnlyTheirActualSettingsOwner()
    {
        using var a = new Wire(); using var b = new Wire();
        a.Libraries[11] = "alpha"; b.Libraries[11] = "beta";
        Assert.Equal("alpha", await a.Store.ReadAsync(11));
        Assert.Equal("alpha", await new LibraryPageStateStore(a.Settings).ReadAsync(11));
        Assert.Equal("beta", await b.Store.ReadAsync(11));
        Assert.Equal(1, a.Reads); Assert.Equal(1, b.Reads);
    }

    [Fact]
    public async Task InvalidationWhileWriteReadIsHeldRechecksOptOutBeforePut()
    {
        using var wire = new Wire(); wire.ReadGate = new();
        var write = wire.Store.WriteAsync(11, "new");
        await wire.ReadStarted.Task;
        wire.Remember = false;
        wire.Settings.NotifyLocalizedSettingChanged("ui.remember_library_page_state");
        wire.ReadGate.SetResult(); await write;
        Assert.Equal(0, wire.Writes);
        Assert.Equal(new(false, null), await wire.Store.ReadSnapshotAsync(11));
    }

    [Fact]
    public async Task InvalidationWhileMergeReadIsHeldPreservesExternalLibrary()
    {
        using var wire = new Wire(); wire.ReadGate = new();
        var write = wire.Store.WriteAsync(11, "new"); await wire.ReadStarted.Task;
        wire.Libraries[22] = "external"; wire.Settings.NotifyLocalizedSettingChanged("ui.library_page_state");
        wire.ReadGate.SetResult(); await write;
        Assert.Equal("external", wire.Libraries[22]);
    }

    [Fact]
    public async Task PendingWriteThenLateEchoReadsAcceptedDocumentWithoutAnotherPut()
    {
        using var wire = new Wire(); wire.PutGate = new();
        await wire.Store.ReadAsync(11);
        var save = wire.Store.WriteAsync(11, "new"); await wire.PutStarted.Task;
        wire.Store.NotifyRealtime("user_settings", "user_settings.changed", Event());
        var read = wire.Store.ReadAsync(11);
        Assert.False(read.IsCompleted);
        wire.PutGate.SetResult(); await save;
        Assert.Equal("new", await read);
        var reads = wire.Reads;
        wire.Store.NotifyRealtime("user_settings", "user_settings.changed", Event());
        var burst = Enumerable.Range(0, 8).Select(_ => wire.Store.ReadAsync(11)).ToArray();
        var values = await Task.WhenAll(burst);
        Assert.All(values, value => Assert.Equal("new", value));
        Assert.Equal(reads + 1, wire.Reads);
        await wire.Store.WriteAsync(11, "new");
        Assert.Equal(1, wire.Writes);
    }

    [Fact]
    public async Task ConcurrentLibrariesMergeAcrossStoreWrappers()
    {
        using var wire = new Wire(); wire.PutGate = new();
        var first = wire.Store.WriteAsync(11, "one"); await wire.PutStarted.Task;
        var second = new LibraryPageStateStore(wire.Settings).WriteAsync(22, "two");
        wire.PutGate.SetResult(); await Task.WhenAll(first, second);
        Assert.Equal("one", wire.Libraries[11]); Assert.Equal("two", wire.Libraries[22]);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("server")]
    [InlineData("session")]
    [InlineData("device")]
    [InlineData("device-aba")]
    public async Task RetiredOwnerCannotConfirmAcceptedWrite(string change)
    {
        using var wire = new Wire(); wire.PutGate = new();
        var save = wire.Store.WriteAsync(11, "retired"); await wire.PutStarted.Task;
        switch (change)
        {
            case "profile": wire.Client.SetProfile("other"); break;
            case "server": wire.Client.SetBaseUrl("https://other.invalid"); break;
            case "session": wire.Client.ClearAuth(); break;
            case "device": wire.Client.SetDeviceMetadata("other", "Other", "test"); break;
            case "device-aba": wire.Client.SetDeviceMetadata("other", "Other", "test"); wire.Client.SetDeviceMetadata("device", "Same", "test"); break;
        }
        wire.PutGate.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
    }

    [Fact]
    public void DeviceNameUpdateDoesNotRetireIdentity()
    {
        using var wire = new Wire(); var owner = wire.Settings.CaptureSettingsContext();
        wire.Client.SetDeviceMetadata("device", "Renamed", "test");
        Assert.True(wire.Settings.IsCurrentSettingsContext(owner));
    }

    [Fact]
    public async Task RealtimeOtherProfileAndUnrelatedKeysDoNotInvalidate()
    {
        using var wire = new Wire(); await wire.Store.ReadAsync(11);
        wire.Store.NotifyRealtime("user_settings", "user_settings.changed", Event(profile: "other"));
        wire.Store.NotifyRealtime("user_settings", "user_settings.changed", Event(key: "ui.title_art"));
        await wire.Store.ReadAsync(11); Assert.Equal(1, wire.Reads);
        wire.Libraries[11] = "external";
        wire.Store.NotifyRealtime("user_settings", "user_settings.changed", Event());
        Assert.Equal("external", await wire.Store.ReadAsync(11)); Assert.Equal(2, wire.Reads);
    }

    [Theory]
    [InlineData("put")]
    [InlineData("delete")]
    [InlineData("context-put")]
    [InlineData("context-delete")]
    [InlineData("device-reset")]
    [InlineData("device-forget")]
    public async Task ActualSettingRoutesInvalidateConfirmedState(string route)
    {
        using var wire = new Wire(); await wire.Store.ReadAsync(11);
        switch (route)
        {
            case "put": await wire.Settings.PutDeviceSettingAsync("ui.remember_library_page_state", "false"); break;
            case "delete": await wire.Settings.DeleteDeviceSettingAsync("ui.remember_library_page_state"); break;
            case "context-put": await wire.Settings.SetContractSettingValueAsync(wire.Client.CaptureContext(), "ui.remember_library_page_state", "profile_device", false); break;
            case "context-delete": await wire.Settings.DeleteContractSettingValueAsync(wire.Client.CaptureContext(), "ui.remember_library_page_state", "profile_device"); break;
            case "device-reset": await wire.Settings.ClearUserDeviceSettingsAsync("device"); break;
            case "device-forget": await wire.Settings.ForgetUserDeviceAsync("device"); break;
        }
        await wire.Store.ReadAsync(11); Assert.Equal(2, wire.Reads);
    }

    private static JsonElement Event(string key = "ui.library_page_state", string profile = "profile")
        => JsonSerializer.SerializeToElement(new { key, scope = "profile_device", profile_id = profile });

    private sealed class Wire : HttpMessageHandler
    {
        internal readonly SiloApiClient Client;
        internal readonly SettingsApi Settings;
        internal LibraryPageStateStore Store => Settings.LibraryPageState;
        internal Dictionary<int, string> Libraries = new() { [11] = "old" };
        internal bool Remember = true;
        internal bool HoldFirstReadOnly;
        internal int Reads, Writes;
        internal TaskCompletionSource? ReadGate, PutGate;
        internal readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource PutStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Wire()
        {
            Client = new(new HttpClient(this)); Client.SetBaseUrl("https://library-state.invalid");
            Client.SetProfile("profile"); Client.SetDeviceMetadata("device", "Fixture", "test"); Settings = new(Client);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                Reads++;
                var body = JsonSerializer.Serialize(new { items = new object[] { new { key = "ui.remember_library_page_state", value = (object)Remember },
                    new { key = "ui.library_page_state", value = (object)new { version = 1, libraries = Libraries.ToDictionary(p => p.Key.ToString(), p => new { search = p.Value }) } } } });
                ReadStarted.TrySetResult(); if (ReadGate is { } gate && (!HoldFirstReadOnly || Reads == 1)) await gate.Task;
                return new(HttpStatusCode.OK) { Content = new StringContent(body) };
            }
            if (request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath.EndsWith("ui.library_page_state"))
            {
                Writes++; PutStarted.TrySetResult(); if (PutGate is { } gate) await gate.Task;
                var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync());
                Libraries = body.GetProperty("value").GetProperty("libraries").EnumerateObject().ToDictionary(p => int.Parse(p.Name), p => p.Value.GetProperty("search").GetString()!);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
