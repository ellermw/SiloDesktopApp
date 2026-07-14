using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

public sealed partial class NowListeningHero : UserControl
{
    private MediaItem? _item;
    private double _progress;

    public NowListeningHero()
    {
        InitializeComponent();
    }

    public void Bind(MediaItem item)
    {
        _item = item;
        TitleText.Text = item.Title;
        var authors = item.Audiobook?.Authors.Select(person => person.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList() ?? [];
        var narrators = item.Audiobook?.Narrators.Select(person => person.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList() ?? [];
        var credits = new List<string>();
        if (authors.Count > 0) credits.Add(string.Join(", ", authors));
        if (narrators.Count > 0) credits.Add($"Narrated by {string.Join(", ", narrators)}");
        CreditsText.Text = string.Join(" · ", credits);
        CreditsText.Visibility = credits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var position = Math.Max(0, item.PositionSeconds ?? 0);
        var duration = Math.Max(0, item.DurationSeconds ?? item.Audiobook?.TotalDurationSeconds ?? 0);
        _progress = duration > 0 ? Math.Clamp(position / duration, 0, 1) : 0;
        ResumeText.Text = position > 0 ? "Resume" : "Listen";
        PositionText.Text = duration > 0 ? $"{FormatDuration(position)} of {FormatDuration(duration)}" : "";
        TimeLeftText.Text = duration > position ? $"{FormatDuration(duration - position)} left" : "";
        UpdateProgressWidth();

        if (!string.IsNullOrWhiteSpace(item.PosterUrl))
            _ = LoadPosterAsync(item);
    }

    private async Task LoadPosterAsync(MediaItem item)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var path = await imageService.GetImageDiskPathAsync(item.ContentId, "poster", item.PosterUrl!, httpClient);
            if (_item != item || string.IsNullOrWhiteSpace(path)) return;
            var image = new BitmapImage { UriSource = new Uri(path), DecodePixelWidth = 560 };
            CoverImage.Source = image;
            BackgroundImage.Source = image;
        }
        catch { }
    }

    private void ProgressTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateProgressWidth();

    private void UpdateProgressWidth()
    {
        if (ProgressTrack.ActualWidth > 0)
            ProgressFill.Width = ProgressTrack.ActualWidth * _progress;
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (_item == null) return;
        _ = App.Services.GetRequiredService<Services.PlayerService>().PlayAsync(_item.ContentId);
    }

    private void MoreInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_item == null) return;
        App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(_item.ContentId);
    }

    private static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{Math.Max(1, span.Minutes)}m";
    }
}
