using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
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
    private int _uploadSelectionVersion;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _uploadDetectionCts;
    private int _searchVersion;
    private bool _downloadInProgress;
    private bool _uploadInProgress;

    /// <summary>
    /// Optional owner supplied by the playback dialog host. File pickers must
    /// use the foreground modal window rather than the obscured app shell.
    /// </summary>
    public IntPtr HostWindowHandle { get; set; }

    /// <summary>Fired after a subtitle is successfully downloaded or uploaded.
    /// The returned server subtitle ID lets the caller refresh and select the
    /// exact track without recreating the active playback session.</summary>
    public event Action<int?>? SubtitleDownloaded;

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

        Opened += (_, _) => LanguageComboBox.Focus(FocusState.Programmatic);
        Closed += (_, _) =>
        {
            _lifetimeCts.Cancel();
            _uploadDetectionCts?.Cancel();
        };
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

            var hwnd = HostWindowHandle != IntPtr.Zero
                ? HostWindowHandle
                : WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            await LoadUploadFileAsync(file);
        }
        catch (Exception ex)
        {
            UploadStatusText.Text = $"Could not read subtitle file: {ex.Message}";
            UploadButton.IsEnabled = false;
        }
    }

    private void UploadDropTarget_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;

        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Drop subtitle file";
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    private async void UploadDropTarget_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            var file = items.OfType<Windows.Storage.StorageFile>().FirstOrDefault();
            if (file == null) return;
            await LoadUploadFileAsync(file);
        }
        catch (Exception ex)
        {
            UploadStatusText.Text = $"Could not read subtitle file: {ex.Message}";
            UploadButton.IsEnabled = false;
        }
    }

    private async Task LoadUploadFileAsync(Windows.Storage.StorageFile file)
    {
        var selectionVersion = Interlocked.Increment(ref _uploadSelectionVersion);
        _uploadFileBytes = null;
        _uploadFileName = null;
        UploadButton.IsEnabled = false;

        var extension = Path.GetExtension(file.Name);
        if (!IsAcceptedSubtitleExtension(extension))
        {
            UploadStatusText.Text = "Unsupported file type. Use SRT, VTT, ASS, SSA, or SUB.";
            UploadButton.IsEnabled = false;
            return;
        }

        var properties = await file.GetBasicPropertiesAsync();
        if ((long)properties.Size > MaxSubtitleUploadBytes)
        {
            UploadStatusText.Text = "Subtitle file is larger than 5 MB.";
            UploadButton.IsEnabled = false;
            return;
        }

        var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
        if (selectionVersion != Volatile.Read(ref _uploadSelectionVersion))
            return;

        _uploadFileBytes = buffer.ToArray();
        _uploadFileName = file.Name;
        _uploadContentType = GuessSubtitleContentType(file.Name, file.ContentType);
        UploadFileText.Text = file.Name;
        UploadButton.IsEnabled = true;
        await DetectUploadLanguageAsync(selectionVersion);
    }

    private async Task DetectUploadLanguageAsync(int selectionVersion)
    {
        if (_uploadFileBytes == null || string.IsNullOrWhiteSpace(_uploadFileName)) return;

        _uploadDetectionCts?.Cancel();
        _uploadDetectionCts?.Dispose();
        _uploadDetectionCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var detectionToken = _uploadDetectionCts.Token;

        BrowseUploadButton.IsEnabled = false;
        UploadButton.IsEnabled = false;
        UploadStatusText.Text = "Detecting language...";
        try
        {
            var detection = await _playbackApi.DetectSubtitleLanguageAsync(
                _uploadFileName,
                _uploadFileBytes,
                _uploadContentType,
                SelectedUploadLanguage,
                detectionToken);

            if (selectionVersion != Volatile.Read(ref _uploadSelectionVersion))
                return;

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
        catch (OperationCanceledException) when (detectionToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            UploadStatusText.Text = $"Language detection failed: {ex.Message}";
        }
        finally
        {
            if (selectionVersion == Volatile.Read(ref _uploadSelectionVersion))
            {
                BrowseUploadButton.IsEnabled = true;
                UploadButton.IsEnabled = _uploadFileBytes != null;
            }
        }
    }

    private static bool IsAcceptedSubtitleExtension(string? extension)
        => extension?.ToLowerInvariant() is ".srt" or ".vtt" or ".ass" or ".ssa" or ".sub";

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_uploadInProgress || _downloadInProgress ||
            _uploadFileBytes == null || string.IsNullOrWhiteSpace(_uploadFileName))
            return;

        _uploadInProgress = true;
        UploadButton.IsEnabled = false;
        BrowseUploadButton.IsEnabled = false;
        ResultsList.IsEnabled = false;
        UploadStatusText.Text = "Uploading subtitle...";
        try
        {
            var response = await _playbackApi.UploadSubtitleAsync(
                _mediaFileId,
                _uploadFileName,
                _uploadFileBytes,
                _uploadContentType,
                SelectedUploadLanguage,
                languageOverride: true,
                releaseName: Path.GetFileNameWithoutExtension(_uploadFileName),
                hearingImpaired: UploadHearingImpairedToggle.IsOn,
                ct: _lifetimeCts.Token);

            UploadStatusText.Text = "Subtitle uploaded.";
            StatusText.Text = "Subtitle uploaded.";
            try { SubtitleDownloaded?.Invoke(GetDownloadedSubtitleId(response)); } catch { }
            Hide();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            UploadStatusText.Text = $"Upload failed: {ex.Message}";
        }
        finally
        {
            _uploadInProgress = false;
            if (!_lifetimeCts.IsCancellationRequested)
            {
                BrowseUploadButton.IsEnabled = true;
                UploadButton.IsEnabled = _uploadFileBytes != null;
                ResultsList.IsEnabled = true;
            }
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadInProgress || _uploadInProgress)
            return;

        var searchVersion = Interlocked.Increment(ref _searchVersion);
        SearchProgress.IsActive = true;
        SearchButton.IsEnabled = false;
        EmptyStateText.Visibility = Visibility.Collapsed;
        ResultsList.Items.Clear();
        StatusText.Text = $"Searching {SelectedLanguage.ToUpperInvariant()} subtitles…";

        try
        {
            var result = await _playbackApi.SearchSubtitlesAsync(
                _mediaFileId,
                [SelectedLanguage],
                _lifetimeCts.Token);
            if (searchVersion != Volatile.Read(ref _searchVersion))
                return;

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
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            if (searchVersion == Volatile.Read(ref _searchVersion) &&
                !_lifetimeCts.IsCancellationRequested)
            {
                SearchProgress.IsActive = false;
                SearchButton.IsEnabled = true;
            }
        }
    }

    private FrameworkElement BuildResultRow(SubtitleSearchResult r)
    {
        var releaseNames = SplitReleaseNames(r.ReleaseName);
        var primaryReleaseName = releaseNames.FirstOrDefault();
        var displayName = string.IsNullOrEmpty(primaryReleaseName)
            ? $"{r.Provider} · {r.Language.ToUpperInvariant()}"
            : primaryReleaseName;

        var scoreColor = r.Score >= 70
            ? Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)
            : r.Score >= 40
                ? Windows.UI.Color.FromArgb(0xFF, 0xEA, 0xB3, 0x08)
                : Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44);
        var scoreBadge = CreateBadge(
            Math.Round(r.Score).ToString("0"),
            scoreColor,
            fontSize: 11,
            fontWeight: FontWeights.Bold);

        var releaseText = new TextBlock
        {
            Text = displayName,
            FontSize = 12,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var provider = r.Provider.ToLowerInvariant() switch
        {
            "opensubtitles" => ("OS", Windows.UI.Color.FromArgb(0xFF, 0xEA, 0xB3, 0x08)),
            "subdl" => ("SDL", Windows.UI.Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6)),
            "subsource" => ("SS", Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44)),
            "upload" => ("UP", Windows.UI.Color.FromArgb(0xFF, 0xA8, 0x55, 0xF7)),
            _ => (r.Provider.ToUpperInvariant(), Windows.UI.Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF)),
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(scoreBadge, 0);
        Grid.SetColumn(releaseText, 1);
        grid.Children.Add(scoreBadge);
        grid.Children.Add(releaseText);

        if (r.HearingImpaired)
        {
            var hiBadge = CreateBadge(
                "HI",
                Windows.UI.Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF),
                fontSize: 9,
                fontWeight: FontWeights.Normal);
            Grid.SetColumn(hiBadge, 2);
            grid.Children.Add(hiBadge);
        }

        var downloads = new TextBlock
        {
            Text = $"↓{r.Downloads:N0}",
            FontSize = 10,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(downloads, 3);
        grid.Children.Add(downloads);

        var providerBadge = CreateBadge(provider.Item1, provider.Item2, 9, FontWeights.SemiBold);
        Grid.SetColumn(providerBadge, 4);
        grid.Children.Add(providerBadge);

        var rowButton = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
        };
        var captured = r;
        rowButton.Click += async (_, _) => await DownloadAsync(captured, rowButton);
        AutomationProperties.SetName(rowButton, $"Download {displayName}");
        return rowButton;
    }

    private async Task DownloadAsync(SubtitleSearchResult r, Button btn)
    {
        if (_downloadInProgress || _uploadInProgress)
            return;

        _downloadInProgress = true;
        btn.IsEnabled = false;
        ResultsList.IsEnabled = false;
        SearchButton.IsEnabled = false;
        UploadButton.IsEnabled = false;
        BrowseUploadButton.IsEnabled = false;
        StatusText.Text = $"Downloading {r.Language.ToUpperInvariant()} from {r.Provider}…";
        try
        {
            var response = await _playbackApi.DownloadSubtitleAsync(
                _mediaFileId,
                r,
                _lifetimeCts.Token);
            StatusText.Text = $"Downloaded {r.Language.ToUpperInvariant()} · {r.Provider}.";
            try { SubtitleDownloaded?.Invoke(GetDownloadedSubtitleId(response)); } catch { }
            Hide();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Download failed: {ex.Message}";
        }
        finally
        {
            _downloadInProgress = false;
            if (!_lifetimeCts.IsCancellationRequested)
            {
                ResultsList.IsEnabled = true;
                SearchButton.IsEnabled = true;
                UploadButton.IsEnabled = _uploadFileBytes != null;
                BrowseUploadButton.IsEnabled = true;
                btn.IsEnabled = true;
            }
        }
    }

    private static int? GetDownloadedSubtitleId(SubtitleDownloadResponse response)
    {
        var id = response.Subtitle?.Id ?? response.Id;
        return id > 0 ? id : null;
    }

    private static Border CreateBadge(
        string text,
        Windows.UI.Color color,
        double fontSize,
        Windows.UI.Text.FontWeight fontWeight)
    {
        return new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x55, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = fontWeight,
                Foreground = new SolidColorBrush(color),
            },
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

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
