using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Views.Admin;
using System.Reflection;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal static partial class AdminMonitorShellNativeFixture
{
    private static AdminActivityPage LiveTab(Frame inner) =>
        (AdminActivityPage)typeof(AdminInsightsPage).GetProperty("Activity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inner.Content)!;

    private static void UseActivityTable(AdminActivityPage page) => ChooseActivityLayout(page, AdminActivityLayout.Table);

    private static void ChooseActivityLayout(AdminActivityPage page, AdminActivityLayout layout) =>
        typeof(AdminActivityPage).GetMethod("ChooseLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [layout]);

    private static async Task InsightsAsync(Window window, Server server, AuthService auth, ServerActivityStore store, NavigationService navigation, SettingsService settings)
    {
        server.SessionCount = 3;
        server.InsightsPoster = await FixturePosterAsync();
        server.RestartRequired = false;
        server.SessionsFail = true;
        server.InsightsPickerPages = true;
        server.InsightsAccountsFail = true;
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        var frame = new Frame { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
        window.Content = frame;
        navigation.Frame = frame;
        frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Insights));
        var shell = (AdminShellPage)frame.Content;
        var inner = (Frame)Field(shell, "_frame")!;
        var insights = (AdminInsightsPage)inner.Content;
        var activity = LiveTab(inner);
        var table = (StackPanel)Field(activity, "_tableHost")!;
        T? Find<T>(DependencyObject root, string id) where T : DependencyObject =>
            Descendants(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
        Button? FindNamed(DependencyObject root, string name) =>
            Descendants(root).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetName(button) == name);
        Button Named(DependencyObject root, string name) => FindNamed(root, name) ??
            throw new InvalidOperationException($"Button '{name}' was missing from {root.GetType().Name}.");
        Button Tab(string tab) => Find<Button>(insights, "AdminInsightsTab_" + tab)!;
        bool HasFilter(string filter) => Find<FrameworkElement>(insights, "AdminInsightsFilter_" + filter) != null;
        MenuFlyout Picker(string key) => (MenuFlyout)Find<Button>(insights, "AdminInsightsFilter_" + key)!.Flyout;
        MenuFlyoutItem? Option(string key, string text) => Picker(key).Items.OfType<MenuFlyoutItem>().FirstOrDefault(item => item.Text == text);
        async Task SelectOption(string key, string text)
        {
            Picker(key).ShowAt(Find<Button>(insights, "AdminInsightsFilter_" + key)!);
            await Task.Delay(150);
            var item = Option(key, text) ?? throw new InvalidOperationException("Missing picker option " + text);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.MenuFlyoutItemAutomationPeer(item)
                .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }
        async Task Resize(int width)
        {
            var scale = frame.XamlRoot?.RasterizationScale ?? 1;
            window.AppWindow.ResizeClient(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
            await Task.Delay(350);
        }

        await Resize(1440);
        await Until(() => Find<Border>(table, "AdminInsightsSectionError") != null);
        Check(Find<Border>(table, "AdminInsightsSectionError") != null &&
            !Descendants(table).OfType<TextBlock>().Any(label => label.Text == "No active streams"),
            "initial session failure uses the Insights error surface");
        Check(Named(activity, "IP Lookup").IsEnabled, "IP Lookup remains available after a failed sessions read");
        await CaptureAsync(frame, "insights-load-failure.png");
        server.SessionsFail = false;
        var refresh = Find<Button>(insights, "AdminInsightsRefresh")!;
        var started = System.Diagnostics.Stopwatch.StartNew();
        Invoke(Named(table, "Retry"));
        await Until(() => !refresh.IsEnabled);
        Check(!refresh.IsEnabled, "Activity Retry shares the Insights refresh spinner");
        await Until(() => activity.RenderedSessionIds.Count == 3 && refresh.IsEnabled);
        Check(started.ElapsedMilliseconds >= 550, "Insights refresh spinner stays visible for at least 600 ms with dispatcher tolerance");
        Check(activity.Layout == AdminActivityLayout.Cards, "three loaded streams default to Cards even after an initial load failure");
        Check(HasFilter("timezone") == false && Find<Border>(insights, "AdminInsightsFilters") == null, "Activity uses its own toolbar");
        Check(AdminInsightsTabs.Ordered.Select(tab => AutomationProperties.GetName(Tab(tab.Wire()))).SequenceEqual(
            ["Activity", "Playback", "Titles", "Library", "People", "Devices & Quality", "Bandwidth"]), "Insights shows seven official tabs in order");
        var paused = Find<Grid>(table, "AdminActivityCard:synthetic-2") ??
            throw new InvalidOperationException("The paused activity card was missing.");
        await Until(() => Descendants(paused).OfType<Border>().Count(border => border.Background is ImageBrush { ImageSource: not null }) >= 2);
        Check(Descendants(paused).OfType<Border>().Count(border => border.Background is ImageBrush { ImageSource: not null }) >= 2,
            "the poster and soft backdrop decode through the shared artwork cache");
        var progress = Find<Grid>(paused, "AdminActivityCard:synthetic-2:Progress")!;
        Check(progress is { Height: 2 } && ((SolidColorBrush)((Border)progress.Children[0]).Background).Color ==
            ((SolidColorBrush)Descendants(paused).OfType<TextBlock>().First(label => label.Text == "Paused").Foreground).Color,
            "paused cards use the warning color on the 2px progress line");

        foreach (var width in new[] { 1440, 560 })
        {
            await Resize(width);
            if (activity.Layout != AdminActivityLayout.Cards) Invoke(Named(activity, "Cards"));
            await CaptureAsync(frame, $"insights-cards-{width}.png");
            var card = Find<Grid>(table, "AdminActivityCard:synthetic-2")!;
            await CaptureAsync(card, $"insights-paused-card-{width}.png");
            var open = Find<Button>(card, "AdminActivityCardOpen:synthetic-2")!;
            Invoke(open);
            var sheet = Find<Grid>(shell, "AdminActivityCardDetails")!;
            await Until(() => sheet.Visibility == Visibility.Visible && FindNamed(sheet, "Session actions") != null);
            Check(sheet.Visibility == Visibility.Visible && Descendants(sheet).OfType<TextBlock>().Any(label => label.Text == "Synthetic Title 2")
                && FindNamed(sheet, "Session actions")?.IsEnabled == true, "card details show the same session facts with actions in the footer");
            await CaptureAsync(frame, $"insights-details-{width}.png");
            Invoke(Find<Button>(sheet, "LogsDetailClose")!);
            await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(frame.XamlRoot), open));
            Check(sheet.Visibility == Visibility.Collapsed && ReferenceEquals(FocusManager.GetFocusedElement(frame.XamlRoot), open), "closing details restores focus to the card opener");
            Invoke(Named(activity, "Table"));
            await CaptureAsync(frame, $"insights-table-{width}.png");
            var backCount = inner.BackStack.Count;
            Invoke(Tab("playback"));
            await Until(() => insights.Filters.Tab == AdminInsightsTab.Playback && HasFilter("timezone"));
            Check(HasFilter("allLibraries") && HasFilter("allAccounts") && HasFilter("timezone"), "Playback shows range, library, account and time zone");
            if (width == 1440)
            {
                TextBlock? Feedback(string list) => Find<TextBlock>(insights, "AdminInsightsOptionStatus_" + list);
                await Until(() => Feedback("allAccounts")?.Text.Contains("Couldn’t load accounts", StringComparison.Ordinal) == true &&
                    Option("allLibraries", "Movies") != null);
                Check(Feedback("allAccounts")?.Text.Contains("Couldn’t load accounts", StringComparison.Ordinal) == true &&
                    Feedback("allLibraries")?.Text.Contains("More results", StringComparison.Ordinal) == true,
                    "Insights distinguishes a failed account read from a partial library list");
                var libraryReads = server.InsightsLibraryReads;
                server.InsightsAccountsFail = false;
                Invoke(refresh);
                await Until(() => refresh.IsEnabled && Option("allAccounts", "first-viewer") != null);
                Check(Option("allAccounts", "first-viewer") != null && Option("allLibraries", "Movies") != null &&
                    server.InsightsLibraryReads == libraryReads && Feedback("allAccounts")?.Text.Contains("More results", StringComparison.Ordinal) == true,
                    "Refresh recovers just the failed picker and retains the successful sibling");
                await SelectOption("allAccounts", "Next page");
                await Until(() => Option("allAccounts", "second-viewer") != null);
                await SelectOption("allAccounts", "second-viewer");
                await Until(() => shell.CurrentRequest.Get("user_id") == "8");
                await SelectOption("allLibraries", "Next page");
                await Until(() => Option("allLibraries", "Shows") != null);
                await SelectOption("allLibraries", "Shows");
                await Until(() => shell.CurrentRequest.Get("library_id") == "2");
                Check(shell.CurrentRequest.Get("user_id") == "8" && shell.CurrentRequest.Get("library_id") == "2",
                    "Both pickers can select and retain entries beyond the first page");
            }
            await CaptureAsync(frame, $"insights-playback-{width}.png");
            if (width < 1024) Invoke(Named(shell, AdminText.T("web.admin.shell.mobileHeader.openNavigation")));
            var sidebar = (FrameworkElement)Field(shell, width < 1024 ? "_drawerPanelHost" : "_desktopSidebarHost")!;
            await Until(() => FindNamed(sidebar, "Insights") != null);
            Check(Descendants(sidebar).OfType<Button>().Any(button => AutomationProperties.GetName(button).StartsWith("Insights", StringComparison.Ordinal))
                && Descendants(sidebar).OfType<TextBlock>().Any(label => label.Text == AdminText.T("web.admin.shell.sidebar.liveSessions", AdminText.Count(3))), "the live stream badge sits on the Insights sidebar item");
            await CaptureAsync(sidebar, $"insights-sidebar-{width}.png");
            if (width < 1024) shell.TryGoBack();
            Invoke(Find<Button>(insights, "AdminInsightsRange_custom")!);
            await Until(() => HasFilter("fromDate"));
            Check(shell.CurrentRequest.Get("from") != null && shell.CurrentRequest.Get("to") != null && HasFilter("fromDate") &&
                HasFilter("toDate"), "Custom seeds both dates into the route and shows the date inputs");
            Invoke(Tab("people"));
            await Until(() => insights.Filters.Tab == AdminInsightsTab.People && HasFilter("allLibraries") && !HasFilter("allAccounts"));
            Check(HasFilter("allLibraries") && !HasFilter("allAccounts") && HasFilter("timezone"), "People hides only the account filter");
            Invoke(Tab("bandwidth"));
            await Until(() => insights.Filters.Tab == AdminInsightsTab.Bandwidth && !HasFilter("timezone"));
            Check(!HasFilter("allLibraries") && !HasFilter("allAccounts") && !HasFilter("timezone") &&
                shell.CurrentRequest.Get("range") == "custom", "Bandwidth keeps the date window and hides all three scoped choices");
            Invoke(Find<Button>(insights, "AdminInsightsReset")!);
            await Until(() => shell.CurrentRequest.Href == "/admin/insights?tab=bandwidth");
            Check(shell.CurrentRequest.Href == "/admin/insights?tab=bandwidth" &&
                inner.BackStack.Count == backCount && ReferenceEquals(inner.Content, insights),
                "Reset keeps the selected tab and all filter changes replaced the same back-stack location");
            Invoke(Tab("activity"));
            await Until(() => insights.Filters.Tab == AdminInsightsTab.Activity);
            activity = LiveTab(inner);
            table = (StackPanel)Field(activity, "_tableHost")!;
            await Until(() => activity.RenderedSessionIds.Count == 3);
            Check(activity.Layout == AdminActivityLayout.Table, "explicit Table layout is remembered on return to Activity");
        }

        Invoke(Named(activity, "Cards"));
        await Until(() => Find<Button>(table, "AdminActivityCardOpen:synthetic-2") != null);
        Invoke(Find<Button>(table, "AdminActivityCardOpen:synthetic-2")!);
        await Until(() => Find<Grid>(shell, "AdminActivityCardDetails")?.Visibility == Visibility.Visible);
        var liveSheet = Find<Grid>(shell, "AdminActivityCardDetails")!;
        var sessionActions = Named(liveSheet, "Session actions");
        Check(sessionActions.Focus(FocusState.Keyboard), "the card details footer accepts keyboard focus");
        server.SessionCount = 4;
        await store.RefreshSessionsAsync();
        await Until(() => activity.RenderedSessionIds.Count == 4);
        Check(activity.RenderedSessionIds.Count == 4 && ReferenceEquals(FindNamed(liveSheet, "Session actions"), sessionActions) &&
            ReferenceEquals(FocusManager.GetFocusedElement(frame.XamlRoot), sessionActions), "a live update leaves the footer action mounted and focused");
        server.SessionCount = 1;
        await store.RefreshSessionsAsync();
        await Until(() => activity.RenderedSessionIds.Count == 1);
        Check(Find<Grid>(shell, "AdminActivityCardDetails")?.Visibility == Visibility.Collapsed, "a session ending closes its card details");
        server.SessionCount = 12;
        await store.RefreshSessionsAsync();
        await Until(() => activity.RenderedSessionIds.Count == 12);
        Check(activity.Layout == AdminActivityLayout.Cards && Find<Grid>(shell, "AdminActivityCardDetails")?.Visibility == Visibility.Collapsed,
            "existing cards stay selected as sessions arrive and a returning id does not reopen details");

        var preferences = settings.Load();
        preferences.AdminActivityLayout = null;
        settings.Save(preferences);
        shell.Navigate(new AdminRouteRequest(AdminRoute.Insights));
        await Until(() => inner.Content is AdminInsightsPage page && !ReferenceEquals(page, insights));
        activity = LiveTab(inner);
        await Until(() => activity.RenderedSessionIds.Count == 12);
        Check(activity.Layout == AdminActivityLayout.Table, "a fresh Activity open with twelve streams defaults to Table");
        var finished = (AdminInsightsPage)inner.Content;
        Check(frame.Navigate(typeof(Page)), "Insights can be left through the outer frame after using its tabs and panels");
        await Until(() => !finished.IsLoaded && Field(activity, "_active") is false);
        Check(!finished.IsLoaded && Field(activity, "_active") is false && Field(activity, "_lease") == null,
            "leaving Insights retires the live tab and its lease");
        navigation.Frame = null;
        server.SessionCount = 3;
        server.InsightsPoster = null;
        server.InsightsPickerPages = false;
        server.RestartRequired = true;
        await store.RefreshSessionsAsync();
        await Resize(1440);
    }

    private static async Task<byte[]> FixturePosterAsync()
    {
        const int width = 64;
        const int height = 96;
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            var light = (x - 28) * (x - 28) + (y - 32) * (y - 32) < 22 * 22;
            pixels[offset] = light ? (byte)74 : (byte)(110 + y);
            pixels[offset + 1] = light ? (byte)183 : (byte)(55 + x);
            pixels[offset + 2] = light ? (byte)246 : (byte)34;
            pixels[offset + 3] = 255;
        }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new DataReader(stream);
        var bytes = new byte[checked((int)stream.Size)];
        await reader.LoadAsync((uint)bytes.Length);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
