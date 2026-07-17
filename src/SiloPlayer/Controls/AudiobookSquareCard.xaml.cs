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
    }

    private static void OnMediaItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AudiobookSquareCard card && e.NewValue is MediaItem item)
            card.Bind(item);
    }

    private void Bind(MediaItem item)
    {
        TitleText.Text = item.Title;
        FallbackTitle.Text = item.Title;
        var position = Math.Max(0, item.PositionSeconds ?? 0);
        var duration = Math.Max(0, item.DurationSeconds ?? 0);
        ProgressFill.Width = duration > 0 ? 168 * Math.Clamp(position / duration, 0, 1) : 0;
        var remaining = Math.Max(0, duration - position);
        TimeLeftText.Text = remaining > 0 ? $"{FormatDuration(remaining)} left" : "";
        if (!string.IsNullOrWhiteSpace(item.PosterUrl)) _ = LoadPosterAsync(item);
    }

    private async Task LoadPosterAsync(MediaItem item)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var path = await imageService.GetImageDiskPathAsync(item.ContentId, "poster", item.PosterUrl!, App.Services.GetRequiredService<HttpClient>());
            if (!ReferenceEquals(item, MediaItem) || string.IsNullOrWhiteSpace(path)) return;
            CoverImage.Source = new BitmapImage { UriSource = new Uri(path), DecodePixelWidth = 260 };
            CoverImage.Opacity = 1;
            FallbackTitle.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (MediaItem == null) return;
        App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(MediaItem.ContentId);
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        CardHoverTransform.TranslateY = -4;
        CoverHoverTransform.ScaleX = 1.06;
        CoverHoverTransform.ScaleY = 1.06;
        HoverBrighten.Opacity = 1;
        MoreButton.Opacity = 1;
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        CardHoverTransform.TranslateY = 0;
        CoverHoverTransform.ScaleX = 1;
        CoverHoverTransform.ScaleY = 1;
        HoverBrighten.Opacity = 0;
        MoreButton.Opacity = 0;
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
