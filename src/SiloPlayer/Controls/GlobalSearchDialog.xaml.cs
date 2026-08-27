using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Converters;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;
using Windows.System;
using Windows.UI;

namespace SiloPlayer.Controls;

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
    private static readonly UrlToImageSourceConverter RemoteImageConverter = new();

    private readonly CatalogApi _catalogApi;
    private readonly SettingsApi _settingsApi;
    private readonly RequestsApi _requestsApi;
    private string _mediaScope = "video";
    private readonly Task _scopeLoadTask;
    private DispatcherTimer? _debounceTimer;
    private CancellationTokenSource? _searchCts;
    private readonly List<MediaItem> _results = [];
    private readonly List<RequestMediaResult> _requestResults = [];
    private int _selectedIndex = -1;
    private bool _hasMore;
    private bool _searchFailed;

    public GlobalSearchDialog()
    {
        _catalogApi = App.Services.GetRequiredService<CatalogApi>();
        _settingsApi = App.Services.GetRequiredService<SettingsApi>();
        _requestsApi = App.Services.GetRequiredService<RequestsApi>();
        _scopeLoadTask = LoadScopeAsync();
        this.InitializeComponent();
        this.Opened += (_, _) => SearchBox.Focus(FocusState.Programmatic);
        this.Closed += OnClosed;
    }

    private async Task LoadScopeAsync()
    {
        try
        {
            var value = (await _settingsApi.GetSettingAsync("search.media_scope")).Value;
            if (value is "all" or "video" or "audiobook") _mediaScope = value;
        }
        catch { }
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
        var previousOwner = Interlocked.Exchange(ref _searchCts, null);
        if (previousOwner != null)
        {
            previousOwner.Cancel();
            previousOwner.Dispose();
        }

        if (string.IsNullOrEmpty(query))
        {
            _results.Clear();
            _requestResults.Clear();
            _hasMore = false;
            _searchFailed = false;
            Render();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _requestResults.Clear();

        LoadingPanel.Visibility = Visibility.Visible;
        ResultsScroll.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;
        ResultsDivider.Visibility = Visibility.Visible;

        try
        {
            await _scopeLoadTask;
            var catalogTask = _catalogApi.SearchAsync(query, PreviewLimit, _mediaScope, cts.Token);
            var requestsTask = _mediaScope is "all" or "video" ? SearchRequestsAsync(query, cts.Token) : Task.FromResult(new List<RequestMediaResult>());
            var response = await catalogTask;
            if (!IsCurrentSearchOwner(cts, query)) return;

            _results.Clear();
            _results.AddRange(response.Items);
            _hasMore = response.HasMore;
            _searchFailed = false;
            Render();
            _ = PublishRequestResultsAsync(requestsTask, query, cts);
        }
        catch (OperationCanceledException) { /* superseded by a newer query */ }
        catch
        {
            _results.Clear();
            _requestResults.Clear();
            _hasMore = false;
            _searchFailed = true;
            Render();
        }
    }

    private async Task PublishRequestResultsAsync(
        Task<List<RequestMediaResult>> requestsTask,
        string query,
        CancellationTokenSource owner)
    {
        try
        {
            var results = await requestsTask;
            if (!IsCurrentSearchOwner(owner, query)) return;

            _requestResults.Clear();
            _requestResults.AddRange(results);
            Render();
        }
        catch (OperationCanceledException)
        {
            // Closing the dialog or typing a newer query owns the surface now.
        }
        catch
        {
            // Request discovery is optional and cannot fail local search.
        }
    }

    private bool IsCurrentSearchOwner(CancellationTokenSource owner, string query) =>
        !owner.IsCancellationRequested
        && ReferenceEquals(_searchCts, owner)
        && string.Equals(SearchBox.Text.Trim(), query, StringComparison.Ordinal);

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _debounceTimer?.Stop();
        var owner = Interlocked.Exchange(ref _searchCts, null);
        if (owner != null)
        {
            owner.Cancel();
            owner.Dispose();
        }

        _results.Clear();
        _requestResults.Clear();
        _selectedIndex = -1;
        _hasMore = false;
        _searchFailed = false;
        SearchBox.Text = string.Empty;
        _debounceTimer?.Stop();
    }

    private async Task<List<RequestMediaResult>> SearchRequestsAsync(string query, CancellationToken ct)
    {
        try
        {
            var status = await _requestsApi.GetStatusAsync(ct);
            if (!status.RequestsEnabled) return [];
            return (await _requestsApi.SearchAsync("all", query, 1, ct)).Results
                .Where(item => !string.Equals(item.Availability, "available", StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToList();
        }
        catch { return []; }
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
            SearchFooter.Visibility = Visibility.Collapsed;
            return;
        }

        ResultsDivider.Visibility = Visibility.Visible;
        SearchFooter.Visibility = Visibility.Visible;
        SearchFooterText.Text = _hasMore
            ? "Showing top results. Press Enter for all results."
            : "Press Enter to open the full search page.";
        ResultsPanel.Children.Clear();

        if (_searchFailed)
        {
            ResultsScroll.Visibility = Visibility.Collapsed;
            EmptyText.Text = "Could not load results. Press Enter to open the search page.";
            EmptyText.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        if (_results.Count == 0 && _requestResults.Count == 0)
        {
            ResultsScroll.Visibility = Visibility.Collapsed;
            EmptyText.Text = "No matches";
            EmptyText.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        ResultsScroll.Visibility = Visibility.Visible;

        for (int i = 0; i < _results.Count; i++)
        {
            ResultsPanel.Children.Add(BuildResultRow(_results[i], i));
        }
        if (_requestResults.Count > 0)
        {
            ResultsPanel.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 4, 0, 0),
                Background = (Brush)Application.Current.Resources["BorderBrush"],
            });
            if (_results.Count > 0)
            {
                var header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7,
                    Margin = new Thickness(8, 10, 8, 4),
                };
                header.Children.Add(new TextBlock
                {
                    Text = "REQUEST TO ADD",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    CharacterSpacing = 100,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                header.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 0, 6, 0),
                    Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                    Child = new TextBlock
                    {
                        Text = _requestResults.Count.ToString(),
                        FontSize = 10,
                        Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                    },
                });
                ResultsPanel.Children.Add(header);
            }
            else
            {
                ResultsPanel.Children.Add(new TextBlock
                {
                    Text = "Not in your library, but you can request:",
                    Margin = new Thickness(8, 10, 8, 4),
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["WarningBrush"],
                });
            }
            foreach (var item in _requestResults) ResultsPanel.Children.Add(BuildRequestRow(item));
        }
        SyncSelectionHighlight();
    }

    private Button BuildResultRow(MediaItem item, int index)
    {
        var surface = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var thumb = new Border
        {
            Width = 40, Height = 56,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrWhiteSpace(item.PosterUrl))
        {
            thumb.Child = new Image
            {
                Source = (ImageSource)RemoteImageConverter.Convert(
                    item.PosterUrl,
                    typeof(ImageSource),
                    null!,
                    string.Empty),
                Stretch = Stretch.UniformToFill
            };
        }
        else
        {
            thumb.Child = new TextBlock
            {
                Text = (item.Title ?? string.Empty)[..Math.Min(item.Title?.Length ?? 0, 24)],
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 3,
                Margin = new Thickness(3),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            };
        }
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

        surface.Child = grid;
        var row = new Button
        {
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = index,
            Content = surface,
        };
        AutomationProperties.SetName(row,
            $"Open {item.Title}, {string.Join(", ", subtitle)}");
        row.Click += (_, _) => PickResult(index);
        return row;
    }

    private Button BuildRequestRow(RequestMediaResult item)
    {
        var poster = new Border { Width = 40, Height = 56, CornerRadius = new CornerRadius(4), Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"] };
        if (!string.IsNullOrWhiteSpace(item.PosterUrl))
        {
            poster.Child = new Image
            {
                Source = (ImageSource)RemoteImageConverter.Convert(
                    item.PosterUrl,
                    typeof(ImageSource),
                    null!,
                    string.Empty),
                Stretch = Stretch.UniformToFill,
            };
        }
        else
        {
            poster.Child = new FontIcon
            {
                Glyph = "\uE7F4",
                FontSize = 16,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var metadata = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.YearText)) metadata.Add(item.YearText);
        metadata.Add(TypeLabel(item.MediaType));
        text.Children.Add(new TextBlock { Text = string.Join(" \u00B7 ", metadata), FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1); grid.Children.Add(poster); grid.Children.Add(text);
        var requestable = item.Request.Requestable;
        var badge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2, 8, 2),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources[requestable ? "WarningBrush" : "BorderBrush"],
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = requestable ? "REQUEST" : item.RequestLabel.ToUpperInvariant(),
                FontSize = 9,
                FontWeight = requestable ? FontWeights.SemiBold : FontWeights.Medium,
                CharacterSpacing = 50,
                Foreground = (Brush)Application.Current.Resources[requestable ? "WarningBrush" : "SecondaryTextBrush"],
            },
        };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);
        var surface = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Child = grid,
        };
        var row = new Button
        {
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = surface,
            Opacity = requestable ? 1 : 0.7,
        };
        AutomationProperties.SetName(row,
            $"Open request details for {item.Title}, {string.Join(", ", metadata)}");
        row.Click += (_, _) =>
        {
            Hide();
            App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(item.MediaType, item.TmdbId));
        };
        return row;
    }

    private static string TypeLabel(string? type) => type?.ToLowerInvariant() switch
    {
        "movie" => "Movie",
        "series" => "Series",
        "season" => "Season",
        "episode" => "Episode",
        "audiobook" => "Audiobook",
        "ebook" => "Ebook",
        "manga" => "Manga",
        _ => type ?? "",
    };

    private void SyncSelectionHighlight()
    {
        FrameworkElement? selectedElement = null;
        foreach (var child in ResultsPanel.Children)
        {
            if (child is Button { Tag: int resultIndex, Content: Border surface } button)
            {
                surface.Background = resultIndex == _selectedIndex
                    ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                    : new SolidColorBrush(Colors.Transparent);
                if (resultIndex == _selectedIndex)
                {
                    selectedElement = button;
                }
            }
        }

        // Scroll selected into view.
        if (selectedElement is not null)
        {
            selectedElement.StartBringIntoView(new Microsoft.UI.Xaml.BringIntoViewOptions
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
