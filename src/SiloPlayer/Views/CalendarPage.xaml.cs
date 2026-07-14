using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

/// <summary>
/// Desktop mirror of <c>web/src/pages/Calendar.tsx</c>: a Mon–Sun week view
/// of upcoming releases and new episodes, with filter (following/trending/everything),
/// optional library scope, and prev/today/next navigation. Each day is rendered
/// as a horizontal scroll row of event cards, closely matching the web DayGroup.
/// </summary>
public sealed partial class CalendarPage : Page
{
    public CalendarViewModel ViewModel { get; }

    private bool _eventsAttached;
    private bool _suppressLibraryEvent;
    private string? _selectedDay;
    private readonly Dictionary<string, FrameworkElement> _dayGroups = new(StringComparer.Ordinal);

    public CalendarPage()
    {
        ViewModel = App.Services.GetRequiredService<CalendarViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.Days.Count == 0 && ViewModel.ErrorMessage == null)
                await ViewModel.LoadCommand.ExecuteAsync(null);

            if (!_eventsAttached)
            {
                _eventsAttached = true;
                ViewModel.Days.CollectionChanged += OnDaysChanged;
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;
                ViewModel.Libraries.CollectionChanged += OnLibrariesChanged;
            }

            RebuildAll();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_eventsAttached)
        {
            ViewModel.Days.CollectionChanged -= OnDaysChanged;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.Libraries.CollectionChanged -= OnLibrariesChanged;
            _eventsAttached = false;
        }
    }

    // ---------- Reactive wiring ----------

    private void OnDaysChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => DispatcherQueue.TryEnqueue(BuildDayRows);

    private void OnLibrariesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => DispatcherQueue.TryEnqueue(BuildLibraryDropdown);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.WeekStart)
            or nameof(ViewModel.Filter)
            or nameof(ViewModel.IsEmpty))
        {
            DispatcherQueue.TryEnqueue(RebuildAll);
        }
    }

    private void RebuildAll()
    {
        BuildWeekStrip();
        BuildFilterPills();
        BuildLibraryDropdown();
        BuildDayRows();
        UpdateEmptyState();
    }

    // ---------- Header: filter pills ----------

    private void BuildFilterPills()
    {
        ApplyPillStyle(FilterFollowingBtn, ViewModel.Filter == "following");
        ApplyPillStyle(FilterTrendingBtn, ViewModel.Filter == "trending");
        ApplyPillStyle(FilterEverythingBtn, ViewModel.Filter == "everything");
    }

    private void ApplyPillStyle(Button btn, bool active)
    {
        var key = active ? "FilterPillActiveButtonStyle" : "FilterPillButtonStyle";
        btn.Style = (Style)Resources[key];
    }

    private async void FilterFollowingBtn_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SetFilterAsync("following");

    private async void FilterTrendingBtn_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SetFilterAsync("trending");

    private async void FilterEverythingBtn_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SetFilterAsync("everything");

    private async void ShowTrending_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SetFilterAsync("trending");

    private async void ShowEverything_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SetFilterAsync("everything");

    // ---------- Header: library dropdown ----------

    private void BuildLibraryDropdown()
    {
        // Web parity: only show the library filter when the user has 2+ libraries.
        if (ViewModel.Libraries.Count <= 1)
        {
            LibraryComboBox.Visibility = Visibility.Collapsed;
            return;
        }

        _suppressLibraryEvent = true;
        try
        {
            LibraryComboBox.Items.Clear();
            LibraryComboBox.Items.Add(new ComboBoxItem { Content = "All Libraries", Tag = null });
            foreach (var lib in ViewModel.Libraries)
                LibraryComboBox.Items.Add(new ComboBoxItem { Content = lib.Name, Tag = lib.Id });

            LibraryComboBox.SelectedIndex = 0;
            if (ViewModel.LibraryId.HasValue)
            {
                for (int i = 1; i < LibraryComboBox.Items.Count; i++)
                {
                    if (LibraryComboBox.Items[i] is ComboBoxItem cbi
                        && cbi.Tag is int id
                        && id == ViewModel.LibraryId.Value)
                    {
                        LibraryComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
        finally
        {
            _suppressLibraryEvent = false;
        }

        LibraryComboBox.Visibility = Visibility.Visible;
    }

    private async void LibraryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLibraryEvent) return;
        if (LibraryComboBox.SelectedItem is not ComboBoxItem item) return;
        int? id = item.Tag is int i ? i : null;
        await ViewModel.SetLibraryAsync(id);
    }

    // ---------- Week strip (7 day cells with event dots) ----------

    private void BuildWeekStrip()
    {
        WeekStripPanel.Children.Clear();
        WeekStripPanel.ColumnDefinitions.Clear();
        for (var i = 0; i < 7; i++)
            WeekStripPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var datesWithEvents = new HashSet<string>();
        foreach (var d in ViewModel.Days) datesWithEvents.Add(d.Date);

        var days = CalendarViewModel.GetWeekDays(ViewModel.WeekStart);
        var activeSelectedDay = _selectedDay != null && days.Contains(_selectedDay) ? _selectedDay : null;
        for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
        {
            var dateStr = days[dayIndex];
            var (label, day) = CalendarViewModel.FormatShortDay(dateStr);
            var today = CalendarViewModel.IsToday(dateStr);
            var hasEvents = datesWithEvents.Contains(dateStr);
            var selected = dateStr == activeSelectedDay;

            var cell = new Border
            {
                Style = (Style)Resources["WeekDayCellBorderStyle"],
            };
            if (selected)
            {
                cell.Background = (Brush)Application.Current.Resources["AccentBrush"];
                cell.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
                cell.BorderThickness = new Thickness(1);
            }
            else if (today)
            {
                // Highlight today with a subtle accent background (web: bg-primary/15 ring)
                cell.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                {
                    Opacity = 1
                };
                cell.Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
                cell.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
                cell.BorderThickness = new Thickness(1);
            }

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 1,
            };

            // Day label (MON/TUE/…) — text-[11px] font-medium uppercase
            stack.Children.Add(new TextBlock
            {
                Text = label.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = selected
                    ? (Brush)Application.Current.Resources["AccentForegroundBrush"]
                    : today ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            // Day number — text-base (16) font-semibold
            stack.Children.Add(new TextBlock
            {
                Text = day.ToString(),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = selected
                    ? (Brush)Application.Current.Resources["AccentForegroundBrush"]
                    : today ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["PrimaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            // Event dot — h-1.5 w-1.5 = 6x6. Always allocates 6px row so cells
            // with/without events have identical heights.
            var dotContainer = new Grid { Height = 6, Width = 6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            if (hasEvents)
            {
                dotContainer.Children.Add(new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = selected
                        ? (Brush)Application.Current.Resources["AccentForegroundBrush"]
                        : today ? (Brush)Application.Current.Resources["AccentBrush"]
                        : (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
            }
            stack.Children.Add(dotContainer);

            cell.Child = stack;
            cell.Tapped += (_, _) => SelectDay(dateStr, hasEvents);
            Grid.SetColumn(cell, dayIndex);
            WeekStripPanel.Children.Add(cell);
        }

        SelectedDayEmptyState.Visibility = activeSelectedDay != null &&
            !datesWithEvents.Contains(activeSelectedDay) && !ViewModel.IsLoading
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (SelectedDayEmptyState.Visibility == Visibility.Visible)
            SelectedDayEmptyText.Text = $"Nothing scheduled for {CalendarViewModel.FormatDayHeading(activeSelectedDay!)}.";
    }

    // ---------- Day rows ----------

    private void BuildDayRows()
    {
        DaysPanel.Children.Clear();
        _dayGroups.Clear();

        foreach (var day in ViewModel.Days)
        {
            if (day.Items.Count == 0) continue;
            var group = BuildDayGroup(day);
            _dayGroups[day.Date] = group;
            DaysPanel.Children.Add(group);
        }

        UpdateEmptyState();
    }

    private void SelectDay(string date, bool hasEvents)
    {
        _selectedDay = date;
        BuildWeekStrip();
        if (!hasEvents || !_dayGroups.TryGetValue(date, out var group)) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ContentScrollViewer.Content is not UIElement content) return;
            var point = group.TransformToVisual(content).TransformPoint(new Point(0, 0));
            ContentScrollViewer.ChangeView(null, Math.Max(0, point.Y - 12), null, false);
        });
    }

    private FrameworkElement BuildDayGroup(CalendarDay day)
    {
        // Web parity (DayGroup.tsx): day heading + horizontally scrolling row of cards.
        var container = new Grid();
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- Heading row: "Monday, April 7th" + optional "Today" pill ---
        var headingGrid = new Grid { Margin = new Thickness(48, 0, 48, 12) };
        headingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var headingText = new TextBlock
        {
            Text = CalendarViewModel.FormatDayHeading(day.Date),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(headingText, 0);
        headingGrid.Children.Add(headingText);

        if (CalendarViewModel.IsToday(day.Date))
        {
            var todayPill = new Border
            {
                Background = (Brush)Application.Current.Resources["AccentBrush"],
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = "TODAY",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    CharacterSpacing = 100,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(todayPill, 1);
            headingGrid.Children.Add(todayPill);
        }

        Grid.SetRow(headingGrid, 0);
        container.Children.Add(headingGrid);

        // --- Horizontal scroll row of event cards ---
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };

        var cardsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Margin = new Thickness(48, 0, 48, 0),
        };

        foreach (var ev in day.Items)
            cardsPanel.Children.Add(BuildEventCard(ev));

        scroller.Content = cardsPanel;
        Grid.SetRow(scroller, 1);
        container.Children.Add(scroller);

        return container;
    }

    private FrameworkElement BuildEventCard(CalendarEvent ev)
    {
        // Mirrors web CalendarEventCard: poster (2:3), title, subtitle (e.g. S1 · E3), optional air time,
        // plus top-left badge pills from ev.Badges.
        const double cardWidth = 160;
        const double posterHeight = 240; // 2:3 aspect

        var root = new Grid { Width = cardWidth, Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(posterHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- Poster panel ---
        var posterPanel = new Grid
        {
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
        };

        var posterImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
        };
        posterPanel.Children.Add(posterImage);

        // Bottom gradient overlay for legibility
        var gradient = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 80,
            IsHitTestVisible = false,
        };
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop { Offset = 0.0, Color = Microsoft.UI.Colors.Transparent });
        brush.GradientStops.Add(new GradientStop { Offset = 1.0, Color = Windows.UI.Color.FromArgb(0x8C, 0x00, 0x00, 0x00) });
        gradient.Background = brush;
        posterPanel.Children.Add(gradient);

        // Badges (top-left) — only the first badge to keep the card clean, matching the web card's pill row size
        if (ev.Badges is { Count: > 0 })
        {
            var badgeStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 8, 0, 0),
            };
            foreach (var badge in ev.Badges)
            {
                badgeStack.Children.Add(BuildBadgePill(badge));
            }
            posterPanel.Children.Add(badgeStack);
        }

        Grid.SetRow(posterPanel, 0);
        root.Children.Add(posterPanel);

        // --- Text block under the poster ---
        var textStack = new StackPanel { Margin = new Thickness(4, 10, 4, 0), Spacing = 2 };

        textStack.Children.Add(new TextBlock
        {
            Text = ev.Title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });

        var subtitle = FormatSubtitle(ev);
        if (!string.IsNullOrEmpty(subtitle))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                CharacterSpacing = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        var airTime = FormatAirTime(ev.AirTime);
        if (!string.IsNullOrEmpty(airTime))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = airTime,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }

        Grid.SetRow(textStack, 1);
        root.Children.Add(textStack);

        // --- Click → item detail (web: /item/{content_id} for movies, /item/{series_id} otherwise) ---
        root.Tapped += (_, _) =>
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            var targetId = ev.Type == "movie"
                ? ev.ContentId
                : (ev.SeriesId ?? ev.ContentId);
            if (!string.IsNullOrEmpty(targetId))
                nav.Navigate<ItemDetailPage>(targetId);
        };

        // Load the poster asynchronously (don't block UI on dozens of simultaneous decodes)
        _ = LoadEventPosterAsync(posterImage, ev);

        return root;
    }

    private Border BuildBadgePill(string badge)
    {
        var label = badge switch
        {
            "series_premiere" => "Series Premiere",
            "season_premiere" => "Season Premiere",
            "finale" => "Finale",
            _ => badge,
        };

        var isPremiere = badge is "series_premiere" or "season_premiere";
        var isFinale = badge == "finale";

        Brush background;
        Brush foreground;

        if (isPremiere)
        {
            background = (Brush)Application.Current.Resources["AccentBrush"];
            foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        }
        else if (isFinale)
        {
            background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x7C, 0x2D, 0x12));
            foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xED, 0xD5));
        }
        else
        {
            background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
            foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        }

        // Fixed-height 20px pill — radius = 10 (exactly half) gives a clean capsule.
        // Using CornerRadius(999) on small borders produces warped ovals in WinUI 3
        // because the radius isn't clamped to geometry half until render time and
        // the antialiased fill goes wonky at extreme values.
        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(10),
            Height = 20,
            Padding = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = label.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 100,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>
    /// Mirrors <c>formatUpcomingSubtitle</c> from
    /// <c>web/src/lib/upcomingEventPresentation.ts</c>.
    /// </summary>
    private static string FormatSubtitle(CalendarEvent ev)
    {
        if ((ev.Type == "episode" || ev.Type == "season_premiere") && ev.SeasonNumber.HasValue)
        {
            if (ev.EpisodeNumber.HasValue)
            {
                var title = !string.IsNullOrEmpty(ev.EpisodeTitle) ? $" - {ev.EpisodeTitle}" : "";
                return $"S{ev.SeasonNumber} \u00B7 E{ev.EpisodeNumber}{title}";
            }
            var extra = !string.IsNullOrEmpty(ev.EpisodeTitle) ? $" \u00B7 {ev.EpisodeTitle}" : "";
            return $"Season {ev.SeasonNumber}{extra}";
        }
        if (ev.Type == "movie") return "Movie";
        return "";
    }

    /// <summary>Mirrors <c>formatUpcomingTime</c> — HH:MM[:SS] → localized h:mm.</summary>
    private static string? FormatAirTime(string? airTime)
    {
        if (string.IsNullOrEmpty(airTime)) return null;
        if (TimeSpan.TryParse(airTime, out var ts))
        {
            var dt = DateTime.Today.Add(ts);
            return dt.ToString("h:mm tt");
        }
        return null;
    }

    private async Task LoadEventPosterAsync(Image target, CalendarEvent ev)
    {
        var url = ev.PosterUrl;
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await Task.Run(async () =>
                await imageService.GetImageAsync(ev.ContentId, "poster", url, httpClient, default));
            if (bytes == null) return;

            var bitmap = new BitmapImage
            {
                DecodePixelWidth = 220,
                DecodePixelType = DecodePixelType.Logical,
            };
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());

            target.Source = bitmap;
            target.Opacity = 1;
        }
        catch { /* ignore — card simply shows the placeholder background */ }
    }

    // ---------- Empty state ----------

    private void UpdateEmptyState()
    {
        var empty = !ViewModel.IsLoading && ViewModel.IsEmpty && ViewModel.ErrorMessage == null;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (empty)
        {
            EmptyStateText.Text = ViewModel.Filter switch
            {
                "following" => "Nothing upcoming from shows you follow this week.",
                "everything" => "Nothing scheduled this week.",
                _ => "No events this week for this view."
            };
            EmptyActionsPanel.Visibility = ViewModel.Filter == "everything"
                ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
