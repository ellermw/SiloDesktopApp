using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class VideoPlaybackRatePreferencesTests
{
    [Fact]
    public void ChangingDeviceRetiresCapturedSettingsContext()
    {
        using var http = new HttpClient();
        var client = new SiloApiClient(http);
        client.SetDeviceMetadata("first", "Test", "Windows");
        var context = client.CaptureContext();
        client.SetDeviceMetadata("first", "Renamed", "Windows");
        Assert.True(client.IsCurrentContext(context));
        client.SetDeviceMetadata("replacement", "Test", "Windows");
        Assert.False(client.IsCurrentContext(context));
    }

    [Theory]
    [InlineData(0.25, true)]
    [InlineData(1.35, true)]
    [InlineData(3, true)]
    [InlineData(0.26, false)]
    [InlineData(0, false)]
    [InlineData(3.05, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void ContractValidationPreservesNonPresetSteps(double rate, bool valid)
        => Assert.Equal(valid, VideoPlaybackRatePreferences.IsValidRate(rate));

    [Theory]
    [InlineData("1.35", 1.35)]
    [InlineData("0.26", 1)]
    [InlineData("null", 1)]
    [InlineData("\"1.5\"", 1)]
    public async Task EffectiveReadUsesCapturedProfileAndDevice(string value, double expected)
    {
        using var f = new Fixture(); f.Handler.Value = value;
        await f.Session.LoadAsync();
        Assert.Equal(expected, f.Session.DesiredRate);
        Assert.Equal(expected, f.Applied.Last());
        Assert.Contains("device_id=desktop-test", f.Handler.ReadUri!.Query);
        Assert.Contains("profile_id=viewer", f.Handler.ReadUri.Query);
        Assert.Empty(f.Handler.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreDoesNotApplyDefaultBeforeOptionalReadSucceeds(bool failRead)
    {
        using var f = new Fixture(); f.Handler.HoldRead = true; f.Handler.FailRead = failRead;
        f.Session.Restore(); Assert.Empty(f.Applied);
        var loading = f.Session.LoadAsync(); await f.Handler.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        f.Session.Restore(); Assert.Empty(f.Applied);
        f.Editable = false;
        f.Handler.ReadRelease.TrySetResult(); await loading;
        Assert.Empty(f.Applied);
        f.Editable = true; f.Session.Restore();
        if (failRead)
        {
            Assert.Empty(f.Applied); Assert.Contains("retry", f.Session.Error!);
            await f.Session.SetAsync(1.5); f.Applied.Clear(); f.Session.Restore();
            Assert.Equal(new[] { 1.5 }, f.Applied);
        }
        else Assert.Equal(new[] { 1.35 }, f.Applied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreKeepsAcceptedManualDesireWhileReadAndSaveAreHeld(bool failRead)
    {
        using var f = new Fixture();
        f.Handler.HoldRead = true; f.Handler.FailRead = failRead; f.Handler.HoldFirstWrite = true;
        var loading = f.Session.LoadAsync(); await f.Handler.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var saving = f.Session.SetAsync(1.5); await f.Handler.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            f.Editable = false; f.Session.Restore();
            Assert.Equal(new[] { 1.5 }, f.Applied);
            f.Editable = true; f.Session.Restore();
            Assert.Equal(new[] { 1.5, 1.5 }, f.Applied);
            Assert.True(f.Session.IsSavePending);
            f.Handler.ReadRelease.TrySetResult(); await loading;
            Assert.Equal(1.5, f.Session.DesiredRate); Assert.Null(f.Session.Error);
            Assert.Equal(new[] { 1.5, 1.5 }, f.Applied);
        }
        finally { f.Handler.ReadRelease.TrySetResult(); f.Handler.WriteRelease.TrySetResult(); await saving; }
        Assert.False(f.Session.IsSavePending);
        Assert.Equal(1.5, Assert.Single(f.Handler.Writes).Rate);
    }

    [Fact]
    public async Task ManualSelectionAppliesImmediatelyAndBeatsHeldRead()
    {
        using var f = new Fixture(); f.Handler.HoldRead = true;
        var loading = f.Session.LoadAsync(); await f.Handler.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Session.SetAsync(1.75);
        Assert.Equal(1.75, f.Applied.Last());
        f.Handler.ReadRelease.TrySetResult(); await loading;
        Assert.Equal(1.75, f.Session.DesiredRate);
        Assert.Equal(new[] { 1.75 }, f.Applied);
        var write = Assert.Single(f.Handler.Writes);
        Assert.Equal(1.75, write.Rate);
        Assert.Contains("scope=profile_device", write.Uri.Query);
        Assert.Contains("device_id=desktop-test", write.Uri.Query);
        Assert.Contains("profile_id=viewer", write.Uri.Query);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewestManualWinsOldSaveResponseAndFailure(bool failOld)
    {
        using var f = new Fixture(); f.Handler.HoldFirstWrite = true; f.Handler.FailFirstWrite = failOld;
        var first = f.Session.SetAsync(1.25); await f.Handler.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = f.Session.SetAsync(2);
        Assert.Equal(2, f.Applied.Last()); Assert.True(f.Session.IsSavePending);
        Assert.Single(f.Handler.Writes);
        f.Handler.WriteRelease.TrySetResult(); await Task.WhenAll(first, second);
        Assert.Equal(new[] { 1.25, 2.0 }, f.Handler.Writes.Select(write => write.Rate));
        Assert.Equal(2, f.Session.DesiredRate); Assert.Null(f.Session.Error); Assert.False(f.Session.IsSavePending);
    }

    [Fact]
    public async Task CurrentSaveFailureKeepsDesiredRateAndAllowsRetry()
    {
        using var f = new Fixture(); f.Handler.FailFirstWrite = true;
        await f.Session.SetAsync(1.5);
        Assert.Equal(1.5, f.Session.DesiredRate); Assert.Contains("retry", f.Session.Error!);
        Assert.False(f.Session.IsSavePending);
        await f.Session.SetAsync(1.5);
        Assert.Null(f.Session.Error); Assert.Equal(2, f.Handler.Writes.Count);
    }

    [Fact]
    public async Task AcceptedQueuedManualSaveSurvivesSameOwnerReplan()
    {
        using var f = new Fixture(); f.Handler.HoldFirstWrite = true;
        var first = f.Session.SetAsync(1.25); await f.Handler.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = f.Session.SetAsync(1.75);
        f.Editable = false; // A same-file replan temporarily disables new menu actions.
        f.Handler.WriteRelease.TrySetResult(); await Task.WhenAll(first, second);
        Assert.Equal(new[] { 1.25, 1.75 }, f.Handler.Writes.Select(write => write.Rate));
        f.Editable = true; f.Session.Restore(); Assert.Equal(1.75, f.Applied.Last());
    }

    [Theory]
    [InlineData("file")]
    [InlineData("profile")]
    [InlineData("server")]
    [InlineData("device")]
    [InlineData("retired")]
    public async Task DelayedReadCannotTouchReplacementOwner(string replacement)
    {
        using var f = new Fixture(); f.Handler.HoldRead = true;
        var loading = f.Session.LoadAsync(); await f.Handler.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        f.Replace(replacement); f.Handler.ReadRelease.TrySetResult(); await loading;
        Assert.Empty(f.Applied); Assert.Null(f.Session.Error);
        await f.Session.SetAsync(2); Assert.Empty(f.Handler.Writes);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("profile")]
    [InlineData("server")]
    [InlineData("device")]
    [InlineData("retired")]
    public async Task QueuedWritePreservesPreferenceScopeWithoutPublishingToReplacementOwner(string replacement)
    {
        using var f = new Fixture(); f.Handler.HoldFirstWrite = true; f.Handler.FailFirstWrite = true;
        var first = f.Session.SetAsync(1.5); await f.Handler.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = f.Session.SetAsync(2); f.Replace(replacement);
        var changes = f.Changes;
        f.Handler.WriteRelease.TrySetResult(); await Task.WhenAll(first, second);
        if (replacement is "file" or "retired")
            Assert.Equal(new[] { 1.5, 2.0 }, f.Handler.Writes.Select(write => write.Rate));
        else Assert.Single(f.Handler.Writes);
        Assert.Equal(new[] { 1.5, 2.0 }, f.Applied);
        Assert.Equal(changes, f.Changes);
        Assert.Null(f.Session.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementReadWaitsForLatestAcceptedSaveAfterRetirement(bool failOld)
    {
        using var f = new Fixture(); f.Handler.HoldFirstWrite = true; f.Handler.FailFirstWrite = failOld;
        var first = f.Session.SetAsync(1.25); await f.Handler.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = f.Session.SetAsync(2);
        f.Session.Retire();
        var applied = new List<double>();
        var replacement = f.Preferences.CreateSession(() => true, () => true, applied.Add, () => { });
        var loading = replacement.LoadAsync();
        try
        {
            Assert.False(loading.IsCompleted);
            Assert.Empty(applied);
        }
        finally { f.Handler.WriteRelease.TrySetResult(); }
        await Task.WhenAll(first, second, loading).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { 1.25, 2.0 }, f.Handler.Writes.Select(write => write.Rate));
        Assert.Equal(2, replacement.DesiredRate);
        Assert.Equal(new[] { 2.0 }, applied);
        Assert.Equal(new[] { 1.25, 2.0 }, f.Applied);
        Assert.Null(replacement.Error);
        Assert.False(replacement.IsSavePending);
    }

    [Fact]
    public async Task RoomOwnershipBlocksManualAndDriftNeverChangesViewerDesire()
    {
        using var f = new Fixture(); await f.Session.SetAsync(1.5);
        f.Editable = false; // Same gate is used for room, audiobook and remote ownership.
        await f.Session.SetAsync(2);
        Assert.Equal(1.5, f.Session.DesiredRate); Assert.Single(f.Handler.Writes);
        f.Applied.Add(1.03); // Transient native room correction has no settings entry point.
        f.Session.Restore(); Assert.Equal(1.03, f.Applied.Last());
        f.Editable = true; f.Session.Restore(); Assert.Equal(1.5, f.Applied.Last());
        Assert.Single(f.Handler.Writes);
    }

    [Fact]
    public async Task CurrentReadFailureIsVisibleAndManualSelectionRecovers()
    {
        using var f = new Fixture(); f.Handler.FailRead = true;
        await f.Session.LoadAsync(); Assert.Contains("retry", f.Session.Error!);
        Assert.Equal(1, f.Session.DesiredRate);
        await f.Session.SetAsync(1.35); Assert.Null(f.Session.Error); Assert.Equal(1.35, f.Session.DesiredRate);
    }

    [Fact]
    public async Task AccessRefreshKeepsTheOwnerAndPersistsHeldAndQueuedSaves()
    {
        using var f = new Fixture(); f.Handler.HoldRead = true; f.Handler.HoldFirstWrite = true;
        var loading = f.Session.LoadAsync(); await f.Handler.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var first = f.Session.SetAsync(1.5); await f.Handler.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        f.Client.InvalidateAccessContext();
        Assert.True(f.Session.CanChange); Assert.True(f.Session.IsSavePending);
        Assert.Equal(1.5, f.Session.DesiredRate);
        var second = f.Session.SetAsync(2);
        f.Handler.ReadRelease.TrySetResult(); f.Handler.WriteRelease.TrySetResult();
        await Task.WhenAll(loading, first, second).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1.5, f.Handler.Writes.First().Rate); Assert.Equal(2, f.Handler.Writes.Last().Rate);
        Assert.True(f.Session.CanChange); Assert.False(f.Session.IsSavePending); Assert.Null(f.Session.Error);
        Assert.Equal(2, f.Session.DesiredRate);
        f.Client.InvalidateAccessContext(); f.Session.Restore(); Assert.Equal(2, f.Applied.Last());
    }

    private sealed class Fixture : IDisposable
    {
        public Handler Handler { get; } = new();
        private readonly HttpClient _http;
        public SiloApiClient Client { get; }
        public VideoPlaybackRatePreferences Preferences { get; }
        public VideoPlaybackRateSession Session { get; }
        public List<double> Applied { get; } = [];
        public int Changes { get; private set; }
        public bool Current = true, Editable = true;
        public Fixture()
        {
            _http = new(Handler); Client = new(_http);
            Client.SetBaseUrl("https://speed.invalid"); Client.SetProfile("viewer");
            Client.SetDeviceMetadata("desktop-test", "Synthetic", "Windows");
            Preferences = new VideoPlaybackRatePreferences(new SettingsApi(Client));
            Session = Preferences.CreateSession(
                () => Current, () => Editable, Applied.Add, () => Changes++);
        }
        public void Replace(string kind)
        {
            if (kind == "profile") Client.SetProfile("replacement");
            else if (kind == "server") Client.SetBaseUrl("https://replacement.invalid");
            else if (kind == "device") Client.SetDeviceMetadata("replacement", "Synthetic", "Windows");
            else if (kind == "retired") Session.Retire();
            else Current = false;
        }
        public void Dispose() => _http.Dispose();
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Value = "1.35";
        public bool HoldRead, HoldFirstWrite, FailFirstWrite, FailRead;
        public Uri? ReadUri;
        public List<(Uri Uri, double Rate)> Writes { get; } = [];
        public TaskCompletionSource ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                ReadUri = request.RequestUri;
                var readValue = Value;
                if (HoldRead) { ReadEntered.TrySetResult(); await ReadRelease.Task; }
                return Json(FailRead ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
                    "{\"items\":[{\"key\":\"player.playback_speed\",\"value\":" + readValue + "}]}");
            }
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var value = body.RootElement.GetProperty("value");
            Writes.Add((request.RequestUri!, value.GetDouble()));
            var first = Writes.Count == 1;
            if (first && HoldFirstWrite) { WriteEntered.TrySetResult(); await WriteRelease.Task; }
            var failed = first && FailFirstWrite;
            if (!failed) Value = value.GetRawText();
            return Json(failed ? HttpStatusCode.InternalServerError : HttpStatusCode.OK, "{}");
        }
        private static HttpResponseMessage Json(HttpStatusCode status, string value)
            => new(status) { Content = new StringContent(value) };
    }
}
