using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

internal static class AccessibilityNavigationNativeFixture
{
    internal static async Task RunAsync(IServiceProvider services, NavigationService navigation)
    {
        var settings = services.GetRequiredService<SettingsService>();
        var accessibility = services.GetRequiredService<AccessibilityService>();
        var text = new TextBlock { Text = "Retained native page", FontSize = 20, LineHeight = 30 };
        var retained = new Page { Content = text };
        navigation.Frame!.Content = retained;
        await Task.Delay(80);
        var path = Path.Combine(Program.ResultDirectory, "local-" +
            (Environment.GetEnvironmentVariable("SILO_PROFILE_MENU_WIDTH") ?? "1920"), "settings.json");
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        var before = File.ReadAllBytes(path);
        var weightBefore = text.ReadLocalValue(TextBlock.FontWeightProperty);
        accessibility.ApplySaved(retained);
        if (File.GetLastWriteTimeUtc(path) != stamp || !before.SequenceEqual(File.ReadAllBytes(path)))
            throw new InvalidOperationException("Route accessibility rewrote the isolated settings file.");
        if (!Equals(weightBefore, text.ReadLocalValue(TextBlock.FontWeightProperty)))
            throw new InvalidOperationException("Default route accessibility needlessly rewrote typography.");

        accessibility.Apply("large", "strong", true, retained);
        if (text.FontSize != 22.5 || text.LineHeight != 33.75 || text.FontWeight.Weight != 520)
            throw new InvalidOperationException("Non-default native typography was not applied.");
        var contrast = ((SolidColorBrush)Application.Current.Resources["BorderBrush"]).Color;
        File.SetLastWriteTimeUtc(path, stamp);
        accessibility.ApplySaved(retained);
        if (File.GetLastWriteTimeUtc(path) != stamp || text.FontSize != 22.5 ||
            ((SolidColorBrush)Application.Current.Resources["BorderBrush"]).Color != contrast)
            throw new InvalidOperationException("Saved route typography compounded scale or changed theme/persistence.");

        var secondText = new TextBlock { Text = "New page", FontSize = 16 };
        var second = new Page { Content = secondText };
        navigation.Frame.Content = second;
        await Task.Delay(80);
        accessibility.ApplySaved(second);
        if (secondText.FontSize != 18)
            throw new InvalidOperationException("New native page did not inherit saved readability preferences.");
        accessibility.Apply("default", "default", false, second);
        navigation.Frame.Content = retained;
        await Task.Delay(80);
        accessibility.ApplySaved(retained);
        if (text.FontSize != 20 || text.LineHeight != 30 || text.FontWeight.Weight != 400)
            throw new InvalidOperationException("Returning to a retained page did not restore default typography.");
        Program.Log("PASS: route accessibility performs no settings write, preserves contrast, scales new pages and restores retained native text.");
    }
}
