using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class RequestBrowseRecoveryBehaviorTests
{
    [Fact]
    public async Task EmptyPagesAdvanceAndFailedLaterPageRetriesWithoutLosingResults()
    {
        var session = new RequestBrowseSession(); var pages = new List<int>(); var fail = true;
        await session.ResetAsync((page, _) =>
        {
            pages.Add(page);
            if (page == 3 && fail) throw new InvalidOperationException("later page unavailable");
            var items = page == 1 ? new List<RequestMediaResult>() : new List<RequestMediaResult> { new() { MediaType = "movie", TmdbId = 7, Title = "Retained" } };
            if (page == 3) items.Add(new() { MediaType = "series", TmdbId = 7, Title = "Distinct type" });
            return Task.FromResult((new DiscoverBrowseResponse { DisplayName = "Drama", Results = items, TotalPages = 3 }, page < 3 ? (int?)(page + 1) : null));
        });
        Assert.True(session.HasMore); Assert.Empty(session.Results);
        await session.LoadMoreAsync(); Assert.Single(session.Results);
        await session.LoadMoreAsync(); Assert.Single(session.Results); Assert.NotNull(session.MoreError); Assert.True(session.HasMore);
        fail = false; await session.LoadMoreAsync();
        Assert.Equal(2, session.Results.Count); Assert.Null(session.MoreError); Assert.False(session.HasMore);
        Assert.Equal(new[] { 1, 2, 3, 3 }, pages);
    }

    [Fact]
    public async Task FilterResetSuppressesSupersededPageEvenIfTransportIgnoresCancellation()
    {
        var session = new RequestBrowseSession(); var pending = new TaskCompletionSource<(DiscoverBrowseResponse, int?)>();
        var old = session.ResetAsync((_, _) => pending.Task);
        await session.ResetAsync((_, _) => Task.FromResult((new DiscoverBrowseResponse { DisplayName = "New", Results = [new() { TmdbId = 2, MediaType = "movie" }] }, (int?)null)));
        pending.SetResult((new() { DisplayName = "Old", Results = [new() { TmdbId = 1, MediaType = "movie" }] }, 2)); await old;
        Assert.Equal("New", session.FirstPage?.DisplayName); Assert.Equal(2, Assert.Single(session.Results).TmdbId); Assert.False(session.HasMore);
    }
}
