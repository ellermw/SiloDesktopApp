using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class LibraryLetterNavigationTests
{
    [Fact]
    public async Task JumpUsesWholeQueryServerIndexWithoutSamplingTitles()
    {
        using var wire = new Wire(); var model = wire.Create();
        var jumped = -1; model.LetterJumpRequested += position => jumped = position;
        await model.LoadCommand.ExecuteAsync(null);
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(1, wire.IndexRequests);
        Assert.Equal(0, wire.ProbeRequests);
        Assert.NotNull(model.GetWindowItem(742));
        Assert.Contains(wire.Seeks, seek => seek >= 720);
        Assert.Equal(742, jumped);
        Assert.Equal(1000, model.TotalCount);
    }

    [Fact]
    public async Task JumpPreservesDescendingSortAndAdvancedFilters()
    {
        using var wire = new Wire(); var model = wire.Create();
        model.SelectedOrder = "desc"; model.UseAdvancedRules = true; model.AdvancedRulesMatch = "any";
        model.AdvancedGroups.Add(new EditableQueryGroup { Match = "all", Rules = { new QueryRule { Field = "genre", Op = "is", Value = "Drama" } } });
        await model.LoadCommand.ExecuteAsync(null);
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal("desc", model.SelectedOrder);
        using var body = JsonDocument.Parse(wire.IndexBody!);
        Assert.Equal("desc", body.RootElement.GetProperty("order").GetString());
        Assert.Equal("any", body.RootElement.GetProperty("match").GetString());
        Assert.Equal("Drama", body.RootElement.GetProperty("groups")[0].GetProperty("rules")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task OldServerDoesNotProbeOrGuessAJump()
    {
        using var wire = new Wire { Supported = false }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); var count = wire.Seeks.Count;
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(0, wire.IndexRequests); Assert.Equal(0, wire.ProbeRequests);
        Assert.Equal(count, wire.Seeks.Count);
        Assert.Null(model.ErrorMessage);
        Assert.False(model.IsLetterIndexLoading);
    }

    [Theory]
    [InlineData("loaded")]
    [InlineData("failed")]
    [InlineData("empty")]
    public async Task LetterIndexLoadingEndsWhenOptionalIndexSettles(string outcome)
    {
        using var wire = new Wire { HoldIndex = true, FailIndex = outcome == "failed",
            IndexTotal = outcome == "empty" ? 0 : 1000 };
        var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        try
        {
            await wire.IndexStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await load;
            Assert.True(model.IsLetterIndexLoading);
            Assert.Null(model.LetterIndex);
            Assert.NotNull(model.GetWindowItem(0));
        }
        finally { wire.ReleaseIndex.TrySetResult(); await model.EnsureLetterIndexAsync(); }
        Assert.False(model.IsLetterIndexLoading);
        if (outcome == "failed") Assert.Null(model.LetterIndex);
        else Assert.Equal(outcome == "empty" ? 0 : 1000, model.LetterIndex!.Total);
    }

    [Fact]
    public async Task RetiredIndexCompletionCannotReleaseANewerLoadingReservation()
    {
        using var wire = new Wire { HoldIndex = true };
        var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        await wire.IndexStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var retired = model.EnsureLetterIndexAsync();
        var retiredRelease = wire.ReleaseIndex;
        model.SuspendCatalogLoads();
        Assert.False(model.IsLetterIndexLoading);
        wire.ReleaseIndex = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.Client.SetProfile("new-synthetic-profile");
        await model.LoadCommand.ExecuteAsync(null);
        var current = model.EnsureLetterIndexAsync();
        try
        {
            Assert.True(model.IsLetterIndexLoading);
            retiredRelease.TrySetResult();
            await retired;
            Assert.True(model.IsLetterIndexLoading);
            Assert.Null(model.LetterIndex);
        }
        finally { wire.ReleaseIndex.TrySetResult(); await current; }
        Assert.False(model.IsLetterIndexLoading);
        Assert.NotNull(model.LetterIndex);
    }

    [Theory]
    [InlineData("added_at")]
    [InlineData("year")]
    public async Task NonTitleSortDoesNotChangeSortOrReadIndex(string sort)
    {
        using var wire = new Wire(); var model = wire.Create(); model.SelectedSort = sort;
        await model.LoadCommand.ExecuteAsync(null);
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(sort, model.SelectedSort); Assert.Equal(0, wire.IndexRequests);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("language")]
    [InlineData("filter")]
    [InlineData("library")]
    [InlineData("navigation")]
    public async Task LateIndexCannotPublishAfterOwnerChanges(string change)
    {
        using var wire = new Wire { HoldIndex = true }; var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        await wire.IndexStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var indexRead = model.EnsureLetterIndexAsync();
        if (change == "profile") wire.Client.SetProfile("new-synthetic-profile");
        else if (change == "language") wire.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        else if (change == "filter") model.SelectedGenre = "Comedy";
        else if (change == "library") model.Library = new Library { Id = 8, Type = "movie" };
        else model.SuspendCatalogLoads();
        wire.ReleaseIndex.TrySetResult();
        await Task.WhenAll(load, indexRead);
        Assert.Null(model.LetterIndex);
    }

    [Fact]
    public async Task FirstCardsDoNotWaitForOptionalLetterIndex()
    {
        using var wire = new Wire { HoldIndex = true }; var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        try
        {
            await wire.IndexStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await load.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.NotNull(model.GetWindowItem(0));
        }
        finally { wire.ReleaseIndex.TrySetResult(); await model.EnsureLetterIndexAsync(); }
    }

    [Fact]
    public async Task LaterCardsCanExtendPastAnOlderIndexTotal()
    {
        using var wire = new Wire { CatalogTotal = 1200, CatalogExact = false }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.LoadWindowAsync(960, 100, force: true);
        Assert.Equal(1060, model.TotalCount);
        Assert.Equal(1060, model.Items.Count);
        Assert.Equal(0, model.DisplayTotalCount);
        Assert.NotNull(model.GetWindowItem(1050));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1200)]
    public async Task TerminalPageRetainsIndexedAndObservedExtentsWithoutAnExactCount(int indexTotal)
    {
        using var wire = new Wire { CatalogExact = false, IndexTotal = indexTotal }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.LoadWindowAsync(800, 100, force: true);
        Assert.Equal(indexTotal, model.TotalCount);
        Assert.Equal(0, model.DisplayTotalCount);
        await model.LoadWindowAsync(900, 100, force: true);
        Assert.False(model.HasMore);
        Assert.Equal(Math.Max(indexTotal, 1000), model.TotalCount);
        Assert.Equal(Math.Max(indexTotal, 1000), model.Items.Count);
        Assert.NotNull(model.GetWindowItem(999));
        // Neither a terminal page nor an earlier window is an exact count.
        // Trim speculative runway, but not the index or actual returned cards.
        await model.LoadWindowAsync(0, 100, force: true);
        Assert.Equal(Math.Max(indexTotal, 1000), model.TotalCount);
        Assert.Equal(0, model.DisplayTotalCount);
    }

    [Fact]
    public async Task LateLargerIndexKeepsTailReachableAfterAnObservedCatalogEnd()
    {
        using var wire = new Wire { CatalogExact = false, HoldIndex = true, IndexTotal = 1200, TailPosition = 1199 }; var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        try
        {
            await load;
            // Grow the unindexed runway until the final page can be requested.
            await model.LoadWindowAsync(360, 100, force: true);
            await model.LoadWindowAsync(720, 100, force: true);
            await model.LoadWindowAsync(900, 100, force: true);
            Assert.Equal(1000, model.TotalCount);
            // A scan adds titles after the terminal page but before the index.
            wire.CatalogTotal = 1200;
        }
        finally { wire.ReleaseIndex.TrySetResult(); await model.EnsureLetterIndexAsync(); }
        Assert.Equal(1200, model.LetterIndex!.Total);
        Assert.Equal(1200, model.TotalCount);
        Assert.Equal(1200, model.Items.Count);
        Assert.Equal(0, model.DisplayTotalCount);
        var tail = Assert.Single(model.LetterIndex.Letters, entry => entry.Letter == "N");
        Assert.True(tail.Count > 0 && tail.Position < model.TotalCount); // Rail's enabled predicate.
        var jumped = -1; model.LetterJumpRequested += position => jumped = position;
        await model.JumpToLetterCommand.ExecuteAsync("N");
        Assert.Equal(1199, jumped);
        Assert.NotNull(model.GetWindowItem(1199));
    }

    [Theory]
    [InlineData(1000)] // The aligned target window is empty.
    [InlineData(1190)] // The window has cards, but not the selected target.
    public async Task MissingLetterTargetRestoresActiveAndCachedCards(int catalogTotal)
    {
        using var wire = new Wire { CatalogExact = false, IndexTotal = 1200, TailPosition = 1199,
            CatalogTotal = catalogTotal };
        var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.LoadWindowAsync(120, 100);
        var original = model.GetWindowItem(120);
        var cached = model.GetWindowItem(0);
        var start = model.WindowStartIndex;
        var end = model.WindowEndIndex;
        Assert.NotNull(original); Assert.NotNull(cached);
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;

        await model.JumpToLetterCommand.ExecuteAsync("N");

        Assert.Empty(jumped);
        Assert.Equal(start, model.WindowStartIndex);
        Assert.Equal(end, model.WindowEndIndex);
        Assert.Same(original, model.GetWindowItem(120));
        Assert.Same(cached, model.GetWindowItem(0));
        Assert.True(model.HasMore);
        Assert.Equal(1200, model.TotalCount);
        Assert.Equal(0, model.DisplayTotalCount);
        Assert.False(model.IsLoading);
        Assert.Null(model.ErrorMessage);
        // A failed optional jump must leave ordinary navigation usable.
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(new[] { 742 }, jumped);
        Assert.NotNull(model.GetWindowItem(742));
    }

    [Fact]
    public async Task LateSmallerIndexRetainsPreviouslyKnownCatalogTotal()
    {
        using var wire = new Wire { HoldIndex = true, IndexTotal = 800 }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1000, model.TotalCount);
        wire.ReleaseIndex.TrySetResult(); await model.EnsureLetterIndexAsync();
        Assert.Equal(800, model.LetterIndex!.Total);
        Assert.Equal(1000, model.TotalCount);
    }

    [Fact]
    public async Task LateExactCatalogCountReplacesLargerIndexTotal()
    {
        using var wire = new Wire { HoldCatalog = true, IndexTotal = 1200, CatalogTotal = 800 }; var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        await model.EnsureLetterIndexAsync(); Assert.Equal(1200, model.LetterIndex!.Total);
        wire.ReleaseCatalog.TrySetResult(); await load;
        Assert.Equal(800, model.TotalCount);
        Assert.Equal(800, model.DisplayTotalCount);
        Assert.Equal(800, model.Items.Count);
        await model.LoadWindowAsync(799, 1, force: true);
        Assert.NotNull(model.GetWindowItem(799));
        Assert.All(wire.Seeks, seek => Assert.InRange(seek, 0, 799));
    }

    [Fact]
    public async Task LateLargerIndexCannotOverrideExactCatalogTotal()
    {
        using var wire = new Wire { HoldIndex = true, IndexTotal = 1200, CatalogTotal = 800 }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Equal(800, model.TotalCount);
        wire.ReleaseIndex.TrySetResult(); await model.EnsureLetterIndexAsync();
        Assert.Equal(1200, model.LetterIndex!.Total);
        Assert.Equal(800, model.TotalCount);
        Assert.Equal(800, model.Items.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TailLetterOutsideExactExtentIsRefusedInsteadOfClamped(bool indexArrivesLast)
    {
        using var wire = new Wire { HoldIndex = indexArrivesLast, HoldCatalog = !indexArrivesLast,
            IndexTotal = 1200, TailPosition = 1040, CatalogTotal = 800 };
        var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        if (indexArrivesLast)
        {
            await load;
            wire.ReleaseIndex.TrySetResult();
        }
        else
        {
            await model.EnsureLetterIndexAsync();
            wire.ReleaseCatalog.TrySetResult();
            await load;
        }
        await model.EnsureLetterIndexAsync();
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        var requests = wire.Seeks.Count;
        await model.JumpToLetterCommand.ExecuteAsync("N");
        Assert.Empty(jumped);
        Assert.Equal(requests, wire.Seeks.Count);
        Assert.Equal(800, model.TotalCount);
        Assert.Equal("Couldn't load N titles.", model.LetterJumpError);
        Assert.Null(model.PendingLetter);
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(new[] { 742 }, jumped);
        Assert.NotNull(model.GetWindowItem(742));
    }

    [Fact]
    public async Task CountShrinkingDuringLetterLoadDoesNotPublishAFalseJump()
    {
        using var wire = new Wire { CatalogTotal = 1200, IndexTotal = 1200, TailPosition = 1040 }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumps = 0; model.LetterJumpRequested += _ => jumps++;
        wire.HoldCatalog = true;
        wire.WindowTotalExact = true; // Explicit newer count, rather than a skip-total estimate.
        var pending = model.JumpToLetterCommand.ExecuteAsync("N");
        wire.CatalogTotal = 800;
        wire.ReleaseCatalog.TrySetResult();
        await pending;
        Assert.Equal(800, model.TotalCount);
        Assert.Equal(0, jumps);
        Assert.Null(model.GetWindowItem(1040));
    }

    [Fact]
    public async Task ExactCatalogTotalCanShrinkAfterAnEarlierExactCount()
    {
        using var wire = new Wire { CatalogTotal = 1200, IndexTotal = 1200 }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        Assert.Equal(1200, model.TotalCount);
        wire.CatalogTotal = 800;
        wire.WindowTotalExact = true; // A later response explicitly supplies a new exact count.
        await model.LoadWindowAsync(720, 80, force: true);
        Assert.Equal(800, model.TotalCount);
        Assert.Equal(800, model.DisplayTotalCount);
        Assert.Equal(800, model.Items.Count);
    }

    [Fact]
    public async Task ProvisionalIndexDoesNotSuppressAuthoritativeCountRequest()
    {
        using var wire = new Wire { CatalogExact = false, ExactCountTotal = 800, IndexTotal = 1200 }; var model = wire.Create();
        var counted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.WindowLoaded += () => { if (model.TotalCount == 800) counted.TrySetResult(); };
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        Assert.Equal(1200, model.TotalCount);
        wire.ReleaseExactCount.TrySetResult();
        await counted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(800, model.DisplayTotalCount);
        Assert.Equal(800, model.Items.Count);
    }

    [Theory]
    [InlineData(900, 800, false)] // Empty page beyond the newly counted end.
    [InlineData(900, 1000, false)] // Older non-empty page beyond that end.
    [InlineData(720, 1000, false)] // Older page overlapping the newly counted end.
    [InlineData(720, 1000, true)] // Its exact total must not enlarge the newer count either.
    [InlineData(900, 800, true)] // An exact-total empty page is still an older window.
    [InlineData(900, 1000, true)] // An older exact total cannot restore phantom slots.
    [InlineData(800, 1000, true)] // The first index beyond the exact extent is also retired.
    public async Task InFlightSkipTotalWindowCannotEnlargeALaterExactCount(int start, int catalogTotal, bool windowTotalExact)
    {
        using var wire = new Wire { CatalogExact = false, ExactCountTotal = 800, IndexTotal = 1200, TailPosition = 1040 };
        var model = wire.Create();
        var counted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.WindowLoaded += () => { if (model.DisplayTotalCount == 800) counted.TrySetResult(); };
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        Assert.Equal(1200, model.TotalCount);
        var original = model.GetWindowItem(0);
        Assert.NotNull(original);
        wire.WindowTotalExact = windowTotalExact;
        wire.HoldCatalog = true;
        var pending = model.LoadWindowAsync(start, 100, force: true);
        try
        {
            Assert.Contains(start, wire.Seeks);
            Assert.False(pending.IsCompleted);
            wire.CatalogTotal = catalogTotal;
            wire.ReleaseExactCount.TrySetResult();
            await counted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(800, model.TotalCount);
        }
        finally { wire.ReleaseCatalog.TrySetResult(); await pending; }

        Assert.Equal(800, model.TotalCount);
        Assert.Equal(800, model.DisplayTotalCount);
        Assert.Equal(800, model.Items.Count);
        if (start >= 800)
        {
            Assert.Same(original, model.GetWindowItem(0));
            Assert.Equal(0, model.WindowStartIndex);
            Assert.Null(model.GetWindowItem(start));
        }
        else
        {
            Assert.NotNull(model.GetWindowItem(799));
            Assert.Equal(799, model.WindowEndIndex);
            Assert.Null(model.GetWindowItem(800));
            Assert.Null(model.GetWindowItem(819));
            Assert.False(model.HasMore);
        }
        Assert.False(model.IsLoading);
        Assert.Null(model.ErrorMessage);
        var tail = Assert.Single(model.LetterIndex!.Letters, entry => entry.Letter == "N");
        Assert.True(tail.Position >= model.TotalCount); // Rail's disabled predicate.
        var requests = wire.Seeks.Count;
        await model.JumpToLetterCommand.ExecuteAsync("N");
        Assert.Equal(requests, wire.Seeks.Count);
    }

    [Theory]
    [InlineData(120, false)] // The old active viewport would cancel the seek.
    [InlineData(300, false)] // An uncached viewport would replace the seek.
    [InlineData(120, true)]
    [InlineData(300, true)]
    public async Task ViewportLoadsCannotCancelPendingLetterJump(int viewportStart, bool missingTarget)
    {
        using var wire = new Wire { CatalogExact = false, IndexTotal = 1200, TailPosition = 1199 };
        var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.LoadWindowAsync(120, 100);
        var original = model.GetWindowItem(120);
        var cached = model.GetWindowItem(0);
        var originalStart = model.WindowStartIndex;
        Assert.NotNull(original); Assert.NotNull(cached);
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        wire.HoldCatalog = true;
        var pending = model.JumpToLetterCommand.ExecuteAsync(missingTarget ? "N" : "M");
        try
        {
            Assert.True(model.IsLetterJumpInProgress);
            var requests = wire.Seeks.Count;
            await model.EnsureRangeLoadedAsync(viewportStart, viewportStart + 59)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(requests, wire.Seeks.Count);
            Assert.False(pending.IsCompleted);
            Assert.True(model.IsLetterJumpInProgress);
        }
        finally { wire.ReleaseCatalog.TrySetResult(); await pending; }

        Assert.False(model.IsLetterJumpInProgress);
        Assert.False(model.IsLoading);
        Assert.Null(model.ErrorMessage);
        if (missingTarget)
        {
            Assert.Empty(jumped);
            Assert.Equal(originalStart, model.WindowStartIndex);
            Assert.Same(original, model.GetWindowItem(120));
            Assert.Same(cached, model.GetWindowItem(0));
        }
        else
        {
            Assert.Equal(new[] { 742 }, jumped);
            Assert.NotNull(model.GetWindowItem(742));
        }
        // The viewport hold ends on both success and a missing target.
        await model.EnsureRangeLoadedAsync(300, 359);
        Assert.NotNull(model.GetWindowItem(300));
    }

    [Fact]
    public void PageDefersViewportRequestsBeforeRecordingTheirRangeDuringLetterJump()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "LibraryPage.xaml.cs"));
        var start = source.IndexOf("private async Task EnsureVisibleRangeLoadedAsync", StringComparison.Ordinal);
        var end = source.IndexOf("var range = GetVisibleItemRange", start, StringComparison.Ordinal);
        var guard = source[start..end];
        Assert.Contains("if (ViewModel.IsLetterJumpInProgress)", guard);
        Assert.Contains("ScheduleVisibleRangeLoad(force);", guard);
    }

    [Fact]
    public void PageRetiresLettersOnNewScrollInputAndForcesTheRetiredViewportToLoad()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "LibraryPage.xaml.cs"));
        foreach (var input in new[] { "PointerWheelChanged", "PointerPressed", "KeyDown" })
        {
            Assert.Contains($"LibraryScroller.AddHandler(UIElement.{input}Event", source);
            Assert.Contains($"LibraryScroller_{input}), true)", source);
        }
        Assert.Contains("LibraryScroller.DirectManipulationStarted += LibraryScroller_DirectManipulationStarted", source);
        var start = source.IndexOf("private void LibraryScroller_ViewChanged", StringComparison.Ordinal);
        var end = source.IndexOf("private void LibraryScroller_SizeChanged", start, StringComparison.Ordinal);
        Assert.DoesNotContain("RetireLetterJump", source[start..end]);
        start = source.IndexOf("private void RetireLetterJumpForManualScroll", StringComparison.Ordinal);
        end = source.IndexOf("private void LibraryScroller_PointerWheelChanged", start, StringComparison.Ordinal);
        Assert.Contains("ViewModel.RetireLetterJump();", source[start..end]);
        Assert.Contains("_lastRequestedStartIndex = _lastRequestedEndIndex = -1;", source[start..end]);
        Assert.Contains("ScheduleVisibleRangeLoad(force: true);", source[start..end]);
        start = source.IndexOf("private void ScrollToTop_Click", StringComparison.Ordinal);
        end = source.IndexOf("// ===== Overlay Header", start, StringComparison.Ordinal);
        Assert.True(source.IndexOf("RetireLetterJumpForManualScroll();", start, StringComparison.Ordinal) <
            source.IndexOf("SetLibraryVerticalOffset(0", start, StringComparison.Ordinal));
        Assert.Contains("EnsureVisibleRangeLoadedAsync(force: true)", source[start..end]);
        start = source.IndexOf("private async Task EnsureVisibleRangeLoadedAsync", StringComparison.Ordinal);
        end = source.IndexOf("var range = GetVisibleItemRange", start, StringComparison.Ordinal);
        Assert.DoesNotContain("if (ViewModel.LetterJumpError is not null)", source[start..end]);
        Assert.Contains("if (ViewModel.IsLetterJumpViewportBlocked)", source[start..end]);
        Assert.Contains("_forceVisibleRangeLoad |= force;", source[start..end]);
    }

    [Fact]
    public async Task ANewerClickRetiresAnOlderPendingJump()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        wire.HoldCatalog = true;
        var oldJump = model.JumpToLetterCommand.ExecuteAsync("M");
        wire.HoldCatalog = false;
        await model.JumpToLetterCommand.ExecuteAsync("A");
        wire.ReleaseCatalog.TrySetResult(); await oldJump;
        Assert.Equal(new[] { 0 }, jumped);
    }

    [Fact]
    public async Task MissingOlderTargetCannotRestoreCardsOverANewerJump()
    {
        using var wire = new Wire { CatalogExact = false, IndexTotal = 1200, TailPosition = 1199 };
        var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        wire.HoldCatalog = true;
        var older = model.JumpToLetterCommand.ExecuteAsync("N");
        wire.HoldCatalog = false;
        await model.JumpToLetterCommand.ExecuteAsync("M");
        var current = model.GetWindowItem(742);
        Assert.NotNull(current);
        wire.ReleaseCatalog.TrySetResult(); await older;
        Assert.Equal(new[] { 742 }, jumped);
        Assert.Same(current, model.GetWindowItem(742));
        Assert.True(model.HasWindow(742, 742));
        Assert.False(model.IsLoading);
    }

    [Fact]
    public async Task FailedLetterReadKeepsTheCatalogUsableInsteadOfPublishingAGlobalError()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var original = model.GetWindowItem(0);
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        wire.FailCatalog = true;

        await model.JumpToLetterCommand.ExecuteAsync("M");

        Assert.Empty(jumped);
        Assert.Same(original, model.GetWindowItem(0));
        Assert.Null(model.ErrorMessage); // Failure belongs to the selected letter's retry surface.
        Assert.False(model.IsLetterJumpInProgress);
        wire.FailCatalog = false;
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(new[] { 742 }, jumped);
        Assert.NotNull(model.GetWindowItem(742));
    }

    [Fact]
    public async Task SupersedingFailedLetterRestoresTheViewportSnapshotFromBeforeBothClicks()
    {
        using var wire = new Wire { TailPosition = 900 }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.LoadWindowAsync(120, 100);
        var active = model.GetWindowItem(120);
        var cached = model.GetWindowItem(0);
        var start = model.WindowStartIndex;
        Assert.NotNull(active); Assert.NotNull(cached);
        var first = wire.HoldNextCatalog();
        var older = model.JumpToLetterCommand.ExecuteAsync("M");
        Task? newer = null;
        Wire.ReadGate? second = null;
        try
        {
            await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second = wire.HoldNextCatalog(fail: true);
            newer = model.JumpToLetterCommand.ExecuteAsync("N");
            await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second.Release.TrySetResult(); await newer;
            Assert.Equal(start, model.WindowStartIndex);
            Assert.Same(active, model.GetWindowItem(120));
            Assert.Same(cached, model.GetWindowItem(0));
            Assert.Equal("Couldn't load N titles.", model.LetterJumpError);
            first.Release.TrySetResult(); await older;
            Assert.Same(cached, model.GetWindowItem(0));
            Assert.Equal("Couldn't load N titles.", model.LetterJumpError);
            Assert.Null(model.ErrorMessage);
        }
        finally
        {
            first.Release.TrySetResult(); second?.Release.TrySetResult();
            await older; if (newer is not null) await newer;
        }
    }

    [Fact]
    public async Task RetryingAMissingTargetRevalidatesTheIndexInsteadOfRepeatingItsOldPosition()
    {
        using var wire = new Wire { CatalogExact = false, IndexTotal = 1200, TailPosition = 1199 };
        var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        await model.JumpToLetterCommand.ExecuteAsync("N");
        Assert.Empty(jumped);
        // A scan changed the optional index independently of the catalog page.
        wire.IndexTotal = 1000; wire.TailPosition = 900;

        await model.JumpToLetterCommand.ExecuteAsync("N");

        Assert.Equal(new[] { 900 }, jumped);
        Assert.NotNull(model.GetWindowItem(900));
        Assert.Equal(2, wire.IndexRequests);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("language")]
    [InlineData("filter")]
    [InlineData("library")]
    [InlineData("navigation")]
    public async Task HeldLetterCompletionCannotPublishAfterItsOwnerChanges(string change)
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        wire.HoldCatalog = true;
        var pending = model.JumpToLetterCommand.ExecuteAsync("M");
        try
        {
            Assert.True(model.IsLetterJumpInProgress);
            Assert.False(pending.IsCompleted);
            if (change == "profile") wire.Client.SetProfile("new-synthetic-profile");
            else if (change == "language") wire.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
            else if (change == "filter") model.SelectedGenre = "Comedy";
            else if (change == "library") model.Library = new Library { Id = 8, Type = "movie" };
            else model.SuspendCatalogLoads();
        }
        finally { wire.ReleaseCatalog.TrySetResult(); await pending; }

        Assert.Empty(jumped);
        Assert.False(model.IsLetterJumpInProgress);
        Assert.Null(model.GetWindowItem(742));
        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task ReversedCompletionsPublishOnlyTheLatestLetterChoice()
    {
        using var wire = new Wire { TailPosition = 900 }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        var first = wire.HoldNextCatalog();
        var older = model.JumpToLetterCommand.ExecuteAsync("M");
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = wire.HoldNextCatalog();
        var newer = model.JumpToLetterCommand.ExecuteAsync("N");
        try
        {
            await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("N", model.PendingLetter);
            second.Release.TrySetResult(); await newer;
            first.Release.TrySetResult(); await older;
            Assert.Equal(new[] { 900 }, jumped);
            Assert.NotNull(model.GetWindowItem(900));
            Assert.Null(model.PendingLetter); Assert.Null(model.LetterJumpError);
        }
        finally { first.Release.TrySetResult(); second.Release.TrySetResult(); await Task.WhenAll(older, newer); }
    }

    [Fact]
    public async Task RepeatedSameLetterKeepsNewerPendingStateWhenOlderReadFails()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        var first = wire.HoldNextCatalog(fail: true);
        var older = model.JumpToLetterCommand.ExecuteAsync("M");
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = wire.HoldNextCatalog();
        var newer = model.JumpToLetterCommand.ExecuteAsync("M");
        try
        {
            await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            first.Release.TrySetResult(); await older;
            Assert.Equal("M", model.PendingLetter);
            Assert.True(model.IsLetterJumpInProgress);
            Assert.Null(model.LetterJumpError); Assert.Null(model.ErrorMessage);
            second.Release.TrySetResult(); await newer;
            Assert.Equal(new[] { 742 }, jumped);
            Assert.Null(model.PendingLetter);
        }
        finally { first.Release.TrySetResult(); second.Release.TrySetResult(); await Task.WhenAll(older, newer); }
    }

    [Fact]
    public async Task RetiredLetterFailureCannotRestoreOverANewerManualViewport()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        var gate = wire.HoldNextCatalog(fail: true);
        var pending = model.JumpToLetterCommand.ExecuteAsync("M");
        try
        {
            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.RetireLetterJump();
            await model.EnsureRangeLoadedAsync(300, 359);
            var current = model.GetWindowItem(300);
            Assert.NotNull(current);
            gate.Release.TrySetResult(); await pending;
            Assert.Same(current, model.GetWindowItem(300));
            Assert.Empty(jumped); Assert.Null(model.PendingLetter);
            Assert.Null(model.LetterJumpError); Assert.Null(model.ErrorMessage);
        }
        finally { gate.Release.TrySetResult(); await pending; }
    }

    [Fact]
    public async Task ScopedRetryClearsFailureImmediatelyAndLoadsTheCurrentChoice()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        wire.FailCatalog = true;
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.NotNull(model.LetterJumpError); Assert.Null(model.PendingLetter);
        wire.FailCatalog = false;
        var gate = wire.HoldNextCatalog();
        var pending = model.RetryLetterJumpCommand.ExecuteAsync(null);
        try
        {
            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("M", model.PendingLetter); Assert.Null(model.LetterJumpError);
            Assert.Null(model.ErrorMessage);
        }
        finally { gate.Release.TrySetResult(); await pending; }
        Assert.NotNull(model.GetWindowItem(742)); Assert.Null(model.PendingLetter);
    }

    [Fact]
    public async Task FailedIndexRevalidationKeepsScopedRetryAvailableUntilItSucceeds()
    {
        using var wire = new Wire { CatalogExact = false, IndexTotal = 1200, TailPosition = 1199 };
        var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.JumpToLetterCommand.ExecuteAsync("N");
        Assert.NotNull(model.LetterJumpError);
        wire.FailIndex = true;

        await model.RetryLetterJumpCommand.ExecuteAsync(null);

        Assert.NotNull(model.LetterJumpError);
        Assert.Null(model.PendingLetter); Assert.Null(model.ErrorMessage);
        wire.FailIndex = false; wire.IndexTotal = 1000; wire.TailPosition = 900;
        await model.RetryLetterJumpCommand.ExecuteAsync(null);
        Assert.NotNull(model.GetWindowItem(900));
        Assert.Null(model.LetterJumpError);
    }

    [Fact]
    public async Task OrdinaryViewportReadsKeepScopedRetryWithoutBlockingBrowsing()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        wire.FailCatalog = true;
        await model.JumpToLetterCommand.ExecuteAsync("M");
        var error = model.LetterJumpError;
        Assert.NotNull(error);
        Assert.False(model.IsLetterJumpViewportBlocked);
        wire.FailCatalog = false;

        await model.EnsureRangeLoadedAsync(300, 359);

        Assert.NotNull(model.GetWindowItem(300));
        Assert.Equal(error, model.LetterJumpError);
        Assert.Null(model.ErrorMessage);
        await model.RetryLetterJumpCommand.ExecuteAsync(null);
        Assert.NotNull(model.GetWindowItem(742));
        Assert.Null(model.LetterJumpError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetryKeepsFailureWhenRevalidatedLetterIsMissingOrOutsideExactExtent(bool missing)
    {
        using var wire = new Wire { CatalogExact = false, IndexTotal = 1200, TailPosition = 1199 };
        var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumps = new List<int>(); model.LetterJumpRequested += jumps.Add;
        await model.JumpToLetterCommand.ExecuteAsync("N");
        Assert.NotNull(model.LetterJumpError);
        if (missing) { wire.MissingTail = true; wire.IndexTotal = 1000; }
        else
        {
            wire.CatalogTotal = 800; wire.WindowTotalExact = true;
            await model.LoadWindowAsync(0, 100, force: true);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await model.RetryLetterJumpCommand.ExecuteAsync(null);
            Assert.Equal("Couldn't load N titles.", model.LetterJumpError);
            Assert.Null(model.PendingLetter); Assert.Null(model.ErrorMessage);
            Assert.False(model.IsLetterJumpInProgress);
            Assert.Empty(jumps);
            Assert.Equal(2 + attempt, wire.IndexRequests);
        }
        wire.MissingTail = false; wire.IndexTotal = 800; wire.TailPosition = 790;
        await model.RetryLetterJumpCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 790 }, jumps);
        Assert.Null(model.LetterJumpError);
    }

    [Fact]
    public async Task AnotherContextWithoutOptionalIndexDoesNotInheritTheOldChoiceFailure()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var previous = model.GetWindowItem(0);
        wire.Client.SetProfile("other-synthetic-profile"); wire.Supported = false;

        await model.JumpToLetterCommand.ExecuteAsync("M");

        Assert.Null(model.LetterIndex); Assert.Null(model.PendingLetter);
        Assert.Null(model.LetterJumpError); Assert.Null(model.ErrorMessage);
        Assert.NotNull(model.GetWindowItem(0));
        Assert.NotSame(previous, model.GetWindowItem(0));
    }

    [Fact]
    public async Task UnsupportedIndexRetryReloadsTheFailedContextResetCatalog()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var previous = model.GetWindowItem(0);
        var jumps = new List<int>(); model.LetterJumpRequested += jumps.Add;
        wire.Client.SetProfile("other-synthetic-profile");
        wire.Supported = false; wire.FailCatalog = true;

        await model.JumpToLetterCommand.ExecuteAsync("M");

        Assert.Null(model.LetterIndex); Assert.Null(model.GetWindowItem(0));
        Assert.Equal(0, model.TotalCount); Assert.Equal(-1, model.WindowStartIndex);
        Assert.Equal("Couldn't load M titles.", model.LetterJumpError);
        Assert.True(model.IsLetterJumpViewportBlocked);
        var reads = wire.Seeks.Count;
        await model.EnsureRangeLoadedAsync(0, 0);
        Assert.Equal(reads, wire.Seeks.Count); // Automatic browsing cannot consume Retry.

        await model.RetryLetterJumpCommand.ExecuteAsync(null);

        Assert.True(wire.Seeks.Count > reads);
        Assert.NotNull(model.LetterJumpError);
        Assert.True(model.IsLetterJumpViewportBlocked);
        reads = wire.Seeks.Count;
        wire.FailCatalog = false;

        await model.RetryLetterJumpCommand.ExecuteAsync(null);

        Assert.True(wire.Seeks.Count > reads);
        Assert.All(wire.Seeks.Skip(reads), seek => Assert.Equal(0, seek));
        Assert.NotNull(model.GetWindowItem(0));
        Assert.NotSame(previous, model.GetWindowItem(0));
        Assert.Null(model.LetterIndex); Assert.Null(model.LetterJumpError);
        Assert.Null(model.PendingLetter); Assert.Null(model.ErrorMessage);
        Assert.False(model.IsLetterJumpViewportBlocked);
        Assert.False(model.IsLetterJumpInProgress);
        Assert.Empty(jumps); // Unsupported indexes never guess a target.
        Assert.Equal(1, wire.IndexRequests);
        Assert.Equal(2, wire.CapabilityRequests);
    }

    [Fact]
    public async Task ManualBrowsingReloadsAFailedContextResetWithoutAnExtent()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        wire.Client.SetProfile("other-synthetic-profile");
        wire.Supported = false; wire.FailCatalog = true;
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(0, model.TotalCount);
        Assert.True(model.IsLetterJumpViewportBlocked);
        wire.FailCatalog = false;

        model.RetireLetterJump();
        await model.EnsureRangeLoadedAsync(0, 0);

        Assert.NotNull(model.GetWindowItem(0));
        Assert.True(model.TotalCount > 0);
        Assert.Null(model.LetterJumpError); Assert.Null(model.ErrorMessage);
        Assert.False(model.IsLetterJumpViewportBlocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowsingRetiresAContextResetWhileItsOptionalIndexIsPending(bool supported)
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumps = new List<int>(); model.LetterJumpRequested += jumps.Add;
        wire.Client.SetProfile("other-synthetic-profile"); wire.Supported = supported;
        wire.CapabilityGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = model.JumpToLetterCommand.ExecuteAsync("M");
        try
        {
            await wire.CapabilityStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, model.TotalCount);
            Assert.Equal("M", model.PendingLetter);
            model.RetireLetterJump(); // Manual input or leaving the Library tab.
            await model.EnsureRangeLoadedAsync(0, 0).WaitAsync(TimeSpan.FromSeconds(5));
            var current = model.GetWindowItem(0);
            Assert.NotNull(current);
            wire.CapabilityGate.TrySetResult(); await pending;
            Assert.Same(current, model.GetWindowItem(0));
            Assert.Empty(jumps); Assert.Null(model.PendingLetter);
            Assert.Null(model.LetterJumpError); Assert.Null(model.ErrorMessage);
            Assert.False(model.IsLetterJumpInProgress);
        }
        finally { wire.CapabilityGate.TrySetResult(); await pending; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryRangeLoadingDoesNotReloadAKnownEmptyCatalog(bool exact)
    {
        using var wire = new Wire { Supported = false, CatalogTotal = 0, CatalogExact = exact };
        var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var reads = wire.Seeks.Count;

        await model.EnsureRangeLoadedAsync(0, 0);

        Assert.Equal(reads, wire.Seeks.Count);
        Assert.Equal(0, model.TotalCount); Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public void PageLoadsAnUnknownEmptyExtentOnlyAfterCheckingLetterOwnership()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "LibraryPage.xaml.cs"));
        var start = source.IndexOf("private async Task EnsureVisibleRangeLoadedAsync", StringComparison.Ordinal);
        var end = source.IndexOf("var range = GetVisibleItemRange", start, StringComparison.Ordinal);
        var guard = source[start..end];
        var empty = guard.IndexOf("if (ViewModel.TotalCount <= 0)", StringComparison.Ordinal);
        Assert.True(empty > guard.IndexOf("if (ViewModel.IsLetterJumpViewportBlocked)", StringComparison.Ordinal));
        Assert.True(empty > guard.IndexOf("if (ViewModel.IsLetterJumpInProgress)", StringComparison.Ordinal));
        Assert.Contains("await ViewModel.EnsureRangeLoadedAsync(0, 0);", guard[empty..]);
        Assert.Contains("ScheduleVisibleRangeLoad(force: true);", guard[empty..]);
    }

    [Theory]
    [InlineData("profile", false)]
    [InlineData("profile", true)]
    [InlineData("language", false)]
    [InlineData("language", true)]
    public async Task ScopeChangeRetiresSettledFailureWhenObservedOrRetried(string change, bool observeFirst)
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        wire.FailCatalog = true;
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.NotNull(model.LetterJumpError);
        var notifications = 0;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.LetterJumpError)) notifications++; };
        if (change == "profile") wire.Client.SetProfile("other-synthetic-profile");
        else wire.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        wire.FailCatalog = false;
        var reads = wire.Seeks.Count;

        if (observeFirst) Assert.Null(model.LetterJumpError);
        await model.RetryLetterJumpCommand.ExecuteAsync(null);

        Assert.Null(model.LetterJumpError); Assert.Null(model.PendingLetter);
        Assert.Equal(1, notifications);
        Assert.Equal(reads, wire.Seeks.Count); // Never replay a choice owned by the old scope.
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.NotNull(model.GetWindowItem(742));
        Assert.Null(model.LetterJumpError);
    }

    [Fact]
    public async Task IndexFailureLeavesCatalogUsableAndDoesNotGuess()
    {
        using var wire = new Wire { FailIndex = true }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Null(model.LetterIndex); Assert.Null(model.ErrorMessage);
        Assert.NotNull(model.GetWindowItem(0)); Assert.Equal(0, wire.ProbeRequests);
        Assert.Equal(2, wire.IndexRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowPublicationsDoNotRetryFailedLetterIndex(bool failCapabilities)
    {
        // The view must only render publications, not turn them into retries.
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "LibraryPage.xaml.cs"));
        var start = source.IndexOf("private void OnLibraryWindowLoaded()", StringComparison.Ordinal);
        var end = source.IndexOf("private void QueueRenderVirtualGrid", start, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureLetterIndexAsync", source[start..end]);

        using var wire = new Wire { FailIndex = true, FailCapabilities = failCapabilities }; var model = wire.Create();
        var publications = 0; model.WindowLoaded += () => publications++;
        await model.LoadCommand.ExecuteAsync(null);
        for (var startIndex = 120; startIndex <= 840; startIndex += 120)
            await model.LoadWindowAsync(startIndex, 100, force: true);
        Assert.True(publications >= 8);
        Assert.Equal(1, wire.CapabilityRequests);
        Assert.Equal(failCapabilities ? 0 : 1, wire.IndexRequests);
        Assert.False(model.IsLetterIndexLoading);
        Assert.Null(model.ErrorMessage);
        Assert.NotNull(model.GetWindowItem(840));
        // Returning to the page is an explicit, bounded retry trigger.
        model.SuspendCatalogLoads();
        await model.EnsureLetterIndexAsync();
        Assert.Equal(failCapabilities ? 2 : 1, wire.CapabilityRequests);
        Assert.Equal(failCapabilities ? 0 : 2, wire.IndexRequests);
    }

    [Fact]
    public async Task FailedIndexCanBeRetriedAndPublishExactLetters()
    {
        using var wire = new Wire { FailIndex = true }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Null(model.LetterIndex);
        Assert.Equal(1, wire.IndexRequests);
        wire.FailIndex = false;
        await model.EnsureLetterIndexAsync();
        Assert.Equal(2, wire.IndexRequests);
        Assert.NotNull(model.LetterIndex);
        var jumped = -1; model.LetterJumpRequested += position => jumped = position;
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(742, jumped);
        Assert.Equal(2, wire.IndexRequests);
        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task IndexFailureIsLoggedWithScopeWithoutShowingCatalogError()
    {
        var directory = Path.Combine(Path.GetTempPath(), "library-letters-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var operation = SiloPlayer.Core.Services.LocalLog.BeginOperation("letters-test", directory);
            using var wire = new Wire { FailIndex = true }; var model = wire.Create();
            await model.LoadCommand.ExecuteAsync(null);
            var log = File.ReadAllText(Path.Combine(directory, "catalog_errors.txt"));
            Assert.Contains("catalog_letters library=7 order=asc type=all group_count=0 query_fingerprint=", log);
            Assert.Contains("status=503", log);
            Assert.Contains("failed after the server advertised letter_index=true", log);
            Assert.Null(model.ErrorMessage);
            Assert.NotNull(model.GetWindowItem(0));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task EmptyLibraryHasNoInventedJump()
    {
        using var wire = new Wire { IndexTotal = 0, CatalogTotal = 0 }; var model = wire.Create();
        var jumps = 0; model.LetterJumpRequested += _ => jumps++;
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.JumpToLetterCommand.ExecuteAsync("A");
        Assert.Equal(0, model.TotalCount); Assert.Empty(model.LetterIndex!.Letters);
        Assert.Equal(0, jumps); Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task ScalarFiltersRemainConjoinedWithAnyStructuredGroups()
    {
        using var wire = new Wire(); var api = new CatalogApi(wire.Client);
        await api.GetCatalogLettersAsync(7, genre: "Drama", yearMin: "2001", queryGroupsMatch: "any",
            queryGroups: [new() { Match = "any", Rules = [new() { Field = "studio", Op = "is", Value = "One" }, new() { Field = "studio", Op = "is", Value = "Two" }] }]);
        using var body = JsonDocument.Parse(wire.IndexBody!);
        var groups = body.RootElement.GetProperty("groups"); Assert.Equal(2, groups.GetArrayLength());
        foreach (var group in groups.EnumerateArray())
        {
            Assert.Equal("all", group.GetProperty("match").GetString());
            Assert.Contains(group.GetProperty("rules").EnumerateArray(), rule => rule.GetProperty("field").GetString() == "genre" && rule.GetProperty("value").GetString() == "Drama");
        }
    }

    [Fact]
    public async Task MultipleGuidedLanguagesRetainStudioActorRatingAndTechnicalFilters()
    {
        using var wire = new Wire(); var model = wire.Create();
        model.SelectedOriginalLanguages = ["en", "fr"];
        model.SelectedStudio = "Studio"; model.SelectedCountry = "GB";
        model.SelectedResolution = "1080p"; model.SelectedAudioLanguage = "en";
        model.SelectedActor = "Actor"; model.SelectedMinimumRating = "7";
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        using var catalog = JsonDocument.Parse(wire.CatalogGroups!);
        using var index = JsonDocument.Parse(wire.IndexBody!);
        var groups = index.RootElement.GetProperty("groups");
        Assert.Equal(catalog.RootElement.GetRawText(), groups.GetRawText());
        Assert.Equal("all", wire.CatalogMatch);
        Assert.Equal("all", index.RootElement.GetProperty("match").GetString());
        Assert.Equal(2, groups.GetArrayLength());
        Assert.Equal("any", groups[0].GetProperty("match").GetString());
        Assert.Equal(new[] { "en", "fr" }, groups[0].GetProperty("rules").EnumerateArray()
            .Select(rule => rule.GetProperty("value").GetString()).ToArray());
        var supplemental = groups[1];
        Assert.Equal("all", supplemental.GetProperty("match").GetString());
        foreach (var (field, value) in new[] { ("studio", "Studio"), ("country", "GB"),
            ("resolution", "1080p"), ("audio_language", "en"), ("actor", "Actor") })
            Assert.Contains(supplemental.GetProperty("rules").EnumerateArray(), rule =>
                rule.GetProperty("field").GetString() == field && rule.GetProperty("value").GetString() == value);
        Assert.Contains(supplemental.GetProperty("rules").EnumerateArray(), rule =>
            rule.GetProperty("field").GetString() == "rating_imdb" &&
            rule.GetProperty("op").GetString() == "gte" && rule.GetProperty("value").GetDouble() == 7);
    }

    [Theory]
    [InlineData("all", "all")]
    [InlineData("all", "any")]
    [InlineData("any", "all")]
    [InlineData("any", "any")]
    public async Task SupplementalFiltersConjoinWithStructuredGroupsUnderBothMatchModes(string match, string extraMatch)
    {
        using var wire = new Wire(); var api = new CatalogApi(wire.Client);
        QueryGroup[] queryGroups =
        [
            new() { Match = "any", Rules = [new() { Field = "original_language", Op = "is", Value = "en" },
                new() { Field = "original_language", Op = "is", Value = "fr" }] },
            new() { Match = "all", Rules = [new() { Field = "genre", Op = "is", Value = "Drama" }] },
        ];
        QueryRule[] extras = [new() { Field = "actor", Op = "is", Value = "Actor" }];
        await api.GetCatalogAsync(7, studio: "Studio", queryGroups: queryGroups, queryGroupsMatch: match,
            extraRules: extras, extraRulesMatch: extraMatch);
        await api.GetCatalogLettersAsync(7, studio: "Studio", queryGroups: queryGroups, queryGroupsMatch: match,
            extraRules: extras, extraRulesMatch: extraMatch);
        using var catalog = JsonDocument.Parse(wire.CatalogGroups!);
        using var index = JsonDocument.Parse(wire.IndexBody!);
        var groups = index.RootElement.GetProperty("groups");
        Assert.Equal(catalog.RootElement.GetRawText(), groups.GetRawText());
        Assert.Equal(wire.CatalogMatch, index.RootElement.GetProperty("match").GetString());
        // Check the whole truth table, not just the presence of filter badges.
        var values = new[] { "en", "fr", "Drama", "Studio", "Actor" };
        for (var mask = 0; mask < 32; mask++)
        {
            bool Has(int bit) => (mask & (1 << bit)) != 0;
            var present = values.Where((_, bit) => Has(bit)).ToHashSet();
            var languages = Has(0) || Has(1);
            var structured = match == "any" ? languages || Has(2) : languages && Has(2);
            var supplemental = extraMatch == "any" ? Has(3) || Has(4) : Has(3) && Has(4);
            var results = groups.EnumerateArray().Select(group =>
            {
                var rules = group.GetProperty("rules").EnumerateArray()
                    .Select(rule => present.Contains(rule.GetProperty("value").GetString()!));
                return group.GetProperty("match").GetString() == "any" ? rules.Any(value => value) : rules.All(value => value);
            });
            var actual = match == "any" ? results.Any(value => value) : results.All(value => value);
            Assert.Equal(structured && supplemental, actual);
        }
    }

    [Fact]
    public void RunsIdentifyRepeatedLetterBlocksAndRejectInvalidPositions()
    {
        var index = new CatalogLettersResponse { Total = 6, Letters = [new() { Letter = "A", Count = 4, Position = 0 }, new() { Letter = "#", Count = 2, Position = 2 }],
            Runs = [new() { Letter = "A", Position = 0 }, new() { Letter = "#", Position = 2 }, new() { Letter = "A", Position = 4 }] };
        index.Validate(); Assert.Equal("A", index.LetterAtIndex(5)); Assert.Equal("#", index.LetterAtIndex(3));
        index.Runs[2].Position = 1;
        Assert.Throws<InvalidDataException>(() => index.Validate());
    }

    private sealed class Wire : HttpMessageHandler
    {
        private readonly HttpClient http;
        internal readonly SiloApiClient Client;
        internal bool Supported = true;
        internal int IndexRequests, ProbeRequests, CapabilityRequests;
        internal string? IndexBody, CatalogGroups, CatalogMatch;
        internal bool HoldIndex, FailIndex, FailCapabilities, MissingTail;
        internal int CatalogTotal = 1000;
        internal int IndexTotal = 1000;
        internal int TailPosition = 749;
        internal bool CatalogExact = true;
        internal bool WindowTotalExact;
        internal int? ExactCountTotal;
        internal readonly TaskCompletionSource ReleaseExactCount = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HoldCatalog, FailCatalog;
        internal readonly TaskCompletionSource ReleaseCatalog = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource IndexStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseIndex = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? CapabilityGate;
        internal readonly TaskCompletionSource CapabilityStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<int> Seeks = [];
        private readonly System.Collections.Concurrent.ConcurrentQueue<ReadGate> heldReads = new();
        internal sealed class ReadGate(bool fail)
        {
            internal bool Fail => fail;
            internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        internal ReadGate HoldNextCatalog(bool fail = false)
        { var gate = new ReadGate(fail); heldReads.Enqueue(gate); return gate; }
        internal Wire() { http = new(this, disposeHandler: false); Client = new(http); Client.SetBaseUrl("https://letter-fixture.invalid"); }
        internal LibraryViewModel Create() => new(new CatalogApi(Client)) { Library = new Library { Id = 7, Type = "movie" } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/search/capabilities"))
            {
                CapabilityRequests++;
                if (CapabilityGate is { } gate)
                {
                    CapabilityStarted.TrySetResult();
                    await gate.Task; // Deliberately late optional-index capability response.
                }
                return FailCapabilities ? Reply(new { detail = "Synthetic unavailable capability probe" }, HttpStatusCode.ServiceUnavailable)
                    : Reply(new { letter_index = Supported });
            }
            if (path.EndsWith("/catalog/letters"))
            {
                IndexRequests++; IndexBody = await request.Content!.ReadAsStringAsync(ct);
                IndexStarted.TrySetResult();
                if (HoldIndex) await ReleaseIndex.Task; // Deliberately ignore cancellation to model a late transport.
                if (FailIndex) return Reply(new { detail = "Synthetic unavailable index" }, HttpStatusCode.ServiceUnavailable);
                if (IndexTotal == 0) return Reply(new { total = 0, letters = Array.Empty<object>(), runs = Array.Empty<object>() });
                if (MissingTail) return Reply(new { total = IndexTotal,
                    letters = new[] { new { letter = "A", count = 742, position = 0 }, new { letter = "M", count = IndexTotal - 742, position = 742 } },
                    runs = new[] { new { letter = "A", position = 0 }, new { letter = "M", position = 742 } } });
                return Reply(new { total = IndexTotal, letters = new[] { new { letter = "A", count = 742, position = 0 }, new { letter = "M", count = TailPosition - 742, position = 742 }, new { letter = "N", count = IndexTotal - TailPosition, position = TailPosition } }, runs = new[] { new { letter = "A", position = 0 }, new { letter = "M", position = 742 }, new { letter = "N", position = TailPosition } } });
            }
            if (path == "/api/v2/catalog")
            {
                var q = request.RequestUri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
                CatalogGroups = q.GetValueOrDefault("groups"); CatalogMatch = q.GetValueOrDefault("match", "all");
                var limit = int.Parse(q["limit"]); var seek = q.TryGetValue("seek", out var value) ? int.Parse(value) : 0;
                Seeks.Add(seek); if (limit == 1) ProbeRequests++;
                if (heldReads.TryDequeue(out var gate))
                {
                    gate.Started.TrySetResult();
                    await gate.Release.Task; // Late transports deliberately ignore cancellation.
                    if (gate.Fail) return Reply(new { detail = "Synthetic retired window failure" }, HttpStatusCode.ServiceUnavailable);
                }
                if (HoldCatalog) await ReleaseCatalog.Task;
                if (FailCatalog) return Reply(new { detail = "Synthetic unavailable catalog window" }, HttpStatusCode.ServiceUnavailable);
                var countOnly = !q.ContainsKey("skip_total") && ExactCountTotal.HasValue;
                if (countOnly) await ReleaseExactCount.Task;
                var total = countOnly ? ExactCountTotal!.Value : CatalogTotal;
                // Normal skip-total windows have no authoritative count.
                // Only explicit opt-in cases model a server returning one anyway.
                var exact = q.ContainsKey("skip_total") ? WindowTotalExact : countOnly || CatalogExact;
                return Reply(new { total = exact ? total : 0, total_exact = exact, items = Enumerable.Range(seek, Math.Max(0, Math.Min(limit, total - seek))).Select(i => new { content_id = "item" + i, title = (i < 742 ? "A " : i < TailPosition ? "M " : "N ") + i, type = "movie" }), page = new { has_more = seek + limit < total } });
            }
            return Reply(new { }, HttpStatusCode.NotFound);
        }
        private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        protected override void Dispose(bool disposing) { if (disposing) http.Dispose(); base.Dispose(disposing); }
    }
}
