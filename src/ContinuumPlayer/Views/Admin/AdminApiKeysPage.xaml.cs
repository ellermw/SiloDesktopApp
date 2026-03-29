using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminApiKeysPage : Page
{
    public AdminApiKeysViewModel ViewModel { get; }
    private bool _rebuildPending;

    public AdminApiKeysPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminApiKeysViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ApiKeys.CollectionChanged += (_, _) => ScheduleRebuild();

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildPending = false;
            RebuildRows();
        });
    }

    // ===== Row Builder =====

    private void RebuildRows()
    {
        KeysPanel.Children.Clear();

        if (ViewModel.ApiKeys.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;

        bool isFirst = true;
        foreach (var key in ViewModel.ApiKeys)
        {
            if (!isFirst)
            {
                KeysPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            isFirst = false;
            KeysPanel.Children.Add(BuildKeyRow(key));
        }
    }

    private FrameworkElement BuildKeyRow(AdminAPIKey key)
    {
        var row = new Grid
        {
            Padding = new Thickness(20, 14, 20, 14),
            ColumnSpacing = 12
        };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        // ---- Label ----
        var labelBlock = new TextBlock
        {
            Text = key.Label,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(labelBlock, 0);
        row.Children.Add(labelBlock);

        // ---- User ----
        var userBlock = new TextBlock
        {
            Text = key.Username,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(userBlock, 1);
        row.Children.Add(userBlock);

        // ---- Key (masked) + copy button ----
        string maskedKey = MaskKey(key.Key);
        var keyPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        // bg-muted rounded px-1.5 py-0.5 → SurfaceRaisedBrush for contrast inside the card
        var keyCodeBorder = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        keyCodeBorder.Child = new TextBlock
        {
            Text = maskedKey,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]
        };
        keyPanel.Children.Add(keyCodeBorder);

        // Copy button: web h-6 w-6 = 24px
        var copyBtn = MakeIconButton("\uE8C8", "Copy key", size: 24);
        var capturedKey = key;
        copyBtn.Click += (_, _) => CopyToClipboard(capturedKey.Key);
        keyPanel.Children.Add(copyBtn);

        Grid.SetColumn(keyPanel, 2);
        row.Children.Add(keyPanel);

        // ---- Tier dropdown (w-[120px] ComboBox — web uses inline Select) ----
        var tierCombo = new ComboBox
        {
            Width = 120,
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center
        };
        tierCombo.Items.Add(new ComboBoxItem { Content = "Standard", Tag = "standard" });
        tierCombo.Items.Add(new ComboBoxItem { Content = "Elevated", Tag = "elevated" });
        // Select current tier
        foreach (ComboBoxItem item in tierCombo.Items)
        {
            if (string.Equals(item.Tag as string, key.RateTier, StringComparison.OrdinalIgnoreCase))
            {
                tierCombo.SelectedItem = item;
                break;
            }
        }
        if (tierCombo.SelectedIndex < 0) tierCombo.SelectedIndex = 0;
        var capturedKeyForTier = capturedKey;
        tierCombo.SelectionChanged += async (_, _) =>
        {
            if (tierCombo.SelectedItem is ComboBoxItem selected && selected.Tag is string newTier
                && !string.Equals(newTier, capturedKeyForTier.RateTier, StringComparison.OrdinalIgnoreCase))
            {
                await ViewModel.UpdateTierCommand.ExecuteAsync((capturedKeyForTier.Id, newTier));
            }
        };
        Grid.SetColumn(tierCombo, 3);
        row.Children.Add(tierCombo);

        // ---- Created ----
        string createdText = "—";
        if (!string.IsNullOrEmpty(key.CreatedAt) && DateTime.TryParse(key.CreatedAt, out var createdDt))
            createdText = createdDt.ToLocalTime().ToString("d");

        var createdBlock = new TextBlock
        {
            Text = createdText,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(createdBlock, 4);
        row.Children.Add(createdBlock);

        // ---- Last Used ----
        string lastUsedText = "Never";
        if (!string.IsNullOrEmpty(key.LastUsedAt) && DateTime.TryParse(key.LastUsedAt, out var lastUsedDt))
            lastUsedText = lastUsedDt.ToLocalTime().ToString("d");

        var lastUsedBlock = new TextBlock
        {
            Text = lastUsedText,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lastUsedBlock, 5);
        row.Children.Add(lastUsedBlock);

        // ---- Actions ----
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Delete button: web h-7 w-7 = 28px
        var deleteBtn = MakeIconButton("\uE74D", "Delete key", size: 28, fgColor: Color.FromArgb(255, 220, 90, 90));
        deleteBtn.Click += async (_, _) => await OpenDeleteDialogAsync(capturedKey);

        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(actionsPanel, 6);
        row.Children.Add(actionsPanel);

        return row;
    }

    // ===== Create Key Button =====

    private async void CreateKeyButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenCreateDialogAsync();
    }

    // ===== Create Dialog (two-step) =====

    private async Task OpenCreateDialogAsync()
    {
        // Build form
        var labelBox = new TextBox
        {
            PlaceholderText = "e.g. CI/CD Pipeline",
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };

        var userCombo = new ComboBox
        {
            CornerRadius = new CornerRadius(8),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var u in ViewModel.Users)
        {
            userCombo.Items.Add(new ComboBoxItem
            {
                Content = u.Username,
                Tag = u.Id
            });
        }
        if (userCombo.Items.Count > 0)
            userCombo.SelectedIndex = 0;

        var form = new StackPanel { Width = 380, Spacing = 16 };

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

        AddField("Label", labelBox);
        AddField("User", userCombo);

        var createDialog = new ContentDialog
        {
            Title = "Create API Key",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = form,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await createDialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        string label = labelBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(label)) return;

        int? userId = null;
        if (userCombo.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is int uid)
            userId = uid;

        var request = new AdminCreateAPIKeyRequest { Label = label, UserId = userId };
        var created = await ViewModel.CreateKeyAsync(request);

        if (created == null)
        {
            if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
            return;
        }

        // Step 2: reveal the full key
        await ShowKeyRevealDialogAsync(created);

        if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
    }

    // ===== Key Reveal Dialog =====

    private async Task ShowKeyRevealDialogAsync(AdminAPIKey key)
    {
        var warningBlock = new TextBlock
        {
            Text = "Copy your API key now. You won't be able to see the full key again.",
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        // bg-muted block rounded p-3 — code block in key reveal dialog
        var keyBorder = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10)
        };
        keyBorder.Child = new TextBlock
        {
            Text = key.Key,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };

        // "Copy & Close" button — matches web: copies key then closes dialog
        var copyBtnReveal = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        copyBtnReveal.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new FontIcon { Glyph = "\uE8C8", FontSize = 13 },
                new TextBlock { Text = "Copy & Close" }
            }
        };

        var revealPanel = new StackPanel { Width = 420, Spacing = 14 };
        revealPanel.Children.Add(warningBlock);
        revealPanel.Children.Add(keyBorder);
        revealPanel.Children.Add(copyBtnReveal);

        var revealDialog = new ContentDialog
        {
            Title = "API Key Created",
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot,
            Content = revealPanel,
            DefaultButton = ContentDialogButton.Close
        };

        // Copy & Close: copy key and hide dialog
        copyBtnReveal.Click += (_, _) =>
        {
            CopyToClipboard(key.Key);
            revealDialog.Hide();
        };

        await revealDialog.ShowAsync();
    }

    // ===== Delete Dialog =====

    private async Task OpenDeleteDialogAsync(AdminAPIKey key)
    {
        var dialog = new ContentDialog
        {
            Title = "Revoke API Key",
            Content = $"Revoke API key \"{key.Label}\"? This action cannot be undone.",
            PrimaryButtonText = "Revoke",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await ViewModel.DeleteKeyCommand.ExecuteAsync(key.Id);
                if (ViewModel.StatusMessage != null) ShowStatus(ViewModel.StatusMessage);
            }
            catch { }
        }
    }

    // ===== Helpers =====

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length <= 10) return key;
        return key[..6] + "..." + key[^4..];
    }

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static Button MakeIconButton(string glyph, string tooltip, int size = 32, Color? fgColor = null)
    {
        var fg = fgColor.HasValue
            ? new SolidColorBrush(fgColor.Value)
            : (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        var btn = new Button
        {
            Width = size,
            Height = size,
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
