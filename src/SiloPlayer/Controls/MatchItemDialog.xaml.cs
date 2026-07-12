using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Controls;

public sealed partial class MatchItemDialog : ContentDialog
{
    private readonly AdminApi _adminApi;
    private readonly string _itemId;
    private MatchCandidate? _selectedCandidate;

    public MatchCandidate? SelectedCandidate => _selectedCandidate;

    public MatchItemDialog(string itemId)
    {
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _itemId = itemId;
        this.InitializeComponent();
    }

    /// <summary>
    /// Extended constructor that also displays the item's on-disk locations
    /// (mirrors the webui MediaLocations / FolderPathsList sections that
    /// appear inside MatchItemDialog). Movies show one row per FileVersion
    /// with folder + filename; series show one row per library root path.
    /// </summary>
    public MatchItemDialog(string itemId, IList<FileVersion>? versions,
        IList<string>? folderPaths, bool isSeries)
        : this(itemId)
    {
        PopulateLocalMedia(versions, folderPaths, isSeries);
    }

    private void PopulateLocalMedia(IList<FileVersion>? versions,
        IList<string>? folderPaths, bool isSeries)
    {
        LocalMediaPanel.Children.Clear();

        if (isSeries)
        {
            if (folderPaths == null || folderPaths.Count == 0)
            {
                LocalMediaSection.Visibility = Visibility.Visible;
                LocalMediaEmpty.Text = "No library folder paths available.";
                LocalMediaEmpty.Visibility = Visibility.Visible;
                return;
            }
            foreach (var path in folderPaths)
                LocalMediaPanel.Children.Add(BuildFolderRow(path));
            LocalMediaSection.Visibility = Visibility.Visible;
            return;
        }

        if (versions == null || versions.Count == 0)
        {
            LocalMediaSection.Visibility = Visibility.Visible;
            LocalMediaEmpty.Text = "No file paths are available for this item.";
            LocalMediaEmpty.Visibility = Visibility.Visible;
            return;
        }

        int shown = 0;
        foreach (var v in versions.OrderByDescending(VersionSortKey))
        {
            if (string.IsNullOrWhiteSpace(v.FilePath)) continue;
            LocalMediaPanel.Children.Add(BuildVersionRow(v));
            shown++;
        }

        if (shown == 0)
        {
            LocalMediaEmpty.Text = "No file paths are available for this item.";
            LocalMediaEmpty.Visibility = Visibility.Visible;
        }
        LocalMediaSection.Visibility = Visibility.Visible;
    }

    /// <summary>Resolution + HDR score for sorting versions high-to-low.</summary>
    private static int VersionSortKey(FileVersion v)
    {
        int res = v.Resolution switch
        {
            "2160p" => 4000,
            "1080p" => 3000,
            "720p" => 2000,
            "480p" => 1000,
            _ => 0,
        };
        return res + (v.Hdr ? 1 : 0);
    }

    private static (string folderName, string folderPath, string fileName) SplitPath(
        string? filePath, string? fallbackName)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return ("", "", fallbackName?.Trim() ?? "");

        int lastSlash = Math.Max(filePath.LastIndexOf('/'), filePath.LastIndexOf('\\'));
        if (lastSlash < 0)
            return ("", "", filePath);

        string folderPath = filePath.Substring(0, lastSlash);
        string fileName = filePath.Substring(lastSlash + 1);
        if (string.IsNullOrEmpty(fileName))
            fileName = fallbackName?.Trim() ?? "Unknown file";

