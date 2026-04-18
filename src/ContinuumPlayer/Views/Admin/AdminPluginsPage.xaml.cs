using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ContinuumPlayer.Core.Models.Plugins;
using ContinuumPlayer.ViewModels.Admin;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminPluginsPage : Page
{
    public AdminPluginsViewModel ViewModel { get; }
    private bool _rebuildPending;

    public AdminPluginsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminPluginsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Installations.CollectionChanged += (_, _) => ScheduleRebuild();
        ViewModel.CatalogEntries.CollectionChanged += (_, _) => ScheduleRebuild();
        ViewModel.Repositories.CollectionChanged += (_, _) => ScheduleRebuild();

        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }

        UpdateTabVisuals();
    }

    // ===== Check for updates =====
    // Matches web: triggers the "check_plugin_updates" scheduled task, then reloads plugin data.
    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButtonText.Text = "Checking updates...";
        try
        {
            var adminApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.AdminApi>();
            await adminApi.RunTaskAsync("check_plugin_updates");
            // Reload plugin catalog + installations after the task triggers.
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error checking updates: {ex.Message}";
        }
        finally
        {
            CheckUpdatesButtonText.Text = "Check for updates";
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildPending = false;
            RebuildAll();
        });
    }

    // ===== Tab switching =====

    private void TabInstalled_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedTab = "installed";
        UpdateTabVisuals();
    }

    private void TabAvailable_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedTab = "available";
        UpdateTabVisuals();
    }

    private void UpdateTabVisuals()
    {
        var accentBg = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"];
        var accentFg = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var secondaryFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        var transparent = new SolidColorBrush(Colors.Transparent);

        bool isInstalled = ViewModel.SelectedTab == "installed";

        TabInstalled.Background = isInstalled ? accentBg : transparent;
        TabInstalled.Foreground = isInstalled ? accentFg : secondaryFg;
        TabAvailable.Background = !isInstalled ? accentBg : transparent;
        TabAvailable.Foreground = !isInstalled ? accentFg : secondaryFg;

        InstalledPanel.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;
        AvailablePanel.Visibility = !isInstalled ? Visibility.Visible : Visibility.Collapsed;

        RebuildAll();
    }

    // ===== Rebuild =====

    private void RebuildAll()
    {
        RebuildInstalled();
        RebuildAvailable();
        RebuildRepos();
    }

    private void RebuildInstalled()
    {
        InstalledCards.Children.Clear();
        if (ViewModel.Installations.Count == 0)
        {
            InstalledEmpty.Visibility = Visibility.Visible;
            return;
        }
        InstalledEmpty.Visibility = Visibility.Collapsed;

        foreach (var plugin in ViewModel.Installations)
        {
            InstalledCards.Children.Add(BuildInstalledCard(plugin));
        }
    }

    private void RebuildAvailable()
    {
        AvailableCards.Children.Clear();
        // Filter out already-installed plugins
        var installedIds = ViewModel.Installations.Select(i => i.PluginId).ToHashSet();
        var available = ViewModel.CatalogEntries.Where(c => !installedIds.Contains(c.PluginId)).ToList();

        if (available.Count == 0)
        {
            AvailableEmpty.Visibility = Visibility.Visible;
            return;
        }
        AvailableEmpty.Visibility = Visibility.Collapsed;

        foreach (var entry in available)
        {
            AvailableCards.Children.Add(BuildAvailableCard(entry));
        }
    }

    private void RebuildRepos()
    {
        RepoRows.Children.Clear();
        if (ViewModel.Repositories.Count == 0)
        {
            RepoEmpty.Visibility = Visibility.Visible;
            return;
        }
        RepoEmpty.Visibility = Visibility.Collapsed;

        for (int i = 0; i < ViewModel.Repositories.Count; i++)
        {
            if (i > 0)
            {
                RepoRows.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
            RepoRows.Children.Add(BuildRepoRow(ViewModel.Repositories[i]));
        }
    }

    // ===== Installed Card =====

    private FrameworkElement BuildInstalledCard(PluginInstallation plugin)
    {
        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16, 20, 16)
        };

        var root = new Grid { ColumnSpacing = 12 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: plugin info
        var info = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = plugin.PluginId,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        titleRow.Children.Add(MakeOutlineBadge(plugin.Version));
        if (plugin.Enabled)
        {
            titleRow.Children.Add(MakeBadge("Enabled",
                Color.FromArgb(40, 34, 197, 94),
                Color.FromArgb(255, 34, 197, 94)));
        }
        else
        {
            titleRow.Children.Add(MakeBadge("Disabled",
                Color.FromArgb(40, 120, 120, 120),
                Color.FromArgb(255, 160, 160, 160)));
        }
        info.Children.Add(titleRow);

        var capText = string.Join(", ", plugin.Capabilities.Select(c => c.DisplayName));
        if (!string.IsNullOrEmpty(capText))
        {
            info.Children.Add(new TextBlock
            {
                Text = capText,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        Grid.SetColumn(info, 0);
        root.Children.Add(info);

        // Right: action buttons
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

        // Enable/Disable toggle
        var toggleBtn = new Button
        {
            Content = plugin.Enabled ? "Disable" : "Enable",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 13
        };
        var capturedPlugin = plugin;
        toggleBtn.Click += async (_, _) =>
        {
            toggleBtn.IsEnabled = false;
            await ViewModel.TogglePluginCommand.ExecuteAsync(capturedPlugin);
        };
        actions.Children.Add(toggleBtn);

        // Update button (if update available)
        if (!string.IsNullOrEmpty(plugin.AvailableVersion) && plugin.AvailableVersion != plugin.Version)
        {
            var updateBtn = new Button
            {
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 13
            };
            updateBtn.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = $"Update to {plugin.AvailableVersion}" }
                }
            };
            updateBtn.Click += async (_, _) =>
            {
                updateBtn.IsEnabled = false;
                await ViewModel.UpdatePluginCommand.ExecuteAsync(capturedPlugin.Id);
            };
            actions.Children.Add(updateBtn);
        }

        // Delete
        var deleteBtn = MakeIconButton("\uE74D", "Delete plugin", 28);
        deleteBtn.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Plugin",
                Content = $"Delete plugin \"{capturedPlugin.PluginId}\"? This cannot be undone.",
                PrimaryButtonText = "Delete",
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.DeletePluginCommand.ExecuteAsync(capturedPlugin.Id);
                ShowStatus("Plugin deleted.");
            }
        };
        actions.Children.Add(deleteBtn);

        Grid.SetColumn(actions, 1);
        root.Children.Add(actions);

        card.Child = root;
        return card;
    }

    // ===== Available Card =====

    private FrameworkElement BuildAvailableCard(PluginCatalogEntry entry)
    {
        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16, 20, 16)
        };

        var root = new Grid { ColumnSpacing = 12 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = entry.PluginId,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        titleRow.Children.Add(MakeOutlineBadge(entry.Version));
        info.Children.Add(titleRow);

        var capText = string.Join(", ", entry.Capabilities.Select(c => c.DisplayName));
        if (!string.IsNullOrEmpty(capText))
        {
            info.Children.Add(new TextBlock
            {
                Text = capText,
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        Grid.SetColumn(info, 0);
        root.Children.Add(info);

        var installBtn = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        installBtn.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new FontIcon { Glyph = "\uE896", FontSize = 12 },
                new TextBlock { Text = "Install" }
            }
        };
        var capturedEntry = entry;
        installBtn.Click += async (_, _) =>
        {
            installBtn.IsEnabled = false;
            await ViewModel.InstallPluginCommand.ExecuteAsync(capturedEntry);
            ShowStatus($"Plugin \"{capturedEntry.PluginId}\" installed.");
        };

        Grid.SetColumn(installBtn, 1);
        root.Children.Add(installBtn);

        card.Child = root;
        return card;
    }

    // ===== Repo Row =====

    private FrameworkElement BuildRepoRow(PluginRepository repo)
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 12, 16, 12),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = repo.DisplayName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        info.Children.Add(new TextBlock
        {
            Text = repo.Url,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(info, 0);
        row.Children.Add(info);

        var capturedRepo = repo;
        var deleteBtn = MakeIconButton("\uE74D", "Delete repository", 28);
        deleteBtn.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Repository",
                Content = $"Delete repository \"{capturedRepo.DisplayName}\"?",
                PrimaryButtonText = "Delete",
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteRepositoryCommand.ExecuteAsync(capturedRepo.Id);
                ShowStatus("Repository deleted.");
            }
        };
        Grid.SetColumn(deleteBtn, 1);
        row.Children.Add(deleteBtn);

        row.PointerEntered += (s, _) => { if (s is Grid g) g.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); };
        row.PointerExited += (s, _) => { if (s is Grid g) g.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); };
        return row;
    }

    // ===== Add Repository Dialog =====

    private async void AddRepoButton_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "e.g. My Plugins", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var urlBox = new TextBox { PlaceholderText = "https://example.com/repo", CornerRadius = new CornerRadius(6), FontSize = 13 };

        var form = new StackPanel { Width = 512, Spacing = 16 };
        AddField(form, "Display Name", nameBox);
        AddField(form, "Repository URL", urlBox);

        var dialog = new ContentDialog
        {
            Title = "Add Repository",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = form,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(urlBox.Text)) return;

        await ViewModel.AddRepositoryAsync(new CreatePluginRepositoryRequest
        {
            Url = urlBox.Text.Trim(),
            DisplayName = nameBox.Text.Trim(),
            Enabled = true
        });
        ShowStatus("Repository added.");
    }

    // ===== Helpers =====

    private static void AddField(StackPanel form, string label, FrameworkElement control)
    {
        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        group.Children.Add(control);
        form.Children.Add(group);
    }

    private static Border MakeBadge(string text, Color bg, Color fg)
    {
        return new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fg)
            }
        };
    }

    private static Border MakeOutlineBadge(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 160, 160, 160)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 160, 160, 160))
            }
        };
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
            Content = new FontIcon { Glyph = glyph, FontSize = 12, Foreground = fg }
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    private void ShowStatus(string message)
    {
        StatusBannerText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => { StatusBanner.Visibility = Visibility.Collapsed; timer.Stop(); };
        timer.Start();
    }
}
