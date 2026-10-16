using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class NewEpisodesFirstTests
{
    [Theory]
    [InlineData(17, 1)]
    [InlineData(18, 2)]
    [InlineData(0, 1)]
    public async Task UnsupportedContractNeverQueriesOrWritesNewKey(int revision, int version)
    {
        using var f = new Fixture(revision, version);
        var snapshot = await f.Preference.LoadAsync(f.Api.CaptureContext());
        Assert.False(snapshot.Supported); Assert.False(snapshot.Value);
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Preference.SaveAsync(snapshot, true));
        Assert.Equal(0, f.Wire.Reads); Assert.Empty(f.Wire.Writes);
    }

    [Theory]
    [InlineData(false, "default")]
    [InlineData(true, "profile")]
    public async Task SupportedContractUsesEffectiveBooleanAndWinningSource(bool value, string source)
    {
        using var f = new Fixture(); f.Wire.Value = value; f.Wire.Source = source;
        var snapshot = await f.Preference.LoadAsync(f.Api.CaptureContext());
        Assert.True(snapshot.Supported); Assert.Equal(value, snapshot.Value); Assert.Equal(source, snapshot.Source);
        Assert.Equal(1, f.Wire.Reads);
    }

    [Fact]
    public async Task SaveWritesBooleanOnlyToCapturedProfileAndReloadUsesServerValue()
    {
        using var f = new Fixture(); var snapshot = await f.Preference.LoadAsync(f.Api.CaptureContext());
        await f.Preference.SaveAsync(snapshot, true);
        Assert.Equal(new[] { "PUT profile viewer" }, f.Wire.Writes);
        Assert.True(JsonDocument.Parse(f.Wire.Body).RootElement.GetProperty("value").GetBoolean());
        f.Wire.Value = false; f.Wire.Source = "default";
        var reloaded = await f.Preference.LoadAsync(snapshot.Context);
        Assert.False(reloaded.Value); Assert.Equal("default", reloaded.Source);
    }

    [Fact]
    public async Task MissingCapabilitiesAreUnsupportedWithoutEffectiveRequest()
    {
        using var f = new Fixture(); f.Wire.MissingCapabilities = true;
        Assert.False((await f.Preference.LoadAsync(f.Api.CaptureContext())).Supported);
        Assert.Equal(0, f.Wire.Reads);
    }

    [Theory]
    [InlineData("capabilities")]
    [InlineData("effective")]
    [InlineData("put")]
    public async Task HeldCompletionCannotPublishForReplacementOwner(string stage)
    {
        using var f = new Fixture(); var context = f.Api.CaptureContext();
        var snapshot = stage == "put" ? await f.Preference.LoadAsync(context) : null;
        f.Wire.Hold = stage;
        var pending = stage == "put" ? f.Preference.SaveAsync(snapshot!, true) : f.Preference.LoadAsync(context);
        await f.Wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        f.Client.SetBaseUrl("https://replacement.invalid"); f.Wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        if (stage == "capabilities") Assert.Equal(0, f.Wire.Reads);
        Assert.All(f.Wire.Hosts, host => Assert.Equal("new-first.invalid", host));
    }

    [Theory]
    [InlineData("capabilities")]
    [InlineData("effective")]
    [InlineData("put")]
    public async Task CurrentTimeoutRemainsAnErrorForCaller(string stage)
    {
        using var f = new Fixture(); var context = f.Api.CaptureContext();
        var snapshot = stage == "put" ? await f.Preference.LoadAsync(context) : null;
        f.Wire.Timeout = stage;
        var pending = stage == "put" ? f.Preference.SaveAsync(snapshot!, true) : f.Preference.LoadAsync(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(f.Api.IsCurrentContext(context));
    }

    private sealed class Fixture : IDisposable
    {
        public Wire Wire { get; }
        private readonly HttpClient _http;
        public SiloApiClient Client { get; }
        public SettingsApi Api { get; }
        public NewEpisodesFirstPreference Preference { get; }
        public Fixture(int revision = 18, int version = 1)
        {
            Wire = new(revision, version); _http = new(Wire); Client = new(_http);
            Client.SetBaseUrl("https://new-first.invalid"); Client.SetProfile("viewer"); Api = new(Client); Preference = new(Api);
        }
        public void Dispose() => _http.Dispose();
    }

    private sealed class Wire(int revision, int version) : HttpMessageHandler
    {
        public bool Value, MissingCapabilities;
        public string Source = "default", Body = "", Hold = "", Timeout = "";
        public int Reads;
        public List<string> Writes = [], Hosts = [];
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Hosts.Add(request.RequestUri!.Host);
            var stage = request.Method == HttpMethod.Put ? "put" : request.RequestUri.AbsolutePath.EndsWith("capabilities") ? "capabilities" : "effective";
            if (stage == "effective")
            {
                Assert.Contains("keys=ui.next_up_new_releases_first", request.RequestUri.Query); Reads++;
            }
            if (stage == "put")
            {
                Assert.EndsWith("/ui.next_up_new_releases_first", request.RequestUri.AbsolutePath);
                Assert.Contains("scope=profile&", request.RequestUri.Query); Assert.DoesNotContain("device_id", request.RequestUri.Query);
                Assert.Contains("profile_id=viewer", request.RequestUri.Query);
                Writes.Add("PUT profile viewer"); Body = await request.Content!.ReadAsStringAsync(ct);
            }
            if (Timeout == stage) throw new TaskCanceledException("Synthetic current request timeout.");
            if (Hold == stage) { Entered.TrySetResult(); await Release.Task; }
            if (stage == "capabilities") return MissingCapabilities ? new(HttpStatusCode.NotFound) : Json(new { api_version = version, manifest_revision = revision });
            if (stage == "put") return Json(new { });
            return Json(new { items = new[] { new { key = "ui.next_up_new_releases_first", value = Value, source = Source } } });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
