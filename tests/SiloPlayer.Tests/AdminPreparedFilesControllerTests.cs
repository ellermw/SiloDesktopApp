using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminPreparedFilesControllerTests
{
    private static string File(string id) => $$"""{"id":"{{id}}","state":"cached","format":"transcode","location":"server","location_name":"Server","media_file_id":"42","title":"Title {{id}}","media_type":"movie","container":"mp4","video_codec":"h264","audio_codec":"aac","bytes":10,"created_at":"2026-10-08T09:30:00Z","last_used_at":"2026-10-08T09:30:00Z","waiting":0,"downloading":0,"finished":1,"stale_waiting":0}""";

    [Fact]
    public async Task ChangedFiltersRetireOldReadsAndRestartWithoutTheirCursor()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var slow = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, _) => request.RequestUri!.Query.Contains("q=old") ? slow.Task
            : Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + File("new") + "],\"page\":{\"has_more\":false}}"));
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);

        var old = controller.SetFileQueryAsync(controller.State.FileQuery with { Search = "old" });
        await controller.SetFileQueryAsync(controller.State.FileQuery with { Search = "new" });
        slow.SetResult(AdminMonitorKit.Json("{\"items\":[" + File("old") + "],\"page\":{\"has_more\":false}}"));
        await old;

        Assert.Equal("new", Assert.Single(controller.State.Files.Items).Id);
        Assert.Equal("new", controller.State.FileQuery.Search);
        Assert.DoesNotContain("cursor=", kit.Http.Requests.Last());
    }

    [Fact]
    public async Task LoadMoreIsBoundedAndDoesNotWalkAWholeServerCollection()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var page = 0;
        kit.Http.Respond = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/storage", StringComparison.Ordinal))
                return Task.FromResult(AdminMonitorKit.Json("{\"locations\":[],\"cache_hours\":72,\"disk_ceiling_percent\":85,\"default_budget_bytes\":0,\"stale_device_days\":14,\"preparing_jobs\":0,\"freed_last_30_days_bytes\":0}"));
            page++;
            return Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + File(page.ToString()) + "],\"page\":{\"has_more\":true,\"next_cursor\":\"c" + page + "\"}}"));
        };
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        for (var i = 0; i < AdminPreparedFilesController.MaxRetainedPages + 3; i++) await controller.LoadMoreFilesAsync();

        Assert.Equal(AdminPreparedFilesController.MaxRetainedPages, page);
        Assert.Equal(AdminPreparedFilesController.MaxRetainedPages, controller.State.Files.Items.Count);
        Assert.True(controller.State.Files.Truncated);
    }

    [Fact]
    public async Task CursorCycleAcrossSeveralPagesFailsClosed()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var page = 0;
        kit.Http.Respond = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/storage", StringComparison.Ordinal))
                return Task.FromResult(AdminMonitorKit.Json("{\"locations\":[],\"cache_hours\":72,\"disk_ceiling_percent\":85,\"default_budget_bytes\":0,\"stale_device_days\":14,\"preparing_jobs\":0,\"freed_last_30_days_bytes\":0}"));
            page++;
            var next = page == 1 ? "c1" : page == 2 ? "c2" : "c1";
            return Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + File(page.ToString()) + "],\"page\":{\"has_more\":true,\"next_cursor\":\"" + next + "\"}}"));
        };
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);

        await controller.OpenAsync("files", null);
        await controller.LoadMoreFilesAsync();
        await controller.LoadMoreFilesAsync();

        Assert.Equal(2, controller.State.Files.Items.Count);
        Assert.Equal(AdminFailureKind.InvalidResponse, controller.State.Files.ErrorKind);
        Assert.NotNull(controller.State.Files.Error);
    }

    [Fact]
    public async Task DestructiveWriteIsSentOnceThenReconciledWithoutReplayAfterUncertainFailure()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var writes = 0;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                writes++;
                throw new IOException("connection closed after dispatch");
            }
            return Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.AbsolutePath.EndsWith("/storage")
                ? "{\"locations\":[],\"cache_hours\":72,\"disk_ceiling_percent\":85,\"default_budget_bytes\":0,\"stale_device_days\":14,\"preparing_jobs\":0,\"freed_last_30_days_bytes\":0}"
                : "{\"items\":[" + File("one") + "],\"page\":{\"has_more\":false}}"));
        };
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        await controller.DeleteFilesAsync([controller.State.Files.Items[0]], includeInUse: false);

        Assert.Equal(1, writes);
        Assert.Equal(AdminActionOutcome.Unknown, controller.State.LastAction?.Outcome);
        Assert.Contains("Check the current list", controller.State.LastAction?.Message);
    }

    [Fact]
    public async Task AuthorityRetirementClearsEveryTabAndDiscardsLateResults()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => pending.Task;
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        var open = controller.OpenAsync("storage", null);
        kit.SetRole("user");
        pending.SetResult(AdminMonitorKit.Json("{\"locations\":[],\"cache_hours\":72,\"disk_ceiling_percent\":85,\"default_budget_bytes\":0,\"stale_device_days\":14,\"preparing_jobs\":0,\"freed_last_30_days_bytes\":0}"));
        await open;

        Assert.Null(controller.State.Owner);
        Assert.Null(controller.State.Storage.Value);
        Assert.Empty(controller.State.Files.Items);
        Assert.Empty(controller.State.Devices.Items);
        Assert.Empty(controller.State.History.Items);
        Assert.Empty(controller.State.Preparations);
        Assert.Throws<MonitorAuthorityRetiredException>(() => controller.CaptureAction());
    }
}