        var segs = folderPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        string folderName = segs.Length > 0 ? segs[^1] : folderPath;
        return (folderName, folderPath, fileName);
    }

    private static string BuildVersionLabel(FileVersion v)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(v.Resolution)) parts.Add(v.Resolution);
        if (v.Hdr) parts.Add("HDR");
        if (!string.IsNullOrEmpty(v.CodecVideo)) parts.Add(v.CodecVideo.ToUpperInvariant());
        return parts.Count > 0 ? string.Join(" ", parts) : $"Version {v.FileId}";
    }

    private FrameworkElement BuildVersionRow(FileVersion v)
    {
        var border = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 8, 8),
        };

        var root = new Grid { ColumnSpacing = 8 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Spacing = 2 };
        info.Children.Add(new TextBlock
        {
            Text = BuildVersionLabel(v),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        });

        var (folderName, folderPath, fileName) = SplitPath(v.FilePath, v.FileName);

        var pathRow = new TextBlock
        {
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (!string.IsNullOrEmpty(folderName))
        {
            pathRow.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = $"{folderName}/",
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }
        pathRow.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = fileName,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        });
        ToolTipService.SetToolTip(pathRow, string.IsNullOrEmpty(folderPath) ? fileName : $"{folderPath}\\{fileName}");
        info.Children.Add(pathRow);

        Grid.SetColumn(info, 0);
        root.Children.Add(info);

        if (!string.IsNullOrEmpty(folderPath))
        {
            var copyBtn = new Button
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6),
                VerticalAlignment = VerticalAlignment.Top,
                Content = new FontIcon { Glyph = "\uE8C8", FontSize = 13 },
            };
            ToolTipService.SetToolTip(copyBtn, "Copy folder path");
            copyBtn.Click += (_, _) => CopyToClipboard(folderPath, "Copied folder path");
            Grid.SetColumn(copyBtn, 1);
            root.Children.Add(copyBtn);
        }

        border.Child = root;
        return border;
    }

    private FrameworkElement BuildFolderRow(string path)
    {
        var border = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 8, 8),
        };

        var root = new Grid { ColumnSpacing = 8 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = path,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(text, path);
        Grid.SetColumn(text, 0);
        root.Children.Add(text);

        var copyBtn = new Button
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            Content = new FontIcon { Glyph = "\uE8C8", FontSize = 13 },
        };
        ToolTipService.SetToolTip(copyBtn, "Copy path");
        copyBtn.Click += (_, _) => CopyToClipboard(path, "Copied path");
        Grid.SetColumn(copyBtn, 1);
        root.Children.Add(copyBtn);

        border.Child = root;
        return border;
    }

    private static void CopyToClipboard(string text, string _unusedToastMessage)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
        }
        catch { /* clipboard access can throw on locked sessions — ignore */ }
    }

    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            _ = DoSearchAsync();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await DoSearchAsync();
    }

    private async Task DoSearchAsync()
    {
        string query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        ResultsPanel.Children.Clear();
        EmptyText.Visibility = Visibility.Collapsed;
        _selectedCandidate = null;
        IsPrimaryButtonEnabled = false;

        try
        {
            var request = new ItemMatchSearchRequest { Title = query };

            // Check if it looks like an ID
            if (query.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                request = new ItemMatchSearchRequest { ImdbId = query };
            else if (int.TryParse(query, out _))
                request = new ItemMatchSearchRequest { TmdbId = query };

            var response = await _adminApi.MatchSearchAsync(_itemId, request);

            if (response.Candidates.Count == 0)
            {
                EmptyText.Text = "No matches found. Try a different search.";
                EmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                foreach (var candidate in response.Candidates)
                {
                    ResultsPanel.Children.Add(BuildCandidateRow(candidate));
                }
            }
        }
        catch (Exception ex)
        {
            EmptyText.Text = $"Error: {ex.Message}";
            EmptyText.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private FrameworkElement BuildCandidateRow(MatchCandidate candidate)
    {
        var rowBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(12)
        };

        var root = new Grid { ColumnSpacing = 12 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Poster thumbnail
        if (!string.IsNullOrEmpty(candidate.ImageUrl))
        {
            var poster = new Border
            {
                Width = 50, Height = 75,
                CornerRadius = new CornerRadius(6),
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"]
            };
            var img = new Image
            {
                Source = new BitmapImage(new Uri(candidate.ImageUrl)),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };
            poster.Child = img;
            Grid.SetColumn(poster, 0);
            root.Children.Add(poster);
        }

        // Info
        var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = $"{candidate.Title} ({candidate.Year})",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        // Provider IDs
        string ids = string.Join(" | ", candidate.ProviderIds.Select(kv => $"{kv.Key}: {kv.Value}"));
        if (!string.IsNullOrEmpty(ids))
        {
            info.Children.Add(new TextBlock
            {
                Text = ids,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            });
        }

        // Sources
        if (candidate.Sources.Count > 0)
        {
            info.Children.Add(new TextBlock
            {
                Text = "Sources: " + string.Join(", ", candidate.Sources),
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }

        Grid.SetColumn(info, 1);
        root.Children.Add(info);

        rowBtn.Content = root;

        var capturedCandidate = candidate;
        rowBtn.Click += (_, _) =>
        {
            // Deselect all, select this one
            foreach (var child in ResultsPanel.Children)
            {
                if (child is Button btn)
                {
                    btn.Background = new SolidColorBrush(Colors.Transparent);
                    btn.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
                }
            }
            rowBtn.Background = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"];
            rowBtn.BorderBrush = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            _selectedCandidate = capturedCandidate;
            IsPrimaryButtonEnabled = true;
        };

        return rowBtn;
    }
}
