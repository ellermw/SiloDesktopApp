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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices.WindowsRuntime;
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
        internal bool Encrypted, KeyConfirmed = true, Unauthenticated, JobTerminal, StaleRestoreList;
        /// <summary>A restore preview with change codes, hostile names and an account labeled by its email.</summary>
        internal bool Rich;
        /// <summary>The list answers with no backups (Empty) or because the destination cannot be read (DestinationDown).</summary>
        internal bool Empty, DestinationDown;
        /// <summary>Answers the list with this status instead of the list while it is not 0; the time the backup task's daily trigger holds.</summary>
        internal int ListStatus;
        internal string TriggerTime = "04:00";
        /// <summary>What a restore start gets: ok, refuse (422) or drop (the connection fails after the request left).</summary>
        internal string RestoreMode = "ok";
        /// <summary>Requests (as "METHOD /path") that wait here before they are answered, so a test can press a control again while the first is on the wire.</summary>
        internal readonly Dictionary<string, TaskCompletionSource> Gates = new(StringComparer.Ordinal);
        internal int Version = 1, Parts, JobReadStatus;
        internal bool PauseUpload, PauseComplete, Checking;
        internal string[] ManagedKeys = [];
        /// <summary>False models an older server that sends the managed list but no managed_by_env_vars map.</summary>
        internal bool NamedVariables = true;
        internal readonly TaskCompletionSource UploadPartStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CompleteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? LastPut, LastIfMatch, LastRestoreBody;
        private int _received;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests) Requests.Add(request.Method + " " + request.RequestUri.PathAndQuery);
            TaskCompletionSource? gate;
            lock (Gates) Gates.TryGetValue(request.Method + " " + path, out gate);
            if (gate != null) await gate.Task.WaitAsync(cancellationToken);
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
                        if (RestoreMode == "refuse")
                            return Json("""{"type":"https://silo.dev/problems/validation_failed","title":"Validation failed","status":422,"detail":"A chosen item can't be restored right now."}""", HttpStatusCode.UnprocessableEntity);
                        if (RestoreMode == "busy")
                            return Json("""{"type":"https://silo.dev/problems/conflict","title":"Conflict","status":409,"detail":"A restore is already running."}""", HttpStatusCode.Conflict, "/api/v2/admin/jobs/job-1");
                        if (RestoreMode == "drop") throw new HttpRequestException("Synthetic transport failure after the request was sent.");
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
                if (Rich && !upload) return Json(RichSet());
                var source = upload ? "\"upload_id\":\"" + UploadId + "\"" : "\"backup_id\":\"" + BackupId + "\"";
                return Json($$"""{{{source}},"digest":"{{Digest}}","format_version":1,"server_id":"server-1","server_name":"Synthetic","silo_version":"1.0.0","created_at":"2026-11-15T04:00:00.000Z","same_server":true,"key_matches":true,"authenticated":{{(upload || Unauthenticated ? "false" : "true")}},"missing_blobs":[],"omitted":[],"never_restored_settings":["server.id"],"library_map":[],"sections":[{"id":"settings","count":1,"items":[{"id":"general","kind":"settings_page","label":"General","status":"differs","selectable":true,"requires":[],"restart_required":false,"external_service":false,"missing_blobs":[],"settings":[{"key":"server.name","class":"portable","status":"differs","masked":false,"backup_value":"Old","current_value":"New","restart_required":false}]}]}]}""");
            }
            return path switch
            {
                "/api/v2/admin/backups/capabilities" => Json("""{"state":"available","allowed":true,"destinations":["default","s3"],"encryption":true,"upload":true,"max_upload_bytes":2147483648,"selective_restore":true,"download":true,"revision":"r1"}"""),
                "/api/v2/admin/backups" => ListStatus != 0 ? Json("{}", (HttpStatusCode)ListStatus) : Json(List()),
                "/api/v2/admin/backups/uploads" => Json(Checking ? "{\"items\":[" + Upload("checking", 3) + "]}" : "{\"items\":[]}"),
                "/api/v2/admin/tasks/backup" => Json("{\"key\":\"backup\",\"next_run_at\":\"2099-11-17T04:00:00.000Z\",\"triggers\":[{\"type\":\"daily\",\"time_of_day\":\"" + TriggerTime + "\"}]}"),
                "/api/v2/admin/jobs" => Json(StaleRestoreList ? "{\"items\":[" + Job("running", false) + "]}" : "{\"items\":[]}"),
                "/api/v2/admin/jobs/job-1" => JobReadStatus == 0 ? Json(Job(JobTerminal ? "completed" : "running", JobTerminal)) : Json("{}", (HttpStatusCode)JobReadStatus),
                "/api/v2/libraries" => Json("{\"items\":[]}"),
                "/api/v2/admin/settings/effective" => Json(JsonSerializer.Serialize(Values), etag: true),
                "/api/v2/admin/settings/sensitive-status" => Json(JsonSerializer.Serialize(ManagedKeys.Length > 0 && NamedVariables
                    ? new { configured = Array.Empty<string>(), managed_by_env = ManagedKeys, managed_by_env_vars = ManagedKeys.ToDictionary(key => key, key => "SILO_" + key.ToUpperInvariant().Replace('.', '_')) }
                    : (object)new { configured = Array.Empty<string>(), managed_by_env = ManagedKeys })),
                "/api/v2/admin/settings/restart-keys" => Json("""{"keys":[],"prefixes":[]}"""),
                "/api/v2/admin/server/status" => Json("""{"restart_required":false,"restart_mark_count":0,"restart_requested":false}"""),
                "/api/v2/admin/system/build" => Json("""{"display":"synthetic","available":true,"build_number":1}"""),
                _ => Json("{}", HttpStatusCode.NotFound),
            };
        }

        private string List() =>
            "{\"server_id\":\"server-1\",\"destination\":\"default\",\"destination_available\":" + (DestinationDown ? "false" : "true") + ",\"encrypted\":" + (Encrypted ? "true" : "false")
            + ",\"key_check\":\"abcd-ef01-2345-6789\",\"key_confirmed\":" + (KeyConfirmed ? "true" : "false") + ",\"enabled\":true,\"busy\":false,\"keep\":7,\"items\":["
            + (Empty || DestinationDown ? "" : ItemJson()) + "],\"other_server_ids\":[],\"warnings\":[" + WarningJson() + "],\"last_success_at\":\"2026-11-15T04:10:00.000Z\"}";

        private string WarningJson() => DestinationDown ? "{\"code\":\"destination_unavailable\",\"message\":\"The backup destination cannot be reached.\"}"
            : Encrypted && !KeyConfirmed ? "{\"code\":\"key_not_confirmed\",\"message\":\"Paused.\"}" : "";

        private string ItemJson() =>
            $$"""{"id":"{{BackupId}}","kind":"nightly","status":"complete","created_at":"2026-11-15T04:00:00.000Z","size_bytes":4,"location":"/data/backups/{{BackupId}}","blob_count":0,"missing_blobs":[],"missing_blobs_total":0,"restore_set_omitted":[],"objects":[{"name":"db.dump","size_bytes":4,"sha256":"{{TestSha}}"},{"name":"restore-set.tar","size_bytes":4,"sha256":"{{TestSha}}"}],"encrypted":false,"key_matches":true,"clean":true,"silo_version":"1.0.0"}""";

        /// <summary>Synthetic names and paths far longer than a restore row, so that the row has to cut them.</summary>
        internal const string LongName = "Quarterly archive of the family holiday recordings and every other long synthetic title that keeps going on and on";
        internal const string LongPath = "/mnt/synthetic/storage/volume-one/archive/media/movies/very/long/folder/name/that/does/not/end/anywhere/near/the/edge/of/the/row/at/all";
        internal const string LongEmail = "a.very.long.synthetic.address.for.a.long.named.account.that.keeps.going@mail.example.invalid";

        /// <summary>A name that tries to fake a line and reorder the text after it.</summary>
        internal static readonly string Hostile = "Movies\n" + char.ConvertFromUtf32(0x202E) + "evil" + char.ConvertFromUtf32(0x2029) + " tail";

        private static Dictionary<string, object?> Row(string id, string kind, string label, string status = "differs", params (string Key, object? Value)[] extra)
        {
            var row = new Dictionary<string, object?>
            {
                ["id"] = id, ["kind"] = kind, ["label"] = label, ["status"] = status, ["selectable"] = true, ["requires"] = Array.Empty<string>(),
                ["restart_required"] = false, ["external_service"] = false, ["missing_blobs"] = Array.Empty<string>(),
            };
            foreach (var (key, value) in extra) row[key] = value;
            return row;
        }

        private static Dictionary<string, object?> Part(string id, params Dictionary<string, object?>[] items) => new() { ["id"] = id, ["count"] = items.Length, ["items"] = items };

        private string RichSet()
        {
            Dictionary<string, object?> Template(string id, string[] changes) => Row(id, "template_collection", "Starter " + id, "differs", ("collection", new Dictionary<string, object?>
                { ["library_ids"] = Array.Empty<string>(), ["items"] = 0, ["missing_items"] = 0, ["unknown_items"] = 0, ["local_id"] = "3", ["template_id"] = "x", ["changes"] = changes }));
            var profile = new Dictionary<string, object?>
            {
                ["id"] = "p1", ["name"] = "Kid" + char.ConvertFromUtf32(0x2066) + "s\nroom", ["status"] = "not_on_this_server", ["selectable"] = true, ["match"] = "new", ["target"] = "new",
                ["is_primary"] = false, ["restricted"] = false, ["libraries_left_out"] = 0, ["no_library_access"] = true, ["has_pin"] = false, ["data_types"] = Array.Empty<object>(),
            };
            var account = Row("u1", "account", "a@example.test", "differs", ("account", new Dictionary<string, object?>
            {
                ["username"] = "a@example.test", ["email"] = "a@example.test", ["role"] = "user", ["admin"] = false, ["match"] = "username_email", ["target"] = "42", ["local_id"] = "42",
                ["local_username"] = "a@example.test", ["policy_differences"] = Array.Empty<object>(), ["libraries_left_out"] = 0, ["profile_room"] = 3, ["profiles"] = new[] { profile },
            }));
            Dictionary<string, object?> Library(string id, string name, string path) => Row(id, "library", name, "differs", ("library", new Dictionary<string, object?> { ["type"] = "movies", ["paths"] = new[] { path } }));
            var longAccount = Row("u2", "account", LongName, "not_on_this_server", ("account", new Dictionary<string, object?>
            {
                ["username"] = LongName, ["email"] = LongEmail, ["role"] = "user", ["admin"] = false, ["match"] = "new", ["target"] = "new",
                ["policy_differences"] = Array.Empty<object>(), ["libraries_left_out"] = 0, ["profile_room"] = 3, ["profiles"] = Array.Empty<object>(),
            }));
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["backup_id"] = BackupId, ["digest"] = Digest, ["format_version"] = 1, ["server_id"] = "server-1", ["server_name"] = "Synthetic", ["silo_version"] = "1.0.0",
                ["created_at"] = "2026-11-15T04:00:00.000Z", ["same_server"] = true, ["key_matches"] = true, ["authenticated"] = true, ["missing_blobs"] = Array.Empty<string>(),
                ["omitted"] = Array.Empty<object>(), ["never_restored_settings"] = new[] { "server.id" }, ["library_map"] = Array.Empty<object>(),
                ["sections"] = new object[]
                {
                    Part("libraries", Row("lib-1", "library", Hostile, "differs", ("missing_blobs", new[] { "m1" }), ("library", new Dictionary<string, object?> { ["type"] = "movies", ["paths"] = new[] { "/media/movies" } })),
                        Library("lib-long", LongName, LongPath), Library("lib-mixed", LongName, "/m"), Library("lib-short", "Short", "/m")),
                    Part("collections", Template("t1", ["title", "libraries", "future_code"]), Template("t2", ["sort order", "3 collection items"])),
                    Part("home_sections", Row("section:9", "home_section", "Row nine", "differs", ("home_section", new Dictionary<string, object?>
                        { ["scope"] = "home", ["section_type"] = "recently_released", ["local_id"] = "7", ["changes"] = new[] { "item_limit", "collection_items" }, ["missing_collection_items"] = 3 }))),
                    Part("users", account, longAccount),
                },
            });
        }

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
        App_Window = window;
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
            Check(outer.Content is AdminShellPage && page.IsLoaded && !page.IsRestoreView && controller.State.Owner is { IsCurrent: true }, "Backups opens through production registration, shell and Frame");
            Check(!Descendants(outer).OfType<TextBlock>().Any(text => text.Text.StartsWith("web.", StringComparison.Ordinal) || text.Text.StartsWith("configure.", StringComparison.Ordinal)),
                "no rendered Backups text is a catalog key");
            Check(Descendants(page).OfType<TextBlock>().Any(text => text.Text == AdminText.T("web.admin.backups.header.title")), "official page title shown");
            Check(Find<FrameworkElement>(page, "BackupRow-" + BackupId) is { IsLoaded: true }, "saved backup row rendered");
            Check(Find<Button>(page, "BackupsUpload") is { IsEnabled: true }, "Upload is offered because the server reports upload capability");
            Check(Find<Button>(page, "BackupRestore-" + BackupId) is { IsEnabled: true }, "Restore… is offered for a backup with a restore set");
            Check(Find<TextBlock>(page, "BackupsRestoreCommand")?.Text == "silo backup restore --from /data/backups/" + BackupId, "full-restore command names the newest verified backup");
            await CaptureWidths(outer, "list");

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

            // A draft for a key the environment takes over is dropped; the row is disabled, shows the official note and carries it in its help text.
            controller.Set("backup.time", "05:09");
            await Until(() => controller.State.DirtyKeys.Contains("backup.time"), "time draft before the environment takes over");
            server.ManagedKeys = ["backup.time"]; await controller.ReloadSettingsAsync(preserveDraft: true);
            var timeBox = Find<TextBox>(page, "BackupsTime")!;
            await Until(() => controller.State.ManagedByEnv.Contains("backup.time") && !timeBox.IsEnabled, "managed time locked");
            Check(!controller.State.DirtyKeys.Contains("backup.time") && timeBox.Text == "04:00", "the draft of the key that became managed is dropped");
            Check(EnvironmentNoteProbe.Verify(page, timeBox, EnvironmentNoteProbe.Named("SILO_BACKUP_TIME")) is null, "the managed Backups row is disabled, shows the official note naming the variable and carries it in its help text");
            Check(EnvironmentNoteProbe.VerifyEditable(Find<TextBox>(page, "BackupsKeep")!) is null, "an editable Backups row carries no environment note");
            server.NamedVariables = false; await controller.ReloadSettingsAsync(preserveDraft: true);
            await Until(() => EnvironmentNoteProbe.Verify(page, timeBox, EnvironmentNoteProbe.Unnamed) is null, "an older server that names no variable still locks the row and shows the official field message");
            server.NamedVariables = true; server.ManagedKeys = []; await controller.ReloadSettingsAsync(preserveDraft: false);
            await Until(() => controller.State.ManagedByEnv.Count == 0 && timeBox.IsEnabled, "managed lock released");

            // Back up now: sent once, also when it is activated again before the first request is answered (a double click, a held Enter key).
            var backupPosts = () => server.Requests.Count(request => request == "POST /api/v2/admin/backups");
            var backupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (server.Gates) server.Gates["POST /api/v2/admin/backups"] = backupGate;
            var backUpNow = Find<Button>(page, "BackupsBackUpNow")!;
            await PressAsync(backUpNow, Windows.System.VirtualKey.Enter, () => backupPosts() == 1, () => Invoke(backUpNow));
            await Until(() => backupPosts() == 1 && controller.State.StartingBackup, "back up now on the wire");
            HoldKey(Windows.System.VirtualKey.Enter, 6);
            if (Find<Button>(page, "BackupsBackUpNow") is { IsEnabled: true } again) Invoke(again);
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => controller.BackUpNowAsync()));
            var duringHold = backupPosts();
            lock (server.Gates) server.Gates.Remove("POST /api/v2/admin/backups");
            backupGate.TrySetResult();
            await Until(() => !controller.State.StartingBackup, "back up now answered");
            Check(duringHold == 1 && backupPosts() == 1, "Back up now is sent exactly once, however often it is activated before the first answer (posts " + duringHold + " while held, " + backupPosts() + " after)");

            // Delete: confirmation required, then DELETE.
            await Until(() => Find<Button>(page, "BackupDelete-" + BackupId) is { IsEnabled: true, IsLoaded: true }, "delete enabled");
            Invoke(Find<Button>(page, "BackupDelete-" + BackupId)!);
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "delete dialog");
            Check(!server.Requests.Any(request => request.StartsWith("DELETE", StringComparison.Ordinal)), "nothing is deleted before confirmation");
            var deleteDialog = (ContentDialog)Field(page, "_dialog")!;
            var deleteConfirm = DialogButton(deleteDialog, AdminText.T("web.admin.backups.table.deleteConfirm"));
            var ownStyle = typeof(AdminBackupsPage).GetMethod("DestructiveStyle", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, null);
            Check(ReferenceEquals(deleteConfirm.Style, ownStyle), "the delete confirmation uses the page's own destructive style");
            Check(RootColor(deleteConfirm, "Normal") == Windows.UI.Color.FromArgb(255, 0xDC, 0x26, 0x26) && RootColor(deleteConfirm, "PointerOver") == Windows.UI.Color.FromArgb(255, 0xB9, 0x1C, 0x1C)
                && RootColor(deleteConfirm, "Pressed") == Windows.UI.Color.FromArgb(255, 0x99, 0x1B, 0x1B), "the destructive button is #dc2626 and darkens on hover and pressed");
            VisualStateManager.GoToState(deleteConfirm, "Normal", false);
            var deleteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (server.Gates) server.Gates["DELETE /api/v2/admin/backups/" + BackupId] = deleteGate;
            Invoke(deleteConfirm);
            await Until(() => Field(page, "_dialog") == null, "delete dialog closed");
            var deletePosts = () => server.Requests.Count(request => request == "DELETE /api/v2/admin/backups/" + BackupId);
            await Until(() => deletePosts() == 1 && controller.State.DeletingBackupId == BackupId, "delete sent");
            // The same confirmed delete reached again before the first answer (a second click, a held Enter) is not sent again.
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => controller.DeleteBackupAsync(BackupId)));
            var deletesHeld = deletePosts();
            lock (server.Gates) server.Gates.Remove("DELETE /api/v2/admin/backups/" + BackupId);
            deleteGate.TrySetResult();
            await Until(() => controller.State.DeletingBackupId == null, "delete answered");
            Check(deletesHeld == 1 && deletePosts() == 1, "a confirmed delete sends one DELETE, however often it is reached before the answer (" + deletesHeld + " while held, " + deletePosts() + " after)");

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
            Check(server.Requests.Count(request => request.Contains("/uploads/" + UploadId + "/restore-set?accept_unauthenticated=true", StringComparison.Ordinal)) == 1
                && uploadPage.RestoreController.State.Preview is { Authenticated: not true }, "Open anyway reads the upload with explicit consent, once, and the preview stays unverified");

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
            await CaptureWidths(outer, "restore-run");
            Invoke(Find<Button>(restorePage, "RestoreStop")!);
            await Until(() => Field(restorePage, "_dialog") is ContentDialog { IsLoaded: true }, "stop dialog");
            Check(!server.Requests.Any(request => request.EndsWith("/cancel", StringComparison.Ordinal)), "nothing stops before confirmation");
            Invoke(DialogButton((ContentDialog)Field(restorePage, "_dialog")!, AdminText.T("web.admin.backups.restore.run.stop")));
            await Until(() => server.Requests.Contains("POST /api/v2/admin/jobs/job-1/cancel"), "cancel sent");
            server.JobTerminal = true;
            await Until(() => restore.State.Job?.Terminal == true, "terminal job", 15000);
            await Until(() => Find<TextBlock>(restorePage, "RestoreReportHeadline") is { IsLoaded: true }, "report rendered");
            await CaptureWidths(outer, "restore-report");
            Check(Find<FrameworkElement>(restorePage, "RestoreRecoveryPoint") != null, "the report names its recovery point");
            Check(Descendants(restorePage).OfType<TextBlock>().Any(text => text.Text.Contains("server.port", StringComparison.Ordinal)), "restart keys from the report are listed");
            // The report's buttons wrap inside the page on a narrow window instead of running past its edge (official #4989).
            foreach (var narrow in new[] { 560, 320 })
            {
                var widthBefore = restorePage.ActualWidth;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(narrow, 900));
                await Reaches(() => restorePage.ActualWidth < widthBefore - 1, 3000);
                string[] reportButtons = ["RestoreDownloadReport", "RestoreChooseAgain"];
                Windows.Foundation.Rect? Box(string id) => Find<Button>(restorePage, id) is { ActualWidth: > 0 } button
                    ? button.TransformToVisual(restorePage).TransformBounds(new Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight)) : null;
                bool Inside() { restorePage.UpdateLayout(); return reportButtons.All(id => Box(id) is { } box && box.Left >= -.5 && box.Right <= restorePage.ActualWidth + .5); }
                var inside = await Reaches(Inside, 3000);
                Program.Log("INFO: report buttons with the window " + narrow + " px wide: page " + restorePage.ActualWidth.ToString("F0") + " px; "
                    + string.Join("; ", reportButtons.Select(id => id + " " + (Box(id) is { } box ? "x " + box.Left.ToString("F0") + ".." + box.Right.ToString("F0") + ", y " + box.Top.ToString("F0") : "missing"))));
                Check(inside, "both report buttons lie inside the page width with the window " + narrow + " px wide");
            }
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            await Reaches(() => restorePage.ActualWidth > 1000, 3000);
            await CaptureWidths(outer, "restore-report-wrap");

            // ── Restore: change codes, plain names, refusal and unknown outcome ──
            server.Rich = true;
            server.StaleRestoreList = true;
            Invoke(Find<Button>(restorePage, "RestoreChooseAgain")!);
            await Until(() => restore.State.Job == null && restore.State.Preview?.Sections?.Any(section => section.Id == "home_sections") == true, "rich preview loaded", 15000);
            await restore.OpenAsync(restore.State.Source!, null);
            await Until(() => restore.State.JobId == null && restore.State.Jobs.SingleOrDefault()?.Terminal == true &&
                Find<FrameworkElement>(restorePage, "RestoreLastRun") is { IsLoaded: true }, "reopening with a stale jobs list stays on Choose and shows the terminal last run");
            await CaptureWidths(outer, "restore-choose-after-terminal");
            server.StaleRestoreList = false;
            await Until(() => Find<CheckBox>(restorePage, "RestoreItem-home_sections-section:9") is { IsLoaded: true }, "section row rendered");
            string[] Lines(string id) => Find<StackPanel>(restorePage, id) is { } list ? Descendants(list).OfType<TextBlock>().Select(text => text.Text).ToArray() : [];
            Invoke(Find<Button>(restorePage, "RestoreDetails-home_sections-section:9")!);
            await Until(() => Find<StackPanel>(restorePage, "RestoreChanges-section:9") != null, "section changes listed");
            Check(Lines("RestoreChanges-section:9").SequenceEqual(["• Item limit", "• 3 collection items"]), "a section's change codes show as the official words, with the missing-items count");
            Invoke(Find<Button>(restorePage, "RestoreDetails-collections-t1")!);
            await Until(() => Find<StackPanel>(restorePage, "RestoreChanges-t1") != null, "template changes listed");
            Check(Lines("RestoreChanges-t1").SequenceEqual(["• Title", "• Libraries", "• future_code"]), "a template collection's change codes show as words, and a code this client does not know shows as sent");
            Invoke(Find<Button>(restorePage, "RestoreDetails-collections-t2")!);
            await Until(() => Find<StackPanel>(restorePage, "RestoreChanges-t2") != null, "older-server changes listed");
            Check(Lines("RestoreChanges-t2").SequenceEqual(["• sort order", "• 3 collection items"]), "an older server's English change words show as sent");
            Check(AutomationProperties.GetName(Find<CheckBox>(restorePage, "RestoreItem-libraries-lib-1")!) == "Movies evil tail", "a library name with a line break and direction characters shows as plain text");
            Invoke(Find<Button>(restorePage, "RestoreAccountDetails-u1")!);
            await Until(() => Find<CheckBox>(restorePage, "RestoreProfile-u1-p1") is { IsLoaded: true }, "account profiles shown");
            Check(string.IsNullOrEmpty(AutomationProperties.GetHelpText(Find<CheckBox>(restorePage, "RestoreAccount-u1")!)), "an account labeled by its email does not repeat the email under its name");
            Check(Descendants(restorePage).OfType<TextBlock>().Any(text => text.Text == AdminText.T("web.admin.backups.restore.users.mergeInto", new { name = "a@example.test" })), "a merge target with no username is named by its email");
            Check(AutomationProperties.GetName(Find<CheckBox>(restorePage, "RestoreProfile-u1-p1")!) == "Kids room", "a profile name with a direction isolate and a line break shows as plain text");
            Check(!Descendants(restorePage).OfType<TextBlock>().Any(text => text.Text.Any(IsHostile)), "no restore text carries a control or direction character from a name");
            // A wide row shares its line 3:2 (an account 9:11) between the name and the detail, each no wider than its text (official #4978), so whatever is
            // too long ends in an ellipsis inside its own share, and a short one is neither cut nor stretched.
            (TextBlock Name, TextBlock Detail, SiloPlayer.Controls.ShareRowPanel Panel) Pair(string nameId, string detailId) =>
                Find<TextBlock>(restorePage, nameId) is { } name && Find<TextBlock>(restorePage, detailId) is { } detail && VisualTreeHelper.GetParent(name) is SiloPlayer.Controls.ShareRowPanel panel
                    ? (name, detail, panel) : throw new InvalidOperationException("no shared row for " + nameId);
            await Until(() => Find<TextBlock>(restorePage, "RestoreItemName-libraries-lib-long") is { ActualWidth: > 0 } && Find<TextBlock>(restorePage, "RestoreAccountName-u2") is { ActualWidth: > 0 }, "wide rows laid out");
            restorePage.UpdateLayout();
            string Describe((TextBlock Name, TextBlock Detail, SiloPlayer.Controls.ShareRowPanel Panel) row) =>
                "line " + row.Panel.ActualWidth.ToString("F0") + ", name " + row.Name.ActualWidth.ToString("F0") + (row.Name.IsTextTrimmed ? " cut" : "") + ", detail " + row.Detail.ActualWidth.ToString("F0") + (row.Detail.IsTextTrimmed ? " cut" : "");
            var both = Pair("RestoreItemName-libraries-lib-long", "RestoreItemDetail-libraries-lib-long");
            var bothRoom = both.Panel.ActualWidth - both.Panel.Spacing;
            Program.Log("INFO: long name and long detail: " + Describe(both));
            Check(both.Name.IsTextTrimmed && both.Detail.IsTextTrimmed && both.Name.ActualWidth <= bothRoom * .6 + 1.5 && both.Detail.ActualWidth <= bothRoom * .4 + 1.5
                && both.Name.ActualWidth > bothRoom * .5 && both.Detail.ActualWidth > bothRoom * .3 && both.Name.ActualWidth + both.Panel.Spacing + both.Detail.ActualWidth <= both.Panel.ActualWidth + 1.5,
                "a long name and a long detail each end in an ellipsis inside their 3:2 share of the line (" + Describe(both) + ")");
            var mixed = Pair("RestoreItemName-libraries-lib-mixed", "RestoreItemDetail-libraries-lib-mixed");
            var mixedRoom = mixed.Panel.ActualWidth - mixed.Panel.Spacing;
            Check(mixed.Name.IsTextTrimmed && !mixed.Detail.IsTextTrimmed && mixed.Name.ActualWidth > mixedRoom * .6 + 2 && mixed.Name.ActualWidth + mixed.Panel.Spacing + mixed.Detail.ActualWidth <= mixed.Panel.ActualWidth + 1.5,
                "a long name takes the room a short detail leaves, past its 60% share (" + Describe(mixed) + ")");
            var shortRow = Pair("RestoreItemName-libraries-lib-short", "RestoreItemDetail-libraries-lib-short");
            Check(!shortRow.Name.IsTextTrimmed && !shortRow.Detail.IsTextTrimmed && Math.Abs(shortRow.Name.ActualWidth + shortRow.Panel.Spacing + shortRow.Detail.ActualWidth - shortRow.Panel.ActualWidth) < 1.5 && shortRow.Panel.ActualWidth < both.Panel.ActualWidth * .3,
                "a short name and detail are neither cut nor stretched across the line (" + Describe(shortRow) + ")");
            var account = Pair("RestoreAccountName-u2", "RestoreAccountContact-u2");
            var accountRoom = account.Panel.ActualWidth - account.Panel.Spacing;
            Check(account.Name.IsTextTrimmed && account.Detail.IsTextTrimmed && account.Name.ActualWidth <= accountRoom * .45 + 1.5 && account.Detail.ActualWidth <= accountRoom * .55 + 1.5
                && account.Name.ActualWidth > accountRoom * .35 && account.Detail.ActualWidth > accountRoom * .45,
                "a long account name and email share their line 9:11, each ending in an ellipsis (" + Describe(account) + ")");
            await CaptureWidths(outer, "restore-choose");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(560, 900));
            Check(await Reaches(() => Find<CheckBox>(restorePage, "RestoreItem-libraries-lib-1")?.Content is StackPanel { Orientation: Orientation.Vertical }), "a narrow restore row stacks the name above its detail");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1024, 900));
            Check(await Reaches(() => Find<StackPanel>(restorePage, "RestoreSummary") is { } summary && Grid.GetColumn(summary) == 1), "a window 1024 px wide puts the summary beside the sections, as the official lg breakpoint does");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900));
            Check(await Reaches(() => Find<StackPanel>(restorePage, "RestoreSummary") is { } stacked && Grid.GetColumn(stacked) == 0 && Grid.GetRow(stacked) == 1), "a narrower window stacks the summary under the sections");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            await Reaches(() => Find<StackPanel>(restorePage, "RestoreSummary") is { } wide && Grid.GetColumn(wide) == 1 && Find<CheckBox>(restorePage, "RestoreItem-libraries-lib-1")?.Content is SiloPlayer.Controls.ShareRowPanel);
            var libraryBox = Find<CheckBox>(restorePage, "RestoreItem-libraries-lib-1")!;
            await PressAsync(libraryBox, Windows.System.VirtualKey.Space, () => restore.Selection.Contains("libraries:lib-1"), () => Toggle(libraryBox));
            Toggle(Find<CheckBox>(restorePage, "RestoreProfile-u1-p1")!);
            await Until(() => restore.Selection.Contains("libraries:lib-1") && restore.Selection.Any(key => key.StartsWith("users:u1/profile:p1", StringComparison.Ordinal)), "items chosen");
            await Until(() => Find<Button>(restorePage, "RestoreReview") is { IsEnabled: true, IsLoaded: true }, "review enabled for the rich preview");
            // Tab goes through the Choose step in reading order, skipping what cannot take focus (the step list is a tab stop only while it holds focus).
            var tabOrder = await TabOrderAsync(restorePage.XamlRoot, Find<Control>(restorePage, "RestoreBackLink")!, "RestoreReview");
            Program.Log("INFO: Tab order on Choose: " + string.Join(" > ", tabOrder.Where(id => id.Length > 0)));
            var libraryAt = tabOrder.IndexOf("RestoreItem-libraries-lib-1");
            Check(tabOrder[0] == "RestoreBackLink" && tabOrder[^1] == "RestoreReview" && libraryAt > 0 && tabOrder.IndexOf("RestoreDetails-libraries-lib-1") == libraryAt + 1
                && tabOrder.IndexOf("RestoreItem-libraries-lib-long") > libraryAt + 1 && tabOrder.IndexOf("RestoreItem-libraries-lib-long") < tabOrder.Count - 1
                && !tabOrder.Contains("RestoreCurrentStep") && tabOrder.Count(id => id == "RestoreReview") == 1,
                "Tab runs from the back link through each row's checkbox and its details toggle to Review, without stopping on the step list");
            Program.Log("INFO: tab stops on Choose without an automation id: " + tabOrder.Count(id => id.Length == 0));
            // Enter on Review (focus on it by keyboard) moves to the Review step, and focus follows to the current step in the step list.
            var reviewButton = Find<Button>(restorePage, "RestoreReview")!;
            await PressAsync(reviewButton, Windows.System.VirtualKey.Enter, () => restore.State.Reviewing, () => Invoke(reviewButton));
            await Until(() => Find<Button>(restorePage, "RestoreStart") is { IsLoaded: true, IsEnabled: true } && Find<StackPanel>(restorePage, "RestoreCaveats") != null, "review with caveats");
            try { await Until(() => Field(restorePage, "_currentStepHolder") is ContentControl { FocusState: not FocusState.Unfocused }, "focus on the current step", 3000); }
            catch (InvalidOperationException)
            {
                var holderState = Field(restorePage, "_currentStepHolder") as ContentControl;
                var focusedNow = FocusManager.GetFocusedElement(restorePage.XamlRoot);
                Program.Log("INFO: focus holder state=" + holderState?.FocusState + ", loaded=" + holderState?.IsLoaded + ", tabStop=" + holderState?.IsTabStop + ", shownStep=" + Field(restorePage, "_shownStep")
                    + ", focused=" + focusedNow?.GetType().Name + "/" + (focusedNow as FrameworkElement)?.Name + ", reviewFocus=" + Find<Button>(restorePage, "RestoreBack")?.FocusState);
            }
            Check(!Descendants(restorePage).OfType<TextBlock>().Any(text => text.Text.Any(IsHostile)), "the review's sentences and left-out list carry no control or direction character from a name");
            Check(Field(restorePage, "_currentStepHolder") is ContentControl { FocusState: not FocusState.Unfocused }, "a step change moves keyboard focus to the current step in the step list");
            Check(Descendants(restorePage).OfType<TextBlock>().Any(text => text.Text.Contains("Movies evil tail", StringComparison.Ordinal)), "the review names the library by its plain text");
            await CaptureWidths(outer, "restore-review");
            // Space on Back (focus on it by keyboard) returns to Choose with focus on the step list; Enter on Review comes back, and focus is on the step list again.
            var backButton = Find<Button>(restorePage, "RestoreBack")!;
            await PressAsync(backButton, Windows.System.VirtualKey.Space, () => !restore.State.Reviewing, () => Invoke(backButton));
            await Until(() => Find<Button>(restorePage, "RestoreReview") is { IsLoaded: true, IsEnabled: true }, "Choose step again");
            Check(await Reaches(() => Field(restorePage, "_currentStepHolder") is ContentControl { FocusState: not FocusState.Unfocused }), "Space on Back leaves the Review step with focus on the current step in the step list");
            reviewButton = Find<Button>(restorePage, "RestoreReview")!;
            await PressAsync(reviewButton, Windows.System.VirtualKey.Enter, () => restore.State.Reviewing, () => Invoke(reviewButton));
            await Until(() => Find<Button>(restorePage, "RestoreStart") is { IsLoaded: true, IsEnabled: true }, "Review step again");
            Check(await Reaches(() => Field(restorePage, "_currentStepHolder") is ContentControl { FocusState: not FocusState.Unfocused }), "Enter on Review moves to the Review step with focus on the current step in the step list");

            // Opening a second restore in the same page instance is not a step change: the first draw neither scrolls to the top nor pulls focus to the step list.
            Check(Field(restorePage, "_shownStep") is 1, "the page last showed the Review step");
            restorePage.SuspendAdminPage();
            Check(Field(restorePage, "_shownStep") is -1 && Field(restorePage, "_shownRefusal") == null, "closing the restore view forgets the step it showed");
            typeof(AdminBackupsPage).GetMethod("Activate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(restorePage, null);
            await Until(() => restorePage.RestoreController?.State.Preview != null && Find<CheckBox>(restorePage, "RestoreItem-libraries-lib-1") is { IsLoaded: true, IsEnabled: true }, "second restore opened in the same page instance");
            restore = restorePage.RestoreController!;
            await Task.Delay(300); // bounded wait for a focus move that must not happen; the state is read after it
            Check(Field(restorePage, "_shownStep") is 0 && Field(restorePage, "_currentStepHolder") is ContentControl { FocusState: FocusState.Unfocused },
                "the first draw of a second restore in the same page instance is not a step change: focus is not pulled to the step list");
            Toggle(Find<CheckBox>(restorePage, "RestoreItem-libraries-lib-1")!);
            await Until(() => restore.Selection.Contains("libraries:lib-1"), "item chosen again");
            await Until(() => Find<Button>(restorePage, "RestoreReview") is { IsEnabled: true, IsLoaded: true }, "review enabled again");
            Invoke(Find<Button>(restorePage, "RestoreReview")!);
            await Until(() => Find<Button>(restorePage, "RestoreStart") is { IsLoaded: true, IsEnabled: true } && restore.State.Reviewing, "Review step again after the second open");

            var restorePosts = () => server.Requests.Count(request => request == "POST /api/v2/admin/backups/" + BackupId + "/restore");
            var postsBefore = restorePosts();
            // A restore the server answers slowly, pressed again before the answer (a double click, a held Enter key): still sent once.
            var restoreGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (server.Gates) server.Gates["POST /api/v2/admin/backups/" + BackupId + "/restore"] = restoreGate;
            server.RestoreMode = "refuse";
            var startButton = Find<Button>(restorePage, "RestoreStart")!;
            await PressAsync(startButton, Windows.System.VirtualKey.Enter, () => restorePosts() == postsBefore + 1, () => Invoke(startButton));
            await Until(() => restorePosts() == postsBefore + 1 && restore.State.Starting, "the restore is on the wire");
            HoldKey(Windows.System.VirtualKey.Enter, 6);
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => restore.StartAsync()));
            var postsHeld = restorePosts();
            lock (server.Gates) server.Gates.Remove("POST /api/v2/admin/backups/" + BackupId + "/restore");
            restoreGate.TrySetResult();
            await Until(() => restore.State.Refusal != null, "refusal shown");
            // New elements enter the visual tree one layout pass after the state that draws them.
            await Until(() => Find<TextBlock>(restorePage, "RestoreRefusal") is { IsLoaded: true }, "refusal rendered");
            Check(Find<TextBlock>(restorePage, "RestoreRefusal")?.Text.Contains("A chosen item can't be restored right now.", StringComparison.Ordinal) == true, "a refused restore shows the server's reason");
            Check(postsHeld == postsBefore + 1 && restorePosts() == postsBefore + 1, "the restore is sent exactly once, however often it is activated before the answer (" + postsHeld + " while held, " + restorePosts() + " after)");
            Check(restore.Selection.Count > 0 && Find<Button>(restorePage, "RestoreStart") is { IsEnabled: true } && Find<Button>(restorePage, "RestoreBack") is { IsEnabled: true } && restore.State.Reviewing,
                "a refused restore keeps the draft, the Review step and the enabled Back and Restore buttons");
            // Keyboard focus was on Restore, which was disabled while the request ran; it lands on the refusal's alert (a 422 offers no action), a tab stop only while it holds focus.
            Check(await Reaches(() => Find<ContentControl>(restorePage, "RestoreRefusalPanel") is { FocusState: not FocusState.Unfocused, IsTabStop: true }),
                "after a refused restore keyboard focus is on the refusal's alert, not lost (focus: " + FocusedId(restorePage.XamlRoot) + ")");
            await CaptureWidths(outer, "restore-refusal");
            var tabbed = await TabAsync(restorePage.XamlRoot);
            Check(tabbed && await Reaches(() => Find<ContentControl>(restorePage, "RestoreRefusalPanel") is { IsTabStop: false }) && FocusedId(restorePage.XamlRoot) != "RestoreRefusalPanel",
                "the refusal's alert is a tab stop only while it holds focus: Tab moves on and it stops being one");
            // A 409 that names the running restore offers "Open the running restore", and focus goes to that button.
            server.RestoreMode = "busy";
            startButton = Find<Button>(restorePage, "RestoreStart")!;
            await PressAsync(startButton, Windows.System.VirtualKey.Enter, () => restorePosts() == postsBefore + 2, () => Invoke(startButton));
            await Until(() => restore.State.Refusal is { RunningJobId: not null }, "a running restore is named");
            await Until(() => Find<Button>(restorePage, "RestoreOpenRunning") is { IsLoaded: true }, "the running restore's button rendered");
            Check(await Reaches(() => Find<Button>(restorePage, "RestoreOpenRunning") is { FocusState: not FocusState.Unfocused }),
                "after a 409 that names a running restore keyboard focus is on 'Open the running restore' (focus: " + FocusedId(restorePage.XamlRoot) + ")");
            Check(restore.Selection.Count > 0 && Find<Button>(restorePage, "RestoreStart") is { IsEnabled: true } && restorePosts() == postsBefore + 2, "that refusal keeps the draft too, and the restore was sent once");
            server.RestoreMode = "drop";
            Invoke(Find<Button>(restorePage, "RestoreStart")!);
            await Until(() => restore.State.OutcomeUnknown, "unknown outcome");
            await Until(() => Find<Button>(restorePage, "RestoreUnknownCheck") is { IsLoaded: true, IsEnabled: true }, "unknown outcome offers a status check");
            Check(restorePosts() == postsBefore + 3 && Find<Button>(restorePage, "RestoreReview") is not { IsEnabled: true }, "an unknown outcome blocks another restore");
            int JobListReads() => server.Requests.Count(request => request.StartsWith("GET /api/v2/admin/jobs", StringComparison.Ordinal) && !request.StartsWith("GET /api/v2/admin/jobs/", StringComparison.Ordinal));
            var readsBefore = JobListReads();
            Invoke(Find<Button>(restorePage, "RestoreUnknownCheck")!);
            await Until(() => JobListReads() > readsBefore, "the status check read the running restores");
            await Task.Delay(150); // bounded wait for a send that must not happen; the count is compared with the one before the check
            Check(restorePosts() == postsBefore + 3, "checking the outcome never sends the restore again");
            server.RestoreMode = "ok"; server.Rich = false;

            // ── List states: unreadable destination, failed read with Retry, empty, paused, saved Start time ──
            var listPage = await OpenAsync(outer, "/admin/backups");
            lastPage = listPage;
            Check(restorePage.RestoreController == null, "leaving the restore page drops its controller, drafts and reads");
            var listController = listPage.Controller!;
            await Until(() => listController.State.List != null && listController.State.SettingsLoaded, "list loaded again");
            string[] Shown(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(text => text.Text).ToArray();
            server.DestinationDown = true;
            await listController.RefreshAsync();
            await Until(() => Find<TextBlock>(listPage, "BackupsWarning-destination_unavailable") != null, "unreadable destination warning");
            Check(!Shown(listPage).Contains(AdminText.T("web.admin.backups.list.empty")) && Find<FrameworkElement>(listPage, "BackupRow-" + BackupId) == null, "an unreadable destination does not read as 'No backups yet.'");
            Check(!Shown(listPage).Contains(AdminText.T("web.admin.backups.location.previousStays")), "the note about earlier backups is not shown while there is no list");
            Check(Find<TextBlock>(listPage, "BackupsBlocker")?.Text == AdminText.T("web.admin.backups.backUpNow.unreachable"), "Back up now says why it can't run while the destination can't be read");
            await CaptureWidths(outer, "list-unreadable");
            server.DestinationDown = false; server.ListStatus = 500;
            await OpenAsync(outer, "/admin/backups/" + BackupId + "/restore");
            var failedPage = await OpenAsync(outer, "/admin/backups");
            lastPage = failedPage;
            await Until(() => Find<Button>(failedPage, "BackupsListRetry") is { IsLoaded: true, IsEnabled: true }, "failed read offers Retry");
            Check(!Shown(failedPage).Contains(AdminText.T("web.admin.backups.list.empty")), "a failed read is not drawn as 'No backups yet.'");
            await CaptureWidths(outer, "list-failed");
            server.ListStatus = 0;
            Invoke(Find<Button>(failedPage, "BackupsListRetry")!);
            await Until(() => Find<FrameworkElement>(failedPage, "BackupRow-" + BackupId) is { IsLoaded: true }, "Retry recovered the list");
            Check(Find<FrameworkElement>(failedPage, "BackupRow-" + BackupId) is { IsLoaded: true } && failedPage.Controller!.State is { ListError: null, List.Items.Length: > 0 }
                && Find<Button>(failedPage, "BackupsListRetry") == null, "Retry after a failed read reads the list again and shows it, and the Retry button is gone");
            listPage = failedPage; listController = listPage.Controller!;
            server.Empty = true;
            await listController.RefreshAsync();
            await Until(() => Shown(listPage).Contains(AdminText.T("web.admin.backups.list.empty")), "empty state");
            Check(Shown(listPage).Contains(AdminText.T("web.admin.backups.list.emptyConfigured")) && Find<Button>(listPage, "BackupsEmptyBackUpNow") is { IsEnabled: true }, "the empty list offers Back up now when it can run");
            await CaptureWidths(outer, "list-empty");
            server.Encrypted = true; server.KeyConfirmed = false;
            await listController.RefreshAsync();
            await Until(() => Find<TextBlock>(listPage, "BackupsBlocker")?.Text == AdminText.T("web.admin.backups.backUpNow.keyNotConfirmed"), "paused until the key is confirmed");
            Check(Shown(listPage).Contains(AdminText.T("web.admin.backups.list.empty")) && !Shown(listPage).Contains(AdminText.T("web.admin.backups.list.emptyConfigured"))
                && Find<Button>(listPage, "BackupsEmptyBackUpNow") == null, "while Back up now can't run the empty list says so once and offers no button");
            Check(Find<TextBlock>(listPage, "BackupsNextPhrase") == null, "a paused server forecasts no next run");
            server.Encrypted = false; server.KeyConfirmed = true; server.Empty = false;
            await listController.RefreshAsync();
            await Until(() => Find<TextBlock>(listPage, "BackupsNextPhrase") != null && Find<FrameworkElement>(listPage, "BackupRow-" + BackupId) != null, "next run shown again");
            listController.Set("backup.time", "05:01");
            await Until(() => Find<Button>(listPage, "BackupsSave") is { IsEnabled: true }, "save enabled for the Start time");
            Invoke(Find<Button>(listPage, "BackupsSave")!);
            await Until(() => listController.State.ScheduleSettling, "waiting for the task to take the Start time");
            await Until(() => Find<TextBlock>(listPage, "BackupsNextPhrase")?.Text == AdminText.T("web.admin.backups.schedule.unknown"), "no next run named until the trigger moves");
            Check(listController.State.ScheduleSettling && Find<TextBlock>(listPage, "BackupsNextPhrase")?.Text == AdminText.T("web.admin.backups.schedule.unknown"),
                "a saved Start time hides the old schedule's next run until the task's daily trigger holds it");
            server.TriggerTime = "05:01";
            await Until(() => !listController.State.ScheduleSettling && Find<TextBlock>(listPage, "BackupsNextPhrase") is { } phrase && phrase.Text != AdminText.T("web.admin.backups.schedule.unknown"), "next run shown once the trigger moved", 12000);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(560, 900));
            Check(await Reaches(() => Find<Button>(listPage, "BackupsBackUpNow") is { HorizontalAlignment: HorizontalAlignment.Stretch }), "the phone header gives Back up now its own full-width row");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            await Reaches(() => Find<Button>(listPage, "BackupsBackUpNow") is { HorizontalAlignment: not HorizontalAlignment.Stretch });
            listController.Discard();

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
            var departedDeletes = server.Requests.Count(request => request == "DELETE /api/v2/admin/backups/uploads/" + UploadId);
            await Task.Delay(300); // bounded wait for a replay that must not come; the count is compared with the one before
            Check(departedDeletes == deletes + 1 && server.Requests.Count(request => request == "DELETE /api/v2/admin/backups/uploads/" + UploadId) == departedDeletes,
                "page departure deletes its known unfinished upload under the same authority without a replay");
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

    /// <summary>Waits for a real condition; false when it did not come within the time.</summary>
    private static async Task<bool> Reaches(Func<bool> condition, int milliseconds = 4000)
    {
        for (var i = 0; i < milliseconds / 25 && !condition(); i++) await Task.Delay(25);
        return condition();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetFocus")] private static extern nint KeyGetFocus();
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint KeyGetWindowThreadProcessId(nint target, out uint process);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool KeyPostMessage(nint target, uint message, nuint parameter, nint position);

    private static int _keysDelivered, _keysFallen;

    /// <summary>The control that has keyboard focus, as an automation id.</summary>
    private static string FocusedId(XamlRoot root) => FocusManager.GetFocusedElement(root) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "";

    private static bool FocusIn(XamlRoot root, DependencyObject ancestor)
    {
        for (var current = FocusManager.GetFocusedElement(root) as DependencyObject; current != null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    /// <summary>
    /// Gives a control keyboard focus and presses a key on it. The key messages are posted to this process's own focused window (never global input),
    /// so they travel the framework's own keyboard path. When the host does not deliver them, the control's automation pattern runs instead and the
    /// log says so: the outcome checks that follow are the same either way.
    /// </summary>
    private static async Task<bool> PressAsync(Control target, Windows.System.VirtualKey key, Func<bool> took, Action fallback)
    {
        target.Focus(FocusState.Keyboard);
        var window = KeyGetFocus();
        KeyGetWindowThreadProcessId(window, out var process);
        var posted = window != 0 && process == (uint)Environment.ProcessId
            && KeyPostMessage(window, 0x0100, (nuint)(int)key, 1) && KeyPostMessage(window, 0x0101, (nuint)(int)key, unchecked((nint)0xC0000001u));
        if (posted && await Reaches(took, 2500)) { _keysDelivered++; return true; }
        _keysFallen++;
        Program.Log("INFO: a posted " + key + " on " + AutomationProperties.GetAutomationId(target) + " had no effect in this host (posted=" + posted + "); its automation pattern ran instead");
        fallback();
        return false;
    }

    /// <summary>The key held down: repeated key-down messages on whatever has focus, as an auto-repeat sends them.</summary>
    private static void HoldKey(Windows.System.VirtualKey key, int repeats)
    {
        var window = KeyGetFocus();
        KeyGetWindowThreadProcessId(window, out var process);
        if (window == 0 || process != (uint)Environment.ProcessId) return;
        for (var i = 0; i < repeats; i++) KeyPostMessage(window, 0x0100, (nuint)(int)key, unchecked((nint)0x40000001u));
    }

    /// <summary>Presses Tab on whatever has focus (posted to this process's own focused window, as the other keys are); true when focus moved.</summary>
    private static async Task<bool> TabAsync(XamlRoot root)
    {
        var before = FocusManager.GetFocusedElement(root);
        var window = KeyGetFocus();
        KeyGetWindowThreadProcessId(window, out var process);
        if (window == 0 || process != (uint)Environment.ProcessId) return false;
        KeyPostMessage(window, 0x0100, 0x09, 1); KeyPostMessage(window, 0x0101, 0x09, unchecked((nint)0xC0000001u));
        return await Reaches(() => !ReferenceEquals(FocusManager.GetFocusedElement(root), before), 1500);
    }

    /// <summary>The automation ids focus visits pressing Tab from a control, until a stop id (or the limit or a Tab that moves nowhere).</summary>
    private static async Task<List<string>> TabOrderAsync(XamlRoot root, Control start, string stopId, int limit = 160)
    {
        var visited = new List<string>();
        start.Focus(FocusState.Keyboard);
        visited.Add(FocusedId(root));
        for (var i = 0; i < limit && visited[^1] != stopId && await TabAsync(root); i++) visited.Add(FocusedId(root));
        return visited;
    }

    private static bool IsHostile(char value) => char.IsControl(value) || value is >= (char)0x202A and <= (char)0x202E or >= (char)0x2066 and <= (char)0x2069 or (char)0x2028 or (char)0x2029;

    private static Windows.UI.Color RootColor(Button button, string state)
    {
        VisualStateManager.GoToState(button, state, false);
        var root = Descendants(button).OfType<Border>().First(border => border.Name == "Root");
        return ((SolidColorBrush)root.Background).Color;
    }

    /// <summary>The page at 1440, 1024, 800 and 560 px wide, saved next to the results; the window goes back to 1440.</summary>
    private static async Task CaptureWidths(Frame outer, string name)
    {
        var window = App_Window;
        foreach (var width in new[] { 1440, 1024, 800, 560 })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
            await Task.Delay(450);
            outer.UpdateLayout();
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(outer);
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
            var file = await folder.CreateFileAsync("backups-" + name + "-" + width + ".png", Windows.Storage.CreationCollisionOption.ReplaceExisting);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
            await encoder.FlushAsync();
            Program.Log("INFO: capture backups-" + name + "-" + width + ".png page=" + bitmap.PixelWidth + "x" + bitmap.PixelHeight);
        }
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
        await Task.Delay(300);
    }

    private static Window App_Window = null!;

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
