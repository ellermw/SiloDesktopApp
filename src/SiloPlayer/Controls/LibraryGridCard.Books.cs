using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

public sealed partial class LibraryGridCard
{
    private readonly TextBlock _authorText = new() { Visibility = Visibility.Collapsed };
    private readonly Border _listeningTrack = new() { Visibility = Visibility.Collapsed };
    private readonly Border _listeningFill = new();

    private void InitializeBookPresentation(Grid posterHost)
    {
        _authorText.FontSize = 12;
        _authorText.Foreground = Brush("SecondaryTextBrush");
        _authorText.TextTrimming = TextTrimming.CharacterEllipsis;
        _authorText.MaxLines = 1;
        SetLeft(_authorText, 4);
        Children.Add(_authorText);
        _listeningTrack.Height = 3;
        _listeningTrack.CornerRadius = new CornerRadius(2);
        _listeningTrack.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(140, 0, 0, 0));
        _listeningTrack.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(90, 0, 0, 0));
        _listeningTrack.BorderThickness = new Thickness(.5);
        _listeningTrack.HorizontalAlignment = HorizontalAlignment.Left;
        _listeningTrack.VerticalAlignment = VerticalAlignment.Bottom;
        _listeningTrack.IsHitTestVisible = false;
        _listeningFill.Height = 3;
        _listeningFill.CornerRadius = new CornerRadius(2);
        _listeningFill.Background = Brush("AccentBrush");
        _listeningFill.HorizontalAlignment = HorizontalAlignment.Left;
        _listeningTrack.Child = _listeningFill;
        posterHost.Children.Add(_listeningTrack);
    }

    private static double? ListeningRatio(MediaItem? item) => item?.Type == "audiobook" && item.UserState?.Played != true
        && item.PositionSeconds is > 0 && item.DurationSeconds is > 0
        && double.IsFinite(item.PositionSeconds.Value) && double.IsFinite(item.DurationSeconds.Value)
            ? Math.Clamp(item.PositionSeconds.Value / item.DurationSeconds.Value, 0, 1) : null;

    private static string? ListeningSubtitle(MediaItem item)
    {
        if (item.Type != "audiobook") return null;
        if (ListeningRatio(item) != null)
        {
            var minutes = (int)Math.Clamp(Math.Floor((item.DurationSeconds!.Value - item.PositionSeconds!.Value) / 60 + .5), 1, int.MaxValue);
            return AppStrings.Get("web.audiobooks.duration.left", new Dictionary<string, object?> { ["duration"] = MediaItemCardActions.Runtime(minutes) });
        }
        return item.Runtime > 0 ? MediaItemCardActions.Runtime(item.Runtime) : null;
    }

    private void BindLibrarySubtitle(MediaItem item) => MediaItemCardActions.BindText(_subtitleText, "Text",
        element => element.Text = ReferenceEquals(MediaItem, item)
            ? MediaItemCardActions.CardSubtitle(item, SortKey, ListeningSubtitle(item)) : _subtitleText.Text);

    private void UpdateBookPresentation()
    {
        var authors = MediaItem is { Type: "audiobook", Authors.Count: > 0 } item && SortKey != "author"
            ? string.Join(", ", item.Authors) : "";
        _authorText.Text = authors;
        _authorText.Visibility = authors.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _listeningTrack.Visibility = ListeningRatio(MediaItem) != null ? Visibility.Visible : Visibility.Collapsed;
        SetTop(_subtitleText, _posterHeight + (_authorText.Visibility == Visibility.Visible || _episodeTitleText.Visibility == Visibility.Visible ? 56 : 34));
        UpdateBookLayout();
    }

    private void UpdateBookLayout()
    {
        _authorText.Width = Math.Max(0, Width - 8);
        SetTop(_authorText, _posterHeight + 34);
        var layout = MediaCardProgressGeometry.Calculate(Width - 2, ListeningRatio(MediaItem) ?? 0, episodeCard: false);
        _listeningTrack.Width = layout.TrackWidth;
        _listeningTrack.Margin = new Thickness(layout.HorizontalInset, 0, 0, layout.BottomInset);
        _listeningFill.Width = layout.FillWidth;
    }

    private void ClearBookPresentation()
    {
        _authorText.Text = "";
        _authorText.Visibility = Visibility.Collapsed;
        _listeningTrack.Visibility = Visibility.Collapsed;
        _listeningFill.Width = 0;
    }
}
