using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>
/// Production Configure › Server › Storage registration and Frame entry with a synthetic server only. Credentials are
/// synthetic markers that must never appear in rendered text. Teardown waits for the review/discard dialogs' Closed
/// events and the page/shell Unloaded events before restoring globals or disposing the provider; a missed barrier fails.
/// </summary>
internal static class AdminStorageSettingsNativeFixture
{
    private const string Secret = "fixture-storage-secret";
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000)
    {
        bool Met() { try { return condition(); } catch (InvalidOperationException) { return false; } }
        for (var i = 0; i < milliseconds / 25 && !Met(); i++) await Task.Delay(25);
        if (!Met()) throw new InvalidOperationException("Timed out: " + label);
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<string> Requests = new();
        internal readonly ConcurrentQueue<(string Request, string Body)> Writes = new();
        internal readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
        {
            ["artwork.storage_backend"] = "", ["artwork.local_path"] = "/data/art", ["metadata.cache_images"] = "true", ["s3.public_url_auth"] = "",
        };
        internal bool StatusKnown = true, RecoveryPending, MissingStatus, RejectQueue, AmbiguousQueue, FailJobs;
        internal string Backend = "local";
        internal string? JobState;
        internal TaskCompletionSource? QueueGate, StatusGate;
        internal int Version = 1;
        internal string[] ManagedKeys = [];
        /// <summary>False models an older server that sends the managed list but no managed_by_env_vars map.</summary>
        internal bool NamedVariables = true;
        internal int Count(string prefix) => Requests.Count(r => r.StartsWith(prefix, StringComparison.Ordinal));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var key = $"{request.Method} {request.RequestUri.PathAndQuery}";
            Requests.Enqueue(key);
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.Method != HttpMethod.Get) Writes.Enqueue((key, body));
            switch (request.Method.Method + " " + path)
            {
                case "GET /api/v2/admin/settings/effective": return Tagged(Json(Values));
                case "GET /api/v2/admin/settings/sensitive-status":
                    return Json(ManagedKeys.Length > 0 && NamedVariables
                        ? new { configured = new[] { "s3.public_access_key" }, managed_by_env = ManagedKeys, managed_by_env_vars = ManagedKeys.ToDictionary(key => key, key => "SILO_" + key.ToUpperInvariant().Replace('.', '_')) }
                        : (object)new { configured = new[] { "s3.public_access_key" }, managed_by_env = ManagedKeys });
                case "GET /api/v2/admin/settings/restart-keys": return Json(new { keys = new[] { "artwork.storage_backend", "artwork.local_path" }, prefixes = new[] { "s3." } });
                case "PUT /api/v2/admin/settings":
                {
                    using var document = JsonDocument.Parse(body);
                    foreach (var value in document.RootElement.GetProperty("values").EnumerateObject())
                        if (!value.Name.EndsWith("_key", StringComparison.Ordinal) && !value.Name.EndsWith("_secret", StringComparison.Ordinal)) Values[value.Name] = value.Value.GetString() ?? "";
                    Version++;
                    return Tagged(Json(new { values = new Dictionary<string, string>(), restart_required = false }));
                }
                case "GET /api/v2/admin/server/status":
                    if (StatusGate is { } statusGate) await statusGate.Task.WaitAsync(cancellationToken);
                    if (MissingStatus) return Json(new { restart_required = false });
                    return Json(new
                    {
                        restart_required = false, restart_mark_count = 0, restart_requested = false,
                        artwork_storage = new { backend = Backend, locked = true, status_known = StatusKnown, private_locked = false },
                    });
                case "GET /api/v2/admin/storage-transitions/capabilities": return Json(new { revision = "fixture", state = "available" });
                case "GET /api/v2/admin/storage-transitions/source-health":
                    return Json(new { current_backend = Backend, reachable = true, recovery_pending = RecoveryPending, recovery_state = RecoveryPending ? "running" : null, recovery_progress_percent = 40 });
                case "GET /api/v2/admin/jobs":
                    if (FailJobs) return Json(new { }, HttpStatusCode.ServiceUnavailable);
                    return Json(new { items = JobState == null ? Array.Empty<object>() : [Job()] });
                case "POST /api/v2/admin/storage-transitions":
                    if (QueueGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                    if (RejectQueue) return Json(new { status = 422, code = "validation_failed" }, HttpStatusCode.UnprocessableEntity);
                    JobState = "queued";
                    if (AmbiguousQueue) return Json(new { }, HttpStatusCode.BadGateway);
                    return Json(new { job = Job(), preflight = new { current_backend = "local", target_backend = "s3" } }, HttpStatusCode.Accepted);
                case "POST /api/v2/admin/jobs/job-1/cancel":
                    JobState = "canceling";
                    return Json(Job(), HttpStatusCode.Accepted);
                case "POST /api/v2/admin/settings/check/s3_public": return Json(new { success = true, message = "Connected" });
                case "GET /api/v2/admin/system/build": return Json(new { display = "synthetic", available = true, build_number = 1 });
                default: return Json(new { });
            }
        }

        private object Job() => new
        {
            id = "job-1", kind = "storage_transition", state = JobState, terminal = false, cancelable = true, created_at = "2026-11-16T00:00:00Z",
            storage_transition_result = new { phase = JobState == "succeeded" ? "completed" : JobState == "queued" ? "queued" : "copying", verified_objects = 3, manual_restart_required = false },
        };
        private HttpResponseMessage Tagged(HttpResponseMessage response) { response.Headers.ETag = new($"\"v{Version}\""); return response; }
        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        // Disclosure state stays in the fixture's result folder, never the user's local application data.
        var preferences = new LoggingDisclosurePreferences(Path.Combine(Program.ResultDirectory, "storage-ui"));
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(preferences)
            .AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        // The same registration entry point as App, rather than a test-only page registration.
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        Frame? outer = null;
        AdminShellPage? shell = null;
        AdminStorageSettingsPage? page = null;
        try
        {
            field.SetValue(null, services);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            var resolved = AdminShellPage.TryOpenHref("/admin/settings/storage");
            if (resolved is not { Kind: AdminRouteResolutionKind.Route })
                throw new InvalidOperationException("Storage is not available through the production Configure registration.");
            for (var i = 0; i < 160 && outer.Content is not AdminShellPage; i++) await Task.Delay(25);
            shell = outer.Content as AdminShellPage ?? throw new InvalidOperationException("Storage did not enter the production Admin shell.");
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            for (var i = 0; i < 160 && inner.Content is not AdminStorageSettingsPage; i++) await Task.Delay(25);
            page = inner.Content as AdminStorageSettingsPage ?? throw new InvalidOperationException("Storage did not activate its production WinUI Page.");
            Check(true, "Storage activates through production registration, shell and Frame");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Storage navigation keeps page transitions suppressed");
            Check(ReferenceEquals(page.ScrollContent, Field(page, "_scrollContent")), "Storage exposes its scroll content for gutter wheel scrolling");
            page.SetHostActivity(true, true);
            await Until(() => page.Controller?.State is { Loaded: true, ArtworkStorage: not null, StatusLoading: false }, "storage loaded");
            await Task.Delay(250);
            var controller = page.Controller!;

            // Header, storage group and locked local status.
            Check(Texts(page).Contains("Storage") && Texts(page).Contains("Where artwork and uploads are stored: local disk or S3 buckets."), "header shows the official title and description");
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.StartsWith("web.", StringComparison.Ordinal) || t.Text.StartsWith("configure.", StringComparison.Ordinal)),
                "rendered page and navigation labels have catalog text");
            var backend = Combo(page, "Storage_artwork.storage_backend");
            Check((backend.SelectedItem as ComboBoxItem)?.Tag as string == "local" && backend.IsEnabled, "locked local storage shows Local disk as the backend");
            Check(Texts(page).Contains("Edit the local path, or choose S3 and fill in Public storage. Saving opens a managed transition review."), "locked local backend explains the transition");
            Check(Group(page, "Storage_public").Visibility == Visibility.Collapsed && Group(page, "Storage_private").Visibility == Visibility.Collapsed,
                "local disk without a private bucket hides public and private storage");
            Check(Field<FrameworkElement>(page, "_lockAlert").Visibility == Visibility.Collapsed, "known lock status shows no lock alert");
            Check(Math.Abs(Field<StackPanel>(page, "_body").ActualWidth - 1120) < 1.5, "the settings column uses the 1120 DIP width");
            await CaptureAsync(outer, "storage-loaded.png");

            // A private-only local review still submits a local path: its required editor must be visible.
            server.Values[AdminStorageRules.LocalPath] = "";
            server.Values["s3.private_bucket"] = "old-private";
            server.Values["s3.private_endpoint"] = "https://s3.example";
            await controller.ReloadAsync(preserveDraft: false);
            await Until(() => !controller.State.StatusLoading, "private-only status");
            controller.Set("s3.private_bucket", "new-private");
            await controller.SaveAsync();
            await Until(() => page.ReviewDialog is { IsLoaded: true } && controller.State.SourceHealth != null, "private-only review");
            await Task.Delay(100);
            var privateReview = page.ReviewDialog!;
            Check(privateReview.Title as string == "Change private storage", "a private bucket edit opens a private-only review");
            Check(Field<FrameworkElement>(page, "_reviewLocal").Visibility == Visibility.Visible &&
                Field<FrameworkElement>(page, "_reviewLocalPathRequired").Visibility == Visibility.Visible &&
                !Button(privateReview, "StorageReviewQueue").IsEnabled, "a blank required local path has a visible editor and validation");
            Input(privateReview, "StorageReviewLocalPath").Text = "/data/art";
            await Until(() => Button(privateReview, "StorageReviewQueue").IsEnabled, "private-only path corrected");
            Check(Field<FrameworkElement>(page, "_reviewLocal").Visibility == Visibility.Visible, "the private-only path editor stays visible after correction");
            var privateClosed = Closed(privateReview);
            Invoke(Button(privateReview, "StorageReviewCancel"));
            await privateClosed.WaitAsync(TimeSpan.FromSeconds(6));
            server.Values[AdminStorageRules.LocalPath] = "/data/art";
            server.Values.Remove("s3.private_bucket");
            server.Values.Remove("s3.private_endpoint");
            await controller.ReloadAsync(preserveDraft: false);
            await Until(() => !controller.State.StatusLoading, "restored local status");
            await Task.Delay(100);

            // A plain edit saves with Save; Discard reverts.
            var cache = Descendants(page).OfType<ToggleSwitch>().Single(t => AutomationProperties.GetAutomationId(t) == "Storage_metadata.cache_images");
            cache.IsOn = false;
            await Until(() => controller.State.DirtyKeys.Count == 1, "cache edit");
            var saveBar = Field<FrameworkElement>(page, "_saveBar");
            await Until(() => saveBar.Visibility == Visibility.Visible && Button(page, "StorageSave").Content?.ToString() == "Save", "plain edit save presentation");
            Check(saveBar.Visibility == Visibility.Visible && Button(page, "StorageSave").Content?.ToString() == "Save", "a plain edit shows the Save pill");
            Invoke(Button(page, "StorageDiscard"));
            await Until(() => controller.State.DirtyKeys.Count == 0, "discard");

            // A draft for a key the environment takes over is dropped; the row is disabled, shows the official note and carries it in its help text.
            await RenderQueuedAsync(page);
            await Until(() => cache.IsEnabled, "the cache switch is editable before the environment takes over");
            cache.IsOn = !cache.IsOn;
            await Until(() => controller.State.DirtyKeys.Count == 1, "cache draft before the environment takes over");
            server.ManagedKeys = [AdminStorageRules.CacheImages]; await controller.ReloadAsync(preserveDraft: true);
            await Until(() => controller.State.ManagedByEnv.Contains(AdminStorageRules.CacheImages) && !cache.IsEnabled && !controller.State.StatusLoading, "managed cache toggle locked");
            await RenderQueuedAsync(page);
            Check(controller.State.DirtyKeys.Count == 0 && cache.IsOn == (controller.State.Get(AdminStorageRules.CacheImages) == "true"), "the draft of the key that became managed is dropped");
            Check(EnvironmentNoteProbe.Verify(page, cache, EnvironmentNoteProbe.Named("SILO_METADATA_CACHE_IMAGES")) is null, "the managed Storage row is disabled, shows the official note naming the variable and carries it in its help text");
            server.NamedVariables = false; await controller.ReloadAsync(preserveDraft: true); await RenderQueuedAsync(page);
            await Until(() => EnvironmentNoteProbe.Verify(page, cache, EnvironmentNoteProbe.Unnamed) is null, "the unnamed note is rendered for the older server");
            Check(EnvironmentNoteProbe.Verify(page, cache, EnvironmentNoteProbe.Unnamed) is null, "an older server that names no variable still locks the row and shows the official field message");
            server.NamedVariables = true; server.ManagedKeys = []; await controller.ReloadAsync(preserveDraft: false);
            await Until(() => controller.State.ManagedByEnv.Count == 0 && cache.IsEnabled && !controller.State.StatusLoading, "managed lock released");

            // Clearing saved token fields must survive the queued render before replacement typing.
            server.Backend = "s3";
            server.Values[AdminStorageRules.Backend] = "s3";
            server.Values["s3.public_bucket"] = "media";
            server.Values[AdminStorageRules.UrlAuth] = "cloudflare_token";
            server.Values[AdminStorageRules.TokenTtl] = "3600";
            server.Values[AdminStorageRules.TokenParam] = "saved_verify";
            await controller.ReloadAsync(preserveDraft: false);
            await Until(() => !controller.State.StatusLoading, "saved token settings loaded");
            await RenderQueuedAsync(page);
            var tokenDisclosure = Button(page, "StorageAdvanced");
            if (AutomationProperties.GetItemStatus(tokenDisclosure) == "collapsed") Invoke(tokenDisclosure);
            await RenderQueuedAsync(page);
            foreach (var (key, saved, replacement, fallback) in new[]
            {
                (AdminStorageRules.TokenTtl, "3600", "7200", "10800"),
                (AdminStorageRules.TokenParam, "saved_verify", "auth", "verify"),
            })
            {
                var input = Input(page, "Storage_" + key);
                Check(input.Text == saved && input.Focus(FocusState.Keyboard), key + " focuses its saved value");
                input.SelectAll();
                input.SelectedText = "";
                await RenderQueuedAsync(page);
                Check(input.FocusState != FocusState.Unfocused && input.Text == "" && controller.State.Get(key) == "",
                    key + " keeps the focused empty draft through a queued render");
                foreach (var character in replacement)
                {
                    input.SelectedText = character.ToString();
                    input.Select(input.Text.Length, 0);
                    await RenderQueuedAsync(page);
                }
                Check(input.Text == replacement && controller.State.Get(key) == replacement,
                    key + " stages exactly the replacement without a restored default");
                input.SelectAll();
                input.SelectedText = "";
                await RenderQueuedAsync(page);
                Check(backend.Focus(FocusState.Programmatic), key + " ends editing");
                await RenderQueuedAsync(page);
                Check(input.Text == fallback && controller.State.Get(key) == "",
                    key + " restores its displayed default only after blur without staging it");
                controller.Discard();
                await RenderQueuedAsync(page);
                Check(input.Text == saved && !controller.State.IsDirty(key), key + " discards to its saved value");
            }
            server.Backend = "local";
            server.Values[AdminStorageRules.Backend] = "";
            server.Values[AdminStorageRules.UrlAuth] = "";
            server.Values.Remove("s3.public_bucket");
            server.Values.Remove(AdminStorageRules.TokenTtl);
            server.Values.Remove(AdminStorageRules.TokenParam);
            await controller.ReloadAsync(preserveDraft: false);
            await Until(() => !controller.State.StatusLoading, "token regression baseline restored");
            await RenderQueuedAsync(page);

            // Choosing S3 on locked local storage stages the backend and routes Save through review.
            Select(backend, "s3");
            await Until(() => controller.State.IsDirty("artwork.storage_backend"), "backend staged");
            await Task.Delay(150);
            Check(Group(page, "Storage_public").Visibility == Visibility.Visible && Group(page, "Storage_private").Visibility == Visibility.Visible, "S3 shows public and private storage");
            Check(Button(page, "StorageSave").Content?.ToString() == "Review transition", "a location change relabels Save as Review transition");
            Input(page, "Storage_s3.public_bucket").Text = "media";
            await Until(() => controller.State.IsDirty("s3.public_bucket"), "bucket staged");
            await Task.Delay(100);
            Check(Text(page, "Storage_public_LocationNote").Text == "Saving this bucket opens a managed transition from local storage. Choose what to copy before switching.",
                "public location note explains the transition from local storage");

            // Write-only credentials.
            var access = Password(page, "Storage_s3.public_access_key");
            var secret = Password(page, "Storage_s3.public_secret_key");
            Check(access.Password.Length == 0 && access.PlaceholderText == "••••••••••••" && secret.PlaceholderText == "Not configured", "credential fields show presence, never values");
            Check(Button(page, "Storage_s3.public_access_key_Action").Visibility == Visibility.Visible, "a saved access key offers Clear saved value");
            secret.Password = Secret;
            await Until(() => controller.State.IsDirty("s3.public_secret_key"), "secret staged");
            Invoke(Button(page, "Storage_public_Check"));
            await Until(() => controller.State.PublicCheck != null, "connection check");
            await Task.Delay(100);
            Check(Text(page, "Storage_public_CheckResult").Text == "Connected", "Check Connection shows the server result");
            var check = server.Writes.Single(w => w.Request.StartsWith("POST /api/v2/admin/settings/check/s3_public", StringComparison.Ordinal));
            Check(check.Body.Contains(Secret, StringComparison.Ordinal) && !check.Body.Contains("s3.public_access_key\":\"", StringComparison.Ordinal),
                "the check sends the staged secret but not an unstaged saved credential");

            // Asset links: Cloudflare shows the Advanced tier with defaults that are not staged.
            Select(Combo(page, "Storage_s3.public_url_auth"), "cloudflare_token");
            await Until(() => controller.State.Get("s3.public_url_auth") == "cloudflare_token", "url auth");
            await Task.Delay(100);
            var disclosure = Button(page, "StorageAdvanced");
            Check(AutomationProperties.GetName(disclosure) == "Advanced · 4 settings", "Advanced names its four Cloudflare settings");
            if (AutomationProperties.GetItemStatus(disclosure) == "collapsed") { Invoke(disclosure); await Task.Delay(100); }
            Check(Input(page, "Storage_s3.public_token_param").Text == "verify" && Input(page, "Storage_s3.public_token_ttl").Text == "10800",
                "token parameter and lifetime show official defaults");
            Check(!controller.State.IsDirty("s3.public_token_param") && !controller.State.IsDirty("s3.public_token_ttl"), "displayed defaults are not staged");
            Check(Texts(page).Contains("seconds"), "link lifetime shows its seconds unit");
            Check(!Texts(page).Any(t => t.Contains(Secret, StringComparison.Ordinal)), "the staged secret never appears in rendered text");
            await CaptureAsync(outer, "storage-s3-staged.png");

            // Review: the copy policies need a private bucket for a local source moving to S3.
            Invoke(Button(page, "StorageSave"));
            await Until(() => page.ReviewDialog is { IsLoaded: true }, "review dialog");
            await Task.Delay(200);
            var review = page.ReviewDialog!;
            Check(review.Title as string == "Change artwork storage", "review title names artwork storage");
            Check(Text(review, "StorageReviewTarget").Text == "New S3 location" && Text(review, "StorageReviewTargetLine").Text == "Public bucket: media · endpoint not set",
                "review target shows the new public bucket");
            var preserve = Radio(review, "StoragePolicy_preserve_uploads");
            var fresh = Radio(review, "StoragePolicy_start_fresh");
            Check(preserve.IsChecked == true && !preserve.IsEnabled && fresh.IsEnabled, "preserve is selected but unavailable without a private bucket");
            Check(Texts(review).Contains("Configure a private S3 bucket to preserve profile avatars."), "unavailable policies explain the missing private bucket");
            Check(!Button(review, "StorageReviewQueue").IsEnabled, "Queue is disabled for an unavailable policy");
            await CaptureAsync(review, "storage-review.png");
            fresh.IsChecked = true;
            await Until(() => controller.State.Review?.Policy == "start_fresh", "policy");
            await Task.Delay(100);
            Check(Button(review, "StorageReviewQueue").IsEnabled, "Start fresh enables Queue");
            var closed = Closed(review);
            Invoke(Button(review, "StorageReviewQueue"));
            await closed.WaitAsync(TimeSpan.FromSeconds(6));
            var queued = server.Writes.Where(w => w.Request == "POST /api/v2/admin/storage-transitions").ToArray();
            Check(queued.Length == 1, "the transition is sent exactly once");
            using (var sent = JsonDocument.Parse(queued[0].Body))
            {
                var values = sent.RootElement.GetProperty("values");
                Check(sent.RootElement.GetProperty("policy").GetString() == "start_fresh" && values.GetProperty("artwork.storage_backend").GetString() == "s3" &&
                    values.GetProperty("s3.public_bucket").GetString() == "media" && values.GetProperty("s3.public_secret_key").GetString() == Secret &&
                    !values.TryGetProperty("metadata.cache_images", out _), "the reviewed command carries the target and staged storage keys only");
            }
            await Until(() => page.LastNotice == "Storage transition queued (job-1).", "queued toast");
            await Until(() => Password(page, "Storage_s3.public_secret_key").Password.Length == 0, "a queued transition erases the typed credential");
            Check(true, "a queued transition erases the typed credential");

            // Active transition with jobs polling only while the page is in front.
            await Until(() => Field<FrameworkElement>(page, "_activePanel").Visibility == Visibility.Visible, "active panel");
            Check(Text(page, "StorageActiveTitle").Text == "Storage transition: queued" && Text(page, "StorageActivePhase").Text == "Waiting to start", "active panel shows status and phase");
            Check(controller.IsJobsPolling, "an active transition polls jobs while the page is active");
            page.SetHostActivity(false, false);
            Check(!controller.IsJobsPolling, "a background window stops job polling");
            var jobReads = server.Count("GET /api/v2/admin/jobs");
            await Task.Delay(2600);
            Check(server.Count("GET /api/v2/admin/jobs") == jobReads, "no job reads while the window is in the background");
            page.SetHostActivity(true, true);
            Invoke(Button(page, "StorageTransitionCancel"));
            await Until(() => page.LastNotice == "Storage transition cancellation requested.", "cancel toast");
            Check(server.Writes.Count(w => w.Request == "POST /api/v2/admin/jobs/job-1/cancel") == 1, "Cancel transition targets the captured job once");
            await CaptureAsync(outer, "storage-active.png");

            // Recovery after restart polls every five seconds only while pending and active.
            server.RecoveryPending = true;
            page.SetHostActivity(false, false); page.SetHostActivity(true, true);
            await Until(() => Field<FrameworkElement>(page, "_recoveryPanel").Visibility == Visibility.Visible, "recovery panel");
            Check(Text(page, "StorageRecoveryTitle").Text == "Storage recovery: running" && controller.IsRecoveryPolling, "pending recovery shows its panel and polls");
            server.RecoveryPending = false; server.JobState = null;
            await Until(() => !controller.IsRecoveryPolling, "recovery poll stops", 9000);

            // Lock status unknown keeps other settings editable and disables location fields.
            server.StatusKnown = false;
            controller.RetryStatus();
            await Until(() => !controller.State.StatusKnown && !controller.State.StatusLoading, "lock status unknown");
            await Task.Delay(150);
            Check(Field<FrameworkElement>(page, "_lockAlert").Visibility == Visibility.Visible && Text(page, "StorageLockStatusText").Text == "Storage lock status is unavailable",
                "unknown lock status shows its alert with Retry status");
            Check(!Combo(page, "Storage_artwork.storage_backend").IsEnabled && !Input(page, "Storage_artwork.local_path").IsEnabled, "location fields wait for a known lock status");
            server.StatusKnown = true;
            Invoke(Button(page, "StorageRetryStatus"));
            await Until(() => controller.State.StatusKnown, "Retry status restores the known lock state");

            // Narrow layout keeps the column readable.
            window.AppWindow.ResizeClient(new(800, 600)); await Task.Delay(300); outer.UpdateLayout();
            Check(Field<StackPanel>(page, "_body").ActualWidth <= 800, "narrow layout fits the settings column");
            await CaptureAsync(outer, "storage-narrow.png");
            window.AppWindow.ResizeClient(new(1440, 900)); await Task.Delay(200);

            // Dirty departure dialog: Cancel keeps the page and its draft.
            Descendants(page).OfType<ToggleSwitch>().Single(t => AutomationProperties.GetAutomationId(t) == "Storage_metadata.cache_images").IsOn =
                controller.State.Get("metadata.cache_images") != "true";
            await Until(() => controller.State.DirtyKeys.Count > 0, "dialog draft");
            var leave = page.CanLeaveAsync();
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "dirty leave dialog");
            var discard = (ContentDialog)Field(page, "_dialog")!;
            var discardClosed = Closed(discard);
            Invoke(Descendants(discard).OfType<Button>().First(b => b.Content?.ToString() == "Cancel"));
            await discardClosed.WaitAsync(TimeSpan.FromSeconds(6));
            Check(!await leave && controller.State.DirtyKeys.Count > 0, "Cancel keeps the Storage page and its draft");
            Invoke(Button(page, "StorageDiscard"));
            await Until(() => controller.State.DirtyKeys.Count == 0, "final discard");
            // Controller invalidation must close a live modal while its queue POST is still held.
            foreach (var missingStatus in new[] { true, false })
            {
                await Until(() => !controller.State.Loading && !controller.State.Reconciling && !controller.State.StatusLoading, "invalidation baseline");
                if (missingStatus)
                {
                    server.Backend = "s3";
                    server.Values[AdminStorageRules.Backend] = "s3";
                    server.Values["s3.public_bucket"] = "media";
                    await controller.ReloadAsync(preserveDraft: false);
                    await Until(() => controller.State.StatusKnown && !controller.State.StatusLoading && controller.State.ManagedTransitionsAvailable, "clean S3 baseline");
                    controller.ChangeBackend("local");
                    Check(controller.State.DirtyKeys.Count == 0, "S3-to-local review does not stage form keys");
                }
                else
                {
                    controller.Set(AdminStorageRules.LocalPath, "/mnt/invalidated-art");
                    await controller.SaveAsync();
                }
                await Until(() => page.ReviewDialog is { IsLoaded: true }, "invalidation review");
                var invalidatedReview = page.ReviewDialog!;
                controller.SetReviewPolicy("start_fresh");
                await Until(() => Button(invalidatedReview, "StorageReviewQueue").IsEnabled, "invalidation queue enabled");
                var invalidatedClosed = Closed(invalidatedReview);
                server.QueueGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                server.RejectQueue = !missingStatus;
                server.AmbiguousQueue = missingStatus;
                if (missingStatus)
                {
                    // Start the status read before Queue, but return its missing status afterward.
                    server.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var statusReads = server.Count("GET /api/v2/admin/server/status");
                    controller.RetryStatus();
                    await Until(() => server.Count("GET /api/v2/admin/server/status") > statusReads, "held status read");
                }
                var writesBefore = server.Count("POST /api/v2/admin/storage-transitions");
                var pendingQueue = controller.QueueTransitionAsync();
                try
                {
                    await Until(() => controller.State.Queuing && server.Count("POST /api/v2/admin/storage-transitions") > writesBefore, "held queue POST");
                    invalidatedReview.Hide();
                    await Task.Delay(100);
                    Check(!invalidatedClosed.IsCompleted && ReferenceEquals(page.ReviewDialog, invalidatedReview) &&
                        invalidatedReview.Content is ScrollViewer { Content: not null }, "user dismissal while queuing keeps the tracked review and content");
                    invalidatedReview.Closing += (_, args) => Check(!args.Cancel && ReferenceEquals(page.ReviewDialog, invalidatedReview) &&
                        invalidatedReview.Content is ScrollViewer { Content: not null }, "forced closure retains review ownership and content until Closed");
                    if (missingStatus)
                    {
                        server.MissingStatus = true;
                        server.StatusGate!.SetResult();
                    }
                    else
                    {
                        server.JobState = "succeeded";
                        controller.RefreshTransitions();
                    }
                    await invalidatedClosed.WaitAsync(TimeSpan.FromSeconds(6));
                    Check(controller.State.Queuing && controller.State.Review == null && page.ReviewDialog == null && ReferenceEquals(page.Controller, controller),
                        missingStatus ? "missing status closes the active review before Queue settles" : "commit notification closes the active review before Queue settles");
                    Check(invalidatedReview.Content is ScrollViewer { Content: null }, "closed invalidated review releases its shared content");
                    if (missingStatus)
                    {
                        Check(controller.State.DirtyKeys.Count == 0 && page.RequiresLeaveConfirmation && shell.RequiresLeaveConfirmation,
                            "a clean pending queue reaches the outer navigation guard after review invalidation");
                        Task<bool>? departure = null;
                        // Mirror MainWindow's outer-frame eligibility gate, not just a direct CanLeaveAsync call.
                        void GuardDeparture(object sender, NavigatingCancelEventArgs args)
                        {
                            if (!shell.RequiresLeaveConfirmation) return;
                            args.Cancel = true;
                            departure = shell.CanLeaveAsync();
                        }
                        outer.Navigating += GuardDeparture;
                        try
                        {
                            outer.Navigate(typeof(Page));
                            Check(departure != null && !await departure && ReferenceEquals(outer.Content, shell) &&
                                ReferenceEquals(page.Controller, controller) && controller.State.Owner?.IsCurrent == true && !pendingQueue.IsCompleted,
                                "outer-frame departure refuses to retire a clean in-flight transition");
                        }
                        finally { outer.Navigating -= GuardDeparture; }
                    }
                }
                finally
                {
                    server.StatusGate?.TrySetResult();
                    server.QueueGate.TrySetResult();
                    await pendingQueue;
                    server.StatusGate = null;
                    server.QueueGate = null;
                    server.RejectQueue = server.AmbiguousQueue = false;
                    server.MissingStatus = false;
                }
                if (missingStatus)
                    Check(controller.State.CommandUnknownKey == "configure.native.storage.transitionUnknown",
                        "the retained owner publishes the invalidated queue's unknown outcome");
                await Until(() => !controller.State.Loading && !controller.State.Queuing && !controller.State.Reconciling, "invalidated command settled");
                server.Backend = "local";
                server.Values[AdminStorageRules.Backend] = "";
                server.Values.Remove("s3.public_bucket");
                server.JobState = null;
                controller.RefreshTransitions();
                await Until(() => controller.State.LatestTransition == null, "invalidation job cleared");
                await controller.ReloadAsync(preserveDraft: false);
                await Until(() => controller.State.StatusKnown && !controller.State.StatusLoading, "invalidation status restored");
                Check(page.ReviewDialog == null, "command completion does not reopen the invalidated review");
                Check(!page.RequiresLeaveConfirmation && !shell.RequiresLeaveConfirmation && await shell.CanLeaveAsync(),
                    "a settled clean transition no longer blocks departure");
            }

            // An accepted command with a lost acknowledgement and failed discovery still offers Refresh.
            controller.Set(AdminStorageRules.LocalPath, "/mnt/ambiguous-art");
            await controller.SaveAsync();
            await Until(() => page.ReviewDialog is { IsLoaded: true }, "ambiguous review");
            var ambiguousClosed = Closed(page.ReviewDialog!);
            controller.SetReviewPolicy("start_fresh");
            server.AmbiguousQueue = server.FailJobs = true;
            var ambiguousWrites = server.Count("POST /api/v2/admin/storage-transitions");
            var discoveryReads = server.Count("GET /api/v2/admin/jobs");
            await controller.QueueTransitionAsync();
            await ambiguousClosed.WaitAsync(TimeSpan.FromSeconds(6));
            await Until(() => server.Count("GET /api/v2/admin/jobs") > discoveryReads &&
                Field<FrameworkElement>(page, "_unknownAlert").Visibility == Visibility.Visible, "ambiguous discovery failed");
            Check(controller.State.LatestTransition == null && Field<FrameworkElement>(page, "_activePanel").Visibility == Visibility.Collapsed &&
                Button(page, "StorageCommandUnknownRefresh").IsEnabled, "unknown command keeps a visible Refresh action without an active job");
            Check(!Text(page, "StorageCommandUnknownText").Text.Contains("was refreshed", StringComparison.Ordinal), "unknown notice does not claim discovery succeeded");
            server.FailJobs = server.AmbiguousQueue = false;
            server.JobState = "succeeded";
            server.Values[AdminStorageRules.LocalPath] = "/mnt/ambiguous-art";
            Invoke(Button(page, "StorageCommandUnknownRefresh"));
            await Until(() => controller.State.GetPersisted(AdminStorageRules.LocalPath) == "/mnt/ambiguous-art" &&
                !controller.State.Loading && !controller.State.Reconciling, "explicit Refresh discovers commitment");
            Check(server.Count("POST /api/v2/admin/storage-transitions") == ambiguousWrites + 1, "explicit discovery never resends the ambiguous command");
            server.JobState = null;
            controller.RefreshTransitions();
            await Until(() => controller.State.LatestTransition == null, "ambiguous job cleared");
            await controller.ReloadAsync(preserveDraft: false);

            Input(page, "Storage_artwork.local_path").Text = "/mnt/retiring-art";
            await Until(() => controller.State.IsDirty("artwork.local_path"), "retirement draft");
            Invoke(Button(page, "StorageSave"));
            await Until(() => page.ReviewDialog is { IsLoaded: true }, "retirement review");
            var retiringReview = page.ReviewDialog!;
            Radio(retiringReview, "StoragePolicy_start_fresh").IsChecked = true;
            await Until(() => Button(retiringReview, "StorageReviewQueue").IsEnabled, "retirement queue enabled");
            server.QueueGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var retirementClosed = Closed(retiringReview);
            try
            {
                Invoke(Button(retiringReview, "StorageReviewQueue"));
                await Until(() => controller.State.Queuing, "retirement queue in flight");
                page.SuspendAdminPage();
                await retirementClosed.WaitAsync(TimeSpan.FromSeconds(6));
                Check(page.Controller == null && page.ReviewDialog == null, "retiring Storage closes its in-flight queue review before services retire");
            }
            finally
            {
                server.QueueGate.TrySetResult();
                if (!retirementClosed.IsCompleted)
                {
                    retiringReview.Hide();
                    await retirementClosed.WaitAsync(TimeSpan.FromSeconds(6));
                }
            }
        }
        catch (Exception error)
        {
            Check(false, "Storage fixture flow: " + error.GetType().Name + " " + error.Message);
        }
        finally
        {
            // Barriers: every dialog Closed, then page and shell Unloaded, before globals or the provider go away.
            try
            {
                if (page?.ReviewDialog is { } open) { var closed = Closed(open); open.Hide(); await closed.WaitAsync(TimeSpan.FromSeconds(6)); }
                if (page != null && Field(page, "_dialog") is ContentDialog { } leaving) { var closed = Closed(leaving); leaving.Hide(); await closed.WaitAsync(TimeSpan.FromSeconds(6)); }
                var pageUnloaded = page is { IsLoaded: true } ? Unloaded(page) : Task.CompletedTask;
                var shellUnloaded = shell is { IsLoaded: true } ? Unloaded(shell) : Task.CompletedTask;
                if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
                window.Content = previousContent;
                await Task.WhenAll(pageUnloaded, shellUnloaded).WaitAsync(TimeSpan.FromSeconds(6));
                // Close runs on the dispatcher after Unloaded; wait for it before the provider goes away.
                await Until(() => page?.Controller == null, "Storage controller retired after Unloaded");
                Check(true, "teardown retires the Storage controller after the Unloaded barriers");
            }
            catch (Exception error) { Check(false, "Storage teardown barrier: " + error.GetType().Name + " " + error.Message); }
            finally
            {
                window.Content = previousContent;
                field.SetValue(null, previous);
            }
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Storage fixture failures: " + string.Join("; ", Failures));
    }

    // A dispatcher fence lets renders queued by TextChanging/LostFocus run before assertions or more typing.
    private static async Task RenderQueuedAsync(FrameworkElement element)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!element.DispatcherQueue.TryEnqueue(() => rendered.TrySetResult()))
            throw new InvalidOperationException("Could not queue the Storage render fence.");
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(6));
        element.UpdateLayout();
    }

    private static Task Closed(ContentDialog dialog)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += (_, _) => done.TrySetResult();
        return done.Task;
    }
    private static Task Unloaded(FrameworkElement element)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        element.Unloaded += (_, _) => done.TrySetResult();
        return done.Task;
    }
    private static void Select(ComboBox select, string tag) => select.SelectedItem = select.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == tag);
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static T Field<T>(object value, string name) => (T)Field(value, name)!;
    private static bool Visible(TextBlock text) { for (DependencyObject? e = text; e != null; e = VisualTreeHelper.GetParent(e)) if (e is UIElement { Visibility: Visibility.Collapsed }) return false; return true; }
    private static string[] Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Where(Visible).Select(t => t.Text).ToArray();
    private static TextBlock Text(DependencyObject root, string id) => Descendants(root).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == id);
    private static Button Button(DependencyObject root, string id) => Descendants(root).OfType<Button>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static TextBox Input(DependencyObject root, string id) => Descendants(root).OfType<TextBox>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static PasswordBox Password(DependencyObject root, string id) => Descendants(root).OfType<PasswordBox>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static ComboBox Combo(DependencyObject root, string id) => Descendants(root).OfType<ComboBox>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static RadioButton Radio(DependencyObject root, string id) => Descendants(root).OfType<RadioButton>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static FrameworkElement Group(DependencyObject root, string id) => Descendants(root).OfType<FrameworkElement>().First(c => AutomationProperties.GetAutomationId(c) == id);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(60);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
}
