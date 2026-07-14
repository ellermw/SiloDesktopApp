using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.HistoryImport;
using SiloPlayer.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Helpers;
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
    private Dictionary<int, string> _userNames = [];
    private HistoryImportSource? _selectedSource;
    private string _runFilter = "all";
    private string? _expandedRunId;
    private bool _isPageActive;
    private CancellationTokenSource? _pageLoadCts;
    private CancellationTokenSource? _selectionLoadCts;
    private CancellationTokenSource? _runsLoadCts;

    // Event channel for realtime refresh
    private IDisposable? _eventSubscription;
    private EventChannelClient? _eventChannel;

    private sealed class SourceFormState
    {
        public required StackPanel Form { get; init; }
        public required TextBox NameBox { get; init; }
        public required ComboBox TypeCombo { get; init; }
        public required TextBox UrlBox { get; init; }
        public required ToggleSwitch EnabledToggle { get; init; }
        public required PasswordBox TokenBox { get; init; }
        public required TextBox PlexUserBox { get; init; }
        public required PasswordBox PlexPasswordBox { get; init; }
        public required ToggleButton PlexLoginModeButton { get; init; }

        public string Name => NameBox.Text.Trim();
        public string SourceType => (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "jellyfin";
        public string Url => UrlBox.Text.Trim();
        public bool Enabled => EnabledToggle.IsOn;
        public string Token => TokenBox.Password.Trim();
        public bool UsePlexLogin => SourceType == "plex" && PlexLoginModeButton.IsChecked == true;
        public string PlexUsername => PlexUserBox.Text.Trim();
        public string PlexPassword => PlexPasswordBox.Password;
    }

    public AdminHistoryImportPage()
    {
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _importApi = App.Services.GetRequiredService<HistoryImportApi>();
        this.InitializeComponent();
    }

    public AdminHistoryImportViewModel ViewModel { get; } = new();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _isPageActive = true;
        _pageLoadCts?.Cancel();
        _pageLoadCts?.Dispose();
        _pageLoadCts = new CancellationTokenSource();
        await LoadSourcesAsync(_pageLoadCts.Token);

        // Subscribe to realtime events for auto-refresh
        try
        {
            if (!_isPageActive || _eventSubscription is not null) return;
            _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
            _eventSubscription = _eventChannel.Subscribe("history_import");
            _eventChannel.EventReceived += OnEventReceived;
        }
        catch { }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _isPageActive = false;
        _pageLoadCts?.Cancel();
        _selectionLoadCts?.Cancel();
        _runsLoadCts?.Cancel();
        if (_eventChannel != null) _eventChannel.EventReceived -= OnEventReceived;
        _eventSubscription?.Dispose();
        _eventSubscription = null;
    }

    private void OnEventReceived(string channel, string eventName, System.Text.Json.JsonElement data)
    {
        if (channel != "history_import" || !_isPageActive) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_isPageActive && _selectedSource != null)
            {
                try { await LoadRunsAsync(_selectedSource.Id); } catch { }
            }
        });
    }

    // ===== Load Sources =====

    private async Task LoadSourcesAsync(CancellationToken cancellationToken = default)
    {
        ViewModel.IsLoading = true;
        ViewModel.ErrorMessage = null;
        try
        {
            _sources = await _adminApi.GetHistoryImportSourcesAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested || !_isPageActive) return;
            RebuildSourceComboBox();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        finally { ViewModel.IsLoading = false; }
    }

    private void RebuildSourceComboBox()
    {
        var selectedId = _selectedSource?.Id;
        NoSourcesCard.Visibility = _sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SourceBarCard.Visibility = _sources.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_sources.Count == 0)
        {
            _selectedSource = null;
            MappingsSection.Visibility = Visibility.Collapsed;
            RunsSection.Visibility = Visibility.Collapsed;
        }
        SourceComboBox.Items.Clear();
        foreach (var src in _sources)
        {
            var sourceLabel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
            };
            sourceLabel.Children.Add(new TextBlock
            {
                Text = src.Name,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            sourceLabel.Children.Add(new Border
            {
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(7, 1, 7, 1),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(src.SourceType)
                        ? "Unknown"
                        : char.ToUpperInvariant(src.SourceType[0]) + src.SourceType[1..],
                    FontSize = 10,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                },
            });
            SourceComboBox.Items.Add(new ComboBoxItem
            {
                Content = sourceLabel,
                Tag = src
            });
        }
        if (_sources.Count > 0)
        {
            var selectedIndex = 0;
            if (selectedId.HasValue)
            {
                for (var index = 0; index < SourceComboBox.Items.Count; index++)
                {
                    if (SourceComboBox.Items[index] is ComboBoxItem { Tag: HistoryImportSource source } && source.Id == selectedId.Value)
                    {
                        selectedIndex = index;
                        break;
                    }
                }
            }
            SourceComboBox.SelectedIndex = selectedIndex;
        }

        UpdateSourceBarState();
    }

    private async void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is HistoryImportSource src)
        {
            _selectedSource = src;
            ViewModel.ErrorMessage = null;
            UpdateSourceBarState();
            _selectionLoadCts?.Cancel();
            _selectionLoadCts?.Dispose();
            _selectionLoadCts = new CancellationTokenSource();
            await LoadMappingsAndRunsAsync(src.Id, _selectionLoadCts.Token);
        }
    }

    private void UpdateSourceBarState()
    {
        bool hasSrc = _selectedSource != null;
        BtnDiscoverUsers.IsEnabled = hasSrc && _selectedSource?.HasAdminToken == true;
        BtnSetToken.IsEnabled = hasSrc;
        BtnEditSource.IsEnabled = hasSrc;
        BtnDeleteSource.IsEnabled = hasSrc;
        BtnBulkRun.IsEnabled = hasSrc;

        // Token badge — simplified: we don't have token status from the list API,
        // so we'll just show the source type
        if (hasSrc)
        {
            SourceUrlText.Text = _selectedSource!.BaseUrl ?? "";
            ConfiguredTokenRow.Visibility = _selectedSource.HasAdminToken ? Visibility.Visible : Visibility.Collapsed;
            MissingTokenCallout.Visibility = _selectedSource.HasAdminToken ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            SourceUrlText.Text = "";
            ConfiguredTokenRow.Visibility = Visibility.Collapsed;
            MissingTokenCallout.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadMappingsAndRunsAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        bool canImport = _selectedSource?.HasAdminToken == true;
        MappingsSection.Visibility = canImport ? Visibility.Visible : Visibility.Collapsed;
        RunsSection.Visibility = Visibility.Collapsed;
        _mappings = [];
        _runs = [];

        if (!canImport)
            return;

        try
        {
            _mappings = await _adminApi.GetMappingsAsync(sourceId, cancellationToken) ?? [];
            if (cancellationToken.IsCancellationRequested || !_isPageActive || _selectedSource?.Id != sourceId) return;
            RebuildMappingsTable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch
        {
            // Match the WebUI's query behavior: a mappings endpoint failure is
            // represented by the empty state and must not prevent run history
            // from loading independently.
            _mappings = [];
            RebuildMappingsTable();
        }

        // A run-history failure must not blank the independently loaded mappings.
        // The WebUI omits this section entirely when there are no available runs.
        try
        {
            var runsTask = _adminApi.GetAdminRunsAsync(sourceId, limit: 20, cancellationToken);
            var usersTask = _adminApi.GetUsersAsync(cancellationToken);
            await Task.WhenAll(runsTask, usersTask);
            if (cancellationToken.IsCancellationRequested || !_isPageActive || _selectedSource?.Id != sourceId) return;
            _runs = runsTask.Result ?? [];
            _userNames = (usersTask.Result ?? []).ToDictionary(user => user.Id, user => user.Username);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch { _runs = []; }
        RebuildRunsSection();
    }

    private async Task LoadRunsAsync(int sourceId)
    {
        _runsLoadCts?.Cancel();
        _runsLoadCts?.Dispose();
        _runsLoadCts = new CancellationTokenSource();
        var cancellationToken = _runsLoadCts.Token;
        try
        {
            _runs = await _adminApi.GetAdminRunsAsync(sourceId, limit: 20, cancellationToken) ?? [];
            if (cancellationToken.IsCancellationRequested || !_isPageActive || _selectedSource?.Id != sourceId) return;
            RebuildRunsSection();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { }
    }

    // ===== Mappings Table =====

    private void RebuildMappingsTable()
    {
        MappingsRowsPanel.Children.Clear();

        if (_mappings.Count == 0)
        {
            NoMappingsText.Visibility = Visibility.Visible;
            MappingsTableCard.Visibility = Visibility.Collapsed;
            BtnBulkRun.Visibility = Visibility.Collapsed;
            return;
        }
        NoMappingsText.Visibility = Visibility.Collapsed;
        MappingsTableCard.Visibility = Visibility.Visible;
        BtnBulkRun.Visibility = Visibility.Visible;

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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        var srcUser = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(mapping.ExternalUsername) ? mapping.ExternalUserId : mapping.ExternalUsername,
            FontSize = 13, FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };

        var arrow = new FontIcon
        {
            Glyph = "\uE72A",
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var siloUser = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        siloUser.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(mapping.ContinuumUsername) ? $"User {mapping.ContinuumUserId}" : mapping.ContinuumUsername,
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(mapping.ProfileName))
            siloUser.Children.Add(new TextBlock
            {
                Text = mapping.ProfileName,
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

        string lastImported = "Never";
        if (!string.IsNullOrEmpty(mapping.LastImportedAt))
            lastImported = FormatTimeAgo(mapping.LastImportedAt);
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
        ToolTipService.SetToolTip(deleteBtn, "Remove mapping");
        deleteBtn.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Remove mapping",
                Content = $"Remove the mapping for \"{(string.IsNullOrWhiteSpace(capturedMapping.ExternalUsername) ? capturedMapping.ExternalUserId : capturedMapping.ExternalUsername)}\"? This won't delete any imported history.",
                PrimaryButtonText = "Remove", PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
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
        Grid.SetColumn(arrow, 1);
        Grid.SetColumn(siloUser, 2);
        Grid.SetColumn(lastImportText, 3);
        Grid.SetColumn(actions, 4);
        row.Children.Add(srcUser);
        row.Children.Add(arrow);
        row.Children.Add(siloUser);
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
            NoRunsText.Visibility = Visibility.Collapsed;
            RunsSection.Visibility = Visibility.Collapsed;
            return;
        }
        NoRunsText.Visibility = Visibility.Collapsed;
        RunsSection.Visibility = Visibility.Visible;

        var filtered = _runs.Where(run => _runFilter switch
        {
            "admin" => run.ConnectionMode == "admin_token",
            "user" => run.ConnectionMode != "admin_token",
            _ => true,
        }).ToList();

        if (filtered.Count == 0)
        {
            RunsPanel.Children.Add(new TextBlock
            {
                Text = $"No {_runFilter} imports to show.",
                FontSize = 14,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(16, 24, 16, 24),
            });
            return;
        }

        for (var index = 0; index < filtered.Count; index++)
        {
            if (index > 0)
                RunsPanel.Children.Add(new Border
                {
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(0, 1, 0, 0),
                });
            RunsPanel.Children.Add(BuildRunCard(filtered[index]));
        }
    }

    private void RunFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string filter }) return;
        _runFilter = filter;
        RunFilterAll.IsChecked = filter == "all";
        RunFilterAdmin.IsChecked = filter == "admin";
        RunFilterUser.IsChecked = filter == "user";
        RebuildRunsSection();
    }

    private FrameworkElement BuildRunCard(HistoryImportRun run)
    {
        var expanded = _expandedRunId == run.Id;
        var content = new StackPanel { Spacing = 0 };
        var header = new Grid { Padding = new Thickness(16, 12, 16, 12), ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var expandButton = new Button
        {
            Width = 24,
            Height = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = expanded ? "\uE70D" : "\uE76C",
                FontSize = 13,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            },
        };
        expandButton.Click += (_, _) =>
        {
            _expandedRunId = expanded ? null : run.Id;
            RebuildRunsSection();
        };
        header.Children.Add(expandButton);

        var status = BuildRunStatusBadge(run.Status);
        Grid.SetColumn(status, 1);
        header.Children.Add(status);

        var titleBlock = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var sourceType = string.IsNullOrWhiteSpace(run.SourceType) ? "Import" : $"{char.ToUpperInvariant(run.SourceType[0])}{run.SourceType[1..]} import";
        titleRow.Children.Add(new TextBlock
        {
            Text = sourceType,
            FontSize = 13, FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        titleRow.Children.Add(BuildRunModeBadge(run.ConnectionMode == "admin_token" ? "Admin" : "Self", run.ConnectionMode != "admin_token"));
        titleBlock.Children.Add(titleRow);
        var userName = _userNames.TryGetValue(run.UserId, out var resolvedName) ? resolvedName : $"User {run.UserId}";
        titleBlock.Children.Add(new TextBlock
        {
            Text = $"{userName} · {FormatRunDate(run.CreatedAt)}",
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        Grid.SetColumn(titleBlock, 2);
        header.Children.Add(titleBlock);

        var summary = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Center,
        };
        void AddSummary(string label, int value, string? brushName = null)
        {
            if (value <= 0) return;
            summary.Children.Add(new TextBlock
            {
                Text = $"{value:N0} {label}",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources[brushName ?? "SecondaryTextBrush"],
            });
        }
        AddSummary("fetched", run.Fetched);
        AddSummary("matched", run.Matched, "SuccessBrush");
        AddSummary("unmatched", run.Unmatched, "WarningBrush");
        Grid.SetColumn(summary, 3);
        header.Children.Add(summary);

        var isActive = run.Status is "queued" or "running";
        if (isActive)
        {
            var cancelButton = new Button
            {
                Content = "Cancel",
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["ErrorBrush"],
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            cancelButton.Click += async (_, _) =>
            {
                cancelButton.IsEnabled = false;
                try
                {
                    await _adminApi.CancelRunAsync(run.Id);
                    if (_selectedSource is not null) await LoadRunsAsync(_selectedSource.Id);
                }
                catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
                finally { cancelButton.IsEnabled = true; }
            };
            Grid.SetColumn(cancelButton, 4);
            header.Children.Add(cancelButton);
        }
        content.Children.Add(header);

        if (expanded)
            content.Children.Add(BuildRunDetails(run));

        return content;
    }

    private FrameworkElement BuildRunDetails(HistoryImportRun run)
    {
        var details = new StackPanel { Spacing = 12 };
        if (!string.IsNullOrWhiteSpace(run.ErrorMessage))
        {
            var errorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            errorRow.Children.Add(new FontIcon
            {
                Foreground = (SolidColorBrush)Application.Current.Resources["ErrorBrush"],
                Glyph = "\uE7BA",
                FontSize = 14,
            });
            errorRow.Children.Add(new TextBlock
            {
                Text = run.ErrorMessage,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            });
            details.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x18, 0xEF, 0x44, 0x44)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xEF, 0x44, 0x44)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Child = errorRow,
            });
        }

        var metrics = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        for (var index = 0; index < 7; index++)
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var metricValues = new (string Label, int Value)[]
        {
            ("Fetched", run.Fetched),
            ("Matched", run.Matched),
            ("Unmatched", run.Unmatched),
            ("Updated", run.ProgressUpdated),
            ("History", run.HistoryCreated),
            ("Watchlist", run.WatchlistAdded),
            ("Skipped", run.Skipped),
        };
        for (var index = 0; index < metricValues.Length; index++)
        {
            var metric = new StackPanel { Spacing = 2 };
            metric.Children.Add(new TextBlock
            {
                Text = metricValues[index].Label,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            });
            metric.Children.Add(new TextBlock
            {
                Text = metricValues[index].Value.ToString("N0"),
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            });
            Grid.SetColumn(metric, index);
            metrics.Children.Add(metric);
        }
        details.Children.Add(metrics);

        if (run.Warnings.Count > 0)
            details.Children.Add(BuildRunIssueList($"Warnings ({run.Warnings.Count})", run.Warnings.Take(5).Concat(
                run.Warnings.Count > 5 ? [$"and {run.Warnings.Count - 5} more…"] : []).ToList()));

        if (run.UnmatchedSamples.Count > 0)
            details.Children.Add(BuildRunIssueList("Unmatched samples", run.UnmatchedSamples.Select(sample =>
                $"{sample.Title}{(sample.Year.HasValue ? $" ({sample.Year})" : "")} — {sample.Reason}").ToList()));

        if (string.IsNullOrWhiteSpace(run.ErrorMessage) && run.Warnings.Count == 0 && run.UnmatchedSamples.Count == 0)
            details.Children.Add(new TextBlock
            {
                Text = "No issues.",
                FontSize = 11,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            });

        return new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
            Padding = new Thickness(44, 0, 16, 16),
            Child = details,
        };
    }

    private static FrameworkElement BuildRunIssueList(string heading, IReadOnlyList<string> issues)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        foreach (var issue in issues)
            panel.Children.Add(new TextBlock
            {
                Text = $"• {issue}",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            });
        return panel;
    }

    private static Border BuildRunModeBadge(string label, bool secondary)
    {
        return new Border
        {
            Background = secondary
                ? (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"]
                : new SolidColorBrush(Colors.Transparent),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            BorderThickness = secondary ? new Thickness(0) : new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(7, 1, 7, 1),
            Child = new TextBlock { Text = label, FontSize = 10 },
        };
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
        var label = string.IsNullOrWhiteSpace(status)
            ? "Unknown"
            : char.ToUpperInvariant(status[0]) + status[1..];
        var icon = status switch
        {
            "queued" => "\uE823",
            "running" => "\uE895",
            "completed" => "\uE73E",
            "failed" => "\uE711",
            "cancelled" => "\uE711",
            _ => "\uE9CE",
        };
        var badgeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        badgeContent.Children.Add(new FontIcon { Glyph = icon, FontSize = 11, Foreground = new SolidColorBrush(fg) });
        badgeContent.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(fg),
        });
        return new Border
        {
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, fg.R, fg.G, fg.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(9, 3, 9, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = badgeContent,
        };
    }

    private static string FormatRunDate(string value)
    {
        if (!DateTimeOffset.TryParse(value, out var parsed)) return "—";
        return $"{DateTimeDisplay.FormatDate(parsed)}, {DateTimeDisplay.FormatTime(parsed)}";
    }

    private static string FormatTimeAgo(string value)
    {
        if (!DateTimeOffset.TryParse(value, out var parsed)) return "Never";
        var elapsed = DateTimeOffset.UtcNow - parsed.ToUniversalTime();
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)elapsed.TotalHours)}h ago";
        return DateTimeDisplay.FormatDate(parsed);
    }

    // ===== Source Dialogs =====

    private async void BtnNewSource_Click(object sender, RoutedEventArgs e)
    {
        var state = BuildSourceForm(null);
        var dialog = new ContentDialog
        {
            Title = "Add source server", PrimaryButtonText = "Add server", CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot, Content = state.Form, DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(state.Name) || string.IsNullOrWhiteSpace(state.Url)) return;
            try
            {
                var created = await _adminApi.CreateHistoryImportSourceAsync(new CreateHistoryImportSourceRequest
                {
                    Name = state.Name,
                    SourceType = state.SourceType,
                    BaseUrl = state.Url,
                    Enabled = state.Enabled,
                    SortOrder = 0,
                    AdminToken = state.UsePlexLogin ? null : string.IsNullOrWhiteSpace(state.Token) ? null : state.Token,
                });

                if (state.UsePlexLogin && !string.IsNullOrWhiteSpace(state.PlexUsername) && !string.IsNullOrEmpty(state.PlexPassword))
                {
                    var login = await _adminApi.PlexAdminLoginAsync(new PlexAdminLoginRequest
                    {
                        Username = state.PlexUsername,
                        Password = state.PlexPassword,
                    });
                    if (!string.IsNullOrWhiteSpace(login.Token))
                        await _adminApi.SetSourceTokenAsync(created.Id, login.Token);
                }
                await LoadSourcesAsync();
            }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    private async void BtnEditSource_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null) return;
        var src = _selectedSource;
        var state = BuildSourceForm(src);
        var dialog = new ContentDialog
        {
            Title = "Edit server", PrimaryButtonText = "Save", CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot, Content = state.Form, DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                await _adminApi.UpdateHistoryImportSourceAsync(src.Id, new UpdateHistoryImportSourceRequest
                {
                    Name = state.Name, BaseUrl = state.Url, Enabled = state.Enabled
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
            Title = "Delete server", Content = $"Delete \"{_selectedSource.Name}\"? All user mappings for this server will also be removed.",
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

    private SourceFormState BuildSourceForm(HistoryImportSource? existing)
    {
        var nameBox = new TextBox { Text = existing?.Name ?? "", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var urlBox = new TextBox { Text = existing?.BaseUrl ?? "", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var typeCombo = new ComboBox { CornerRadius = new CornerRadius(6), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Stretch };
        typeCombo.Items.Add(new ComboBoxItem { Content = "Jellyfin", Tag = "jellyfin" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Emby", Tag = "emby" });
        typeCombo.Items.Add(new ComboBoxItem { Content = "Plex", Tag = "plex" });
        typeCombo.SelectedIndex = 0;
        if (existing is not null)
        {
            for (var index = 0; index < typeCombo.Items.Count; index++)
            {
                if (typeCombo.Items[index] is ComboBoxItem item && (string)item.Tag == existing.SourceType)
                {
                    typeCombo.SelectedIndex = index;
                    break;
                }
            }
            typeCombo.IsEnabled = false;
        }

        var tokenBox = new PasswordBox
        {
            PasswordRevealMode = PasswordRevealMode.Peek,
            CornerRadius = new CornerRadius(6),
            FontSize = 13,
        };
        var plexUserBox = new TextBox { PlaceholderText = "you@example.com", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var plexPasswordBox = new PasswordBox { PasswordRevealMode = PasswordRevealMode.Peek, CornerRadius = new CornerRadius(6), FontSize = 13 };
        var loginModeButton = new ToggleButton { Content = "Sign in with Plex", IsChecked = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var tokenModeButton = new ToggleButton { Content = "Paste token", HorizontalAlignment = HorizontalAlignment.Stretch };
        var modeGrid = new Grid { ColumnSpacing = 2 };
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(tokenModeButton, 1);
        modeGrid.Children.Add(loginModeButton);
        modeGrid.Children.Add(tokenModeButton);

        var loginPanel = new StackPanel { Spacing = 10 };
        var tokenPanel = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        var tokenLabel = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
        tokenPanel.Children.Add(tokenLabel);
        tokenPanel.Children.Add(tokenBox);
        var plexUserGroup = new StackPanel { Spacing = 6 };
        plexUserGroup.Children.Add(new TextBlock { Text = "Plex email or username", FontSize = 12, FontWeight = FontWeights.SemiBold });
        plexUserGroup.Children.Add(plexUserBox);
        var plexPasswordGroup = new StackPanel { Spacing = 6 };
        plexPasswordGroup.Children.Add(new TextBlock { Text = "Plex password", FontSize = 12, FontWeight = FontWeights.SemiBold });
        plexPasswordGroup.Children.Add(plexPasswordBox);
        loginPanel.Children.Add(plexUserGroup);
        loginPanel.Children.Add(plexPasswordGroup);

        var tokenSection = new StackPanel { Spacing = 10 };
        tokenSection.Children.Add(modeGrid);
        tokenSection.Children.Add(loginPanel);
        tokenSection.Children.Add(tokenPanel);

        void SelectLoginMode(bool login)
        {
            loginModeButton.IsChecked = login;
            tokenModeButton.IsChecked = !login;
            loginPanel.Visibility = login ? Visibility.Visible : Visibility.Collapsed;
            tokenPanel.Visibility = login ? Visibility.Collapsed : Visibility.Visible;
        }
        loginModeButton.Click += (_, _) => SelectLoginMode(true);
        tokenModeButton.Click += (_, _) => SelectLoginMode(false);

        var form = new StackPanel { Width = 512, Spacing = 16 };
        void AddField(string label, FrameworkElement control)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            group.Children.Add(control);
            form.Children.Add(group);
        }

        var topGrid = new Grid { ColumnSpacing = 16 };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nameGroup = new StackPanel { Spacing = 6 };
        nameGroup.Children.Add(new TextBlock { Text = "Name", FontSize = 12, FontWeight = FontWeights.SemiBold });
        nameGroup.Children.Add(nameBox);
        var typeGroup = new StackPanel { Spacing = 6 };
        typeGroup.Children.Add(new TextBlock { Text = "Type", FontSize = 12, FontWeight = FontWeights.SemiBold });
        typeGroup.Children.Add(typeCombo);
        Grid.SetColumn(typeGroup, 1);
        topGrid.Children.Add(nameGroup);
        topGrid.Children.Add(typeGroup);
        form.Children.Add(topGrid);
        AddField("Server URL", urlBox);

        if (existing is null)
            form.Children.Add(tokenSection);

        var enabledToggle = new ToggleSwitch
        {
            IsOn = existing?.Enabled ?? true,
            OnContent = "",
            OffContent = "",
            MinWidth = 44,
        };
        var enabledRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        enabledRow.Children.Add(enabledToggle);
        enabledRow.Children.Add(new TextBlock { Text = "Enabled", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        form.Children.Add(enabledRow);

        void UpdateHints()
        {
            var type = (typeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "jellyfin";
            nameBox.PlaceholderText = type switch { "emby" => "My Emby Server", "plex" => "My Plex Server", _ => "My Jellyfin Server" };
            urlBox.PlaceholderText = type switch { "emby" => "https://emby.example.com", "plex" => "https://plex.example.com:32400", _ => "https://jellyfin.example.com" };
            tokenLabel.Text = type == "plex" ? "Plex auth token (optional)" : "Admin API key (optional)";
            tokenBox.PlaceholderText = type == "plex" ? "Paste Plex token here…" : "Paste API key here…";
            modeGrid.Visibility = type == "plex" ? Visibility.Visible : Visibility.Collapsed;
            if (type != "plex") SelectLoginMode(false);
            else SelectLoginMode(true);
        }
        typeCombo.SelectionChanged += (_, _) => UpdateHints();
        UpdateHints();

        return new SourceFormState
        {
            Form = form,
            NameBox = nameBox,
            TypeCombo = typeCombo,
            UrlBox = urlBox,
            EnabledToggle = enabledToggle,
            TokenBox = tokenBox,
            PlexUserBox = plexUserBox,
            PlexPasswordBox = plexPasswordBox,
            PlexLoginModeButton = loginModeButton,
        };
    }

    // ===== Token Dialog =====

    private async void BtnSetToken_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null) return;
        var source = _selectedSource;
        var isPlex = string.Equals(source.SourceType, "plex", StringComparison.OrdinalIgnoreCase);
        var tokenBox = new PasswordBox { PlaceholderText = "Paste token here…", PasswordRevealMode = PasswordRevealMode.Peek, FontSize = 13 };
        var plexUserBox = new TextBox { PlaceholderText = "you@example.com", FontSize = 13 };
        var plexPasswordBox = new PasswordBox { PasswordRevealMode = PasswordRevealMode.Peek, FontSize = 13 };
        var loginModeButton = new ToggleButton { Content = "Sign in with Plex", IsChecked = isPlex };
        var tokenModeButton = new ToggleButton { Content = "Paste token", IsChecked = !isPlex };

        var modeGrid = new Grid { ColumnSpacing = 2, Visibility = isPlex ? Visibility.Visible : Visibility.Collapsed };
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(tokenModeButton, 1);
        modeGrid.Children.Add(loginModeButton);
        modeGrid.Children.Add(tokenModeButton);

        var loginPanel = new StackPanel { Spacing = 10, Visibility = isPlex ? Visibility.Visible : Visibility.Collapsed };
        loginPanel.Children.Add(new TextBlock { Text = "Sign in with your Plex account to generate an admin token automatically.", FontSize = 13, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap });
        var loginUserGroup = new StackPanel { Spacing = 6 };
        loginUserGroup.Children.Add(new TextBlock { Text = "Email or username", FontSize = 12, FontWeight = FontWeights.SemiBold });
        loginUserGroup.Children.Add(plexUserBox);
        var loginPasswordGroup = new StackPanel { Spacing = 6 };
        loginPasswordGroup.Children.Add(new TextBlock { Text = "Password", FontSize = 12, FontWeight = FontWeights.SemiBold });
        loginPasswordGroup.Children.Add(plexPasswordBox);
        loginPanel.Children.Add(loginUserGroup);
        loginPanel.Children.Add(loginPasswordGroup);

        var tokenPanel = new StackPanel { Spacing = 10, Visibility = isPlex ? Visibility.Collapsed : Visibility.Visible };
        tokenPanel.Children.Add(new TextBlock
        {
            Text = isPlex ? "Paste your Plex auth token directly." : "This key is used to discover users on the server and import their watch history into Silo.",
            FontSize = 13,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        tokenPanel.Children.Add(new TextBlock { Text = "API key / Token", FontSize = 12, FontWeight = FontWeights.SemiBold });
        tokenPanel.Children.Add(tokenBox);

        var form = new StackPanel { Width = 448, Spacing = 14 };
        form.Children.Add(modeGrid);
        form.Children.Add(loginPanel);
        form.Children.Add(tokenPanel);
        if (source.HasAdminToken)
            form.Children.Add(new TextBlock { Text = "A token is already configured. Saving will replace it.", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] });

        var dialog = new ContentDialog
        {
            Title = $"Admin API key — {source.Name}", PrimaryButtonText = isPlex ? "Sign in & save" : "Save",
            SecondaryButtonText = source.HasAdminToken ? "Remove" : "",
            CloseButtonText = "Cancel", XamlRoot = this.XamlRoot, Content = form,
            DefaultButton = ContentDialogButton.Primary
        };
        void SelectMode(bool login)
        {
            loginModeButton.IsChecked = login;
            tokenModeButton.IsChecked = !login;
            loginPanel.Visibility = login ? Visibility.Visible : Visibility.Collapsed;
            tokenPanel.Visibility = login ? Visibility.Collapsed : Visibility.Visible;
            dialog.PrimaryButtonText = login ? "Sign in & save" : "Save";
        }
        loginModeButton.Click += (_, _) => SelectMode(true);
        tokenModeButton.Click += (_, _) => SelectMode(false);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                if (isPlex && loginModeButton.IsChecked == true)
                {
                    if (string.IsNullOrWhiteSpace(plexUserBox.Text) || string.IsNullOrEmpty(plexPasswordBox.Password)) return;
                    var login = await _adminApi.PlexAdminLoginAsync(new PlexAdminLoginRequest { Username = plexUserBox.Text.Trim(), Password = plexPasswordBox.Password });
                    if (!string.IsNullOrWhiteSpace(login.Token))
                        await _adminApi.SetSourceTokenAsync(source.Id, login.Token);
                }
                else if (!string.IsNullOrWhiteSpace(tokenBox.Password))
                {
                    await _adminApi.SetSourceTokenAsync(source.Id, tokenBox.Password.Trim());
                }
                await LoadSourcesAsync();
            }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            try
            {
                await _adminApi.ClearSourceTokenAsync(source.Id);
                await LoadSourcesAsync();
            }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        }
    }

    // ===== Discover Users Dialog =====

    private async void BtnDiscoverUsers_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSource == null) return;
        var source = _selectedSource;
        var discoverButton = new Button
        {
            Content = "Discover users",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var promptContent = new StackPanel
        {
            Width = 560,
            Spacing = 12,
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        promptContent.Children.Add(new FontIcon
        {
            Glyph = "\uE721",
            FontSize = 32,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        promptContent.Children.Add(new TextBlock
        {
            Text = "Query the server to find user accounts available for import.",
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
        });
        promptContent.Children.Add(discoverButton);

        var shouldDiscover = false;
        var prompt = new ContentDialog
        {
            Title = $"Discover users on {source.Name}",
            CloseButtonText = "Done",
            XamlRoot = XamlRoot,
            Content = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
                CornerRadius = new CornerRadius(12),
                Child = promptContent,
            },
        };
        discoverButton.Click += (_, _) =>
        {
            shouldDiscover = true;
            prompt.Hide();
        };
        await prompt.ShowAsync();
        if (shouldDiscover)
            await DiscoverAndShowUsersAsync(source);
    }

    private async Task DiscoverAndShowUsersAsync(HistoryImportSource source)
    {
        List<HistoryImportExternalUser> externalUsers;
        try
        {
            BtnDiscoverUsers.IsEnabled = false;
            DiscoverUsersText.Text = "Discovering…";
            externalUsers = await _adminApi.DiscoverExternalUsersAsync(source.Id);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Discovery failed: {ex.Message}";
            BtnDiscoverUsers.IsEnabled = true;
            DiscoverUsersText.Text = "Discover users";
            return;
        }
        finally
        {
            BtnDiscoverUsers.IsEnabled = true;
            DiscoverUsersText.Text = "Discover users";
        }

        var mappedIds = _mappings.Select(mapping => mapping.ExternalUserId).ToHashSet(StringComparer.Ordinal);
        var unmappedUsers = externalUsers.Where(user => !mappedIds.Contains(user.Id)).ToList();
        var adminUsers = await _adminApi.GetUsersAsync();
        HistoryImportExternalUser? selectedExternalUser = null;

        var countText = new TextBlock { FontSize = 13, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] };
        var searchBox = new TextBox { PlaceholderText = "Search users…", Padding = new Thickness(34, 0, 8, 0) };
        var listPanel = new StackPanel { Spacing = 0 };
        var listScroll = new ScrollViewer { Content = listPanel, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var listCard = new Border { Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"], CornerRadius = new CornerRadius(12), Child = listScroll };

        var selectedText = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold };
        var userCombo = new ComboBox { PlaceholderText = "Select user…", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var user in adminUsers)
            userCombo.Items.Add(new ComboBoxItem { Content = user.Username, Tag = user.Id });
        var profileCombo = new ComboBox { PlaceholderText = "Select profile…", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
        var saveMappingButton = new Button { Content = "Save mapping", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var cancelMappingButton = new Button { Content = "Cancel", Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };

        var mappingGrid = new Grid { ColumnSpacing = 12 };
        mappingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mappingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var userGroup = new StackPanel { Spacing = 6 };
        userGroup.Children.Add(new TextBlock { Text = "Silo user", FontSize = 12, FontWeight = FontWeights.SemiBold });
        userGroup.Children.Add(userCombo);
        var profileGroup = new StackPanel { Spacing = 6 };
        profileGroup.Children.Add(new TextBlock { Text = "Profile", FontSize = 12, FontWeight = FontWeights.SemiBold });
        profileGroup.Children.Add(profileCombo);
        Grid.SetColumn(profileGroup, 1);
        mappingGrid.Children.Add(userGroup);
        mappingGrid.Children.Add(profileGroup);
        var mappingActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        mappingActions.Children.Add(saveMappingButton);
        mappingActions.Children.Add(cancelMappingButton);
        var mappingPanel = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
        mappingPanel.Children.Add(selectedText);
        mappingPanel.Children.Add(mappingGrid);
        mappingPanel.Children.Add(mappingActions);
        var mappingCard = new Border { Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"], CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = mappingPanel };

        void ClearSelection()
        {
            selectedExternalUser = null;
            selectedText.Text = "";
            userCombo.SelectedIndex = -1;
            profileCombo.Items.Clear();
            profileCombo.IsEnabled = false;
            mappingPanel.Visibility = Visibility.Collapsed;
        }

        void RebuildExternalUsers()
        {
            listPanel.Children.Clear();
            var query = searchBox.Text?.Trim() ?? "";
            var filtered = unmappedUsers.Where(user => string.IsNullOrWhiteSpace(query) ||
                user.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                user.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            countText.Text = unmappedUsers.Count == 0
                ? "All users are already mapped."
                : $"{unmappedUsers.Count} unmapped user{(unmappedUsers.Count == 1 ? "" : "s")} found";
            if (filtered.Count == 0)
            {
                listPanel.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(query) ? "No unmapped users." : $"No users matching “{query}”",
                    Padding = new Thickness(16, 24, 16, 24),
                    TextAlignment = TextAlignment.Center,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                });
                return;
            }

            foreach (var externalUser in filtered)
            {
                var capturedUser = externalUser;
                var rowGrid = new Grid { Padding = new Thickness(14, 9, 14, 9), ColumnSpacing = 10 };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var copy = new StackPanel { Spacing = 2 };
                copy.Children.Add(new TextBlock { Text = externalUser.Name, FontSize = 13, FontWeight = FontWeights.SemiBold });
                copy.Children.Add(new TextBlock { Text = externalUser.Id, FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"], TextTrimming = TextTrimming.CharacterEllipsis });
                var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 12, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"], VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(chevron, 1);
                rowGrid.Children.Add(copy);
                rowGrid.Children.Add(chevron);
                var rowButton = new Button { Content = rowGrid, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0) };
                rowButton.Click += (_, _) =>
                {
                    selectedExternalUser = capturedUser;
                    selectedText.Text = $"Map {capturedUser.Name} to a Silo user and profile:";
                    userCombo.SelectedIndex = -1;
                    profileCombo.Items.Clear();
                    profileCombo.IsEnabled = false;
                    mappingPanel.Visibility = Visibility.Visible;
                };
                listPanel.Children.Add(rowButton);
            }
        }

        searchBox.TextChanged += (_, _) => RebuildExternalUsers();
        userCombo.SelectionChanged += async (_, _) =>
        {
            profileCombo.Items.Clear();
            profileCombo.IsEnabled = false;
            if (userCombo.SelectedItem is not ComboBoxItem { Tag: int userId }) return;
            try
            {
                var profiles = await _adminApi.GetUserProfilesAsync(userId);
                foreach (var profile in profiles)
                    profileCombo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
                profileCombo.IsEnabled = true;
            }
            catch { }
        };
        cancelMappingButton.Click += (_, _) => ClearSelection();
        saveMappingButton.Click += async (_, _) =>
        {
            if (selectedExternalUser is null ||
                userCombo.SelectedItem is not ComboBoxItem { Tag: int userId } ||
                profileCombo.SelectedItem is not ComboBoxItem { Tag: string profileId }) return;
            saveMappingButton.IsEnabled = false;
            try
            {
                await _adminApi.CreateMappingAsync(new CreateHistoryImportMappingRequest
                {
                    SourceId = source.Id,
                    ExternalUserId = selectedExternalUser.Id,
                    ExternalUsername = selectedExternalUser.Name,
                    ContinuumUserId = userId,
                    ProfileId = profileId,
                });
                unmappedUsers.RemoveAll(user => user.Id == selectedExternalUser.Id);
                ClearSelection();
                RebuildExternalUsers();
                await LoadMappingsAndRunsAsync(source.Id);
            }
            catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
            finally { saveMappingButton.IsEnabled = true; }
        };

        var form = new StackPanel { Width = 640, Spacing = 14 };
        form.Children.Add(countText);
        var searchGrid = new Grid();
        searchGrid.Children.Add(searchBox);
        searchGrid.Children.Add(new FontIcon { Glyph = "\uE721", FontSize = 14, Margin = new Thickness(12, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"] });
        form.Children.Add(searchGrid);
        form.Children.Add(listCard);
        form.Children.Add(mappingCard);
        RebuildExternalUsers();

        var dialog = new ContentDialog
        {
            Title = $"Discover users on {source.Name}",
            CloseButtonText = "Done",
            XamlRoot = XamlRoot,
            Content = form,
        };
        await dialog.ShowAsync();
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
        BulkRunText.Text = "Importing…";
        try
        {
            await _adminApi.BulkRunSourceAsync(_selectedSource.Id);
            await LoadRunsAsync(_selectedSource.Id);
        }
        catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; }
        finally
        {
            BtnBulkRun.IsEnabled = true;
            BulkRunText.Text = "Import all";
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
