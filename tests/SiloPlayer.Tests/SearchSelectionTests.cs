using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SearchSelectionTests
{
    [Fact]
    public void PointerSelectionFollowsIdentityAndKeyboardContinuesFromIt()
    {
        var state = new SearchSelectionState(); state.Replace(["catalog:a", "person:p"]);
        state.Select("person:p"); Assert.Equal(1, state.Index);
        state.Replace(["request:series:1", "catalog:a", "person:p"]);
        Assert.Equal(2, state.Index); Assert.Equal(1, state.Move(-1));
        state.Select("missing"); Assert.Equal(-1, state.Index);
        state.Select("request:series:1"); state.Replace([]); Assert.Equal(-1, state.Index);
    }
    [Fact]
    public void LateGroupsKeepSelectedIdentityAndAllOptionsAreReachable()
    {
        var state = new SearchSelectionState();
        state.Replace(["catalog:a", "request:series:1"]);
        Assert.Equal(0, state.Move(1)); Assert.Equal(1, state.Move(1));
        state.Replace(["catalog:a", "person:p", "request:series:1"]);
        Assert.Equal(2, state.Index); Assert.Equal(2, state.Move(1));
        Assert.Equal(1, state.Move(-1)); Assert.Equal(0, state.Move(-1));
        Assert.Equal(-1, state.Move(-1)); Assert.Equal(-1, state.Move(-1));
        Assert.Equal(0, state.Move(1));
    }

    [Fact]
    public void RemovedSelectionAndEmptyGroupsCannotPointAtAnOldResult()
    {
        var state = new SearchSelectionState(); state.Replace(["catalog:a"]); state.Move(1);
        state.Replace(["person:p"]); Assert.Equal(-1, state.Index);
        Assert.Equal(-1, state.Move(-1)); Assert.Equal(0, state.Move(1)); state.Replace([]); Assert.Equal(-1, state.Move(1));
    }
}
