using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Tests;

public sealed class PlaybackMenuRequestTests
{
    private const string Owner = "11111111111111111111111111111111";
    private const string Replacement = "22222222222222222222222222222222";

    private static string[] Message(string sequence = "1", string x = "160", string width = "80", string surface = "1920")
        => ["silo-native-audio-open", Owner, sequence, x, "900", width, "40", surface, "1080"];

    [Fact]
    public void ParsesFiniteOriginalOpenerIntoNormalizedCoordinates()
    {
        Assert.True(PlaybackMenuRequest.TryParse(Message(), out var request));
        Assert.Equal(Owner, request!.OwnerToken);
        Assert.Equal(1, request.Sequence);
        Assert.Equal(160d / 1920, request.Anchor.Left);
        Assert.Equal(240d / 1920, request.Anchor.Right);
        Assert.Equal(900d / 1080, request.Anchor.Top);
    }

    [Theory]
    [InlineData("0", "160", "80", "1920")]
    [InlineData("-1", "160", "80", "1920")]
    [InlineData("1.5", "160", "80", "1920")]
    [InlineData("1", "NaN", "80", "1920")]
    [InlineData("1", "Infinity", "80", "1920")]
    [InlineData("1", "-1", "80", "1920")]
    [InlineData("1", "160", "0", "1920")]
    [InlineData("1", "160", "-1", "1920")]
    [InlineData("1", "160", "3000", "1920")]
    [InlineData("1", "160", "80", "0")]
    [InlineData("1", "160", "80", "1000000")]
    public void RejectsInvalidSequenceOrGeometry(string sequence, string x, string width, string surface)
        => Assert.False(PlaybackMenuRequest.TryParse(Message(sequence, x, width, surface), out _));

    [Fact]
    public void RejectsMalformedScopeOrWrongShape()
    {
        Assert.False(PlaybackMenuRequest.TryParse([], out _));
        var message = Message(); message[1] = "not-a-scope";
        Assert.False(PlaybackMenuRequest.TryParse(message, out _));
        message = Message(); message[0] = "other-intent";
        Assert.False(PlaybackMenuRequest.TryParse(message, out _));
    }

    [Fact]
    public void DifferentKindsShareOneMonotonicPresentationGate()
    {
        var gate = new PlaybackMenuRequestGate(); gate.ResetOwner(Owner);
        Assert.True(PlaybackMenuRequest.TryParse(Message(), out var audio));
        var message = Message("2"); message[0] = "silo-native-quality-open";
        Assert.True(PlaybackMenuRequest.TryParse(message, out var quality));
        Assert.Equal("audio", audio!.Kind);
        Assert.Equal("quality", quality!.Kind);
        Assert.True(gate.TryOpen(audio)); Assert.True(gate.TryOpen(quality));
        Assert.False(gate.TryClose(Owner, audio.Sequence));
        Assert.True(gate.IsCurrent(quality));
        Assert.False(gate.TryOpen(audio));
    }

    [Fact]
    public void OldCloseCannotRetireANewerPresentation()
    {
        var gate = new PlaybackMenuRequestGate(); gate.ResetOwner(Owner);
        Assert.True(PlaybackMenuRequest.TryParse(Message(), out var first));
        Assert.True(PlaybackMenuRequest.TryParse(Message("2"), out var second));
        Assert.True(gate.TryOpen(first!));
        Assert.True(gate.TryOpen(second!));
        Assert.False(gate.TryClose(Owner, 1));
        Assert.True(gate.IsCurrent(second!));
        Assert.True(gate.TryClose(Owner, 2));
        Assert.False(gate.IsCurrent(second!));
    }

    [Fact]
    public void OwnerReplacementRejectsQueuedOldOpenAndClose()
    {
        var gate = new PlaybackMenuRequestGate(); gate.ResetOwner(Owner);
        Assert.True(PlaybackMenuRequest.TryParse(Message(), out var old));
        Assert.True(gate.TryOpen(old!));
        gate.ResetOwner(Replacement);
        Assert.False(gate.IsCurrent(old!));
        Assert.False(gate.TryOpen(old!));
        Assert.False(gate.TryClose(Owner, 1));
    }

    [Fact]
    public void ReplayedSequenceCannotReopenAClosedMenu()
    {
        var gate = new PlaybackMenuRequestGate(); gate.ResetOwner(Owner);
        Assert.True(PlaybackMenuRequest.TryParse(Message(), out var request));
        Assert.True(gate.TryOpen(request!));
        Assert.True(gate.TryClose(Owner, 1));
        Assert.False(gate.TryOpen(request!));
        gate.ResetOwner(Owner);
        Assert.False(gate.TryOpen(request!)); // Republishing the same inventory is not a new owner.
    }
}
