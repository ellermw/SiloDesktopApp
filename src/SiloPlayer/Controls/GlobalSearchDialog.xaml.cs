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
using SiloPlayer.Core.Services;
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
    private readonly PeopleApi _peopleApi;
    private readonly AuthService _auth;
    private readonly SiloApiClient _apiClient;
    private readonly ApiRequestContext _dialogContext;
    private bool _requestDiscoveryEnabled;
    private string _mediaScope = "video";
    private readonly Task _scopeLoadTask;
    private DispatcherTimer? _debounceTimer;
    private CancellationTokenSource? _searchCts;
    private readonly List<MediaItem> _results = [];
    private readonly List<RequestMediaResult> _requestResults = [];
    private readonly List<Person> _peopleResults = [];
    private readonly SearchSelectionState _selection = new();
    private string _renderedQuery = "";
    private bool _hasMore;
    private bool _searchFailed;
    private bool _peopleFirst;
    private bool _peoplePending;
    private bool _requestsPending;
    private bool _optionalSearchFailed;

    public GlobalSearchDialog()
    {
        _catalogApi = App.Services.GetRequiredService<CatalogApi>();
        _settingsApi = App.Services.GetRequiredService<SettingsApi>();
        _requestsApi = App.Services.GetRequiredService<RequestsApi>();
        _peopleApi = App.Services.GetRequiredService<PeopleApi>();
        _auth = App.Services.GetRequiredService<AuthService>();
        _apiClient = App.Services.GetRequiredService<SiloApiClient>();
        _dialogContext = _apiClient.CaptureContext();
        this.InitializeComponent();
        // TextBox consumes arrow navigation before ordinary routed handlers.
        SearchBox.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(SearchBox_KeyDown), true);
        _scopeLoadTask = LoadScopeAsync();
        CornerRadius = new CornerRadius(8);
        Resources["ContentDialogPadding"] = new Thickness(0);
        Resources["ContentDialogMinHeight"] = 0d;
        Resources["ContentDialogMinWidth"] = 0d;
        this.Opened += (_, _) => { UpdateDialogGeometry(); if (XamlRoot != null) XamlRoot.Changed += SearchRoot_Changed; SearchBox.Focus(FocusState.Programmatic); };
        this.Closed += OnClosed;
    }

    private void SearchRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateDialogGeometry();
    private void UpdateDialogGeometry()
    {
        if (XamlRoot == null) return;
        var viewport = XamlRoot.Size;
        Resources["ContentDialogMaxWidth"] = 512d;
        Resources["ContentDialogMaxHeight"] = Math.Max(72, Math.Min(512, viewport.Height - 96));
        var outerWidth = Math.Max(120, Math.Min(512, viewport.Width - 32));
        SearchSurface.Width = outerWidth;
        SearchSurface.MaxHeight = Math.Max(72, Math.Min(512, viewport.Height - 96));
        ResultsScroll.MaxHeight = Math.Max(40, Math.Min(352, viewport.Height * 0.55));
        Margin = new Thickness(0);
        // The default template centers BackgroundElement independently of the
        // dialog's alignment. Place that actual frame at the WebUI's20% top.
        if (GetTemplateChild("BackgroundElement") is Border frame)
        {
            SearchSurface.Width = Math.Max(0, outerWidth - frame.BorderThickness.Left - frame.BorderThickness.Right);
            frame.VerticalAlignment = VerticalAlignment.Top;
            frame.Margin = new Thickness(0, viewport.Height * .20, 0, 0);
        }
        EscapeHint.Visibility = viewport.Width >= 640 && string.IsNullOrEmpty(SearchBox.Text.Trim()) ? Visibility.Visible : Visibility.Collapsed;
        SearchFooter.Visibility = !string.IsNullOrEmpty(SearchBox.Text.Trim()) && viewport.Width >= 640 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadScopeAsync()
    {
        await Task.WhenAll(LoadMediaScopeAsync(), LoadRequestCapabilityAsync());
    }

    private async Task LoadMediaScopeAsync()
    {
        try
        {
            var value = (await _settingsApi.GetSettingAsync("search.media_scope")).Value;
            if (_apiClient.IsCurrentContext(_dialogContext) && value is ("all" or "video" or "audiobook")) _mediaScope = value;
        }
        catch { }
    }

    private async Task LoadRequestCapabilityAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_auth.SelectedProfileId)) return;
            var status = await _requestsApi.GetStatusAsync();
            if (!_apiClient.IsCurrentContext(_dialogContext)) return;
            _requestDiscoveryEnabled = status.RequestsEnabled && _auth.SelectedProfileId == _dialogContext.ProfileId;
        }
        catch { _requestDiscoveryEnabled = false; }
        if (_apiClient.IsCurrentContext(_dialogContext))
            SearchBox.PlaceholderText = _requestDiscoveryEnabled
                ? "Search library or find titles to request..."
                : "Search library...";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _selection.ClearSelection();
        SyncSelectionHighlight();
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
            _peopleResults.Clear();
            _hasMore = false;
            _searchFailed = false;
            Render();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _requestResults.Clear();
        _peopleResults.Clear();
        _selection.Replace([]);
        _peoplePending = true;
        _requestsPending = true;
        _optionalSearchFailed = false;

        LoadingPanel.Visibility = Visibility.Visible;
        ResultsScroll.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;
        ResultsDivider.Visibility = Visibility.Visible;

        try
        {
            await _scopeLoadTask;
            if (!_apiClient.IsCurrentContext(_dialogContext)) return;
            var catalogTask = _catalogApi.SearchAsync(query, PreviewLimit, _mediaScope, cts.Token);
            var requestsTask = _requestDiscoveryEnabled && query.Length > 1 ? SearchRequestsAsync(query, cts.Token) : Task.FromResult(new List<RequestMediaResult>());
            var peopleTask = _peopleApi.SearchScopedAsync(query, _mediaScope == "all" ? null : _mediaScope, 4, cts.Token);
            var response = await catalogTask;
            if (!IsCurrentSearchOwner(cts, query)) return;

            _results.Clear();
            _results.AddRange(response.Items);
            _hasMore = response.HasMore;
            _searchFailed = false;
            Render();
            _ = PublishRequestResultsAsync(requestsTask, query, cts);
            _ = PublishPeopleResultsAsync(peopleTask, query, cts);
        }
        catch (OperationCanceledException) { /* superseded by a newer query */ }
        catch
        {
            if (!IsCurrentSearchOwner(cts, query)) return;
            _results.Clear();
            _requestResults.Clear();
            _peopleResults.Clear();
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
            if (IsCurrentSearchOwner(owner, query)) _optionalSearchFailed = true;
        }
        finally
        {
            if (IsCurrentSearchOwner(owner, query)) { _requestsPending = false; Render(); }
        }
    }

    private async Task PublishPeopleResultsAsync(Task<List<Person>> task, string query, CancellationTokenSource owner)
    {
        try
        {
            var people = await task;
            if (!IsCurrentSearchOwner(owner, query)) return;
            _peopleResults.Clear(); _peopleResults.AddRange(people); Render();
        }
        catch (OperationCanceledException) { }
        catch { if (IsCurrentSearchOwner(owner, query)) _optionalSearchFailed = true; }
        finally
        {
            if (IsCurrentSearchOwner(owner, query)) { _peoplePending = false; Render(); }
        }
    }

    private bool IsCurrentSearchOwner(CancellationTokenSource owner, string query) =>
        !owner.IsCancellationRequested
         && _apiClient.IsCurrentContext(_dialogContext)
        && ReferenceEquals(_searchCts, owner)
        && string.Equals(SearchBox.Text.Trim(), query, StringComparison.Ordinal);

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        if (XamlRoot != null) XamlRoot.Changed -= SearchRoot_Changed;
        _debounceTimer?.Stop();
        var owner = Interlocked.Exchange(ref _searchCts, null);
        if (owner != null)
        {
            owner.Cancel();
            owner.Dispose();
        }

        _results.Clear();
        _requestResults.Clear();
        _peopleResults.Clear();
        _selection.Replace([]);
        _hasMore = false;
        _searchFailed = false;
        SearchBox.Text = string.Empty;
        _debounceTimer?.Stop();
    }

    private async Task<List<RequestMediaResult>> SearchRequestsAsync(string query, CancellationToken ct)
    {
        if (!_requestDiscoveryEnabled || !_apiClient.IsCurrentContext(_dialogContext)) return [];
        return (await _requestsApi.SearchAsync("all", query, 1, ct)).Results
            .Where(item => !string.Equals(item.Availability, "available", StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToList();
    }

    private void Render()
    {
        EscapeHint.Visibility = XamlRoot?.Size.Width >= 640 && string.IsNullOrEmpty(SearchBox.Text.Trim()) ? Visibility.Visible : Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;

        var query = SearchBox.Text.Trim();
        _renderedQuery = query;
        _peopleFirst = _peopleResults.Any(person => person.Name.Trim().Equals(query, StringComparison.OrdinalIgnoreCase));
        var catalogKeys = _results.Select(item => "catalog:" + item.ContentId);
        var peopleKeys = _peopleResults.Select(person => "person:" + person.Id);
        _selection.Replace((_peopleFirst ? peopleKeys.Concat(catalogKeys) : catalogKeys.Concat(peopleKeys))
            .Concat(_requestResults.Select(item => $"request:{item.MediaType}:{item.TmdbId}")));
        if (string.IsNullOrEmpty(query))
        {
            ResultsScroll.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Collapsed;
            ResultsDivider.Visibility = Visibility.Collapsed;
            SearchFooter.Visibility = Visibility.Collapsed;
            return;
        }

        ResultsDivider.Visibility = Visibility.Visible;
        SearchFooter.Visibility = XamlRoot?.Size.Width >= 640 ? Visibility.Visible : Visibility.Collapsed;
        SearchFooterText.Text = "↑ ↓  Navigate     Esc  Close";
        if (_optionalSearchFailed) SearchFooterText.Text = "Some search sources could not be loaded. Press Enter for the full search page.";
        ResultsPanel.Children.Clear();

        if (_searchFailed)
        {
            ResultsScroll.Visibility = Visibility.Collapsed;
            EmptyText.Text = "Could not load results. Press Enter to open the search page.";
            EmptyText.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        if (_results.Count == 0 && _peopleResults.Count == 0 && _requestResults.Count == 0)
        {
            ResultsScroll.Visibility = Visibility.Collapsed;
            EmptyText.Text = _peoplePending || _requestsPending ? "Searching…"
                : _optionalSearchFailed ? "Some search sources could not be loaded. Press Enter to retry on the search page." : "No matches";
            EmptyText.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        ResultsScroll.Visibility = Visibility.Visible;

        void AddCatalogRows()
        {
            if (_results.Count > 0 && _peopleResults.Count > 0)
            {
                if (_peopleFirst) AddGroupDivider();
                AddGroupHeading("TITLES");
            }
            for (int i = 0; i < _results.Count; i++)
                ResultsPanel.Children.Add(BuildResultRow(_results[i], (_peopleFirst ? _peopleResults.Count : 0) + i));
        }
        void AddGroupHeading(string text) => ResultsPanel.Children.Add(new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.Medium, LineHeight = 40d / 3, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, CharacterSpacing = 100, Margin = new Thickness(12, 8, 12, 4), Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        void AddGroupDivider() => ResultsPanel.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = new SolidColorBrush(Colors.White) { Opacity = .05 } });
        if (!_peopleFirst) AddCatalogRows();
        if (_peopleResults.Count > 0)
        {
            if (!_peopleFirst && _results.Count > 0) AddGroupDivider();
            AddGroupHeading("PEOPLE");
            for (var i = 0; i < _peopleResults.Count; i++)
            {
                var person = _peopleResults[i];
                ResultsPanel.Children.Add(BuildResultRow(new MediaItem { Title = person.Name, PosterUrl = person.PhotoUrl, Type = "person" }, (_peopleFirst ? 0 : _results.Count) + i));
            }
        }
        if (_peopleFirst) AddCatalogRows();
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
            for (var i = 0; i < _requestResults.Count; i++)
                ResultsPanel.Children.Add(BuildRequestRow(_requestResults[i], _results.Count + _peopleResults.Count + i));
        }
        SyncSelectionHighlight();
    }

    private FrameworkElement BuildResultRow(MediaItem item, int index)
    {
        var surface = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
        };
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var thumb = new Border
        {
            Width = 40, Height = item.Type == "person" ? 40 : 56,
            CornerRadius = new CornerRadius(item.Type == "person" ? 20 : 4),
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
                Text = item.Type == "person" ? string.Join("", (item.Title ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant() : (item.Title ?? string.Empty)[..Math.Min(item.Title?.Length ?? 0, 24)],
                FontSize = item.Type == "person" ? 12 : 10,
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

        var textStack = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = item.Title ?? "",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
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
            FontSize = 12,
            LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
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
        if (item.Type is not ("movie" or "episode")) return row;
        var container = new Grid { Tag = index };
        container.Children.Add(row);
        var play = new Button { Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12,0,0,0), Padding = new Thickness(0), CornerRadius = new CornerRadius(20), Content = new FontIcon { Glyph = "\uE768", FontSize = 16 }, Opacity = 0 };
        AutomationProperties.SetName(play, $"Play {item.Title}");
        play.Click += (_, _) => { Hide(); _ = App.Services.GetRequiredService<PlayerService>().PlayAsync(item.ContentId); };
        container.PointerEntered += (_, _) => play.Opacity = 1;
        container.PointerExited += (_, _) => { if (play.FocusState == FocusState.Unfocused) play.Opacity = 0; };
        play.GotFocus += (_, _) => play.Opacity = 1;
        play.LostFocus += (_, _) => play.Opacity = 0;
        container.Children.Add(play); return container;
    }

    private Button BuildRequestRow(RequestMediaResult item, int index)
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
            Tag = index,
            Opacity = requestable ? 1 : 0.7,
        };
        AutomationProperties.SetName(row,
            $"Open request details for {item.Title}, {string.Join(", ", metadata)}");
        row.Click += (_, _) => PickResult(index);
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
        "person" => "Person",
        _ => type ?? "",
    };

    private void SyncSelectionHighlight()
    {
        FrameworkElement? selectedElement = null;
        foreach (var child in ResultsPanel.Children)
        {
            var candidate = child is Grid host ? host.Children.OfType<Button>().FirstOrDefault() : child;
            if (candidate is Button { Tag: int resultIndex, Content: Border surface } button)
            {
                surface.Background = resultIndex == _selection.Index
                    ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                    : new SolidColorBrush(Colors.Transparent);
                if (resultIndex == _selection.Index)
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
                if (_renderedQuery == SearchBox.Text.Trim())
                {
                    _selection.Move(1);
                    SyncSelectionHighlight();
                    e.Handled = true;
                }
                break;
            case VirtualKey.Up:
                if (_renderedQuery == SearchBox.Text.Trim())
                {
                    _selection.Move(-1);
                    SyncSelectionHighlight();
                    e.Handled = true;
                }
                break;
            case VirtualKey.Enter:
                if (_selection.Index >= 0 && _renderedQuery == SearchBox.Text.Trim())
                {
                    PickResult(_selection.Index);
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
        if (_renderedQuery != SearchBox.Text.Trim() || index < 0 || index >= _results.Count + _peopleResults.Count + _requestResults.Count) return;
        this.Hide();
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (_peopleFirst && index < _peopleResults.Count)
            nav.Navigate(typeof(PersonDetailPage), _peopleResults[index].Id);
        else if (_peopleFirst && index < _peopleResults.Count + _results.Count)
            nav.Navigate(typeof(ItemDetailPage), _results[index - _peopleResults.Count].ContentId);
        else if (!_peopleFirst && index < _results.Count) nav.Navigate(typeof(ItemDetailPage), _results[index].ContentId);
        else if (!_peopleFirst && index < _results.Count + _peopleResults.Count)
            nav.Navigate(typeof(PersonDetailPage), _peopleResults[index - _results.Count].Id);
        else
        {
            var item = _requestResults[index - _results.Count - _peopleResults.Count];
            nav.Navigate<RequestDetailPage>(new RequestDetailNavigation(item.MediaType, item.TmdbId));
        }
    }
}
