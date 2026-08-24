using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

/// <summary>
/// Tinder-style 3-step "Watch Tonight" dialog matching the webui implementation.
///
/// Step 1 — Mode select: "Pick up where I left off" (continue) or "Find something new" (discover).
/// Step 2 — Genre picker (discover only): toggle genre chips to narrow the pool.
/// Step 3 — Swipe deck: stack of cards backed by /recommendations/watch-tonight/cards.
///          The top card is draggable (pointer-drag) and exposes Skip / Play / Like buttons.
///          Advancing past the loaded cards triggers a paged fetch via exclude_ids[].
///
/// Ported from continuum-server/web/src/components/WatchTonightDialog.tsx + watchtonight/*.tsx.
/// Simplified for WinUI 3: 2D scale-flip approximates the web's rotateY card flip; spring
/// animations replaced with CubicEase storyboards.
/// </summary>
public sealed partial class WatchTonightDialog : ContentDialog
{
    private readonly RecommendationsApi _api;

    // ─── State ──────────────────────────────────────────────────────────
    private enum Step { ModeSelect, GenrePicker, SwipeDeck }
    private Step _step = Step.ModeSelect;
    private string _mode = "discover";           // "continue" | "discover"
    private readonly HashSet<string> _selectedGenres = new();
    private readonly List<SwipeCard> _cards = new();
    private int _topIndex;
    private bool _hasMore;
    private bool _isFetching;
    private bool _prefetchTriggered;

    // Chip buttons kept so we can toggle their styling on (de)selection.
    private readonly Dictionary<string, Button> _genreChips = new();

    // Cards currently laid out on the stack — so we can update transforms
    // / reuse the same Border during drag without rebuilding every frame.
    private readonly List<Border> _stackCards = new();

    // Per-card visual state keyed by the outer Border. Kept OUT of
    // Border.Resources because a UIElement placed in a ResourceDictionary
    // while it is also in a visual tree throws "element is already the
    // child of another element" at load time.
    private sealed class CardState
    {
        public FrameworkElement? FrontFace;
        public FrameworkElement? BackFace;
        public bool IsFlipped;
        public bool DidDrag;
    }
    private readonly Dictionary<Border, CardState> _cardState = new();

    private static readonly string[] AdvancedFilterGenres =
    [
        "Action", "Adventure", "Animation", "Comedy", "Crime",
        "Documentary", "Drama", "Family", "Fantasy", "History",
        "Horror", "Music", "Mystery", "Romance", "Science Fiction",
        "Thriller", "War", "Western",
    ];

    public WatchTonightDialog()
    {
        _api = App.Services.GetRequiredService<RecommendationsApi>();
        this.InitializeComponent();
        this.Opened += OnOpened;
        this.Closed += OnClosed;
        this.KeyDown += OnKeyDown;
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        BuildGenreChips();
        GoToStep(Step.ModeSelect);
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _mode = "discover";
        _selectedGenres.Clear();
        _cards.Clear();
        _topIndex = 0;
        _hasMore = false;
        _prefetchTriggered = false;
        CardStackHost.Children.Clear();
        _stackCards.Clear();
        _cardState.Clear();
        foreach (var (_, button) in _genreChips)
            StyleChip(button, false);
        UpdateGenreGoButtonLabel();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    // ─── Step navigation ────────────────────────────────────────────────

    private void GoToStep(Step step)
    {
        _step = step;
        ModeSelectStep.Visibility = step == Step.ModeSelect ? Visibility.Visible : Visibility.Collapsed;
        GenrePickerStep.Visibility = step == Step.GenrePicker ? Visibility.Visible : Visibility.Collapsed;
        SwipeDeckStep.Visibility = step == Step.SwipeDeck ? Visibility.Visible : Visibility.Collapsed;

        DescriptionText.Text = step switch
        {
            Step.ModeSelect => "What are you in the mood for?",
            Step.GenrePicker => "Pick some genres to narrow things down",
            Step.SwipeDeck => _mode == "continue"
                ? "Swipe through your in-progress titles"
                : "Swipe right to play, left to skip",
            _ => ""
        };

        if (step == Step.SwipeDeck)
        {
            _cards.Clear();
            _topIndex = 0;
            _hasMore = false;
            _prefetchTriggered = false;
            // Reset any leftover overlay state from a previous session so we
            // don't flash stale "You've seen everything" / error text before
            // the loader takes over.
            DeckEmptyPanel.Visibility = Visibility.Collapsed;
            DeckErrorPanel.Visibility = Visibility.Collapsed;
            _ = FetchNextPageAsync();
        }
    }

    private void ContinueModeButton_Click(object sender, RoutedEventArgs e)
    {
        _mode = "continue";
        GoToStep(Step.SwipeDeck);
    }

    private void DiscoverModeButton_Click(object sender, RoutedEventArgs e)
    {
        _mode = "discover";
        GoToStep(Step.GenrePicker);
    }

    private void GenreBackButton_Click(object sender, RoutedEventArgs e)
    {
        GoToStep(Step.ModeSelect);
    }

    private void GenreGoButton_Click(object sender, RoutedEventArgs e)
    {
        GoToStep(Step.SwipeDeck);
    }

    private void StartOverButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedGenres.Clear();
        foreach (var (_, btn) in _genreChips) StyleChip(btn, false);
        UpdateGenreGoButtonLabel();
        GoToStep(Step.ModeSelect);
    }

