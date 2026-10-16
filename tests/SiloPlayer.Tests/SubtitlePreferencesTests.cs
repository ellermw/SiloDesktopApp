using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitlePreferencesTests
{
    [Theory]
    [InlineData(19, false)]
    [InlineData(18, false)]
    [InlineData(19, true)]
    [InlineData(18, true)]
    public async Task SharedOffsetWriterSerializesSaveAndResetThroughShadowCleanup(int revision, bool reset)
    {
        using var f = new Fixture(revision); var preferences = f.Preferences;
        var state = await preferences.LoadAsync(f.Api.CaptureContext());
        f.Handler.HoldPut = true;
        var first = f.Api.Subtitles.SaveOffsetAsync(state, 600);
        await f.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = reset ? preferences.ResetOffsetAsync(state) : preferences.SaveOffsetAsync(state, 900);
        await Task.Delay(50); var before = f.Handler.Writes.Count;
        f.Handler.Release.TrySetResult(); await Task.WhenAll(first, second);
        Assert.Equal(1, before);
        Assert.Equal((reset ? "DELETE " : "PUT ") + (revision == 19 ? "profile" : "profile_device"), f.Handler.Writes[revision == 19 ? 2 : 1]);
    }
    [Fact]
    public async Task ProfileAppearanceWritesTypedObjectThenClearsSourceOnlyDeviceShadow()
    {
        using var f = new Fixture();
        var state = await f.Preferences.LoadAsync(f.Api.CaptureContext());
        Assert.True(state.IsAppearanceCustomized);
        Assert.Equal(450, state.OffsetMs);
        await f.Preferences.SaveAppearanceAsync(state, new SubtitleAppearance { FontColor = "#facc15" });
        Assert.Equal(new[] { "PUT profile", "DELETE profile_device" }, f.Handler.Writes);
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(f.Handler.Body).RootElement.GetProperty("value").ValueKind);
    }

    [Theory]
    [InlineData(18, 1, "profile_device")]
    [InlineData(19, 1, "profile")]
    [InlineData(20, 2, "profile_device")]
    public async Task TimingWritesOnlySupportedScopeAndClamps(int revision, int version, string scope)
    {
        using var f = new Fixture(revision, version);
        var state = await f.Preferences.LoadAsync(f.Api.CaptureContext());
        await f.Preferences.SaveOffsetAsync(state, 12000);
        Assert.Equal("PUT " + scope, f.Handler.Writes[0]);
        Assert.Equal(scope == "profile" ? 2 : 1, f.Handler.Writes.Count);
        Assert.Equal(10000, JsonDocument.Parse(f.Handler.Body).RootElement.GetProperty("value").GetInt32());
        Assert.Equal(-10000, SubtitlePreferences.ClampOffset(-12000));
    }

    [Theory]
    [InlineData(18)]
    [InlineData(19)]
    public async Task ResetToleratesMissingRowsAndKeepsOlderServerDeviceOnly(int revision)
    {
        using var f = new Fixture(revision);
        var state = await f.Preferences.LoadAsync(f.Api.CaptureContext());
        f.Handler.DeleteStatus = HttpStatusCode.NotFound;
        await f.Preferences.ResetOffsetAsync(state);
        Assert.Equal(revision == 19 ? new[] { "DELETE profile", "DELETE profile_device" } : new[] { "DELETE profile_device" }, f.Handler.Writes);
        f.Handler.Writes.Clear();
        await f.Preferences.ResetAppearanceAsync(state);
        Assert.Equal(new[] { "DELETE profile", "DELETE profile_device" }, f.Handler.Writes);
    }

    [Fact]
    public async Task ProfileAndDefaultSourcesAvoidUnnecessaryCleanup()
    {
        using var f = new Fixture(); f.Handler.Source = "default";
        var state = await f.Preferences.LoadAsync(f.Api.CaptureContext());
        Assert.False(state.IsAppearanceCustomized);
        await f.Preferences.SaveAppearanceAsync(state, new());
        Assert.Equal(new[] { "PUT profile" }, f.Handler.Writes);
    }

    [Fact]
    public async Task FailedCleanupIsReportedAndDoesNotPretendSaveSucceeded()
    {
        using var f = new Fixture(); var state = await f.Preferences.LoadAsync(f.Api.CaptureContext());
        f.Handler.DeleteStatus = HttpStatusCode.InternalServerError;
        await Assert.ThrowsAsync<ApiException>(() => f.Preferences.SaveAppearanceAsync(state, new()));
        Assert.Equal(new[] { "PUT profile", "DELETE profile_device" }, f.Handler.Writes);
    }

    [Fact]
    public async Task OwnerReplacementBetweenPutAndCleanupCannotClearReplacementDevice()
    {
        using var f = new Fixture(); var state = await f.Preferences.LoadAsync(f.Api.CaptureContext());
        f.Handler.AfterPut = () => f.Client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Preferences.SaveOffsetAsync(state, 100));
        Assert.Equal(new[] { "PUT profile" }, f.Handler.Writes);
    }

    [Fact]
    public async Task HeldCapabilitiesCannotDispatchEffectiveReadForNewOwner()
    {
        using var f = new Fixture(); f.Handler.HoldCapabilities = true;
        var loading = f.Preferences.LoadAsync(f.Api.CaptureContext());
        await f.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        f.Client.SetProfile("replacement"); f.Handler.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        Assert.Equal(0, f.Handler.EffectiveReads);
    }

    [Fact]
    public async Task HeldEffectiveCannotPublishSnapshotForNewServerWithSameProfile()
    {
        using var f = new Fixture(); f.Handler.HoldEffective = true;
        var loading = f.Preferences.LoadAsync(f.Api.CaptureContext());
        await f.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        f.Client.SetBaseUrl("https://replacement.invalid"); f.Handler.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        Assert.Empty(f.Handler.Writes);
    }

    private sealed class Fixture(int revision = 19, int version = 1) : IDisposable
    {
        public Handler Handler { get; } = new(revision, version);
        public SiloApiClient Client { get; private set; } = null!;
        public SettingsApi Api { get; private set; } = null!;
        private HttpClient? _http;
        private SubtitlePreferences? _preferences;
        public SubtitlePreferences Preferences
        {
            get
            {
                if (_preferences != null) return _preferences;
                _http = new(Handler); Client = new(_http); Client.SetBaseUrl("https://subtitle.invalid"); Client.SetProfile("viewer");
                Api = new(Client); return _preferences = Api.Subtitles;
            }
        }
        public void Dispose() => _http?.Dispose();
    }

    private sealed class Handler(int revision, int version) : HttpMessageHandler
    {
        public List<string> Writes { get; } = [];
        public string Body = "";
        public string Source = "profile_device";
        public HttpStatusCode DeleteStatus = HttpStatusCode.NoContent;
        public Action? AfterPut;
        public bool HoldCapabilities, HoldEffective, HoldPut;
        public int EffectiveReads;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                if (path.EndsWith("capabilities"))
                {
                    if (HoldCapabilities) { Entered.TrySetResult(); await Release.Task; }
                    return Json(new { api_version = version, manifest_revision = revision });
                }
                EffectiveReads++;
                if (HoldEffective) { Entered.TrySetResult(); await Release.Task; }
                return Json(new { items = new object[] {
                    new { key = SubtitlePreferences.AppearanceKey, source = Source, value = (object)new { fontColor = "#ffffff" } },
                    new { key = SubtitlePreferences.OffsetKey, source = Source, value = (object)450 } } });
            }
            var scope = request.RequestUri.Query.Contains("scope=profile_device") ? "profile_device" : "profile";
            Writes.Add(request.Method + " " + scope);
            if (request.Method == HttpMethod.Put)
            {
                if (HoldPut) { HoldPut = false; Entered.TrySetResult(); await Release.Task; }
                Body = await request.Content!.ReadAsStringAsync(ct); AfterPut?.Invoke(); return Json(new { });
            }
            return new(DeleteStatus) { Content = new StringContent("{}") };
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
