using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitleAdmissionOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedSessionCannotAdmitAReplacementInventoryRowOrOff(bool off)
    {
        WithManager(manager =>
        {
            var requested = Track("requested-a", 7);
            var captured = Session(requested);
            SetSession(manager, captured);
            var previous = manager.CaptureLocalSubtitleIntent();
            // A grant is published after the service resolves its row, but
            // before the manager accepts the gesture. The ordinal is reused.
            SetSession(manager, Session(Track("replacement-b", 7)));
            var accepted = Admit(manager, captured, off ? null : requested);
            var actual = manager.CaptureLocalSubtitleIntent();
            Assert.True(!accepted, $"A retired session admitted {(off ? "Off" : "a different row")}: actual={actual.Track?.TrackId ?? "Off"}.");
            Assert.Equal(previous, actual);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameSessionCannotAdmitARemovedOrUnavailableRow(bool unavailable)
    {
        WithManager(manager =>
        {
            var requested = Track("requested-a", 7);
            var session = Session(requested);
            SetSession(manager, session);
            manager.RecordLocalSubtitleSelection(null, isAutomatic: true);
            var previous = manager.CaptureLocalSubtitleIntent();
            if (unavailable) requested.Unplayable = true;
            else session.SubtitleUrls = [Track("replacement-b", 7)];
            Assert.False(Admit(manager, session, requested));
            Assert.Equal(previous, manager.CaptureLocalSubtitleIntent());
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CurrentExactRowAndOffRetainProvenanceAcrossReindex(bool off, bool automatic)
    {
        WithManager(manager =>
        {
            var requested = Track("requested-a", 7);
            var session = Session(requested);
            SetSession(manager, session);
            requested.Index = 8;
            Assert.True(Admit(manager, session, off ? null : requested, automatic));
            var intent = manager.CaptureLocalSubtitleIntent();
            Assert.True(intent.HasSelection);
            Assert.Same(off ? null : requested, intent.Track);
            Assert.Equal(42, intent.MediaFileId);
            Assert.Equal(automatic, intent.IsAutomatic);
            Assert.Equal(off ? -1 : 8, manager.SubtitleSelectionForLoad);
        });
    }

    private static bool Admit(PlaybackManager manager, PlaybackStartResponse session, SubtitleTrackInfo? track, bool automatic = false)
    {
        var atomic = typeof(PlaybackManager).GetMethod("TryRecordLocalSubtitleSelection");
        if (atomic != null) return (bool)atomic.Invoke(manager, [session, track, automatic])!;
        // Exercise the original production admission on the RED baseline,
        // rather than treating an absent API as the failure itself.
        try { manager.RecordLocalSubtitleSelection(track?.Index, automatic); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private static SubtitleTrackInfo Track(string id, int index) => new()
    { TrackId = id, Index = index, Source = "external", Codec = "subrip", Language = "eng" };
    private static PlaybackStartResponse Session(SubtitleTrackInfo track) => new()
    { MediaFileId = 42, ProtocolVersion = 3, SubtitleUrls = [track] };
    private static void SetSession(PlaybackManager manager, PlaybackStartResponse session)
        => typeof(PlaybackManager).GetProperty(nameof(PlaybackManager.CurrentSession))!.SetValue(manager, session);
    private static void WithManager(Action<PlaybackManager> check)
    {
        using var http = new HttpClient();
        var api = new SiloApiClient(http);
        using var auth = new AuthService(api, new(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        check(manager);
    }
}
