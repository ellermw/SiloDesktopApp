using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class NotificationsInboxBehaviorTests
{
    [Fact]
    public async Task InboxIsUsableWhileOptionalPreferencesAndCountArePending()
    {
        using var wire = new Wire { HoldOptional = true };
        var vm = wire.ViewModel();
        var pending = vm.LoadCommand.ExecuteAsync(null);
        try
        {
            await wire.OptionalStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Single(vm.Notifications);
            Assert.False(vm.IsLoading);
            Assert.True(vm.CanMarkAllRead);
            Assert.True(vm.HasMore);
        }
        finally { wire.Release.TrySetResult(); await pending; }
    }

    [Fact]
    public async Task OptionalPreferencesResponseCannotCrossProfileAuthority()
    {
        using var wire = new Wire { HoldOptional = true }; wire.ViewModel();
        var pending = new NotificationsApi(wire.Client!).GetPreferencesAsync();
        await wire.OptionalStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        wire.Client!.SetProfile("different-profile"); wire.Release.TrySetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task ProfileChangeCannotReuseAnotherProfilesCachedInbox()
    {
        using var wire = new Wire(); var vm = wire.ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        wire.Client!.SetProfile("different-profile");
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, wire.ListReads);
        Assert.Equal("different-profile", Assert.Single(vm.Notifications).Id);
    }

    [Fact]
    public async Task DuplicateReadOfRemovedUnreadRowDoesNotConsumeAnotherRowsCount()
    {
        using var wire = new Wire(); var vm = wire.ViewModel();
        await vm.SetFilterCommand.ExecuteAsync("unread");
        vm.ApplyCreated(new() { Id = "second" });
        Assert.Equal(2, vm.UnreadCount);
        vm.ApplyRead("recent"); Assert.Equal(1, vm.UnreadCount);
        vm.ApplyRead("recent");
        Assert.Equal(1, vm.UnreadCount);
        Assert.Equal("second", Assert.Single(vm.Notifications).Id);
    }

    [Fact]
    public async Task LoadingOlderPagesPreservesFirstPageReadCutoff()
    {
        using var wire = new Wire();
        var vm = wire.ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "recent", "older" }, vm.Notifications.Select(n => n.Id));
        await vm.MarkAllReadCommand.ExecuteAsync(null);
        Assert.Equal("first-page-cutoff", wire.MarkedThrough);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedCursorIsRejectedWithoutAppendingOrLooping(bool cycle)
    {
        using var wire = new Wire { SecondCursor = cycle ? "earlier" : "next", ThirdCursor = "next" };
        var vm = wire.ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        if (cycle)
        {
            await vm.LoadMoreCommand.ExecuteAsync(null);
            Assert.Equal(2, vm.Notifications.Count);
        }
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Contains("cursor", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.HasMore);
        Assert.Equal(cycle ? 2 : 1, vm.Notifications.Count);
        var reads = wire.ListReads;
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(reads, wire.ListReads);
        await vm.ReloadAsync();
        Assert.Null(vm.ErrorMessage);
        Assert.Single(vm.Notifications);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal bool HoldOptional;
        internal string? SecondCursor, ThirdCursor;
        internal int ListReads;
        internal string? MarkedThrough;
        internal readonly TaskCompletionSource OptionalStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HttpClient? _http;
        internal SiloApiClient? Client;
        internal NotificationsViewModel ViewModel()
        {
            _http = new HttpClient(this, false);
            var client = new SiloApiClient(_http); client.SetBaseUrl("https://notification-inbox.invalid");
            Client = client;
            client.SetProfile("fixture-profile");
            return new(new NotificationsApi(client));
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("notification-inbox.invalid", request.RequestUri!.Host);
            var path = request.RequestUri.AbsolutePath;
            object body;
            if (path.EndsWith("/read-all"))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                MarkedThrough = json.RootElement.GetProperty("through").GetString();
                return new(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/notifications"))
            {
                ListReads++;
                var cursor = request.RequestUri.Query.Contains("cursor=");
                var third = request.RequestUri.Query.Contains("cursor=earlier");
                body = new
                {
                    items = new[] { new { id = cursor ? third ? "oldest" : "older" : Client!.CaptureContext().ProfileId == "different-profile" ? "different-profile" : "recent" } },
                    page = new { next_cursor = cursor ? third ? ThirdCursor : SecondCursor : "next", has_more = !cursor || (third ? ThirdCursor : SecondCursor) != null },
                    read_cutoff = cursor ? "older-page-cutoff" : "first-page-cutoff"
                };
            }
            else
            {
                if (HoldOptional) { OptionalStarted.TrySetResult(); await Release.Task.WaitAsync(ct); }
                body = path.EndsWith("/unread-count") ? (object)new { count = 1 } : new { enabled = true };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
        protected override void Dispose(bool disposing) { Release.TrySetResult(); if (disposing) _http?.Dispose(); base.Dispose(disposing); }
    }
}
