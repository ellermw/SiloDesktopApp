using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Downloads;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class DownloadsPage : Page
{
    public DownloadsViewModel ViewModel { get; }

    public DownloadsPage()
    {
        ViewModel = App.Services.GetRequiredService<DownloadsViewModel>();
        this.InitializeComponent();

        ViewModel.Downloads.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                BuildDownloadRows();
                UpdateCounts();
            });
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadDownloadsCommand.ExecuteAsync(null);
        BuildDownloadRows();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        int count = ViewModel.Downloads.Count;
        bool hasItems = count > 0 && !ViewModel.IsLoading;

        EmptyState.Visibility = count == 0 && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;

        CountPanel.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

        if (hasItems)
        {
            ItemCountText.Text = count.ToString();
            ItemCountLabel.Text = count == 1 ? "file" : "files";
        }
    }

    private void BuildDownloadRows()
    {
        DownloadsList.Children.Clear();

        foreach (var dl in ViewModel.Downloads)
        {
            var row = CreateDownloadRow(dl);
            DownloadsList.Children.Add(row);
        }
    }

    private Border CreateDownloadRow(Download dl)
    {
        // Outer card
        var card = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        // File icon
        var fileIcon = new FontIcon
        {
            Glyph = "\uE8A5", // Document
            FontSize = 20,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(fileIcon, 0);
        grid.Children.Add(fileIcon);

        // Info column
        var infoPanel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        // File name
        infoPanel.Children.Add(new TextBlock
        {
            Text = dl.FileName,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        });

        // Status + size + date row
        var metaPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        // Status badge
        var statusColor = dl.Status switch
        {
            "ready" => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
            "pending" or "processing" => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            "failed" => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"],
            _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"]
        };

        var statusBadge = new Border
        {
            Background = statusColor,
            Opacity = 0.15,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = dl.Status.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = statusColor
            }
        };
        metaPanel.Children.Add(statusBadge);

        // File size
        if (dl.FileSize > 0)
        {
            metaPanel.Children.Add(new TextBlock
            {
                Text = FormatFileSize(dl.FileSize),
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // Date
        if (!string.IsNullOrEmpty(dl.CreatedAt))
        {
            metaPanel.Children.Add(new TextBlock
            {
                Text = FormatDate(dl.CreatedAt),
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        infoPanel.Children.Add(metaPanel);
        Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(infoPanel);

        // Actions
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Save to Disk button (only for ready downloads)
        if (dl.Status == "ready")
        {
            var saveButton = new Button
            {
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(6),
                Tag = dl.Id
            };
            var saveContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            saveContent.Children.Add(new FontIcon
            {
                Glyph = "\uE896", // Save
                FontSize = 14,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"]
            });
            saveContent.Children.Add(new TextBlock { Text = "Save to Disk", VerticalAlignment = VerticalAlignment.Center });
            saveButton.Content = saveContent;
            saveButton.Click += SaveButton_Click;
            actionsPanel.Children.Add(saveButton);
        }

        // Delete button
        var deleteButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(8, 8, 8, 8),
            CornerRadius = new CornerRadius(6),
            Tag = dl.Id,
            Content = new FontIcon
            {
                Glyph = "\uE74D", // Delete
                FontSize = 14,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"]
            }
        };
        deleteButton.Click += DeleteButton_Click;
        actionsPanel.Children.Add(deleteButton);

        Grid.SetColumn(actionsPanel, 2);
        grid.Children.Add(actionsPanel);

        card.Child = grid;
        return card;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int downloadId) return;

        var dl = ViewModel.Downloads.FirstOrDefault(d => d.Id == downloadId);
        if (dl == null) return;

        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();

            // Initialize the picker with the window handle (required for WinUI 3)
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            // Determine file extension from file name
            var ext = Path.GetExtension(dl.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ".mkv";

            picker.SuggestedFileName = dl.FileName;
            picker.FileTypeChoices.Add("Media File", [ext]);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return; // User cancelled

            // Download the file from the server
            var apiClient = App.Services.GetRequiredService<ContinuumApiClient>();
            var downloadPath = DownloadsApi.GetDownloadFilePath(downloadId);
            var url = $"{apiClient.BaseUrl}{downloadPath}";
            if (apiClient.AccessToken != null)
                url += $"?token={Uri.EscapeDataString(apiClient.AccessToken)}";

            var httpClient = App.Services.GetRequiredService<HttpClient>();
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var sourceStream = await response.Content.ReadAsStreamAsync();
            using var destStream = await file.OpenStreamForWriteAsync();
            await sourceStream.CopyToAsync(destStream);
        }
        catch (Exception ex)
        {
            // Show error in a simple way -- non-fatal
            System.Diagnostics.Debug.WriteLine($"Save download failed: {ex.Message}");
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int downloadId)
        {
            await ViewModel.DeleteDownloadCommand.ExecuteAsync(downloadId);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private static string FormatDate(string dateStr)
    {
        if (DateTime.TryParse(dateStr, out var dt))
            return dt.ToString("MMM d, yyyy");
        return dateStr;
    }
}
