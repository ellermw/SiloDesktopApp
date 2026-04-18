using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using ContinuumPlayer.Core.Api;

namespace ContinuumPlayer.Controls;

/// <summary>
/// Modal subtitle-search dialog. Calls POST /subtitles/search with the selected
/// language, renders results with provider/release/score/HI badges, and lets
/// the user download any result via POST /subtitles/download. On successful
/// download, <see cref="SubtitleDownloaded"/> fires so the caller can reload
/// subtitles in the player.
///
/// Mirrors upstream web/src/pages/ItemDetail/components/SubtitleSearchDialog.tsx.
/// </summary>
public sealed partial class SubtitleSearchDialog : ContentDialog
{
    private readonly PlaybackApi _playbackApi;
    private readonly int _mediaFileId;

    /// <summary>Fired after a subtitle is successfully downloaded. Caller
    /// should reload/refresh the active subtitle list.</summary>
    public event Action? SubtitleDownloaded;

    public SubtitleSearchDialog(int mediaFileId, string? defaultLanguage = null)
    {
        _playbackApi = App.Services.GetRequiredService<PlaybackApi>();
        _mediaFileId = mediaFileId;
        this.InitializeComponent();

        if (!string.IsNullOrEmpty(defaultLanguage))
            SelectLanguageByTag(defaultLanguage);
    }

    private void SelectLanguageByTag(string tag)
    {
        foreach (var item in LanguageComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                LanguageComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private string SelectedLanguage =>
        (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchProgress.IsActive = true;
        SearchButton.IsEnabled = false;
        EmptyStateText.Visibility = Visibility.Collapsed;
        ResultsList.Items.Clear();
        StatusText.Text = $"Searching {SelectedLanguage.ToUpperInvariant()} subtitles…";

        try
        {
            var result = await _playbackApi.SearchSubtitlesAsync(_mediaFileId, [SelectedLanguage]);
            if (result.Results.Count == 0)
            {
                StatusText.Text = "No results.";
                EmptyStateText.Text = "No subtitles found in this language.";
                EmptyStateText.Visibility = Visibility.Visible;
                return;
            }

            foreach (var r in result.Results.OrderByDescending(x => x.Score))
                ResultsList.Items.Add(BuildResultRow(r));
            StatusText.Text = $"{result.Results.Count} result(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            SearchProgress.IsActive = false;
            SearchButton.IsEnabled = true;
        }
    }

    private FrameworkElement BuildResultRow(SubtitleSearchResult r)
    {
        var releaseText = new TextBlock
        {
            Text = string.IsNullOrEmpty(r.ReleaseName) ? $"{r.Provider} · {r.Language.ToUpperInvariant()}" : r.ReleaseName,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var metaPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        metaPanel.Children.Add(new TextBlock
        {
            Text = $"{r.Provider.ToUpperInvariant()} · {r.Language.ToUpperInvariant()} · score {r.Score:0.00}",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        if (r.HearingImpaired)
        {
            metaPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0x60, 0xA5, 0xFA)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 0, 5, 0),
                Child = new TextBlock
                {
                    Text = "HI",
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                },
            });
        }

        var textStack = new StackPanel { Spacing = 2, Children = { releaseText, metaPanel } };
        Grid.SetColumn(textStack, 0);

        var downloadBtn = new Button
        {
            Content = "Download",
            VerticalAlignment = VerticalAlignment.Center,
        };
        var captured = r;
        downloadBtn.Click += async (_, _) => await DownloadAsync(captured, downloadBtn);
        Grid.SetColumn(downloadBtn, 1);

        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(4, 6, 4, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(textStack);
        grid.Children.Add(downloadBtn);
        return grid;
    }

    private async Task DownloadAsync(SubtitleSearchResult r, Button btn)
    {
        btn.IsEnabled = false;
        btn.Content = "Downloading…";
        StatusText.Text = $"Downloading {r.Language.ToUpperInvariant()} from {r.Provider}…";
        try
        {
            await _playbackApi.DownloadSubtitleAsync(_mediaFileId, r.Provider, r.SubtitleId, r.Language, r.Format);
            btn.Content = "Downloaded";
            StatusText.Text = $"Downloaded {r.Language.ToUpperInvariant()} · {r.Provider}.";
            try { SubtitleDownloaded?.Invoke(); } catch { }
        }
        catch (Exception ex)
        {
            btn.IsEnabled = true;
            btn.Content = "Retry";
            StatusText.Text = $"Download failed: {ex.Message}";
        }
    }
}
