using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminSubtitlesPage : Page
{
    private readonly AdminApi _adminApi;
    private readonly ToastService _toastService;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private CancellationTokenSource? _loadCts;
    private bool _ready;
    private int _page;
    private int _pageSize = 25;
    private int _total;
    private string _providerFilter = "";
    private static readonly (string Code, string Label)[] LanguageOptions =
    [
        ("ar", "Arabic"), ("eu", "Basque"), ("bn", "Bengali"), ("bg", "Bulgarian"),
        ("ca", "Catalan"), ("zh", "Chinese"), ("hr", "Croatian"), ("cs", "Czech"),
        ("da", "Danish"), ("nl", "Dutch"), ("en", "English"), ("fa", "Persian"),
        ("fi", "Finnish"), ("fr", "French"), ("gl", "Galician"), ("de", "German"),
        ("el", "Greek"), ("he", "Hebrew"), ("hi", "Hindi"), ("hu", "Hungarian"),
        ("id", "Indonesian"), ("it", "Italian"), ("ja", "Japanese"), ("ko", "Korean"),
        ("ms", "Malay"), ("no", "Norwegian"), ("pl", "Polish"), ("pt", "Portuguese"),
        ("ro", "Romanian"), ("ru", "Russian"), ("sk", "Slovak"), ("sl", "Slovenian"),
        ("es", "Spanish"), ("sv", "Swedish"), ("ta", "Tamil"), ("te", "Telugu"),
        ("th", "Thai"), ("tr", "Turkish"), ("uk", "Ukrainian"), ("vi", "Vietnamese"),
    ];

    public AdminSubtitlesPage()
    {
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _toastService = App.Services.GetRequiredService<ToastService>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        UpdateProviderFilterButtons();

        Loaded += AdminSubtitlesPage_Loaded;
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        Unloaded += (_, _) =>
        {
            _searchTimer.Stop();
            _loadCts?.Cancel();
        };
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            _page = 0;
            await LoadSubtitlesAsync();
        };
    }

    private async void AdminSubtitlesPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout();
        if (!_ready)
        {
            PopulateLanguageFilter();
            _ready = true;
            await Task.WhenAll(LoadUsersAsync(), LoadSubtitlesAsync());
            return;
        }
        await LoadSubtitlesAsync();
    }

    private void ApplyResponsiveLayout()
    {
        var width = ActualWidth;
        var side = width < 640 ? 16 : width < 1024 ? 24 : 40;
        SubtitlesPageShell.Padding = new Thickness(side, width < 640 ? 16 : 24, side, 40);
        SubtitlesTitle.FontSize = width < 600 ? 34 : width < 860 ? 40 : 48;
        foreach (var row in SubtitlesListView.Items.OfType<Grid>())
        {
            var actions = row.Children.OfType<StackPanel>().FirstOrDefault(panel => Grid.GetColumn(panel) == 9);
            if (actions is not null && width < 760) actions.Opacity = 1;
        }

        var stats = new FrameworkElement[] { TotalStoredStat, UploadsStat, ProviderDownloadsStat, LanguagesStat };
        var statColumns = width >= 1280 ? 4 : width >= 640 ? 2 : 1;
        SubtitleStatsGrid.ColumnDefinitions.Clear();
        SubtitleStatsGrid.RowDefinitions.Clear();
        for (var column = 0; column < statColumns; column++)
            SubtitleStatsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < (int)Math.Ceiling(stats.Length / (double)statColumns); row++)
            SubtitleStatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < stats.Length; index++)
        {
            Grid.SetColumn(stats[index], index % statColumns);
            Grid.SetRow(stats[index], index / statColumns);
        }

        SubtitleFiltersGrid.ColumnDefinitions.Clear();
        SubtitleFiltersGrid.RowDefinitions.Clear();
        if (width >= 1280)
        {
            foreach (var length in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(180), new GridLength(200), GridLength.Auto })
                SubtitleFiltersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
            SubtitleFiltersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            PlaceFilter(SearchBox, 0, 0); PlaceFilter(ProviderFiltersPanel, 1, 0);
            PlaceFilter(LanguageFilterComboBox, 2, 0); PlaceFilter(UserFilterComboBox, 3, 0); PlaceFilter(ResetFiltersButton, 4, 0);
        }
        else if (width >= 760)
        {
            SubtitleFiltersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            SubtitleFiltersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            SubtitleFiltersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var row = 0; row < 3; row++) SubtitleFiltersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            PlaceFilter(SearchBox, 0, 0, 3); PlaceFilter(ProviderFiltersPanel, 0, 1, 3);
            PlaceFilter(LanguageFilterComboBox, 0, 2); PlaceFilter(UserFilterComboBox, 1, 2); PlaceFilter(ResetFiltersButton, 2, 2);
        }
        else
        {
            SubtitleFiltersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var row = 0; row < 5; row++) SubtitleFiltersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            PlaceFilter(SearchBox, 0, 0); PlaceFilter(ProviderFiltersPanel, 0, 1);
            PlaceFilter(LanguageFilterComboBox, 0, 2); PlaceFilter(UserFilterComboBox, 0, 3); PlaceFilter(ResetFiltersButton, 0, 4);
            LanguageFilterComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            UserFilterComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        SubtitlePaginationGrid.ColumnDefinitions.Clear();
        SubtitlePaginationGrid.RowDefinitions.Clear();
        if (width >= 760)
        {
            SubtitlePaginationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 4; i++) SubtitlePaginationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SubtitlePaginationGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            PlacePagination(PageSummaryText, 0, 0); PlacePagination(PageSizeComboBox, 1, 0);
            PlacePagination(PrevPageButton, 2, 0); PlacePagination(PageText, 3, 0); PlacePagination(NextPageButton, 4, 0);
        }
        else
        {
            for (var i = 0; i < 4; i++) SubtitlePaginationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SubtitlePaginationGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SubtitlePaginationGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            PlacePagination(PageSummaryText, 0, 0, 4); PlacePagination(PageSizeComboBox, 0, 1);
            PlacePagination(PrevPageButton, 1, 1); PlacePagination(PageText, 2, 1); PlacePagination(NextPageButton, 3, 1);
        }
    }

    private static void PlaceFilter(FrameworkElement element, int column, int row, int columnSpan = 1)
    {
        Grid.SetColumn(element, column); Grid.SetRow(element, row); Grid.SetColumnSpan(element, columnSpan);
    }

    private static void PlacePagination(FrameworkElement element, int column, int row, int columnSpan = 1)
    {
        Grid.SetColumn(element, column); Grid.SetRow(element, row); Grid.SetColumnSpan(element, columnSpan);
    }

    private async Task LoadUsersAsync()
    {
        UserFilterComboBox.Items.Clear();
        UserFilterComboBox.Items.Add(new ComboBoxItem { Content = "All uploaders", Tag = "" });
        UserFilterComboBox.SelectedIndex = 0;

        try
        {
            var users = await _adminApi.GetUsersAsync();
            foreach (var user in users.OrderBy(u => u.Username))
            {
                UserFilterComboBox.Items.Add(new ComboBoxItem { Content = user.Username, Tag = user.Id.ToString() });
            }
        }
        catch
        {
            // Filter stays usable without the optional uploader list.
        }
    }

    private async Task LoadSubtitlesAsync()
    {
        if (!_ready) return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var loadCts = _loadCts = new CancellationTokenSource();
        var cancellationToken = loadCts.Token;

        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        StatusText.Text = "Loading subtitles...";

        try
        {
            var response = await _adminApi.GetDownloadedSubtitlesAsync(new AdminDownloadedSubtitlesFilters
            {
                Provider = _providerFilter,
                Language = SelectedTag(LanguageFilterComboBox),
                UserId = int.TryParse(SelectedTag(UserFilterComboBox), out var userId) ? userId : null,
                Query = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
                Limit = _pageSize,
                Offset = _page * _pageSize,
            }, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(_loadCts, loadCts)) return;

            _total = response.Total;
            TotalStoredText.Text = response.Total.ToString("N0");
            UploadsText.Text = response.Uploads.ToString("N0");
            ProviderDownloadsText.Text = response.ProviderDownloads.ToString("N0");
            // Match the WebUI Set semantics: a row with a missing language is
            // still one distinct value on the current page.
            LanguagesText.Text = response.Subtitles.Select(s => s.Language ?? "").Distinct(StringComparer.Ordinal).Count().ToString("N0");

            SubtitlesListView.Items.Clear();
            foreach (var subtitle in response.Subtitles)
                SubtitlesListView.Items.Add(BuildSubtitleRow(subtitle));

            var hasActiveFilters = !string.IsNullOrWhiteSpace(_providerFilter) ||
                !string.IsNullOrWhiteSpace(SelectedTag(LanguageFilterComboBox)) ||
                !string.IsNullOrWhiteSpace(SelectedTag(UserFilterComboBox)) ||
                !string.IsNullOrWhiteSpace(SearchBox.Text);
            var isEmpty = response.Subtitles.Count == 0;
            SubtitlesTableScroll.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            SubtitlesEmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            if (isEmpty)
            {
                SubtitlesEmptyTitle.Text = hasActiveFilters
                    ? "No subtitles match these filters"
                    : "No stored subtitles yet";
                SubtitlesEmptyDetail.Text = hasActiveFilters
                    ? "Try widening the provider, language, or uploader filters to see more results."
                    : "User uploads and provider downloads will appear here once subtitles are stored in S3.";
                SubtitlesEmptyResetButton.Visibility = hasActiveFilters ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdatePagination(response.Subtitles.Count);
            StatusText.Text = "";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load subtitles: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_loadCts, loadCts))
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void PopulateLanguageFilter()
    {
        LanguageFilterComboBox.Items.Clear();
        LanguageFilterComboBox.Items.Add(new ComboBoxItem { Content = "All languages", Tag = "" });
        foreach (var language in LanguageOptions.OrderBy(language => language.Label, StringComparer.CurrentCulture))
            LanguageFilterComboBox.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
        LanguageFilterComboBox.SelectedIndex = 0;
    }

    private FrameworkElement BuildSubtitleRow(AdminDownloadedSubtitle subtitle)
    {
        var root = new Grid
        {
            ColumnSpacing = 14,
            Padding = new Thickness(16, 10, 16, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        double[] widths = [1.25, 1.15, .8, .8, 1.2, .55, .35, .65, .65];
        foreach (var width in widths)
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        var mediaTitle = string.IsNullOrWhiteSpace(subtitle.MediaTitle) ? $"Media file {subtitle.MediaFileId}" : subtitle.MediaTitle;
        FrameworkElement mediaCell;
        if (!string.IsNullOrWhiteSpace(subtitle.MediaContentId))
        {
            var link = new Button
            {
                Content = mediaTitle,
                Tag = subtitle.MediaContentId,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            link.Click += ItemLink_Click;
            mediaCell = link;
        }
        else
        {
            mediaCell = MakeCell(mediaTitle, semiBold: true);
        }
        AddCell(root, mediaCell, 0);
        AddCell(root, MakeCell(Basename(subtitle.FilePath), fontFamily: "Consolas"), 1);
        var language = subtitle.Language ?? "";
        AddCell(root, MakeBadge($"{language.ToUpperInvariant()}  {Services.PlayerService.LanguageCodeToName(language)}"), 2);
        AddCell(root, MakeBadge(ProviderLabel(subtitle.Provider)), 3);
        AddCell(root, MakeCell(string.IsNullOrWhiteSpace(subtitle.ReleaseName) ? "—" : subtitle.ReleaseName, fontFamily: "Consolas"), 4);
        AddCell(root, MakeBadge($".{(subtitle.Format ?? "srt").TrimStart('.')}") , 5);
        AddCell(root, MakeCell(subtitle.HearingImpaired ? "HI" : ""), 6);
        AddCell(root, MakeCell(string.IsNullOrWhiteSpace(subtitle.UploaderUsername) ? "—" : subtitle.UploaderUsername), 7);
        AddCell(root, MakeCell(FormatRelativeDate(subtitle.CreatedAt)), 8);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = ActualWidth < 760 ? 1 : 0,
        };

        var edit = MakeIconButton(Symbol.Edit, "Edit subtitle", subtitle);
        edit.Click += EditSubtitle_Click;
        actions.Children.Add(edit);

        var download = MakeIconButton(Symbol.Download, "Download subtitle", subtitle);
        download.Click += DownloadSubtitle_Click;
        actions.Children.Add(download);

        var delete = MakeIconButton(Symbol.Delete, "Delete subtitle", subtitle);
        delete.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
        delete.Click += DeleteSubtitle_Click;
        actions.Children.Add(delete);

        Grid.SetColumn(actions, 9);
        root.Children.Add(actions);
        var pointerOver = false;
        root.PointerEntered += (_, _) =>
        {
            pointerOver = true;
            actions.Opacity = 1;
        };
        root.PointerExited += (_, _) =>
        {
            pointerOver = false;
            if (ActualWidth >= 760 && !actions.Children.OfType<Control>().Any(control => control.FocusState != FocusState.Unfocused))
                actions.Opacity = 0;
        };
        foreach (var control in actions.Children.OfType<Control>())
        {
            control.GettingFocus += (_, _) => actions.Opacity = 1;
            control.LostFocus += (_, _) =>
            {
                if (ActualWidth >= 760 && !pointerOver)
                    actions.Opacity = 0;
            };
        }

        return root;
    }

    private static void AddCell(Grid root, FrameworkElement element, int column)
    {
        element.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(element, column);
        root.Children.Add(element);
    }

    private static TextBlock MakeCell(string text, bool semiBold = false, string? fontFamily = null)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = semiBold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };

        // WinUI's projected FontFamily setter does not accept null reliably and
        // can surface the default family as the invalid string "Unknown".
        if (!string.IsNullOrWhiteSpace(fontFamily))
            cell.FontFamily = new FontFamily(fontFamily);

        return cell;
    }

    private static Button MakeIconButton(Symbol symbol, string tooltip, AdminDownloadedSubtitle subtitle)
    {
        var button = new Button
        {
            Content = new SymbolIcon(symbol),
            Tag = subtitle,
            Padding = new Thickness(7),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static Border MakeBadge(string text) => new()
    {
        Background = (Brush)Application.Current.Resources["SidebarAccentBrush"],
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(6, 2, 6, 2),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        },
    };

    private async void EditSubtitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdminDownloadedSubtitle subtitle }) return;

        var languageBox = new ComboBox { Header = "Language", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var language in LanguageOptions.OrderBy(language => language.Label, StringComparer.CurrentCulture))
            languageBox.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
        var selectedLanguage = languageBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), subtitle.Language, StringComparison.OrdinalIgnoreCase));
        if (selectedLanguage is null)
        {
            selectedLanguage = new ComboBoxItem { Content = Services.PlayerService.LanguageCodeToName(subtitle.Language), Tag = subtitle.Language };
            languageBox.Items.Insert(0, selectedLanguage);
        }
        languageBox.SelectedItem = selectedLanguage;
        var releaseBox = new TextBox
        {
            Header = "Release name",
            Text = subtitle.ReleaseName,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
        };
        var hiToggle = new ToggleSwitch { IsOn = subtitle.HearingImpaired, OnContent = "", OffContent = "", MinWidth = 44 };
        var hiRow = new Grid { ColumnSpacing = 14 };
        hiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hiText = new StackPanel { Spacing = 2 };
        hiText.Children.Add(new TextBlock { Text = "Hearing impaired", FontWeight = FontWeights.SemiBold });
        hiText.Children.Add(new TextBlock { Text = "Marks this track as SDH/CC.", FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        hiRow.Children.Add(hiText); Grid.SetColumn(hiToggle, 1); hiRow.Children.Add(hiToggle);
        var summary = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Background = (Brush)Application.Current.Resources["SurfaceBrush"] };
        var summaryContent = new StackPanel { Spacing = 6 };
        summaryContent.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(subtitle.MediaTitle) ? $"Media file {subtitle.MediaFileId}" : subtitle.MediaTitle, FontWeight = FontWeights.SemiBold });
        var displayLanguage = subtitle.Language ?? "";
        summaryContent.Children.Add(new TextBlock { Text = $"{displayLanguage.ToUpperInvariant()} · {Services.PlayerService.LanguageCodeToName(displayLanguage)}    {ProviderLabel(subtitle.Provider)}", FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        summary.Child = summaryContent;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Edit subtitle",
            PrimaryButtonText = "Save changes",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 14,
                MinWidth = 420,
                Children =
                {
                    new TextBlock { Text = "Update stored metadata for this subtitle record. File content is not replaced.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] },
                    summary, languageBox, releaseBox, hiRow,
                },
            },
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _adminApi.UpdateDownloadedSubtitleAsync(subtitle.Id, new AdminUpdateDownloadedSubtitleRequest
            {
                Language = (languageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? subtitle.Language,
                ReleaseName = releaseBox.Text.Trim(),
                HearingImpaired = hiToggle.IsOn,
            });
            await LoadSubtitlesAsync();
            _toastService.Success("Subtitle updated");
        }
        catch (Exception ex)
        {
            _toastService.Error($"Failed to update subtitle: {ex.Message}");
        }
    }

    private async void DownloadSubtitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdminDownloadedSubtitle subtitle } downloadButton) return;

        var ext = string.IsNullOrWhiteSpace(subtitle.Format) ? ".srt" : $".{subtitle.Format.TrimStart('.')}";
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = BuildDownloadFileName(subtitle, ext),
        };
        picker.FileTypeChoices.Add("Subtitle", [ext]);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        try
        {
            downloadButton.IsEnabled = false;
            var bytes = await _adminApi.DownloadDownloadedSubtitleAsync(subtitle.Id);
            await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
            _toastService.Success("Subtitle downloaded");
        }
        catch (Exception ex)
        {
            _toastService.Error($"Failed to download subtitle: {ex.Message}");
        }
        finally { downloadButton.IsEnabled = true; }
    }

    private async void DeleteSubtitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdminDownloadedSubtitle subtitle }) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete subtitle?",
            Content = $"Remove {ProviderLabel(subtitle.Provider)} {(subtitle.Language ?? "").ToUpperInvariant()} subtitles for \"{(string.IsNullOrWhiteSpace(subtitle.MediaTitle) ? "this media" : subtitle.MediaTitle)}\"? This deletes the stored file from S3.",
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _adminApi.DeleteDownloadedSubtitleAsync(subtitle.Id);
            await LoadSubtitlesAsync();
            _toastService.Success("Subtitle deleted");
        }
        catch (Exception ex)
        {
            _toastService.Error($"Failed to delete subtitle: {ex.Message}");
        }
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _page = 0;
        _ = LoadSubtitlesAsync();
    }

    private async void ProviderFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _providerFilter = button.Tag?.ToString() ?? "";
        UpdateProviderFilterButtons();
        _page = 0;
        if (_ready) await LoadSubtitlesAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        _providerFilter = "";
        UpdateProviderFilterButtons();
        LanguageFilterComboBox.SelectedIndex = 0;
        UserFilterComboBox.SelectedIndex = 0;
        SearchBox.Text = "";
        _page = 0;
        await LoadSubtitlesAsync();
    }

    private void UpdateProviderFilterButtons()
    {
        foreach (var button in new[] { ProviderAllButton, ProviderUploadButton, ProviderOpenSubtitlesButton, ProviderSubDlButton, ProviderSubSourceButton })
        {
            var active = string.Equals(button.Tag?.ToString() ?? "", _providerFilter, StringComparison.Ordinal);
            button.Background = active
                ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                : (Brush)Application.Current.Resources["SurfaceBrush"];
            button.BorderBrush = active
                ? (Brush)Application.Current.Resources["AccentBrush"]
                : (Brush)Application.Current.Resources["BorderBrush"];
            button.BorderThickness = new Thickness(1);
            button.Foreground = active
                ? (Brush)Application.Current.Resources["PrimaryTextBrush"]
                : (Brush)Application.Current.Resources["SecondaryTextBrush"];
        }
    }

    private async void PageSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (int.TryParse(SelectedTag(PageSizeComboBox), out var pageSize))
        {
            _pageSize = pageSize;
            _page = 0;
            await LoadSubtitlesAsync();
        }
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 0) return;
        _page--;
        await LoadSubtitlesAsync();
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if ((_page + 1) * _pageSize >= _total) return;
        _page++;
        await LoadSubtitlesAsync();
    }

    private void UpdatePagination(int pageItemCount)
    {
        SubtitlePaginationGrid.Visibility = _total > 0 ? Visibility.Visible : Visibility.Collapsed;
        var first = _total == 0 ? 0 : _page * _pageSize + 1;
        var last = _total == 0 ? 0 : _page * _pageSize + pageItemCount;
        var pageCount = Math.Max(1, (int)Math.Ceiling(_total / (double)_pageSize));
        PageSummaryText.Text = _total == 0 ? "No subtitles" : $"Showing {first:N0}–{last:N0} of {_total:N0}";
        PageText.Text = $"Page {_page + 1:N0} of {pageCount:N0}";
        PrevPageButton.IsEnabled = _page > 0;
        NextPageButton.IsEnabled = (_page + 1) * _pageSize < _total;
    }

    private static string? SelectedTag(ComboBox comboBox)
        => (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private static string ProviderLabel(string provider) => provider switch
    {
        "upload" => "Upload",
        "opensubtitles" => "OpenSubtitles",
        "subdl" => "SubDL",
        "subsource" => "SubSource",
        _ when !string.IsNullOrWhiteSpace(provider) => provider,
        _ => "Unknown",
    };

    private static string FormatRelativeDate(string value)
    {
        if (!DateTimeOffset.TryParse(value, out var date)) return value;
        var elapsed = DateTimeOffset.Now - date.ToLocalTime();
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays}d ago";
        return SiloPlayer.Helpers.DateTimeDisplay.FormatDate(date);
    }

    private static string Basename(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "—";
        var normalized = path.Replace('\\', '/');
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    private void ItemLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string itemId } || string.IsNullOrWhiteSpace(itemId)) return;
        App.MainWindowInstance?.RestoreMainPane();
        App.Services.GetRequiredService<Helpers.NavigationService>().Navigate<ItemDetailPage>(itemId);
    }

    private static string BuildDownloadFileName(AdminDownloadedSubtitle subtitle, string ext)
    {
        var baseName = (subtitle.ReleaseName ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = $"subtitle-{subtitle.Id}";
        if (!baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            baseName += ext;

        foreach (var invalid in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(invalid, '_');
        return baseName;
    }
}
