using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.MediaMaintenance;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Controls;

public sealed partial class MatchItemDialog : ContentDialog
{
    private readonly MediaMaintenanceApi _maintenanceApi;
    private readonly string _itemId;
    private readonly string _itemType;
    private readonly int? _libraryId;
    private readonly List<ProviderIdInput> _providerIdInputs = [];
    private MatchCandidate? _selectedCandidate;
    private bool _searching;
    private bool _applying;

    public MatchCandidate? SelectedCandidate => _selectedCandidate;
    public bool HasAppliedMatch { get; private set; }

    public MatchItemDialog(string itemId)
        : this(itemId, "", null, "movie", null, null, null)
    {
    }

    public MatchItemDialog(
        string itemId,
        string title,
        int? year,
        string itemType,
        int? libraryId,
        IList<FileVersion>? versions,
        IList<string>? folderPaths)
    {
        _maintenanceApi = App.Services.GetRequiredService<MediaMaintenanceApi>();
        _itemId = itemId;
        _itemType = string.IsNullOrWhiteSpace(itemType) ? "item" : itemType.Trim().ToLowerInvariant();
        _libraryId = libraryId;
        this.InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 896d;
        Opened += (_, _) => UpdateViewport();
        SizeChanged += (_, _) => UpdateViewport();

        CurrentTitleText.Text = string.IsNullOrWhiteSpace(title) ? "Untitled" : title;
        CurrentYearText.Text = year is > 0 ? $"({year})" : "";
        CurrentTypeText.Text = _itemType;

        var yearText = year is > 0 ? year.Value.ToString() : "";
        if (IsVideoMatchType(_itemType))
        {
            VideoSearchFields.Visibility = Visibility.Visible;
            GenericSearchFields.Visibility = Visibility.Collapsed;
            TitleBox.Text = title;
            YearBox.Text = yearText;
        }
        else
        {
            VideoSearchFields.Visibility = Visibility.Collapsed;
            GenericSearchFields.Visibility = Visibility.Visible;
            GenericTitleBox.Text = title;
            GenericYearBox.Text = yearText;
            AddProviderIdRow();
        }

        PopulateLocalMedia(versions, folderPaths, _itemType == "series");
    }

    private async void ApplyMatch_Click(object sender, RoutedEventArgs args)
    {
        if (_selectedCandidate == null || _applying)
            return;
        var selected = _selectedCandidate;
        _applying = true;
        ApplyMatchButton.IsEnabled = false;
        SearchButton.IsEnabled = false;
        ApplyMatchButton.Content = "Applying...";
        ApplyStatusText.Text = "Applying match…";
        ApplyStatusText.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        ApplyStatusText.Visibility = Visibility.Visible;
        try
        {
            await _maintenanceApi.ApplyMatchAsync(_itemId, new ItemMatchApplyRequest
            {
                ProviderIds = selected.ProviderIds,
            });
            HasAppliedMatch = true;
            App.Services.GetService<SiloPlayer.Services.ToastService>()?.Success("Match applied");
            Hide();
        }
        catch (Exception ex)
        {
            ApplyStatusText.Text = $"Match could not be applied: {ex.Message}";
            ApplyStatusText.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
            ApplyMatchButton.Content = "Apply Match";
            ApplyMatchButton.IsEnabled = _selectedCandidate != null;
        }
        finally
        {
            _applying = false;
            SearchButton.IsEnabled = !_searching;
        }
    }

    private void UpdateViewport()
    {
        MatchBody.Width = Math.Max(240, Math.Min(800, (XamlRoot?.Size.Width ?? 960) - 80));
        MatchBody.MaxHeight = Math.Max(160, (XamlRoot?.Size.Height ?? 900) * .8 - 140);
    }

    /// <summary>
    /// Extended constructor that also displays the item's on-disk locations
    /// (mirrors the webui MediaLocations / FolderPathsList sections that
    /// appear inside MatchItemDialog). Movies show one row per FileVersion
    /// with folder + filename; series show one row per library root path.
    /// </summary>
    public MatchItemDialog(string itemId, IList<FileVersion>? versions,
        IList<string>? folderPaths, bool isSeries)
        : this(itemId, "", null, isSeries ? "series" : "movie", null, versions, folderPaths)
    {
    }

