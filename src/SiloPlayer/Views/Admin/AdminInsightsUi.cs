using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Core.Services.Admin;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

internal static class AdminInsightsUi
{
    internal static FrameworkElement KpiCard(string label, string value, UIElement? subtext = null, ActivityTone? accent = null)
    {
        var stack = new StackPanel { Spacing = 4, Children =
        {
            A.Text(label, 11, A.Muted, FontWeights.Medium, trim: false),
            A.Text(value, 24, accent is { } tone ? A.LabelColor(tone) : A.Foreground, FontWeights.ExtraBold, trim: false),
        }};
        if (subtext != null) stack.Children.Add(subtext);
        return new Border { CornerRadius = new(16), Padding = new(16), Background = A.Solid(A.Surface), Child = stack };
    }

    internal static FrameworkElement Notice(string message)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.Children.Add(A.Icon("info", 14, A.Muted));
        var label = A.Text(message, 12, A.Muted, trim: false);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return new Border { CornerRadius = new(12), Padding = new(14, 10, 14, 10), BorderThickness = new(1),
            BorderBrush = A.Solid(A.Border, 0.7), Background = A.Solid(A.Surface, 0.35), Child = row };
    }

    internal static FrameworkElement SectionError(string? detail, Action? retry, bool retryEnabled = true)
    {
        var title = AdminInsightsText.T("web.admin.insights.loadError");
        var stack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new Border { Child = A.Icon("circle-alert", 28, A.Destructive), HorizontalAlignment = HorizontalAlignment.Center },
                new StackPanel { Spacing = 4, Children =
                {
                    A.Text(title, 14, A.Foreground, FontWeights.SemiBold, trim: false),
                    A.Text(detail ?? title, 12, A.Muted, trim: false),
                }},
            }};
        foreach (var label in ((StackPanel)stack.Children[1]).Children.OfType<TextBlock>())
        {
            label.TextAlignment = TextAlignment.Center;
            label.MaxWidth = 448;
        }
        if (retry != null)
        {
            var label = AdminInsightsText.T("web.admin.insights.retry");
            var button = A.Outline(label);
            AutomationProperties.SetName(button, label);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            button.IsEnabled = retryEnabled;
            button.Click += (_, _) => retry();
            stack.Children.Add(button);
        }
        var panel = new Border { CornerRadius = new(16), Padding = new(16, 40, 16, 40), Background = A.Solid(A.Surface), Child = stack };
        AutomationProperties.SetLiveSetting(panel, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(panel, "AdminInsightsSectionError");
        return panel;
    }
}

/// <summary>A results surface that retains its content while a new query loads. Supply each
/// navigational control with its result so it cannot open a stale drilldown by mouse or keyboard.</summary>
internal sealed class AdminInsightsResultHost : Grid
{
    private readonly ContentPresenter _result = new();
    private readonly ProgressBar _sweep = new()
    {
        Height = 2, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
    };
    private readonly Dictionary<Control, bool> _links = [];
    private bool _refreshing;

    internal AdminInsightsResultHost()
    {
        Children.Add(_result);
        Children.Add(_sweep);
    }

    internal void SetResult(UIElement? result, IEnumerable<Control>? links = null)
    {
        SetRefreshing(false);
        _links.Clear();
        _result.Content = result;
        if (links != null)
            foreach (var link in links) _links.TryAdd(link, link.IsEnabled);
    }

    internal void SetRefreshing(bool refreshing)
    {
        if (_refreshing == refreshing) return;
        _refreshing = refreshing;
        _sweep.IsIndeterminate = refreshing;
        _sweep.Visibility = refreshing ? Visibility.Visible : Visibility.Collapsed;
        foreach (var link in _links.Keys.ToArray())
        {
            if (refreshing) _links[link] = link.IsEnabled;
            link.IsEnabled = !refreshing && _links[link];
        }
    }
}
