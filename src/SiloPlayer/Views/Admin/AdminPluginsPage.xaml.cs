using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPluginsPage : Page
{
    public AdminPluginsViewModel ViewModel { get; }
    private bool _rebuildPending;
    private bool _syncingCommunityToggle;
    private int _installedPage;
    private int _catalogPage;
    private const int InstalledPageSize = 10;
    private const int CatalogPageSize = 12;

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
            var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
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
        TabInstalledText.Text = ViewModel.Installations.Count > 0 ? $"Installed  {ViewModel.Installations.Count}" : "Installed";
        TabCatalogText.Text = ViewModel.CatalogEntries.Count > 0 ? $"Catalog  {ViewModel.CatalogEntries.Count}" : "Catalog";
        SyncCommunityCatalogControl();
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
        var filtered = ViewModel.Installations.Where(p => PluginMatches(
            InstalledSearchBox.Text, p.PluginId, p.Presentation, p.Capabilities, p.SourceKind, p.RepositoryName)).ToList();
        InstalledMatchText.Text = string.IsNullOrWhiteSpace(InstalledSearchBox.Text)
            ? $"{filtered.Count} plugins"
            : $"{filtered.Count} of {ViewModel.Installations.Count}";
        if (filtered.Count == 0)
        {
            InstalledEmpty.Visibility = Visibility.Visible;
            InstalledPager.Visibility = Visibility.Collapsed;
            return;
        }
        InstalledEmpty.Visibility = Visibility.Collapsed;
        var pageCount = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)InstalledPageSize));
        _installedPage = Math.Min(_installedPage, pageCount - 1);
        foreach (var plugin in filtered.Skip(_installedPage * InstalledPageSize).Take(InstalledPageSize))
        {
            InstalledCards.Children.Add(BuildInstalledCard(plugin));
        }
        InstalledPager.Visibility = pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        InstalledPageText.Text = $"Page {_installedPage + 1} of {pageCount}";
    }

    private void RebuildAvailable()
    {
        AvailableCards.Children.Clear();
        var available = ViewModel.CatalogEntries.Where(c => PluginMatches(
            CatalogSearchBox.Text, c.PluginId, c.Presentation, c.Capabilities, c.SourceKind, c.RepositoryName)).ToList();
        CatalogMatchText.Text = string.IsNullOrWhiteSpace(CatalogSearchBox.Text)
            ? $"{available.Count} plugins"
            : $"{available.Count} of {ViewModel.CatalogEntries.Count}";

        if (available.Count == 0)
        {
            AvailableEmpty.Visibility = Visibility.Visible;
            CatalogPager.Visibility = Visibility.Collapsed;
            return;
        }
        AvailableEmpty.Visibility = Visibility.Collapsed;

        var pageCount = Math.Max(1, (int)Math.Ceiling(available.Count / (double)CatalogPageSize));
        _catalogPage = Math.Min(_catalogPage, pageCount - 1);
        foreach (var entry in available.Skip(_catalogPage * CatalogPageSize).Take(CatalogPageSize))
        {
            AvailableCards.Children.Add(BuildAvailableCard(entry));
        }
        CatalogPager.Visibility = pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        CatalogPageText.Text = $"Page {_catalogPage + 1} of {pageCount}";
    }

    private void InstalledSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _installedPage = 0;
        RebuildInstalled();
    }

    private void CatalogSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _catalogPage = 0;
        RebuildAvailable();
    }

    private void InstalledPrevious_Click(object sender, RoutedEventArgs e) { if (_installedPage > 0) { _installedPage--; RebuildInstalled(); } }
    private void InstalledNext_Click(object sender, RoutedEventArgs e) { _installedPage++; RebuildInstalled(); }
    private void CatalogPrevious_Click(object sender, RoutedEventArgs e) { if (_catalogPage > 0) { _catalogPage--; RebuildAvailable(); } }
    private void CatalogNext_Click(object sender, RoutedEventArgs e) { _catalogPage++; RebuildAvailable(); }

    private void SyncCommunityCatalogControl()
    {
        if (ViewModel.CatalogSettings is not { } settings) return;
        _syncingCommunityToggle = true;
        CommunityCatalogToggle.IsOn = settings.IncludeApprovedCommunityPlugins;
        CommunityCatalogDescription.Text = settings.IncludeApprovedCommunityPlugins
            ? $"Showing {settings.ApprovedCommunityPluginCount} approved community plugins."
            : settings.InstalledCommunityPluginCount > 0
                ? $"Hidden. {settings.InstalledCommunityPluginCount} installed community plugin(s) keep running, but updates are paused."
                : "Include plugins reviewed and approved by the Silo project.";
        _syncingCommunityToggle = false;
    }

    private async void CommunityCatalogToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingCommunityToggle || ViewModel.CatalogSettings is not { } settings) return;
        var requested = CommunityCatalogToggle.IsOn;
        if (!requested && settings.InstalledCommunityPluginCount > 0)
        {
            var dialog = new ContentDialog
            {
                Title = "Hide approved community plugins?",
                Content = $"{settings.InstalledCommunityPluginCount} installed community plugin(s) will keep running, but update discovery will pause until this catalog is included again.",
                PrimaryButtonText = "Hide and pause updates",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _syncingCommunityToggle = true;
                CommunityCatalogToggle.IsOn = true;
                _syncingCommunityToggle = false;
                return;
            }
        }
        CommunityCatalogToggle.IsEnabled = false;
        await ViewModel.SetApprovedCommunityCatalogAsync(requested);
        CommunityCatalogToggle.IsEnabled = true;
        SyncCommunityCatalogControl();
        if (ViewModel.StatusMessage is { } message) ShowStatus(message);
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
            Text = PluginDisplayName(plugin.PluginId, plugin.Presentation),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        titleRow.Children.Add(MakeOutlineBadge(plugin.Version));
        titleRow.Children.Add(MakeOutlineBadge(SourceLabel(plugin.SourceKind)));
        if (plugin.UpdatesPaused)
            titleRow.Children.Add(MakeOutlineBadge("Updates paused"));
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

        info.Children.Add(new TextBlock
        {
            Text = PluginSummary(plugin.Presentation, plugin.Capabilities),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 700,
        });
        if (!string.IsNullOrWhiteSpace(plugin.Presentation?.PublisherName))
            info.Children.Add(new TextBlock
            {
                Text = $"By {plugin.Presentation.PublisherName}",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            });

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

        if (plugin.GlobalConfigSchema.Count > 0)
        {
            var configure = new Button { Content = "Configure", Padding = new Thickness(12, 6, 12, 6) };
            configure.Click += async (_, _) => await OpenPluginConfigurationAsync(plugin);
            actions.Children.Add(configure);
        }

        var updatePolicy = new ComboBox { Width = 78, VerticalAlignment = VerticalAlignment.Center };
        foreach (var policy in new[] { ("auto", "Auto"), ("manual", "Manual"), ("pinned", "Pinned") })
            updatePolicy.Items.Add(new ComboBoxItem { Content = policy.Item2, Tag = policy.Item1, IsSelected = string.Equals(plugin.UpdatePolicy, policy.Item1, StringComparison.OrdinalIgnoreCase) });
        var capturedPlugin = plugin;
        updatePolicy.SelectionChanged += async (_, _) =>
        {
            if (updatePolicy.SelectedItem is not ComboBoxItem { Tag: string policy } || string.Equals(policy, capturedPlugin.UpdatePolicy, StringComparison.OrdinalIgnoreCase)) return;
            updatePolicy.IsEnabled = false;
            await App.Services.GetRequiredService<SiloPlayer.Core.Api.PluginsApi>().UpdateInstallationAsync(capturedPlugin.Id, new SiloPlayer.Core.Models.Plugins.UpdatePluginInstallationRequest { UpdatePolicy = policy });
            await ViewModel.LoadCommand.ExecuteAsync(null);
        };
        actions.Children.Add(updatePolicy);

        var enabledToggle = new ToggleSwitch { IsOn = plugin.Enabled, OnContent = "", OffContent = "" };
        enabledToggle.Toggled += async (_, _) =>
        {
            if (enabledToggle.IsOn == capturedPlugin.Enabled) return;
            enabledToggle.IsEnabled = false;
            await ViewModel.TogglePluginCommand.ExecuteAsync(capturedPlugin);
        };
        actions.Children.Add(enabledToggle);

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

    private async Task OpenPluginConfigurationAsync(PluginInstallation plugin)
    {
        var panel = new StackPanel { Spacing = 18, MinWidth = 520 };
        var editors = new List<(PluginConfigSchema Schema, Dictionary<string, FrameworkElement> Fields)>();
        foreach (var schema in plugin.GlobalConfigSchema)
        {
            panel.Children.Add(new TextBlock { Text = schema.Title, FontSize = 16, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrWhiteSpace(schema.Description))
                panel.Children.Add(new TextBlock { Text = schema.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });

            var existing = plugin.GlobalConfigs.FirstOrDefault(config => config.Key == schema.Key)?.Value ?? [];
            var fields = new Dictionary<string, FrameworkElement>();
            foreach (var field in schema.AdminForm?.Fields ?? [])
            {
                var group = new StackPanel { Spacing = 5 };
                group.Children.Add(new TextBlock { Text = field.Label, FontWeight = FontWeights.SemiBold });
                var current = existing.TryGetValue(field.Key, out var value) ? value?.ToString() ?? "" : field.DefaultValue?.ToString() ?? "";
                FrameworkElement editor;
                if (field.Control.Equals("TOGGLE", StringComparison.OrdinalIgnoreCase))
                    editor = new ToggleSwitch { IsOn = bool.TryParse(current, out var enabled) && enabled, OnContent = "", OffContent = "" };
                else if (field.Options is { Count: > 0 })
                {
                    var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                    foreach (var option in field.Options)
                        combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value, IsSelected = string.Equals(option.Value, current, StringComparison.Ordinal) });
                    editor = combo;
                }
                else if (field.Secret)
                    editor = new PasswordBox { PlaceholderText = string.IsNullOrWhiteSpace(current) ? field.Placeholder ?? "" : "configured" };
                else
                    editor = new TextBox { Text = current, PlaceholderText = field.Placeholder ?? "", AcceptsReturn = field.Multiline, MinHeight = field.Multiline ? 90 : 0, TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
                group.Children.Add(editor);
                if (!string.IsNullOrWhiteSpace(field.Description))
                    group.Children.Add(new TextBlock { Text = field.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
                panel.Children.Add(group);
                fields[field.Key] = editor;
            }
            editors.Add((schema, fields));
        }

        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = $"Configure {PluginDisplayName(plugin.PluginId, plugin.Presentation)}", PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, Content = new ScrollViewer { Content = panel, MaxHeight = 620 } };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.PluginsApi>();
        foreach (var (schema, fields) in editors)
        {
            var values = new Dictionary<string, object>();
            foreach (var (key, editor) in fields)
            {
                object? value = editor switch
                {
                    ToggleSwitch toggle => toggle.IsOn,
                    ComboBox combo when combo.SelectedItem is ComboBoxItem selected => selected.Tag?.ToString() ?? "",
                    PasswordBox password => password.Password,
                    TextBox text => text.Text,
                    _ => "",
                };
                if (editor is PasswordBox && string.IsNullOrEmpty((string)value)) continue;
                values[key] = value;
            }
            await api.SaveGlobalConfigAsync(plugin.Id, new SavePluginConfigRequest { Key = schema.Key, Value = values });
        }
        ShowStatus("Plugin configuration saved.");
        await ViewModel.LoadCommand.ExecuteAsync(null);
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
            Text = PluginDisplayName(entry.PluginId, entry.Presentation),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        titleRow.Children.Add(MakeOutlineBadge(entry.Version));
        titleRow.Children.Add(MakeOutlineBadge(SourceLabel(entry.SourceKind)));
        info.Children.Add(titleRow);

        info.Children.Add(new TextBlock
        {
            Text = PluginSummary(entry.Presentation, entry.Capabilities),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 700,
        });

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
        var alreadyInstalled = ViewModel.Installations.Any(i => i.PluginId == entry.PluginId);
        installBtn.Content = alreadyInstalled ? new TextBlock { Text = "Installed" } : new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new FontIcon { Glyph = "\uE896", FontSize = 12 },
                new TextBlock { Text = "Install" }
            }
        };
        installBtn.IsEnabled = !alreadyInstalled;
        var capturedEntry = entry;
        installBtn.Click += async (_, _) =>
        {
            if (alreadyInstalled) return;
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
        info.Children.Add(MakeOutlineBadge(SourceLabel(repo.SourceKind)));
        Grid.SetColumn(info, 0);
        row.Children.Add(info);

        var capturedRepo = repo;
        if (repo.Managed)
        {
            var managed = new TextBlock
            {
                Text = "Managed by Silo",
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(managed, 1);
            row.Children.Add(managed);
            return row;
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var toggleRepo = new Button
        {
            Content = repo.Enabled ? "Disable" : "Enable",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Padding = new Thickness(10, 4, 10, 4),
        };
        toggleRepo.Click += async (_, _) =>
        {
            toggleRepo.IsEnabled = false;
            await ViewModel.ToggleRepositoryAsync(capturedRepo);
        };
        actions.Children.Add(toggleRepo);
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
        actions.Children.Add(deleteBtn);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);

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

    private static string SourceLabel(string sourceKind) => sourceKind switch
    {
        "silo" => "Silo maintained",
        "approved_community" => "Approved community",
        _ => "External source",
    };

    private static string PluginDisplayName(string pluginId, PluginPresentation? presentation)
    {
        if (!string.IsNullOrWhiteSpace(presentation?.DisplayName))
            return presentation.DisplayName.Trim();
        var trimmed = pluginId.StartsWith("silo", StringComparison.OrdinalIgnoreCase)
            ? pluginId[4..].TrimStart('.', '_', '-')
            : pluginId;
        return string.Join(" ", trimmed.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string PluginSummary(PluginPresentation? presentation, IEnumerable<PluginCapability> capabilities)
        => !string.IsNullOrWhiteSpace(presentation?.Summary)
            ? presentation.Summary.Trim()
            : capabilities.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Description))?.Description?.Trim()
                ?? "No description provided.";

    private static bool PluginMatches(string query, string pluginId, PluginPresentation? presentation,
        IEnumerable<PluginCapability> capabilities, string sourceKind, string? repositoryName)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var searchable = string.Join('\n', new[]
        {
            pluginId,
            PluginDisplayName(pluginId, presentation),
            presentation?.Summary,
            presentation?.DescriptionMarkdown,
            presentation?.PublisherName,
            repositoryName,
            SourceLabel(sourceKind),
            string.Join('\n', capabilities.SelectMany(c => new[] { c.DisplayName, c.Description, c.Type }))
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return searchable.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

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
