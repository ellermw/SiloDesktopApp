using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Downloads;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class DownloadsViewModelTests
{
    [Fact]
    public async Task FailedDeleteKeepsRegistryEntryAndShowsRetryableError()
    {
        using var http = new HttpClient(new DeleteHandler(HttpStatusCode.InternalServerError));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://downloads.test");
        var viewModel = new DownloadsViewModel(new DownloadsApi(client));
        var item = new Download { Id = "download-1", Status = "ready" };
        viewModel.Downloads.Add(item);

        await viewModel.DeleteDownloadCommand.ExecuteAsync(item.Id);

        Assert.Same(item, Assert.Single(viewModel.Downloads));
        Assert.False(viewModel.IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
    }

    [Fact]
    public async Task SuccessfulDeleteRemovesEntryAndClearsPreviousError()
    {
        using var http = new HttpClient(new DeleteHandler(HttpStatusCode.NoContent));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://downloads.test");
        var viewModel = new DownloadsViewModel(new DownloadsApi(client)) { ErrorMessage = "previous failure" };
        viewModel.Downloads.Add(new Download { Id = "download-1" });

        await viewModel.DeleteDownloadCommand.ExecuteAsync("download-1");

        Assert.Empty(viewModel.Downloads);
        Assert.True(viewModel.IsEmpty);
        Assert.Null(viewModel.ErrorMessage);
    }

    private sealed class DeleteHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/api/v2/downloads/download-1", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"error\":\"internal_error\",\"message\":\"Please retry later\"}")
            });
        }
    }

    [Fact]
    public async Task CanceledDeleteKeepsEntryWithoutShowingFailure()
    {
        var handler = new PendingDeleteHandler();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://downloads.test");
        var viewModel = new DownloadsViewModel(new DownloadsApi(client));
        viewModel.Downloads.Add(new Download { Id = "download-1" });

        var deleting = viewModel.DeleteDownloadCommand.ExecuteAsync("download-1");
        await handler.Started.Task;
        viewModel.DeleteDownloadCommand.Cancel();
        await deleting;

        Assert.Single(viewModel.Downloads);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task NetworkTimeoutDuringDeleteIsShownAsFailure()
    {
        using var http = new HttpClient(new TimeoutDeleteHandler());
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://downloads.test");
        var viewModel = new DownloadsViewModel(new DownloadsApi(client));
        viewModel.Downloads.Add(new Download { Id = "download-1" });

        await viewModel.DeleteDownloadCommand.ExecuteAsync("download-1");

        Assert.Single(viewModel.Downloads);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
    }

    private sealed class PendingDeleteHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class TimeoutDeleteHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new TaskCanceledException("Connection timed out");
    }
}
