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
    private bool _typographyModified;

    public void Apply(string textScale, string textWeight, bool highContrast, DependencyObject? root = null)
    {
        var settings = settingsService.Load();
        settings.UiTextScale = NormalizeScale(textScale);
        settings.UiTextWeight = textWeight == "strong" ? "strong" : "default";
        settings.UiHighContrast = highContrast;
        settingsService.Save(settings);

        // ApplyTheme reads the just-saved preference and applies the in-app high-contrast
        // palette itself, then a Windows contrast theme over it; writing brushes here
        // afterwards would cover the system palette. Turning high contrast off stays exact.
        themeService.ApplyTheme(themeService.CurrentTheme);

        root ??= App.MainWindowInstance?.Content;
        if (root != null) ApplyTypography(root, ScaleFactor(settings.UiTextScale), settings.UiTextWeight == "strong");
    }

    /// <summary>
    /// Applies saved typography to newly shown content. Brushes are application
    /// resources already set by <see cref="Apply"/> and ThemeService, so route
    /// navigation must not persist settings or reapply the theme.
    /// </summary>
    public void ApplySaved(DependencyObject? root = null)
    {
        var settings = settingsService.Load();
        root ??= App.MainWindowInstance?.Content;
        if (root == null || !AccessibilityTypographyPolicy.RequiresTreeWalk(
                settings.UiTextScale, settings.UiTextWeight, _typographyModified))
            return;
        ApplyTypography(root, ScaleFactor(NormalizeScale(settings.UiTextScale)), settings.UiTextWeight == "strong");
    }

    private void ApplyTypography(DependencyObject node, double scale, bool strong)
    {
        // Once any element is rewritten, retained pages may hold scaled values
        // that must be restored even after the preference returns to default.
        if (scale != 1 || strong) _typographyModified = true;
        ApplyTypographyTree(node, scale, strong);
    }

    private void ApplyTypographyTree(DependencyObject node, double scale, bool strong)
    {
        // Capture/apply descendants while their inherited font is still unscaled.
        // Changing a parent first made newly realized child baselines compound the scale.
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            ApplyTypographyTree(VisualTreeHelper.GetChild(node, i), scale, strong);

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
}
