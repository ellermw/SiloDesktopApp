using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

public sealed partial class AudiobookSquareCard : UserControl
{
    private bool _isPointerOver;
    private bool _isKeyboardFocusWithin;
    private int _posterGeneration;
    private MediaItem? _artworkItem;

    public static readonly DependencyProperty MediaItemProperty = DependencyProperty.Register(
        nameof(MediaItem), typeof(MediaItem), typeof(AudiobookSquareCard),
        new PropertyMetadata(null, OnMediaItemChanged));

    public MediaItem? MediaItem
    {
        get => (MediaItem?)GetValue(MediaItemProperty);
        set => SetValue(MediaItemProperty, value);
    }

    public AudiobookSquareCard()
    {
        InitializeComponent();
        MoreButton.Tapped += (_, args) => args.Handled = true;
        ContextRequested += Card_ContextRequested;
        Loaded += (_, _) => ObserveArtwork(MediaItem);
        Unloaded += (_, _) => { if (!IsLoaded) ObserveArtwork(null); };
    }

    private static void OnMediaItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AudiobookSquareCard card) return;
        card.ObserveArtwork(card.IsLoaded ? e.NewValue as MediaItem : null);
        if (e.NewValue is MediaItem item) card.Bind(item);
        else card.ResetCard();
    }

    private void ObserveArtwork(MediaItem? item)
    {
        if (ReferenceEquals(_artworkItem, item)) return;
        if (_artworkItem != null) _artworkItem.ArtworkUrlsChanged -= OnArtworkUrlsChanged;
        _artworkItem = item;
        if (_artworkItem != null) _artworkItem.ArtworkUrlsChanged += OnArtworkUrlsChanged;
    }

    private void OnArtworkUrlsChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || !ReferenceEquals(sender, MediaItem) || CoverImage.Source != null
            || string.IsNullOrWhiteSpace(MediaItem?.PosterUrl)) return;
        _ = LoadPosterAsync(MediaItem!, ++_posterGeneration);
    }

    private void Bind(MediaItem item)
    {
        var generation = ++_posterGeneration;
        CoverImage.Source = null;
        CoverImage.Opacity = 0;
        FallbackTitle.Visibility = Visibility.Visible;
        TitleText.Text = item.Title;
        FallbackTitle.Text = item.Title;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, item.Title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MoreButton, $"More actions for {item.Title}");
        var position = Math.Max(0, item.PositionSeconds ?? 0);
        var duration = Math.Max(0, item.DurationSeconds ?? 0);
        ProgressFill.Width = duration > 0 ? 168 * Math.Clamp(position / duration, 0, 1) : 0;
        var remaining = Math.Max(0, duration - position);
        TimeLeftText.Text = remaining > 0 ? $"{FormatDuration(remaining)} left" : "";
        if (!string.IsNullOrWhiteSpace(item.PosterUrl)) _ = LoadPosterAsync(item, generation);
    }

    private async Task LoadPosterAsync(MediaItem item, int generation)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var path = await imageService.GetImageDiskPathAsync(item.ContentId, "poster", item.PosterUrl!, App.Services.GetRequiredService<HttpClient>());
            if (generation != _posterGeneration
                || !ReferenceEquals(item, MediaItem)
                || string.IsNullOrWhiteSpace(path)) return;
            CoverImage.Source = new BitmapImage { UriSource = new Uri(path), DecodePixelWidth = 260 };
            CoverImage.Opacity = 1;
            FallbackTitle.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private void ResetCard()
    {
        ++_posterGeneration;
        CoverImage.Source = null;
        CoverImage.Opacity = 0;
        FallbackTitle.Visibility = Visibility.Visible;
        TitleText.Text = "";
        FallbackTitle.Text = "";
        TimeLeftText.Text = "";
        ProgressFill.Width = 0;
        _isPointerOver = false;
        _isKeyboardFocusWithin = false;
        SetInteractiveVisualState(false);
    }

    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = ActivateCard();
    }

    private void Card_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), this)) return;
        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
            e.Handled = ActivateCard();
    }

    private bool ActivateCard()
    {
        if (MediaItem == null) return false;
        App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(MediaItem.ContentId);
        return true;
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        SetInteractiveVisualState(true);
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        if (!_isKeyboardFocusWithin) SetInteractiveVisualState(false);
    }

    private void Card_GotFocus(object sender, RoutedEventArgs e)
    {
        _isKeyboardFocusWithin = true;
        SetInteractiveVisualState(true);
    }

    private void Card_LostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var focused = XamlRoot == null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            _isKeyboardFocusWithin = IsDescendantOf(focused, this);
            if (!_isKeyboardFocusWithin && !_isPointerOver) SetInteractiveVisualState(false);
        });
    }

    private void SetInteractiveVisualState(bool active)
    {
        CardHoverTransform.TranslateY = active ? -4 : 0;
        CoverHoverTransform.ScaleX = active ? 1.06 : 1;
        CoverHoverTransform.ScaleY = active ? 1.06 : 1;
        HoverBrighten.Opacity = active ? 1 : 0;
        MoreButton.Opacity = active ? 1 : 0;
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }
        return false;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MediaItem == null) return;
        MediaItemMenu.Build(MediaItem, MediaItemMenu.Surface.Default).ShowAt(MoreButton);
    }

    private void Card_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (MediaItem == null) return;
        MediaItemMenu.Build(MediaItem, MediaItemMenu.Surface.Default).ShowAt(this);
        args.Handled = true;
    }

    private static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{Math.Max(1, span.Minutes)}m";
    }
}
