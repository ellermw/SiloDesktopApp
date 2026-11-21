using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using R = SiloPlayer.Core.Services.Admin.AdminDownloadSettingsRules;

/// <summary>Production Manage > Downloads > Settings registration and Frame entry using synthetic server data only.</summary>
internal static class AdminDownloadSettingsNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000)
    {
        for (var i = 0; i < milliseconds / 25 && !condition(); i++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly Dictionary<string, string> Values = new()
        {
            ["download.enabled"] = "true", ["download.transcode_enabled"] = "false", ["download.max_concurrent_prepares"] = "0",
            ["download.artifact_dir"] = "", ["download.artifact_max_bytes"] = "53687091200", ["download.artifact_disk_ceiling_percent"] = "85",
            ["download.server_bandwidth_mbps"] = "0", ["download.user_bandwidth_mbps"] = "50", ["download.max_concurrent_per_user"] = "3",
            ["download.max_per_period"] = "0", ["download.period_duration"] = "24h", ["download.local_transcode_fallback"] = "true",
            ["download.artifact_cache_hours"] = "72", ["playback.transcode_dir"] = "/mnt/fast/transcode", ["branding.server_name"] = "Synthetic Silo",
        };
        internal readonly List<string> Requests = [];
        internal int Saves, Version = 1;
        internal bool Conflict, ReadForbidden, EffectiveFailure, SensitiveFailure, UnknownOutcome;
        internal string[] Managed = [];
        /// <summary>False models an older server that sends the managed list but no managed_by_env_vars map.</summary>
        internal bool NamedVariables = true;
        internal TaskCompletionSource? SaveGate;
        internal string? LastIfMatch, LastPut;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests) Requests.Add(request.Method + " " + path);
            if (path.EndsWith("/effective", StringComparison.Ordinal))
            {
                if (ReadForbidden) return Json(new { type = "permission_denied", title = "Forbidden", status = 403 }, HttpStatusCode.Forbidden);
                if (EffectiveFailure) return Json(new { title = "Unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable);
            }
            object body;
            if (request.Method == HttpMethod.Put)
            {
                Saves++; LastIfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                LastPut = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var data = JsonDocument.Parse(LastPut);
                if (SaveGate is { } pending) await pending.Task.WaitAsync(cancellationToken);
                if (UnknownOutcome) throw new HttpRequestException("synthetic transport failure");
                if (Conflict) return Json(new { type = "precondition_failed", title = "Settings changed", status = 412 }, HttpStatusCode.PreconditionFailed);
                var updated = new Dictionary<string, string>();
                foreach (var entry in data.RootElement.GetProperty("values").EnumerateObject())
                    updated[entry.Name] = Values[entry.Name] = entry.Value.GetString()!.TrimStart('+');
                Version++;
                body = new { values = updated, restart_required = updated.ContainsKey("download.artifact_dir") };
            }
            else if (path.EndsWith("/sensitive-status", StringComparison.Ordinal))
            {
                if (SensitiveFailure) return Json(new { title = "Status unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable);
                body = NamedVariables ? new { configured = Array.Empty<string>(), managed_by_env = Managed, managed_by_env_vars = Managed.ToDictionary(key => key, key => "SILO_" + key.ToUpperInvariant().Replace('.', '_')) }
                    : (object)new { configured = Array.Empty<string>(), managed_by_env = Managed };
            }
            else body = path switch
            {
                "/api/v2/admin/settings/effective" => Values,
                "/api/v2/admin/settings/restart-keys" => new { keys = new[] { "download.artifact_dir" }, prefixes = Array.Empty<string>() },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                _ => new { },
            };
            return Json(body);
        }
        private HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new($"\"downloads-fixture-v{Version}\""); return response;
        }
    }

    private static readonly string[] EssentialLabels =
    [
        "Allow downloads", "Prepare device-friendly copies", "Files prepared at once", "Prepared file directory",
        "Default storage budget per location", "Disk ceiling (%)", "Server bandwidth",
    ];
    private static readonly string[] AdvancedLabels =
    [
        "Per-user bandwidth", "Downloads at once per user", "Downloads per period", "Period length",
        "Prepare locally when workers are unavailable", "Keep cached files for",
    ];

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
        // The signed-in user's own local settings stay in this fixture's private directory; the server page must not touch them.
        var localSettings = new SettingsService(Program.ResultDirectory);
        var preferenceDirectory = Path.Combine(Program.ResultDirectory, "downloads-ui");
        if (Directory.Exists(preferenceDirectory)) Directory.Delete(preferenceDirectory, true);
        var preferences = new LoggingDisclosurePreferences(preferenceDirectory);
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation)
            .AddSingleton(new ToastService()).AddSingleton(localSettings).AddSingleton(preferences);
        // Invoke the same registration entry point as App, rather than registering a test-only page.
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        var localBefore = Snapshot(Program.ResultDirectory, "settings");
        Frame? outer = null;
        try
        {
            field.SetValue(null, services);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            var resolved = AdminShellPage.TryOpenHref("/admin/settings/downloads");
            if (resolved is not { Kind: AdminRouteResolutionKind.Route })
                throw new InvalidOperationException("Downloads settings are not available through the production Manage registration.");
            for (var i = 0; i < 160 && outer.Content is not AdminShellPage; i++) await Task.Delay(25);
            if (outer.Content is not AdminShellPage shell) throw new InvalidOperationException("Downloads settings did not enter the production Admin shell.");
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            for (var i = 0; i < 160 && inner.Content?.GetType().Name != "AdminDownloadSettingsPage"; i++) await Task.Delay(25);
            if (inner.Content is not AdminDownloadSettingsPage page) throw new InvalidOperationException("Downloads settings did not activate their production WinUI Page.");
            await Until(() => page.Controller?.State.Loaded == true, "Downloads loaded");
            var controller = page.Controller!;
            await Until(() => Toggle(page, R.Enabled).IsOn, "fields rendered");
            Check(true, "Downloads settings activate through production registration, shell and Frame");
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.StartsWith("web.", StringComparison.Ordinal) || t.Text.StartsWith("configure.", StringComparison.Ordinal)),
                "rendered Downloads settings and navigation labels have catalog text");
            var shellText = Descendants(shell).OfType<TextBlock>().Where(t => !IsInside(t, page)).Select(t => t.Text).ToArray();
            Check(shellText.Contains("Downloads") && shellText.Contains("Settings"), "sidebar shows the Downloads topic with its Settings leaf");
            Check(AdminNavigation.SearchGroups("disk ceiling", AdminShellPage.CreateRegistry().IsRegistered).SelectMany(g => g.Pages).Single().Route == AdminRoute.DownloadSettings,
                "registered Downloads settings are searchable through their official keywords");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Downloads navigation keeps page transitions suppressed");
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Offline downloads, per-user and server-wide limits, and prepared copies."),
                "page header carries the official Downloads description");
            Check(Descendants(page).OfType<TextBlock>().Count(t => t.Text == "Downloads") >= 2, "page title and one Downloads group heading are shown");
            var storage = Descendants(page).OfType<HyperlinkButton>().Single(b => AutomationProperties.GetAutomationId(b) == "DownloadsStorageLink");
            Check(AutomationProperties.GetName(storage) == "See storage on the server and nodes" && !storage.IsEnabled &&
                !AdminShellPage.CreateRegistry().IsRegistered(AdminRoute.PreparedFiles),
                "the storage link names the official text and is unavailable until its Prepared Files page is registered");

            // Order, labels and the collapsed Advanced section.
            var ids = EditorIds(page);
            Check(ids.Take(7).SequenceEqual(EssentialLabels), "the seven essential rows keep the official labels and order");
            var disclosure = Descendants(page).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "DownloadsAdvanced");
            Check(AutomationProperties.GetName(disclosure) == "Advanced · 6 settings", "Advanced disclosure names its setting count");
            Check(Rail(page).Visibility == Visibility.Collapsed && Box(page, R.UserBandwidth) == null, "Advanced starts collapsed with no advanced editors shown");

            // Toggles, number fields and the zero/default semantics.
            var enabled = Toggle(page, R.Enabled);
            Check(AutomationProperties.GetName(enabled) == "Allow downloads" && enabled.IsOn, "Allow downloads shows the saved state");
            enabled.IsOn = false; await Until(() => controller.State.DirtyKeys.Contains(R.Enabled), "toggle staged");
            enabled.IsOn = true; await Until(() => controller.State.DirtyKeys.Count == 0, "toggle restored");
            Check(true, "returning a toggle to its saved value clears its dirty state");
            var prepares = Box(page, R.MaxConcurrentPrepares)!;
            Check(prepares.Text == "0" && prepares.IsEnabled && Descendants(page).OfType<TextBlock>().Any(t => t.Text == "0 uses the built-in default of 2."),
                "Files prepared at once keeps 0 as a number with its built-in-default description, not Unlimited");
            Check(Unlimited(page, R.MaxConcurrentPrepares) == null, "Files prepared at once has no Unlimited switch");

            // Scaled GB budget: display, typing, fractions, Unlimited and restore.
            var budget = Box(page, R.ArtifactMaxBytes)!;
            Check(budget.Text == "53.687" && !Descendants(page).OfType<TextBlock>().Any(t => t.Text.Contains("53687091200", StringComparison.Ordinal)),
                "the byte budget is shown as decimal GB, never as raw bytes");
            Check(UnitLabel(page, budget) == "GB", "the budget row shows its GB unit in the trailing slot");
            var budgetZeroUnlimited = Unlimited(page, R.ArtifactMaxBytes)!;
            budget.Focus(FocusState.Programmatic);
            foreach (var draft in new[] { "0", "0.", "0.5" })
            {
                budget.Text = draft;
                await Task.Delay(120); // Let the queued render run between individual keystrokes.
                Check(budget.Text == draft && budget.IsEnabled && budgetZeroUnlimited.IsChecked == false,
                    $"typing {draft} GB preserves the draft without selecting Unlimited");
            }
            Check(controller.State.Values[R.ArtifactMaxBytes] == "500000000" && !controller.State.Errors.ContainsKey(R.ArtifactMaxBytes),
                "typing 0.5 GB one keystroke at a time stages a finite half-GB cap");
            foreach (var draft in new[] { "0.0000000004", "-0.0000000004" })
            {
                budget.Text = draft;
                await Task.Delay(120);
                Check(budget.Text == draft && budget.IsEnabled && budgetZeroUnlimited.IsChecked == false &&
                    controller.State.Errors.ContainsKey(R.ArtifactMaxBytes) && !Button(page, "DownloadsSave").IsEnabled,
                    $"sub-byte draft {draft} is rejected inline, never converted to Unlimited");
                var savesBeforeZero = server.Saves;
                await controller.SaveAsync();
                Check(server.Saves == savesBeforeZero, "a rounded-to-zero limit cannot be saved");
            }
            await Click(page, "DownloadsDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "discard zero drafts");
            await Task.Delay(120);
            Check(budget.Text == "53.687" && budgetZeroUnlimited.IsChecked == false, "Discard clears an invalid zero draft");
            budget.Text = "100"; await Until(() => controller.State.Values[R.ArtifactMaxBytes] == "100000000000", "typed GB stored as bytes");
            budget.Text = "1.5"; await Until(() => controller.State.Values[R.ArtifactMaxBytes] == "1500000000", "fraction stored exactly");
            await Task.Delay(120);
            Check(budget.Text == "1.5", "a fractional entry is not rewritten mid-keystroke");
            budget.Text = "1."; await Until(() => controller.State.Values[R.ArtifactMaxBytes] == "1000000000", "trailing point");
            await Task.Delay(120);
            Check(budget.Text == "1.", "a trailing decimal point survives a render");
            var budgetUnlimited = Unlimited(page, R.ArtifactMaxBytes)!;
            Check(AutomationProperties.GetName(budgetUnlimited) == "Unlimited" && budgetUnlimited.IsChecked == false, "budget shows an unchecked Unlimited switch with its official name");
            Invoke(budgetUnlimited);
            await Until(() => controller.State.Values[R.ArtifactMaxBytes] == "0", "Unlimited writes the zero sentinel");
            await Task.Delay(120);
            Check(budgetUnlimited.IsChecked == true && !budget.IsEnabled && budget.Text == "" && budget.PlaceholderText == "Unlimited", "Unlimited disables the box and shows the Unlimited placeholder");
            Invoke(budgetUnlimited);
            await Until(() => controller.State.Values[R.ArtifactMaxBytes] == "1000000000", "unchecking restores the previous limit");
            await Task.Delay(120);
            Check(budget.IsEnabled && budget.Text == "1", "unchecking Unlimited restores the limit it replaced");
            Invoke(budgetUnlimited); await Until(() => controller.State.Values[R.ArtifactMaxBytes] == "0", "unlimited again");
            budget.Text = "";
            await Click(page, "DownloadsDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "discard budget edits");
            await Task.Delay(120);
            Check(budget.Text == "53.687" && budgetUnlimited.IsChecked == false, "Discard restores the saved GB value");
            var bandwidth = Box(page, R.ServerBandwidth)!;
            var bandwidthUnlimited = Unlimited(page, R.ServerBandwidth)!;
            Check(bandwidthUnlimited.IsChecked == true && !bandwidth.IsEnabled && bandwidth.PlaceholderText == "Unlimited" && UnitLabel(page, bandwidth) == "Mbps",
                "a saved zero bandwidth reads as Unlimited with its Mbps unit");
            Invoke(bandwidthUnlimited);
            await Until(() => controller.State.Values[R.ServerBandwidth] == "", "unchecking with nothing remembered leaves an empty box");
            await Task.Delay(120);
            Check(bandwidth.IsEnabled && bandwidth.Text == "" && controller.State.Errors.ContainsKey(R.ServerBandwidth) &&
                Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Enter a number or choose Unlimited." && t.Visibility == Visibility.Visible) && !Button(page, "DownloadsSave").IsEnabled,
                "an empty limit box explains itself and blocks Save");
            bandwidth.Text = "250"; await Until(() => controller.State.Values[R.ServerBandwidth] == "250", "bandwidth typed");
            await Task.Delay(120);
            Check(!controller.State.Errors.ContainsKey(R.ServerBandwidth) && Button(page, "DownloadsSave").IsEnabled, "a valid limit clears the error and allows Save");
            await Click(page, "DownloadsDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "discard bandwidth");

            // Disk ceiling bounds and inline errors.
            var ceiling = Box(page, R.DiskCeilingPercent)!;
            Check(ceiling.Text == "85", "disk ceiling shows the saved percentage");
            ceiling.Text = "99"; await Until(() => controller.State.Values[R.DiskCeilingPercent] == "99", "ceiling typed");
            await Task.Delay(120);
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Enter a whole number from 50 to 95." && t.Visibility == Visibility.Visible) && !Button(page, "DownloadsSave").IsEnabled,
                "a ceiling above 95 reports the official 50 to 95 range and blocks Save");
            var puts = server.Saves;
            await controller.SaveAsync();
            Check(server.Saves == puts, "an invalid value is never sent to the server");
            ceiling.Text = "90"; await Until(() => controller.State.Errors.Count == 0, "ceiling valid");
            await Click(page, "DownloadsDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "discard ceiling");

            // Prepared file directory: derived placeholder, reset and restart chip.
            var directory = Box(page, R.ArtifactDir)!;
            Check(directory.PlaceholderText == "/mnt/fast/silo-download-artifacts" && directory.Text == "", "a blank directory shows the derived default as its placeholder");
            var reset = Button(page, "Downloads_download.artifact_dir_Reset");
            Check(reset.Visibility == Visibility.Collapsed, "no reset is offered while the directory runs the default");
            var restartBadges = Descendants(page).OfType<Border>().Where(b => AutomationProperties.GetName(b) == "Restart" && b.Visibility == Visibility.Visible).ToArray();
            Check(restartBadges.Length == 1, "the restart chip appears only on the server-marked directory row");
            directory.Text = "/mnt/downloads"; await Until(() => controller.State.Values[R.ArtifactDir] == "/mnt/downloads", "directory typed");
            await Task.Delay(120);
            Check(reset.Visibility == Visibility.Visible && AutomationProperties.GetName(reset) == "Reset Prepared file directory to default", "an overridden directory offers a named Reset to default");
            await CaptureAsync(outer, "downloads-directory-overridden.png");
            directory.Text = "downloads/relative"; await Until(() => controller.State.Errors.ContainsKey(R.ArtifactDir), "relative directory flagged");
            await Task.Delay(120);
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Enter an absolute path, or leave blank for the default."), "a relative directory asks for an absolute path");
            directory.Text = "/mnt/downloads"; await Until(() => !controller.State.Errors.ContainsKey(R.ArtifactDir), "absolute again");
            Invoke(reset);
            await Until(() => controller.State.DirtyKeys.Count == 0 && controller.State.Values[R.ArtifactDir] == "", "reset restores the blank default");
            await Task.Delay(120);
            Check(directory.Text == "" && reset.Visibility == Visibility.Collapsed, "Reset to default returns to the derived placeholder and hides itself");

            // Advanced disclosure, persistence and forced-open.
            Invoke(disclosure);
            await Until(() => Rail(page).Visibility == Visibility.Visible, "advanced opened");
            Check(File.Exists(Path.Combine(preferenceDirectory, "downloads.pref")) && preferences.Read("downloads"), "opening Advanced persists the choice");
            ids = EditorIds(page);
            Check(ids.SequenceEqual(EssentialLabels.Concat(AdvancedLabels)), "all thirteen rows appear in the official order with Advanced open");
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "PER USER") && Descendants(page).OfType<TextBlock>().Any(t => t.Text == "WHOLE SERVER") &&
                AutomationProperties.GetName(Descendants(page).OfType<TextBlock>().First(t => t.Text == "PER USER")) == "Per user",
                "per-user and whole-server subheadings are present, quiet and named in sentence case");
            Check(Box(page, R.UserBandwidth)!.Text == "50" && UnitLabel(page, Box(page, R.UserBandwidth)!) == "Mbps" && Box(page, R.MaxConcurrentPerUser)!.Text == "3" &&
                Unlimited(page, R.MaxPerPeriod)!.IsChecked == true && Box(page, R.PeriodDuration)!.Text == "24h" && Toggle(page, R.LocalTranscodeFallback).IsOn && Box(page, R.CacheHours)!.Text == "72",
                "every advanced field shows its saved value");
            // Every unscaled limit must reject alternative zero spellings without selecting Unlimited.
            foreach (var key in new[] { R.ServerBandwidth, R.UserBandwidth, R.MaxConcurrentPerUser, R.MaxPerPeriod })
            {
                var limitBox = Box(page, key)!;
                var unlimited = Unlimited(page, key)!;
                if (unlimited.IsChecked == true)
                {
                    Invoke(unlimited);
                    // Unchecking restores the limit the control last showed (the official LimitField's remembered limit),
                    // which is blank when none was set and the discarded draft when one was; either way it is no longer Unlimited.
                    await Until(() => unlimited.IsChecked == false && limitBox.IsEnabled && controller.State.Values[key] != R.Unlimited, "uncheck " + key);
                    await Task.Delay(120);
                }
                foreach (var draft in new[] { "00", "+0", "-0" })
                {
                    limitBox.Text = "100";
                    await Until(() => controller.State.Values[key] == "100", "finite limit " + key);
                    await Task.Delay(120);
                    if (draft == "00")
                    {
                        limitBox.Select(0, 1);
                        limitBox.SelectedText = ""; // Deleting 1 from 100 must not remove the quota.
                    }
                    else limitBox.Text = draft;
                    await Task.Delay(120);
                    Check(limitBox.Text == draft && limitBox.IsEnabled && unlimited.IsChecked == false &&
                        controller.State.Values[key] == "" && controller.State.Errors.TryGetValue(key, out var error) &&
                        error.Problem == AdminDownloadFieldProblem.Blank && !Button(page, "DownloadsSave").IsEnabled,
                        $"typed {draft} on {key} remains editable and blocks Save, never Unlimited");
                    var savesBeforeZero = server.Saves;
                    await controller.SaveAsync();
                    Check(server.Saves == savesBeforeZero, $"typed {draft} on {key} cannot reach the server");
                }
                Invoke(unlimited);
                await Until(() => controller.State.Values[key] == R.Unlimited, "explicit Unlimited " + key);
                await Task.Delay(120);
                Check(unlimited.IsChecked == true && !limitBox.IsEnabled && !controller.State.Errors.ContainsKey(key),
                    "only explicitly choosing Unlimited accepts zero on " + key);
                controller.Discard();
                await Until(() => controller.State.DirtyKeys.Count == 0, "discard zero spellings " + key);
                await Task.Delay(120);
            }
            var userBandwidth = Box(page, R.UserBandwidth)!;
            var userUnlimited = Unlimited(page, R.UserBandwidth)!;
            userBandwidth.Focus(FocusState.Programmatic);
            userBandwidth.Text = "10"; await Task.Delay(120);
            userBandwidth.Select(0, 1);
            userBandwidth.SelectedText = ""; // Delete the leading 1, leaving 0 between edits.
            await Task.Delay(120);
            Check(userBandwidth.Text == "0" && userBandwidth.IsEnabled && userUnlimited.IsChecked == false &&
                controller.State.Errors.ContainsKey(R.UserBandwidth) && !Button(page, "DownloadsSave").IsEnabled,
                "deleting 10 down to 0 keeps the limit editor enabled and blocks a silent Unlimited save");
            prepares.Focus(FocusState.Programmatic); await Task.Delay(120);
            Check(userBandwidth.Text == "0" && userUnlimited.IsChecked == false,
                "leaving a zero draft does not silently select Unlimited");
            userBandwidth.Text = "50"; await Until(() => !controller.State.DirtyKeys.Contains(R.UserBandwidth), "finish bandwidth edit");
            userBandwidth.Text = "10"; await Task.Delay(120);
            controller.Set(R.UserBandwidth, R.Unlimited); await Task.Delay(120);
            Check(userUnlimited.IsChecked == true && !userBandwidth.IsEnabled, "an external Unlimited value renders normally");
            Invoke(userUnlimited); await Task.Delay(120);
            Check(userBandwidth.Text == "10" && userBandwidth.IsEnabled && controller.State.Values[R.UserBandwidth] == "10",
                "unchecking a programmatically selected Unlimited restores the previous limit");
            await Click(page, "DownloadsDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "discard bandwidth regression edits");
            await Task.Delay(120);
            var cacheBox = Box(page, R.CacheHours)!;
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Rolling window each user's count is measured over, e.g. 24h or 168h."), "period length shows its official description");
            Invoke(disclosure); await Until(() => Rail(page).Visibility == Visibility.Collapsed, "manual collapse");
            Check(!preferences.Read("downloads"), "collapsing Advanced persists the choice");
            controller.Set(R.CacheHours, "100");
            await Until(() => Rail(page).Visibility == Visibility.Visible, "dirty advanced field forces the section open");
            Check(true, "a dirty advanced field forces the section open");
            Invoke(disclosure); await Until(() => Rail(page).Visibility == Visibility.Collapsed, "manual collapse while forced");
            await Task.Delay(120);
            Check(Rail(page).Visibility == Visibility.Collapsed, "a manual collapse outranks the current reason to force it open");
            // The reason clears (saved/discarded), then a different advanced field goes dirty: the stale manual collapse must not hide it.
            controller.Discard(); await Until(() => controller.State.DirtyKeys.Count == 0, "advanced edits discarded");
            await Task.Delay(120);
            Check(Rail(page).Visibility == Visibility.Collapsed, "clearing the reason leaves the manual collapse in place");
            controller.Set(R.MaxPerPeriod, "5");
            await Until(() => Rail(page).Visibility == Visibility.Visible, "a new reason re-expands the section");
            Check(true, "a new reason to force the section open re-expands it after a manual collapse");
            controller.Discard(); await Until(() => controller.State.DirtyKeys.Count == 0, "advanced edits discarded again");
            await Until(() => Rail(page).Visibility == Visibility.Collapsed, "the section closes once nothing forces it open");
            Invoke(disclosure); await Until(() => Rail(page).Visibility == Visibility.Visible, "reopen advanced"); // persisted open for the layout checks

            // A valid saved unlimited quota may have a zero period. Limiting only the quota must explain
            // the dependency on the untouched period, rather than silently disabling Save.
            server.Values[R.PeriodDuration] = "0s";
            await controller.ReloadAsync(false);
            await Until(() => !controller.State.Loading && controller.State.Values[R.PeriodDuration] == "0s", "saved zero period loaded");
            var quotaDependency = Box(page, R.MaxPerPeriod)!;
            Invoke(Unlimited(page, R.MaxPerPeriod)!);
            // Unchecking restores whatever limit the control last remembered (blank when none), so wait for "no longer Unlimited".
            await Until(() => Unlimited(page, R.MaxPerPeriod)!.IsChecked == false && quotaDependency.IsEnabled && controller.State.Values[R.MaxPerPeriod] != R.Unlimited, "saved unlimited quota unchecked");
            quotaDependency.Text = "5";
            await Until(() => controller.State.Errors.ContainsKey(R.PeriodDuration), "quota requires positive saved period");
            await Task.Delay(120);
            Check(!controller.State.DirtyKeys.Contains(R.PeriodDuration) &&
                Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Set a period longer than zero while Downloads per period is limited." && IsLaidOut(t)) &&
                !Button(page, "DownloadsSave").IsEnabled,
                "limiting a saved unlimited quota explains the clean period dependency beside the period");
            controller.Discard();
            server.Values[R.PeriodDuration] = "24h";
            await controller.ReloadAsync(false);
            await Until(() => !controller.State.Loading && controller.State.Values[R.PeriodDuration] == "24h", "saved positive period restored");

            // Per-user period pair, duration grammar and the local-fallback toggle.
            var perPeriod = Box(page, R.MaxPerPeriod)!;
            var periodUnlimited = Unlimited(page, R.MaxPerPeriod)!;
            Invoke(periodUnlimited); await Until(() => periodUnlimited.IsChecked == false && perPeriod.IsEnabled && controller.State.Values[R.MaxPerPeriod] != R.Unlimited, "per-period unchecked");
            perPeriod.Text = "5"; await Until(() => controller.State.Values[R.MaxPerPeriod] == "5", "per-period typed");
            var period = Box(page, R.PeriodDuration)!;
            period.Text = "0s"; await Until(() => controller.State.Errors.ContainsKey(R.PeriodDuration), "zero period flagged");
            await Task.Delay(120);
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "The period must be longer than zero.") && !Button(page, "DownloadsSave").IsEnabled, "limiting downloads per period with a zero period is blocked on the period");
            period.Text = "soon"; await Until(() => controller.State.Errors.TryGetValue(R.PeriodDuration, out var e) && e.Problem == AdminDownloadFieldProblem.NotDuration, "invalid duration");
            await Task.Delay(120);
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Enter a duration such as 24h, 168h or 7d."), "an invalid period reports the accepted grammar");
            period.Text = "7d"; await Until(() => controller.State.Errors.Count == 0, "7d valid");
            var fallback = Toggle(page, R.LocalTranscodeFallback);
            fallback.IsOn = false; await Until(() => controller.State.Values[R.LocalTranscodeFallback] == "false", "fallback off");
            Check(controller.State.DirtyKeys.Contains(R.LocalTranscodeFallback), "the local fallback toggle stages false");
            await CaptureAsync(outer, "downloads-advanced-dirty.png");

            // Guarded save with a late edit.
            Check(controller.State.DirtyKeys.Count == 3 && Descendants(page).OfType<TextBlock>().Any(t => t.Text == "3 unsaved changes"), "the save bar counts three unsaved changes");
            Check(Descendants(page).OfType<Border>().Count(b => AutomationProperties.GetName(b) == "Unsaved change" && b.Visibility == Visibility.Visible) == 0 &&
                Descendants(page).OfType<Border>().Count(b => AutomationProperties.GetName(b) == "Unsaved changes in this group" && b.Visibility == Visibility.Visible) == 1,
                "the group shows its unsaved dot and rows show none, as the official Downloads rows do");
            server.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var saves = server.Saves;
            await Click(page, "DownloadsSave"); await Until(() => controller.State.Saving && server.Saves > saves, "save in flight");
            await Until(() => !Button(page, "DownloadsSave").IsEnabled, "save state rendered");
            Check(!await page.CanLeaveAsync(), "navigation cannot discard or depart during a current save");
            Check(cacheBox.IsEnabled, "other fields stay editable during a save");
            cacheBox.Text = "99";
            await Until(() => controller.State.Values[R.CacheHours] == "99", "late edit reached controller");
            server.SaveGate.SetResult(); server.SaveGate = null;
            await Until(() => !controller.State.Saving, "save settled");
            await Task.Delay(150);
            Check(server.Saves == saves + 1 && server.LastIfMatch == "\"downloads-fixture-v1\"", "native Save sends one strong version-guarded write");
            using (var sent = JsonDocument.Parse(server.LastPut!))
            {
                var keys = sent.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name).Order().ToArray();
                Check(keys.SequenceEqual(new[] { R.LocalTranscodeFallback, R.MaxPerPeriod, R.PeriodDuration }.Order()), "the write carries only the dirty Downloads keys");
            }
            Check(controller.State.Values[R.CacheHours] == "99" && controller.State.DirtyKeys.SequenceEqual([R.CacheHours]) && controller.State.PersistedValues[R.CacheHours] == "72",
                "a late edit stays dirty over the acknowledged canonical values");
            Check(controller.State.PersistedValues[R.PeriodDuration] == "7d" && controller.State.PersistedValues[R.MaxPerPeriod] == "5", "acknowledged values become the saved baseline");
            await Click(page, "DownloadsDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "discard late edit");
            await Task.Delay(120);

            // Restart-required acknowledgement after saving the directory.
            directory.Text = "/mnt/saved-downloads"; await Until(() => controller.State.DirtyKeys.Contains(R.ArtifactDir), "directory staged for save");
            await Click(page, "DownloadsSave"); await Until(() => controller.State.PersistedValues[R.ArtifactDir] == "/mnt/saved-downloads", "directory saved");
            await Task.Delay(150);
            Check(controller.State.RestartRequired, "saving the directory reports that a restart is required");

            // Dirty departure dialog.
            cacheBox.Text = "55"; await Until(() => controller.State.DirtyKeys.Count > 0, "dialog draft");
            shell.Navigate(new(AdminRoute.Activity));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "dirty leave dialog");
            var dialog = (ContentDialog)Field(page, "_dialog")!;
            await CaptureAsync(dialog, "downloads-discard-dialog.png");
            var cancelButton = DialogButton(dialog, "Cancel");
            var discardButton = DialogButton(dialog, "Discard");
            Check(cancelButton.TransformToVisual(dialog).TransformPoint(new(0, 0)).X < discardButton.TransformToVisual(dialog).TransformPoint(new(0, 0)).X, "dirty dialog places Cancel before Discard");
            Check(cancelButton.FocusState != FocusState.Unfocused, "dirty dialog starts on Cancel");
            Invoke(cancelButton); await Task.Delay(150);
            Check(ReferenceEquals(inner.Content, page) && controller.State.DirtyKeys.Count > 0, "Cancel retains the Downloads page and its draft");

            // Layout at the required viewports.
            foreach (var (width, height) in new[] { (1440, 900), (1440, 600), (1920, 600), (800, 600) })
            {
                window.AppWindow.ResizeClient(new(width, height)); await Task.Delay(250); outer.UpdateLayout();
                var scroller = (ScrollViewer)Field(page, "_scroller")!;
                scroller.ChangeView(null, 0, null, true); await Task.Delay(100);
                Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, $"Downloads has no horizontal scroll overflow at {width}x{height}");
                var viewportLeft = scroller.TransformToVisual(null).TransformPoint(new(0, 0)).X;
                var gutter = width >= 1280 ? 40 : width >= 1024 ? 32 : width >= 640 ? 24 : 16;
                var available = scroller.ActualWidth - 2 * gutter;
                var expectedLeft = viewportLeft + gutter + Math.Max(0, available - AdminShellPage.ContentMaxWidth) / 2;
                var bodyLeft = ((FrameworkElement)Field(page, "_body")!).TransformToVisual(null).TransformPoint(new(0, 0)).X;
                Program.Log($"TRACE downloads width={width} viewport={scroller.ActualWidth} bodyLeft={bodyLeft} expectedLeft={expectedLeft}");
                Check(Math.Abs(bodyLeft - expectedLeft) < 1.5, $"Downloads settings column starts at the shared frame gutter at {width}x{height}");
                foreach (var edge in new[] { 8d, scroller.ActualWidth - 18 })
                {
                    var point = scroller.TransformToVisual(null).TransformPoint(new(edge, scroller.ActualHeight / 2));
                    DependencyObject? target = VisualTreeHelper.FindElementsInHostCoordinates(point, outer).FirstOrDefault();
                    while (target != null && target is not ScrollViewer) target = VisualTreeHelper.GetParent(target);
                    Check(ReferenceEquals(target, scroller), $"Downloads side gutter has the page scrolling ancestor at {width}x{height}");
                }
                var boxes = Descendants(page).OfType<TextBox>().Where(c => AutomationProperties.GetAutomationId(c).StartsWith("Downloads_", StringComparison.Ordinal) && c.IsLoaded && c.ActualWidth > 0).ToArray();
                var edges = boxes.Select(c => c.TransformToVisual(page).TransformPoint(new(0, 0)).X + c.ActualWidth).ToArray();
                Program.Log($"TRACE downloads right edges {width}: {string.Join(",", edges.Select(e => e.ToString("0.#")))}");
                if (width >= 640) Check(edges.Max() - edges.Min() < 1.5, $"every settings control ends on the shared right edge at {width}x{height}");
                foreach (var control in Descendants(page).OfType<Control>().Where(c => AutomationProperties.GetAutomationId(c).StartsWith("Downloads_", StringComparison.Ordinal) && c.IsLoaded && c.ActualWidth > 0 && c.Visibility == Visibility.Visible))
                {
                    var point = control.TransformToVisual(page).TransformPoint(new(0, 0));
                    Check(point.X >= -1 && point.X + control.ActualWidth <= page.ActualWidth + 1, $"{AutomationProperties.GetAutomationId(control)} stays inside the viewport at {width}x{height}");
                    Check(AutomationProperties.GetName(control).Length > 0, $"{AutomationProperties.GetAutomationId(control)} has an accessible name");
                    if (AutomationProperties.GetLabeledBy(control) is FrameworkElement label)
                    {
                        DependencyObject? row = VisualTreeHelper.GetParent(control);
                        while (row != null && row.GetType().Name != "AdminSettingsRow") row = VisualTreeHelper.GetParent(row);
                        if (row is UIElement fieldRow) Check(label.TransformToVisual(fieldRow).TransformPoint(new(0, 0)).X <= 15, $"label for {AutomationProperties.GetAutomationId(control)} stays on its row's left edge at {width}x{height}");
                    }
                }
                foreach (var box in boxes) Check(box.BorderBrush is SolidColorBrush { Color.A: 64 }, $"{AutomationProperties.GetAutomationId(box)} uses the official settings-row border opacity at {width}x{height}");
                foreach (var gb in new[] { R.ArtifactMaxBytes, R.ServerBandwidth, R.UserBandwidth })
                {
                    var limitBox = Box(page, gb)!;
                    var unit = UnitText(page, limitBox);
                    if (unit != null && width >= 640)
                    {
                        var boxRect = limitBox.TransformToVisual(page).TransformPoint(new(0, 0));
                        var unitRect = unit.TransformToVisual(page).TransformPoint(new(0, 0));
                        Check(unitRect.X >= boxRect.X + limitBox.ActualWidth - 1 && Math.Abs((unitRect.Y + unit.ActualHeight / 2) - (boxRect.Y + limitBox.ActualHeight / 2)) < 5,
                            $"{gb} unit sits after the control, centered on it at {width}x{height}");
                    }
                }
                var link = Descendants(page).OfType<HyperlinkButton>().Single();
                Check(link.TransformToVisual(page).TransformPoint(new(0, 0)).X + link.ActualWidth <= page.ActualWidth + 1, $"storage link stays inside the viewport at {width}x{height}");
                await CaptureAsync(outer, $"downloads-{width}x{height}.png");
                scroller.ChangeView(null, scroller.ScrollableHeight, null, true); await Task.Delay(150);
                await CaptureAsync(outer, $"downloads-{width}x{height}-bottom.png");
                scroller.ChangeView(null, 0, null, true); await Task.Delay(50);
            }
            window.AppWindow.ResizeClient(new(1440, 900)); await Task.Delay(250);

            // Control state shapes.
            var stateScroller = (ScrollViewer)Field(page, "_scroller")!;
            outer.UpdateLayout();
            directory.Text = "/mnt/state-capture"; await Until(() => controller.State.Values[R.ArtifactDir] == "/mnt/state-capture", "directory for state captures");
            stateScroller.ChangeView(null, 0, null, true); await Task.Delay(150);
            foreach (var id in new[] { "Downloads_download.artifact_dir_Reset", "DownloadsSave", "DownloadsDiscard" })
            {
                var button = Button(page, id);
                foreach (var state in new[] { "Normal", "PointerOver", "Pressed", "Disabled" })
                {
                    var wasEnabled = button.IsEnabled;
                    button.IsEnabled = state != "Disabled";
                    VisualStateManager.GoToState(button, state, false); await Task.Delay(50);
                    var border = Descendants(button).OfType<Border>().Single(b => b.Name == "SettingsButtonRoot");
                    Check(border.CornerRadius.TopLeft is >= 10 and <= 25 && border.ActualWidth > border.ActualHeight, $"{id} keeps a bounded non-ellipse backdrop in {state}");
                    await CaptureAsync(outer, $"downloads-{id.Replace("download.artifact_dir_", "")}-{state}.png");
                    button.IsEnabled = wasEnabled;
                }
                VisualStateManager.GoToState(button, "Normal", false);
                if (button.IsEnabled)
                {
                    button.Focus(FocusState.Keyboard); await Task.Delay(50);
                    Check(button.FocusState == FocusState.Keyboard && button.UseSystemFocusVisuals, $"{id} exposes keyboard focus");
                    await CaptureAsync(outer, $"downloads-{id.Replace("download.artifact_dir_", "")}-keyboard-focus.png");
                }
            }
            disclosure.Focus(FocusState.Keyboard); await Task.Delay(50);
            Check(disclosure.FocusState == FocusState.Keyboard && disclosure.UseSystemFocusVisuals, "the Advanced disclosure exposes keyboard focus");
            await CaptureAsync(outer, "downloads-advanced-keyboard-focus.png");
            budgetUnlimited.Focus(FocusState.Keyboard); await Task.Delay(50);
            Check(budgetUnlimited.FocusState == FocusState.Keyboard && budgetUnlimited.UseSystemFocusVisuals, "an Unlimited switch exposes keyboard focus");
            await CaptureAsync(outer, "downloads-unlimited-keyboard-focus.png");
            await Click(page, "DownloadsDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "state captures discarded");

            // Environment-managed key, unreadable status and restart status.
            // A draft for a key the environment takes over is dropped, the note names the variable, and a later save omits the key.
            directory.Text = "/mnt/draft"; cacheBox.Text = "96";
            await Until(() => controller.State.DirtyKeys.Contains(R.ArtifactDir) && controller.State.DirtyKeys.Contains(R.CacheHours), "drafts before the environment takes over");
            server.Managed = [R.ArtifactDir]; await controller.ReloadAsync(true); await Task.Delay(150);
            Check(!directory.IsEnabled && !Button(page, "Downloads_download.artifact_dir_Reset").IsEnabled, "an environment-managed directory cannot be edited or reset");
            Check(EnvironmentNoteProbe.Verify(page, directory, EnvironmentNoteProbe.Named("SILO_DOWNLOAD_ARTIFACT_DIR")) is null, "the managed row is disabled, shows the official note naming the variable and carries it in its help text");
            Check(controller.State.DirtyKeys.SequenceEqual([R.CacheHours]) && directory.Text != "/mnt/draft", "the draft of the key that became managed is dropped and the other draft stays");
            Check(EnvironmentNoteProbe.VerifyEditable(cacheBox) is null, "an editable row carries no environment note");
            var savesBeforeManaged = server.Saves;
            await Click(page, "DownloadsSave"); await Until(() => server.Saves == savesBeforeManaged + 1 && !controller.State.Saving, "save with a managed key present");
            Check(server.LastPut is { } managedBody && !managedBody.Contains(R.ArtifactDir, StringComparison.Ordinal) && managedBody.Contains(R.CacheHours, StringComparison.Ordinal), "the save body omits the environment-managed key");
            server.NamedVariables = false; await controller.ReloadAsync(true);
            await Until(() => EnvironmentNoteProbe.Verify(page, directory, EnvironmentNoteProbe.Unnamed) is null, "the unnamed note is rendered for the older server");
            Check(EnvironmentNoteProbe.Verify(page, directory, EnvironmentNoteProbe.Unnamed) is null, "an older server that names no variable still locks the row and shows the official field message");
            server.NamedVariables = true; await controller.ReloadAsync(true);
            await Until(() => EnvironmentNoteProbe.Verify(page, directory, EnvironmentNoteProbe.Named("SILO_DOWNLOAD_ARTIFACT_DIR")) is null, "the named note is rendered again");
            await CaptureAsync(outer, "downloads-environment-managed.png");
            // A status re-read that fails after one that answered keeps the last answered list (the official client keeps its last
            // data on a refetch error): the page stays loaded and the managed row stays locked, so a save cannot include its key.
            server.Managed = []; server.SensitiveFailure = true; await controller.ReloadAsync(true);
            await Until(() => controller.State.Loaded && !controller.State.Loading, "reload with an unreadable environment status settled");
            await Task.Delay(150);
            Check(controller.State.Loaded && !directory.IsEnabled && EnvironmentNoteProbe.Verify(page, directory, EnvironmentNoteProbe.Named("SILO_DOWNLOAD_ARTIFACT_DIR")) is null,
                "a failed environment status re-read keeps the page loaded and the last answered managed row locked with its note");
            Check(cacheBox.IsEnabled, "a failed environment status re-read leaves the other rows editable");
            server.SensitiveFailure = false; await controller.ReloadAsync(true);
            await Until(() => directory.IsEnabled, "the row is editable again once the status answers without the key");
            Check(EnvironmentNoteProbe.VerifyEditable(directory) is null, "a row that is no longer managed carries no environment note");

            // Conflict, unknown outcome and permission recovery.
            cacheBox.Text = "60"; await Until(() => controller.State.DirtyKeys.Contains(R.CacheHours), "conflict draft");
            server.Conflict = true; await Click(page, "DownloadsSave");
            await Until(() => controller.State.Conflict, "conflict shown"); await Task.Delay(100);
            Check(!Button(page, "DownloadsSave").IsEnabled && cacheBox.Text == "60", "stale version disables Save and retains the draft");
            await CaptureAsync(outer, "downloads-conflict.png");
            server.Conflict = false; await Click(page, "DownloadsReload");
            await Until(() => !controller.State.Conflict && !controller.State.Loading, "explicit reload");
            Check(controller.State.DirtyKeys.Contains(R.CacheHours) && Button(page, "DownloadsSave").IsEnabled, "explicit reload preserves the draft and re-enables Save");
            server.UnknownOutcome = true; var beforeUnknown = server.Saves;
            await Click(page, "DownloadsSave"); await Until(() => controller.State.OutcomeUnknown, "unknown outcome shown"); await Task.Delay(100);
            Check(server.Saves == beforeUnknown + 1 && !Button(page, "DownloadsSave").IsEnabled && cacheBox.Text == "60" &&
                Descendants(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("The save outcome is unknown", StringComparison.Ordinal)), "an unknown save outcome keeps the draft, blocks Save and is not replayed");
            await CaptureAsync(outer, "downloads-unknown-outcome.png");
            server.UnknownOutcome = false; await Click(page, "DownloadsReload");
            await Until(() => !controller.State.OutcomeUnknown && !controller.State.Loading, "reload after unknown");
            server.ReadForbidden = true; await controller.ReloadAsync(true); await Task.Delay(150);
            Check(!cacheBox.IsEnabled && !Button(page, "DownloadsSave").IsEnabled && Descendants(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("Permission to manage", StringComparison.Ordinal)),
                "permission denial disables editing and saving with an actionable message");
            server.ReadForbidden = false; await controller.ReloadAsync(true); await Task.Delay(150);
            Check(cacheBox.IsEnabled && cacheBox.Text == "60", "restored access keeps the retained draft");
            controller.Discard(); await Until(() => controller.State.DirtyKeys.Count == 0, "discard before departure");

            // Departure, then Back through a failed first read: the official sentence with Retry and no editable defaults.
            cacheBox.Text = "61"; await Until(() => controller.State.DirtyKeys.Count > 0, "departure draft");
            shell.Navigate(new(AdminRoute.Activity));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "discard navigation dialog");
            Invoke(DialogButton((ContentDialog)Field(page, "_dialog")!, "Discard"));
            await Until(() => inner.Content is AdminActivityPage, "discard permitted navigation");
            await Task.Delay(150);
            Check(controller.State.Values.Count == 0 && controller.State.DirtyKeys.Count == 0 && cacheBox.Text.Length == 0, "departure retires the controller and clears values, drafts and editor text");
            // Re-entry itself must refresh the warm shared snapshot; the fixture must not invalidate it for the page.
            server.EffectiveFailure = true;
            inner.GoBack(); await Until(() => inner.Content is AdminDownloadSettingsPage { Controller.State.Error: not null }, "Back with a failed first read");
            page = (AdminDownloadSettingsPage)inner.Content; controller = page.Controller!;
            await Until(() => !controller.State.Loading, "failed read settled"); await Task.Delay(150);
            Check(!controller.State.Loaded && Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Download settings could not be loaded. Retry before editing limits.") &&
                Button(page, "DownloadsRetry").Visibility == Visibility.Visible && Button(page, "DownloadsRetry").IsEnabled &&
                Descendants(page).OfType<Control>().Where(c => AutomationProperties.GetAutomationId(c).StartsWith("Downloads_", StringComparison.Ordinal)).All(c => !IsLaidOut(c)) &&
                !Descendants(page).OfType<HyperlinkButton>().Any(IsLaidOut),
                "a failed first read shows the official sentence and Retry with no editable defaults");
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Downloads" && IsLaidOut(t)), "the failed page keeps its title");
            await CaptureAsync(outer, "downloads-load-error.png");
            controller.Set(R.CacheHours, "1"); await controller.SaveAsync();
            Check(controller.State.DirtyKeys.Count == 0, "a failed read stages and saves nothing");
            server.EffectiveFailure = false; await Click(page, "DownloadsRetry");
            await Until(() => controller.State.Loaded && controller.State.Error == null && !controller.State.Loading, "retry recovers");
            await Until(() => Box(page, R.CacheHours)?.Text.Length > 0, "Back fields rendered");
            cacheBox = Box(page, R.CacheHours)!;
            Check(cacheBox.IsEnabled && controller.State.DirtyKeys.Count == 0 && Rail(page).Visibility == Visibility.Visible,
                "Retry restores the editable page from fresh server values with the remembered Advanced choice");
            // Only the admin settings surface is used; the user's own offline settings are never touched.
            string[] requests; lock (server.Requests) requests = server.Requests.ToArray();
            Check(requests.All(r => r.Contains("/api/v2/admin/", StringComparison.Ordinal)) && requests.Where(r => r.StartsWith("PUT ", StringComparison.Ordinal)).All(r => r == "PUT /api/v2/admin/settings"),
                "only the guarded admin settings endpoints are used for writes");
            Check(Snapshot(Program.ResultDirectory, "settings") == localBefore, "the signed-in user's local offline-download settings are unchanged");

            // Authority retirement clears everything.
            cacheBox.Text = "70"; await Until(() => controller.State.DirtyKeys.Count > 0, "retirement draft");
            auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user", Username = "synthetic-admin" }); await Task.Delay(250);
            Check(controller.State.Values.Count == 0 && cacheBox.Text.Length == 0 && Descendants(page).OfType<TextBox>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Downloads_", StringComparison.Ordinal)).All(b => b.Text.Length == 0) && !Button(page, "DownloadsSave").IsEnabled,
                "authority retirement clears values, drafts and the save command");
        }
        finally
        {
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            window.Content = previousContent;
            await Task.Delay(100);
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Downloads settings fixture failures: " + string.Join("; ", Failures));
    }

    private static string Snapshot(string directory, string stem)
    {
        try
        {
            return string.Join("|", Directory.EnumerateFiles(directory, stem + "*.json").OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => Path.GetFileName(p) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))));
        }
        catch (IOException) { return "unreadable"; }
    }
    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current)) if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static Border Rail(DependencyObject page) => (Border)Field(page, "_rail")!;
    private static TextBox? Box(DependencyObject page, string key) =>
        Descendants(page).OfType<TextBox>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == "Downloads_" + key && (IsLaidOut(c)));
    private static bool IsLaidOut(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is UIElement ui; current = VisualTreeHelper.GetParent(current))
            if (ui.Visibility != Visibility.Visible) return false;
        return true;
    }
    private static ToggleSwitch Toggle(DependencyObject page, string key) => Descendants(page).OfType<ToggleSwitch>().Single(c => AutomationProperties.GetAutomationId(c) == "Downloads_" + key);
    private static CheckBox? Unlimited(DependencyObject page, string key) => Descendants(page).OfType<CheckBox>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == "Downloads_" + key + "_Unlimited");
    private static TextBlock? UnitText(DependencyObject page, FrameworkElement control)
    {
        DependencyObject? row = VisualTreeHelper.GetParent(control);
        while (row != null && row.GetType().Name != "AdminSettingsRow") row = VisualTreeHelper.GetParent(row);
        return row == null ? null : Descendants(row).OfType<TextBlock>().FirstOrDefault(t => t.Text is "GB" or "Mbps");
    }
    private static string? UnitLabel(DependencyObject page, FrameworkElement control) => UnitText(page, control)?.Text;
    /// <summary>Accessible names of the visible row editors, in on-screen order.</summary>
    private static string[] EditorIds(DependencyObject page) => Descendants(page).OfType<Control>()
        .Where(c => AutomationProperties.GetAutomationId(c).StartsWith("Downloads_", StringComparison.Ordinal) && !AutomationProperties.GetAutomationId(c).EndsWith("_Unlimited", StringComparison.Ordinal)
            && !AutomationProperties.GetAutomationId(c).EndsWith("_Reset", StringComparison.Ordinal) && IsLaidOut(c))
        .Select(c => AutomationProperties.GetName(c)).ToArray();
    private static Button Button(DependencyObject page, string id) => Descendants(page).OfType<Button>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static Button DialogButton(ContentDialog dialog, string label) => Descendants(dialog).OfType<Button>().First(c => c.Content?.ToString() == label || AutomationProperties.GetName(c) == label);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    /// <summary>Toggles a check box through its automation peer, as a click or Space key does.</summary>
    private static void Invoke(CheckBox box) => ((IToggleProvider)new CheckBoxAutomationPeer(box).GetPattern(PatternInterface.Toggle)).Toggle();
    private static async Task Click(DependencyObject page, string id)
    {
        var button = Button(page, id);
        await Until(() => button.IsEnabled && button.IsLoaded, id + " enabled");
        Invoke(button);
    }
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
