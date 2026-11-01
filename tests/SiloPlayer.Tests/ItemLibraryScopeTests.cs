using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ItemLibraryScopeTests
{
    private static JsonElement[] Files(params (string Id, object? Library)[] files) => files.Select(file => JsonSerializer.SerializeToElement(new { id = file.Id, library_id = file.Library })).ToArray();
    [Fact]
    public void UnscopedMultipleLibrariesRequireAnActualItemLibraryChoice()
    {
        var scope = new ItemLibraryScope(null, false, true);
        Assert.False(scope.CanSearch);
        scope.ReplaceFiles(Files(("a", "9"), ("b", 2), ("c", "9")));
        Assert.Equal(new[] { 2, 9 }, scope.LibraryIds); Assert.True(scope.NeedsChoice); Assert.False(scope.CanSearch);
        scope.Pick(17); Assert.False(scope.CanSearch);
        scope.Pick(9); Assert.True(scope.CanSearch); Assert.Equal(9, scope.LibraryId);
    }
    [Fact]
    public void SingleLibraryIsInferredWithoutAnArbitraryGlobalLibrary()
    {
        var scope = new ItemLibraryScope(null, false, true); scope.ReplaceFiles(Files(("a", "7"), ("b", 7)));
        Assert.Equal(7, scope.LibraryId); Assert.True(scope.CanSearch);
    }
    [Fact]
    public void AbsentOrUnreadableLibrariesKeepTheServersUnscopedFallback()
    {
        var scope = new ItemLibraryScope(null, false, true); scope.ReplaceFiles(Files(("a", null), ("b", "bad"), ("c", "0")));
        Assert.Empty(scope.LibraryIds); Assert.True(scope.CanSearch); Assert.Null(scope.LibraryId);
        scope.ReplaceFiles([]); Assert.True(scope.CanSearch); Assert.False(scope.NeedsChoice);
    }
    [Fact]
    public void CuratorsWithoutInventoryKeepScopedAndUnscopedMatch()
    {
        var unscoped = new ItemLibraryScope(null, false, false); Assert.True(unscoped.CanSearch); Assert.Null(unscoped.LibraryId);
        var scoped = new ItemLibraryScope(5, false, false); Assert.True(scoped.CanSearch); Assert.Equal(5, scoped.LibraryId);
    }
    [Fact]
    public void ScopedItemLeavingItsLibraryCannotApplyUnscoped()
    {
        var scope = new ItemLibraryScope(2, false, true); scope.ReplaceFiles(Files(("a", 2))); var old = scope.Capture();
        scope.ReplaceFiles(Files(("a", 7))); Assert.False(scope.IsCurrent(old)); Assert.False(scope.CanSearch);
    }
    [Fact]
    public void SplitUsesSelectedFilesThenPageScopeThenChoice()
    {
        var scope = new ItemLibraryScope(2, true, true); scope.ReplaceFiles(Files(("a", 2), ("b", 7), ("c", 7)));
        scope.SelectFiles(["b", "c"]); Assert.Equal(7, scope.LibraryId);
        scope.SelectFiles(["a", "b"]); Assert.Equal(2, scope.LibraryId);
        var unscoped = new ItemLibraryScope(null, true, true); unscoped.ReplaceFiles(Files(("a", 2), ("b", 7)));
        unscoped.SelectFiles(["a", "b"]); Assert.True(unscoped.NeedsChoice); unscoped.Pick(7); Assert.Equal(7, unscoped.LibraryId);
    }
    [Fact]
    public void SelectedFilesWithUnknownMembershipAreNotInferred()
    {
        var scope = new ItemLibraryScope(null, true, true); scope.ReplaceFiles(Files(("a", 2), ("b", 7), ("c", null)));
        scope.SelectFiles(["a", "c"]); Assert.False(scope.CanSearch);
    }
    [Fact]
    public void InventoryOrSelectionChangesRetireResultsEvenWhenEffectiveLibraryIsUnchanged()
    {
        var scope = new ItemLibraryScope(null, true, true); scope.ReplaceFiles(Files(("a", 2), ("b", 2)));
        scope.SelectFiles(["a"]); var search = scope.Capture();
        scope.SelectFiles(["b"]); Assert.False(scope.IsCurrent(search)); var preview = scope.Capture();
        scope.ReplaceFiles(Files(("a", 2), ("b", 2), ("c", 2))); Assert.False(scope.IsCurrent(preview));
        var stable = scope.Capture(); Assert.False(scope.ReplaceFiles(Files(("c", "2"), ("b", "2"), ("a", "2")))); Assert.True(scope.IsCurrent(stable));
    }
    [Fact]
    public void ARemovedLibraryPickNeverFallsBackToAnotherMultiLibraryScope()
    {
        var scope = new ItemLibraryScope(null, false, true); scope.ReplaceFiles(Files(("a", 2), ("b", 7))); scope.Pick(7); var old = scope.Capture();
        scope.ReplaceFiles(Files(("a", 2), ("c", 9))); Assert.True(scope.NeedsChoice); Assert.False(scope.IsCurrent(old));
    }
    [Fact]
    public void SplitWithARetiredPageLibraryCanChooseFromRemainingItemLibraries()
    {
        var scope = new ItemLibraryScope(2, true, true); scope.ReplaceFiles(Files(("a", 7), ("b", 9)));
        scope.SelectFiles(["a", "b"]); Assert.True(scope.NeedsChoice);
        scope.Pick(9); Assert.Equal(9, scope.LibraryId); Assert.True(scope.CanSearch);
    }
    [Fact]
    public void AmbiguousUnmatchedPlansCanValidateInventoryWithoutASearchLibrary()
    {
        var scope = new ItemLibraryScope(null, true, true); scope.ReplaceFiles(Files(("a", 2), ("b", 7), ("c", 2)));
        scope.SelectFiles(["a", "b"]); var plan = scope.Capture();
        Assert.False(scope.IsCurrent(plan)); Assert.True(scope.IsInventoryCurrent(plan));
        scope.ReplaceFiles(Files(("a", 7), ("b", 7), ("c", 2))); Assert.False(scope.IsInventoryCurrent(plan));
    }
    [Fact]
    public void ChoicesRemainAvailableAfterAPickButNeverOverrideAnImposedLibrary()
    {
        var scope = new ItemLibraryScope(null, true, true); scope.ReplaceFiles(Files(("a", 2), ("b", 7), ("c", 2)));
        scope.SelectFiles(["a", "b"]); Assert.True(scope.AllowsChoice); scope.Pick(7); Assert.True(scope.AllowsChoice);
        scope.SelectFiles(["a"]); Assert.False(scope.AllowsChoice); Assert.Equal(2, scope.LibraryId);
        var match = new ItemLibraryScope(2, false, true); match.ReplaceFiles(Files(("a", 2), ("b", 7))); Assert.False(match.AllowsChoice);
        var scopedSplit = new ItemLibraryScope(2, true, true); scopedSplit.ReplaceFiles(Files(("a", 2), ("b", 7))); scopedSplit.SelectFiles(["a", "b"]); Assert.False(scopedSplit.AllowsChoice);
    }
}
