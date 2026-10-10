using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private Grid? _savedWhoDecides;
    private Grid? _savedSyncStrip;
    private string? _savedSourceEditorId;
    private bool _savedSourceChanging;

    private void BuildSavedSyncedContents(bool resetSource = true)
    {
        if (!ViewModel.IsImportedCollection || ViewModel.LoadedCollection is not { } collection) return;
        Detach(ImportedDefaultSortCombo); Detach(_titleLimit); Detach(_savedSchedule); Detach(SourceUrlTextBox); Detach(_syncedMatchButton);
        RemoveFrom(_syncedBody, _syncedMatchButton); RemoveFrom(_syncedBody, _titleLimit);
        var body = new StackPanel { Spacing = 20 };
        ImportedSourceSection.Child = body;
        body.Children.Add(new StackPanel { Spacing = 4, Children = {
            new TextBlock { Text = "The list it follows", FontSize = 17, LineHeight = 25.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold },
            SavedSyncedCopy("Titles come from this list and update on its schedule.", 13.5)
        } });
        if (collection.LastSyncStatus == "failed") body.Children.Add(new Border { Padding = new(14), CornerRadius = new(12), BorderBrush = CurrentBrush("ErrorBrush"), BorderThickness = new(1), Child = SavedSyncedCopy(collection.LastSyncMessage ?? "The last sync failed. Your titles are still here.", 13) });
        _savedSyncStrip = new Grid();
        foreach (var _ in new[] { 0, 1, 2 }) _savedSyncStrip.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _savedSyncStrip.RowDefinitions.Add(new() { Height = GridLength.Auto }); _savedSyncStrip.RowDefinitions.Add(new() { Height = GridLength.Auto }); _savedSyncStrip.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var last = ViewModel.IsSyncing ? "Syncing now" : string.IsNullOrWhiteSpace(collection.LastSyncAt) ? "Not synced yet" : RelativeSyncTime(collection.LastSyncAt);
        var lastLine = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
        if (!string.IsNullOrWhiteSpace(collection.LastSyncAt)) lastLine.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new(4), VerticalAlignment = VerticalAlignment.Center, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(collection.LastSyncStatus switch { "success" => Windows.UI.Color.FromArgb(255, 52, 211, 153), "warning" => Windows.UI.Color.FromArgb(255, 251, 191, 36), "failed" => Windows.UI.Color.FromArgb(255, 248, 113, 113), _ => Windows.UI.Color.FromArgb(255, 156, 163, 175) }) });
        lastLine.Children.Add(new TextBlock { Text = last, FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(collection.LastSyncAt)) lastLine.Children.Add(SavedSyncedCopy($"{collection.ItemCount:N0} titles", 12.5));
        var lastColumn = new StackPanel { Spacing = 4, Children = { SavedSyncedCopy("Last sync", 12.5), lastLine } };
        var nextColumn = new StackPanel { Spacing = 4, Children = { SavedSyncedCopy("Next sync", 12.5), new TextBlock { Text = SyncDate(collection.NextSyncAt), FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } } };
        var skippedLine = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
        skippedLine.Children.Add(new TextBlock { Text = ViewModel.SyncSkippedTitles is { } count ? $"{count:N0} title{(count == 1 ? "" : "s")} skipped" : "Not counted yet", FontSize = 14, FontWeight = FontWeights.SemiBold });
        var libraryNames = ViewModel.AvailableLibraries.Where(l => ViewModel.SelectedLibraryIds.Count == 0 || ViewModel.SelectedLibraryIds.Contains(l.Id)).Select(l => l.Name).ToArray();
        var where = libraryNames.Length switch { 0 => "your libraries", 1 => libraryNames[0], _ => string.Join(", ", libraryNames[..^1]) + " and " + libraryNames[^1] };
        var explanation = new Border { Visibility = Visibility.Collapsed, Background = CurrentBrush("SurfaceBrush"), CornerRadius = new(12), Padding = new(12, 10, 12, 10), Child = SavedSyncedCopy(ViewModel.SyncSkippedTitles is { } skippedCount ? $"{skippedCount:N0} title{(skippedCount == 1 ? "" : "s")} on the list {(skippedCount == 1 ? "isn't" : "aren't")} in {where}, so they're skipped. Add them to one of those libraries and they join at the next sync." : $"Titles on the list that aren't in {where} are skipped." + (ViewModel.SourceKind == "trakt" ? "" : " Sync now to count them."), 13) };
        var why = new Button { Content = WebUiIcon.Create("info", 16), MinHeight = 0, MinWidth = 0, Height = 24, Width = 24, Padding = new(0), Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        why.Click += (_, _) => explanation.Visibility = explanation.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(why, "Why titles are skipped"); skippedLine.Children.Add(why);
        var skipped = new StackPanel { Spacing = 4, Children = { SavedSyncedCopy("Not in your libraries", 12.5), skippedLine } };
        foreach (var cell in new[] { lastColumn, nextColumn, skipped }) _savedSyncStrip.Children.Add(new Border { Child = cell, Padding = new(16, 12, 16, 12), BorderBrush = CurrentBrush("BorderBrush") });
        body.Children.Add(new Border { Child = _savedSyncStrip, CornerRadius = new(14), BorderBrush = CurrentBrush("BorderBrush"), BorderThickness = new(1) });
        body.Children.Add(explanation);
        if (resetSource || _savedSourceEditorId != collection.Id) { _savedSourceChanging = false; _savedSourceEditorId = collection.Id; }
        body.Children.Add(BuildSavedSourceLine());
        if (ViewModel.SourceKind is not ("trakt" or "tmdb_discover"))
        {
            _savedWhoDecides = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
            _savedWhoDecides.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); _savedWhoDecides.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            _savedWhoDecides.RowDefinitions.Add(new() { Height = GridLength.Auto }); _savedWhoDecides.RowDefinitions.Add(new() { Height = GridLength.Auto });
            _savedWhoDecides.Children.Add(SavedDecisionCard("The list decides", "lock", ["Which titles are in it", "Their order, while the sort is “List order”"]));
            _savedWhoDecides.Children.Add(SavedDecisionCard("You decide", "pencil", ["Name, description and artwork", ViewModel.Capabilities?.SyncScheduleEditable == true ? "Order, max titles and the schedule" : "Order and max titles", "Where it shows"]));
            body.Children.Add(_savedWhoDecides);
        }
        else _savedWhoDecides = null;
        _syncedSource = ViewModel.SourceKind;
        var media = collection.SourceConfig?.GetValueOrDefault("media_type")?.ToString();
        _syncedPick = new() { MediaKind = media == "all" ? "mixed" : media ?? "mixed" };
        if (_syncedMatchButton.Flyout is Flyout { Content: ScrollViewer previousChoices }) previousChoices.Content = null;
        _syncedMatchButton.Flyout = new Flyout { Content = new ScrollViewer { Content = _syncedLibraryChoices, MaxHeight = 320, MinWidth = 200, HorizontalScrollMode = ScrollMode.Disabled } };
        _syncedMatchButton.IsEnabled = ViewModel.CollectionType != "trakt"; RefreshSyncedLibraries();
        body.Children.Add(SyncedDivider(_syncedMatchButton));
        _titleLimit.Text = ViewModel.MaxItemsText ?? ""; _titleLimit.IsEnabled = ViewModel.CollectionType != "trakt";
        // The same input is reused after Create becomes a saved list.
        if (!_newSynced) { _titleLimit.LostFocus -= SavedLimit_LostFocus; _titleLimit.LostFocus += SavedLimit_LostFocus; _titleLimit.KeyDown -= SavedLimit_KeyDown; _titleLimit.KeyDown += SavedLimit_KeyDown; }
        body.Children.Add(BuildSyncedOrder());
        if (ImportedDefaultSortCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, "")) is { } listOrder) listOrder.Content = "List order";
        _savedSchedule.MinHeight = 0; _savedSchedule.Height = 36; _savedSchedule.FontSize = 14;
        AutomationProperties.SetName(_savedSchedule, "Sync schedule");
        var schedule = new StackPanel { Spacing = 12, Children = { Field("Sync", _savedSchedule) } };
        if (ViewModel.Capabilities?.SyncScheduleEditable != true) schedule.Children.Add(SavedSyncedCopy("This server doesn't let profiles change a list's schedule.", 13));
        else if (!ViewModel.CanEditSavedSyncSchedule) schedule.Children.Add(SavedSyncedCopy("A stopped Trakt list can't be scheduled again.", 13));
        body.Children.Add(SyncedDivider(schedule));
        UpdateSyncedResponsive();
    }

    private void SavedLimit_LostFocus(object sender, RoutedEventArgs args) => CommitSyncedLimit();
    private void SavedLimit_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args) { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; CommitSyncedLimit(); } }

    private FrameworkElement BuildSavedSourceLine()
    {
        if (ViewModel.SourceKind is "trakt" or "tmdb_discover") return BuildLockedSourceSummary();
        if (!ViewModel.HasEditableSourceUrl)
        {
            var source = new WrapPanel { HorizontalSpacing = 24, VerticalSpacing = 4 };
            source.Children.Add(SavedSyncedCopy("Follows", 13.5));
            source.Children.Add(new TextBlock { Text = ViewModel.SourceKind == "tmdb" ? "TMDB chart: " + SavedChartSummary() : ViewModel.SourceProviderLabel + ": " + ViewModel.SourcePresetSummary, FontSize = 13.5, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
            source.Children.Add(SavedSyncedCopy("Set when it was made", 12.5)); return source;
        }
        if (_savedSourceChanging)
        {
            SourceUrlTextBox.MaxWidth = double.PositiveInfinity; SourceUrlTextBox.HorizontalAlignment = HorizontalAlignment.Stretch; SourceUrlTextBox.MinHeight = 0; SourceUrlTextBox.Height = 36; SourceUrlTextBox.IsEnabled = true;
            return new StackPanel { Spacing = 8, Children = { Field(ViewModel.SourceKind == "mdblist" ? "MDBList link" : "Public TMDB list link", SourceUrlTextBox), SavedSyncedCopy("The new link is used at the next sync.", 12) } };
        }
        var labels = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = ViewModel.SourceProviderLabel, FontSize = 14, FontWeight = FontWeights.SemiBold },
            new TextBlock { Text = (ViewModel.SourceUrl ?? "").Replace("https://", "").Replace("http://", "").TrimEnd('/'), FontSize = 13, Foreground = CurrentBrush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis } } };
        var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.Children.Add(labels);
        var change = new Button { Content = "Change link", Height = 32, MinHeight = 0, Style = (Style)Application.Current.Resources["OutlineButtonStyle"] };
        change.Click += (_, _) => { _savedSourceChanging = true; BuildSavedSyncedContents(false); SourceUrlTextBox.Focus(FocusState.Programmatic); };
        Grid.SetColumn(change, 1); row.Children.Add(change);
        return new Border { Child = row, Background = CurrentBrush("SurfaceBrush"), CornerRadius = new(12), Padding = new(16, 12, 16, 12) };
    }

    private TextBlock SavedSyncedCopy(string text, double size) => new() { Text = text, FontSize = size, LineHeight = size * 1.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = CurrentBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap };
    private FrameworkElement BuildLockedSourceSummary()
    {
        var discover = ViewModel.SourceKind == "tmdb_discover";
        var config = ViewModel.LoadedCollection?.SourceConfig;
        string Value(string key) => config?.GetValueOrDefault(key)?.ToString() ?? "";
        var media = Value("media_type") == "tv" ? "TV shows" : discover ? Value("media_type") == "movie" ? "Movies" : "Titles" : "movies";
        var finds = media;
        if (discover && config?.GetValueOrDefault("discover") is { } raw)
        {
            var element = System.Text.Json.JsonSerializer.SerializeToElement(raw);
            var sort = element.ValueKind == System.Text.Json.JsonValueKind.Object && element.TryGetProperty("sort_by", out var order) ? order.GetString() : null;
            var description = sort switch { "popularity.desc" => "most popular first", "vote_average.desc" => "highest rated first", "vote_count.desc" => "most voted first", "revenue.desc" => "highest grossing first", "primary_release_date.desc" or "release_date.desc" or "first_air_date.desc" => "newest first", _ => null };
            if (description != null) finds += ", " + description;
        }
        else if (!discover)
        {
            var preset = Value("preset"); if (string.IsNullOrWhiteSpace(preset)) preset = "trending";
            finds = Value("mode") == "trakt_list" ? new[] { Value("list_url"), Value("url"), ViewModel.SourceUrl }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "" : char.ToUpperInvariant(preset[0]) + preset[1..] + " " + media;
        }
        var details = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
        details.ColumnDefinitions.Add(new() { Width = new(72) }); details.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var n = 0; n < 2; n++)
        {
            details.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var label = SavedSyncedCopy(n == 0 ? "Source" : "Finds", 13.5); Grid.SetRow(label, n); details.Children.Add(label);
            var value = new TextBlock { Text = n == 0 ? discover ? "TMDB Discover" : "Trakt" : finds, FontSize = 13.5, TextWrapping = TextWrapping.Wrap }; Grid.SetRow(value, n); Grid.SetColumn(value, 1); details.Children.Add(value);
        }
        return new Border { CornerRadius = new(12), Padding = new(16, 14, 16, 14), Background = CurrentBrush("SurfaceBrush"), Child = new StackPanel { Spacing = 12, Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { WebUiIcon.Create("lock", 16), new TextBlock { Text = discover ? "Made by a starter pack. Its rules can't be changed here." : "New Trakt lists aren't supported. This one keeps its source and libraries.", FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } } },
            details, SavedSyncedCopy(discover ? "You can still change its name, artwork, max titles, schedule and where it shows." : "You can still change its name, artwork, order, schedule and where it shows.", 13)
        } } };
    }
    private string SavedSourceLabel() => ViewModel.SourceKind switch { "tmdb" => "TMDB chart", "tmdb_list" => "TMDB list", "tmdb_discover" => "TMDB Discover", "mdblist" => "MDBList", "trakt" => "Trakt", _ => ViewModel.SourceProviderLabel };
    private string SavedChartSummary()
    {
        var config = ViewModel.LoadedCollection?.SourceConfig;
        var preset = config?.GetValueOrDefault("preset")?.ToString();
        if (string.IsNullOrWhiteSpace(preset)) preset = "trending";
        var title = preset switch { "top_rated" => "Top rated", "now_playing" => "Now playing", "airing_today" => "Airing today", "on_the_air" => "On the air", _ => char.ToUpperInvariant(preset[0]) + preset[1..] };
        var media = config?.GetValueOrDefault("media_type")?.ToString() switch { "movie" => "movies", "tv" => "TV shows", _ => "movies and TV shows" };
        var window = preset == "trending" ? config?.GetValueOrDefault("time_window")?.ToString() == "week" ? ", this week" : ", today" : "";
        return title + " " + media + window;
    }
    private Border SavedDecisionCard(string title, string icon, string[] lines)
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { WebUiIcon.Create(icon, 14, CurrentBrush("SecondaryTextBrush")), new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold } } };
        var body = new StackPanel { Spacing = 2 }; body.Children.Add(heading); heading.Margin = new(0, 0, 0, 4);
        foreach (var line in lines) body.Children.Add(SavedSyncedCopy(line, 12.5));
        var color = (CurrentBrush("SecondaryBackgroundBrush") as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color ?? Microsoft.UI.Colors.Transparent;
        return new Border { Child = body, Padding = new(14, 12, 14, 12), CornerRadius = new(12), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(color) { Opacity = .55 } };
    }
    private static string SyncDate(string? date) => DateTimeOffset.TryParse(date, out var value) ? $"{SiloPlayer.Helpers.DateTimeDisplay.FormatDate(value, medium: true)}, {SiloPlayer.Helpers.DateTimeDisplay.FormatTime(value)}" : "Not scheduled";
    private static string RelativeSyncTime(string date)
    {
        if (!DateTimeOffset.TryParse(date, out var time)) return "Not synced yet";
        var elapsed = DateTimeOffset.UtcNow - time;
        if (elapsed.TotalMinutes < 1) return "Just now";
        var value = Math.Round(elapsed.TotalMinutes);
        foreach (var (size, unit) in new[] { (60, "minute"), (24, "hour"), (30, "day"), (12, "month") })
        {
            if (value < size) return $"{value} {unit}{(value == 1 ? "" : "s")} ago";
            value = Math.Round(value / size);
        }
        return $"{value} year{(value == 1 ? "" : "s")} ago";
    }
}
