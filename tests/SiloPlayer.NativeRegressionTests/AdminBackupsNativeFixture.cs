using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>
/// Production Configure > Server > Backups registration and Frame entry for all three routes, on a
/// synthetic server only. OS pickers are replaced by page seams confined to this fixture's result
/// directory; signed objects come from a separate fixture transport that records request headers.
/// </summary>
internal static class AdminBackupsNativeFixture
{
    private const string BackupId = "20261115T040000Z-nightly";
    private const string UploadId = "0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b";
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TestSha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
    private static readonly List<string> Failures = [];

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }

    private static async Task Until(Func<bool> condition, string label, int milliseconds = 8000)
    {
        for (var i = 0; i < milliseconds / 25 && !condition(); i++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly List<string> Requests = [];
        internal readonly Dictionary<string, string> Values = new()
        {
            ["backup.enabled"] = "true", ["backup.time"] = "04:00", ["backup.keep"] = "7", ["backup.include_intro_fingerprints"] = "false",
            ["backup.include_subtitles"] = "true", ["backup.destination"] = "default", ["backup.s3.endpoint"] = "", ["backup.s3.region"] = "",
            ["backup.s3.bucket"] = "", ["backup.s3.key_prefix"] = "", ["backup.s3.access_key"] = "", ["backup.s3.path_style"] = "false", ["s3.private_bucket"] = "",
        };
        internal bool Encrypted, KeyConfirmed = true, Unauthenticated, JobTerminal;
        internal int Version = 1, Parts, JobReadStatus;
        internal bool PauseUpload, PauseComplete, Checking;
        internal readonly TaskCompletionSource UploadPartStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CompleteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? LastPut, LastIfMatch, LastRestoreBody;
        private int _received;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests) Requests.Add(request.Method + " " + request.RequestUri.PathAndQuery);
            if (request.Method == HttpMethod.Put && path == "/api/v2/admin/settings")
            {
                LastIfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                LastPut = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var data = JsonDocument.Parse(LastPut);
                var updated = new Dictionary<string, string>();
                foreach (var entry in data.RootElement.GetProperty("values").EnumerateObject())
                    if (entry.Name != "backup.s3.secret_key") updated[entry.Name] = Values[entry.Name] = entry.Value.GetString()!;
                Version++;
                return Json(JsonSerializer.Serialize(new { values = updated, restart_required = false }), etag: true);
            }
            if (request.Method == HttpMethod.Put && path.Contains("/parts/", StringComparison.Ordinal))
            {
                if (PauseUpload)
                {
                    UploadPartStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                Parts++; _received++;
                return Json(Upload(_received >= 3 ? "uploading" : "uploading", _received));
            }
            if (request.Method == HttpMethod.Post)
            {
                switch (path)
                {
                    case "/api/v2/admin/backups":
                        return Json("""{"id":"20261116T120000Z-manual","kind":"manual","status":"in_progress"}""", HttpStatusCode.Accepted);
                    case "/api/v2/admin/backups/s3/acknowledge-key":
                        KeyConfirmed = true;
                        return Json("""{"key_check":"abcd-ef01-2345-6789"}""");
                    case "/api/v2/admin/backups/uploads":
                        _received = 0;
                        return Json(Upload("uploading", 0), HttpStatusCode.Created);
                    case "/api/v2/admin/backups/" + BackupId + "/download-links":
                        return Json($$"""{"items":[{"name":"db.dump","size_bytes":4,"url":"/api/v2/admin/backups/{{BackupId}}/objects/db.dump?exp=1999999999&sig=fixture","expires_at":"{{DateTimeOffset.UtcNow.AddMinutes(15):O}}"}]}""");
                    case "/api/v2/admin/backups/" + BackupId + "/restore":
                        LastRestoreBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                        return Json(Job("queued", false), HttpStatusCode.Accepted, "/api/v2/admin/jobs/job-1");
                    case "/api/v2/admin/jobs/job-1/cancel":
                        return Json(Job("canceling", false), HttpStatusCode.Accepted);
                }
                if (path.EndsWith("/complete", StringComparison.Ordinal))
                {
                    if (PauseComplete)
                    {
                        // Like the official server, the check outlives the request that started it.
                        Checking = true;
                        CompleteStarted.TrySetResult();
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    return Json(Upload("complete", 3, ",\"digest\":\"" + Digest + "\",\"source\":{\"server_id\":\"s-2\",\"server_name\":\"Den\",\"silo_version\":\"1.0.0\",\"created_at\":\"2026-11-15T04:00:00.000Z\",\"format_version\":1}"));
                }
            }
            if (request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (path.EndsWith("/restore-set", StringComparison.Ordinal))
            {
                var upload = path.Contains("/uploads/", StringComparison.Ordinal);
                if ((Unauthenticated || upload) && !request.RequestUri.Query.Contains("accept_unauthenticated=true"))
                    return Json("""{"type":"https://silo.dev/problems/backup_unauthenticated","title":"Backup not authenticated","status":409,"detail":"Not verified."}""", HttpStatusCode.Conflict);
                var source = upload ? "\"upload_id\":\"" + UploadId + "\"" : "\"backup_id\":\"" + BackupId + "\"";
                return Json($$"""{{{source}},"digest":"{{Digest}}","format_version":1,"server_id":"server-1","server_name":"Synthetic","silo_version":"1.0.0","created_at":"2026-11-15T04:00:00.000Z","same_server":true,"key_matches":true,"authenticated":{{(upload || Unauthenticated ? "false" : "true")}},"missing_blobs":[],"omitted":[],"never_restored_settings":["server.id"],"library_map":[],"sections":[{"id":"settings","count":1,"items":[{"id":"general","kind":"settings_page","label":"General","status":"differs","selectable":true,"requires":[],"restart_required":false,"external_service":false,"missing_blobs":[],"settings":[{"key":"server.name","class":"portable","status":"differs","masked":false,"backup_value":"Old","current_value":"New","restart_required":false}]}]}]}""");
            }
            return path switch
            {
                "/api/v2/admin/backups/capabilities" => Json("""{"state":"available","allowed":true,"destinations":["default","s3"],"encryption":true,"upload":true,"max_upload_bytes":2147483648,"selective_restore":true,"download":true,"revision":"r1"}"""),
                "/api/v2/admin/backups" => Json(List()),
                "/api/v2/admin/backups/uploads" => Json(Checking ? "{\"items\":[" + Upload("checking", 3) + "]}" : "{\"items\":[]}"),
                "/api/v2/admin/tasks/backup" => Json("""{"key":"backup","next_run_at":"2099-11-17T04:00:00.000Z"}"""),
                "/api/v2/admin/jobs" => Json("{\"items\":[]}"),
                "/api/v2/admin/jobs/job-1" => JobReadStatus == 0 ? Json(Job(JobTerminal ? "completed" : "running", JobTerminal)) : Json("{}", (HttpStatusCode)JobReadStatus),
                "/api/v2/libraries" => Json("{\"items\":[]}"),
                "/api/v2/admin/settings/effective" => Json(JsonSerializer.Serialize(Values), etag: true),
                "/api/v2/admin/settings/sensitive-status" => Json("""{"configured":[],"managed_by_env":[]}"""),
                "/api/v2/admin/settings/restart-keys" => Json("""{"keys":[],"prefixes":[]}"""),
                "/api/v2/admin/server/status" => Json("""{"restart_required":false,"restart_mark_count":0,"restart_requested":false}"""),
                "/api/v2/admin/system/build" => Json("""{"display":"synthetic","available":true,"build_number":1}"""),
                _ => Json("{}", HttpStatusCode.NotFound),
            };
        }

        private string List() =>
            $$"""{"server_id":"server-1","destination":"default","destination_available":true,"encrypted":{{(Encrypted ? "true" : "false")}},"key_check":"abcd-ef01-2345-6789","key_confirmed":{{(KeyConfirmed ? "true" : "false")}},"enabled":true,"busy":false,"keep":7,"items":[{"id":"{{BackupId}}","kind":"nightly","status":"complete","created_at":"2026-11-15T04:00:00.000Z","size_bytes":4,"location":"/data/backups/{{BackupId}}","blob_count":0,"missing_blobs":[],"missing_blobs_total":0,"restore_set_omitted":[],"objects":[{"name":"db.dump","size_bytes":4,"sha256":"{{TestSha}}"},{"name":"restore-set.tar","size_bytes":4,"sha256":"{{TestSha}}"}],"encrypted":false,"key_matches":true,"clean":true,"silo_version":"1.0.0"}],"other_server_ids":[],"warnings":[{{(Encrypted && !KeyConfirmed ? "{\"code\":\"key_not_confirmed\",\"message\":\"Paused.\"}" : "")}}],"last_success_at":"2026-11-15T04:10:00.000Z"}""";

        private static string Upload(string status, int received, string extra = "") =>
            $$"""{"id":"{{UploadId}}","status":"{{status}}","size_bytes":3145728,"part_size":1048576,"part_count":3,"received_parts":{{received}},"received_bytes":{{received * 1048576}},"created_at":"2026-11-15T04:00:00.000Z","updated_at":"2026-11-15T04:00:00.000Z","location":"uploads/x"{{extra}}}""";

        private static string Job(string state, bool terminal) =>
            $$"""{"id":"job-1","kind":"backup_restore","state":"{{state}}","terminal":{{(terminal ? "true" : "false")}},"cancelable":{{(terminal ? "false" : "true")}},"created_at":"2026-11-16T12:00:00.000Z","library_ids":[],"artifact_size_bytes":0,"public_link_supported":false,"backup_restore_source":{"backup_id":"{{BackupId}}"}{{(terminal ? ",\"finished_at\":\"2026-11-16T12:01:00.000Z\",\"backup_restore_result\":{\"backup_id\":\"" + BackupId + "\",\"before_restore_backup_id\":\"20261116T120000Z-before-restore\",\"library_map\":[],\"items\":[{\"section\":\"settings\",\"id\":\"general\",\"kind\":\"settings_page\",\"label\":\"General\",\"outcome\":\"restored\",\"restart_required\":true,\"restart_keys\":[\"server.port\"],\"notes\":[],\"missing_blobs\":[]}],\"counts\":{\"restored\":1,\"unchanged\":0,\"left_out\":0,\"failed\":0},\"interrupted\":false}" : "")}}}""";

        private HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK, string? location = null, bool etag = false)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, (int)status >= 400 ? "application/problem+json" : "application/json") };
            if (location != null) response.Headers.Location = new Uri(location, UriKind.Relative);
            if (etag) response.Headers.ETag = new($"\"backups-fixture-v{Version}\"");
            return response;
        }
    }

    /// <summary>The signed-object transport: records headers so the fixture can prove no account credentials were sent.</summary>
    private sealed class ObjectServer : HttpMessageHandler
    {
        internal readonly List<HttpRequestMessage> Requests = [];
        internal bool Timeout;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Timeout) return Task.FromException<HttpResponseMessage>(new OperationCanceledException());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("test")) });
        }
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server();
        var objects = new ObjectServer();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        using var download = new AdminBackupSignedDownload(objects);
        var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(new ToastService())
            .AddSingleton(new SettingsService(Program.ResultDirectory)).AddSingleton(download);
        // The same registration entry point as App, not a test-only page.
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        var outputs = Path.Combine(Path.GetFullPath(Program.ResultDirectory), "backups-fixture");
        if (Directory.Exists(outputs)) throw new InvalidOperationException("Backups fixture output already exists.");
        Directory.CreateDirectory(outputs);
        var savePicker = typeof(AdminBackupsPage).GetField("PickSavePathOverride", BindingFlags.Static | BindingFlags.NonPublic)!;
        var uploadPicker = typeof(AdminBackupsPage).GetField("PickUploadOverride", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousSavePicker = savePicker.GetValue(null);
        var previousUploadPicker = uploadPicker.GetValue(null);
        Func<string, Task<string?>> pickSave = name => Task.FromResult<string?>(Path.Combine(outputs, Path.GetFileName(name)));
        Func<Task<(string Name, Stream Stream)?>> pickUpload = () =>
        {
            var bytes = new byte[3 << 20];
            Encoding.ASCII.GetBytes("restore-set.json").CopyTo(bytes, 0);
            Encoding.ASCII.GetBytes("ustar").CopyTo(bytes, 257);
            return Task.FromResult<(string, Stream)?>(("restore-set.tar", new MemoryStream(bytes, writable: false)));
        };
        Frame? outer = null;
        AdminBackupsPage? lastPage = null;
        try
        {
            savePicker.SetValue(null, pickSave);
            uploadPicker.SetValue(null, pickUpload);
            field.SetValue(null, services);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;

            // ── List route ─────────────────────────────────────────
            var page = await OpenAsync(outer, "/admin/backups?backup=" + BackupId);
            lastPage = page;
            var controller = page.Controller!;
            await Until(() => controller.State.List != null && controller.State.SettingsLoaded, "list and settings loaded");
            await Until(() => Find<Button>(page, "BackupsBackUpNow") is { IsLoaded: true, IsEnabled: true }, "Back up now rendered");
            Check(true, "Backups opens through production registration, shell and Frame");
            Check(!Descendants(outer).OfType<TextBlock>().Any(text => text.Text.StartsWith("web.", StringComparison.Ordinal) || text.Text.StartsWith("configure.", StringComparison.Ordinal)),
                "no rendered Backups text is a catalog key");
            Check(Descendants(page).OfType<TextBlock>().Any(text => text.Text == AdminText.T("web.admin.backups.header.title")), "official page title shown");
            Check(Find<FrameworkElement>(page, "BackupRow-" + BackupId) is { IsLoaded: true }, "saved backup row rendered");
            Check(Find<Button>(page, "BackupsUpload") is { IsEnabled: true }, "Upload is offered because the server reports upload capability");
            Check(Find<Button>(page, "BackupRestore-" + BackupId) is { IsEnabled: true }, "Restore… is offered for a backup with a restore set");
            Check(Find<TextBlock>(page, "BackupsRestoreCommand")?.Text == "silo backup restore --from /data/backups/" + BackupId, "full-restore command names the newest verified backup");

            // A linked row stays highlighted, but settings edits and refreshes must not reveal it again.
            await Task.Delay(200); // Let the initial linked-row reveal and layout drain.
            var repeatedReveals = 0;
            ((StackPanel)Field(page, "_body")!).BringIntoViewRequested += (_, args) =>
            {
                if (args.TargetElement is FrameworkElement target && AutomationProperties.GetAutomationId(target) == "BackupRow-" + BackupId) repeatedReveals++;
            };
            controller.Set("backup.time", "05:01");
            await controller.RefreshAsync();
            await Task.Delay(200);
            Check(repeatedReveals == 0 && Find<Grid>(page, "BackupRow-" + BackupId)?.Background != null,
                "linked backup remains highlighted without repeated scrolling on settings edits or refreshes");
            controller.Discard();

            // Back up now: sent once.
            Invoke(Find<Button>(page, "BackupsBackUpNow")!);
            await Until(() => server.Requests.Count(request => request == "POST /api/v2/admin/backups") == 1, "back up now sent");
            await Task.Delay(200);
            Check(server.Requests.Count(request => request == "POST /api/v2/admin/backups") == 1, "Back up now is sent exactly once");

            // Delete: confirmation required, then DELETE.
            await Until(() => Find<Button>(page, "BackupDelete-" + BackupId) is { IsEnabled: true, IsLoaded: true }, "delete enabled");
            Invoke(Find<Button>(page, "BackupDelete-" + BackupId)!);
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "delete dialog");
            Check(!server.Requests.Any(request => request.StartsWith("DELETE", StringComparison.Ordinal)), "nothing is deleted before confirmation");
            var deleteDialog = (ContentDialog)Field(page, "_dialog")!;
            Invoke(DialogButton(deleteDialog, AdminText.T("web.admin.backups.table.deleteConfirm")));
            await Until(() => Field(page, "_dialog") == null, "delete dialog closed");
            await Until(() => server.Requests.Contains("DELETE /api/v2/admin/backups/" + BackupId), "delete sent");
            Check(true, "a confirmed delete sends one DELETE");

            // Download: signed link, no account headers, verified and saved to the picked path.
            await Until(() => Find<Button>(page, "BackupDownload-" + BackupId) is { IsEnabled: true, IsLoaded: true }, "download enabled");
            Invoke(Find<Button>(page, "BackupDownload-" + BackupId)!);
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "download dialog");
            var downloadDialog = (ContentDialog)Field(page, "_dialog")!;
            await Until(() => Find<Button>(downloadDialog, "BackupsDownloadFile-db.dump") is { IsLoaded: true, IsEnabled: true }, "file button");
            Invoke(Find<Button>(downloadDialog, "BackupsDownloadFile-db.dump")!);
            var saved = Path.Combine(outputs, "db.dump");
            try
            {
                await Until(() => File.Exists(saved) && Find<TextBlock>(downloadDialog, "BackupsDownloadState-db.dump")?.Text == AdminText.T("configure.native.backups.download.savedVerified"), "verified save");
            }
            catch
            {
                Program.Log("INFO: download status=" + Find<TextBlock>(downloadDialog, "BackupsDownloadState-db.dump")?.Text
                    + "; bytes=" + (File.Exists(saved) ? new FileInfo(saved).Length : -1) + "; object requests=" + objects.Requests.Count);
                throw;
            }
            Check(File.ReadAllText(saved) == "test", "downloaded object saved with its verified bytes");
            await Task.Delay(100);
            Check(Find<TextBlock>(downloadDialog, "BackupsDownloadState-db.dump")?.Text == AdminText.T("configure.native.backups.download.savedVerified"),
                "late progress cannot overwrite the verified download result");
            var objectRequest = objects.Requests.Single();
            Check(objectRequest.Headers.Authorization == null && !objectRequest.Headers.Any(header => header.Key.StartsWith("X-Profile", StringComparison.OrdinalIgnoreCase)),
                "the signed object request carries no account or profile credentials");
            Check(objectRequest.RequestUri!.AbsolutePath == "/api/v2/admin/backups/" + BackupId + "/objects/db.dump", "the object came from this backup's signed path");
            objects.Timeout = true;
            await Until(() => Find<Button>(downloadDialog, "BackupsDownloadFile-db.dump") is { IsEnabled: true }, "download can be retried");
            Invoke(Find<Button>(downloadDialog, "BackupsDownloadFile-db.dump")!);
            await Until(() => Find<TextBlock>(downloadDialog, "BackupsDownloadState-db.dump")?.Text == AdminText.T("configure.native.backups.download.timeout"), "download timeout explained");
            await Task.Delay(100);
            Check(File.ReadAllText(saved) == "test" && Find<TextBlock>(downloadDialog, "BackupsDownloadState-db.dump")?.Text == AdminText.T("configure.native.backups.download.timeout"),
                "download deadline shows a terminal error and preserves the existing destination");
            objects.Timeout = false;
            Invoke(Find<Button>(downloadDialog, "BackupsDownloadClose")!);
            await Until(() => Field(page, "_dialog") == null, "download dialog closed");

            // Settings: invalid keep blocks Save; a valid edit saves with the strong validator.
            var keep = Find<TextBox>(page, "BackupsKeep")!;
            keep.Text = "61";
            await Until(() => controller.State.Errors.ContainsKey("backup.keep"), "keep error");
            await Task.Delay(120);
            Check(Find<Button>(page, "BackupsSave") is { IsEnabled: false }, "keep outside 1–60 blocks Save");
            keep.Text = "14";
            await Until(() => controller.State.Errors.Count == 0 && Find<Button>(page, "BackupsSave") is { IsEnabled: true }, "save enabled");
            Invoke(Find<Button>(page, "BackupsSave")!);
            await Until(() => server.LastPut != null && controller.State.DirtyKeys.Count == 0, "settings saved");
            Check(server.LastPut!.Contains("\"backup.keep\":\"14\"", StringComparison.Ordinal) && server.LastIfMatch == "\"backups-fixture-v1\"", "Save sends the edited key under the read validator");

            // Key confirmation when encrypted backups are paused.
            server.Encrypted = true; server.KeyConfirmed = false;
            await controller.RefreshAsync();
            await Until(() => Find<Button>(page, "BackupsConfirmKeyWarning") is { IsLoaded: true }, "confirm key offered");
            Invoke(Find<Button>(page, "BackupsConfirmKeyWarning")!);
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "key dialog");
            var keyDialog = (ContentDialog)Field(page, "_dialog")!;
            await Until(() => Find<CheckBox>(keyDialog, "BackupsKeyKept") is { IsLoaded: true }, "kept box");
            Check(Find<Button>(keyDialog, "BackupsConfirmKeySubmit") is { IsEnabled: false }, "confirmation needs the explicit 'I keep a copy' box");
            Check(Find<TextBlock>(keyDialog, "BackupsKeyCheck")?.Text == "abcd-ef01-2345-6789", "the dialog shows the server's key check");
            Toggle(Find<CheckBox>(keyDialog, "BackupsKeyKept")!);
            await Until(() => Find<Button>(keyDialog, "BackupsConfirmKeySubmit") is { IsEnabled: true }, "submit enabled");
            Invoke(Find<Button>(keyDialog, "BackupsConfirmKeySubmit")!);
            await Until(() => Field(page, "_dialog") == null, "key dialog closed");
            Check(server.Requests.Contains("POST /api/v2/admin/backups/s3/acknowledge-key"), "the key is acknowledged once confirmed");

            // Cancelling the OS picker must leave a pointer-accessible way out of the chooser.
            var pickerReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<Task<(string Name, Stream Stream)?>> cancelPicker = () =>
            {
                pickerReturned.TrySetResult();
                return Task.FromResult<(string, Stream)?>(null);
            };
            uploadPicker.SetValue(null, cancelPicker);
            var createsBeforeCancel = server.Requests.Count(request => request == "POST /api/v2/admin/backups/uploads");
            Invoke(Find<Button>(page, "BackupsUpload")!);
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "upload chooser dialog");
            var chooserDialog = (ContentDialog)Field(page, "_dialog")!;
            await Until(() => Find<Button>(chooserDialog, "BackupsUploadChoose") is { IsLoaded: true, IsEnabled: true }, "upload chooser ready");
            Invoke(Find<Button>(chooserDialog, "BackupsUploadChoose")!);
            await Until(() => pickerReturned.Task.IsCompleted, "cancelled picker returned");
            await Until(() => Find<Button>(chooserDialog, "BackupsUploadCancel") is { IsLoaded: true, IsEnabled: true }, "chooser Cancel available after picker cancellation");
            Invoke(Find<Button>(chooserDialog, "BackupsUploadCancel")!);
            await Until(() => Field(page, "_dialog") == null && Field(page, "_upload") == null, "chooser dismissed and upload session retired");
            Check(server.Requests.Count(request => request == "POST /api/v2/admin/backups/uploads") == createsBeforeCancel,
                "Upload can be dismissed by its Cancel button after cancelling the picker without creating an upload");
            uploadPicker.SetValue(null, pickUpload);

            // Upload navigation must wait for dialog cleanup so a dirty draft can ask to discard.
            controller.Set("backup.time", "05:02");
            await Until(() => controller.State.DirtyKeys.Contains("backup.time"), "dirty settings before upload");
            Invoke(Find<Button>(page, "BackupsUpload")!);
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "upload dialog");
            var uploadDialog = (ContentDialog)Field(page, "_dialog")!;
            await Until(() => Find<Button>(uploadDialog, "BackupsUploadChoose") is { IsLoaded: true }, "choose button");
            var createsBeforeSelection = server.Requests.Count(request => request == "POST /api/v2/admin/backups/uploads");
            Func<Task<(string Name, Stream Stream)?>> unreadable = () => throw new IOException("Synthetic picker/file-open failure");
            uploadPicker.SetValue(null, unreadable);
            Invoke(Find<Button>(uploadDialog, "BackupsUploadChoose")!);
            await Until(() => Descendants(uploadDialog).OfType<TextBlock>().Any(text => text.Text == AdminText.T("configure.native.backups.fileUnreadable")), "unreadable selection explained");
            Check(server.Requests.Count(request => request == "POST /api/v2/admin/backups/uploads") == createsBeforeSelection
                && Find<Button>(uploadDialog, "BackupsUploadChoose") is { IsEnabled: true }, "picker/file-open failure is recoverable without a server upload");
            uploadPicker.SetValue(null, pickUpload);
            Invoke(Find<Button>(uploadDialog, "BackupsUploadChoose")!);
            await Until(() => Find<Button>(uploadDialog, "BackupsUploadChooseWhat") is { IsLoaded: true, IsEnabled: true }, "upload opened");
            Check(server.Parts == 3 && server.Requests.Count(request => request == "POST /api/v2/admin/backups/uploads") == 1, "three parts sent for one created upload");
            Invoke(Find<Button>(uploadDialog, "BackupsUploadChooseWhat")!);
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true } dialog && !ReferenceEquals(dialog, uploadDialog), "discard confirmation after upload closes");
            var discardDialog = (ContentDialog)Field(page, "_dialog")!;
            Check(controller.State.DirtyKeys.Contains("backup.time"), "upload navigation keeps the dirty draft until discard is confirmed");
            Invoke(DialogButton(discardDialog, AdminText.T("web.shell.unsavedChanges.discard")));
            var uploadPage = await WaitForPageAsync(outer, page);
            lastPage = uploadPage;
            await Until(() => uploadPage.RestoreController?.State.Unauthenticated == true, "upload preview needs consent");
            Check(!server.Requests.Any(request => request.Contains("/uploads/" + UploadId + "/restore-set?accept_unauthenticated=true", StringComparison.Ordinal)),
                "an upload's preview is not opened without consent");
            await Until(() => Find<Button>(uploadPage, "RestoreOpenAnyway") is { IsLoaded: true }, "open anyway");
            Invoke(Find<Button>(uploadPage, "RestoreOpenAnyway")!);
            await Until(() => uploadPage.RestoreController?.State.Preview != null, "consented preview");
            Check(true, "Open anyway reads the upload with explicit consent");

            // ── Backup restore route: choose, review, restore, stop, report ──
            var restorePage = await OpenAsync(outer, "/admin/backups/" + BackupId + "/restore");
            lastPage = restorePage;
            var restore = restorePage.RestoreController!;
            await Until(() => restore.State.Preview != null, "backup preview");
            await Until(() => Find<CheckBox>(restorePage, "RestoreItem-settings-general") is { IsLoaded: true, IsEnabled: true }, "item rendered");
            Check(Find<FrameworkElement>(restorePage, "RestoreStep-0") != null, "the step indicator shows Choose, Review and Restore");
            Toggle(Find<CheckBox>(restorePage, "RestoreItem-settings-general")!);
            await Until(() => restore.Selection.Contains("settings:general"), "item chosen");
            await Until(() => Find<Button>(restorePage, "RestoreReview") is { IsEnabled: true, IsLoaded: true }, "review enabled");
            Invoke(Find<Button>(restorePage, "RestoreReview")!);
            await Until(() => Find<Button>(restorePage, "RestoreStart") is { IsLoaded: true, IsEnabled: true }, "restore enabled");
            Invoke(Find<Button>(restorePage, "RestoreStart")!);
            await Until(() => restore.State.Job != null, "job shown");
            Check(server.LastRestoreBody?.Contains("\"digest\":\"" + Digest + "\"", StringComparison.Ordinal) == true &&
                  server.LastRestoreBody.Contains("\"settings\":[\"general\"]", StringComparison.Ordinal), "the reviewed digest and selection are sent");
            Check(server.Requests.Count(request => request == "POST /api/v2/admin/backups/" + BackupId + "/restore") == 1, "the restore is sent once");
            server.JobReadStatus = 404;
            await restore.RetryJobAsync();
            await Until(() => Find<Button>(restorePage, "RestoreJobRetry") is { IsLoaded: true, IsEnabled: true }, "retained job retry shown");
            Check(restore.State.Job != null && Descendants(restorePage).OfType<TextBlock>().Any(text => text.Text == AdminText.T("configure.native.backups.restore.staleJob")),
                "failed monitoring labels retained progress as stale and offers Retry");
            server.JobReadStatus = 0;
            Invoke(Find<Button>(restorePage, "RestoreJobRetry")!);
            await Until(() => restore.State.JobError == null, "job retry recovered");
            await Until(() => Find<Button>(restorePage, "RestoreStop") is { IsLoaded: true, IsEnabled: true }, "stop enabled");
            Invoke(Find<Button>(restorePage, "RestoreStop")!);
            await Until(() => Field(restorePage, "_dialog") is ContentDialog { IsLoaded: true }, "stop dialog");
            Check(!server.Requests.Any(request => request.EndsWith("/cancel", StringComparison.Ordinal)), "nothing stops before confirmation");
            Invoke(DialogButton((ContentDialog)Field(restorePage, "_dialog")!, AdminText.T("web.admin.backups.restore.run.stop")));
            await Until(() => server.Requests.Contains("POST /api/v2/admin/jobs/job-1/cancel"), "cancel sent");
            server.JobTerminal = true;
            await Until(() => restore.State.Job?.Terminal == true, "terminal job", 15000);
            await Until(() => Find<TextBlock>(restorePage, "RestoreReportHeadline") is { IsLoaded: true }, "report rendered");
            Check(Find<FrameworkElement>(restorePage, "RestoreRecoveryPoint") != null, "the report names its recovery point");
            Check(Descendants(restorePage).OfType<TextBlock>().Any(text => text.Text.Contains("server.port", StringComparison.Ordinal)), "restart keys from the report are listed");

            var cleanupPage = await OpenAsync(outer, "/admin/backups");
            lastPage = cleanupPage;
            await Until(() => cleanupPage.Controller?.State.List != null
                && Find<Button>(cleanupPage, "BackupsUpload") is { IsLoaded: true, IsEnabled: true }, "cleanup upload button ready");
            // Closing while the server checks needs a pointer-accessible button and keeps the upload being checked.
            server.PauseComplete = true;
            var deletesBeforeChecking = server.Requests.Count(request => request == "DELETE /api/v2/admin/backups/uploads/" + UploadId);
            Invoke(Find<Button>(cleanupPage, "BackupsUpload")!);
            await Until(() => Field(cleanupPage, "_dialog") is ContentDialog { IsLoaded: true }, "checking upload dialog");
            var checkingDialog = (ContentDialog)Field(cleanupPage, "_dialog")!;
            await Until(() => Find<Button>(checkingDialog, "BackupsUploadChoose") is { IsLoaded: true, IsEnabled: true }, "checking upload choice");
            Invoke(Find<Button>(checkingDialog, "BackupsUploadChoose")!);
            await server.CompleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => Find<Button>(checkingDialog, "BackupsUploadClose") is { IsLoaded: true, IsEnabled: true }, "Close available while the upload is checked");
            Invoke(Find<Button>(checkingDialog, "BackupsUploadClose")!);
            await Until(() => Field(cleanupPage, "_dialog") == null && Field(cleanupPage, "_upload") == null, "checking dialog dismissed and upload session retired");
            await Until(() => Find<FrameworkElement>(cleanupPage, "UploadRow-" + UploadId) is { IsLoaded: true }, "upload being checked listed");
            await Task.Delay(200);
            Check(server.Requests.Count(request => request == "DELETE /api/v2/admin/backups/uploads/" + UploadId) == deletesBeforeChecking
                && Find<FrameworkElement>(cleanupPage, "UploadRow-" + UploadId) != null,
                "Upload can be dismissed by its Close button while it is checked, and the upload being checked stays listed");
            server.PauseComplete = false; server.Checking = false;
            await cleanupPage.Controller!.RefreshUploadsAsync();
            await Until(() => Find<FrameworkElement>(cleanupPage, "UploadRow-" + UploadId) == null
                && Find<Button>(cleanupPage, "BackupsUpload") is { IsLoaded: true, IsEnabled: true }, "upload button ready after the checked upload");

            server.PauseUpload = true;
            var deletes = server.Requests.Count(request => request == "DELETE /api/v2/admin/backups/uploads/" + UploadId);
            Invoke(Find<Button>(cleanupPage, "BackupsUpload")!);
            await Until(() => Field(cleanupPage, "_dialog") is ContentDialog { IsLoaded: true }, "cleanup upload dialog");
            var cleanupDialog = (ContentDialog)Field(cleanupPage, "_dialog")!;
            await Until(() => Find<Button>(cleanupDialog, "BackupsUploadChoose") is { IsLoaded: true, IsEnabled: true }, "cleanup upload choice");
            Invoke(Find<Button>(cleanupDialog, "BackupsUploadChoose")!);
            await server.UploadPartStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var cleanupClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cleanupDialog.Closed += (_, _) => cleanupClosed.TrySetResult();
            cleanupPage.SuspendAdminPage();
            await cleanupClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => server.Requests.Count(request => request == "DELETE /api/v2/admin/backups/uploads/" + UploadId) == deletes + 1, "departed upload deleted");
            Check(true, "page departure deletes its known unfinished upload under the same authority without a replay");
        }
        finally
        {
            if (lastPage != null && Field(lastPage, "_dialog") is ContentDialog open)
            {
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                open.Closed += (_, _) => closed.TrySetResult();
                open.Hide();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            }
            var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (lastPage is { IsLoaded: true }) lastPage.Unloaded += (_, _) => unloaded.TrySetResult(); else unloaded.TrySetResult();
            var shellUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (outer?.Content is AdminShellPage { IsLoaded: true } shell) shell.Unloaded += (_, _) => shellUnloaded.TrySetResult(); else shellUnloaded.TrySetResult();
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            window.Content = previousContent;
            await Task.WhenAll(unloaded.Task, shellUnloaded.Task).WaitAsync(TimeSpan.FromSeconds(3));
            savePicker.SetValue(null, previousSavePicker);
            uploadPicker.SetValue(null, previousUploadPicker);
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Backups fixture failures: " + string.Join("; ", Failures));
    }

    private static async Task<AdminBackupsPage> OpenAsync(Frame outer, string href)
    {
        var resolved = AdminShellPage.TryOpenHref(href);
        if (resolved is not { Kind: AdminRouteResolutionKind.Route }) throw new InvalidOperationException(href + " is not available through the production registration.");
        await Until(() => outer.Content is AdminShellPage, "shell for " + href);
        var shell = (AdminShellPage)outer.Content;
        var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        await Until(() => inner.Content is AdminBackupsPage { IsLoaded: true } page && (page.Controller != null || page.RestoreController != null)
            && ((AdminRouteRequest?)typeof(AdminBackupsPage).GetField("_location", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page))?.Href.Split('?')[0] == href.Split('?')[0],
            "page for " + href);
        return (AdminBackupsPage)inner.Content;
    }

    private static async Task<AdminBackupsPage> WaitForPageAsync(Frame outer, AdminBackupsPage previous)
    {
        var shell = (AdminShellPage)outer.Content;
        var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        await Until(() => inner.Content is AdminBackupsPage { IsLoaded: true } page && !ReferenceEquals(page, previous) && page.IsRestoreView, "next Backups page");
        return (AdminBackupsPage)inner.Content;
    }

    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static T? Find<T>(DependencyObject root, string id) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
    private static Button DialogButton(ContentDialog dialog, string label) => Descendants(dialog).OfType<Button>().First(button => button.Content?.ToString() == label || AutomationProperties.GetName(button) == label);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Toggle(CheckBox box) => ((IToggleProvider)new CheckBoxAutomationPeer(box).GetPattern(PatternInterface.Toggle)).Toggle();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
