using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitleInventoryConcurrencyTests
{
    [Fact]
    public void CaptureCycleAndAutoReadNativeInventoryOnceNotOncePerRow()
    {
        // PlayerService requires WinUI/libmpv. Guard the call graph here: one
        // snapshot per operation, and no native/URL reads in either row resolver.
        var service = Read("SiloPlayer", "PlayerService.cs");
        var capture = Between(Read("SiloPlayer", "PlayerService.SiloCast.cs"),
            "public JsonObject CaptureSiloCastState()", "public async Task ApplySiloCastControlAsync(");
        var snapshot = Between(service, "private SubtitleRouteSnapshot CaptureSubtitleSelectionRoutes(",
            "private bool HasSubtitleSelectionRoute(");
        var route = Between(service, "private static bool HasSubtitleSelectionRoute(",
            "private void CommitExecutedSubtitleSelection(");
        var resolve = Between(service, "private static int ResolveNativeEmbeddedSidFromSnapshot(",
            "private void SendTitleToOsc()");
        Assert.Equal(1, Count(capture, "CaptureSubtitleSelectionRoutes("));
        Assert.Contains("subtitleInventory.Where(t => HasSubtitleSelectionRoute(t, routes))", capture);
        foreach (var call in new[] { "GetEmbeddedSubtitleTracks(", "HasFfmpegTrackIndices(", "HasContainerTrackIds(", "GetSubtitleUrls(" })
        {
            Assert.Equal(1, Count(snapshot, call));
            Assert.DoesNotContain(call, route);
            Assert.DoesNotContain(call, resolve);
            Assert.DoesNotContain(call, capture);
        }
        var cycle = Between(service, "private void CycleSubtitleBackward()", "private sealed record SubtitleRouteSnapshot(");
        Assert.Equal(1, Count(cycle, "CaptureSubtitleSelectionRoutes("));
        Assert.Contains("ResolveNativeEmbeddedSidFromSnapshot(track, routes)", cycle);
        Assert.DoesNotContain("ResolveNativeEmbeddedSid(track)", cycle);
        var auto = Between(service, "private async Task InitializeLoadedMediaAsync(", "private bool IsLoadedMediaInitializationCurrent(");
        Assert.Equal(1, Count(auto, "CaptureSubtitleSelectionRoutes("));
        Assert.Contains("HasSubtitleSelectionRoute(t, routes)", auto);
    }

    [Fact]
    public void EveryNativeSubtitleAddUsesTheSameOuterGate()
    {
        var service = Read("SiloPlayer", "PlayerService.cs");
        var sync = Read("SiloPlayer", "PlayerService.SubtitleSync.cs");
        Assert.Equal(2, Count(service, ".AddSubtitle("));
        Assert.Equal(1, Count(sync, ".AddSubtitle("));
        var sidecar = Between(service, "private bool SelectSubtitleSidecar(", "private long CurrentSubtitleSidecarGeneration()");
        var live = Between(service, "private void ApplySubtitleTranslationCues(", "private void ApplySubtitleTranslationCompleted(");
        var corrected = Between(sync, "private async Task ReloadCorrectedSubtitleAsync(", "private void SendSubtitleSyncState()");
        foreach (var (method, innerGate) in new[] { (sidecar, "_subtitleTrackStateLock"), (live, "_liveSubtitleLock"), (corrected, "_subtitleSyncGate") })
        {
            var addGate = method.IndexOf("lock (_subtitleAddGate)", StringComparison.Ordinal);
            var inner = method.IndexOf($"lock ({innerGate})", StringComparison.Ordinal);
            var add = method.IndexOf(".AddSubtitle(", StringComparison.Ordinal);
            Assert.True(addGate >= 0 && inner > addGate && add > inner);
        }
        // Corrected-cue workers can be invoked synchronously inside the sync gate.
        var yieldPoint = corrected.IndexOf("await Task.Yield();", StringComparison.Ordinal);
        Assert.True(yieldPoint >= 0 && yieldPoint < corrected.IndexOf("lock (_subtitleAddGate)", StringComparison.Ordinal));
    }

    [Fact]
    public void RefreshAndPresentationShareTheManagersSelectionGate()
    {
        var service = Read("SiloPlayer", "PlayerService.cs");
        var intent = Read("SiloPlayer.Core", "PlaybackManager.TrackIntent.cs");
        Assert.Contains("SubtitleInventorySyncRoot => _trackIntentGate;", intent);
        var refresh = Between(service, "public async Task RefreshSubtitlesAfterAiAsync(", "private void WriteLiveSubtitleFileLocked()");
        var gate = refresh.IndexOf("lock (manager.SubtitleInventorySyncRoot)", StringComparison.Ordinal);
        Assert.True(gate >= 0 && gate < refresh.IndexOf("var previousInventory =", StringComparison.Ordinal));
        Assert.True(gate < refresh.IndexOf("retained.Index =", StringComparison.Ordinal));
        Assert.True(gate < refresh.IndexOf("session.SubtitleUrls =", StringComparison.Ordinal));
        var capture = Between(Read("SiloPlayer", "PlayerService.SiloCast.cs"),
            "public JsonObject CaptureSiloCastState()", "public async Task ApplySiloCastControlAsync(");
        var captureGate = capture.IndexOf("lock (manager.SubtitleInventorySyncRoot)", StringComparison.Ordinal);
        Assert.True(captureGate >= 0 && captureGate < capture.IndexOf("var subtitleInventory =", StringComparison.Ordinal));
        var urls = Between(Read("SiloPlayer.Core", "PlaybackManager.cs"),
            "public List<(SubtitleTrackInfo Track, string FullUrl)> GetSubtitleUrls(", "private static bool RequiresSubtitleAccountAuthentication(");
        Assert.Contains("lock (_trackIntentGate)", urls);
    }

    [Fact]
    public void ManualPickRevalidatesTheSessionAndExactRowBeforeChangingPendingState()
    {
        var selection = Between(Read("SiloPlayer", "PlayerService.cs"),
            "private async Task<bool> TrySetSubtitleTrackAndPersistAsync(", "private bool MatchesSubtitlePreference(");
        var locked = selection[selection.IndexOf("lock (_subtitleTrackStateLock)", StringComparison.Ordinal)..];
        var record = locked.IndexOf("if (!manager.TryRecordLocalSubtitleSelection(selectionSession, track))", StringComparison.Ordinal);
        Assert.True(record >= 0);
        var validate = locked[..record];
        Assert.Contains("!ReferenceEquals(selectionSession, manager?.CurrentSession)", validate);
        Assert.Contains("selectionSession?.SubtitleUrls.Contains(track) != true", validate);
        Assert.Contains("selectedSubtitleIndex = track?.Index ?? -1;", validate);
        var refuse = locked[record..locked.IndexOf("_pendingExplicitInitialSubtitleOwner = null;", StringComparison.Ordinal)];
        Assert.Contains("return false;", refuse);
        foreach (var mutation in new[] { "_pendingExplicitInitialSubtitleOwner = null;", "_pendingAutomaticSubtitleSelection = null;",
            "_activeSubtitleServerIndex = selectedSubtitleIndex;", "_pendingSubtitlePreferenceIndex = selectedSubtitleIndex;",
            "_pendingSubtitlePreferenceOwner = preferenceOwner;" })
            Assert.True(locked.IndexOf(mutation, StringComparison.Ordinal) > record, $"State changed before row validation: {mutation}");
        Assert.DoesNotContain("manager.RecordLocalSubtitleSelection(", selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetiredClickedRowCannotRecordARemovedOrReusedIndex(bool reuseIndex)
    {
        using var http = new HttpClient();
        var api = new SiloApiClient(http);
        using var auth = new AuthService(api, new(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        var retained = new SubtitleTrackInfo { Index = 1, TrackId = "retained" };
        var clicked = new SubtitleTrackInfo { Index = 2, TrackId = "clicked" };
        var session = new PlaybackStartResponse { MediaFileId = 42, ProtocolVersion = 3, SubtitleUrls = [retained, clicked] };
        typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
        manager.RecordLocalSubtitleSelection(retained.Index, isAutomatic: true);
        var intent = manager.CaptureLocalSubtitleIntent();
        lock (manager.SubtitleInventorySyncRoot)
            session.SubtitleUrls = reuseIndex ? [retained, new() { Index = 2, TrackId = "unrelated" }] : [retained];

        Assert.False(manager.TryRecordLocalSubtitleSelection(session, clicked));
        Assert.Equal(intent, manager.CaptureLocalSubtitleIntent());
        Assert.Equal(retained.Index, manager.SubtitleSelectionForLoad);
        Assert.True(manager.IsSubtitleSelectionAutomatic);
    }

    [Fact]
    public async Task UrlEnumerationHoldsTheInventoryGateUntilEveryRowIsRead()
    {
        using var http = new HttpClient();
        var api = new SiloApiClient(http); api.SetBaseUrl("https://inventory.invalid");
        using var auth = new AuthService(api, new(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        using var enumerating = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        IEnumerable<SubtitleTrackInfo> Inventory()
        {
            yield return new() { Index = 1, Url = "/first.srt" };
            enumerating.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Inventory reader was not released.");
            yield return new() { Index = 2, Url = "/second.srt" };
        }
        var read = Task.Run(() => manager.GetSubtitleUrls(Inventory()));
        try
        {
            Assert.True(enumerating.Wait(TimeSpan.FromSeconds(5)));
            var entered = Monitor.TryEnter(manager.SubtitleInventorySyncRoot);
            if (entered) Monitor.Exit(manager.SubtitleInventorySyncRoot);
            Assert.False(entered); // A refresh cannot publish half-way through enumeration.
        }
        finally
        {
            release.Set();
            await read.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(2, (await read).Count);
    }

    [Fact]
    public void AcceptedLegacyDownloadNeedsNoBridgeButAReplacementStillRequiresIdentity()
    {
        using var http = new HttpClient();
        var api = new SiloApiClient(http);
        using var auth = new AuthService(api, new(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        var track = new SubtitleTrackInfo { Index = 2, Source = "downloaded", Language = "eng", Codec = "srt" };
        var session = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [track] };
        typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
        manager.RecordLocalSubtitleSelection(2);
        Assert.Equal(2, manager.SubtitleSelectionForLoad);
        lock (manager.SubtitleInventorySyncRoot) track.Index = 8;
        Assert.Equal(8, manager.SubtitleSelectionForLoad);
        session.SubtitleUrls = [new() { Index = 8, Source = track.Source, Language = track.Language, Codec = track.Codec }];
        Assert.Equal(-1, manager.SubtitleSelectionForLoad);
        Assert.True(manager.IsRetainedSubtitleUnavailable);
        Assert.Same(track, manager.CaptureLocalSubtitleIntent().Track);
    }

    private static string Read(string project, string name)
        => File.ReadAllText(Path.Combine(TestRepository.Root, "src", project, "Services", name));

    private static string Between(string source, string first, string next)
    {
        var start = source.IndexOf(first, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {first}");
        var end = source.IndexOf(next, start + first.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing {next}");
        return source[start..end];
    }

    private static int Count(string source, string text) => source.Split(text, StringSplitOptions.None).Length - 1;
}
