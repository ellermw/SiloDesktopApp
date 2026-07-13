using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.HistoryImport;
using SiloPlayer.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using Windows.UI;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminHistoryImportPage : Page
{
    private readonly AdminApi _adminApi;
    private readonly HistoryImportApi _importApi;
    private List<HistoryImportSource> _sources = [];
    private List<HistoryImportUserMapping> _mappings = [];
    private List<HistoryImportRun> _runs = [];
    private HistoryImportSource? _selectedSource;

    // Event channel for realtime refresh
    private IDisposable? _eventSubscription;
    private EventChannelClient? _eventChannel;

    public AdminHistoryImportPage()
    {
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _importApi = App.Services.GetRequiredService<HistoryImportApi>();
        this.InitializeComponent();
    }

    public AdminHistoryImportViewModel ViewModel { get; } = new();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSourcesAsync();

        // Subscribe to realtime events for auto-refresh
        try
        {
            _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
            _eventSubscription = _eventChannel.Subscribe("history_import");
            _eventChannel.EventReceived += OnEventReceived;
        }
        catch { }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_eventChannel != null) _eventChannel.EventReceived -= OnEventReceived;
        _eventSubscription?.Dispose();
        _eventSubscription = null;
    }

    private void OnEventReceived(string channel, string eventName, System.Text.Json.JsonElement data)
    {
        if (channel != "history_import") return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_selectedSource != null)
            {
                try { await LoadRunsAsync(_selectedSource.Id); } catch { }
            }
        });
    }

    // ===== Load Sources =====

    private async Task LoadSourcesAsync()
    {
        ViewModel.IsLoading = true;
        ViewModel.ErrorMessage = null;
        try
        {
            _sources = await _adminApi.GetHistoryImportSourcesAsync();
            RebuildSourceComboBox();
        }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        finally { ViewModel.IsLoading = false; }
    }

    private void RebuildSourceComboBox()
    {
        SourceComboBox.Items.Clear();
        foreach (var src in _sources)
        {
            SourceComboBox.Items.Add(new ComboBoxItem
            {
                Content = $"{src.Name} ({src.SourceType})",
                Tag = src
            });
        }
        if (_sources.Count > 0 && SourceComboBox.SelectedIndex < 0)
            SourceComboBox.SelectedIndex = 0;

        UpdateSourceBarState();
    }

    private async void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is HistoryImportSource src)
        {
            _selectedSource = src;
            UpdateSourceBarState();
            await LoadMappingsAndRunsAsync(src.Id);
        }
    }

    private void UpdateSourceBarState()
    {
        bool hasSrc = _selectedSource != null;
        BtnDiscoverUsers.IsEnabled = hasSrc;
        BtnSetToken.IsEnabled = hasSrc;
        BtnEditSource.IsEnabled = hasSrc;
        BtnDeleteSource.IsEnabled = hasSrc;
        BtnBulkRun.IsEnabled = hasSrc;

        // Token badge — simplified: we don't have token status from the list API,
        // so we'll just show the source type
        if (hasSrc)
        {
            SourceUrlText.Text = _selectedSource!.BaseUrl ?? "";
            TokenBadgeText.Text = _selectedSource.HasAdminToken ? "API key configured" : "API key not configured";
            TokenStatusDot.Fill = (SolidColorBrush)Application.Current.Resources[_selectedSource.HasAdminToken ? "SuccessBrush" : "SecondaryTextBrush"];
        }
        else
        {
            SourceUrlText.Text = "";
            TokenBadgeText.Text = "API key not configured";
        }
    }

    private async Task LoadMappingsAndRunsAsync(int sourceId)
    {
        MappingsSection.Visibility = Visibility.Visible;
        RunsSection.Visibility = Visibility.Visible;

        try
        {
            var mappingsTask = _adminApi.GetMappingsAsync(sourceId);
            var runsTask = _adminApi.GetAdminRunsAsync(sourceId, limit: 20);
            await Task.WhenAll(mappingsTask, runsTask);

            _mappings = mappingsTask.Result ?? [];
            _runs = runsTask.Result ?? [];

            RebuildMappingsTable();
            RebuildRunsSection();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }
    }

    private async Task LoadRunsAsync(int sourceId)
    {
        try
        {
            _runs = await _adminApi.GetAdminRunsAsync(sourceId, limit: 20) ?? [];
            RebuildRunsSection();
        }
        catch { }
    }

    // ===== Mappings Table =====

    private void RebuildMappingsTable()
    {
        MappingsRowsPanel.Children.Clear();

        if (_mappings.Count == 0)
        {
            NoMappingsText.Visibility = Visibility.Visible;
            return;
        }
        NoMappingsText.Visibility = Visibility.Collapsed;

        bool first = true;
        foreach (var mapping in _mappings)
        {
            if (!first)
                MappingsRowsPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0)
                });
            first = false;
            MappingsRowsPanel.Children.Add(BuildMappingRow(mapping));
        }
    }

    private FrameworkElement BuildMappingRow(HistoryImportUserMapping mapping)
    {
        var row = new Grid { Padding = new Thickness(16, 10, 16, 10), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        var srcUser = new TextBlock
        {
            Text = mapping.ExternalUsername, FontSize = 13, FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };

        var ctmUser = new TextBlock
        {
            Text = mapping.ContinuumUsername, FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };

        var profile = new TextBlock
        {
            Text = mapping.ProfileName, FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };

        string lastImported = "Never";
        if (!string.IsNullOrEmpty(mapping.LastImportedAt) && DateTime.TryParse(mapping.LastImportedAt, out var dt))
            lastImported = dt.ToLocalTime().ToString("g");
        var lastImportText = new TextBlock
        {
            Text = lastImported, FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var capturedMapping = mapping;

        var runBtn = new Button
        {
            Width = 28, Height = 28, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon { Glyph = "\uE768", FontSize = 12 }
        };
        ToolTipService.SetToolTip(runBtn, "Run import for this mapping");
        runBtn.Click += async (_, _) =>
        {
            runBtn.IsEnabled = false;
            try { await _adminApi.RunMappingAsync(capturedMapping.Id); }
            catch { }
            finally { runBtn.IsEnabled = true; }
            if (_selectedSource != null) await LoadRunsAsync(_selectedSource.Id);
        };

        var deleteBtn = new Button
        {
            Width = 28, Height = 28, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 }
        };
        ToolTipService.SetToolTip(deleteBtn, "Delete mapping");
        deleteBtn.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Delete mapping", Content = $"Delete mapping for \"{capturedMapping.ExternalUsername}\"?",
                PrimaryButtonText = "Delete", PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                CloseButtonText = "Cancel", XamlRoot = this.XamlRoot, DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await _adminApi.DeleteMappingAsync(capturedMapping.Id);
                if (_selectedSource != null) await LoadMappingsAndRunsAsync(_selectedSource.Id);
            }
        };

        actions.Children.Add(runBtn);
        actions.Children.Add(deleteBtn);

        Grid.SetColumn(srcUser, 0);
        Grid.SetColumn(ctmUser, 1);
        Grid.SetColumn(profile, 2);
        Grid.SetColumn(lastImportText, 3);
        Grid.SetColumn(actions, 4);
        row.Children.Add(srcUser);
        row.Children.Add(ctmUser);
        row.Children.Add(profile);
        row.Children.Add(lastImportText);
        row.Children.Add(actions);

        row.PointerEntered += (s, _) => { if (s is Grid g) g.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); };
        row.PointerExited += (s, _) => { if (s is Grid g) g.Background = new SolidColorBrush(Colors.Transparent); };
        return row;
    }

    // ===== Runs Section =====

    private void RebuildRunsSection()
    {
        RunsPanel.Children.Clear();

        if (_runs.Count == 0)
        {
            NoRunsText.Visibility = Visibility.Visible;
            return;
        }
        NoRunsText.Visibility = Visibility.Collapsed;

        foreach (var run in _runs)
            RunsPanel.Children.Add(BuildRunCard(run));
    }

    private FrameworkElement BuildRunCard(HistoryImportRun run)
    {
        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12),
        };

        var content = new StackPanel { Spacing = 8 };

        // Header: status badge + type + timestamp
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(BuildRunStatusBadge(run.Status));
        header.Children.Add(new TextBlock
        {
            Text = $"{char.ToUpper(run.SourceType[0])}{run.SourceType[1..]} import",
            FontSize = 13, FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        if (!string.IsNullOrEmpty(run.CreatedAt) && DateTime.TryParse(run.CreatedAt, out var createdDt))
        {
            header.Children.Add(new TextBlock
            {
                Text = createdDt.ToLocalTime().ToString("g"),
                FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        content.Children.Add(header);

        // Metrics row
        var metrics = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        void AddMetric(string label, int value)
        {
            metrics.Children.Add(new TextBlock
            {
                Text = $"{label}: {value:N0}", FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"]
            });
        }
        AddMetric("Fetched", run.Fetched);
        AddMetric("Matched", run.Matched);
        AddMetric("Unmatched", run.Unmatched);
        AddMetric("Updated", run.ProgressUpdated);
        AddMetric("Created", run.HistoryCreated);
        AddMetric("Skipped", run.Skipped);
        content.Children.Add(metrics);

        // Warnings
        if (run.Warnings.Count > 0)
        {
            var warnText = new TextBlock
            {
                Text = $"{run.Warnings.Count} warning{(run.Warnings.Count != 1 ? "s" : "")}",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24))
            };
            content.Children.Add(warnText);
        }

        // Error
        if (!string.IsNullOrEmpty(run.ErrorMessage))
        {
            content.Children.Add(new TextBlock
            {
                Text = run.ErrorMessage, FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["ErrorBrush"],
                TextWrapping = TextWrapping.Wrap, MaxLines = 3
            });
        }

        card.Child = content;
        return card;
    }

    private static Border BuildRunStatusBadge(string status)
    {
        var (bg, fg) = status switch
        {
            "completed" => (Color.FromArgb(0x33, 0x4A, 0xDE, 0x80), Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80)),
            "running" => (Color.FromArgb(0x33, 0xFB, 0xBF, 0x24), Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)),
            "queued" => (Color.FromArgb(0x33, 0x60, 0xA5, 0xFA), Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA)),
            "failed" => (Color.FromArgb(0x33, 0xEF, 0x6B, 0x73), Color.FromArgb(0xFF, 0xEF, 0x6B, 0x73)),
            "cancelled" => (Color.FromArgb(0x33, 0x9C, 0xA3, 0xAF), Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF)),
            _ => (Color.FromArgb(0x33, 0x9C, 0xA3, 0xAF), Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF)),
        };
        return new Border
        {
            Background = new SolidColorBrush(bg), Height = 20, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = status, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fg), VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    // ===== Source Dialogs =====

    private async void BtnNewSource_Click(object sender, RoutedEventArgs e)
    {
        var (form, getName, getType, getUrl) = BuildSourceForm(null);
        var dialog = new ContentDialog
        {
            Title = "New Import Source", PrimaryButtonText = "Create", CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot, Content = form, DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            string name = getName(); string type = getType(); string url = getUrl();
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                await _adminApi.CreateHistoryImportSourceAsync(new CreateHistoryImportSourceRequest
                {
                    Name = name, SourceType = type, BaseUrl = url, Enabled = true
                });
                await LoadSourcesAsync();
            }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    private async void BtnEditSource_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null) return;
        var src = _selectedSource;
        var (form, getName, _, getUrl) = BuildSourceForm(src);
        var dialog = new ContentDialog
        {
            Title = "Edit Source", PrimaryButtonText = "Save", CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot, Content = form, DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                await _adminApi.UpdateHistoryImportSourceAsync(src.Id, new UpdateHistoryImportSourceRequest
                {
                    Name = getName(), BaseUrl = getUrl()
                });
                await LoadSourcesAsync();
            }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    private async void BtnDeleteSource_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null) return;
        var dialog = new ContentDialog
        {
            Title = "Delete source", Content = $"Delete \"{_selectedSource.Name}\"? All mappings will be removed.",
            PrimaryButtonText = "Delete", PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel", XamlRoot = this.XamlRoot, DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _adminApi.DeleteHistoryImportSourceAsync(_selectedSource.Id);
            _selectedSource = null;
            MappingsSection.Visibility = Visibility.Collapsed;
            RunsSection.Visibility = Visibility.Collapsed;
            await LoadSourcesAsync();
        }
    }

    private (FrameworkElement, Func<string>, Func<string>, Func<string>) BuildSourceForm(HistoryImportSource? existing)
    {
        var nameBox = new TextBox { PlaceholderText = "Source name", Text = existing?.Name ?? "", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var urlBox = new TextBox { PlaceholderText = "https://plex.example.com", Text = existing?.BaseUrl ?? "", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var typeCombo = new ComboBox { CornerRadius = new CornerRadius(6), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Stretch };
        typeCombo.Items.Add(new ComboBoxItem { Content = "Plex", Tag = "plex" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Emby", Tag = "emby" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Jellyfin", Tag = "jellyfin" });
        typeCombo.SelectedIndex = 0;
        if (existing != null)
        {
            for (int i = 0; i < typeCombo.Items.Count; i++)
                if (typeCombo.Items[i] is ComboBoxItem ci && (string)ci.Tag == existing.SourceType) { typeCombo.SelectedIndex = i; break; }
            typeCombo.IsEnabled = false; // can't change type after creation
        }

        var form = new StackPanel { Width = 420, Spacing = 14 };
        void AddField(string label, FrameworkElement control)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            group.Children.Add(control);
            form.Children.Add(group);
        }
        AddField("Name", nameBox);
        AddField("Type", typeCombo);
        AddField("Server URL", urlBox);

        return (form,
            () => nameBox.Text.Trim(),
            () => (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "plex",
            () => urlBox.Text.Trim());
    }

    // ===== Token Dialog =====

    private async void BtnSetToken_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null) return;
        var tokenBox = new PasswordBox { PlaceholderText = "API token or access token", FontSize = 13 };
        var form = new StackPanel { Width = 420, Spacing = 14 };
        form.Children.Add(new TextBlock { Text = $"Set the API token for {_selectedSource.Name}.", FontSize = 13, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap });
        form.Children.Add(tokenBox);

        var dialog = new ContentDialog
        {
            Title = "Set Token", PrimaryButtonText = "Save", SecondaryButtonText = "Clear Token",
            CloseButtonText = "Cancel", XamlRoot = this.XamlRoot, Content = form,
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tokenBox.Password))
        {
            try { await _adminApi.SetSourceTokenAsync(_selectedSource.Id, tokenBox.Password); }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            try { await _adminApi.ClearSourceTokenAsync(_selectedSource.Id); }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    // ===== Discover Users Dialog =====

    private async void BtnDiscoverUsers_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null) return;

        // Step 1: Fetch external users
        List<HistoryImportExternalUser> externalUsers;
        try
        {
            BtnDiscoverUsers.IsEnabled = false;
            BtnDiscoverUsers.Content = "Discovering...";
            externalUsers = await _adminApi.DiscoverExternalUsersAsync(_selectedSource.Id);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Discovery failed: {ex.Message}";
            BtnDiscoverUsers.IsEnabled = true;
            BtnDiscoverUsers.Content = "Discover Users";
            return;
        }
        finally
        {
            BtnDiscoverUsers.IsEnabled = true;
            BtnDiscoverUsers.Content = "Discover Users";
        }

        if (externalUsers.Count == 0)
        {
            ViewModel.ErrorMessage = "No users found on the external server. Check the token and server URL.";
            return;
        }

        // Step 2: Show user selection dialog
        var listPanel = new StackPanel { Spacing = 4, MaxHeight = 400 };
        var scroll = new ScrollViewer { Content = listPanel, MaxHeight = 400 };
        var checkboxes = new List<(HistoryImportExternalUser User, CheckBox Check)>();

        foreach (var user in externalUsers)
        {
            var cb = new CheckBox { Content = $"{user.Name}{(string.IsNullOrEmpty(user.Email) ? "" : $" ({user.Email}")}", FontSize = 13, IsChecked = true };
            listPanel.Children.Add(cb);
            checkboxes.Add((user, cb));
        }

        var selectDialog = new ContentDialog
        {
            Title = $"Found {externalUsers.Count} users", PrimaryButtonText = "Map Selected",
            CloseButtonText = "Cancel", XamlRoot = this.XamlRoot, Content = scroll,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await selectDialog.ShowAsync() != ContentDialogResult.Primary) return;

        var selected = checkboxes.Where(x => x.Check.IsChecked == true).Select(x => x.User).ToList();
        if (selected.Count == 0) return;

        // Step 3: Get Silo users + profiles for mapping
        var adminUsers = await _adminApi.GetUsersAsync();

        foreach (var extUser in selected)
        {
            // Auto-match by username
            var matchedUser = adminUsers.FirstOrDefault(u =>
                string.Equals(u.Username, extUser.Name, StringComparison.OrdinalIgnoreCase));
            if (matchedUser == null) continue;

            var profiles = await _adminApi.GetUserProfilesAsync(matchedUser.Id);
            if (profiles.Count == 0) continue;

            try
            {
                await _adminApi.CreateMappingAsync(new CreateHistoryImportMappingRequest
                {
                    SourceId = _selectedSource.Id,
                    ExternalUserId = extUser.Id,
                    ExternalUsername = extUser.Name,
                    ContinuumUserId = matchedUser.Id,
                    ProfileId = profiles[0].Id
                });
            }
            catch { }
        }

        await LoadMappingsAndRunsAsync(_selectedSource.Id);
    }

    // ===== Add Mapping Dialog =====

    private async void BtnAddMapping_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null) return;

        var adminUsers = await _adminApi.GetUsersAsync();

        var extIdBox = new TextBox { PlaceholderText = "External user ID", FontSize = 13, CornerRadius = new CornerRadius(6) };
        var extNameBox = new TextBox { PlaceholderText = "External username", FontSize = 13, CornerRadius = new CornerRadius(6) };
        var userCombo = new ComboBox { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var u in adminUsers)
            userCombo.Items.Add(new ComboBoxItem { Content = u.Username, Tag = u.Id });
        if (userCombo.Items.Count > 0) userCombo.SelectedIndex = 0;

        var profileCombo = new ComboBox { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Stretch };
        userCombo.SelectionChanged += async (_, _) =>
        {
            profileCombo.Items.Clear();
            if (userCombo.SelectedItem is ComboBoxItem ci && ci.Tag is int userId)
            {
                var profiles = await _adminApi.GetUserProfilesAsync(userId);
                foreach (var p in profiles)
                    profileCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
                if (profileCombo.Items.Count > 0) profileCombo.SelectedIndex = 0;
            }
        };
        // Trigger initial profile load
        if (userCombo.Items.Count > 0)
        {
            var firstUser = (ComboBoxItem)userCombo.Items[0];
            if (firstUser.Tag is int uid)
            {
                var profiles = await _adminApi.GetUserProfilesAsync(uid);
                foreach (var p in profiles)
                    profileCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
                if (profileCombo.Items.Count > 0) profileCombo.SelectedIndex = 0;
            }
        }

        var form = new StackPanel { Width = 420, Spacing = 14 };
        void AddField(string label, FrameworkElement control)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Medium, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            group.Children.Add(control);
            form.Children.Add(group);
        }
        AddField("External User ID", extIdBox);
        AddField("External Username", extNameBox);
        AddField("Silo User", userCombo);
        AddField("Profile", profileCombo);

        var dialog = new ContentDialog
        {
            Title = "Add Mapping", PrimaryButtonText = "Create", CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot, Content = form, DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            int userId = (userCombo.SelectedItem as ComboBoxItem)?.Tag is int u2 ? u2 : 0;
            string profileId = (profileCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            if (userId == 0 || string.IsNullOrEmpty(profileId)) return;

            try
            {
                await _adminApi.CreateMappingAsync(new CreateHistoryImportMappingRequest
                {
                    SourceId = _selectedSource.Id,
                    ExternalUserId = extIdBox.Text.Trim(),
                    ExternalUsername = extNameBox.Text.Trim(),
                    ContinuumUserId = userId,
                    ProfileId = profileId
                });
                await LoadMappingsAndRunsAsync(_selectedSource.Id);
            }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    // ===== Bulk Run =====

    private async void BtnBulkRun_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null) return;
        BtnBulkRun.IsEnabled = false;
        BtnBulkRun.Content = "Running...";
        try
        {
            await _adminApi.BulkRunSourceAsync(_selectedSource.Id);
            await LoadRunsAsync(_selectedSource.Id);
        }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        finally
        {
            BtnBulkRun.IsEnabled = true;
            BtnBulkRun.Content = "Run All";
        }
    }
}

public partial class AdminHistoryImportViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isLoading;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string? _errorMessage;
}
