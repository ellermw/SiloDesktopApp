using System.Net;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed partial class AdminLibrariesControllerTests
{
    /// <summary>
    /// The Stop a row drew a moment ago still means Stop when the last scan ended in between: it asks the server to cancel (which finds nothing) and never starts
    /// a scan the admin did not ask for (official: the button's action is fixed when it is rendered).
    /// </summary>
    [Fact]
    public async Task AStopDrawnBeforeTheScansEndedDoesNotStartAScan()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            server.Override = request => request.RequestUri!.AbsolutePath == "/api/v2/scan/cancel"
                ? AdminMonitorKit.Json("{\"library_id\":\"1\",\"cancelled\":0}") : null;
            await controller.OpenAsync();
            Assert.Empty(controller.State.Scans);
            await controller.ScanOrStopAsync("1", stop: true);
            Assert.Contains("POST /api/v2/scan/cancel", kit.Http.Requests);
            Assert.DoesNotContain("POST /api/v2/scan", kit.Http.Requests);

            // A drawn Scan with nothing running starts one.
            await controller.ScanOrStopAsync("1", stop: false);
            Assert.Contains("POST /api/v2/scan", kit.Http.Requests);
        }
    }

    /// <summary>A second refresh Stop is not dropped while another job's cancel is pending; only a repeat for the same job waits (its button is disabled).</summary>
    [Fact]
    public async Task AnotherRefreshStopIsNotDroppedWhileOneCancelIsPending()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            server.Override = request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (!path.StartsWith("/api/v2/library-jobs/", StringComparison.Ordinal) || !path.EndsWith("/cancel", StringComparison.Ordinal)) return null;
                var id = path["/api/v2/library-jobs/".Length..^"/cancel".Length];
                if (id == "j1") gate.Task.Wait(TimeSpan.FromSeconds(5));
                return AdminMonitorKit.Json("{\"id\":\"" + id + "\",\"kind\":\"library_refresh\",\"state\":\"canceling\",\"terminal\":false,\"cancelable\":false,\"created_at\":\"2026-11-15T10:00:00Z\"}", HttpStatusCode.Accepted);
            };
            await controller.OpenAsync();
            var first = controller.CancelJobAsync("j1");
            await Until(() => controller.State.JobsCancelling.Contains("j1"));
            // The same job again is already being cancelled; a different job is not blocked by it.
            await controller.CancelJobAsync("j1");
            await controller.CancelJobAsync("j2");
            Assert.Equal(1, kit.Http.Requests.Count(line => line == "POST /api/v2/library-jobs/j1/cancel"));
            Assert.Equal(1, kit.Http.Requests.Count(line => line == "POST /api/v2/library-jobs/j2/cancel"));
            Assert.True(controller.State.JobsCancelling.Contains("j1") && !controller.State.JobsCancelling.Contains("j2"));
            gate.SetResult();
            await first;
            Assert.Empty(controller.State.JobsCancelling);
        }
    }
}
