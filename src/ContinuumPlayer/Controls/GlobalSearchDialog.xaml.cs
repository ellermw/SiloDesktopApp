using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Services;
using ContinuumPlayer.Views;
using Windows.System;
using Windows.UI;

namespace ContinuumPlayer.Controls;

/// <summary>
/// Command-palette style global search dialog — webui parity with
/// <c>web/src/components/GlobalSearch.tsx</c>. Opens via Ctrl+K and shows a
/// debounced live preview (up to 8 results) from /catalog?q=. Arrow Up/Down
/// cycles results, Enter navigates to the selected item, Ctrl+Enter / empty
/// Enter navigates to the full search page.
/// </summary>
public sealed partial class GlobalSearchDialog : ContentDialog
{
    private const int PreviewLimit = 8;
    private const int DebounceMs = 200;

    private readonly CatalogApi _catalogApi;
    private DispatcherTimer? _debounceTimer;
    private CancellationTokenSource? _searchCts;
    private readonly List<MediaItem> _results = [];
    private int _selectedIndex = -1;

    public GlobalSearchDialog()
    {
        _catalogApi = App.Services.GetRequiredService<CatalogApi>();
        this.InitializeComponent();
        this.Opened += (_, _) => SearchBox.Focus(FocusState.Programmatic);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _selectedIndex = -1;
        ScheduleSearch();
    }

    private void ScheduleSearch()
    {
        if (_debounceTimer == null)
        {
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
            _debounceTimer.Tick += async (_, _) =>
            {
                _debounceTimer!.Stop();
                await RunSearchAsync();
            };
        }
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task RunSearchAsync()
    {
        var query = SearchBox.Text.Trim();
        _searchCts?.Cancel();

        if (string.IsNullOrEmpty(query))
        {
            _results.Clear();
            Render();
            return;
        }

        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;

        LoadingPanel.Visibility = Visibility.Visible;
        ResultsScroll.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;
        ResultsDivider.Visibility = Visibility.Visible;

        try
        {
            var response = await _catalogApi.SearchAsync(query, PreviewLimit, cts.Token);
            if (cts.IsCancellationRequested) return;

            _results.Clear();
            _results.AddRange(response.Items);
            Render();
        }
        catch (OperationCanceledException) { /* superseded by a newer query */ }
        catch
        {
            _results.Clear();
            Render();
        }
    }

    private void Render()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;

        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            ResultsScroll.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Collapsed;
            ResultsDivider.Visibility = Visibility.Collapsed;
            return;
        }

        ResultsDivider.Visibility = Visibility.Visible;
        ResultsPanel.Children.Clear();

        if (_results.Count == 0)
        {
            ResultsScroll.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        ResultsScroll.Visibility = Visibility.Visible;

        for (int i = 0; i < _results.Count; i++)
        {
            ResultsPanel.Children.Add(BuildResultRow(_results[i], i));
        }
        SyncSelectionHighlight();
    }

    private Border BuildResultRow(MediaItem item, int index)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Tag = index,
        };
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Poster thumb (placeholder block; full image loading would add latency).
        var thumb = new Border
        {
            Width = 36, Height = 50,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(thumb, 0);
        grid.Children.Add(thumb);

        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = item.Title ?? "",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });
        var subtitle = new List<string>();
        if (item.Year > 0) subtitle.Add(item.Year.ToString());
        subtitle.Add(TypeLabel(item.Type));
        textStack.Children.Add(new TextBlock
        {
            Text = string.Join(" \u00B7 ", subtitle),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        row.Child = grid;
        row.Tapped += (_, _) => PickResult(index);
        return row;
    }

    private static string TypeLabel(string? type) => type?.ToLowerInvariant() switch
    {
        "movie" => "Movie",
        "series" => "Series",
        "season" => "Season",
        "episode" => "Episode",
        _ => type ?? "",
    };

    private void SyncSelectionHighlight()
    {
        for (int i = 0; i < ResultsPanel.Children.Count; i++)
        {
            if (ResultsPanel.Children[i] is Border b)
            {
                b.Background = i == _selectedIndex
                    ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                    : new SolidColorBrush(Colors.Transparent);
            }
        }

        // Scroll selected into view.
        if (_selectedIndex >= 0 && _selectedIndex < ResultsPanel.Children.Count
            && ResultsPanel.Children[_selectedIndex] is FrameworkElement el)
        {
            el.StartBringIntoView(new Microsoft.UI.Xaml.BringIntoViewOptions
            {
                VerticalAlignmentRatio = 0.5,
                AnimationDesired = false,
            });
        }
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                if (_results.Count > 0)
                {
                    _selectedIndex = _selectedIndex < _results.Count - 1 ? _selectedIndex + 1 : 0;
                    SyncSelectionHighlight();
                    e.Handled = true;
                }
                break;
            case VirtualKey.Up:
                if (_results.Count > 0)
                {
                    _selectedIndex = _selectedIndex <= 0 ? _results.Count - 1 : _selectedIndex - 1;
                    SyncSelectionHighlight();
                    e.Handled = true;
                }
                break;
            case VirtualKey.Enter:
                if (_selectedIndex >= 0 && _selectedIndex < _results.Count)
                {
                    PickResult(_selectedIndex);
                }
                else if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    // No selection: navigate to full search page with query.
                    this.Hide();
                    var nav = App.Services.GetRequiredService<NavigationService>();
                    nav.Navigate(typeof(SearchPage), SearchBox.Text.Trim());
                }
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                this.Hide();
                e.Handled = true;
                break;
        }
    }

    private void PickResult(int index)
    {
        if (index < 0 || index >= _results.Count) return;
        var item = _results[index];
        this.Hide();
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate(typeof(ItemDetailPage), item.ContentId);
    }
}