    // ─── Genre chips ────────────────────────────────────────────────────

    private void BuildGenreChips()
    {
        if (_genreChips.Count > 0) return;

        foreach (var genre in AdvancedFilterGenres)
        {
            // Default Button style has MinWidth≈120 / MinHeight≈32 which
            // makes the chips way too chunky and breaks wrap spacing.
            // Override both to 0 so the chip hugs its content.
            var btn = new Button
            {
                Content = new TextBlock
                {
                    Text = genre,
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                },
                Padding = new Thickness(14, 6, 14, 6),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                MinWidth = 0,
                MinHeight = 0,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            StyleChip(btn, false);
            string captured = genre;
            btn.Click += (_, _) => ToggleGenre(captured);
            _genreChips[genre] = btn;
            GenreChipsPanel.Children.Add(btn);
        }
    }

    private void ToggleGenre(string genre)
    {
        if (_selectedGenres.Contains(genre))
            _selectedGenres.Remove(genre);
        else
            _selectedGenres.Add(genre);
        if (_genreChips.TryGetValue(genre, out var btn))
            StyleChip(btn, _selectedGenres.Contains(genre));
        UpdateGenreGoButtonLabel();
    }

    private void StyleChip(Button btn, bool selected)
    {
        if (selected)
        {
            btn.Background = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"];
            btn.BorderBrush = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            if (btn.Content is TextBlock tb)
                tb.Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        }
        else
        {
            btn.Background = new SolidColorBrush(Colors.Transparent);
            btn.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
            if (btn.Content is TextBlock tb)
                tb.Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        }
    }

    private void UpdateGenreGoButtonLabel()
    {
        GenreGoButtonText.Text = _selectedGenres.Count == 0
            ? "All Genres"
            : $"Go ({_selectedGenres.Count})";
    }

    // ─── Deck fetch / paging ────────────────────────────────────────────

    private async Task FetchNextPageAsync()
    {
        if (_isFetching) return;
        _isFetching = true;
        bool firstPage = _cards.Count == 0;

        if (firstPage)
        {
            DeckLoadingPanel.Visibility = Visibility.Visible;
            DeckEmptyPanel.Visibility = Visibility.Collapsed;
            DeckErrorPanel.Visibility = Visibility.Collapsed;
            CardStackHost.Visibility = Visibility.Collapsed;
            ActionButtonsPanel.Visibility = Visibility.Collapsed;
            CounterText.Visibility = Visibility.Collapsed;
        }

        try
        {
            var excludeIds = _cards.Select(c => c.ContentId).ToList();
            var genres = _selectedGenres.Count > 0 ? _selectedGenres.ToList() : null;
            var page = await _api.GetWatchTonightCardsAsync(_mode, genres, excludeIds, 12);
            _cards.AddRange(page.Cards);
            _hasMore = page.HasMore;
            _prefetchTriggered = false;

            DeckLoadingPanel.Visibility = Visibility.Collapsed;
            if (_cards.Count == 0 && !_hasMore)
            {
                ShowDeckEmpty();
            }
            else
            {
                CardStackHost.Visibility = Visibility.Visible;
                ActionButtonsPanel.Visibility = Visibility.Visible;
                CounterText.Visibility = Visibility.Visible;
                RebuildStack();
            }
        }
        catch (Exception ex)
        {
            DeckLoadingPanel.Visibility = Visibility.Collapsed;
            ShowDeckError(ex.Message);
        }
        finally
        {
            _isFetching = false;
        }
    }

    private void ShowDeckEmpty()
    {
        CardStackHost.Visibility = Visibility.Collapsed;
        ActionButtonsPanel.Visibility = Visibility.Collapsed;
        CounterText.Visibility = Visibility.Collapsed;
        DeckEmptyPanel.Visibility = Visibility.Visible;
        DeckErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowDeckError(string message)
    {
        CardStackHost.Visibility = Visibility.Collapsed;
        ActionButtonsPanel.Visibility = Visibility.Collapsed;
        CounterText.Visibility = Visibility.Collapsed;
        DeckEmptyPanel.Visibility = Visibility.Collapsed;
        DeckErrorPanel.Visibility = Visibility.Visible;
        DeckErrorText.Text = string.IsNullOrWhiteSpace(message)
            ? "Failed to load suggestions."
            : message;
    }

    // ─── Card stack rendering ───────────────────────────────────────────

    private void RebuildStack()
    {
        CardStackHost.Children.Clear();
        _stackCards.Clear();
        _cardState.Clear();

        var visible = _cards.Skip(_topIndex).Take(3).ToList();

        if (visible.Count == 0)
        {
            if (!_hasMore)
            {
                ShowDeckEmpty();
                return;
            }
            // Waiting for more cards — show skeleton/spinner while the
            // next page lands.
            DeckLoadingPanel.Visibility = Visibility.Visible;
            ActionButtonsPanel.Visibility = Visibility.Collapsed;
            CounterText.Visibility = Visibility.Collapsed;
            if (!_isFetching) _ = FetchNextPageAsync();
            return;
        }

        // Render back-to-front so the top card is last in Z order and
        // receives input first.
        for (int i = visible.Count - 1; i >= 0; i--)
        {
            int depth = i;
            bool isTop = depth == 0;
            var card = BuildCard(visible[i], isTop);
            var (scale, yOff, opacity) = depth switch
            {
                0 => (1.0, 0.0, 1.0),
                1 => (0.95, 8.0, 0.7),
                _ => (0.9, 16.0, 0.4),
            };
            var transform = new CompositeTransform
            {
                CenterX = 180,
                CenterY = 220,
                ScaleX = scale,
                ScaleY = scale,
                TranslateY = yOff,
            };
            card.RenderTransform = transform;
            card.Opacity = opacity;
            Canvas.SetZIndex(card, visible.Count - depth);
            CardStackHost.Children.Add(card);
            _stackCards.Add(card);
        }

        // Prefetch next page when we're down to the last 3 loaded cards.
        int remaining = _cards.Count - _topIndex;
        if (remaining <= 3 && _hasMore && !_isFetching && !_prefetchTriggered)
        {
            _prefetchTriggered = true;
            _ = FetchNextPageAsync();
        }

        UpdateCounter();
    }

    private void UpdateCounter()
    {
        CounterText.Text = _hasMore
            ? $"{_topIndex + 1} / {_cards.Count}+"
            : $"{_topIndex + 1} / {_cards.Count}";
    }

    // ─── Card rendering ─────────────────────────────────────────────────

    private Border BuildCard(SwipeCard card, bool isTop)
    {
        // Outer frame — full card with rounded corners + border + drop shadow.
        var outer = new Border
        {
            Width = 360,
            Height = 440,
            CornerRadius = new CornerRadius(16),
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Tag = card,
        };

        // Flip host: two children, FrontFace visible / BackFace collapsed.
        // Click to flip — uses a 2D scale animation to approximate the web's
        // rotateY 3D flip.
        var flipHost = new Grid();
        var frontFace = BuildFrontFace(card, isTop);
        var backFace = BuildBackFace(card);
        backFace.Visibility = Visibility.Collapsed;
        flipHost.Children.Add(frontFace);
        flipHost.Children.Add(backFace);

        // Stash flip state in the per-card state dictionary. (Using
        // Border.Resources for this throws "element already child" because
        // putting a UIElement in a ResourceDictionary while it's also in a
        // visual tree gives it two logical parents.)
        _cardState[outer] = new CardState
        {
            FrontFace = frontFace,
            BackFace = backFace,
        };

        outer.Child = flipHost;

        if (isTop)
        {
            outer.ManipulationMode = ManipulationModes.TranslateX;
            outer.ManipulationStarted += TopCard_ManipulationStarted;
            outer.ManipulationDelta += TopCard_ManipulationDelta;
            outer.ManipulationCompleted += TopCard_ManipulationCompleted;
            outer.Tapped += TopCard_Tapped;
        }

        return outer;
    }

    private FrameworkElement BuildFrontFace(SwipeCard card, bool isTop)
    {
        var grid = new Grid();

        // ── Background image (backdrop preferred, poster fallback) ───
        string? imgUrl = !string.IsNullOrEmpty(card.BackdropUrl) ? card.BackdropUrl : card.PosterUrl;
        if (!string.IsNullOrEmpty(imgUrl))
        {
            try
            {
                grid.Children.Add(new Image
                {
                    Source = new BitmapImage(new Uri(imgUrl)),
                    Stretch = Stretch.UniformToFill,
                });
            }
            catch { /* placeholder below */ }
        }
        if (grid.Children.Count == 0)
        {
            grid.Children.Add(new TextBlock
            {
                Text = "No Image",
                FontSize = 14,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        // ── Gradient overlay (bottom dark → top clear) ───────────────
        var gradient = new Rectangle
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 1),
                EndPoint = new Windows.Foundation.Point(0, 0),
                GradientStops =
                {
                    new GradientStop { Offset = 0, Color = Color.FromArgb(0xCC, 0, 0, 0) },
                    new GradientStop { Offset = 0.5, Color = Color.FromArgb(0x33, 0, 0, 0) },
                    new GradientStop { Offset = 1, Color = Color.FromArgb(0x00, 0, 0, 0) },
                },
            },
        };
        grid.Children.Add(gradient);

        // ── Source badge (top-right) ────────────────────────────────
        string badgeLabel = SourceLabel(card.WatchTonightSource);
        if (!string.IsNullOrEmpty(badgeLabel))
        {
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(12),
                Child = new TextBlock
                {
                    Text = badgeLabel,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                },
            });
        }

        // ── Swipe indicators (only on top card, driven by drag) ────
        if (isTop)
        {
            var rejectBadge = new Border
            {
                Width = 56, Height = 56,
                CornerRadius = new CornerRadius(28),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xEF, 0x44, 0x44)),
                BorderThickness = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(16),
                Opacity = 0,
                Child = new FontIcon
                {
                    Glyph = "\uE711",
                    FontSize = 24,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xEF, 0x44, 0x44)),
                },
            };
            rejectBadge.Name = "RejectIndicator";
            grid.Children.Add(rejectBadge);

            var acceptBadge = new Border
            {
                Width = 56, Height = 56,
                CornerRadius = new CornerRadius(28),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)),
                BorderThickness = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(16),
                Opacity = 0,
                Child = new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 24,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)),
                },
            };
            acceptBadge.Name = "AcceptIndicator";
            grid.Children.Add(acceptBadge);
        }

        // ── Bottom info block (title, metadata badges, progress) ────
        var bottomInfo = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(20, 20, 20, 20),
        };

        bool hasEpisodeMeta = card.SeasonNumber.HasValue && card.EpisodeNumber.HasValue;
        string heading = hasEpisodeMeta && !string.IsNullOrEmpty(card.SeriesTitle)
            ? card.SeriesTitle!
            : card.Title;

        // Logo preferred, else big title.
        if (!string.IsNullOrEmpty(card.LogoUrl))
        {
            try
            {
                bottomInfo.Children.Add(new Image
                {
                    Source = new BitmapImage(new Uri(card.LogoUrl)),
                    Height = 40,
                    MaxWidth = 240,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }
            catch
            {
                bottomInfo.Children.Add(BuildTitleText(heading));
            }
        }
        else
        {
            bottomInfo.Children.Add(BuildTitleText(heading));
        }

        if (hasEpisodeMeta)
        {
            string ep = $"S{card.SeasonNumber} E{card.EpisodeNumber}";
            if (!string.IsNullOrEmpty(card.Title) && !string.IsNullOrEmpty(card.SeriesTitle) && card.Title != card.SeriesTitle)
                ep += $" \u2022 {card.Title}";
            bottomInfo.Children.Add(new TextBlock
            {
                Text = ep,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            });
        }

        var metaRow = new Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 6 };
        if (card.Year > 0)
        {
            metaRow.Children.Add(new TextBlock
            {
                Text = card.Year.ToString(),
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
            });
        }
        if (card.RatingImdb.HasValue)
        {
            var ratingStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            ratingStack.Children.Add(new FontIcon
            {
                Glyph = "\uE735",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFA, 0xCC, 0x15)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            ratingStack.Children.Add(new TextBlock
            {
                Text = card.RatingImdb.Value.ToString("0.0"),
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFA, 0xCC, 0x15)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            metaRow.Children.Add(ratingStack);
        }
        if (card.Runtime.HasValue && card.Runtime.Value > 0)
        {
            int rt = card.Runtime.Value;
            string rtStr = rt >= 60 ? $"{rt / 60}h {rt % 60}m" : $"{rt}m";
            metaRow.Children.Add(new TextBlock
            {
                Text = rtStr,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            });
        }
        foreach (var g in card.Genres.Take(3))
        {
            metaRow.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock
                {
                    Text = g,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
                },
            });
        }
        if (metaRow.Children.Count > 0)
            bottomInfo.Children.Add(metaRow);

        grid.Children.Add(bottomInfo);

        // ── Progress bar for continue_watching ───────────────────────
        if (card.WatchTonightSource == "continue_watching"
            && card.DurationSeconds.HasValue && card.DurationSeconds.Value > 0)
        {
            double pct = Math.Clamp(
                (card.PositionSeconds ?? 0) / card.DurationSeconds.Value * 100,
                0, 100);
            var track = new Grid
            {
                Height = 3,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
            var fill = new Rectangle
            {
                Fill = (SolidColorBrush)Application.Current.Resources["AccentBrush"],
            };
            Grid.SetColumn(fill, 0);
            track.Children.Add(fill);
            grid.Children.Add(track);
        }

        // ── Flip hint (top card only) ────────────────────────────────
        if (isTop)
        {
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 12, 12, 0),
                // Positioned under the badge — offset down a bit.
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE946", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)) },
                        new TextBlock { Text = "Tap for details", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)) },
                    },
                },
                Visibility = Visibility.Collapsed,  // hidden — badge already conveys source, keeps card clean
            });
        }

        return grid;
    }

    private static TextBlock BuildTitleText(string heading) => new()
    {
        Text = heading,
        FontSize = 22,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Colors.White),
        TextWrapping = TextWrapping.Wrap,
        MaxLines = 2,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private FrameworkElement BuildBackFace(SwipeCard card)
    {
        var grid = new Grid
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
        };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(20),
        };

        var content = new StackPanel { Spacing = 14 };

        bool hasEpisodeMeta = card.SeasonNumber.HasValue && card.EpisodeNumber.HasValue;
        string heading = hasEpisodeMeta && !string.IsNullOrEmpty(card.SeriesTitle)
            ? card.SeriesTitle!
            : card.Title;

        content.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        if (hasEpisodeMeta)
        {
            string ep = $"S{card.SeasonNumber} E{card.EpisodeNumber}";
            if (!string.IsNullOrEmpty(card.Title) && !string.IsNullOrEmpty(card.SeriesTitle) && card.Title != card.SeriesTitle)
                ep += $" \u2022 {card.Title}";
            content.Children.Add(new TextBlock
            {
                Text = ep,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }

        if (!string.IsNullOrEmpty(card.Overview))
        {
            content.Children.Add(new TextBlock
            {
                Text = card.Overview,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 8,
                TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = 18,
            });
        }

        if (card.Cast != null && card.Cast.Count > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "CAST",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                CharacterSpacing = 120,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 4, 0, 0),
            });
            var castStack = new StackPanel { Spacing = 4 };
            foreach (var c in card.Cast.Take(6))
            {
                var row = new TextBlock
                {
                    FontSize = 12,
                    Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap,
                };
                row.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = c.Name,
                    FontWeight = FontWeights.Medium,
                });
                if (!string.IsNullOrEmpty(c.Character))
                {
                    row.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text = $" as {c.Character}",
                        Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                    });
                }
                castStack.Children.Add(row);
            }
            content.Children.Add(castStack);
        }

        // Metadata row on back (year, rating, runtime)
        var metaRow = new Controls.WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 6 };
        if (card.RatingImdb.HasValue)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            stack.Children.Add(new FontIcon
            {
                Glyph = "\uE735",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFA, 0xCC, 0x15)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(new TextBlock
            {
                Text = card.RatingImdb.Value.ToString("0.0"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFA, 0xCC, 0x15)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            metaRow.Children.Add(stack);
        }
        if (card.Runtime.HasValue && card.Runtime.Value > 0)
        {
            int rt = card.Runtime.Value;
            string rtStr = rt >= 60 ? $"{rt / 60}h {rt % 60}m" : $"{rt}m";
            metaRow.Children.Add(new TextBlock
            {
                Text = rtStr,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }
        if (card.Year > 0)
        {
            metaRow.Children.Add(new TextBlock
            {
                Text = card.Year.ToString(),
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }
        if (metaRow.Children.Count > 0)
            content.Children.Add(metaRow);

        if (card.Genres.Count > 0)
        {
            var chipRow = new Controls.WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
            foreach (var g in card.Genres)
            {
                chipRow.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Child = new TextBlock
                    {
                        Text = g,
                        FontSize = 10,
                        Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                    },
                });
            }
            content.Children.Add(chipRow);
        }

        scroll.Content = content;
        grid.Children.Add(scroll);

        // Flip-back hint
        grid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 12, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE946", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)) },
                    new TextBlock { Text = "Tap to flip back", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)) },
                },
            },
        });

        return grid;
    }

    // ─── Drag / flip handlers (top card) ────────────────────────────────

    private const double SwipeThreshold = 100;

    private void TopCard_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        if (sender is Border card && _cardState.TryGetValue(card, out var st))
            st.DidDrag = true;
    }

    private void TopCard_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is not Border card) return;
        if (card.RenderTransform is not CompositeTransform tf) return;

        tf.TranslateX += e.Delta.Translation.X;
        // Subtle rotation proportional to drag (±12deg).
        tf.Rotation = Math.Clamp(tf.TranslateX / 200.0 * 12.0, -12, 12);

        if (_cardState.TryGetValue(card, out var st))
            UpdateSwipeIndicators(st.FrontFace, tf.TranslateX);
    }

    private void TopCard_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (sender is not Border card) return;
        if (card.RenderTransform is not CompositeTransform tf) return;

        double offset = tf.TranslateX;
        double velocity = e.Velocities.Linear.X;
        bool flung = Math.Abs(velocity) > 1.5;

        if (offset > SwipeThreshold || (flung && velocity > 0))
        {
            AnimateCardExit(card, targetX: 400, onDone: AcceptTopCard);
        }
        else if (offset < -SwipeThreshold || (flung && velocity < 0))
        {
            AnimateCardExit(card, targetX: -400, onDone: AdvanceTopCard);
        }
        else
        {
            // Snap back to center.
            AnimateTo(tf, "TranslateX", 0, 200);
            AnimateTo(tf, "Rotation", 0, 200);
            if (_cardState.TryGetValue(card, out var st))
            {
                UpdateSwipeIndicators(st.FrontFace, 0);
                // Clear didDrag after snapping so a tap without drag still flips.
                st.DidDrag = Math.Abs(offset) > 4;
            }
        }
    }

    private void TopCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Border card) return;
        if (!_cardState.TryGetValue(card, out var st)) return;
        if (st.DidDrag)
        {
            st.DidDrag = false;
            return;
        }
        FlipCard(card);
    }

    private void FlipCard(Border card)
    {
        if (!_cardState.TryGetValue(card, out var st)) return;
        if (st.FrontFace is not FrameworkElement front) return;
        if (st.BackFace is not FrameworkElement back) return;

        bool isFlipped = st.IsFlipped;
        // 2D flip approximation: scale X to 0, swap faces, scale back to 1.
        var scale = new ScaleTransform { ScaleX = 1, ScaleY = 1, CenterX = 180, CenterY = 220 };
        card.RenderTransform = scale;

        var half1 = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(half1, scale);
        Storyboard.SetTargetProperty(half1, "ScaleX");
        var sb1 = new Storyboard();
        sb1.Children.Add(half1);
        sb1.Completed += (_, _) =>
        {
            front.Visibility = isFlipped ? Visibility.Visible : Visibility.Collapsed;
            back.Visibility = isFlipped ? Visibility.Collapsed : Visibility.Visible;
            st.IsFlipped = !isFlipped;

            var half2 = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(half2, scale);
            Storyboard.SetTargetProperty(half2, "ScaleX");
            var sb2 = new Storyboard();
            sb2.Children.Add(half2);
            sb2.Completed += (_, _) =>
            {
                // Restore the composite transform so drag handlers work again.
                var ct = new CompositeTransform
                {
                    CenterX = 180, CenterY = 220,
                    ScaleX = 1, ScaleY = 1,
                };
                card.RenderTransform = ct;
            };
            sb2.Begin();
        };
        sb1.Begin();
    }

    private void UpdateSwipeIndicators(FrameworkElement? frontFace, double translateX)
    {
        if (frontFace is not Grid g) return;
        foreach (var child in g.Children)
        {
            if (child is Border b)
            {
                if (b.Name == "RejectIndicator")
                    b.Opacity = Math.Clamp(-translateX / SwipeThreshold, 0, 1);
                else if (b.Name == "AcceptIndicator")
                    b.Opacity = Math.Clamp(translateX / SwipeThreshold, 0, 1);
            }
        }
    }

    private void AnimateCardExit(Border card, double targetX, Action onDone)
    {
        if (card.RenderTransform is not CompositeTransform tf) return;
        var sb = new Storyboard();

        var tx = new DoubleAnimation
        {
            To = targetX,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(tx, tf);
        Storyboard.SetTargetProperty(tx, "TranslateX");
        sb.Children.Add(tx);

        var op = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(op, card);
        Storyboard.SetTargetProperty(op, "Opacity");
        sb.Children.Add(op);

        var rot = new DoubleAnimation
        {
            To = targetX > 0 ? 15 : -15,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(rot, tf);
        Storyboard.SetTargetProperty(rot, "Rotation");
        sb.Children.Add(rot);

        sb.Completed += (_, _) => onDone();
        sb.Begin();
    }

    private static void AnimateTo(CompositeTransform tf, string property, double to, int durationMs)
    {
        var anim = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, tf);
        Storyboard.SetTargetProperty(anim, property);
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    // ─── Actions ────────────────────────────────────────────────────────

    private void AdvanceTopCard()
    {
        _topIndex++;
        RebuildStack();
    }

    private void AcceptTopCard()
    {
        PlayTopCard();
    }

    private async void PlayTopCard()
    {
        if (_topIndex >= _cards.Count) return;
        var card = _cards[_topIndex];
        this.Hide();
        var nav = App.Services.GetRequiredService<NavigationService>();
        try
        {
            switch (card.Type.Trim().ToLowerInvariant())
            {
                case "movie":
                case "episode":
                case "audiobook":
                    await App.Services.GetRequiredService<Services.PlayerService>()
                        .PlayAsync(card.ContentId);
                    break;
                case "ebook":
                    nav.Navigate<EbookReaderPage>(new EbookReaderNavigation(card.ContentId));
                    break;
                default:
                    nav.Navigate<ItemDetailPage>(card.ContentId);
                    break;
            }
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<Services.ToastService>()
                .Error($"Could not start playback: {ex.Message}");
        }
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stackCards.Count == 0) return;
        // Animate the top card off to the left, then advance.
        var topCard = _stackCards[^1];
        AnimateCardExit(topCard, -400, AdvanceTopCard);
    }

    private void LikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stackCards.Count == 0) return;
        var topCard = _stackCards[^1];
        AnimateCardExit(topCard, 400, AcceptTopCard);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        PlayTopCard();
    }

    // ─── Keyboard ───────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_step != Step.SwipeDeck) return;
        if (_stackCards.Count == 0) return;
        var topCard = _stackCards[^1];
        if (e.Key == Windows.System.VirtualKey.Left)
        {
            AnimateCardExit(topCard, -400, AdvanceTopCard);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Right)
        {
            AnimateCardExit(topCard, 400, AcceptTopCard);
            e.Handled = true;
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static string SourceLabel(string source) => source switch
    {
        "continue_watching" => "Continue",
        "next_up" => "Next Up",
        "recommendation" => "For You",
        _ => ""
    };
}
