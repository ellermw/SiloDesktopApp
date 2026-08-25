using Xunit;

namespace SiloPlayer.Tests;

public sealed class ChapterThumbnailRealtimeParitySourceTests
{
    [Fact]
    public void PlayerService_PatchesCurrentChapterFromRealtimeEvent()
    {
        var source = File.ReadAllText(SourcePath("src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("case \"chapter_thumbnail_ready\"", source);
        Assert.Contains("ApplyRealtimeChapterThumbnail(ev.Payload)", source);
        Assert.Contains("session.MediaFileId != fileId", source);
        Assert.Contains("chapter.ThumbnailUrl = thumbnailUrl", source);
        Assert.Contains("chapter.ThumbnailThumbhash = thumbnailThumbhash", source);
        Assert.Contains("osc-patch-chapter-thumbnail-url", source);
        Assert.Contains("InvokeSubscribersSafely(ChaptersChanged", source);
    }

    [Fact]
    public void NativeOsc_PatchesOneChapterWithoutDiscardingLoadedPreviews()
    {
        var source = File.ReadAllText(SourcePath("libs", "mpv", "scripts", "silo-osc.lua"))
            .ReplaceLineEndings("\n");

        Assert.Contains("osc-patch-chapter-thumbnail-url", source);
        Assert.Contains("chapter.thumbnail_url = url", source);
        Assert.Contains("state.chapter_thumbnail_requested[chapter_index] = nil", source);
        Assert.DoesNotContain("osc-patch-chapter-thumbnail-url\", function(index, url)\n        state.chapter_thumbnails = {}", source);
    }

    [Fact]
    public void PlayerOverlay_RefreshesOpenChapterMenuWithoutRestartingPlayback()
    {
        var source = File.ReadAllText(SourcePath("src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs"));

        Assert.Contains("_playerService.ChaptersChanged += OnChaptersChanged", source);
        Assert.Contains("_playerService.ChaptersChanged -= OnChaptersChanged", source);
        Assert.Contains("if (ChaptersFlyout.IsOpen)", source);
        Assert.Contains("PopulateChaptersFlyout()", source);
    }

    private static string SourcePath(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }
}
