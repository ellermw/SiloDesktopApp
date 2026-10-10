using System.Runtime.CompilerServices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;
using Windows.UI.Text;

namespace SiloPlayer.Services;

/// <summary>Applies the WebUI readability preferences to native WinUI content.</summary>
public sealed class AccessibilityService(SettingsService settingsService, ThemeService themeService)
{
    private sealed record Baseline(double FontSize, FontWeight FontWeight, double LineHeight = 0);
    private readonly ConditionalWeakTable<FrameworkElement, Baseline> _baselines = new();

    public void Apply(string textScale, string textWeight, bool highContrast, DependencyObject? root = null)
    {
        var settings = settingsService.Load();
        settings.UiTextScale = NormalizeScale(textScale);
        settings.UiTextWeight = textWeight == "strong" ? "strong" : "default";
        settings.UiHighContrast = highContrast;
        settingsService.Save(settings);

        // Restore the active theme first so turning high contrast off is exact.
        themeService.ApplyTheme(themeService.CurrentTheme);
        if (highContrast)
        {
            SetBrush("PrimaryTextBrush", "#FFFFFF");
            SetBrush("SecondaryTextBrush", "#D1D5DB");
            SetBrush("TertiaryTextBrush", "#B6C0CE");
            SetBrush("BorderBrush", "#8091A7");
        }

        root ??= App.MainWindowInstance?.Content;
        if (root != null) ApplyTypography(root, ScaleFactor(settings.UiTextScale), settings.UiTextWeight == "strong");
    }

    public void ApplySaved(DependencyObject? root = null)
    {
        var settings = settingsService.Load();
        Apply(settings.UiTextScale, settings.UiTextWeight, settings.UiHighContrast, root);
    }

    private void ApplyTypography(DependencyObject node, double scale, bool strong)
    {
        // Capture/apply descendants while their inherited font is still unscaled.
        // Changing a parent first made newly realized child baselines compound the scale.
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            ApplyTypography(VisualTreeHelper.GetChild(node, i), scale, strong);

        if (node is TextBlock text)
        {
            var baseline = _baselines.GetValue(text, element => new Baseline(text.FontSize, text.FontWeight, text.LineHeight));
            text.FontSize = baseline.FontSize * scale;
            text.LineHeight = baseline.LineHeight * scale;
            text.FontWeight = strong ? StrongWeight(baseline.FontWeight) : baseline.FontWeight;
        }
        else if (node is Control control)
        {
            var baseline = _baselines.GetValue(control, element => new Baseline(control.FontSize, control.FontWeight));
            control.FontSize = baseline.FontSize * scale;
            control.FontWeight = strong ? StrongWeight(baseline.FontWeight) : baseline.FontWeight;
        }
    }

    private static string NormalizeScale(string? value) => value is "large" or "x-large" ? value : "default";
    private static double ScaleFactor(string value) => value switch { "large" => 1.125, "x-large" => 1.25, _ => 1 };
    private static FontWeight StrongWeight(FontWeight baseline) => new()
    {
        Weight = baseline.Weight switch { < 500 => 520, < 600 => 600, < 700 => 700, _ => 800 }
    };

    private static void SetBrush(string key, string hex)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            var clean = hex.TrimStart('#');
            brush.Color = Windows.UI.Color.FromArgb(255,
                Convert.ToByte(clean[..2], 16), Convert.ToByte(clean.Substring(2, 2), 16), Convert.ToByte(clean.Substring(4, 2), 16));
        }
    }
}
