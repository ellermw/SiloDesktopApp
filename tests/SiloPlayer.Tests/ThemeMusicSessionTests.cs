using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class ThemeMusicSessionTests
{
    [Fact]
    public async Task InterruptedPendingGrantCannotStartAndSameOwnerStaysSuppressed()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ThemeAudioGrant>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new Sink(); var grants = 0;
        using var music = new ThemeMusicSession((_, _, _) => { grants++; ready.SetResult(); return release.Task; }, sink);
        var selection = new ThemeSongSet { OwnerId = "series", Items = [new() { Id = "1" }] };
        var loading = music.SelectAsync(selection, true); await ready.Task; music.Interrupt(); music.Interrupt();
        release.SetResult(new() { Url = "https://fixture.invalid/audio", Delivery = "original" }); await loading;
        await music.SelectAsync(selection, true); Assert.Equal(1, grants); Assert.Equal(0, sink.Loads);
    }
    [Fact]
    public async Task ConvertedLoopObtainsFreshGrantAndSameOwnerNavigationResumes()
    {
        var sink = new Sink(); var grants = 0;
        using var music = new ThemeMusicSession((_, _, _) => { grants++; return Task.FromResult(new ThemeAudioGrant { Delivery = "converted" }); }, sink);
        var set = new ThemeSongSet { OwnerId = "series", Items = [new() { Id = "1" }] };
        await music.SelectAsync(set, true); Assert.False(sink.Loop);
        music.Suspend(); await music.SelectAsync(set, true); Assert.Equal(1, grants); Assert.Equal(1, sink.Resumes);
        await music.EndedAsync(); Assert.Equal(2, grants); music.ResetAuthority(); await music.EndedAsync(); Assert.Equal(2, grants);
    }
    private sealed class Sink : IThemeAudioSink
    { public int Loads, Resumes; public bool Loop; public void Load(ThemeAudioGrant grant, bool loop) { Loads++; Loop = loop; }
      public void Pause() { } public void Resume() => Resumes++; public void Stop() { } }
}
