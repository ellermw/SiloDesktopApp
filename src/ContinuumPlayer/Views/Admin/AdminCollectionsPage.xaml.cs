using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminCollectionsPage : Page
{
    public AdminCollectionsViewModel ViewModel { get; }

    // Track whether the picker is being updated programmatically so we don't re-trigger a load
    private bool _suppressPickerChange;

    public AdminCollectionsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminCollectionsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Collections.CollectionChanged += (_, _) => BuildCollectionRows();
            await ViewModel.LoadCommand.ExecuteAsync(null);
            PopulateLibraryPicker();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    // ===== Library Picker =====

    private void PopulateLibraryPicker()
    {
        _suppressPickerChange = true;

        LibraryPicker.Items.Clear();
        LibraryPicker.Items.Add(new ComboBoxItem { Content = "All Libraries", Tag = (int?)null });

        foreach (var lib in ViewModel.Libraries)
            LibraryPicker.Items.Add(new ComboBoxItem { Content = lib.Name, Tag = (int?)lib.Id });

        LibraryPicker.SelectedIndex = 0;
        _suppressPickerChange = false;
    }

    private async void LibraryPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPickerChange) return;
        if (LibraryPicker.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SelectedLibraryId = item.Tag as int?;
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    // ===== Table Builder =====

    private void BuildCollectionRows()
    {
        CollectionsPanel.Children.Clear();

        if (ViewModel.Collections.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var col in ViewModel.Collections)
        {
            if (!isFirst)
            {
                CollectionsPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            CollectionsPanel.Children.Add(BuildCollectionRow(col));
        }
    }

    private FrameworkElement BuildCollectionRow(LibraryCollection col)
    {
        var row = new Grid
        {
            Padding = new Thickness(20, 12, 20, 12),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        // ---- Title column ----
        var titlePanel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        titleRow.Children.Add(new TextBlock
        {
            Text = col.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (col.Featured)
        {
            titleRow.Children.Add(new FontIcon
            {
                Glyph = "\uE734",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 200, 0)),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        titleRow.Children.Add(MakeBadge(col.Visibility,
            col.Visibility == "visible"
                ? Color.FromArgb(255, 63, 185, 80)
                : Color.FromArgb(255, 130, 130, 130)));

        titlePanel.Children.Add(titleRow);

        if (!string.IsNullOrEmpty(col.Description))
        {
            titlePanel.Children.Add(new TextBlock
            {
                Text = col.Description,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 300
            });
        }

        // ---- Source column ----
        var sourcePanel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        sourcePanel.Children.Add(MakeBadge(col.CollectionType, Color.FromArgb(120, 130, 130, 130)));

        if (!string.IsNullOrEmpty(col.SourceUrl))
        {
            sourcePanel.Children.Add(new TextBlock
            {
                Text = col.SourceUrl,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 200
            });
        }
        else
        {
            sourcePanel.Children.Add(new TextBlock
            {
                Text = "Local metadata",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }

        // ---- Items column ----
        string itemsText = col.CollectionType == "smart" ? "\u2014" : col.ItemCount.ToString();
        var itemsBlock = new TextBlock
        {
            Text = itemsText,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        // ---- Sync Status column ----
        var syncPanel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        var syncColor = col.LastSyncStatus switch
        {
            "running" => Color.FromArgb(255, 59, 130, 246),
            "success" => Color.FromArgb(255, 63, 185, 80),
            "failed" => Color.FromArgb(255, 220, 90, 90),
            "warning" => Color.FromArgb(255, 245, 158, 11),
            _ => Color.FromArgb(255, 130, 130, 130) // idle / unknown
        };
        syncPanel.Children.Add(MakeBadge(col.LastSyncStatus, syncColor));

        string syncMsg = string.IsNullOrEmpty(col.LastSyncMessage) ? "Not synced yet" : col.LastSyncMessage;
        syncPanel.Children.Add(new TextBlock
        {
            Text = syncMsg,
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200
        });

        // ---- Updated column ----
        string updatedText = "";
        if (DateTime.TryParse(col.UpdatedAt, out var updatedDt))
            updatedText = updatedDt.ToLocalTime().ToString("g");
        else
            updatedText = col.UpdatedAt;

        var updatedBlock = new TextBlock
        {
            Text = updatedText,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        // ---- Actions column ----
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        var syncBtn = MakeIconButton("\uE72C", "Sync collection");
        var deleteBtn = MakeIconButton("\uE74D", "Delete collection", Color.FromArgb(255, 220, 90, 90));

        var capturedCol = col;
        syncBtn.Click += async (_, _) =>
        {
            syncBtn.IsEnabled = false;
            try
            {
                await ViewModel.SyncCollectionCommand.ExecuteAsync(capturedCol.Id);
                ShowStatus(ViewModel.StatusMessage ?? "Sync started.");
            }
            finally { syncBtn.IsEnabled = true; }
        };
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedCol);

        actionsPanel.Children.Add(syncBtn);
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(titlePanel, 0);
        Grid.SetColumn(sourcePanel, 1);
        Grid.SetColumn(itemsBlock, 2);
        Grid.SetColumn(syncPanel, 3);
        Grid.SetColumn(updatedBlock, 4);
        Grid.SetColumn(actionsPanel, 5);

        row.Children.Add(titlePanel);
        row.Children.Add(sourcePanel);
        row.Children.Add(itemsBlock);
        row.Children.Add(syncPanel);
        row.Children.Add(updatedBlock);
        row.Children.Add(actionsPanel);

        return row;
    }

    // ===== Header Button =====

    private async void AddCollectionButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync();
    }

    // ===== Create Dialog =====

    private async Task OpenCreateDialogAsync()
    {
        var (formContent, getBody) = BuildCollectionForm();

        var dialog = new ContentDialog
        {
            Title = "Add Collection",
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
                await ViewModel.LoadCommand.ExecuteAsync(null);
                ShowStatus("Collection created.");
            }
            catch { }
        }
    }

    // ===== Delete Dialog =====

    private async Task OpenDeleteDialogAsync(LibraryCollection col)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete Collection",
            Content = $"Delete collection \"{col.Title}\"? This action cannot be undone.",
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
                await ViewModel.DeleteCollectionCommand.ExecuteAsync(col.Id);
                ShowStatus(ViewModel.StatusMessage ?? "Collection deleted.");
            }
            catch { }
        }
    }

    // ===== Form Builder =====

    private (FrameworkElement Content, Func<ContinuumPlayer.Core.Models.Admin.CreateLibraryCollectionRequest?> GetBody) BuildCollectionForm()
    {
        var titleBox = new TextBox
        {
            PlaceholderText = "Collection title",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var descBox = new TextBox
        {
            PlaceholderText = "Description (optional)",
            AcceptsReturn = false,
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var sourceUrlBox = new TextBox
        {
            PlaceholderText = "Source URL (MDBList / TMDB, optional)",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var typeCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        typeCombo.Items.Add(new ComboBoxItem { Content = "Manual", Tag = "manual" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Smart", Tag = "smart" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "MDBList", Tag = "mdblist" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "TMDB", Tag = "tmdb" });
        typeCombo.SelectedIndex = 0;

        var visibilityCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        visibilityCombo.Items.Add(new ComboBoxItem { Content = "Visible", Tag = "visible" });
        visibilityCombo.Items.Add(new ComboBoxItem { Content = "Hidden", Tag = "hidden" });
        visibilityCombo.SelectedIndex = 0;

        var featuredSwitch = new ToggleSwitch
        {
            IsOn = false,
            OnContent = "Featured",
            OffContent = "Not featured"
        };

        // Library selector from loaded libraries
        var libCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        libCombo.Items.Add(new ComboBoxItem { Content = "— None —", Tag = (int?)null });
        foreach (var lib in ViewModel.Libraries)
            libCombo.Items.Add(new ComboBoxItem { Content = lib.Name, Tag = (int?)lib.Id });

        // Pre-select filtered library if one is active
        if (ViewModel.SelectedLibraryId.HasValue)
        {
            foreach (ComboBoxItem item in libCombo.Items)
            {
                if (item.Tag is int id && id == ViewModel.SelectedLibraryId.Value)
                {
                    libCombo.SelectedItem = item;
                    break;
                }
            }
        }
        if (libCombo.SelectedItem == null) libCombo.SelectedIndex = 0;

        var form = new StackPanel { Width = 380, Spacing = 14 };

        void AddField(string label, FrameworkElement control)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
            });
            group.Children.Add(control);
            form.Children.Add(group);
        }

        AddField("Title", titleBox);
        AddField("Description", descBox);
        AddField("Library", libCombo);
        AddField("Type", typeCombo);
        AddField("Visibility", visibilityCombo);
        AddField("Source URL", sourceUrlBox);

        var featuredGroup = new StackPanel { Spacing = 6 };
        featuredGroup.Children.Add(new TextBlock
        {
            Text = "Featured",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        });
        featuredGroup.Children.Add(featuredSwitch);
        form.Children.Add(featuredGroup);

        ContinuumPlayer.Core.Models.Admin.CreateLibraryCollectionRequest? GetBody()
        {
            string title = titleBox.Text.Trim();
            if (string.IsNullOrEmpty(title)) return null;

            string type = "manual";
            if (typeCombo.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string t)
                type = t;

            string visibility = "visible";
            if (visibilityCombo.SelectedItem is ComboBoxItem visItem && visItem.Tag is string v)
                visibility = v;

            int? libraryId = null;
            if (libCombo.SelectedItem is ComboBoxItem libItem && libItem.Tag is int lid)
                libraryId = lid;

            return new ContinuumPlayer.Core.Models.Admin.CreateLibraryCollectionRequest
            {
                Title = title,
                Description = string.IsNullOrEmpty(descBox.Text) ? null : descBox.Text.Trim(),
                LibraryId = libraryId,
                CollectionType = type,
                Visibility = visibility,
                SourceUrl = string.IsNullOrEmpty(sourceUrlBox.Text) ? null : sourceUrlBox.Text.Trim(),
                Featured = featuredSwitch.IsOn
            };
        }

        return (form, GetBody);
    }

    // ===== Helpers =====

    private static Border MakeBadge(string text, Color color)
    {
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        badge.Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color)
        };
        return badge;
    }

    private static Button MakeIconButton(string glyph, string tooltip, Color? fgColor = null)
    {
        var fg = fgColor.HasValue
            ? new SolidColorBrush(fgColor.Value)
            : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        var btn = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
                Foreground = fg
            }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

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
