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
                Check(scope.Client.TryInvalidateAccessContext(scope.Client.CaptureContext(), out var context,
                    transportCatchUp: handler == "OnReadCatchUpRequested"), "Could not advance catch-up authority.");
                typeof(MainWindow).GetMethod(handler, Private)!.Invoke(scope.Window, [context]);
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
