using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>Dashboard part of ScrollSmoothnessNativeFixture: the default widgets, Now Playing with three sessions, 3440x1440.</summary>
internal static partial class AdminDashboardNativeFixture
{
    internal static async Task ScrollSmoothnessAsync(Window window)
    {
        var server = new Server { Sessions = 3 };
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        var realtime = new Realtime();
        using var authority = new AdminMonitorAuthority(client, auth);
        var api = new AdminMonitorApi(client);
        using var store = new ServerActivityStore(api, authority, realtime);
        var clock = new CaptureClock();
        await using var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(api).AddSingleton(store)
            .AddSingleton(new AdminInsightsApi(client)).AddSingleton(new AdminInsightsRefreshGate(clock))
            .AddSingleton<TimeProvider>(clock).AddSingleton(http)
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "dashboard-scroll-images")))
            .AddSingleton(new ToastService()).AddSingleton(new SiloPlayer.Helpers.NavigationService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddSingleton<IDashboardLayoutCache>(new DashboardMemoryLayoutCache())
            .BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        field.SetValue(null, services);
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        var previousContent = window.Content;
        try
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(ScrollSmoothnessNativeFixture.ClientWidth, ScrollSmoothnessNativeFixture.ClientHeight));
            var frame = new Frame();
            window.Content = frame;
            frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Dashboard));
            var innerFrame = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(frame.Content)!;
            var page = (AdminDashboardPage)innerFrame.Content;
            page.SetHostActivity(visible: true, focused: true);
            await Until(() => page.LayoutSync?.IsLoaded == true && page.Store?.HasDashboardData == true, 10000);
            await Until(() => page.Store?.Get(DashboardDataStore.Sessions).As<IReadOnlyList<SiloPlayer.Core.Models.Admin.DashboardSession>>()?.Count == 3, 5000);
            await Task.Delay(1500);
            var scroller = (ScrollViewer)typeof(AdminDashboardPage).GetField("_scroll", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Program.Log($"INFO: scroll dashboard default widgets={page.PlacedWidgets().Count} scrollable={scroller.ScrollableHeight:0}");
            ScrollSmoothnessNativeFixture.Check(page.PlacedWidgets().Count == 15, "dashboard shows the 15 default widgets");
            if (scroller.ScrollableHeight < 1200)
            {
                // The default layout fits a 1440px-high window; the owner's scrolls it, so place the Add-only widgets too.
                foreach (var widget in page.LayoutSync!.HiddenWidgets.ToList()) page.LayoutSync.AddWidget(widget.Id);
                await (page.Store?.RefreshDashboardAsync(manual: true) ?? Task.CompletedTask);
                await Task.Delay(2000);
                Program.Log($"INFO: scroll dashboard all widgets={page.PlacedWidgets().Count} scrollable={scroller.ScrollableHeight:0}");
            }
            await ScrollSmoothnessNativeFixture.MeasurePageAsync(scroller, "dashboard", frame);
            // Live: the page's clock runs (so its own polls fire), a library scan reports progress each
            // second, and the recent-errors read changes every five seconds, as on a scanning server.
            var processed = 120; var ticks = 0;
            var readsBefore = server.Reads.Count;
            await ScrollSmoothnessNativeFixture.MeasurePageAsync(scroller, "dashboard-live", frame, () =>
            {
                clock.Now = clock.Now.AddSeconds(1);
                processed += 7;
                realtime.PublishScans(System.Text.Json.JsonDocument.Parse(
                    "[{\"id\":\"scan-1\",\"library_id\":1,\"mode\":\"library\",\"status\":\"running\",\"result\":{\"total_files\":4000,\"files_processed\":" + processed + "}}]").RootElement.Clone());
                if (++ticks % 5 == 0) server.LogVersion++;
            });
            Program.Log($"INFO: scroll dashboard-live server reads during measurement={server.Reads.Count - readsBefore}");
        }
        finally
        {
            var retired = window.Content as FrameworkElement;
            if (retired is Frame frame) frame.Navigate(typeof(Page));
            window.Content = previousContent;
            if (retired != null) await Until(() => !retired.IsLoaded);
            field.SetValue(null, previous);
        }
    }
}
