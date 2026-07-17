using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Text.Json;
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
    private int _catalogColumns;
    private Windows.Storage.StorageFile? _selectedPluginFile;
    private readonly List<Window> _pluginRouteWindows = [];
    private const int InstalledPageSize = 10;
    private const int CatalogPageSize = 12;

    public AdminPluginsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminPluginsViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Installations.CollectionChanged += Plugins_CollectionChanged;
        ViewModel.CatalogEntries.CollectionChanged += Plugins_CollectionChanged;
        ViewModel.Repositories.CollectionChanged += Plugins_CollectionChanged;

        if (ViewModel.Installations.Count > 0 || ViewModel.CatalogEntries.Count > 0 || ViewModel.Repositories.Count > 0)
            RebuildAll();

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

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Installations.CollectionChanged -= Plugins_CollectionChanged;
        ViewModel.CatalogEntries.CollectionChanged -= Plugins_CollectionChanged;
        ViewModel.Repositories.CollectionChanged -= Plugins_CollectionChanged;
        base.OnNavigatedFrom(e);
    }

    private void Plugins_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => ScheduleRebuild();

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
        var accentFg = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"];
        var secondaryFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];
        var border = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
        var transparent = new SolidColorBrush(Colors.Transparent);

        bool isInstalled = ViewModel.SelectedTab == "installed";

        TabInstalled.Background = transparent;
        TabInstalled.Foreground = isInstalled ? accentFg : secondaryFg;
        TabInstalled.BorderBrush = isInstalled ? accentFg : transparent;
        TabAvailable.Background = transparent;
        TabAvailable.Foreground = !isInstalled ? accentFg : secondaryFg;
        TabAvailable.BorderBrush = !isInstalled ? accentFg : transparent;

        InstalledPanel.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;
        AvailablePanel.Visibility = !isInstalled ? Visibility.Visible : Visibility.Collapsed;

        RebuildAll();
    }

    // ===== Rebuild =====

    private void RebuildAll()
    {
        InstalledCountText.Text = ViewModel.Installations.Count.ToString();
        CatalogCountText.Text = ViewModel.CatalogEntries.Count.ToString();
        InstalledCountBadge.Visibility = ViewModel.Installations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CatalogCountBadge.Visibility = ViewModel.CatalogEntries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        AvailableCards.ColumnDefinitions.Clear();
        AvailableCards.RowDefinitions.Clear();
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
        var visibleEntries = available.Skip(_catalogPage * CatalogPageSize).Take(CatalogPageSize).ToList();
        var availableWidth = AvailableCards.ActualWidth > 0 ? AvailableCards.ActualWidth : ActualWidth;
        _catalogColumns = availableWidth >= 1120 ? 3 : availableWidth >= 720 ? 2 : 1;
        for (var column = 0; column < _catalogColumns; column++)
            AvailableCards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < (int)Math.Ceiling(visibleEntries.Count / (double)_catalogColumns); row++)
            AvailableCards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < visibleEntries.Count; index++)
        {
            var card = BuildAvailableCard(visibleEntries[index]);
            Grid.SetColumn(card, index % _catalogColumns);
            Grid.SetRow(card, index / _catalogColumns);
            AvailableCards.Children.Add(card);
        }
        CatalogPager.Visibility = pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        CatalogPageText.Text = $"Page {_catalogPage + 1} of {pageCount}";
    }

    private void AvailableCards_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var nextColumns = e.NewSize.Width >= 1120 ? 3 : e.NewSize.Width >= 720 ? 2 : 1;
        if (_catalogColumns != 0 && nextColumns != _catalogColumns && ViewModel.SelectedTab != "installed")
            RebuildAvailable();
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

    private async void ChoosePluginFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        _selectedPluginFile = file;
        ChosenPluginFileText.Text = file.Name;
        UploadPluginButton.IsEnabled = true;
    }

    private async void UploadPluginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPluginFile is null) return;
        ChoosePluginFileButton.IsEnabled = false;
        UploadPluginButton.IsEnabled = false;
        UploadPluginButtonText.Text = "Uploading...";
        try
        {
            await using var source = await _selectedPluginFile.OpenStreamForReadAsync();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            var contentType = string.Equals(_selectedPluginFile.FileType, ".zip", StringComparison.OrdinalIgnoreCase)
                ? "application/zip"
                : "application/octet-stream";
            var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.PluginsApi>();
            var progress = new Progress<int>(percent => UploadPluginButtonText.Text = $"Uploading {percent}%");
            await api.UploadPluginAsync(_selectedPluginFile.Name, buffer.ToArray(), contentType, progress);
            _selectedPluginFile = null;
            ChosenPluginFileText.Text = "Choose plugin file...";
            ShowStatus("Plugin uploaded.");
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Plugin upload failed: {ex.Message}";
            ShowStatus(ViewModel.ErrorMessage, isError: true);
        }
        finally
        {
            ChoosePluginFileButton.IsEnabled = true;
            UploadPluginButton.IsEnabled = _selectedPluginFile is not null;
            UploadPluginButtonText.Text = "Upload";
        }
    }

    private void SyncCommunityCatalogControl()
    {
        if (ViewModel.CatalogSettings is not { } settings) return;
        _syncingCommunityToggle = true;
        CommunityCatalogToggle.IsOn = settings.IncludeApprovedCommunityPlugins;
        CommunityCatalogDescription.Text = settings.MigratedPluginCount > 0
            ? $"{settings.MigratedPluginCount} existing {(settings.MigratedPluginCount == 1 ? "installation was" : "installations were")} moved here without changing configuration."
            : "";
        CommunityCatalogDescription.Visibility = settings.MigratedPluginCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        var catalogEntry = ViewModel.CatalogEntries.FirstOrDefault(entry =>
            string.Equals(entry.PluginId, plugin.PluginId, StringComparison.Ordinal));
        var presentation = catalogEntry?.Presentation ?? plugin.Presentation;
        var repositoryUrl = plugin.RepoUrl ?? catalogEntry?.RepoUrl;
        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(12),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20)
        };

        var root = new Grid { ColumnSpacing = 20 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: the WebUI uses a fixed plugin glyph followed by a dense metadata stack.
        var left = new Grid { ColumnSpacing = 16 };
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(12),
            Background = plugin.Enabled
                ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                : (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            Child = new FontIcon
            {
                Glyph = "\uE74C",
                FontSize = 20,
                Foreground = plugin.Enabled
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };
        left.Children.Add(icon);

        var info = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = PluginDisplayName(plugin.PluginId, presentation),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        });
        titleRow.Children.Add(MakeOutlineBadge(plugin.Version));
        titleRow.Children.Add(MakeOutlineBadge(SourceLabel(plugin.SourceKind)));
        if (plugin.UpdatesPaused)
            titleRow.Children.Add(MakeOutlineBadge("Updates paused"));
        var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        state.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(plugin.Enabled
                ? Color.FromArgb(255, 34, 197, 94)
                : Color.FromArgb(255, 112, 112, 120))
        });
        state.Children.Add(new TextBlock
        {
            Text = plugin.Enabled ? "Active" : "Inactive",
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        titleRow.Children.Add(state);
        info.Children.Add(titleRow);

        info.Children.Add(new TextBlock
        {
            Text = plugin.PluginId,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });

        info.Children.Add(new TextBlock
        {
            Text = PluginSummary(presentation, plugin.Capabilities),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        if (plugin.Capabilities.Count > 0)
        {
            var capabilities = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var capability in plugin.Capabilities)
                capabilities.Children.Add(MakeCapabilityBadge(string.IsNullOrWhiteSpace(capability.DisplayName)
                    ? CapabilityLabel(capability.Type)
                    : capability.DisplayName));
            info.Children.Add(capabilities);
        }

        var resources = BuildResourceLinks(presentation, repositoryUrl);
        if (resources is not null) info.Children.Add(resources);

        Grid.SetColumn(info, 1);
        left.Children.Add(info);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        // Right: action buttons
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

        var adminRoutes = plugin.Routes
            .Where(route => route.Navigable && route.NavigationKind == "admin")
            .ToList();
        foreach (var route in adminRoutes)
        {
            var routeButton = new Button
            {
                Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                Padding = new Thickness(12, 6, 12, 6),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8A7", FontSize = 12 },
                        new TextBlock { Text = string.IsNullOrWhiteSpace(route.NavigationLabel) ? route.Path : route.NavigationLabel }
                    }
                }
            };
            routeButton.Click += async (_, _) => await OpenPluginRouteAsync(plugin, route);
            actions.Children.Add(routeButton);
        }

        var configure = new Button
        {
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Padding = adminRoutes.Count > 0 ? new Thickness(8) : new Thickness(12, 6, 12, 6),
            Content = adminRoutes.Count > 0
                ? new FontIcon { Glyph = "\uE713", FontSize = 12 }
                : new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE713", FontSize = 12 },
                        new TextBlock { Text = "Configure" }
                    }
                }
        };
        if (adminRoutes.Count > 0) ToolTipService.SetToolTip(configure, "Plugin settings");
        configure.Click += async (_, _) => await OpenPluginConfigurationAsync(plugin);
        actions.Children.Add(configure);

        var updatePolicy = new ComboBox { Width = 78, VerticalAlignment = VerticalAlignment.Center };
        foreach (var policy in new[] { ("auto", "Auto"), ("notify", "Notify"), ("off", "Off") })
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

        var enabledToggle = new ToggleSwitch
        {
            IsOn = plugin.Enabled,
            OnContent = "",
            OffContent = "",
            Width = 40,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        enabledToggle.Toggled += async (_, _) =>
        {
            if (enabledToggle.IsOn == capturedPlugin.Enabled) return;
            enabledToggle.IsEnabled = false;
            await ViewModel.TogglePluginCommand.ExecuteAsync(capturedPlugin);
        };
        actions.Children.Add(new Border
        {
            Width = 48,
            Height = 32,
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Child = enabledToggle
        });

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
        var authCapabilities = plugin.Capabilities.Where(capability => capability.Type == "auth_provider.v1").ToList();
        var taskCapabilities = plugin.Capabilities.Where(capability => capability.Type == "scheduled_task.v1").ToList();
        var adminRoutes = plugin.Routes.Where(route => route.Navigable && route.NavigationKind == "admin").ToList();
        if (plugin.GlobalConfigSchema.Count == 0 && authCapabilities.Count == 0 &&
            taskCapabilities.Count == 0 && adminRoutes.Count == 0)
        {
            var emptyDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = BuildPluginDialogTitle(plugin),
                Content = new StackPanel
                {
                    Spacing = 20,
                    MinWidth = 580,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Configure bindings, credentials, and runtime settings.",
                            FontSize = 13,
                            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
                        },
                        new TextBlock
                        {
                            Text = "This plugin has no additional configuration.",
                            FontSize = 13,
                            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(0, 20, 0, 20)
                        }
                    }
                },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.None
            };
            await emptyDialog.ShowAsync();
            return;
        }

        var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.PluginsApi>();
        var panel = new StackPanel { Spacing = 18, MinWidth = 560 };
        panel.Children.Add(new TextBlock
        {
            Text = "Configure bindings, credentials, and runtime settings.",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });

        if (plugin.GlobalConfigSchema.Count > 0)
            panel.Children.Add(BuildPluginSectionHeading("\uE713", "Global Configuration"));
        foreach (var schema in plugin.GlobalConfigSchema)
        {
            var schemaPanel = new StackPanel { Spacing = 10 };
            schemaPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(schema.Title) ? schema.Key : schema.Title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrWhiteSpace(schema.Description))
                schemaPanel.Children.Add(new TextBlock { Text = schema.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });

            var existing = plugin.GlobalConfigs.FirstOrDefault(config => config.Key == schema.Key)?.Value ?? [];
            var fields = new Dictionary<string, FrameworkElement>();
            var formFields = GetPluginConfigFields(schema);
            if (formFields.Count == 0)
            {
                schemaPanel.Children.Add(new TextBlock
                {
                    Text = "This plugin uses a configuration schema shape that the admin form does not support yet.",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 245, 158, 11))
                });
            }
            foreach (var field in formFields)
            {
                var group = new StackPanel { Spacing = 5 };
                group.Children.Add(new TextBlock { Text = field.Label, FontWeight = FontWeights.SemiBold });
                var current = existing.TryGetValue(field.Key, out var value)
                    ? PluginValueString(value)
                    : PluginValueString(field.DefaultValue);
                FrameworkElement editor;
                if (field.Control.Equals("TOGGLE", StringComparison.OrdinalIgnoreCase) ||
                    field.Control.Equals("SWITCH", StringComparison.OrdinalIgnoreCase))
                    editor = new ToggleSwitch { IsOn = bool.TryParse(current, out var enabled) && enabled, OnContent = "", OffContent = "" };
                else if (field.Options is { Count: > 0 })
                {
                    var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                    foreach (var option in field.Options)
                        combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value, IsSelected = string.Equals(option.Value, current, StringComparison.Ordinal) });
                    editor = combo;
                }
                else if (field.Control.Equals("NUMBER", StringComparison.OrdinalIgnoreCase) ||
                         field.Control.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
                    editor = new NumberBox
                    {
                        Value = double.TryParse(current, out var number) ? number : double.NaN,
                        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                        Tag = field.Control.ToUpperInvariant()
                    };
                else if (field.Secret)
                    editor = new PasswordBox { PlaceholderText = string.IsNullOrWhiteSpace(current) ? field.Placeholder ?? "" : "configured" };
                else
                    editor = new TextBox { Text = current, PlaceholderText = field.Placeholder ?? "", AcceptsReturn = field.Multiline, MinHeight = field.Multiline ? 90 : 0, TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
                group.Children.Add(editor);
                if (!string.IsNullOrWhiteSpace(field.Description))
                    group.Children.Add(new TextBlock { Text = field.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
                schemaPanel.Children.Add(group);
                fields[field.Key] = editor;
            }
            if (fields.Count > 0)
            {
                var save = new Button
                {
                    Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Content = string.IsNullOrWhiteSpace(schema.AdminForm?.SubmitLabel) ? "Save config" : schema.AdminForm.SubmitLabel
                };
                save.Click += async (_, _) =>
                {
                    save.IsEnabled = false;
                    try
                    {
                        var values = ReadPluginFormValues(fields);
                        await api.SaveGlobalConfigAsync(plugin.Id, new SavePluginConfigRequest { Key = schema.Key, Value = values });
                        ShowStatus("Plugin configuration saved.");
                    }
                    catch (Exception ex)
                    {
                        ShowStatus($"Could not save plugin configuration: {ex.Message}", isError: true);
                    }
                    finally { save.IsEnabled = true; }
                };
                schemaPanel.Children.Add(save);
            }
            panel.Children.Add(BuildPluginSectionCard(schemaPanel));
        }

        if (authCapabilities.Count > 0)
        {
            panel.Children.Add(BuildPluginSectionHeading("\uE72E", "Auth Providers"));
            var authPanel = new StackPanel { Spacing = 8 };
            for (var index = 0; index < authCapabilities.Count; index++)
            {
                var capability = authCapabilities[index];
                var binding = plugin.AuthBindings.FirstOrDefault(item => item.CapabilityId == capability.Id);
                var toggle = new ToggleSwitch
                {
                    IsOn = binding?.Enabled ?? false,
                    OnContent = "",
                    OffContent = "",
                    VerticalAlignment = VerticalAlignment.Center
                };
                var row = BuildPluginBindingRow(capability.DisplayName, capability.Id, toggle);
                var displayOrder = binding?.DisplayOrder ?? index + 1;
                toggle.Toggled += async (_, _) =>
                {
                    toggle.IsEnabled = false;
                    try
                    {
                        await api.SaveAuthBindingAsync(plugin.Id, new SavePluginAuthBindingRequest
                        {
                            CapabilityId = capability.Id,
                            Enabled = toggle.IsOn,
                            DisplayOrder = displayOrder,
                            AutoProvision = binding?.AutoProvision ?? true,
                            DefaultLogin = binding?.DefaultLogin ?? false
                        });
                        ShowStatus("Auth binding saved.");
                    }
                    catch (Exception ex)
                    {
                        toggle.IsOn = !toggle.IsOn;
                        ShowStatus($"Could not save auth binding: {ex.Message}", isError: true);
                    }
                    finally { toggle.IsEnabled = true; }
                };
                authPanel.Children.Add(row);
            }
            panel.Children.Add(authPanel);
        }

        if (taskCapabilities.Count > 0)
        {
            panel.Children.Add(BuildPluginSectionHeading("\uE823", "Scheduled Tasks"));
            var taskPanel = new StackPanel { Spacing = 8 };
            foreach (var capability in taskCapabilities)
            {
                var binding = plugin.TaskBindings.FirstOrDefault(item => item.CapabilityId == capability.Id);
                var saveBinding = new Button
                {
                    Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                    Content = "Save binding",
                    VerticalAlignment = VerticalAlignment.Center
                };
                saveBinding.Click += async (_, _) =>
                {
                    saveBinding.IsEnabled = false;
                    try
                    {
                        await api.SaveTaskBindingAsync(plugin.Id, capability.Id, new SavePluginTaskBindingRequest
                        {
                            Enabled = binding?.Enabled ?? true,
                            Trigger = binding?.Trigger ?? new Dictionary<string, object> { ["type"] = "startup" }
                        });
                        ShowStatus("Task binding saved.");
                    }
                    catch (Exception ex)
                    {
                        ShowStatus($"Could not save task binding: {ex.Message}", isError: true);
                    }
                    finally { saveBinding.IsEnabled = true; }
                };
                taskPanel.Children.Add(BuildPluginBindingRow(capability.DisplayName, capability.Id, saveBinding));
            }
            panel.Children.Add(taskPanel);
        }

        if (adminRoutes.Count > 0)
        {
            panel.Children.Add(BuildPluginSectionHeading("\uE8A7", "Plugin Pages"));
            var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var route in adminRoutes)
            {
                var button = new Button
                {
                    Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE8A7", FontSize = 12 },
                            new TextBlock { Text = string.IsNullOrWhiteSpace(route.NavigationLabel) ? route.Path : route.NavigationLabel }
                        }
                    }
                };
                button.Click += async (_, _) => await OpenPluginRouteAsync(plugin, route);
                links.Children.Add(button);
            }
            panel.Children.Add(links);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = BuildPluginDialogTitle(plugin),
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.None,
            Content = new ScrollViewer { Content = panel, MaxHeight = 620 }
        };
        await dialog.ShowAsync();
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private async Task OpenPluginRouteAsync(PluginInstallation plugin, PluginRoute route)
    {
        var apiClient = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
        if (string.IsNullOrWhiteSpace(apiClient.BaseUrl)) return;

        var routePath = route.Path.EndsWith("/*", StringComparison.Ordinal)
            ? route.Path[..^2]
            : route.Path;
        if (!routePath.StartsWith('/')) routePath = "/" + routePath;
        var routeUrl = $"{apiClient.BaseUrl.TrimEnd('/')}/api/v1/plugins/{plugin.Id}{routePath}";
        routeUrl += routeUrl.Contains('?') ? "&theme=dark" : "?theme=dark";

        var webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var host = new Grid { Background = (Brush)Application.Current.Resources["PageBackgroundBrush"] };
        host.Children.Add(webView);
        var window = new Window
        {
            Title = string.IsNullOrWhiteSpace(route.NavigationLabel)
                ? PluginDisplayName(plugin.PluginId, plugin.Presentation)
                : route.NavigationLabel,
            Content = host
        };
        _pluginRouteWindows.Add(window);
        window.Closed += (_, _) =>
        {
            webView.Close();
            _pluginRouteWindows.Remove(window);
        };
        window.Activate();
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 760));

        try
        {
            await webView.EnsureCoreWebView2Async();
            var requestPrefix = apiClient.BaseUrl.TrimEnd('/') + "/*";
            webView.CoreWebView2.AddWebResourceRequestedFilter(
                requestPrefix,
                CoreWebView2WebResourceContext.All);
            webView.CoreWebView2.WebResourceRequested += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(apiClient.AccessToken))
                    args.Request.Headers.SetHeader("Authorization", $"Bearer {apiClient.AccessToken}");
                if (!string.IsNullOrWhiteSpace(apiClient.ProfileId))
                    args.Request.Headers.SetHeader("X-Profile-Id", apiClient.ProfileId);
                if (!string.IsNullOrWhiteSpace(apiClient.ProfileToken))
                    args.Request.Headers.SetHeader("X-Profile-Token", apiClient.ProfileToken);
            };
            webView.Source = new Uri(routeUrl);
        }
        catch (Exception ex)
        {
            window.Close();
            ShowStatus($"Could not open plugin page: {ex.Message}", isError: true);
        }
    }

    // ===== Available Card =====

    private FrameworkElement BuildAvailableCard(PluginCatalogEntry entry)
    {
        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(12),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20),
            MinHeight = 210,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var root = new Grid { RowSpacing = 16 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new Grid { ColumnSpacing = 16 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            VerticalAlignment = VerticalAlignment.Top,
            Child = new FontIcon
            {
                Glyph = "\uE74C",
                FontSize = 20,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            }
        });

        var info = new StackPanel { Spacing = 6 };
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
            Text = entry.PluginId,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });

        info.Children.Add(new TextBlock
        {
            Text = PluginSummary(entry.Presentation, entry.Capabilities),
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (entry.Capabilities.Count > 0)
        {
            var capabilityRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var capability in entry.Capabilities)
                capabilityRow.Children.Add(MakeCapabilityBadge(string.IsNullOrWhiteSpace(capability.DisplayName)
                    ? CapabilityLabel(capability.Type)
                    : capability.DisplayName));
            info.Children.Add(capabilityRow);
        }

        Grid.SetColumn(info, 1);
        top.Children.Add(info);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        var footer = new Grid
        {
            ColumnSpacing = 12,
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 12, 0, 0)
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var resourceLinks = BuildResourceLinks(entry.Presentation, entry.RepoUrl);
        if (resourceLinks is not null) footer.Children.Add(resourceLinks);

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
        if (alreadyInstalled)
        {
            installBtn.Style = (Style)Application.Current.Resources["OutlineButtonStyle"];
            installBtn.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        }
        var capturedEntry = entry;
        installBtn.Click += async (_, _) =>
        {
            if (alreadyInstalled) return;
            installBtn.IsEnabled = false;
            await ViewModel.InstallPluginCommand.ExecuteAsync(capturedEntry);
            ShowStatus($"Plugin \"{capturedEntry.PluginId}\" installed.");
        };

        Grid.SetColumn(installBtn, 1);
        footer.Children.Add(installBtn);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

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

    private void AddRepoButton_Click(object sender, RoutedEventArgs e)
    {
        var opening = RepoForm.Visibility != Visibility.Visible;
        RepoForm.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        AddRepoButtonText.Text = opening ? "Cancel" : "Add";
        AddRepoButtonIcon.Glyph = opening ? "\uE711" : "\uE710";
        if (opening) RepoNameBox.Focus(FocusState.Programmatic);
    }

    private async void SubmitRepoButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RepoNameBox.Text) || string.IsNullOrWhiteSpace(RepoUrlBox.Text)) return;
        await ViewModel.AddRepositoryAsync(new CreatePluginRepositoryRequest
        {
            Url = RepoUrlBox.Text.Trim(),
            DisplayName = RepoNameBox.Text.Trim(),
            Enabled = true
        });
        RepoNameBox.Text = "";
        RepoUrlBox.Text = "";
        RepoForm.Visibility = Visibility.Collapsed;
        AddRepoButtonText.Text = "Add";
        AddRepoButtonIcon.Glyph = "\uE710";
        ShowStatus("Repository added.");
    }

    // ===== Helpers =====

    private static FrameworkElement BuildPluginDialogTitle(PluginInstallation plugin)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            Children =
            {
                new FontIcon { Glyph = "\uE74C", FontSize = 17, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock
                {
                    Text = plugin.PluginId,
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                MakeOutlineBadge(plugin.Version)
            }
        };
    }

    private static FrameworkElement BuildPluginSectionHeading(string glyph, string text)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new FontIcon { Glyph = glyph, FontSize = 14 },
                new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold }
            }
        };
    }

    private static Border BuildPluginSectionCard(FrameworkElement content)
    {
        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14),
            Child = content
        };
    }

    private static Border BuildPluginBindingRow(string? displayName, string capabilityId, FrameworkElement action)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { Spacing = 2 };
        labels.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(displayName) ? capabilityId : displayName,
            FontSize = 13,
            FontWeight = FontWeights.Medium
        });
        labels.Children.Add(new TextBlock
        {
            Text = capabilityId,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
        });
        row.Children.Add(labels);
        Grid.SetColumn(action, 1);
        row.Children.Add(action);
        return BuildPluginSectionCard(row);
    }

    private static List<PluginAdminFormField> GetPluginConfigFields(PluginConfigSchema schema)
    {
        if (schema.AdminForm?.Fields is { Count: > 0 } declaredFields)
            return declaredFields;
        if (string.IsNullOrWhiteSpace(schema.JsonSchema)) return [];

        try
        {
            using var document = JsonDocument.Parse(schema.JsonSchema);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "object" ||
                !root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
                return [];

            var required = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.Array)
                foreach (var item in requiredElement.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } key) required.Add(key);

            var fields = new List<PluginAdminFormField>();
            foreach (var property in properties.EnumerateObject())
            {
                if (!property.Value.TryGetProperty("type", out var propertyTypeElement) ||
                    propertyTypeElement.ValueKind != JsonValueKind.String)
                    return [];
                var propertyType = propertyTypeElement.GetString();
                if (propertyType is not ("string" or "number" or "integer" or "boolean")) return [];

                var field = new PluginAdminFormField
                {
                    Key = property.Name,
                    Label = property.Value.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                        ? title.GetString() ?? HumanizePluginKey(property.Name)
                        : HumanizePluginKey(property.Name),
                    Description = property.Value.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
                        ? description.GetString()
                        : null,
                    Control = propertyType switch
                    {
                        "boolean" => "SWITCH",
                        "number" => "NUMBER",
                        "integer" => "INTEGER",
                        _ => "TEXT"
                    },
                    Required = required.Contains(property.Name)
                };
                if (property.Value.TryGetProperty("default", out var defaultValue))
                    field.DefaultValue = JsonElementValue(defaultValue);
                fields.Add(field);
            }
            return fields;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string HumanizePluginKey(string key)
        => string.Join(" ", key.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static object? JsonElementValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDouble(out var number) => number,
        _ => element.ToString()
    };

    private static string PluginValueString(object? value) => value switch
    {
        null => "",
        JsonElement element => JsonElementValue(element)?.ToString() ?? "",
        _ => value.ToString() ?? ""
    };

    private static Dictionary<string, object> ReadPluginFormValues(Dictionary<string, FrameworkElement> fields)
    {
        var values = new Dictionary<string, object>();
        foreach (var (key, editor) in fields)
        {
            object value = editor switch
            {
                ToggleSwitch toggle => toggle.IsOn,
                ComboBox combo when combo.SelectedItem is ComboBoxItem selected => selected.Tag?.ToString() ?? "",
                NumberBox number when number.Tag as string == "INTEGER" && !double.IsNaN(number.Value) => Convert.ToInt64(number.Value),
                NumberBox number when !double.IsNaN(number.Value) => number.Value,
                PasswordBox password => password.Password,
                TextBox text => text.Text,
                _ => ""
            };
            if (editor is PasswordBox && string.IsNullOrEmpty((string)value)) continue;
            values[key] = value;
        }
        return values;
    }

    private static string CapabilityLabel(string type) => type switch
    {
        "metadata_provider.v1" => "Metadata",
        "auth_provider.v1" => "Auth",
        "scheduled_task.v1" => "Task",
        "media_analyzer.v1" => "Analyzer",
        _ => type.Split('.')[0]
    };

    private static Border MakeCapabilityBadge(string text)
    {
        return new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 2, 8, 2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };
    }

    private static StackPanel? BuildResourceLinks(PluginPresentation? presentation, string? repoUrl)
    {
        var links = new[]
        {
            (Label: "Source", Url: presentation?.SourceUrl ?? repoUrl),
            (Label: "Changelog", Url: presentation?.ChangelogUrl),
            (Label: "Support", Url: presentation?.SupportUrl)
        }
        .Select(link => (link.Label, Uri: SafeExternalUri(link.Url)))
        .Where(link => link.Uri is not null)
        .ToList();

        if (links.Count == 0) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var (label, uri) in links)
        {
            row.Children.Add(new HyperlinkButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = label, FontSize = 11 },
                        new FontIcon { Glyph = "\uE8A7", FontSize = 10 }
                    }
                },
                NavigateUri = uri,
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            });
        }
        return row;
    }

    private static Uri? SafeExternalUri(string? raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        return uri.Scheme is "http" or "https" ? uri : null;
    }

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

    private void ShowStatus(string message, bool isError = false)
    {
        StatusBannerText.Text = message;
        StatusBanner.Background = isError
            ? new SolidColorBrush(Color.FromArgb(36, 239, 68, 68))
            : (Brush)Application.Current.Resources["AccentBackgroundBrush"];
        StatusBannerText.Foreground = isError
            ? new SolidColorBrush(Color.FromArgb(255, 248, 113, 113))
            : (Brush)Application.Current.Resources["AccentBrush"];
        StatusBanner.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => { StatusBanner.Visibility = Visibility.Collapsed; timer.Stop(); };
        timer.Start();
    }
}
