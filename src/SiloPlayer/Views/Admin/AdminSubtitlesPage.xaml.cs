using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminSubtitlesPage : Page
{
    private readonly AdminApi _adminApi;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _ready;
    private int _page;
    private int _pageSize = 25;
    private int _total;

    public AdminSubtitlesPage()
    {
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        InitializeComponent();

        Loaded += AdminSubtitlesPage_Loaded;
        Unloaded += (_, _) => _searchTimer.Stop();
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            _page = 0;
            await LoadSubtitlesAsync();
        };
    }

    private async void AdminSubtitlesPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_ready) return;
        await LoadUsersAsync();
        _ready = true;
        await LoadSubtitlesAsync();
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
                var label = string.IsNullOrWhiteSpace(user.Email)
                    ? user.Username
                    : $"{user.Username} ({user.Email})";
                UserFilterComboBox.Items.Add(new ComboBoxItem { Content = label, Tag = user.Id.ToString() });
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

        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        StatusText.Text = "Loading subtitles...";

        try
        {
            var response = await _adminApi.GetDownloadedSubtitlesAsync(new AdminDownloadedSubtitlesFilters
            {
                Provider = SelectedTag(ProviderFilterComboBox),
                Language = SelectedTag(LanguageFilterComboBox),
                UserId = int.TryParse(SelectedTag(UserFilterComboBox), out var userId) ? userId : null,
                Query = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
                Limit = _pageSize,
                Offset = _page * _pageSize,
            });

            _total = response.Total;
            TotalStoredText.Text = response.Total.ToString("N0");
            UploadsText.Text = response.Uploads.ToString("N0");
            ProviderDownloadsText.Text = response.ProviderDownloads.ToString("N0");
            LanguagesText.Text = response.Subtitles.Select(s => s.Language).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0");

            SubtitlesListView.Items.Clear();
            foreach (var subtitle in response.Subtitles)
                SubtitlesListView.Items.Add(BuildSubtitleRow(subtitle));

            if (response.Subtitles.Count == 0)
            {
                SubtitlesListView.Items.Add(new TextBlock
                {
                    Text = "No subtitles match these filters.",
                    Margin = new Thickness(20),
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
            }

            UpdatePagination(response.Subtitles.Count);
            StatusText.Text = $"{response.Subtitles.Count:N0} subtitle(s) loaded.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load subtitles: {ex.Message}";
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private FrameworkElement BuildSubtitleRow(AdminDownloadedSubtitle subtitle)
    {
        var root = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stack = new StackPanel { Spacing = 6 };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(subtitle.MediaTitle) ? $"Media file {subtitle.MediaFileId}" : subtitle.MediaTitle,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 520,
        });
        titleRow.Children.Add(MakeBadge(ProviderLabel(subtitle.Provider)));
        titleRow.Children.Add(MakeBadge(Services.PlayerService.LanguageCodeToName(subtitle.Language)));
        if (!string.IsNullOrWhiteSpace(subtitle.Format))
            titleRow.Children.Add(MakeBadge(subtitle.Format.ToUpperInvariant()));
        if (subtitle.HearingImpaired)
            titleRow.Children.Add(MakeBadge("HI"));
        stack.Children.Add(titleRow);

        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(subtitle.ReleaseName) ? "(no release name)" : subtitle.ReleaseName,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(subtitle.UploaderUsername)) meta.Add($"by {subtitle.UploaderUsername}");
        if (!string.IsNullOrWhiteSpace(subtitle.CreatedAt)) meta.Add(FormatDate(subtitle.CreatedAt));
        if (subtitle.Score > 0) meta.Add($"score {subtitle.Score:0}");
        if (!string.IsNullOrWhiteSpace(subtitle.FilePath)) meta.Add(subtitle.FilePath);
        stack.Children.Add(new TextBlock
        {
            Text = string.Join(" - ", meta),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        Grid.SetColumn(stack, 0);
        root.Children.Add(stack);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var edit = new Button { Content = "Edit", Tag = subtitle };
        edit.Click += EditSubtitle_Click;
        actions.Children.Add(edit);

        var download = new Button { Content = "Download", Tag = subtitle };
        download.Click += DownloadSubtitle_Click;
        actions.Children.Add(download);

        var delete = new Button
        {
            Content = "Delete",
            Tag = subtitle,
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
        };
        delete.Click += DeleteSubtitle_Click;
        actions.Children.Add(delete);

        Grid.SetColumn(actions, 1);
        root.Children.Add(actions);

        return root;
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

        var languageBox = new TextBox
        {
            Header = "Language",
            Text = subtitle.Language,
            PlaceholderText = "en",
        };
        var releaseBox = new TextBox
        {
            Header = "Release name",
            Text = subtitle.ReleaseName,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MinHeight = 90,
        };
        var hiToggle = new ToggleSwitch
        {
            Header = "Hearing impaired (HI)",
            IsOn = subtitle.HearingImpaired,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Edit Subtitle",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                MinWidth = 420,
                Children = { languageBox, releaseBox, hiToggle },
            },
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _adminApi.UpdateDownloadedSubtitleAsync(subtitle.Id, new AdminUpdateDownloadedSubtitleRequest
            {
                Language = languageBox.Text.Trim(),
                ReleaseName = releaseBox.Text.Trim(),
                HearingImpaired = hiToggle.IsOn,
            });
            await LoadSubtitlesAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to update subtitle: {ex.Message}";
        }
    }

    private async void DownloadSubtitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdminDownloadedSubtitle subtitle }) return;

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
            StatusText.Text = "Downloading subtitle...";
            var bytes = await _adminApi.DownloadDownloadedSubtitleAsync(subtitle.Id);
            await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
            StatusText.Text = $"Saved {file.Name}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to download subtitle: {ex.Message}";
        }
    }

    private async void DeleteSubtitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdminDownloadedSubtitle subtitle }) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete Subtitle?",
            Content = "This removes the stored subtitle file from Silo.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _adminApi.DeleteDownloadedSubtitleAsync(subtitle.Id);
            await LoadSubtitlesAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to delete subtitle: {ex.Message}";
        }
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _page = 0;
        _ = LoadSubtitlesAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        ProviderFilterComboBox.SelectedIndex = 0;
        LanguageFilterComboBox.SelectedIndex = 0;
        UserFilterComboBox.SelectedIndex = 0;
        SearchBox.Text = "";
        _page = 0;
        await LoadSubtitlesAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadSubtitlesAsync();

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
        var first = _total == 0 ? 0 : _page * _pageSize + 1;
        var last = _total == 0 ? 0 : _page * _pageSize + pageItemCount;
        var pageCount = Math.Max(1, (int)Math.Ceiling(_total / (double)_pageSize));
        PageSummaryText.Text = _total == 0 ? "No subtitles" : $"Showing {first:N0}-{last:N0} of {_total:N0}";
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

    private static string FormatDate(string value)
        => DateTimeOffset.TryParse(value, out var dto)
            ? dto.LocalDateTime.ToString("g")
            : value;

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
