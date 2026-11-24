using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Views.Dialogs;

/// <summary>
/// What a Home layout import will do, shown before anything is saved (official HomeLayoutImportSummary and
/// HideRowsChecklist, silo-server 3b2b32168, #4648). Checking or unchecking a server row to hide refreshes the
/// lines that depend on it and raises <see cref="Changed"/> so the dialog can enable or disable Import.
/// </summary>
internal sealed class HomeLayoutImportSummary
{
    /// <summary>The panel to place in the dialog.</summary>
    public Border Root { get; } = new();
    // The page names inside a message are emphasised: the arguments carry these marks and SetRich splits on them.
    private const char Mark = '\u0001';
    private readonly HomeLayoutPlan _plan;
    private readonly TextBlock _nothing, _replaces, _hidesOnly, _addsFromOther;
    private readonly List<(HomeLayoutHideOption Option, CheckBox Box)> _rows = [];

    public event Action? Changed;
    public bool HasChanges => _plan.HasChanges;

    public HomeLayoutImportSummary(HomeLayoutPlan plan)
    {
        _plan = plan;
        Root.Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];
        Root.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
        Root.BorderThickness = new Thickness(1); Root.CornerRadius = new CornerRadius(8); Root.Padding = new Thickness(12);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(_nothing = Line(Text("web.home.layout.nothingApplies", "Nothing in this file applies to this profile on this server.")));
        panel.Children.Add(_replaces = Line(""));
        panel.Children.Add(_hidesOnly = Line(""));
        panel.Children.Add(_addsFromOther = Line(""));
        if (plan.HideWatchedItems is { } hideWatched)
            panel.Children.Add(Line(hideWatched ? Text("web.home.layout.hideWatchedOn", "Turns Hide watched items on.") : Text("web.home.layout.hideWatchedOff", "Turns Hide watched items off.")));
        if (plan.HideOptions.Count > 0) panel.Children.Add(BuildChecklist());
        if (plan.SkippedServerSectionChanges > 0) panel.Children.Add(Line(HomeLayoutTransfer.ServerChangesText(plan.SkippedServerSectionChanges), muted: true));
        if (plan.SkippedPages.Count > 0) panel.Children.Add(Line(HomeLayoutTransfer.NoLibraryMatchesText(Join(plan.SkippedPages)), muted: true));
        if (plan.SkippedSections.Count > 0)
        {
            var skipped = new StackPanel { Spacing = 2 };
            skipped.Children.Add(Line(Text("web.home.layout.skippedHeading", "These sections are skipped:"), muted: true));
            foreach (var row in plan.SkippedSections)
            {
                var line = Line("•  " + Text("web.home.layout.skippedSection", $"{row.Title} ({row.Page}): {row.ReasonText}",
                    ("section", row.Title), ("page", row.Page), ("reason", row.ReasonText)), muted: true);
                line.Margin = new Thickness(8, 0, 0, 0); skipped.Children.Add(line);
            }
            panel.Children.Add(skipped);
        }
        Root.Child = panel;
        Refresh();
    }

    private static string Text(string key, string english, params (string Name, object? Value)[] arguments)
        => HomeLayoutTransfer.T(key, english, arguments);
    // "Home, Movies" in English.
    private static string Join(IEnumerable<string> names) => string.Join(", ", names);
    private static string Emphasis(string names) => Mark + names + Mark;

    private static TextBlock Line(string text, bool muted = false)
    {
        var block = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };
        if (muted) block.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        SetRich(block, text); return block;
    }

    private static void SetRich(TextBlock block, string text)
    {
        block.Inlines.Clear();
        var parts = text.Split(Mark);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var run = new Run { Text = parts[i] };
            if (i % 2 == 1) run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            block.Inlines.Add(run);
        }
    }

    private UIElement BuildChecklist()
    {
        var list = new StackPanel { Spacing = 6 };
        var heading = new TextBlock { Text = Text("web.home.layout.addedRowsHeading", "Hide server rows added since this export:"), FontSize = 14, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        list.Children.Add(heading);
        var showPage = _plan.PagesWithTokens > 1;
        foreach (var option in _plan.HideOptions)
        {
            var title = option.Row.DisplayTitle;
            var label = showPage || option.PageKey != "home:"
                ? Text("web.home.layout.rowWithPage", $"{title} ({option.PageLabel})", ("section", title), ("page", option.PageLabel))
                : title;
            var box = new CheckBox { IsChecked = option.Checked, MinWidth = 0, Content = new TextBlock { Text = label, FontSize = 14, TextWrapping = TextWrapping.Wrap } };
            AutomationProperties.SetName(box, label);
            box.Checked += (_, _) => Toggled(option, true);
            box.Unchecked += (_, _) => Toggled(option, false);
            _rows.Add((option, box)); list.Children.Add(box);
        }
        return list;
    }

    /// <summary>Rows can be left alone while an import is running.</summary>
    public void SetEnabled(bool enabled) { foreach (var (_, box) in _rows) box.IsEnabled = enabled; }

    private void Toggled(HomeLayoutHideOption option, bool value)
    {
        if (option.Checked == value) return;
        option.Checked = value; Refresh(); Changed?.Invoke();
    }

    private void Refresh()
    {
        var pages = Join(_plan.Pages.Select(page => page.Label));
        var hideOnly = Join(_plan.CheckedUnappliedPages.Select(page => page.Label));
        _nothing.Visibility = _plan.HasChanges ? Visibility.Collapsed : Visibility.Visible;
        Set(_replaces, _plan.Pages.Count > 0 && _plan.SameServer, () => Text("web.home.layout.replacesOn", $"Replaces this profile's layout on: {Emphasis(pages)}.", ("pages", Emphasis(pages))));
        Set(_hidesOnly, hideOnly.Length > 0 && _plan.SameServer, () => Text("web.home.layout.hidesOnlyOn", $"Hides selected rows on: {Emphasis(hideOnly)} without changing your other rows there.", ("pages", Emphasis(hideOnly))));
        Set(_addsFromOther, _plan.Pages.Count > 0 && !_plan.SameServer, () => Text("web.home.layout.addsFromOtherServer",
            $"This layout comes from another server. Its own sections are added to: {Emphasis(pages)}, replacing sections you added here before. Your changes to this server's sections stay.", ("pages", Emphasis(pages))));
    }

    private static void Set(TextBlock block, bool visible, Func<string> text)
    {
        block.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) SetRich(block, text());
    }
}
