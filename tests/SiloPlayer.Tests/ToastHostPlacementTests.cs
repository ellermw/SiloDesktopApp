using SiloPlayer.Services;

namespace SiloPlayer.Tests;

public sealed class ToastHostPlacementTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void OverlayRequiresCoveringVisiblePlaybackAndLiveToasts(bool covers, bool visible, bool toasts, bool expected)
    {
        var moves = new List<bool>();
        var placement = new ToastHostPlacement<object>(new(), (_, overlay) => moves.Add(overlay));
        placement.Update(covers, visible, toasts);
        Assert.Equal(expected, placement.IsPlaybackOverlay);
        Assert.Equal(expected ? new[] { true } : Array.Empty<bool>(), moves);
    }

    [Fact]
    public void ExpandedAndFullscreenUpdatesDoNotDuplicateTheHost()
    {
        var content = new object();
        var shell = new HashSet<object> { content };
        var playback = new HashSet<object>();
        var moves = new List<bool>();
        var placement = new ToastHostPlacement<object>(content, (host, overlay) =>
        {
            Assert.True((overlay ? shell : playback).Remove(host));
            Assert.True((overlay ? playback : shell).Add(host));
            Assert.Equal(1, shell.Count + playback.Count);
            moves.Add(overlay);
        });
        placement.Update(true, true, true); // Expanded.
        placement.Update(true, true, true); // Fullscreen entry with an active toast.
        placement.Update(true, true, true); // Fullscreen exit, still expanded.
        placement.Update(false, true, true); // Mini-bar/PiP uncovers shell.
        placement.Update(true, true, true); // Expand again.
        placement.Update(true, false, true); // Hidden/stopped/destroyed video.
        placement.Update(false, false, true);
        Assert.Equal(new[] { true, false, true, false }, moves);
        Assert.Same(content, Assert.Single(shell));
        Assert.Empty(playback);
    }

    [Fact]
    public void NewToastOpensOverlayAndLastExpiryReturnsHostToShell()
    {
        var moves = new List<bool>();
        var placement = new ToastHostPlacement<object>(new(), (_, overlay) => moves.Add(overlay));
        placement.Update(true, true, false);
        placement.Update(true, true, true);
        placement.Update(true, true, true); // A second toast shares the host.
        placement.Update(true, true, false);
        placement.Update(true, true, false);
        Assert.Equal(new[] { true, false }, moves);
    }

    [Fact]
    public async Task MovingTheHostPreservesTimerAndActionState()
    {
        // The production router knows only the host reference. Its payload
        // (the control's actual timers/buttons in WinUI) is never reconstructed.
        var timer = new object();
        var calls = 0;
        Func<Task> undo = () => { calls++; return Task.CompletedTask; };
        var content = new Payload(timer, 1234, undo);
        var placement = new ToastHostPlacement<Payload>(content, (host, _) => Assert.Same(content, host));
        placement.Update(true, true, true);
        content.Remaining = 600;
        content.ActionPending = true;
        placement.Update(false, true, true);
        placement.Update(true, true, true);
        Assert.Same(timer, placement.Content.Timer);
        Assert.Equal(600, placement.Content.Remaining);
        Assert.True(placement.Content.ActionPending);
        Assert.Same(undo, placement.Content.Undo);
        await placement.Content.Undo();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FailedMoveDoesNotAdvanceSelectionAndCanBeRetried()
    {
        var fail = true;
        var placement = new ToastHostPlacement<object>(new(), (_, _) =>
        {
            if (fail) throw new InvalidOperationException("Synthetic attach failure");
        });
        Assert.Throws<InvalidOperationException>(() => placement.Update(true, true, true));
        Assert.False(placement.IsPlaybackOverlay);
        fail = false;
        placement.Update(true, true, true);
        Assert.True(placement.IsPlaybackOverlay);
    }

    private sealed class Payload(object timer, int remaining, Func<Task> undo)
    {
        internal object Timer { get; } = timer;
        internal int Remaining { get; set; } = remaining;
        internal Func<Task> Undo { get; } = undo;
        internal bool ActionPending { get; set; }
    }
}
