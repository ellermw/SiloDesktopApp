using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminLibrariesPage : Page
{
    public AdminLibrariesViewModel ViewModel { get; }

    // Colors matching the web UI
    private static readonly Color EnabledColor = Color.FromArgb(255, 63, 185, 80);
    private static readonly Color DestructiveColor = Color.FromArgb(255, 220, 90, 90);
    private static readonly Color HealthyColor = Color.FromArgb(255, 52, 211, 153);     // emerald-400
    private static readonly Color HealthyBgColor = Color.FromArgb(12, 52, 211, 153);    // emerald-500/5
    private static readonly Color HealthyBorderColor = Color.FromArgb(64, 52, 211, 153);// emerald-500/25
    private static readonly Color UnhealthyBgColor = Color.FromArgb(12, 220, 90, 90);
    private static readonly Color UnhealthyBorderColor = Color.FromArgb(64, 220, 90, 90);

    public AdminLibrariesPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminLibrariesViewModel>();
        this.InitializeComponent();
    }

    // Cached brush lookups to avoid repeated resource dictionary access
    private SolidColorBrush _primaryText = null!;
    private SolidColorBrush _secondaryText = null!;
    private SolidColorBrush _tertiaryText = null!;
    private SolidColorBrush _borderBrush = null!;
    private SolidColorBrush _surfaceBrush = null!;
    private SolidColorBrush _accentBrush = null!;
    private SolidColorBrush _cardBg = null!;
    private bool _loaded;

    private void CacheBrushes()
    {
        if (_primaryText != null) return;
        _primaryText = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
        _secondaryText = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        _tertiaryText = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"];
        _borderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
        _surfaceBrush = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"];
        _accentBrush = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        _cardBg = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"];
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return; // Prevent double-subscription on re-navigation
        _loaded = true;
        CacheBrushes();

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            // Rebuild ONCE after all data is loaded — no CollectionChanged subscriptions needed for initial load
            RebuildAll();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void RebuildAll()
    {
        BuildLibraryRows();
        BuildSkippedRootsRows();
    }

    // ===================================================================
    //  Table Builder — 6 columns: Name | Paths | Type | Status | Last Scanned | Actions
    // ===================================================================

    private void BuildLibraryRows()
    {
        LibrariesPanel.Children.Clear();

        if (ViewModel.Libraries.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var lib in ViewModel.Libraries)
        {
            if (!isFirst)
            {
                LibrariesPanel.Children.Add(new Border
                {
                    BorderBrush = _borderBrush,
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            LibrariesPanel.Children.Add(BuildLibraryRow(lib));

            // If empty_root warning, add the expanded warning row below (full-width)
            if (lib.ScanWarningCode == "empty_root")
            {
                LibrariesPanel.Children.Add(new Border
                {
                    BorderBrush = _borderBrush,
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
                LibrariesPanel.Children.Add(BuildEmptyRootWarningRow(lib));
            }
        }
    }

    private FrameworkElement BuildLibraryRow(Library lib)
    {
        var row = new Grid
        {
            Padding = new Thickness(20, 12, 20, 12),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

        // ---- Col 0: Name (font-medium, just name text) ----
        var nameBlock = new TextBlock
        {
            Text = lib.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = _primaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        // ---- Col 1: Paths (font-mono text-xs, each path on its own line) ----
        var pathsColumn = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (lib.Paths.Count > 0)
        {
            foreach (var path in lib.Paths)
            {
                pathsColumn.Children.Add(new TextBlock
                {
                    Text = path,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = _secondaryText,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
        }

        // ---- Col 2: Type badge (secondary variant) ----
        string typeText = lib.Type switch
        {
            "movies" => "Movies",
            "series" => "Series",
            "mixed" => "Mixed",
            _ => lib.Type
        };

        var typeBadge = MakeBadge(typeText, "secondary");

        // ---- Col 3: Status column ----
        var statusColumn = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        statusColumn.Children.Add(MakeBadge(
            lib.Enabled ? "Enabled" : "Disabled",
            lib.Enabled ? "outline" : "destructive"));

        if (lib.ScanWarningCode == "empty_root")
        {
            statusColumn.Children.Add(MakeBadge("Empty root guarded", "destructive"));
        }

        // ---- Col 4: Last Scanned (text-muted-foreground text-xs) ----
        var lastScannedColumn = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        string lastScannedText = "Never";
        if (!string.IsNullOrEmpty(lib.LastScannedAt) && DateTimeOffset.TryParse(lib.LastScannedAt, out var lastScanned))
        {
            lastScannedText = lastScanned.LocalDateTime.ToString("g");
        }
        lastScannedColumn.Children.Add(new TextBlock
        {
            Text = lastScannedText,
            FontSize = 12,
            Foreground = _tertiaryText
        });

        if (!string.IsNullOrEmpty(lib.ScanWarningAt) && DateTimeOffset.TryParse(lib.ScanWarningAt, out var warningAt))
        {
            lastScannedColumn.Children.Add(new TextBlock
            {
                Text = $"Warning: {warningAt.LocalDateTime:g}",
                FontSize = 11,
                Foreground = new SolidColorBrush(DestructiveColor)
            });
        }

        // ---- Col 5: Actions (ghost icon buttons, 28x28) ----
        var actionsWrapper = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2
        };

        var capturedLib = lib;

        // Check mount (HardDrive icon)
        var mountBtn = MakeIconButton28("\uEDA2", "Check mount");
        mountBtn.Click += async (_, _) =>
        {
            await ViewModel.CheckMountCommand.ExecuteAsync(capturedLib.Id);
            DispatcherQueue.TryEnqueue(RebuildAll);
        };

        // Scan Library (RefreshCw icon)
        var scanBtn = MakeIconButton28("\uE72C", "Scan Library");
        scanBtn.Click += async (_, _) =>
        {
            await ViewModel.ScanLibraryCommand.ExecuteAsync(capturedLib.Id);
            ShowStatus(ViewModel.StatusMessage ?? "Scan started.");
        };

        // Refresh metadata (DatabaseBackup icon)
        var refreshBtn = MakeIconButton28("\uE895", "Refresh metadata");
        refreshBtn.Click += async (_, _) =>
        {
            await ViewModel.RefreshMetadataCommand.ExecuteAsync(capturedLib.Id);
            ShowStatus(ViewModel.StatusMessage ?? "Refresh started.");
        };

        actionsPanel.Children.Add(mountBtn);
        actionsPanel.Children.Add(scanBtn);
        actionsPanel.Children.Add(refreshBtn);

        // If empty root warning, add confirm cleanup button (Trash2 destructive)
        if (lib.ScanWarningCode == "empty_root")
        {
            var confirmCleanupBtn = MakeIconButton28("\uE74D", "Confirm empty root cleanup", DestructiveColor);
            confirmCleanupBtn.Click += async (_, _) =>
            {
                await OpenConfirmEmptyRootDialogAsync(capturedLib);
            };
            actionsPanel.Children.Add(confirmCleanupBtn);
        }

        // Edit (Pencil)
        var editBtn = MakeIconButton28("\uE70F", "Edit library");
        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedLib);
        actionsPanel.Children.Add(editBtn);

        // Delete (Trash2)
        var deleteBtn = MakeIconButton28("\uE74D", "Delete library");
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedLib);
        actionsPanel.Children.Add(deleteBtn);

        actionsWrapper.Children.Add(actionsPanel);

        // Show mount check inline result if we have one
        if (ViewModel.MountCheckResults.TryGetValue(lib.Id, out var mountCheck))
        {
            actionsWrapper.Children.Add(BuildMountCheckInlineResult(mountCheck, isWarningRow: false));
        }

        Grid.SetColumn(nameBlock, 0);
        Grid.SetColumn(pathsColumn, 1);
        Grid.SetColumn(typeBadge, 2);
        Grid.SetColumn(statusColumn, 3);
        Grid.SetColumn(lastScannedColumn, 4);
        Grid.SetColumn(actionsWrapper, 5);

        row.Children.Add(nameBlock);
        row.Children.Add(pathsColumn);
        row.Children.Add(typeBadge);
        row.Children.Add(statusColumn);
        row.Children.Add(lastScannedColumn);
        row.Children.Add(actionsWrapper);

        return row;
    }

    // ===================================================================
    //  Empty Root Warning Row (colSpan=6, bg-destructive/5)
    // ===================================================================

    private FrameworkElement BuildEmptyRootWarningRow(Library lib)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(12, 220, 90, 90)),
            Padding = new Thickness(20, 12, 20, 12)
        };

        var content = new StackPanel { Spacing = 8 };

        content.Children.Add(new TextBlock
        {
            Text = "Scan found 0 media files for this library. Cleanup was paused to avoid accidental deletion.",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(DestructiveColor),
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(new TextBlock
        {
            Text = lib.ScanWarningMessage
                   ?? "Run another scan after storage returns, or confirm deletion before the next empty-root scan.",
            FontSize = 13,
            Foreground = _secondaryText,
            TextWrapping = TextWrapping.Wrap
        });

        var capturedLib = lib;
        var checkMountBtn = new Button
        {
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            Padding = new Thickness(10, 6, 10, 6)
        };
        var checkMountContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        checkMountContent.Children.Add(new FontIcon { Glyph = "\uEDA2", FontSize = 13 });
        checkMountContent.Children.Add(new TextBlock { Text = "Check Mount", FontSize = 12 });
        checkMountBtn.Content = checkMountContent;
        checkMountBtn.Click += async (_, _) =>
        {
            await ViewModel.CheckMountCommand.ExecuteAsync(capturedLib.Id);
            BuildLibraryRows();
        };

        content.Children.Add(checkMountBtn);

        // Show mount check result if available
        if (ViewModel.MountCheckResults.TryGetValue(lib.Id, out var mountCheck))
        {
            content.Children.Add(BuildMountCheckInlineResult(mountCheck, isWarningRow: true));
        }

        border.Child = content;
        return border;
    }

    // ===================================================================
    //  Mount Check Inline Result
    // ===================================================================

    private FrameworkElement BuildMountCheckInlineResult(LibraryMountCheckResponse result, bool isWarningRow)
    {
        bool healthy = result.Healthy;
        var bgColor = healthy ? HealthyBgColor : UnhealthyBgColor;
        var borderColor = healthy ? HealthyBorderColor : UnhealthyBorderColor;
        var textColor = healthy ? HealthyColor : DestructiveColor;

        var container = new Border
        {
            Background = new SolidColorBrush(bgColor),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var stack = new StackPanel { Spacing = 4 };

        // Summary line
        stack.Children.Add(new TextBlock
        {
            Text = result.Summary,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(textColor),
            TextWrapping = TextWrapping.Wrap
        });

        // If healthy + warning row: show "Storage looks available again" message
        if (healthy && isWarningRow)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Storage looks available again. Run Scan Library to verify contents and clear the warning.",
                FontSize = 12,
                Foreground = _secondaryText,
                TextWrapping = TextWrapping.Wrap
            });
        }

        // If unhealthy, show failing roots
        if (!healthy)
        {
            foreach (var root in result.Roots.Where(r => !r.Reachable))
            {
                var rootText = root.Path;
                if (!string.IsNullOrEmpty(root.ErrorMessage))
                    rootText += $": {root.ErrorMessage}";

                var rootLine = new TextBlock
                {
                    FontSize = 12,
                    Foreground = _secondaryText,
                    TextWrapping = TextWrapping.Wrap
                };
                rootLine.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = root.Path,
                    FontFamily = new FontFamily("Consolas")
                });
                if (!string.IsNullOrEmpty(root.ErrorMessage))
                {
                    rootLine.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text = $": {root.ErrorMessage}"
                    });
                }
                stack.Children.Add(rootLine);
            }
        }

        // Checked timestamp
        string checkedText = "Checked ";
        if (DateTimeOffset.TryParse(result.CheckedAt, out var checkedAt))
            checkedText += checkedAt.LocalDateTime.ToString("g");
        else
            checkedText += result.CheckedAt;

        stack.Children.Add(new TextBlock
        {
            Text = checkedText,
            FontSize = 11,
            Foreground = _tertiaryText
        });

        container.Child = stack;
        return container;
    }

    // ===================================================================
    //  Skipped Roots Table
    // ===================================================================

    private void BuildSkippedRootsRows()
    {
        SkippedRootsPanel.Children.Clear();

        if (ViewModel.SkippedRoots.Count == 0)
        {
            SkippedRootsSection.Visibility = Visibility.Collapsed;
            return;
        }

        SkippedRootsSection.Visibility = Visibility.Visible;

        bool isFirst = true;
        foreach (var root in ViewModel.SkippedRoots)
        {
            if (!isFirst)
            {
                SkippedRootsPanel.Children.Add(new Border
                {
                    BorderBrush = _borderBrush,
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            SkippedRootsPanel.Children.Add(BuildSkippedRootRow(root));
        }
    }

    private FrameworkElement BuildSkippedRootRow(LibrarySkippedRoot root)
    {
        var row = new Grid
        {
            Padding = new Thickness(0, 8, 0, 8),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        // Root path (monospace)
        var pathBlock = new TextBlock
        {
            Text = root.RootPath,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = _primaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Library name
        var libNameBlock = new TextBlock
        {
            Text = root.LibraryName,
            FontSize = 13,
            Foreground = _primaryText,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Reason badge (outline)
        var reasonBadge = MakeBadge(root.Reason, "outline");

        // Sample file path
        var sampleBlock = new TextBlock
        {
            Text = root.SampleFilePath,
            FontSize = 12,
            Foreground = _tertiaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 400,
            VerticalAlignment = VerticalAlignment.Center
        };

        // First seen
        string firstSeenText = "";
        if (DateTimeOffset.TryParse(root.FirstSeenAt, out var firstSeen))
            firstSeenText = firstSeen.LocalDateTime.ToString("g");
        var firstSeenBlock = new TextBlock
        {
            Text = firstSeenText,
            FontSize = 12,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Last seen
        string lastSeenText = "";
        if (DateTimeOffset.TryParse(root.LastSeenAt, out var lastSeen))
            lastSeenText = lastSeen.LocalDateTime.ToString("g");
        var lastSeenBlock = new TextBlock
        {
            Text = lastSeenText,
            FontSize = 12,
            Foreground = _tertiaryText,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(pathBlock, 0);
        Grid.SetColumn(libNameBlock, 1);
        Grid.SetColumn(reasonBadge, 2);
        Grid.SetColumn(sampleBlock, 3);
        Grid.SetColumn(firstSeenBlock, 4);
        Grid.SetColumn(lastSeenBlock, 5);

        row.Children.Add(pathBlock);
        row.Children.Add(libNameBlock);
        row.Children.Add(reasonBadge);
        row.Children.Add(sampleBlock);
        row.Children.Add(firstSeenBlock);
        row.Children.Add(lastSeenBlock);

        return row;
    }

    // ===================================================================
    //  Badge Builder
    // ===================================================================

    /// <summary>
    /// Creates a badge matching shadcn/ui variants: secondary, outline, destructive.
    /// </summary>
    private static Border MakeBadge(string text, string variant)
    {
        Color bgColor, fgColor, borderColorVal;
        double borderWidth = 0;

        switch (variant)
        {
            case "destructive":
                bgColor = Color.FromArgb(30, 220, 90, 90);
                fgColor = DestructiveColor;
                borderColorVal = Colors.Transparent;
                break;
            case "outline":
                bgColor = Colors.Transparent;
                fgColor = Color.FromArgb(255, 144, 160, 181); // SecondaryText
                borderColorVal = Color.FromArgb(120, 130, 130, 130);
                borderWidth = 1;
                break;
            case "secondary":
            default:
                bgColor = Color.FromArgb(40, 144, 160, 181);
                fgColor = Color.FromArgb(255, 144, 160, 181);
                borderColorVal = Colors.Transparent;
                break;
        }

        var badge = new Border
        {
            Background = new SolidColorBrush(bgColor),
            BorderBrush = new SolidColorBrush(borderColorVal),
            BorderThickness = new Thickness(borderWidth),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fgColor)
        };
        return badge;
    }

    // ===================================================================
    //  Icon Button Builder (28x28, matching web h-7 w-7)
    // ===================================================================

    private Button MakeIconButton28(string glyph, string tooltip, Color? fgColor = null)
    {
        var fg = fgColor.HasValue
            ? new SolidColorBrush(fgColor.Value)
            : _secondaryText;

        var btn = new Button
        {
            Width = 28,
            Height = 28,
            MinWidth = 28,
            MinHeight = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = fg
            }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    // ===================================================================
    //  Header Button Handlers
    // ===================================================================

    private async void ScanAllButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ScanAllCommand.ExecuteAsync(null);
        ShowStatus(ViewModel.StatusMessage ?? "Scan all started.");
    }

    private async void AddLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync();
    }

    // ===================================================================
    //  Create Dialog
    // ===================================================================

    private async Task OpenCreateDialogAsync()
    {
        var (formContent, getBody) = BuildLibraryForm(null);

        var dialog = new ContentDialog
        {
            Title = "Add Library",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = formContent,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var body = getBody();
            if (body == null) return;

            try
            {
                await ViewModel.CreateLibraryCommand.ExecuteAsync(body);
                ShowStatus(ViewModel.StatusMessage ?? "Library created.");
            }
            catch { }
        }
    }

    // ===================================================================
    //  Edit Dialog
    // ===================================================================

    private async Task OpenEditDialogAsync(Library lib)
    {
        var (formContent, getBody) = BuildLibraryForm(lib);

        var dialog = new ContentDialog
        {
            Title = "Edit Library",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = formContent,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var body = getBody();
            if (body == null) return;

            try
            {
                await ViewModel.UpdateLibraryCommand.ExecuteAsync((lib.Id, body));
                ShowStatus(ViewModel.StatusMessage ?? "Library updated.");
            }
            catch { }
        }
    }

    // ===================================================================
    //  Delete Dialog
    // ===================================================================

    private async Task OpenDeleteDialogAsync(Library lib)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete library",
            Content = $"Delete library \"{lib.Name}\"? This action cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.DeleteLibraryCommand.ExecuteAsync(lib.Id);
                ShowStatus(ViewModel.StatusMessage ?? "Library deleted.");
            }
            catch { }
        }
    }

    // ===================================================================
    //  Confirm Empty Root Cleanup Dialog
    // ===================================================================

    private async Task OpenConfirmEmptyRootDialogAsync(Library lib)
    {
        var dialog = new ContentDialog
        {
            Title = "Confirm empty root cleanup",
            Content = $"If the next scan still finds 0 media files for \"{lib.Name}\", remove the library items?",
            PrimaryButtonText = "Confirm",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.ConfirmEmptyRootCleanupCommand.ExecuteAsync(lib.Id);
                ShowStatus(ViewModel.StatusMessage ?? "Empty root cleanup confirmed.");
            }
            catch { }
        }
    }

    // ===================================================================
    //  Form Builder — matches web: Name+Enabled on same row, Paths, Type+Poster
    // ===================================================================

    private (FrameworkElement Content, Func<object?> GetBody) BuildLibraryForm(Library? editingLib)
    {
        // Name field
        var nameBox = new TextBox
        {
            PlaceholderText = "Library name",
            Text = editingLib?.Name ?? "",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        // Enabled toggle — web uses Switch with label "Enabled"
        var enabledSwitch = new ToggleSwitch
        {
            IsOn = editingLib?.Enabled ?? true,
            OnContent = "Enabled",
            OffContent = "Disabled"
        };

        // Type ComboBox
        var typeCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        typeCombo.Items.Add(new ComboBoxItem { Content = "Movies", Tag = "movies" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Series", Tag = "series" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Mixed", Tag = "mixed" });

        // Pre-select current type
        string currentType = editingLib?.Type ?? "movies";
        foreach (ComboBoxItem item in typeCombo.Items)
        {
            if ((string)item.Tag == currentType)
            {
                typeCombo.SelectedItem = item;
                break;
            }
        }
        if (typeCombo.SelectedItem == null && typeCombo.Items.Count > 0)
            typeCombo.SelectedIndex = 0;

        // ---- Paths section ----
        var pathsPanel = new StackPanel { Spacing = 6 };
        var pathInputs = new List<TextBox>();

        void AddPathRow(string initialValue)
        {
            var tb = new TextBox
            {
                PlaceholderText = "/mnt/media/movies",
                Text = initialValue,
                CornerRadius = new CornerRadius(8),
                FontSize = 13,
                FontFamily = new FontFamily("Consolas"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            pathInputs.Add(tb);

            var rowGrid = new Grid { ColumnSpacing = 4 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var deleteBtn = new Button
            {
                Width = 36,
                Height = 36,
                MinWidth = 36,
                MinHeight = 36,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 13 },
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(deleteBtn, "Remove path");
            deleteBtn.Click += (_, _) =>
            {
                if (pathInputs.Count <= 1) return;
                pathInputs.Remove(tb);
                pathsPanel.Children.Remove(rowGrid);
                RefreshDeleteButtons();
            };

            Grid.SetColumn(tb, 0);
            Grid.SetColumn(deleteBtn, 1);
            rowGrid.Children.Add(tb);
            rowGrid.Children.Add(deleteBtn);
            pathsPanel.Children.Add(rowGrid);
        }

        void RefreshDeleteButtons()
        {
            bool moreThanOne = pathInputs.Count > 1;
            foreach (Grid g in pathsPanel.Children.Cast<Grid>())
            {
                if (g.Children.Count > 1 && g.Children[1] is Button btn)
                    btn.Visibility = moreThanOne ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Seed with existing paths or one blank row
        var initialPaths = editingLib?.Paths ?? [];
        if (initialPaths.Count == 0)
            AddPathRow("");
        else
            foreach (var p in initialPaths)
                AddPathRow(p);

        RefreshDeleteButtons();

        var addPathBtn = new Button
        {
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"]
        };
        var addPathContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        addPathContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 13 });
        addPathContent.Children.Add(new TextBlock { Text = "Add Path", FontSize = 12 });
        addPathBtn.Content = addPathContent;
        addPathBtn.Click += (_, _) =>
        {
            AddPathRow("");
            RefreshDeleteButtons();
        };

        // Build form layout
        var form = new StackPanel { Width = 420, Spacing = 14 };

        // Row 1: Name + Enabled (grid grid-cols-[1fr_auto] items-end gap-3)
        var nameRow = new Grid { ColumnSpacing = 12 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameGroup = new StackPanel { Spacing = 6 };
        nameGroup.Children.Add(MakeFormLabel("Name"));
        nameGroup.Children.Add(nameBox);

        var enabledGroup = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        enabledGroup.Children.Add(enabledSwitch);

        Grid.SetColumn(nameGroup, 0);
        Grid.SetColumn(enabledGroup, 1);
        nameRow.Children.Add(nameGroup);
        nameRow.Children.Add(enabledGroup);
        form.Children.Add(nameRow);

        // Row 2: Paths
        var pathsGroup = new StackPanel { Spacing = 6 };
        pathsGroup.Children.Add(MakeFormLabel("Paths"));
        pathsGroup.Children.Add(pathsPanel);
        pathsGroup.Children.Add(addPathBtn);
        form.Children.Add(pathsGroup);

        // Row 3: Type + Poster (grid grid-cols-2 items-end gap-3)
        var typeRow = new Grid { ColumnSpacing = 12 };
        typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var typeGroup = new StackPanel { Spacing = 6 };
        typeGroup.Children.Add(MakeFormLabel("Type"));
        typeGroup.Children.Add(typeCombo);
        Grid.SetColumn(typeGroup, 0);
        typeRow.Children.Add(typeGroup);

        // Poster section (only when editing)
        if (editingLib != null)
        {
            var posterGroup = new StackPanel { Spacing = 6 };
            posterGroup.Children.Add(MakeFormLabel("Poster"));

            var posterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            if (!string.IsNullOrEmpty(editingLib.PosterUrl))
            {
                var posterImg = new Image
                {
                    Height = 56,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(editingLib.PosterUrl))
                };
                var posterBorder = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = _borderBrush,
                    BorderThickness = new Thickness(1),
                    Child = posterImg
                };
                posterRow.Children.Add(posterBorder);
            }
            else
            {
                var placeholder = new Border
                {
                    Width = 100,
                    Height = 56,
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = _borderBrush,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromArgb(20, 144, 160, 181)),
                    Child = new FontIcon
                    {
                        Glyph = "\uEB9F",
                        FontSize = 16,
                        Foreground = _tertiaryText,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                posterRow.Children.Add(placeholder);
            }

            posterGroup.Children.Add(posterRow);
            Grid.SetColumn(posterGroup, 1);
            typeRow.Children.Add(posterGroup);
        }

        form.Children.Add(typeRow);

        // GetBody func
        object? GetBody()
        {
            string name = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return null;

            string selectedType = "movies";
            if (typeCombo.SelectedItem is ComboBoxItem selected && selected.Tag is string tag)
                selectedType = tag;

            var paths = pathInputs
                .Select(tb => tb.Text.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            return new
            {
                name,
                type = selectedType,
                enabled = enabledSwitch.IsOn,
                paths
            };
        }

        return (form, GetBody);
    }

    // ===================================================================
    //  Helpers
    // ===================================================================

    private TextBlock MakeFormLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = _secondaryText
    };

    private void ShowStatus(string message)
    {
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }
}
