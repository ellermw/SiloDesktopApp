using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private bool _normalizingChart;
    private readonly StackPanel _syncedChartUi = new() { Spacing = 16 };
    private readonly Grid _syncedChartCards = new() { RowSpacing = 10, ColumnSpacing = 10 };

    private void ConfigureSyncedChart()
    {
        _chartPreset.Visibility = _chartMedia.Visibility = _chartWindow.Visibility = Visibility.Collapsed;
        _syncedChart.Children.Add(new StackPanel { Visibility = Visibility.Collapsed, Children = { _chartPreset, _chartMedia, _chartWindow } });
        _syncedChart.Children.Add(_syncedChartUi);
        RebuildSyncedChart();
    }

    private void RebuildSyncedChart()
    {
        if (_normalizingChart || _syncedChart.Children.Count == 0) return;
        RemoveFrom(_syncedChartUi, _syncedChartCards);
        _syncedChartUi.Children.Clear(); _syncedChartCards.Children.Clear();
        var entries = new[] { ("trending", "Trending", "What's hot now", "Trending"), ("popular", "Popular", "Most viewed", "Popular"), ("top_rated", "Top rated", "Best scores", "TopRated"),
            ("now_playing", "Now playing", "Movies in cinemas", "NowPlaying"), ("upcoming", "Upcoming", "Movies coming soon", "Upcoming"), ("airing_today", "Airing today", "TV, today", "AiringToday"), ("on_the_air", "On the air", "TV, this week", "OnTheAir") }
            .Select(entry => (entry.Item1, CollectionText.Get("web.collections.tmdbSources.chart" + entry.Item4, entry.Item2), CollectionText.Get("web.collections.tmdbSources.chart" + entry.Item4 + "Hint", entry.Item3))).ToArray();
        var preset = (_chartPreset.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        for (var n = 0; n < entries.Length; n++)
        {
            var (key, label, hint) = entries[n];
            var card = new RadioButton { Tag = key, GroupName = "synced-chart-" + GetHashCode(), IsChecked = preset == key, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(14, 12, 14, 12), MinHeight = 66, Content = new StackPanel { Spacing = 2, Children = {
                    new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = hint, FontSize = 12.5, Foreground = CurrentBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap }
                } } };
            card.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RadioButton">
                  <Grid>
                    <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CheckStates">
                      <VisualState x:Name="Unchecked" />
                      <VisualState x:Name="Checked"><VisualState.Setters><Setter Target="Dot.Opacity" Value="1" /><Setter Target="Frame.BorderBrush" Value="{StaticResource PrimaryTextBrush}" /></VisualState.Setters></VisualState>
                    </VisualStateGroup></VisualStateManager.VisualStateGroups>
                    <Border x:Name="Frame" CornerRadius="16" BorderThickness="1" BorderBrush="{StaticResource BorderBrush}" Background="Transparent">
                      <Grid Padding="{TemplateBinding Padding}" ColumnSpacing="10">
                        <Grid.ColumnDefinitions><ColumnDefinition Width="18" /><ColumnDefinition Width="*" /></Grid.ColumnDefinitions>
                        <Border Width="18" Height="18" VerticalAlignment="Top" CornerRadius="9" BorderThickness="1" BorderBrush="{StaticResource SecondaryTextBrush}">
                          <Ellipse x:Name="Dot" Width="8" Height="8" Fill="{StaticResource PrimaryTextBrush}" Opacity="0" />
                        </Border>
                        <ContentPresenter Grid.Column="1" Content="{TemplateBinding Content}" HorizontalContentAlignment="Stretch" />
                      </Grid>
                    </Border>
                  </Grid>
                </ControlTemplate>
                """);
            card.Checked += (_, _) => _chartPreset.SelectedItem = _chartPreset.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, key));
            AutomationProperties.SetName(card, label + " " + hint); _syncedChartCards.Children.Add(card);
        }
        LayoutSyncedChartCards();
        _syncedChartUi.Children.Add(Field("web.collections.fields.chart", "Chart", _syncedChartCards));
        if (preset != null)
        {
            var media = (_chartMedia.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var allowed = _chartMedia.Items.OfType<ComboBoxItem>().Select(item => item.Tag?.ToString()).ToHashSet();
            var choices = new WrapPanel { HorizontalSpacing = 32, VerticalSpacing = 12 };
            var movies = CollectionText.Get("web.collections.tmdbSources.mediaMovies", "Movies"); var shows = CollectionText.Get("web.collections.tmdbSources.mediaTv", "TV shows");
            choices.Children.Add(Field("web.collections.fields.show", "Show", SyncedChartSegments("media", preset == "trending" ? [("movie", movies), ("tv", shows), ("all", CollectionText.Get("web.collections.tmdbSources.mediaBoth", "Both"))] : [("movie", movies), ("tv", shows)], media, allowed,
                key => _chartMedia.SelectedItem = _chartMedia.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, key)))));
            if (preset == "trending") choices.Children.Add(Field("web.collections.fields.trendingOver", "Trending over", SyncedChartSegments("window", [("day", CollectionText.Get("web.collections.tmdbSources.windowDay", "Today")), ("week", CollectionText.Get("web.collections.tmdbSources.windowWeek", "This week"))], (_chartWindow.SelectedItem as ComboBoxItem)?.Tag?.ToString(), null,
                key => _chartWindow.SelectedItem = _chartWindow.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, key)))));
            _syncedChartUi.Children.Add(choices);
            if (allowed.Count == 1) _syncedChartUi.Children.Add(new TextBlock { Text = CollectionText.Get("web.collections.tmdbSources.chartLockReason", "TMDB has this chart for " + (allowed.Contains("movie") ? "movies" : "TV shows") + " only", ("kind", allowed.Contains("movie") ? "movie" : "tv")), FontSize = 12.5, Foreground = CurrentBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        }
        _syncedChartUi.Children.Add(new TextBlock { Text = CollectionText.Get("web.collections.copy.chartRulesNote", "“Trending over” and “Both” appear only for Trending."), FontSize = 12.5, Foreground = CurrentBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
    }

    private Border SyncedChartSegments(string group, (string Key, string Label)[] choices, string? selected, HashSet<string?>? allowed, Action<string> changed)
    {
        var row = new WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 4 };
        foreach (var (key, label) in choices)
        {
            var choice = SyncedSourceTab(label, key == selected); choice.Height = 36; choice.GroupName = "synced-chart-" + group + "-" + GetHashCode(); choice.IsEnabled = allowed == null || allowed.Contains(key);
            choice.Checked += (_, _) => changed(key); row.Children.Add(choice);
        }
        return new Border { Child = row, Padding = new(4), CornerRadius = new(12), Background = CurrentBrush("SurfaceBrush") };
    }

    private void LayoutSyncedChartCards()
    {
        var columns = ActualWidth < 640 ? 2 : 4;
        _syncedChartCards.ColumnDefinitions.Clear(); _syncedChartCards.RowDefinitions.Clear();
        for (var n = 0; n < columns; n++) _syncedChartCards.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var n = 0; n < (int)Math.Ceiling((double)_syncedChartCards.Children.Count / columns); n++) _syncedChartCards.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var n = 0; n < _syncedChartCards.Children.Count; n++) { Grid.SetColumn((FrameworkElement)_syncedChartCards.Children[n], n % columns); Grid.SetRow((FrameworkElement)_syncedChartCards.Children[n], n / columns); }
    }
}