    private static bool IsVideoMatchType(string type) => type.Trim().ToLowerInvariant() is
        "movie" or "movies" or "series" or "show" or "shows" or "tv" or
        "season" or "seasons" or "episode" or "episodes";

    private void PopulateLocalMedia(IList<FileVersion>? versions,
        IList<string>? folderPaths, bool isSeries)
    {
        LocalMediaPanel.Children.Clear();

        if (isSeries)
        {
            if (folderPaths == null || folderPaths.Count == 0)
            {
                LocalMediaSection.Visibility = Visibility.Visible;
                LocalMediaEmpty.Text = "No folder paths are available for this item.";
                LocalMediaEmpty.Visibility = Visibility.Visible;
                return;
            }
            var rootPath = ComputeRootPath(folderPaths);
            var folders = new StackPanel();
            if (!string.IsNullOrWhiteSpace(rootPath))
                folders.Children.Add(BuildRootPathRow(rootPath));
            foreach (var path in folderPaths)
                folders.Children.Add(new Border
                {
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, folders.Children.Count > 0 ? 1 : 0, 0, 0),
                    Child = BuildFolderRow(path),
                });
            LocalMediaPanel.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                Background = (Brush)Application.Current.Resources["AppBackgroundBrush"],
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = folders,
            });
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
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
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

    private FrameworkElement BuildFolderRow(string path, bool isRootPath = false)
    {
        var border = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
        };

        var root = new Grid { ColumnSpacing = 8 };
        if (!isRootPath) root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!isRootPath) root.Children.Add(WebUiIcon.Create("folder", 14, (Brush)Application.Current.Resources["SecondaryTextBrush"]));

        var text = new TextBlock
        {
            Text = isRootPath ? path : path.TrimEnd('/', '\\').Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = isRootPath ? FontWeights.Normal : FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources[isRootPath ? "SecondaryTextBrush" : "PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(text, path);
        Grid.SetColumn(text, isRootPath ? 0 : 1);
        root.Children.Add(text);

        var copyBtn = new Button
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Width = 24, Height = 24, MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(0), Opacity = isRootPath ? 1 : 0,
            Content = WebUiIcon.Create("copy", 12, (Brush)Application.Current.Resources["SecondaryTextBrush"]),
        };
        AutomationProperties.SetName(copyBtn, (isRootPath ? "Copy root path " : "Copy folder path ") + path);
        if (!isRootPath)
        {
            border.PointerEntered += (_, _) => copyBtn.Opacity = 1;
            border.PointerExited += (_, _) => { if (copyBtn.FocusState == FocusState.Unfocused) copyBtn.Opacity = 0; };
            copyBtn.GotFocus += (_, _) => copyBtn.Opacity = 1;
            copyBtn.LostFocus += (_, _) => copyBtn.Opacity = 0;
        }
        ToolTipService.SetToolTip(copyBtn, isRootPath ? "Copy root path" : "Copy full path");
        copyBtn.Click += (_, _) => CopyToClipboard(
            path,
            isRootPath ? "Copied root path" : "Copied folder path");
        Grid.SetColumn(copyBtn, isRootPath ? 1 : 2);
        root.Children.Add(copyBtn);

        border.Child = root;
        return border;
    }

    private FrameworkElement BuildRootPathRow(string path)
    {
        var root = new StackPanel { Spacing = 4, Margin = new Thickness(12, 8, 12, 8) };
        root.Children.Add(new TextBlock
        {
            Text = "ROOT PATH",
            FontSize = 11,
            FontWeight = FontWeights.Medium, CharacterSpacing = 80,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        var row = BuildFolderRow(path, isRootPath: true);
        if (row is Border body) body.Padding = new Thickness(0);
        root.Children.Add(row);
        return root;
    }

    private static string ComputeRootPath(IList<string> paths)
    {
        if (paths.Count == 0) return "";
        var parents = paths
            .Select(path => path.Trim().TrimEnd('/', '\\')
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .SkipLast(1)
                .ToArray())
            .ToList();
        if (parents.Count == 0 || parents.Any(parts => parts.Length == 0)) return "";
        var shared = 0;
        var limit = parents.Min(parts => parts.Length);
        while (shared < limit && parents.All(parts =>
                   string.Equals(parts[shared], parents[0][shared], StringComparison.Ordinal)))
            shared++;
        if (shared == 0) return "";
        var separator = paths[0].Contains('\\') ? "\\" : "/";
        var prefix = paths[0].StartsWith(separator, StringComparison.Ordinal) ? separator : "";
        return prefix + string.Join(separator, parents[0].Take(shared));
    }

    private static void CopyToClipboard(string text, string successMessage)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
            App.Services.GetService<SiloPlayer.Services.ToastService>()?.Success(successMessage);
        }
        catch
        {
            App.Services.GetService<SiloPlayer.Services.ToastService>()?.Error("Failed to copy path");
        }
    }

    private sealed class ProviderIdInput(Grid host, TextBox providerBox, TextBox valueBox)
    {
        public Grid Host { get; } = host;
        public TextBox ProviderBox { get; } = providerBox;
        public TextBox ValueBox { get; } = valueBox;
    }

    private void AddProviderId_Click(object sender, RoutedEventArgs e) => AddProviderIdRow();

    private void AddProviderIdRow()
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var providerBox = new TextBox
        {
            PlaceholderText = "isbn",
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
        };
        providerBox.KeyDown += SearchField_KeyDown;
        AutomationProperties.SetName(providerBox, "Provider");
        row.Children.Add(providerBox);

        var valueBox = new TextBox
        {
            PlaceholderText = "978...",
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
        };
        valueBox.KeyDown += SearchField_KeyDown;
        AutomationProperties.SetName(valueBox, "Provider ID");
        Grid.SetColumn(valueBox, 1);
        row.Children.Add(valueBox);

        var removeButton = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        AutomationProperties.SetName(removeButton, "Remove provider ID");
        ToolTipService.SetToolTip(removeButton, "Remove provider ID");
        Grid.SetColumn(removeButton, 2);
        row.Children.Add(removeButton);

        var input = new ProviderIdInput(row, providerBox, valueBox);
        removeButton.Click += (_, _) =>
        {
            if (_providerIdInputs.Count == 1)
            {
                providerBox.Text = "";
                valueBox.Text = "";
                return;
            }
            _providerIdInputs.Remove(input);
            ProviderIdsPanel.Children.Remove(row);
        };

        _providerIdInputs.Add(input);
        ProviderIdsPanel.Children.Add(row);
    }

    private Dictionary<string, string>? GetProviderIds()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in _providerIdInputs)
        {
            var provider = input.ProviderBox.Text.Trim().ToLowerInvariant();
            var value = input.ValueBox.Text.Trim();
            if (provider.Length > 0 && value.Length > 0)
                values[provider] = value;
        }
        return values.Count > 0 ? values : null;
    }

    private void SearchField_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _ = DoSearchAsync();
            e.Handled = true;
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await DoSearchAsync();
    }

    private async Task DoSearchAsync()
    {
        if (_searching || _applying) return;
        var isVideo = IsVideoMatchType(_itemType);
        var title = (isVideo ? TitleBox.Text : GenericTitleBox.Text).Trim();
        var yearText = (isVideo ? YearBox.Text : GenericYearBox.Text).Trim();
        var year = int.TryParse(yearText, out var parsedYear) ? parsedYear : (int?)null;
        var imdbId = isVideo ? ImdbIdBox.Text.Trim() : "";
        var tmdbId = isVideo ? TmdbIdBox.Text.Trim() : "";
        var tvdbId = isVideo ? TvdbIdBox.Text.Trim() : "";
        var providerIds = isVideo ? null : GetProviderIds();

        _searching = true;
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        SearchButton.IsEnabled = false;
        SearchButtonText.Text = "Searching...";
        ResultsPanel.Children.Clear();
        ResultsSection.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;
        _selectedCandidate = null;
        ApplyMatchButton.Visibility = Visibility.Collapsed;
        ApplyStatusText.Visibility = Visibility.Collapsed;

        try
        {
            var request = new ItemMatchSearchRequest
            {
                Title = title.Length > 0 ? title : null,
                Year = year,
                ImdbId = imdbId.Length > 0 ? imdbId : null,
                TmdbId = tmdbId.Length > 0 ? tmdbId : null,
                TvdbId = tvdbId.Length > 0 ? tvdbId : null,
                ProviderIds = providerIds,
                LibraryId = _libraryId,
            };

            var response = await _maintenanceApi.SearchMatchesAsync(_itemId, request);
            TruncatedText.Visibility = response.Truncated ? Visibility.Visible : Visibility.Collapsed;

            if (response.Candidates.Count == 0)
            {
                EmptyText.Text = "No candidates found.";
                EmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                ResultsSection.Visibility = Visibility.Visible;
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
            _searching = false;
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            SearchButton.IsEnabled = true;
            SearchButtonText.Text = "Search";
        }
    }

    private FrameworkElement BuildCandidateRow(MatchCandidate candidate)
    {
        var matchedFallbackTitle = candidate.TitleIsFallback &&
            !string.IsNullOrWhiteSpace(candidate.MatchedTitle) &&
            !string.Equals(candidate.MatchedTitle, candidate.Title, StringComparison.Ordinal)
                ? candidate.MatchedTitle
                : null;
        var displayTitle = matchedFallbackTitle ?? candidate.Title;

        var rowBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8)
        };

        var root = new Grid { ColumnSpacing = 12 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Poster thumbnail
        if (!string.IsNullOrEmpty(candidate.ImageUrl) &&
            Uri.TryCreate(candidate.ImageUrl, UriKind.Absolute, out var imageUri))
        {
            var poster = new Border
            {
                Width = 64, Height = 96,
                CornerRadius = new CornerRadius(6),
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"]
            };
            var img = new Image
            {
                Source = new BitmapImage(imageUri),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };
            poster.Child = img;
            ToolTipService.SetToolTip(poster, new ToolTip
            {
                Content = new Image
                {
                    Source = new BitmapImage(imageUri),
                    Width = 192,
                    Height = 288,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                }
            });
            Grid.SetColumn(poster, 0);
            root.Children.Add(poster);
        }
        else
        {
            var placeholder = new Border
            {
                Width = 64,
                Height = 96,
                CornerRadius = new CornerRadius(6),
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            };
            Grid.SetColumn(placeholder, 0);
            root.Children.Add(placeholder);
        }

        // Info
        var info = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Top };
        info.Children.Add(new TextBlock
        {
            Text = displayTitle,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (matchedFallbackTitle != null)
        {
            info.Children.Add(new TextBlock
            {
                Text = $"Native title: {candidate.Title}",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        else if (!string.IsNullOrWhiteSpace(candidate.OriginalTitle) &&
                 !string.Equals(candidate.OriginalTitle, candidate.Title, StringComparison.Ordinal))
        {
            info.Children.Add(new TextBlock
            {
                Text = $"Original: {candidate.OriginalTitle}",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        if (matchedFallbackTitle == null &&
            !string.IsNullOrWhiteSpace(candidate.MatchedTitle) &&
            !string.Equals(candidate.MatchedTitle, candidate.Title, StringComparison.Ordinal) &&
            !string.Equals(candidate.MatchedTitle, candidate.OriginalTitle, StringComparison.Ordinal))
        {
            info.Children.Add(new TextBlock
            {
                Text = $"Matched alias: {candidate.MatchedTitle}",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        info.Children.Add(new TextBlock
        {
            Text = candidate.Year > 0 ? candidate.Year.ToString() : "",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });

        var badges = new WrapPanel
        {
            HorizontalSpacing = 5,
            VerticalSpacing = 4,
        };
        if (candidate.MatchScore.HasValue)
            badges.Children.Add(BuildCandidateBadge($"Score {candidate.MatchScore.Value:F1}", filled: true));
        foreach (var source in candidate.Sources)
            badges.Children.Add(BuildCandidateBadge(source, filled: false));
        if (candidate.Sources.Count > 1)
            badges.Children.Add(BuildCandidateBadge($"{candidate.Sources.Count} sources agree", filled: true));
        if (badges.Children.Count > 0)
            info.Children.Add(badges);

        if (candidate.MatchReasons.Count > 0)
        {
            info.Children.Add(new TextBlock
            {
                Text = "Match reasons: " + string.Join(", ", candidate.MatchReasons.Select(
                    reason => reason.Replace('_', ' '))),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
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
            if (_applying) return;
            ApplyStatusText.Visibility = Visibility.Collapsed;
            ApplyMatchButton.Visibility = Visibility.Visible;
            ApplyMatchButton.IsEnabled = true;
            ApplyMatchButton.Content = "Apply Match";
        };

        return rowBtn;
    }

    private static Border BuildCandidateBadge(string text, bool filled)
    {
        return new Border
        {
            Background = filled
                ? (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"]
                : new SolidColorBrush(Colors.Transparent),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            },
        };
    }
}
