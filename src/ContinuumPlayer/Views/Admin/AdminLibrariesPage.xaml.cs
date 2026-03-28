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

    public AdminLibrariesPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminLibrariesViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Libraries.CollectionChanged += (_, _) => BuildLibraryRows();
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    // ===== Table Builder =====

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
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            LibrariesPanel.Children.Add(BuildLibraryRow(lib));
        }
    }

    private FrameworkElement BuildLibraryRow(Library lib)
    {
        var row = new Grid
        {
            Padding = new Thickness(20, 12, 20, 12),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        // ---- Name column ----
        var nameBlock = new TextBlock
        {
            Text = lib.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // ---- Type badge ----
        string typeText = lib.Type switch
        {
            "movies" => "Movies",
            "series" => "Series",
            "mixed" => "Mixed",
            _ => lib.Type
        };

        var typeBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 130, 130, 130)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        typeBadge.Child = new TextBlock
        {
            Text = typeText,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };

        // ---- Status column: enabled dot ----
        var statusColor = lib.Enabled
            ? Color.FromArgb(255, 63, 185, 80)
            : Color.FromArgb(255, 120, 120, 120);

        var statusDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(statusDot, lib.Enabled ? "Enabled" : "Disabled");

        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPanel.Children.Add(statusDot);
        statusPanel.Children.Add(new TextBlock
        {
            Text = lib.Enabled ? "Enabled" : "Disabled",
            FontSize = 12,
            Foreground = new SolidColorBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center
        });

        // ---- Actions ----
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        var scanBtn = MakeIconButton("\uE72C", "Scan library");
        var refreshBtn = MakeIconButton("\uE895", "Refresh metadata");
        var editBtn = MakeIconButton("\uE70F", "Edit library");
        var deleteBtn = MakeIconButton("\uE74D", "Delete library",
            Color.FromArgb(255, 220, 90, 90));

        var capturedLib = lib;
        scanBtn.Click += async (_, _) =>
        {
            await ViewModel.ScanLibraryCommand.ExecuteAsync(capturedLib.Id);
            ShowStatus(ViewModel.StatusMessage ?? "Scan started.");
        };
        refreshBtn.Click += async (_, _) =>
        {
            await ViewModel.RefreshMetadataCommand.ExecuteAsync(capturedLib.Id);
            ShowStatus(ViewModel.StatusMessage ?? "Refresh started.");
        };
        editBtn.Click += async (_, _) => await OpenEditDialogAsync(capturedLib);
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedLib);

        actionsPanel.Children.Add(scanBtn);
        actionsPanel.Children.Add(refreshBtn);
        actionsPanel.Children.Add(editBtn);
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(nameBlock, 0);
        Grid.SetColumn(typeBadge, 1);
        Grid.SetColumn(statusPanel, 2);
        Grid.SetColumn(actionsPanel, 3);

        row.Children.Add(nameBlock);
        row.Children.Add(typeBadge);
        row.Children.Add(statusPanel);
        row.Children.Add(actionsPanel);

        return row;
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

    // ===== Header Button Handlers =====

    private async void ScanAllButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ScanAllCommand.ExecuteAsync(null);
        ShowStatus(ViewModel.StatusMessage ?? "Scan all started.");
    }

    private async void AddLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync();
    }

    // ===== Create Dialog =====

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

    // ===== Edit Dialog =====

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

    // ===== Delete Dialog =====

    private async Task OpenDeleteDialogAsync(Library lib)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete Library",
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

    // ===== Form Builder =====

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

        // Enabled toggle
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

        // Build form layout
        var form = new StackPanel { Width = 380, Spacing = 14 };

        // Name row with enabled toggle
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
        enabledGroup.Children.Add(MakeFormLabel("Enabled"));
        enabledGroup.Children.Add(enabledSwitch);

        Grid.SetColumn(nameGroup, 0);
        Grid.SetColumn(enabledGroup, 1);
        nameRow.Children.Add(nameGroup);
        nameRow.Children.Add(enabledGroup);
        form.Children.Add(nameRow);

        // Type row
        var typeGroup = new StackPanel { Spacing = 6 };
        typeGroup.Children.Add(MakeFormLabel("Type"));
        typeGroup.Children.Add(typeCombo);
        form.Children.Add(typeGroup);

        // GetBody func
        object? GetBody()
        {
            string name = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return null;

            string selectedType = "movies";
            if (typeCombo.SelectedItem is ComboBoxItem selected && selected.Tag is string tag)
                selectedType = tag;

            return new
            {
                name,
                type = selectedType,
                enabled = enabledSwitch.IsOn
            };
        }

        return (form, GetBody);
    }

    // ===== Helpers =====

    private static TextBlock MakeFormLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
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
