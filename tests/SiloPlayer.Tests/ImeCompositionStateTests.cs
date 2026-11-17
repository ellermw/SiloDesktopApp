using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Official #4287 (9eb3df6f3): IME composition keys never trigger app actions.</summary>
public sealed class ImeCompositionStateTests
{
    private const int Back = 8, Enter = 13, Escape = 27, Up = 38, Down = 40, Comma = 188, A = 65;

    [Fact]
    public void KeysDuringCompositionBelongToTheIme()
    {
        var state = new ImeCompositionState(() => 0);
        Assert.False(state.ShouldIgnoreKey(Enter));
        state.Start();
        Assert.True(state.IsComposing);
        foreach (var key in new[] { Enter, Down, Up, Escape, Back, Comma, A })
            Assert.True(state.ShouldIgnoreKey(key));
        // Still composing: a run of candidate keys never leaks through.
        Assert.True(state.ShouldIgnoreKey(Enter));
    }

    [Fact]
    public void ConfirmingEnterAfterCompositionEndIsIgnoredAndNextEnterActsOnce()
    {
        long now = 1_000;
        var state = new ImeCompositionState(() => now);
        state.Start();
        now += 40;
        state.End();
        Assert.False(state.IsComposing);
        now += 5;
        Assert.True(state.ShouldIgnoreKey(Enter)); // the key that confirmed the conversion
        now += 400;
        var acted = 0;
        if (!state.ShouldIgnoreKey(Enter)) acted++;
        Assert.Equal(1, acted);
        if (!state.ShouldIgnoreKey(Enter)) acted++;
        Assert.Equal(2, acted); // every later Enter acts normally
    }

    [Fact]
    public void SwallowedConfirmKeyDoesNotEatTheNextOrdinaryEnter()
    {
        long now = 0;
        var state = new ImeCompositionState(() => now);
        state.Start(); state.End();
        // The IME consumed the confirming Enter, so no key reached the handler.
        now += (long)ImeCompositionState.ConfirmWindow.TotalMilliseconds + 1;
        Assert.False(state.ShouldIgnoreKey(Enter));
    }

    [Fact]
    public void AnyKeyAfterTheEndConsumesTheConfirmAllowance()
    {
        var state = new ImeCompositionState(() => 0);
        state.Start(); state.End();
        Assert.False(state.ShouldIgnoreKey(A)); // ordinary typing resumed
        Assert.False(state.ShouldIgnoreKey(Enter));
    }

    [Fact]
    public void EscapeAndArrowsThatCancelOrNavigateTheCandidateAreIgnoredOnce()
    {
        foreach (var key in new[] { Escape, Down, Up, Back })
        {
            var state = new ImeCompositionState(() => 0);
            state.Start(); state.End();
            Assert.True(state.ShouldIgnoreKey(key));
            Assert.False(state.ShouldIgnoreKey(key));
        }
    }

    [Fact]
    public void ProcessKeyIsIgnoredWithoutAConsumedAllowance()
    {
        var state = new ImeCompositionState(() => 0);
        Assert.True(state.ShouldIgnoreKey(ImeCompositionState.ProcessKey));
        Assert.True(state.ShouldIgnoreKey(Enter, ImeCompositionState.ProcessKey));
        Assert.False(state.ShouldIgnoreKey(Enter));
        state.Start(); state.End();
        Assert.True(state.ShouldIgnoreKey(ImeCompositionState.ProcessKey));
        Assert.True(state.ShouldIgnoreKey(Enter)); // the confirm allowance survives an IME-owned key
        Assert.False(state.ShouldIgnoreKey(Enter));
    }

    [Fact]
    public void ResetAndRestartNeverLeaveEnterBlocked()
    {
        var state = new ImeCompositionState(() => 0);
        state.Start();
        state.Reset();
        Assert.False(state.IsComposing);
        Assert.False(state.ShouldIgnoreKey(Enter));
        state.Start(); state.End(); state.Start();
        Assert.True(state.ShouldIgnoreKey(Enter)); // a new composition is active again
        state.Reset();
        Assert.False(state.ShouldIgnoreKey(Enter));
    }
}
