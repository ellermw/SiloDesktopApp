using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using System.Runtime.InteropServices.WindowsRuntime;

namespace SiloPlayer.Controls;

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
    private const long MaxSubtitleUploadBytes = 5L * 1024L * 1024L;

    private readonly PlaybackApi _playbackApi;
    private readonly int _mediaFileId;
    private byte[]? _uploadFileBytes;
    private string? _uploadFileName;
    private string _uploadContentType = "application/octet-stream";

    /// <summary>Fired after a subtitle is successfully downloaded. Caller
    /// should reload/refresh the active subtitle list.</summary>
    public event Action? SubtitleDownloaded;

    public SubtitleSearchDialog(int mediaFileId, string? defaultLanguage = null)
    {
        _playbackApi = App.Services.GetRequiredService<PlaybackApi>();
        _mediaFileId = mediaFileId;
        this.InitializeComponent();

        if (!string.IsNullOrEmpty(defaultLanguage))
        {
            SelectLanguageByTag(LanguageComboBox, defaultLanguage);
            SelectLanguageByTag(UploadLanguageComboBox, defaultLanguage);
        }
    }

    private static void SelectLanguageByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private string SelectedLanguage =>
        (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";

    private string SelectedUploadLanguage =>
        (UploadLanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? SelectedLanguage;

    private async void BrowseUploadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            foreach (var ext in new[] { ".srt", ".vtt", ".ass", ".ssa", ".sub" })
                picker.FileTypeFilter.Add(ext);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var properties = await file.GetBasicPropertiesAsync();
            if ((long)properties.Size > MaxSubtitleUploadBytes)
            {
                UploadStatusText.Text = "Subtitle file is larger than 5 MB.";
                UploadButton.IsEnabled = false;
                return;
            }

            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            _uploadFileBytes = buffer.ToArray();
            _uploadFileName = file.Name;
            _uploadContentType = GuessSubtitleContentType(file.Name, file.ContentType);
            UploadFileText.Text = file.Name;
            UploadButton.IsEnabled = true;

            await DetectUploadLanguageAsync();
        }
        catch (Exception ex)
        {
            UploadStatusText.Text = $"Could not read subtitle file: {ex.Message}";
            UploadButton.IsEnabled = false;
        }
    }

    private async Task DetectUploadLanguageAsync()
    {
        if (_uploadFileBytes == null || string.IsNullOrWhiteSpace(_uploadFileName)) return;

        BrowseUploadButton.IsEnabled = false;
        UploadButton.IsEnabled = false;
        UploadStatusText.Text = "Detecting language...";
        try
        {
            var detection = await _playbackApi.DetectSubtitleLanguageAsync(
                _uploadFileName,
                _uploadFileBytes,
                _uploadContentType,
                SelectedUploadLanguage);

            if (!string.IsNullOrWhiteSpace(detection.Language))
            {
                SelectLanguageByTag(UploadLanguageComboBox, detection.Language);
                UploadStatusText.Text = $"Detected {Services.PlayerService.LanguageCodeToName(detection.Language)} from {DetectionSourceLabel(detection.Source)}.";
            }
            else
            {
                UploadStatusText.Text = "Language detection did not return a language. Pick one before uploading.";
            }
        }
        catch (Exception ex)
        {
            UploadStatusText.Text = $"Language detection failed: {ex.Message}";
        }
        finally
        {
            BrowseUploadButton.IsEnabled = true;
            UploadButton.IsEnabled = _uploadFileBytes != null;
        }
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_uploadFileBytes == null || string.IsNullOrWhiteSpace(_uploadFileName)) return;

        UploadButton.IsEnabled = false;
        BrowseUploadButton.IsEnabled = false;
        UploadStatusText.Text = "Uploading subtitle...";
        try
        {
            await _playbackApi.UploadSubtitleAsync(
                _mediaFileId,
                _uploadFileName,
                _uploadFileBytes,
                _uploadContentType,
                SelectedUploadLanguage,
                languageOverride: true,
                releaseName: Path.GetFileNameWithoutExtension(_uploadFileName),
                hearingImpaired: UploadHearingImpairedToggle.IsOn);

            UploadStatusText.Text = "Subtitle uploaded.";
            StatusText.Text = "Subtitle uploaded.";
            try { SubtitleDownloaded?.Invoke(); } catch { }
        }
        catch (Exception ex)
        {
            UploadStatusText.Text = $"Upload failed: {ex.Message}";
            UploadButton.IsEnabled = true;
        }
        finally
        {
            BrowseUploadButton.IsEnabled = true;
        }
    }

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
            StatusText.Text = result.Warnings.Count > 0
                ? $"{result.Results.Count} result(s). {string.Join(" ", result.Warnings)}"
                : $"{result.Results.Count} result(s).";
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
        var releaseNames = SplitReleaseNames(r.ReleaseName);
        var primaryReleaseName = releaseNames.FirstOrDefault();
        var displayName = string.IsNullOrEmpty(primaryReleaseName)
            ? $"{r.Provider} · {r.Language.ToUpperInvariant()}"
            : primaryReleaseName;
        var downloadsText = r.Downloads > 0 ? $" · {r.Downloads:N0} downloads" : "";
        var releaseText = new TextBlock
        {
            Text = string.IsNullOrEmpty(r.ReleaseName) ? $"{r.Provider} · {r.Language.ToUpperInvariant()}" : r.ReleaseName,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        releaseText.Text = displayName;
        var metaPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        metaPanel.Children.Add(new TextBlock
        {
            Text = $"{r.Provider.ToUpperInvariant()} · {r.Language.ToUpperInvariant()} · score {r.Score:0.00}",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        if (!string.IsNullOrEmpty(downloadsText))
        {
            metaPanel.Children.Add(new TextBlock
            {
                Text = downloadsText.TrimStart(' ', '·'),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }
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
        if (releaseNames.Count > 1)
        {
            textStack.Children.Add(new TextBlock
            {
                Text = $"{releaseNames.Count - 1} more variant(s)",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
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
            await _playbackApi.DownloadSubtitleAsync(_mediaFileId, r);
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

    private static List<string> SplitReleaseNames(string? raw)
        => (raw ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

    private static string DetectionSourceLabel(string? source) => source switch
    {
        "filename" => "filename",
        "metadata" => "metadata",
        "content" => "content",
        "manual" => "manual selection",
        _ => "detection",
    };

    private static string GuessSubtitleContentType(string fileName, string? pickerContentType)
    {
        if (!string.IsNullOrWhiteSpace(pickerContentType) &&
            !string.Equals(pickerContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return pickerContentType;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".vtt" => "text/vtt",
            ".srt" => "application/x-subrip",
            ".ass" or ".ssa" => "text/x-ssa",
            ".sub" => "application/octet-stream",
            _ => "application/octet-stream",
        };
    }
}
