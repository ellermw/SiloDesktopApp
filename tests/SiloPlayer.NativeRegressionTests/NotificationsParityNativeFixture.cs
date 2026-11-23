using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

/// <summary>
/// Official silo-server 3b2b32168 (#4409, #3550): the inbox with a next-episode, a watchlist-available and the
/// stopped webhook and destination notices; the notified-series list without reason chips or tabs but Muted;
/// Settings &gt; Notifications with Other destinations. Synthetic data, captured at 1440 and 800 wide.
/// </summary>
internal static class NotificationsParityNativeFixture
{
    private const string Host = "https://notifications-parity.invalid";

    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl(Host); client.SetProfile("viewer");
        var api = new NotificationsApi(client); var vm = new NotificationsViewModel(api);
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("viewer");
        using var events = new EventChannelClient(client, auth);
        var toast = new ToastService(); var navigation = new NavigationService();
        using var owned = new ServiceCollection().AddSingleton(client).AddSingleton(api).AddSingleton(vm).AddSingleton(http)
            .AddTransient(_ => new NotificationSettingsViewModel(api)).AddSingleton(auth).AddSingleton(events)
            .AddSingleton(toast).AddSingleton(navigation).BuildServiceProvider();
        field.SetValue(null, new Services(owned, previous));
        var toasts = new ToastContainer(); parent.Children.Add(toasts); toast.Register(toasts, parent.DispatcherQueue);
        var frame = new Frame { Width = 1440, Height = 900 }; parent.Children.Add(frame); navigation.Frame = frame;
        try
        {
            // --- inbox -------------------------------------------------------------------------------------
            foreach (var width in new[] { 1440, 800 })
            {
                await Resize(frame, window, width); frame.Navigate(typeof(NotificationsPage));
                var page = (NotificationsPage)frame.Content;
                await Until(() => page.IsLoaded && vm.Notifications.Count == 4 && !vm.IsLoading);
                page.UpdateLayout(); await Task.Delay(300);
                var rows = NotificationFixtureUi.Rows(page);
                Require(rows.Length == 4, "inbox shows the four notices: " + rows.Length);
                var titles = rows.Select(row => NotificationFixtureUi.Named<TextBlock>(row, "NotificationTitleText").Text).ToArray();
                var lines = rows.Select(row => NotificationFixtureUi.Named<TextBlock>(row, "NotificationSubtitleText").Text).ToArray();
                Require(titles.SequenceEqual(new[] { "Severance", "Andor", "A webhook stopped working", "A notification destination stopped working" }), "inbox titles: " + string.Join(" | ", titles));
                Require(lines[0] == "S2E3 — Hello, Ms. Cobel" && lines[1] == "A series on your watchlist is now available" &&
                    lines[2] == "Open notification settings to fix it." && lines[3] == "Open notification settings to fix it.", "inbox lines: " + string.Join(" | ", lines));
                Require(page.FindName("FavoritesToggle") == null && page.FindName("WatchlistToggle") == null && page.FindName("ContinueWatchingToggle") == null && page.FindName("NextUpToggle") != null,
                    "preferences keep only the master switch and Next Up");
                await MediaParityNativeFixture.CaptureAsync(frame, $"notifications-inbox-{width}.png");
                Program.Log($"PASS: inbox at {width}px shows next episode, watchlist arrival, webhook stopped and destination stopped.");
                frame.Navigate(typeof(Page)); await Task.Delay(100);
            }
            wire.EmptyInbox = true;
            foreach (var width in new[] { 1440, 800 })
            {
                await Resize(frame, window, width); frame.Navigate(typeof(NotificationsPage));
                var page = (NotificationsPage)frame.Content;
                // A load inside the page's freshness window is skipped; read the now-empty inbox.
                await Until(() => page.IsLoaded && !vm.IsLoading); await vm.ReloadAsync();
                await Until(() => page.IsLoaded && vm.Notifications.Count == 0 && !vm.IsLoading && ((FrameworkElement)page.FindName("EmptyState")).Visibility == Visibility.Visible);
                page.UpdateLayout(); await Task.Delay(300);
                var hint = Descendants(page).OfType<TextBlock>().Any(text => text.Text.StartsWith("You’ll be notified here when a new episode arrives for a series you’re caught up on", StringComparison.Ordinal));
                Require(hint, "empty inbox words the next-up hint");
                await MediaParityNativeFixture.CaptureAsync(frame, $"notifications-inbox-empty-{width}.png");
                frame.Navigate(typeof(Page)); await Task.Delay(100);
            }
            wire.EmptyInbox = false;

            // --- notified series -------------------------------------------------------------------------------
            foreach (var width in new[] { 1440, 800 })
            {
                await Resize(frame, window, width); frame.Navigate(typeof(NotificationsPage), "series");
                var page = (NotificationsPage)frame.Content;
                await Until(() => page.IsLoaded && ((Button)page.FindName("SeriesButton")).Visibility == Visibility.Visible);
                var series = (NotificationSeriesControl)page.FindName("SeriesView");
                if (series.Visibility != Visibility.Visible) Invoke((Button)page.FindName("SeriesButton"));
                await Until(() => series.State.Rows.Count == 4 && !series.State.IsLoading);
                page.UpdateLayout(); await Task.Delay(300);
                var tabs = ((StackPanel)series.FindName("FilterTabs")).Children.OfType<Button>().Select(button => (string)button.Tag).ToArray();
                Require(tabs.SequenceEqual(new[] { "all", "muted" }), "filter tabs are All and Muted: " + string.Join(",", tabs));
                Require(series.FindName("ReasonsHeader") == null, "no Why column");
                var texts = series.State.Rows.Select(row => row.Explanation(series.State.Preferences)).ToArray();
                Require(texts.SequenceEqual(new[] { "The next episode after S02E08", "After you catch up", "The next new episode", "Muted" }), "series lines: " + string.Join(" | ", texts));
                await MediaParityNativeFixture.CaptureAsync(frame, $"notifications-series-{width}.png");
                Invoke((Button)series.FindName("MutedFilter"));
                await Until(() => series.State.Options.Status == "muted" && !series.State.IsLoading && series.State.Rows.Count == 1);
                page.UpdateLayout(); await Task.Delay(250);
                await MediaParityNativeFixture.CaptureAsync(frame, $"notifications-series-muted-{width}.png");
                ((TextBox)series.FindName("SearchBox")).Text = "none";
                await Until(() => series.State.Options.Q == "none" && series.State.IsEmpty);
                page.UpdateLayout(); await Task.Delay(250);
                Require(((TextBlock)series.FindName("SeriesEmptyTitle")).Text == "No series match. Try another search or filter.", "empty filter result is worded as the WebUI words it");
                await MediaParityNativeFixture.CaptureAsync(frame, $"notifications-series-no-match-{width}.png");
                Program.Log($"PASS: series list at {width}px: next-episode lines, All/Muted tabs only, no reason column, filter-empty state.");
                // The control keeps its options for the same identity: put them back before the next width.
                ((TextBox)series.FindName("SearchBox")).Text = "";
                Invoke(((StackPanel)series.FindName("FilterTabs")).Children.OfType<Button>().First(button => (string)button.Tag == "all"));
                await Until(() => series.State.Options.Q == "" && series.State.Options.Status == "all" && series.State.Rows.Count == 4 && !series.State.IsLoading);
                series.Close(); frame.Navigate(typeof(Page)); await Task.Delay(100);
            }
            frame.Navigate(typeof(Page)); frame.Visibility = Visibility.Collapsed; await Task.Delay(100);

            // --- Settings > Notifications ------------------------------------------------------------------------------
            foreach (var (width, mode, name) in new[] { (1440, "three", "settings-destinations-1440"), (800, "three", "settings-destinations-800"), (1440, "empty", "settings-destinations-empty-1440"), (800, "empty", "settings-destinations-empty-800"), (1440, "limit", "settings-destinations-limit-1440") })
            {
                wire.DestinationMode = mode; await ResizeWindow(window, width);
                var settings = new NotificationSettingsControl { Width = width - 48 };
                var shell = new Border { Width = width, Padding = new(24, 24, 24, 24), Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], Child = settings };
                parent.Children.Add(shell);
                await Until(() => settings.ViewModel.PluginDestinations is { IsLoading: false, Channels.Count: > 0 } && ((FrameworkElement)settings.FindName("PluginDestinationsGroup")).Visibility == Visibility.Visible);
                shell.UpdateLayout(); await Task.Delay(400);
                var cards = ((StackPanel)settings.FindName("DestinationsHost")).Children.Count;
                Require(cards == (mode == "empty" ? 0 : 3), $"{name}: {cards} destination cards");
                Require(((FrameworkElement)settings.FindName("DestinationsEmpty")).Visibility == (mode == "empty" ? Visibility.Visible : Visibility.Collapsed), name + ": empty text");
                Require(((FrameworkElement)settings.FindName("DestinationsLimitText")).Visibility == (mode == "limit" ? Visibility.Visible : Visibility.Collapsed) && ((Button)settings.FindName("AddDestinationButton")).IsEnabled == (mode != "limit"), name + ": limit");
                if (mode == "three")
                {
                    var all = string.Join("\n", Descendants(settings).OfType<TextBlock>().Select(text => text.Text));
                    foreach (var expected in new[] { "Phone alerts", "Living room", "Old relay", "Unavailable", "Last success: ", "m ago: HTTP 500", "The plugin that provides this destination is turned off or removed, so nothing is sent." })
                        Require(all.Contains(expected, StringComparison.Ordinal), name + ": shows " + expected);
                    Require(!all.Contains("Favorites", StringComparison.Ordinal) && !all.Contains("Continue Watching", StringComparison.Ordinal), name + ": no retired reason switches");
                    var buttons = Descendants(settings).OfType<Button>().ToArray();
                    Require(buttons.Count(button => button.Content as string == "Edit" && !button.IsEnabled) == 1 && buttons.Count(button => button.Content as string == "Edit" && button.IsEnabled) >= 2, "an unavailable destination cannot be edited");
                }
                await MediaParityNativeFixture.CaptureAsync(shell, name + ".png");
                if (mode == "three" && width == 1440)
                {
                    // The add dialog: kind, name, the plugin's schema form (text with default, secret, number, switch, select), what to send for.
                    var open = (Task)typeof(NotificationSettingsControl).GetMethod("OpenDestinationDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, [null])!;
                    ContentDialog? dialog = null;
                    await Until(() => (dialog = FindDialog(window)) != null);
                    dialog!.UpdateLayout(); await Task.Delay(500);
                    Require(Descendants(dialog).OfType<PasswordBox>().Count() == 1 && Descendants(dialog).OfType<ComboBox>().Count() == 2 && Descendants(dialog).OfType<ToggleSwitch>().Count() >= 3, "add dialog draws the plugin's form");
                    Require(!dialog.IsPrimaryButtonEnabled, "Add is off while a required field is empty");
                    await MediaParityNativeFixture.CaptureAsync(dialog, "settings-destination-add-dialog.png");
                    // The form is taller than the dialog: show its lower half (switch, select, what to send for) as well.
                    var scroller = Descendants(dialog).OfType<ScrollViewer>().First(item => item.MaxHeight == 520);
                    scroller.ChangeView(null, scroller.ScrollableHeight, null, true); await Task.Delay(400);
                    await MediaParityNativeFixture.CaptureAsync(dialog, "settings-destination-add-dialog-bottom.png");
                    scroller.ChangeView(null, 0, null, true); await Task.Delay(200);
                    var topic = Descendants(dialog).OfType<TextBox>().Single(box => AutomationProperties.GetAutomationId(box) == "DestinationField_topic");
                    topic.Text = "family-alerts"; await Until(() => dialog.IsPrimaryButtonEnabled);
                    var token = Descendants(dialog).OfType<PasswordBox>().Single(); token.Password = "fixture-secret"; await Task.Delay(100);
                    Invoke(Descendants(dialog).OfType<Button>().First(button => button.Name == "PrimaryButton"));
                    await open; await Task.Delay(200);
                    Require(wire.Posts.Count == 1, "Add sends the create exactly once: " + wire.Posts.Count);
                    using (var body = JsonDocument.Parse(wire.Posts[0]))
                    {
                        var root = body.RootElement; var values = root.GetProperty("settings");
                        Require(root.GetProperty("plugin_id").GetString() == "ntfy" && root.GetProperty("capability_id").GetString() == "push" && root.GetProperty("name").GetString() == "ntfy" &&
                            !root.TryGetProperty("enabled", out _) && values.GetProperty("topic").GetString() == "family-alerts" && values.GetProperty("server").GetString() == "https://ntfy.sh" &&
                            values.TryGetProperty("token", out _), "the create body carries the kind, name, typed settings and the secret");
                    }
                    Program.Log("PASS: add dialog draws the plugin form, enables Add when valid, and sends one create.");

                    // Delete confirmation.
                    var delete = Descendants(settings).OfType<Button>().First(button => button.Content as string == "Delete");
                    Invoke(delete); dialog = null; await Until(() => (dialog = FindDialog(window)) != null);
                    dialog!.UpdateLayout(); await Task.Delay(400);
                    Require(wire.Deletes.Count == 0, "nothing is deleted before the confirmation");
                    await MediaParityNativeFixture.CaptureAsync(dialog, "settings-destination-delete-confirmation.png");
                    Invoke(Descendants(dialog).OfType<Button>().First(button => button.Name == "PrimaryButton"));
                    await Until(() => wire.Deletes.Count == 1 && FindDialog(window) == null);
                    Require(wire.Deletes[0].IfMatch == "\"etag-d1\"", "delete sends the validator of the row chosen");
                    Program.Log("PASS: delete asks first, then sends one DELETE with the chosen row's validator.");
                }
                parent.Children.Remove(shell); await Task.Delay(100);
            }
        }
        finally
        {
            vm.CancelPendingLoad();
            await NotificationFixtureUi.DetachAsync(frame, () => { frame.Navigate(typeof(Page)); parent.Children.Remove(frame); });
            parent.Children.Remove(toasts); toast.Unregister(); navigation.Frame = null; field.SetValue(null, previous);
        }
    }

    private static async Task Resize(Frame frame, Window window, int width)
    {
        frame.Width = width; frame.Height = 900;
        await ResizeWindow(window, width);
    }
    private static async Task ResizeWindow(Window window, int width)
    {
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900)); window.AppWindow.Show(false); await Task.Delay(250);
        Program.Log($"TRACE window client {window.Content.XamlRoot.Size.Width}x{window.Content.XamlRoot.Size.Height} for requested {width}");
    }

    private static ContentDialog? FindDialog(Window window) => window.Content?.XamlRoot is { } root
        ? VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Where(popup => popup.Child != null).SelectMany(popup => Descendants(popup.Child!).Prepend(popup.Child!)).OfType<ContentDialog>().FirstOrDefault() : null;

    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var attempt = 0; attempt < 300; attempt++) { if (ready()) return; await Task.Delay(20); } throw new InvalidOperationException("Notifications parity fixture did not settle."); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var d in Descendants(child)) yield return d; }
    }
    private sealed class Services(IServiceProvider owned, IServiceProvider previous) : IServiceProvider
    { public object? GetService(Type type) => owned.GetService(type) ?? previous.GetService(type); }

    private sealed class Wire : HttpMessageHandler
    {
        internal bool EmptyInbox;
        internal string DestinationMode = "three";
        internal readonly List<string> Posts = [];
        internal readonly List<(string Path, string IfMatch)> Deletes = [];

        private static string Iso(TimeSpan ago) => (DateTimeOffset.UtcNow - ago).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(value as string ?? JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };

        private static object Destination(string id, string name, bool available, string? success, string? failure, string? failureMessage, bool next = true, bool requests = true) => new
        {
            id, etag = "\"etag-" + id + "\"", name, plugin_id = "ntfy", capability_id = "push", channel_name = available ? "ntfy" : "", available, enabled = true,
            notify_next_up = next, notify_requests = requests, consecutive_failures = failure == null ? 0 : 2, created_at = Iso(TimeSpan.FromDays(9)), disabled_reason = (string?)null,
            last_success_at = success, last_failure_at = failure, last_failure_message = failureMessage,
            secret_settings_set = available ? new[] { "token" } : Array.Empty<string>(), settings = available ? new Dictionary<string, object> { ["server"] = "https://ntfy.sh", ["topic"] = "silo" } : new Dictionary<string, object>(),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath; var query = r.RequestUri.Query;
            if (path == "/poster.png") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(Program.AppDirectory, "Assets", "silo-icon-1024.png"), ct)) };
            if (path.EndsWith("/plugin-channels"))
                return Json(new { items = new object[]
                {
                    new { plugin_id = "ntfy", capability_id = "push", display_name = "ntfy", description = "Send to a topic on ntfy.sh or your own ntfy server.", settings_form = new { fields = new object[]
                    {
                        new { key = "server", label = "Server", control = "text", type = "string", required = true, secret = false, placeholder = "https://ntfy.sh", default_value = "https://ntfy.sh", description = "The ntfy server to publish to." },
                        new { key = "topic", label = "Topic", control = "text", type = "string", required = true, secret = false, min_length = 3, max_length = 64 },
                        new { key = "token", label = "Access token", control = "password", type = "string", required = false, secret = true, description = "Only needed for a protected topic." },
                        new { key = "port", label = "Port", control = "number", type = "integer", required = false, secret = false, minimum = 1, maximum = 65535 },
                        new { key = "markdown", label = "Render Markdown", control = "switch", type = "boolean", required = false, secret = false, description = "Format messages as Markdown." },
                        new { key = "priority", label = "Priority", control = "select", type = "integer", required = false, secret = false, options = new[] { new { label = "Low", value = 2 }, new { label = "Default", value = 3 }, new { label = "High", value = 4 } } },
                    } } },
                    new { plugin_id = "pushover", capability_id = "app", display_name = "Pushover", description = "", settings_form = new { fields = Array.Empty<object>() } },
                } });
            if (path.EndsWith("/plugin-destinations"))
            {
                if (r.Method == HttpMethod.Post)
                {
                    Posts.Add(await r.Content!.ReadAsStringAsync(ct));
                    return Json(Destination("new", "ntfy", true, null, null, null), HttpStatusCode.Created);
                }
                var items = DestinationMode == "empty" ? Array.Empty<object>() : new object[]
                {
                    Destination("d1", "Phone alerts", true, Iso(TimeSpan.FromMinutes(3)), null, null),
                    Destination("d2", "Living room", true, Iso(TimeSpan.FromHours(2)), Iso(TimeSpan.FromMinutes(5)), "HTTP 500", requests: false),
                    Destination("d3", "Old relay", false, null, null, null),
                };
                return Json(new { items, page = new { has_more = false } });
            }
            if (path.Contains("/plugin-destinations/") && r.Method == HttpMethod.Delete)
            {
                Deletes.Add((path, r.Headers.IfMatch.SingleOrDefault()?.ToString() ?? "")); return new(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/series"))
            {
                var none = query.Contains("q=none"); var muted = query.Contains("status=muted");
                object Row(string id, string title, bool notifies, bool isMuted, bool after) => new
                {
                    item = new { content_id = id, type = "series", title, poster_url = Host + "/poster.png" }, reasons = new { next_up = true }, active_reasons = new { next_up = true },
                    notifies, muted = isMuted, next_up_after = after ? new { season_number = 2, episode_number = 8 } : null,
                };
                var rows = none ? Array.Empty<object>() : muted ? new object[] { Row("bear", "The Bear", false, true, true) }
                    : new object[] { Row("andor", "Andor", true, false, true), Row("severance", "Severance", false, false, true), Row("horses", "Slow Horses", true, false, false), Row("bear", "The Bear", false, true, true) };
                return Json(new { items = rows, page = new { has_more = false } });
            }
            if (path.EndsWith("/capabilities"))
                return Json("""{"series":{"available":true,"mute":true,"bulk_mute":true},"plugin_destinations":{"available":true,"max_per_profile":MAX},"webhooks":{"available":false}}"""
                    .Replace("MAX", DestinationMode == "limit" ? "3" : "10"));
            if (path.EndsWith("/preferences")) return Json("""{"profile_id":"viewer","enabled":true,"notify_next_up":true}""");
            if (path.EndsWith("/unread-count")) return Json(new { count = EmptyInbox ? 0 : 3 });
            if (path.EndsWith("/notifications"))
            {
                object Note(string id, string type, TimeSpan ago, object flags, string? seriesId = null, string? seriesTitle = null, string? episodeId = null, string? episodeTitle = null, int? season = null, int? episode = null, bool read = false) => new
                {
                    id, type, profile_id = "viewer", series_id = seriesId, series_title = seriesTitle, episode_id = episodeId, episode_title = episodeTitle, season_number = season, episode_number = episode,
                    poster_url = seriesId == null ? null : Host + "/poster.png", reason_flags = flags, created_at = Iso(ago), read_at = read ? Iso(ago) : null,
                };
                var items = EmptyInbox ? Array.Empty<object>() : new[]
                {
                    Note("n1", "episode.available", TimeSpan.FromMinutes(10), new { next_up = true }, "s1", "Severance", "e1", "Hello, Ms. Cobel", 2, 3),
                    Note("n2", "watchlist.available", TimeSpan.FromHours(3), new { media_type = "series", tmdb_id = 83867 }, "s2", "Andor"),
                    Note("n3", "webhook.auto_disabled", TimeSpan.FromHours(30), new { }),
                    Note("n4", "plugin_destination.auto_disabled", TimeSpan.FromHours(31), new { }, read: true),
                };
                return Json(new { items, page = new { has_more = false }, read_cutoff = "fixture-cutoff" });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
