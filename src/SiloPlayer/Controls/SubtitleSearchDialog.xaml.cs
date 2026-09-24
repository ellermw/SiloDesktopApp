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
    private readonly SiloApiClient _apiClient;
    private readonly ApiRequestContext _requestContext;
    private bool _providerStatusLoaded;
    private bool _onlineSearchEnabled = true;
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
    private readonly bool _playerMode;

    /// <summary>
    /// Optional owner supplied by the playback dialog host. File pickers must
    /// use the foreground modal window rather than the obscured app shell.
    /// </summary>
    public IntPtr HostWindowHandle { get; set; }

    /// <summary>Fired after a subtitle is successfully downloaded or uploaded.
    /// The returned server subtitle ID lets the caller refresh and select the
    /// exact track without recreating the active playback session.</summary>
    public event Action<long?>? SubtitleDownloaded;

    public SubtitleSearchDialog(
        int mediaFileId,
        string? defaultLanguage = null,
        bool playerMode = true,
        string? title = null,
        string? versionLabel = null)
    {
        _playbackApi = App.Services.GetRequiredService<PlaybackApi>();
        _apiClient = App.Services.GetRequiredService<SiloApiClient>();
        _requestContext = _apiClient.CaptureContext();
        _mediaFileId = mediaFileId;
        _playerMode = playerMode;
        this.InitializeComponent();

        if (!playerMode)
        {
            DialogRoot.Width = 720;
            DialogRoot.MaxWidth = 720;
            DialogDescriptionText.Text = string.Join(" · ", new[] { title, versionLabel }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            DialogDescriptionText.Visibility = string.IsNullOrWhiteSpace(DialogDescriptionText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            EmptyStateText.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrEmpty(defaultLanguage))
        {
            SelectLanguageByTag(LanguageComboBox, defaultLanguage);
            SelectLanguageByTag(UploadLanguageComboBox, defaultLanguage);
        }

        Opened += OnOpened;
        Closed += (_, _) =>
        {
            _lifetimeCts.Cancel();
            _uploadDetectionCts?.Cancel();
        };
    }

    private bool HasCurrentContext => !_lifetimeCts.IsCancellationRequested &&
        _apiClient.IsCurrentContext(_requestContext);

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        try
        {
            var enabled = await _playbackApi.CanSearchSubtitlesAsync(_lifetimeCts.Token);
            if (!HasCurrentContext) { Hide(); return; }
            _onlineSearchEnabled = enabled;
            _providerStatusLoaded = true;
            var visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            OnlineSearchHeading.Visibility = visibility;
            OnlineSearchControls.Visibility = visibility;
            OnlineSearchResults.Visibility = visibility;
            SearchButton.IsEnabled = enabled && !_downloadInProgress && !_uploadInProgress;
        }
        catch (OperationCanceledException)
        {
            if (!_lifetimeCts.IsCancellationRequested) Hide();
        }
    }

    private void ClearMessages()
    {
        ErrorText.Text = "";
        ErrorBorder.Visibility = Visibility.Collapsed;
        WarningsPanel.Children.Clear();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void ShowWarnings(IEnumerable<string> warnings)
    {
        WarningsPanel.Children.Clear();
        foreach (var warning in warnings.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            WarningsPanel.Children.Add(new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x1A, 0xEA, 0xB3, 0x08)),
                CornerRadius = new CornerRadius(8),
                Child = new TextBlock
                {
                    Text = warning,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFD, 0xE0, 0x68)),
                    TextWrapping = TextWrapping.Wrap,
                },
            });
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
            if (!HasCurrentContext) return;
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
            if (!HasCurrentContext) return;
            UploadStatusText.Text = $"Could not read subtitle file: {ex.Message}";
            UploadButton.IsEnabled = false;
        }
    }

    private async Task LoadUploadFileAsync(Windows.Storage.StorageFile file)
    {
        if (!HasCurrentContext) return;
        var selectionVersion = Interlocked.Increment(ref _uploadSelectionVersion);
        _uploadFileBytes = null;
        _uploadFileName = null;
        UploadButton.IsEnabled = false;

        var extension = Path.GetExtension(file.Name);
        if (!IsAcceptedSubtitleExtension(extension))
        {
            UploadFileText.Text = "No file selected";
            UploadStatusText.Text = "Unsupported file type. Use SRT, VTT, ASS, SSA, or SUB.";
            UploadButton.IsEnabled = false;
            return;
        }

        var properties = await file.GetBasicPropertiesAsync();
        if ((long)properties.Size > MaxSubtitleUploadBytes)
        {
            UploadFileText.Text = "No file selected";
            UploadStatusText.Text = "Subtitle file is larger than 5 MB.";
            UploadButton.IsEnabled = false;
            return;
        }

        var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
        if (!HasCurrentContext || selectionVersion != Volatile.Read(ref _uploadSelectionVersion))
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
        if (!HasCurrentContext || _uploadFileBytes == null || string.IsNullOrWhiteSpace(_uploadFileName)) return;

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

            if (!HasCurrentContext || selectionVersion != Volatile.Read(ref _uploadSelectionVersion))
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
            if (!HasCurrentContext) return;
            UploadStatusText.Text = $"Language detection failed: {ex.Message}";
        }
        finally
        {
            if (HasCurrentContext && selectionVersion == Volatile.Read(ref _uploadSelectionVersion))
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
        if (!HasCurrentContext || _uploadInProgress || _downloadInProgress ||
            _uploadFileBytes == null || string.IsNullOrWhiteSpace(_uploadFileName))
            return;

        _uploadInProgress = true;
        ClearMessages();
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

            if (!HasCurrentContext) return;
            UploadStatusText.Text = "Subtitle uploaded.";
            StatusText.Text = "Subtitle uploaded.";
            try { SubtitleDownloaded?.Invoke(GetDownloadedSubtitleId(response)); } catch { }
            if (_playerMode)
            {
                Hide();
            }
            else
            {
                _uploadFileBytes = null;
                _uploadFileName = null;
                UploadFileText.Text = "No file selected";
                UploadButton.IsEnabled = false;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!HasCurrentContext) return;
            UploadStatusText.Text = $"Upload failed: {ex.Message}";
            ShowError($"Upload failed: {ex.Message}");
        }
        finally
        {
            _uploadInProgress = false;
            if (HasCurrentContext)
            {
                BrowseUploadButton.IsEnabled = true;
                UploadButton.IsEnabled = _uploadFileBytes != null;
                ResultsList.IsEnabled = true;
            }
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasCurrentContext || !_providerStatusLoaded || !_onlineSearchEnabled ||
            _downloadInProgress || _uploadInProgress)
            return;

        var searchVersion = Interlocked.Increment(ref _searchVersion);
        ClearMessages();
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
            if (!HasCurrentContext || searchVersion != Volatile.Read(ref _searchVersion))
                return;

            if (result.Results.Count == 0)
            {
                StatusText.Text = "No results.";
                EmptyStateText.Text = _playerMode
                    ? "No subtitles found in this language."
                    : "No subtitles found for this version and language.";
                EmptyStateText.Visibility = Visibility.Visible;
                ShowWarnings(result.Warnings);
                return;
            }

            foreach (var r in result.Results.OrderByDescending(x => x.Score))
                ResultsList.Items.Add(BuildResultRow(r));
            ShowWarnings(result.Warnings);
            StatusText.Text = $"{result.Results.Count} result(s).";
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!HasCurrentContext) return;
            StatusText.Text = "Search failed.";
            ShowError(ex.Message);
        }
        finally
        {
            if (searchVersion == Volatile.Read(ref _searchVersion) &&
                HasCurrentContext)
            {
                SearchProgress.IsActive = false;
                SearchButton.IsEnabled = _providerStatusLoaded && _onlineSearchEnabled;
            }
        }
    }

    private FrameworkElement BuildResultRow(SubtitleSearchResult r)
        => _playerMode ? BuildPlayerResultRow(r) : BuildDetailResultRow(r);

    private FrameworkElement BuildPlayerResultRow(SubtitleSearchResult r)
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

    private FrameworkElement BuildDetailResultRow(SubtitleSearchResult result)
    {
        var releaseNames = SplitReleaseNames(result.ReleaseName);
        var primaryReleaseName = releaseNames.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(primaryReleaseName))
            primaryReleaseName = $"{result.Provider} · {result.Language.ToUpperInvariant()}";

        var scoreColor = result.Score >= 70
            ? Windows.UI.Color.FromArgb(0xFF, 0x6E, 0xD9, 0x9A)
            : result.Score >= 40
                ? Windows.UI.Color.FromArgb(0xFF, 0xFD, 0xE0, 0x68)
                : Windows.UI.Color.FromArgb(0xFF, 0xFC, 0xA5, 0xA5);

        var scorePanel = new Border
        {
            Width = 48,
            MinHeight = 54,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x1A, scoreColor.R, scoreColor.G, scoreColor.B)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x4D, scoreColor.R, scoreColor.G, scoreColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 1,
                Children =
                {
                    new TextBlock
                    {
                        Text = Math.Round(result.Score).ToString("0"),
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(scoreColor),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = "SCORE",
                        FontSize = 9,
                        CharacterSpacing = 120,
                        Foreground = new SolidColorBrush(scoreColor),
                        Opacity = 0.72,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };
        AutomationProperties.SetName(scorePanel, $"Match score {Math.Round(result.Score):0}");

        var provider = result.Provider.ToLowerInvariant() switch
        {
            "opensubtitles" => ("OS", Windows.UI.Color.FromArgb(0xFF, 0xFD, 0xE0, 0x68)),
            "subdl" => ("SDL", Windows.UI.Color.FromArgb(0xFF, 0x7D, 0xC4, 0xFF)),
            "subsource" => ("SS", Windows.UI.Color.FromArgb(0xFF, 0xFC, 0xA5, 0xA5)),
            "upload" => ("UP", Windows.UI.Color.FromArgb(0xFF, 0xD8, 0xB4, 0xFE)),
            _ => (result.Provider.Length > 2 ? result.Provider[..2].ToUpperInvariant() : result.Provider.ToUpperInvariant(),
                Windows.UI.Color.FromArgb(0xFF, 0xC4, 0xC7, 0xCE)),
        };

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        badges.Children.Add(CreateBadge(provider.Item1, provider.Item2, 10, FontWeights.SemiBold));
        badges.Children.Add(CreateBadge(result.Format.ToUpperInvariant(), Windows.UI.Color.FromArgb(0xFF, 0xC4, 0xC7, 0xCE), 10, FontWeights.Normal));
        badges.Children.Add(CreateBadge(
            Services.PlayerService.LanguageCodeToName(result.Language),
            Windows.UI.Color.FromArgb(0xFF, 0xC4, 0xC7, 0xCE),
            10,
            FontWeights.Normal));
        if (result.HearingImpaired)
            badges.Children.Add(CreateBadge("HI", Windows.UI.Color.FromArgb(0xFF, 0xC4, 0xC7, 0xCE), 10, FontWeights.Normal));
        if (result.Downloads > 0)
        {
            badges.Children.Add(new TextBlock
            {
                Text = $"{result.Downloads:N0} {(result.Downloads == 1 ? "download" : "downloads")}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var releaseText = new TextBlock
        {
            Text = primaryReleaseName,
            FontSize = 13,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTipService.SetToolTip(releaseText, string.Join(Environment.NewLine, releaseNames));

        var releasePanel = new StackPanel { Spacing = 4 };
        releasePanel.Children.Add(releaseText);
        var extras = releaseNames.Skip(1).ToList();
        if (extras.Count > 0)
        {
            var extrasPanel = new StackPanel
            {
                Spacing = 2,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(8, 2, 0, 0),
            };
            foreach (var extra in extras)
            {
                extrasPanel.Children.Add(new TextBlock
                {
                    Text = extra,
                    FontSize = 12,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }

            var expandButton = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 2, 4, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                Content = $"⌄  {extras.Count} more variant{(extras.Count == 1 ? "" : "s")}",
                FontSize = 11,
            };
            expandButton.Click += (_, _) =>
            {
                var expanding = extrasPanel.Visibility != Visibility.Visible;
                extrasPanel.Visibility = expanding ? Visibility.Visible : Visibility.Collapsed;
                expandButton.Content = expanding
                    ? "⌃  Collapse"
                    : $"⌄  {extras.Count} more variant{(extras.Count == 1 ? "" : "s")}";
            };
            releasePanel.Children.Add(extrasPanel);
            releasePanel.Children.Add(expandButton);
        }

        var centerPanel = new StackPanel { Spacing = 6 };
        centerPanel.Children.Add(badges);
        centerPanel.Children.Add(releasePanel);

        var downloadButton = new Button
        {
            MinWidth = 120,
            Padding = new Thickness(14, 7, 14, 7),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new FontIcon { Glyph = "\uE896", FontSize = 14 },
                    new TextBlock { Text = "Download", VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        downloadButton.Click += async (_, _) => await DownloadAsync(result, downloadButton);
        AutomationProperties.SetName(downloadButton, $"Download {primaryReleaseName}");

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(scorePanel, 0);
        Grid.SetColumn(centerPanel, 1);
        Grid.SetColumn(downloadButton, 2);
        grid.Children.Add(scorePanel);
        grid.Children.Add(centerPanel);
        grid.Children.Add(downloadButton);

        return new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = grid,
        };
    }

    private async Task DownloadAsync(SubtitleSearchResult r, Button btn)
    {
        if (!HasCurrentContext || !_onlineSearchEnabled || _downloadInProgress || _uploadInProgress)
            return;

        _downloadInProgress = true;
        btn.IsEnabled = false;
        ResultsList.IsEnabled = false;
        SearchButton.IsEnabled = false;
        UploadButton.IsEnabled = false;
        BrowseUploadButton.IsEnabled = false;
        ClearMessages();
        StatusText.Text = $"Downloading {r.Language.ToUpperInvariant()} from {r.Provider}…";
        try
        {
            var response = await _playbackApi.DownloadSubtitleAsync(
                _mediaFileId,
                r,
                _lifetimeCts.Token);
            if (!HasCurrentContext) return;
            StatusText.Text = $"Downloaded {r.Language.ToUpperInvariant()} · {r.Provider}.";
            try { SubtitleDownloaded?.Invoke(GetDownloadedSubtitleId(response)); } catch { }
            if (_playerMode)
                Hide();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!HasCurrentContext) return;
            StatusText.Text = "Download failed.";
            ShowError(ex.Message);
        }
        finally
        {
            _downloadInProgress = false;
            if (HasCurrentContext)
            {
                ResultsList.IsEnabled = true;
                SearchButton.IsEnabled = _providerStatusLoaded && _onlineSearchEnabled;
                UploadButton.IsEnabled = _uploadFileBytes != null;
                BrowseUploadButton.IsEnabled = true;
                btn.IsEnabled = true;
            }
        }
    }

    private static long? GetDownloadedSubtitleId(SubtitleDownloadResponse response)
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
