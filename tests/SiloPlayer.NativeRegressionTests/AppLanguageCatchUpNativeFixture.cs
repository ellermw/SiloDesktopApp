using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

internal static partial class EpisodeQuickPlayNativeFixture
{
    internal static async Task VerifyAppLanguageCatchUpAsync()
    {
        var locales = LocaleService.Shared;
        var before = locales.Current;
        await using var scope = new LifecycleScope();
        try
        {
            scope.Auth.SetTokens("fixture-access", "fixture-refresh", 3600);
            scope.Auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
            scope.Auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture" });
            scope.Wire.AppLanguage = "en";
            await scope.ShowAsync();
            scope.Frame.Content = new Page();
            var coordinator = new AppLocaleCoordinator(new(new SettingsApi(scope.Client)), locales,
                new SettingsService(Path.Combine(Program.ResultDirectory, "language-catch-up")),
                () => ["en-US"], "en-US", AppLocalization.PostTo(scope.Window.DispatcherQueue));
            SetField(scope.Window, "_appLocaleCoordinator", coordinator);
            Check(await coordinator.RefreshAsync(), "Initial shell language did not load.");
            foreach (var handler in new[] { "OnReadCatchUpRequested", "OnAccessChanged" })
            {
                scope.Wire.AppLanguage = handler == "OnReadCatchUpRequested" ? "ar" : "ja";
                scope.Wire.AppLanguageHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var previous = locales.Current.Language;
                var reconnect = handler == "OnReadCatchUpRequested";
                Check(scope.Client.TryInvalidateAccessContext(scope.Client.CaptureContext(), out var context,
                    transportCatchUp: reconnect), "Could not advance catch-up authority.");
                var method = typeof(MainWindow).GetMethod(handler, Private)!;
                if (reconnect)
                {
                    var path = Path.Combine(Program.ResultDirectory, "ui_lag.txt");
                    var beforeLines = UiStallNativeFixture.Lines(path).Count;
                    var monitor = new UiStallMonitor(UiStallLog.Shared, () => scope.Frame, () => "idle");
                    var dispatcher = scope.Window.DispatcherQueue;
                    void BlockUiInvalidation()
                    {
                        Check(dispatcher.HasThreadAccess, "Reconnect invalidated the catalog off the UI thread.");
                        Thread.Sleep(950);
                    }
                    scope.Catalog.LibraryCacheInvalidated += BlockUiInvalidation;
                    monitor.Start();
                    try
                    {
                        // Deliver the reconnect on a worker, just as the event channel does.
                        await Task.Run(() => method.Invoke(scope.Window, [context]));
                        await Until(() => UiStallNativeFixture.Lines(path).Skip(beforeLines).Any(line =>
                            line.Contains("longest_op=catch-up ") && line.Contains("| op_owner=Page |")));
                    }
                    finally
                    {
                        monitor.Stop();
                        scope.Catalog.LibraryCacheInvalidated -= BlockUiInvalidation;
                    }
                }
                else method.Invoke(scope.Window, [context]);
                await Task.Delay(100);
                Check(locales.Current.Language == previous, handler + " cleared the applied presentation while reading.");
                scope.Wire.AppLanguageHold.TrySetResult();
                await Until(() => locales.Current.Language == scope.Wire.AppLanguage);
                Check(coordinator.LoadedOwner == scope.Client.CaptureContext(), handler + " did not load current request authority.");
                Program.Log("PASS: app-language shell " + handler + " re-reads the current effective language and retains presentation during the read.");
                scope.Wire.AppLanguageHold = null;
            }
        }
        finally
        {
            scope.Wire.AppLanguageHold?.TrySetResult();
            locales.Apply(locales.Create(before.Resolution));
        }
    }
}
