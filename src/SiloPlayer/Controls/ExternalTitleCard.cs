using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Converters;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

public static class ExternalTitleCard
{
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static Border StatusBadge(string label, bool overlay = false)
    {
        var primary = label == "Available"; var destructive = label == "Failed";
        var secondary = label is "Approved" or "Processing" or "Partially available";
        var foreground = primary ? "AccentForegroundBrush" : destructive ? "DestructiveForegroundBrush" : label is "Declined" or "Cancelled" or "Unavailable" ? "SecondaryTextBrush" : "PrimaryTextBrush";
        return new Border { Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1), BorderBrush = primary || destructive || secondary ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : Brush("BorderBrush"),
            Background = primary ? Brush("AccentBrush") : destructive ? Brush("ErrorBrush") : secondary ? Brush("SecondaryBackgroundBrush") : overlay ? Brush("AppBackgroundBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = new TextBlock { Text = label, FontSize = 12, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.Medium, Foreground = Brush(foreground), TextTrimming = TextTrimming.CharacterEllipsis } };
    }

    public static Grid Build(RequestMediaResult item, double width, Func<Task>? request = null,
        Func<Task>? watchlist = null, bool removeOnly = false, string? statusCaption = null, bool attention = false, string? statusBadge = null,
        bool watchlistCard = false, Action? statusSearchAction = null)
    {
        var presentation = App.Services.GetService<UICustomizationService>()?.CardPresentation;
        var showCaption = presentation?.Caption != "artwork";
        var card = new Grid { Width = width, RowSpacing = 0 };
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(width * 1.5) });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var artworkWidth = Math.Max(0, width - 2);
        var artworkHeight = Math.Max(0, width * 1.5 - 2);
        var dim = attention || (!watchlistCard && !item.Request.Requestable);
        var image = new Image { Width = artworkWidth, Height = artworkHeight, Stretch = Stretch.UniformToFill };
        if (item.PosterUrl is { } url)
            image.Source = (ImageSource)new UrlToImageSourceConverter().Convert(url, typeof(ImageSource), dim ? "request-dim" : null!, "");
        var poster = new Grid { Width = artworkWidth, Height = artworkHeight };
        poster.Background = Brush("CardBackgroundBrush"); poster.Children.Add(image);
        var fallback = new DefaultArtwork { MediaType = item.MediaType, Dim = dim };
        fallback.Visibility = image.Source == null ? Visibility.Visible : Visibility.Collapsed;
        image.Loaded += (_, _) => { fallback.Visibility = image.Source == null ? Visibility.Visible : Visibility.Collapsed; _ = fallback.ObserveConvertedImageAsync(image); };
        image.ImageOpened += (_, _) => fallback.Visibility = Visibility.Collapsed;
        image.ImageFailed += (_, _) => { image.Source = null; fallback.Visibility = Visibility.Visible; };
        poster.Children.Insert(0, fallback);
        poster.Children.Add(new Border { Height = 96, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
            Background = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 1), EndPoint = new Windows.Foundation.Point(0, 0),
                GradientStops = { new GradientStop { Offset = 0, Color = Microsoft.UI.ColorHelper.FromArgb(126, 0, 0, 0) },
                    new GradientStop { Offset = 1, Color = Microsoft.UI.ColorHelper.FromArgb(0, 0, 0, 0) } } } });
        card.Children.Add(new Border { CornerRadius = new CornerRadius(16), BorderBrush = AlphaBrush("BorderBrush", .65), BorderThickness = new Thickness(1), Child = poster });
        void ClipPoster()
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(poster);
            var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
            geometry.Size = new System.Numerics.Vector2((float)poster.ActualWidth, (float)poster.ActualHeight);
            geometry.CornerRadius = new System.Numerics.Vector2(15);
            visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
        }
        poster.Loaded += (_, _) => ClipPoster(); poster.SizeChanged += (_, _) => { if (poster.IsLoaded) ClipPoster(); };
        var title = new TextBlock { Text = item.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, LineHeight = 21, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextTrimming = TextTrimming.CharacterEllipsis, CharacterSpacing = -25, Margin = new Thickness(4, 12, 4, 0) };
        title.Visibility = showCaption ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(title, 1); card.Children.Add(title);
        var meta = string.Join(" · ", new[] { item.MediaType == "series" ? "Series" : "Movie", item.YearText }.Where(s => s.Length > 0)).ToUpperInvariant();
        var caption = new StackPanel { Margin = new Thickness(4, showCaption ? 4 : 12, 4, 0), Spacing = 6,
            Visibility = showCaption || statusCaption != null ? Visibility.Visible : Visibility.Collapsed };
        if (showCaption) caption.Children.Add(new TextBlock { Text = meta, FontSize = 11, FontWeight = FontWeights.Medium, LineHeight = 16.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            CharacterSpacing = 140, Foreground = Brush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        if (statusCaption != null)
        {
            var statusText = new TextBlock { FontSize = 12, LineHeight = 18,
                Foreground = attention ? Brush("WarningBrush") : Brush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
            statusText.Inlines.Add(new Run { Text = statusCaption });
            if (statusSearchAction != null)
            {
                statusText.Inlines.Add(new Run { Text = " · " });
                var find = new Hyperlink { Foreground = statusText.Foreground, FontWeight = FontWeights.Medium };
                find.Inlines.Add(new Run { Text = "Find it" });
                find.Click += (_, _) => statusSearchAction();
                statusText.Inlines.Add(find);
            }
            caption.Children.Add(statusText);
        }
        Grid.SetRow(caption, 2); card.Children.Add(caption);
        var navigation = App.Services.GetRequiredService<NavigationService>();
        var open = new Button { Tag = new RequestDetailNavigation(item.MediaType, item.TmdbId),
            Style = (Style)Application.Current.Resources["PosterHitTargetButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        AutomationProperties.SetName(open, $"Open {item.Title} request details");
        open.Click += (_, _) => navigation.Navigate<RequestDetailPage>((RequestDetailNavigation)open.Tag);
        Grid.SetRowSpan(open, statusSearchAction == null ? 3 : 2); card.Children.Add(open);
        if (item.LibraryContentId is { Length: > 0 } libraryId)
        {
            var chip = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
                Children = { WebUiIcon.Create("library", 12, strokeThickness: 2.4), new TextBlock { Text = "LIBRARY", FontSize = 10, FontWeight = FontWeights.SemiBold, CharacterSpacing = 140 } } },
                Margin = new Thickness(8), Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(999),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(chip, $"Open {item.Title} in library");
            chip.Click += (_, _) => navigation.Navigate<ItemDetailPage>(libraryId); card.Children.Add(chip);
        }
        var state = item.Request.Status.Length > 0 || item.Request.State?.Length > 0 || item.Availability == "available";
        var overlayService = App.Services.GetService<CardOverlayService>();
        var overlayPrefs = overlayService?.GetPrefs();
        var showWatchlistBadge = overlayPrefs?.TryGetValue("request_status", out var preference) == true && preference.Enabled;
        if (watchlistCard && overlayService != null && overlayPrefs != null)
        {
            var data = new OverlayData { RequestStatus = statusBadge, Year = item.Year, RatingTmdb = item.VoteAverage,
                ContentRating = (item as WatchlistTitle)?.ContentRating };
            var geometry = CardOverlayGeometry.ForPoster(artworkWidth, overlayService.Preset);
            var stacks = new Dictionary<OverlayPosition, StackPanel>();
            var percent = item.Request.Download is { Phase: "queued" or "downloading" or "paused" or "stalled" or "importing" or "import_blocked" } download
                ? download.Percent : null;
            var progressVisible = showWatchlistBadge && !string.IsNullOrWhiteSpace(statusBadge) && percent != null;
            if (progressVisible)
            {
                var progress = new ProgressBar { Value = Math.Clamp(percent!.Value, 0, 100), Minimum = 0, Maximum = 100,
                    Height = 4, MinHeight = 4, VerticalAlignment = VerticalAlignment.Bottom,
                    IsHitTestVisible = false, Foreground = Brush("AccentBrush"), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(76, 255, 255, 255)) };
                AutomationProperties.SetName(progress, "Request download progress");
                poster.Children.Add(progress);
            }
            foreach (var def in overlayService.GetOrderedDefinitions())
            {
                if (!overlayPrefs.TryGetValue(def.Id, out var config) || !config.Enabled) continue;
                var value = def.GetValue(data);
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!stacks.TryGetValue(config.Position, out var stack))
                {
                    var bottom = config.Position is OverlayPosition.BottomLeft or OverlayPosition.BottomRight;
                    stack = new StackPanel { Spacing = geometry.StackGap,
                        HorizontalAlignment = config.Position is OverlayPosition.TopLeft or OverlayPosition.BottomLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                        VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top,
                        Margin = new Thickness(geometry.EdgeInset, geometry.EdgeInset, geometry.EdgeInset, geometry.EdgeInset + (bottom && progressVisible ? 8 : 0)),
                        IsHitTestVisible = false };
                    stacks[config.Position] = stack; poster.Children.Add(stack);
                }
                if (stack.Children.Count >= 3) continue;
                if (def.Id == "request_status" && attention && config.AccentColor == null) config = config with { AccentColor = "#f59e0b" };
                var badge = PosterCard.BuildBadge(value, def.Id, config, overlayService.Preset, geometry.Scale);
                badge.HorizontalAlignment = stack.HorizontalAlignment;
                badge.VerticalAlignment = stack.VerticalAlignment;
                badge.MaxWidth = artworkWidth - geometry.EdgeInset * 2;
                stack.Children.Add(badge);
            }
        }
        if (!watchlistCard && (state || !item.Request.Requestable || statusBadge != null))
        {
            var label = RequestViewerPolicy.Label(item.Request.Status, null, item.Request.State, item.Availability);
            if (!state) label = RequestViewerPolicy.Reason(item.Request.Reason);
            if (statusBadge != null) label = statusBadge;
            var badge = StatusBadge(label, overlay: true); badge.Margin = new Thickness(8);
            badge.HorizontalAlignment = watchlistCard ? HorizontalAlignment.Left : HorizontalAlignment.Right; badge.VerticalAlignment = VerticalAlignment.Top;
            if (watchlistCard)
            {
                badge.Padding = new Thickness(8, 0, 8, 0);
                badge.BorderThickness = new Thickness(1);
                badge.Background = attention ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(100, 245, 158, 11)) : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(178, 0, 0, 0));
                if (badge.Child is TextBlock text) { text.Text = label.ToUpperInvariant(); text.FontSize = 11; text.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White); }
            }
            badge.MaxWidth = width - (item.LibraryContentId is { Length: > 0 } ? 88 : 16);
            card.Children.Add(badge);
        }
        var actions = new List<(Button Button, Func<bool> Pending)>();
        var hovered = false;
        void UpdateReveal()
        {
            foreach (var action in actions)
            {
                var revealed = hovered || action.Pending() || action.Button.FocusState != FocusState.Unfocused;
                action.Button.Opacity = revealed ? 1 : 0;
                action.Button.IsHitTestVisible = revealed;
            }
        }
        if (request != null && item.Request.Requestable)
        {
            var pending = false;
            var button = new Button { Height = 36, MinHeight = 0, CornerRadius = new CornerRadius(999), Padding = new Thickness(14, 0, 14, 0),
                Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
            object NormalContent() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { WebUiIcon.Create("plus", 14, Brush("AccentForegroundBrush"), 2.5), new TextBlock { Text = "Request", FontSize = 12, FontWeight = FontWeights.SemiBold } } };
            button.Content = NormalContent(); AutomationProperties.SetName(button, $"Request {item.Title}");
            button.Click += async (_, _) => { if (pending) return; pending = true; button.IsEnabled = false;
                AutomationProperties.SetName(button, $"Sending request for {item.Title}");
                button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new ProgressRing { IsActive = true, Width = 14, Height = 14 }, new TextBlock { Text = "Sending", FontSize = 12, FontWeight = FontWeights.SemiBold } } }; UpdateReveal();
                try { await request(); }
                catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
                finally { pending = false; button.IsEnabled = true; button.Content = NormalContent(); AutomationProperties.SetName(button, $"Request {item.Title}"); UpdateReveal(); } };
            actions.Add((button, () => pending)); card.Children.Add(button);
        }
        if (watchlist != null)
        {
            var pending = false;
            var button = new Button { Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(10),
                Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0,
                Background = AlphaBrush("AppBackgroundBrush", .8), BorderBrush = AlphaBrush("BorderBrush", .2), BorderThickness = new Thickness(1) };
            void UpdateBookmark() { var wide = card.XamlRoot == null || card.XamlRoot.Size.Width >= 640;
                button.Width = button.Height = wide ? 32 : 24; button.Margin = new Thickness(wide ? 10 : 6);
                button.Content = WebUiIcon.Create(item.InWatchlist == true || removeOnly ? "bookmark-check" : "bookmark", wide ? 16 : 12,
                item.InWatchlist == true || removeOnly ? Brush("AccentBrush") : Brush("PrimaryTextBrush"));
                ToolTipService.SetToolTip(button, item.InWatchlist == true || removeOnly ? "On Watchlist" : "Add to Watchlist");
                AutomationProperties.SetName(button, $"{(item.InWatchlist == true || removeOnly ? "Remove" : "Add")} {item.Title} {(item.InWatchlist == true || removeOnly ? "from" : "to")} your watchlist"); }
            UpdateBookmark();
            XamlRoot? observedRoot = null;
            void RootChanged(XamlRoot _, XamlRootChangedEventArgs __) => UpdateBookmark();
            card.Loaded += (_, _) => { if (observedRoot != null) observedRoot.Changed -= RootChanged; observedRoot = card.XamlRoot; if (observedRoot != null) observedRoot.Changed += RootChanged; UpdateBookmark(); };
            card.Unloaded += (_, _) => { if (observedRoot != null) observedRoot.Changed -= RootChanged; observedRoot = null; };
            button.Click += async (_, _) => { if (pending) return; pending = true; button.IsEnabled = false; UpdateReveal();
                try { await watchlist(); UpdateBookmark(); } catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
                finally { pending = false; button.IsEnabled = true; UpdateReveal(); } };
            actions.Add((button, () => pending)); card.Children.Add(button);
        }
        foreach (var action in actions) { action.Button.GotFocus += (_, _) => UpdateReveal(); action.Button.LostFocus += (_, _) => UpdateReveal(); }
        card.PointerEntered += (_, _) => { hovered = true; UpdateReveal(); };
        card.PointerExited += (_, _) => { hovered = false; UpdateReveal(); };
        UpdateReveal();
        return card;
    }

    private static Brush AlphaBrush(string key, double opacity)
    {
        var color = ((SolidColorBrush)Brush(key)).Color;
        color.A = (byte)Math.Round(color.A * opacity);
        return new SolidColorBrush(color);
    }
}
