using System.Collections.Specialized;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.HistoryImport;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Models.Sessions;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Models.WatchProviders;
using SiloPlayer.Core.Services;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views.Dialogs;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    private readonly CardOverlayService _cardOverlayService;
    private readonly UICustomizationService _uiCustomizationService;
    private readonly List<PrimaryMenuItem> _interfaceMenuDraft = [];
    private bool _interfaceMenuDirty;
    private bool _suppressInterfaceEvents;
    private CardOverlayPrefs? _cardOverlayDraft;
    private string _cardOverlayPreviewVariant = "movie";
    private CancellationTokenSource? _overlaySaveCts;
    private CancellationTokenSource? _themeCssSaveCts;
    private bool _themeCssLoaded;
    private bool _rememberLibraryPagesLoaded;
    private bool _pageInitialized;
    private int _profilesLoadGeneration;
    private int _householdSessionsLoadGeneration;
    private DispatcherTimer? _householdSessionsTimer;
    private bool _householdSessionsLoading;
    private IReadOnlyList<PlaybackSessionSummary> _householdSessions = [];
    private DateTime _loadedAtUtc;
    private bool _showingSettingsOverview;
    private bool _canManageProfiles;
    private readonly List<SettingsOverviewCard> _settingsOverviewCards = [];
    // Start suppressed — handlers that fire during XAML parse (before all sibling
    // x:Name fields are assigned) would otherwise null-ref on their forward references
    // and surface as a cryptic "Failed to assign to RangeBase.Value" XamlParseException.
    // Set back to false after the page finishes loading.
    private bool _suppressEvents = true;

    // Language options for preferred audio language.
    private static readonly (string Tag, string Label)[] AudioLanguageOptions =
        [("", "Profile default"), ("original", "Original Language"),
         .. MediaLanguageCatalog.All.Select(language => (language.Code, language.Label))];

    // Language options for subtitles (includes "None")
    private static readonly (string Tag, string Label)[] SubtitleLanguageOptions =
        [("", "Profile default"), ("none", "None"),
         .. MediaLanguageCatalog.All.Select(language => (language.Code, language.Label))];

    private static readonly (string Tag, string Label)[] SubtitleModeOptions =
    [
        ("", "Profile default"),
        ("auto", "Auto"),
        ("always", "Always"),
        ("off", "Off"),
    ];

    private static readonly (string Tag, string Label)[] ForcedSubtitleOptions =
    [
        ("", "Profile default"),
        ("on", "On"),
        ("off", "Off"),
    ];

    // Subtitle font color options
    private static readonly (string Hex, string Label)[] SubtitleColorOptions =
    [
        ("#ffffff", "White"),
        ("#facc15", "Yellow"),
        ("#22c55e", "Green"),
        ("#06b6d4", "Cyan"),
        ("#d946ef", "Magenta"),
        ("#ef4444", "Red"),
        ("#3b82f6", "Blue"),
        ("#000000", "Black"),
    ];

    private static readonly (string Hex, string Label)[] SubtitleBackgroundColorOptions =
    [
        ("#000000", "Black"),
        ("#374151", "Dark Gray"),
        ("#1e3a5f", "Navy"),
        ("#7f1d1d", "Dark Red"),
        ("#14532d", "Dark Green"),
    ];

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        _cardOverlayService = App.Services.GetRequiredService<CardOverlayService>();
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        this.InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        PopulateProfileLanguageChoices();

        ViewModel.LibraryCards.CollectionChanged += LibraryCards_CollectionChanged;
        ViewModel.HomeSections.CollectionChanged += HomeSections_CollectionChanged;
        ViewModel.ImportRuns.CollectionChanged += ImportRuns_CollectionChanged;
        ViewModel.PluginSettingsList.CollectionChanged += PluginSettings_CollectionChanged;
        ViewModel.WatchProviderCards.CollectionChanged += WatchProviderCards_CollectionChanged;
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.VisibleLibraryCount))
                DispatcherQueue.TryEnqueue(() => App.MainWindowInstance?.UpdateLibraryNavItems());
        };

        BuildSubtitleColorSwatches();
        SizeChanged += SettingsPage_SizeChanged;
    }

    private void SettingsPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // SettingsLayout.tsx switches from the grouped 220 px rail to a horizontal
        // pill strip at Tailwind's `lg` breakpoint. Keep the native page on the same
        // responsive geometry instead of squeezing the desktop rail into narrow windows.
        var isCompact = e.NewSize.Width < 1024;
        var isPhoneWidth = e.NewSize.Width < 640;

        SettingsPageShell.Margin = isPhoneWidth
            ? new Thickness(16, 16, 16, 32)
            : isCompact
                ? new Thickness(24, 24, 24, 40)
                : new Thickness(48, 24, 48, 48);

        SettingsHeaderGrid.ColumnDefinitions[0].Width = isPhoneWidth
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        SettingsHeaderGrid.ColumnDefinitions[1].Width = isPhoneWidth
            ? new GridLength(0)
            : GridLength.Auto;
        Grid.SetRow(SettingsSearchPanel, isPhoneWidth ? 1 : 0);
        Grid.SetColumn(SettingsSearchPanel, isPhoneWidth ? 0 : 1);
        SettingsSearchPanel.Width = isPhoneWidth ? double.NaN : 384;
        SettingsSearchPanel.HorizontalAlignment = isPhoneWidth
            ? HorizontalAlignment.Stretch
            : HorizontalAlignment.Right;

        SettingsLayoutGrid.ColumnDefinitions[0].Width = isCompact
            ? new GridLength(0)
            : new GridLength(220);
        SettingsLayoutGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);

        Grid.SetRow(SettingsNavigationScroller, 0);
        Grid.SetColumn(SettingsNavigationScroller, 0);
        Grid.SetColumnSpan(SettingsNavigationScroller, 1);
        Grid.SetRow(SettingsContentPanel, 0);
        Grid.SetColumn(SettingsContentPanel, isCompact ? 0 : 1);
        Grid.SetColumnSpan(SettingsContentPanel, isCompact ? 2 : 1);

        SettingsNavigationScroller.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        SettingsAllSettingsButton.Visibility = isCompact && !_showingSettingsOverview
            ? Visibility.Visible
            : Visibility.Collapsed;

        SettingsNavigationScroller.VerticalScrollBarVisibility = isCompact
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        SettingsNavigationScroller.HorizontalScrollBarVisibility = isCompact
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
        SettingsNavigationScroller.Padding = isCompact
            ? new Thickness(4)
            : new Thickness(0, 0, 12, 0);
        SettingsNavigationScroller.Background = isCompact
            ? (Brush)Application.Current.Resources["CardBackgroundBrush"]
            : new SolidColorBrush(Colors.Transparent);

        SettingsNavigationGroups.Orientation = Orientation.Vertical;
        SettingsNavigationGroups.Spacing = 20;
        SetSettingsNavigationGroupLayout(PlaybackNavGroup, false);
        SetSettingsNavigationGroupLayout(AppearanceNavGroup, false);
        SetSettingsNavigationGroupLayout(HomeDiscoveryNavGroup, false);
        SetSettingsNavigationGroupLayout(ConnectionsNavGroup, false);
        SetSettingsNavigationGroupLayout(AccountNavGroup, false);

        SettingsContentPanel.Padding = isCompact
            ? new Thickness(0)
            : new Thickness(40, 0, 0, 0);
        SettingsContentPanel.MaxWidth = double.PositiveInfinity;
    }

    private static void SetSettingsNavigationGroupLayout(StackPanel group, bool isCompact)
    {
        group.Orientation = isCompact ? Orientation.Horizontal : Orientation.Vertical;
        group.Spacing = isCompact ? 4 : 2;
        if (group.Children.Count > 0)
            group.Children[0].Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PopulateProfileLanguageChoices()
    {
        PopulateLanguageCombo(SpokenLanguageComboBox, "No preference");
        PopulateMetadataLanguageCombo();
        PopulateLanguageCombo(SubtitleLanguageComboBox, "None");
    }

    private void PopulateMetadataLanguageCombo()
    {
        MetadataLanguageComboBox.Items.Clear();
        MetadataLanguageComboBox.Items.Add(new ComboBoxItem { Content = "Library default", Tag = "" });
        MetadataLanguageComboBox.Items.Add(new ComboBoxItem { Content = "Original language", Tag = "original" });
        foreach (var language in MediaLanguageCatalog.All)
            MetadataLanguageComboBox.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
        MetadataLanguageComboBox.SelectedIndex = 0;
    }

    private static void PopulateLanguageCombo(ComboBox combo, string emptyLabel)
    {
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = emptyLabel, Tag = "" });
        foreach (var language in MediaLanguageCatalog.All)
            combo.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
        combo.SelectedIndex = 0;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var stale = !_pageInitialized || DateTime.UtcNow - _loadedAtUtc > TimeSpan.FromMinutes(2);
        if (stale)
        {
            ShowLibraryLoadingState();
            var rememberLibraryPagesTask = LoadRememberLibraryPagesAsync();
            await ViewModel.LoadCommand.ExecuteAsync(null);
            await rememberLibraryPagesTask;
            RebuildLibraryCards();
            SyncComboBoxes();
            SyncSubtitleAppearanceControls();
            BuildThemeCards();
            UpdateCurrentThemeDisplay();
            BuildDateTimeFormatControls();
            BuildAccessibilityControls();
            BuildThemeTokenEditor();
            ThemeCatalogRefreshButton.Visibility = string.Equals(
                App.Services.GetRequiredService<SiloPlayer.Core.Services.SettingsService>().Load().LastUserRole,
                "admin", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            AudioPassthroughToggle.IsOn = App.Services.GetRequiredService<SiloPlayer.Core.Services.SettingsService>().Load().AudioBitstreamPassthrough;
            await InitializeRtxUpscalingSettingAsync();
            _pageInitialized = true;
            _loadedAtUtc = DateTime.UtcNow;
        }

        BuildSettingsOverview();
        if (e.Parameter is string requestedTab)
        {
            var button = ResolveSettingsTabButton(requestedTab);
            if (button is not null)
                ShowSettingsDetail(button);
        }
        else
        {
            ShowSettingsOverview();
        }
    }

    private Button? ResolveSettingsTabButton(string requestedTab) => requestedTab switch
    {
        "Playback" => PlaybackTab,
        "Subtitles" or "SubtitleAppearance" => SubtitlesTab,
        "Appearance" => AppearanceTab,
        "Interface" or "NavigationCards" => InterfaceTab,
        "ThemeEditor" => ThemeEditorTab,
        "Accessibility" => AccessibilityTab,
        "HomeScreen" => HomeScreenTab,
        "CardOverlays" => CardOverlaysTab,
        "Personalize" => PersonalizeTab,
        "Libraries" => LibrariesTab,
        "Import" or "HistoryImport" => ImportTab,
        "WebhookSync" => WebhookSyncTab,
        "WatchProviders" => WatchProvidersTab,
        "Devices" => DevicesTab,
        "Notifications" or "NotificationsSettings" => NotificationsSettingsTab,
        "ConnectApps" => ConnectAppsTab,
        "Profiles" => ProfilesTab,
        _ => null,
    };

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // Stop the history_import event channel subscription when leaving Settings.
        StopImportEventSubscription();
        _themeCssSaveCts?.Cancel();
        _themeCssSaveCts?.Dispose();
        _themeCssSaveCts = null;
        _webhookPlexAuthCts?.Cancel();
        _webhookPlexAuthCts?.Dispose();
        _webhookPlexAuthCts = null;
        _webhookRefreshTimer?.Stop();
        _householdSessionsTimer?.Stop();
        base.OnNavigatedFrom(e);
    }

    private void SyncComboBoxes()
    {
        _suppressEvents = true;

        SelectComboBoxByTag(QualityComboBox, ViewModel.QualityPreference);
        SelectComboBoxByTag(MaxBitrateComboBox, ViewModel.MaxBitrateKbps);
        SelectComboBoxByTag(SubtitleLanguageComboBox, ViewModel.SubtitleLanguage);
        SelectComboBoxByTag(SubtitleModeComboBox, ViewModel.SubtitleMode);
        SelectComboBoxByTag(NextUpModeComboBox, ViewModel.NextUpMode);

        SelectComboBoxByTag(SpokenLanguageComboBox, ViewModel.AudioLanguage);
        SelectComboBoxByTag(MetadataLanguageComboBox, ViewModel.PreferredMetadataLanguage);
        BuildMetadataLanguageExceptions();

        _suppressEvents = false;
    }

    private void BuildMetadataLanguageExceptions()
    {
        MetadataLanguageExceptionsPanel.Children.Clear();
        MetadataExceptionSourceComboBox.Items.Clear();

        foreach (var pair in ViewModel.MetadataLanguageOverrides)
        {
            var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var source = new TextBlock
            {
                Text = MediaLanguageCatalog.Label(pair.Key),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var target = CreateMetadataTargetCombo(pair.Key, pair.Value);
            var remove = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                Style = (Style)Application.Current.Resources["TransparentButtonStyle"],
                Width = 36,
                Height = 36,
                Tag = pair.Key,
            };
            ToolTipService.SetToolTip(remove, $"Remove {MediaLanguageCatalog.Label(pair.Key)} exception");
            remove.Click += RemoveMetadataLanguageException_Click;
            Grid.SetColumn(source, 0);
            Grid.SetColumn(target, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(source);
            row.Children.Add(target);
            row.Children.Add(remove);
            MetadataLanguageExceptionsPanel.Children.Add(row);
        }

        foreach (var language in MediaLanguageCatalog.All.Where(language =>
                     !ViewModel.MetadataLanguageOverrides.ContainsKey(language.Code)))
            MetadataExceptionSourceComboBox.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });

        MetadataExceptionSourceComboBox.SelectedItem = null;
        AddMetadataLanguageExceptionButton.IsEnabled = false;
    }

    private void MetadataExceptionSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AddMetadataLanguageExceptionButton.IsEnabled =
            MetadataExceptionSourceComboBox.SelectedItem is ComboBoxItem { Tag: string };
    }

    private ComboBox CreateMetadataTargetCombo(string sourceLanguage, string selectedValue)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = sourceLanguage };
        combo.Items.Add(new ComboBoxItem { Content = "Original language", Tag = "original" });
        foreach (var language in MediaLanguageCatalog.All)
            combo.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
        SelectComboBoxByTag(combo, selectedValue);
        combo.SelectionChanged += MetadataLanguageTarget_SelectionChanged;
        return combo;
    }

    private async void AddMetadataLanguageException_Click(object sender, RoutedEventArgs e)
    {
        if (MetadataExceptionSourceComboBox.SelectedItem is not ComboBoxItem { Tag: string source }) return;
        await ViewModel.SetMetadataLanguageOverrideAsync(source, "original");
        BuildMetadataLanguageExceptions();
    }

    private async void RemoveMetadataLanguageException_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string source }) return;
        await ViewModel.SetMetadataLanguageOverrideAsync(source, null);
        BuildMetadataLanguageExceptions();
    }

    private async void MetadataLanguageTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || sender is not ComboBox { Tag: string source, SelectedItem: ComboBoxItem { Tag: string target } }) return;
        await ViewModel.SetMetadataLanguageOverrideAsync(source, target);
    }

    private void AudioPassthroughToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        var settingsService = App.Services.GetRequiredService<SiloPlayer.Core.Services.SettingsService>();
        var settings = settingsService.Load();
        settings.AudioBitstreamPassthrough = AudioPassthroughToggle.IsOn;
        settingsService.Save(settings);
    }

    private bool _initializingRtxUpscaling = true;

    private async Task InitializeRtxUpscalingSettingAsync()
    {
        _initializingRtxUpscaling = true;
        try
        {
            var settings = App.Services.GetRequiredService<SettingsService>().Load();
            RtxUpscalingToggle.IsOn = settings.NvidiaVideoUpscaling;
            var adapter = await Task.Run(() => SiloPlayer.Player.RtxVideoAdapter.Name);
            // A migrated preference can always be turned off on another PC.
            RtxUpscalingToggle.IsEnabled = adapter != null || RtxUpscalingToggle.IsOn;
            RtxUpscalingHardwareText.Text = adapter != null
                ? $"Detected: {adapter}. AI activation also requires NVIDIA driver support."
                : "No NVIDIA RTX GPU detected. Normal scaling is available.";
        }
        finally { _initializingRtxUpscaling = false; }
    }

    private void RtxUpscalingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializingRtxUpscaling || _suppressEvents) return;
        var service = App.Services.GetRequiredService<SettingsService>();
        var settings = service.Load();
        settings.NvidiaVideoUpscaling = RtxUpscalingToggle.IsOn;
        service.Save(settings);
        RtxUpscalingHardwareText.Text = "Saved for this PC. Restart Silo for Windows Desktop App to apply.";
    }

    private async Task LoadRememberLibraryPagesAsync()
    {
        try
        {
            const string key = "ui.remember_library_page_state";
            var response = await App.Services.GetRequiredService<SettingsApi>().GetEffectiveSettingsAsync([key]);
            var value = response.Settings.FirstOrDefault(entry => entry.Key == key)?.EffectiveValue;
            _suppressEvents = true;
            RememberLibraryPagesToggle.IsOn = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
            _suppressEvents = false;
            _rememberLibraryPagesLoaded = true;
        }
        catch
        {
            _rememberLibraryPagesLoaded = false;
        }
    }

    private async void RememberLibraryPagesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || !_rememberLibraryPagesLoaded) return;
        var api = App.Services.GetRequiredService<SettingsApi>();
        RememberLibraryPagesToggle.IsEnabled = false;
        try
        {
            if (RememberLibraryPagesToggle.IsOn)
            {
                await api.DeleteDeviceSettingAsync("ui.remember_library_page_state");
            }
            else
            {
                await api.DeleteDeviceSettingAsync("ui.library_page_state");
                await api.PutDeviceSettingAsync("ui.remember_library_page_state", "false");
            }
            App.Services.GetRequiredService<ToastService>().Success("Library page preference saved");
        }
        catch (Exception ex)
        {
            _suppressEvents = true;
            RememberLibraryPagesToggle.IsOn = !RememberLibraryPagesToggle.IsOn;
            _suppressEvents = false;
            App.Services.GetRequiredService<ToastService>().Error($"Failed to save library page preference: {ex.Message}");
        }
        finally
        {
            RememberLibraryPagesToggle.IsEnabled = true;
        }
    }

    private void BuildAccessibilityControls()
    {
        BuildAccessibilityOptions(TextSizeButtons,
            [("default", "Default"), ("large", "Large"), ("x-large", "Extra Large")],
            ViewModel.TextScale,
            async value => await ViewModel.SetAccessibilityAsync(textScale: value));
        BuildAccessibilityOptions(TextWeightButtons,
            [("default", "Default"), ("strong", "Bolder")],
            ViewModel.TextWeight,
            async value => await ViewModel.SetAccessibilityAsync(textWeight: value));
        BuildAccessibilityOptions(ContrastButtons,
            [("false", "Standard"), ("true", "High Contrast")],
            ViewModel.HighContrast ? "true" : "false",
            async value => await ViewModel.SetAccessibilityAsync(highContrast: value == "true"));
        App.Services.GetRequiredService<AccessibilityService>().ApplySaved(this);
    }

    private void BuildAccessibilityOptions(Panel host, (string Value, string Label)[] options,
        string selected, Func<string, Task> onSelected)
    {
        host.Children.Clear();
        foreach (var option in options)
        {
            var button = new Button
            {
                Content = option.Label,
                Tag = option.Value,
                Padding = new Thickness(14, 7, 14, 7),
                Style = (Style)Application.Current.Resources[
                    option.Value == selected ? "AccentButtonStyle" : "SecondaryButtonStyle"],
            };
            button.Click += async (_, _) =>
            {
                await onSelected(option.Value);
                BuildAccessibilityControls();
            };
            host.Children.Add(button);
        }
    }

    private static readonly (string Token, string Label)[] NativeThemeTokens =
    [
        ("background", "Background"), ("foreground", "Foreground"), ("card", "Card"),
        ("surface", "Surface"), ("surface-hover", "Surface Hover"), ("primary", "Primary"),
        ("primary-foreground", "Primary Text"), ("secondary", "Secondary"),
        ("secondary-foreground", "Secondary Text"), ("muted-foreground", "Muted Text"),
        ("border", "Border"), ("input", "Input Border"), ("sidebar", "Sidebar"),
        ("sidebar-accent", "Sidebar Accent"), ("sidebar-border", "Sidebar Border"),
        ("destructive", "Destructive"),
    ];

    private void BuildThemeTokenEditor()
    {
        if (ThemeTokenOverridesHost == null) return;
        ThemeTokenOverridesHost.Children.Clear();
        var theme = ViewModel.ThemeService;
        var overrides = theme.GetThemeOverrides();

        foreach (var (token, label) in NativeThemeTokens)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            var valueBox = new TextBox
            {
                Text = overrides.TryGetValue(token, out var value) ? value : "",
                PlaceholderText = "Theme default",
                FontFamily = new FontFamily("Consolas"), FontSize = 12,
            };
            Grid.SetColumn(valueBox, 1);
            row.Children.Add(valueBox);
            var apply = new Button { Content = "Apply", Padding = new Thickness(10, 6, 10, 6) };
            apply.Click += (_, _) =>
            {
                var candidate = valueBox.Text.Trim();
                if (candidate.Length > 0 && (candidate.Length != 7 || candidate[0] != '#' || !candidate[1..].All(Uri.IsHexDigit)))
                {
                    ViewModel.ErrorMessage = $"{label} must use #RRGGBB format.";
                    return;
                }
                theme.SetThemeOverride(token, candidate);
                BuildThemeTokenEditor();
            };
            Grid.SetColumn(apply, 2);
            row.Children.Add(apply);
            ThemeTokenOverridesHost.Children.Add(row);
        }

        var fontRow = new Grid { ColumnSpacing = 10 };
        fontRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fontRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        fontRow.Children.Add(new TextBlock { Text = "Font Family", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
        var fontCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        fontCombo.Items.Add(new ComboBoxItem { Content = "Theme default", Tag = "" });
        foreach (var font in new[] { "Outfit", "Sora", "Urbanist", "Manrope" })
            fontCombo.Items.Add(new ComboBoxItem { Content = font, Tag = font });
        SelectComboBoxByTag(fontCombo, overrides.TryGetValue("font-body", out var fontValue) ? fontValue : "");
        var fontReady = false;
        fontCombo.Loaded += (_, _) => fontReady = true;
        fontCombo.SelectionChanged += (_, _) =>
        {
            if (fontReady && fontCombo.SelectedItem is ComboBoxItem item && item.Tag is string selected)
                theme.SetThemeOverride("font-body", selected);
        };
        Grid.SetColumn(fontCombo, 1);
        fontRow.Children.Add(fontCombo);
        ThemeTokenOverridesHost.Children.Add(fontRow);
        UpdateThemeResetVisibility();
    }

    private void UpdateThemeResetVisibility()
        => ThemeResetOverridesButton.Visibility = ViewModel.ThemeService.GetThemeOverrides().Count > 0 ||
                                                  !string.IsNullOrWhiteSpace(ThemeCustomCssBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;

    private void ThemeResetOverrides_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ThemeService.ResetThemeOverrides();
        _suppressEvents = true;
        ThemeCustomCssBox.Text = "";
        _suppressEvents = false;
        _ = App.Services.GetRequiredService<SettingsApi>().PutSettingAsync("ui_custom_css", "");
        BuildThemeTokenEditor();
    }

    private async void ThemeExport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = "Silo-Custom-Theme" };
        picker.FileTypeChoices.Add("Silo theme", [".json"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance));
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        var document = new Dictionary<string, object?>
        {
            ["version"] = 1, ["name"] = "Silo Custom Theme", ["baseTheme"] = ViewModel.ThemeService.CurrentTheme,
            ["vars"] = ViewModel.ThemeService.GetThemeOverrides(), ["customCss"] = ThemeCustomCssBox.Text, ["createdAt"] = DateTime.UtcNow.ToString("O"),
        };
        await Windows.Storage.FileIO.WriteTextAsync(file, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        ViewModel.StatusMessage = "Theme exported";
    }

    private async void ThemeImport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance));
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        try
        {
            using var doc = JsonDocument.Parse(await Windows.Storage.FileIO.ReadTextAsync(file));
            if (!doc.RootElement.TryGetProperty("version", out var version) || version.GetInt32() != 1)
                throw new InvalidOperationException("Unsupported theme file version.");
            if (doc.RootElement.TryGetProperty("baseTheme", out var baseTheme) && baseTheme.ValueKind == JsonValueKind.String)
                ViewModel.ThemeService.ApplyTheme(baseTheme.GetString() ?? ViewModel.ThemeService.CurrentTheme);
            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            if (doc.RootElement.TryGetProperty("vars", out var values) && values.ValueKind == JsonValueKind.Object)
                foreach (var property in values.EnumerateObject())
                    if (property.Value.ValueKind == JsonValueKind.String) vars[property.Name] = property.Value.GetString() ?? "";
            ViewModel.ThemeService.ImportThemeOverrides(vars);
            if (doc.RootElement.TryGetProperty("customCss", out var css) && css.ValueKind == JsonValueKind.String)
            {
                _suppressEvents = true;
                ThemeCustomCssBox.Text = SanitizeThemeCss(css.GetString() ?? "");
                _suppressEvents = false;
                await App.Services.GetRequiredService<SettingsApi>().PutSettingAsync("ui_custom_css", ThemeCustomCssBox.Text);
            }
            BuildThemeTokenEditor();
            ViewModel.StatusMessage = "Theme imported";
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not import theme: {ex.Message}"; }
    }

    private bool _themeCatalogLoading;

    private void ThemeEditorSectionTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tab }) return;
        ThemeTokensPanel.Visibility = tab == "tokens" ? Visibility.Visible : Visibility.Collapsed;
        ThemeCssPanel.Visibility = tab == "css" ? Visibility.Visible : Visibility.Collapsed;
        ThemeCatalogPanel.Visibility = tab == "catalog" ? Visibility.Visible : Visibility.Collapsed;
        ThemeTokensTab.Style = (Style)Resources[tab == "tokens" ? "ActiveTabStyle" : "InactiveTabStyle"];
        ThemeCssTab.Style = (Style)Resources[tab == "css" ? "ActiveTabStyle" : "InactiveTabStyle"];
        ThemeCatalogTab.Style = (Style)Resources[tab == "catalog" ? "ActiveTabStyle" : "InactiveTabStyle"];
        if (tab == "css") _ = LoadThemeCustomCssAsync();
        if (tab == "catalog") _ = LoadThemeCatalogAsync();
    }

    private async Task LoadThemeCustomCssAsync()
    {
        if (_themeCssLoaded) return;
        try
        {
            var setting = await App.Services.GetRequiredService<SettingsApi>().GetSettingAsync("ui_custom_css");
            _suppressEvents = true;
            ThemeCustomCssBox.Text = setting.Value ?? "";
            _suppressEvents = false;
            _themeCssLoaded = true;
            UpdateThemeResetVisibility();
        }
        catch (Exception ex)
        {
            ThemeCustomCssStatus.Text = $"Could not load custom CSS: {ex.Message}";
        }
    }

    private void ThemeCustomCssBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || !_themeCssLoaded) return;
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _themeCssSaveCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        ThemeCustomCssStatus.Text = "Saving…";
        UpdateThemeResetVisibility();
        _ = SaveThemeCssAfterDelayAsync(ThemeCustomCssBox.Text, cts);
    }

    private async Task SaveThemeCssAfterDelayAsync(string css, CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(1000, owner.Token);
            var sanitized = SanitizeThemeCss(css);
            await App.Services.GetRequiredService<SettingsApi>().PutSettingAsync("ui_custom_css", sanitized, owner.Token);
            if (ReferenceEquals(_themeCssSaveCts, owner))
            {
                ThemeCustomCssStatus.Text = sanitized == css ? "Saved" : "Saved; external CSS resources were blocked.";
                if (sanitized != css)
                {
                    _suppressEvents = true;
                    ThemeCustomCssBox.Text = sanitized;
                    _suppressEvents = false;
                }
            }
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_themeCssSaveCts, owner)) ThemeCustomCssStatus.Text = $"Save failed: {ex.Message}";
        }
        finally
        {
            Interlocked.CompareExchange(ref _themeCssSaveCts, null, owner);
            owner.Dispose();
        }
    }

    private static string SanitizeThemeCss(string css)
    {
        var result = Regex.Replace(css, "@import\\s+(?:url\\(.*?\\)|['\"].*?['\"])[^;]*;?", "/* [blocked @import] */", RegexOptions.IgnoreCase);
        return Regex.Replace(result, "url\\(\\s*(['\"]?)([\\s\\S]*?)\\1\\s*\\)", match =>
        {
            var value = match.Groups[2].Value.Trim().Trim('\'', '\"');
            var safe = value.Length == 0 || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                       (value.StartsWith('/') && !value.StartsWith("//")) || value.StartsWith('#') ||
                       (!Regex.IsMatch(value, "^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase) && !value.StartsWith("//"));
            return safe ? match.Value : "/* [blocked external url] */";
        }, RegexOptions.IgnoreCase);
    }

    private async Task LoadThemeCatalogAsync(bool refresh = false)
    {
        if (_themeCatalogLoading) return;
        _themeCatalogLoading = true;
        CommunityThemesHost.Children.Clear();
        CommunityThemesHost.Children.Add(new TextBlock
        {
            Text = "Loading community themes...", FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        try
        {
            var api = App.Services.GetRequiredService<SettingsApi>();
            var response = refresh ? await api.RefreshThemeCatalogAsync() : await api.GetThemeCatalogAsync();
            CommunityThemesHost.Children.Clear();
            if (response.Themes.Count == 0)
            {
                CommunityThemesHost.Children.Add(new TextBlock { Text = "No community themes available yet.", FontSize = 13 });
                return;
            }
            foreach (var entry in response.Themes)
                CommunityThemesHost.Children.Add(BuildCommunityThemeCard(entry, api));
        }
        catch (Exception ex)
        {
            CommunityThemesHost.Children.Clear();
            CommunityThemesHost.Children.Add(new TextBlock
            {
                Text = $"Could not load the theme catalog. {ex.Message}", TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], FontSize = 13,
            });
        }
        finally { _themeCatalogLoading = false; }
    }

    private Border BuildCommunityThemeCard(ThemeCatalogEntry entry, SettingsApi api)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(14), Padding = new Thickness(14),
        };
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel { Spacing = 3 };
        info.Children.Add(new TextBlock { Text = entry.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        info.Children.Add(new TextBlock { Text = $"by {entry.Author}", FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        if (!string.IsNullOrWhiteSpace(entry.Description))
            info.Children.Add(new TextBlock { Text = entry.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        grid.Children.Add(info);
        var install = new Button { Content = "Install", Style = (Style)Application.Current.Resources["AccentButtonStyle"], Padding = new Thickness(14, 7, 14, 7) };
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false;
            install.Content = "Installing...";
            try
            {
                var theme = await api.DownloadThemeAsync(entry.DownloadUrl);
                if (theme.Version != 1) throw new InvalidOperationException("Unsupported theme file version.");
                ViewModel.ThemeService.ApplyTheme(theme.BaseTheme);
                ViewModel.ThemeService.ImportThemeOverrides(theme.Vars);
                BuildThemeTokenEditor();
                ViewModel.StatusMessage = $"Installed {entry.Name}";
            }
            catch (Exception ex) { ViewModel.ErrorMessage = $"Failed to install {entry.Name}: {ex.Message}"; }
            finally { install.IsEnabled = true; install.Content = "Install"; }
        };
        Grid.SetColumn(install, 1);
        grid.Children.Add(install);
        card.Child = grid;
        return card;
    }

    private async void ThemeCatalogRefresh_Click(object sender, RoutedEventArgs e)
        => await LoadThemeCatalogAsync(refresh: true);

    private void SyncSubtitleAppearanceControls()
    {
        _suppressEvents = true;
        SelectComboBoxByTag(SubtitleFontSizeComboBox, ViewModel.SubFontSize);
        SelectComboBoxByTag(SubtitleFontFamilyComboBox, ViewModel.SubFontFamily);
        SelectComboBoxByTag(SubtitleBgStyleComboBox, ViewModel.SubBackgroundStyle);
        SelectComboBoxByTag(SubtitlePositionComboBox, ViewModel.SubPosition);
        SubtitleOutlineToggle.IsOn = ViewModel.SubOutlineEnabled;
        // B55: SubBackgroundOpacity is now int 0-100 (matching webui) — no
        // more /*100 scaling.
        SubtitleBgOpacitySlider.Value = ViewModel.SubBackgroundOpacity;
        SubtitleBgOpacityLabel.Text = $"{ViewModel.SubBackgroundOpacity}%";
        UpdateSubtitleColorSelection();
        UpdateSubtitleDependentControls();
        SubtitleResetButton.Visibility = ViewModel.HasSubtitleAppearanceDeviceOverride ? Visibility.Visible : Visibility.Collapsed;
        UpdateSubtitlePreview();
        _suppressEvents = false;
    }

    private static void SelectComboBoxByTag(ComboBox combo, string tagValue)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag is string tag && tag == tagValue)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        // If no match, try selecting first item
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    // ===== Tab switching =====
    private void BuildSettingsOverview()
    {
        SettingsOverviewGroups.Children.Clear();
        _settingsOverviewCards.Clear();

        var auth = App.Services.GetRequiredService<AuthService>();
        var profile = auth.SelectedProfile;
        var profileName = string.IsNullOrWhiteSpace(profile?.Name) ? "Your profile" : profile.Name;
        var avatarHost = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = (Brush)Application.Current.Resources["AccentBackgroundBrush"],
        };
        if (!string.IsNullOrWhiteSpace(profile?.AvatarUrl) &&
            Uri.TryCreate(profile.AvatarUrl, UriKind.Absolute, out var avatarUri))
        {
            avatarHost.Child = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(avatarUri),
                Stretch = Stretch.UniformToFill,
            };
        }
        else
        {
            avatarHost.Child = new TextBlock
            {
                Text = profileName[..1].ToUpperInvariant(),
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        var profileText = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        profileText.Children.Add(new TextBlock
        {
            Text = "Current profile",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        profileText.Children.Add(new TextBlock
        {
            Text = profileName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var profileContent = new Grid { ColumnSpacing = 12 };
        profileContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profileContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profileContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profileContent.Children.Add(avatarHost);
        Grid.SetColumn(profileText, 1);
        profileContent.Children.Add(profileText);
        var profileChevron = new FontIcon
        {
            Glyph = "\uE76C",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(profileChevron, 2);
        profileContent.Children.Add(profileChevron);
        SettingsCurrentProfileButton.Content = profileContent;
        AutomationProperties.SetName(SettingsCurrentProfileButton, $"Current profile: {profileName}");

        _canManageProfiles = AuthorizationPolicy.IsActingAdmin(auth) || profile?.IsPrimary == true;
        ProfilesTab.Visibility = _canManageProfiles ? Visibility.Visible : Visibility.Collapsed;
        var groups = new (string Label, (string Tag, string Label, string Description, string Glyph, string Search)[] Items)[]
        {
            ("Playback",
            [
                ("Playback", "Playback", "Quality, languages, skipping, and what plays next.", "\uE768", "video quality bitrate bandwidth spoken metadata language auto skip intros credits recaps preview auto play next up"),
                ("Subtitles", "Subtitles", "Subtitle language, when they appear, and how they look.", "\uED1E", "subtitle forced captions font size color background position"),
                ("Devices", "Your Devices", "Per-device quality, HDR, and audio or subtitle sync.", "\uE7F4", "devices tv phone tablet browser hdr dolby vision sound delay lip sync"),
            ]),
            ("Appearance",
            [
                ("Appearance", "Appearance", "Theme, interface tone, and date and time formats.", "\uE790", "theme dark light custom date time clock"),
                ("Interface", "Navigation & Cards", "Your primary menu, poster size, and card captions.", "\uE7F8", "navigation menu poster size card captions title year artwork preset"),
                ("CardOverlays", "Card Overlays", "Badges drawn on poster cards, and where they sit.", "\uE81E", "poster badges overlay accent color preset icon position"),
                ("Accessibility", "Accessibility", "Text size, weight, and contrast for easier reading.", "\uE7F3", "contrast readability motion transparency text size weight"),
                ("ThemeEditor", "Theme Editor", "Fine-tune theme colors and add your own CSS.", "\uE771", "design tokens token overrides custom css community themes"),
            ]),
            ("Home & Discovery",
            [
                ("HomeScreen", "Home Screen", "Which rows appear on Home, and in what order.", "\uECA5", "sections rows continue watching next up recently added library order"),
                ("Personalize", "Personalize", "Re-tune the taste profile behind your recommendations.", "\uE735", "taste profile recommendations ratings likes dislikes"),
                ("Libraries", "Libraries", "Which libraries you see, their order, and per-library audio.", "\uE8F1", "library visibility access disabled order playback spoken subtitle"),
            ]),
            ("Connections",
            [
                ("ConnectApps", "Connect Apps", "Sign-in details for Silo and Jellyfin-compatible apps.", "\uE839", "jellyfin infuse swiftfin jellycon findroid sign in login server username password pin"),
                ("WatchProviders", "Watch Providers", "Trakt watch history, favorites, and scrobbling.", "\uE823", "trakt import export scrobble favorites watch history"),
                ("WebhookSync", "Webhook Sync", "Take progress from Plex, Emby, and Jellyfin webhooks.", "\uE968", "plex emby jellyfin webhook progress watched"),
                ("HistoryImport", "History Import", "Bring an existing Emby watch history into Silo.", "\uE8B5", "emby watched history import mapping sync"),
            ]),
            ("Account",
            [
                ("Profiles", "Profiles", "Household profile names, PINs, and library access.", "\uE77B", "profile name pin access primary household library create delete"),
                ("NotificationsSettings", "Notifications", "New-episode alerts by email, Discord, push, or webhook.", "\uEA8F", "new episodes email discord browser push webhooks alerts digest url"),
            ]),
        };

        foreach (var group in groups)
        {
            var section = new StackPanel { Spacing = 12 };
            section.Children.Add(new TextBlock
            {
                Text = group.Label,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
            });
            var cards = new WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 12 };
            section.Children.Add(cards);
            foreach (var entry in group.Items)
            {
                if (entry.Tag == "Profiles" && !_canManageProfiles) continue;
                var card = BuildSettingsOverviewCard(entry.Tag, entry.Label, entry.Description, entry.Glyph);
                cards.Children.Add(card);
                _settingsOverviewCards.Add(new SettingsOverviewCard(
                    card,
                    section,
                    $"{entry.Label} {entry.Description} {entry.Search}"));
            }
            if (cards.Children.Count > 0)
                SettingsOverviewGroups.Children.Add(section);
        }
    }

    private Button BuildSettingsOverviewCard(string tag, string label, string description, string glyph)
    {
        var text = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label, FontSize = 15, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
        });
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 19,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        var chevron = new FontIcon
        {
            Glyph = "\uE76C",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(chevron, 2);
        content.Children.Add(chevron);
        var button = new Button
        {
            Tag = tag,
            Width = 330,
            MinHeight = 92,
            Padding = new Thickness(16, 13, 14, 13),
            CornerRadius = new CornerRadius(18),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            Content = content,
        };
        button.Click += SettingsOverviewCard_Click;
        AutomationProperties.SetName(button, $"{label}. {description}");
        return button;
    }

    private void ShowSettingsOverview()
    {
        _showingSettingsOverview = true;
        SettingsHeaderSubtitle.Text = "Make Silo work the way you like.";
        SettingsOverviewPanel.Visibility = Visibility.Visible;
        SettingsLayoutGrid.Visibility = Visibility.Collapsed;
        SettingsAllSettingsButton.Visibility = Visibility.Collapsed;
        SettingsSearchBox_TextChanged(SettingsSearchBox, null!);
    }

    private void ShowSettingsDetail(Button button)
    {
        _showingSettingsOverview = false;
        SettingsHeaderSubtitle.Text = "Manage your playback preferences, libraries, and display options.";
        SettingsOverviewPanel.Visibility = Visibility.Collapsed;
        SettingsLayoutGrid.Visibility = Visibility.Visible;
        SettingsAllSettingsButton.Visibility = ActualWidth < 1024
            ? Visibility.Visible
            : Visibility.Collapsed;
        Tab_Click(button, new RoutedEventArgs());
    }

    private void SettingsOverviewCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && ResolveSettingsTabButton(tag) is Button target)
            ShowSettingsDetail(target);
    }

    private void SettingsCurrentProfile_Click(object sender, RoutedEventArgs e)
        => App.Services.GetRequiredService<NavigationService>().Navigate<ProfileSelectPage>();

    private void SettingsAllSettings_Click(object sender, RoutedEventArgs e) => ShowSettingsOverview();

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SettingsSearchBox is null || SettingsSearchStatus is null)
            return;

        var query = SettingsSearchBox.Text.Trim();
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_showingSettingsOverview)
        {
            foreach (var entry in _settingsOverviewCards)
                entry.Button.Visibility = tokens.Length == 0 || tokens.All(token =>
                    entry.SearchText.Contains(token, StringComparison.OrdinalIgnoreCase))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            foreach (var section in _settingsOverviewCards.Select(entry => entry.Section).Distinct())
                section.Visibility = _settingsOverviewCards.Any(entry =>
                    ReferenceEquals(entry.Section, section) && entry.Button.Visibility == Visibility.Visible)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            var overviewMatches = _settingsOverviewCards.Count(entry => entry.Button.Visibility == Visibility.Visible);
            SettingsSearchStatus.Text = tokens.Length == 0
                ? $"{_settingsOverviewCards.Count} settings sections"
                : overviewMatches == 0 ? "No matching settings" : $"{overviewMatches} {(overviewMatches == 1 ? "match" : "matches")}";
            return;
        }
        var entries = new (Button Button, string SearchText)[]
        {
            (PlaybackTab, "playback quality language skipping video spoken metadata auto skip intros credits recaps preview auto play next up episodes"),
            (SubtitlesTab, "subtitles subtitle language behavior forced captions font size family color outline background opacity position preview"),
            (AppearanceTab, "appearance theme profile dark light custom date time format clock reset cinema"),
            (InterfaceTab, "navigation cards menu poster size card captions title year artwork preset primary menu"),
            (ThemeEditorTab, "theme editor customize colors css design tokens token overrides custom css community themes preview"),
            (AccessibilityTab, "accessibility readability contrast motion transparency text size weight high contrast preview"),
            (HomeScreenTab, "home screen sections layout rows continue watching next up recently added library order scope reset"),
            (CardOverlaysTab, "card overlays poster badges overlay accent color preset preview icon position styling"),
            (PersonalizeTab, "personalize taste profile recommendations ratings likes dislikes refine"),
            (LibrariesTab, "libraries library visibility access disabled order playback preferences spoken subtitle forced remember"),
            (ImportTab, "history import emby jellyfin plex watched mapping sync fetched matched unmatched progress skipped"),
            (WebhookSyncTab, "webhook sync plex emby jellyfin intake progress watched connections deliveries server url token"),
            (WatchProvidersTab, "watch providers trakt import export scrobble favorites history progress removals"),
            (DevicesTab, "your devices device tv phone tablet browser this device forget hdr dolby vision frame rate fill screen audio subtitle sync offset"),
            (NotificationsSettingsTab, "notifications new episodes email discord browser push webhooks per episode alerts digest url"),
            (ConnectAppsTab, "connect apps silo jellyfin compatible infuse swiftfin jellycon findroid sign in login server address username password pin"),
            (ProfilesTab, "profiles profile names pin access rules primary household library create delete"),
        };

        var matches = 0;
        foreach (var entry in entries)
        {
            if (ReferenceEquals(entry.Button, ProfilesTab) && !_canManageProfiles)
            {
                entry.Button.Visibility = Visibility.Collapsed;
                continue;
            }
            var visible = tokens.Length == 0 || tokens.All(token =>
                entry.SearchText.Contains(token, StringComparison.OrdinalIgnoreCase));
            entry.Button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) matches++;
        }

        PlaybackNavGroup.Visibility = new[] { PlaybackTab, SubtitlesTab, DevicesTab }
            .Any(button => button.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;
        AppearanceNavGroup.Visibility = new[] { AppearanceTab, InterfaceTab, CardOverlaysTab, AccessibilityTab, ThemeEditorTab }
            .Any(button => button.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        HomeDiscoveryNavGroup.Visibility = new[] { HomeScreenTab, PersonalizeTab, LibrariesTab }
            .Any(button => button.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsNavGroup.Visibility = new[] { ConnectAppsTab, WatchProvidersTab, WebhookSyncTab, ImportTab }
            .Any(button => button.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;
        AccountNavGroup.Visibility = new[] { ProfilesTab, NotificationsSettingsTab }
            .Any(button => button.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;

        var availableSettingsCount = _canManageProfiles ? entries.Length : entries.Length - 1;
        SettingsSearchStatus.Text = tokens.Length == 0
            ? $"{availableSettingsCount} settings sections"
            : matches == 0 ? "No matching settings" : $"{matches} {(matches == 1 ? "match" : "matches")}";
    }

    private sealed record SettingsOverviewCard(Button Button, FrameworkElement Section, string SearchText);

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton || clickedButton.Tag is not string tag)
            return;

        var tabs = new[] { AppearanceTab, InterfaceTab, PlaybackTab, LibrariesTab, SubtitlesTab, HomeScreenTab, CardOverlaysTab, PersonalizeTab, ImportTab, WebhookSyncTab, WatchProvidersTab, DevicesTab, NotificationsSettingsTab, ConnectAppsTab, ProfilesTab, ThemeEditorTab, AccessibilityTab, PluginsTab, SessionsTab };
        foreach (var tab in tabs)
        {
            tab.Style = (Style)Resources["InactiveTabStyle"];
        }

        clickedButton.Style = (Style)Resources["ActiveTabStyle"];

        AppearancePanel.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        InterfacePanel.Visibility = tag == "Interface" ? Visibility.Visible : Visibility.Collapsed;
        PlaybackPanel.Visibility = tag == "Playback" ? Visibility.Visible : Visibility.Collapsed;
        LibrariesPanel.Visibility = tag == "Libraries" ? Visibility.Visible : Visibility.Collapsed;
        SubtitlesPanel.Visibility = tag == "Subtitles" ? Visibility.Visible : Visibility.Collapsed;
        HomeScreenPanel.Visibility = tag == "HomeScreen" ? Visibility.Visible : Visibility.Collapsed;
        CardOverlaysPanel.Visibility = tag == "CardOverlays" ? Visibility.Visible : Visibility.Collapsed;
        PersonalizePanel.Visibility = tag == "Personalize" ? Visibility.Visible : Visibility.Collapsed;
        ImportPanel.Visibility = tag == "Import" ? Visibility.Visible : Visibility.Collapsed;
        PluginsPanel.Visibility = tag == "Plugins" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPanel.Visibility = tag == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        WebhookSyncPanel.Visibility = tag == "WebhookSync" ? Visibility.Visible : Visibility.Collapsed;
        WatchProvidersPanel.Visibility = tag == "WatchProviders" ? Visibility.Visible : Visibility.Collapsed;
        NotificationsSettingsPanel.Visibility = tag == "NotificationsSettings" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPanel.Visibility = tag == "Devices" ? Visibility.Visible : Visibility.Collapsed;
        ConnectAppsPanel.Visibility = tag == "ConnectApps" ? Visibility.Visible : Visibility.Collapsed;
        ThemeEditorPanel.Visibility = tag == "ThemeEditor" ? Visibility.Visible : Visibility.Collapsed;
        AccessibilityPanel.Visibility = tag == "Accessibility" ? Visibility.Visible : Visibility.Collapsed;
        SessionsPanel.Visibility = tag == "Sessions" ? Visibility.Visible : Visibility.Collapsed;
        if (tag != "WebhookSync") _webhookRefreshTimer?.Stop();
        if (tag != "Profiles") _householdSessionsTimer?.Stop();

        if (tag == "Interface")
        {
            _ = LoadInterfaceCustomizationAsync();
        }
        else if (tag == "Profiles")
        {
            _ = LoadProfilesAsync();
            StartHouseholdSessionsPolling();
        }

        if (tag == "Sessions")
        {
            _ = LoadSessionsAsync();
        }
        else if (tag == "ThemeEditor")
        {
            BuildThemeTokenEditor();
            _ = LoadThemeCatalogAsync();
        }
        else if (tag == "WebhookSync")
        {
            _ = LoadWebhookConnectionsAsync();
        }
        else if (tag == "HomeScreen")
        {
            _ = ViewModel.LoadHomeSectionsCommand.ExecuteAsync(null);
        }
        else if (tag == "Import")
        {
            _ = EnsureImportTabLoadedAsync();
        }
        else if (tag == "Plugins")
        {
            _ = ViewModel.LoadPluginSettingsCommand.ExecuteAsync(null);
        }
        else if (tag == "WatchProviders")
        {
            _ = LoadWatchProvidersAsync();
        }
        else if (tag == "CardOverlays")
        {
            _ = LoadCardOverlaySettingsAsync();
        }
        else if (tag == "Personalize")
        {
            _ = LoadPersonalizeSummaryAsync();
        }
        else if (tag == "Devices")
        {
            _ = LoadDevicesAsync();
        }
        else if (tag == "ConnectApps")
        {
            _ = LoadConnectAppsAsync();
        }
        else if (tag == "NotificationsSettings" && NotificationsSettingsHost.Content is null)
        {
            NotificationsSettingsHost.Content = new global::SiloPlayer.Controls.NotificationSettingsControl();
        }
    }

    private sealed record InterfaceMenuChoice(string Label, PrimaryMenuItem Item)
    {
        public override string ToString() => Label;
    }

    private async Task LoadInterfaceCustomizationAsync()
    {
        InterfaceControls.IsHitTestVisible = false;
        InterfaceControls.Opacity = 0.55;
        InterfaceStatusCard.Visibility = Visibility.Visible;
        InterfaceStatusTitle.Text = "Checking server support…";
        InterfaceStatusText.Text = "Loading the current desktop navigation and card layout.";
        try
        {
            await _uiCustomizationService.RefreshAsync();
            if (_uiCustomizationService.IsUnavailable)
            {
                ShowInterfaceUnavailable(
                    "Customization unavailable",
                    "Saved navigation and card settings could not be loaded. Editing stays disabled to protect your existing choices.");
                return;
            }
            if (!_uiCustomizationService.IsSupported)
            {
                ShowInterfaceUnavailable(
                    "Server upgrade required",
                    "This server does not support synchronized navigation and card customization yet.");
                return;
            }

            InterfaceStatusCard.Visibility = Visibility.Collapsed;
            InterfaceControls.IsHitTestVisible = true;
            InterfaceControls.Opacity = 1;
            _interfaceMenuDraft.Clear();
            _interfaceMenuDraft.AddRange(
                (_uiCustomizationService.PrimaryMenu?.Items ?? UICustomizationService.DefaultDesktopMenu())
                .Select(item => item.Clone()));
            _interfaceMenuDirty = false;
            SyncInterfaceControls();
        }
        catch (Exception ex)
        {
            ShowInterfaceUnavailable("Customization unavailable", ex.Message);
        }
    }

    private void ShowInterfaceUnavailable(string title, string message)
    {
        InterfaceStatusCard.Visibility = Visibility.Visible;
        InterfaceStatusTitle.Text = title;
        InterfaceStatusText.Text = message;
        InterfaceControls.IsHitTestVisible = false;
        InterfaceControls.Opacity = 0.55;
    }

    private void SyncInterfaceControls()
    {
        var presentation = _uiCustomizationService.CardPresentation;
        _suppressInterfaceEvents = true;
        SelectComboBoxByTag(InterfacePosterSizeCombo, presentation.PosterSize);
        SelectComboBoxByTag(InterfaceCaptionCombo, presentation.Caption);
        _suppressInterfaceEvents = false;

        var cardDeviceOverride = string.Equals(
            _uiCustomizationService.CardPresentationSource,
            "profile_device",
            StringComparison.OrdinalIgnoreCase);
        var menuDeviceOverride = string.Equals(
            _uiCustomizationService.PrimaryMenuSource,
            "profile_device",
            StringComparison.OrdinalIgnoreCase);
        InterfaceCardDeviceOverrideNotice.Visibility = cardDeviceOverride ? Visibility.Visible : Visibility.Collapsed;
        InterfaceMenuDeviceOverrideNotice.Visibility = menuDeviceOverride ? Visibility.Visible : Visibility.Collapsed;
        InterfacePosterSizeCombo.IsEnabled = !cardDeviceOverride;
        InterfaceCaptionCombo.IsEnabled = !cardDeviceOverride;
        BalancedCardPreset.IsEnabled = !cardDeviceOverride;
        CompactCardPreset.IsEnabled = !cardDeviceOverride;
        CinemaCardPreset.IsEnabled = !cardDeviceOverride;
        ArtworkCardPreset.IsEnabled = !cardDeviceOverride;

        UpdateInterfacePresetVisuals();
        BuildInterfaceCardPreview();
        BuildInterfacePrimaryMenu();
        BuildInterfaceAddMenuChoices();
        InterfacePrimaryMenuHost.IsHitTestVisible = !menuDeviceOverride;
        InterfacePrimaryMenuHost.Opacity = menuDeviceOverride ? 0.55 : 1;
        InterfaceAddMenuCombo.IsEnabled = !menuDeviceOverride;
        SaveInterfaceMenuButton.IsEnabled = _interfaceMenuDirty && !menuDeviceOverride;
        ResetDesktopCardLayoutButton.Visibility = string.Equals(
            _uiCustomizationService.CardPresentationSource,
            "profile_client",
            StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async void CardPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value }) return;
        var parts = value.Split(':');
        if (parts.Length != 2) return;
        await SaveInterfaceCardPresentationAsync(new CardPresentation
        {
            PosterSize = parts[0],
            Caption = parts[1],
        });
    }

    private async void InterfaceCardOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressInterfaceEvents ||
            InterfacePosterSizeCombo.SelectedItem is not ComboBoxItem { Tag: string posterSize } ||
            InterfaceCaptionCombo.SelectedItem is not ComboBoxItem { Tag: string caption })
            return;
        await SaveInterfaceCardPresentationAsync(new CardPresentation
        {
            PosterSize = posterSize,
            Caption = caption,
        });
    }

    private async Task SaveInterfaceCardPresentationAsync(CardPresentation presentation)
    {
        InterfaceControls.IsHitTestVisible = false;
        try
        {
            await _uiCustomizationService.SaveCardPresentationAsync(presentation);
            SyncInterfaceControls();
            App.Services.GetRequiredService<ToastService>().Success("Desktop card layout saved");
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error($"Could not save card layout: {ex.Message}");
        }
        finally
        {
            InterfaceControls.IsHitTestVisible = true;
        }
    }

    private async void ResetDesktopCardLayout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _uiCustomizationService.ResetCardPresentationAsync();
            SyncInterfaceControls();
            App.Services.GetRequiredService<ToastService>().Success("Desktop card layout reset");
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error($"Could not reset card layout: {ex.Message}");
        }
    }

    private async void ClearInterfaceCardDeviceOverride_Click(object sender, RoutedEventArgs e)
    {
        await ClearInterfaceDeviceOverrideAsync(UICustomizationSettingKeys.CardPresentation, "Card layout");
    }

    private async void ClearInterfaceMenuDeviceOverride_Click(object sender, RoutedEventArgs e)
    {
        await ClearInterfaceDeviceOverrideAsync(UICustomizationSettingKeys.PrimaryMenu, "Navigation");
    }

    private async Task ClearInterfaceDeviceOverrideAsync(string key, string label)
    {
        try
        {
            await _uiCustomizationService.ClearDeviceOverrideAsync(key);
            _interfaceMenuDraft.Clear();
            _interfaceMenuDraft.AddRange(
                (_uiCustomizationService.PrimaryMenu?.Items ?? UICustomizationService.DefaultDesktopMenu())
                .Select(item => item.Clone()));
            _interfaceMenuDirty = false;
            SyncInterfaceControls();
            App.Services.GetRequiredService<ToastService>().Success($"{label} now follows the desktop-family preference");
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error($"Could not clear {label.ToLowerInvariant()}: {ex.Message}");
        }
    }

    private void UpdateInterfacePresetVisuals()
    {
        var current = $"{_uiCustomizationService.CardPresentation.PosterSize}:{_uiCustomizationService.CardPresentation.Caption}";
        foreach (var button in new[] { BalancedCardPreset, CompactCardPreset, CinemaCardPreset, ArtworkCardPreset })
        {
            var active = string.Equals(button.Tag as string, current, StringComparison.Ordinal);
            button.Opacity = active ? 1 : 0.72;
            button.BorderThickness = active ? new Thickness(2) : new Thickness(1);
            button.BorderBrush = (Brush)Application.Current.Resources[active ? "SidebarAccentBrush" : "BorderBrush"];
        }
    }

    private void BuildInterfaceCardPreview()
    {
        InterfaceCardPreview.Children.Clear();
        var presentation = _uiCustomizationService.CardPresentation;
        var widths = presentation.PosterSize switch
        {
            "compact" => new[] { 46d, 46d, 46d, 46d },
            "large" => new[] { 72d, 72d },
            _ => new[] { 58d, 58d, 58d },
        };
        foreach (var width in widths)
        {
            var stack = new StackPanel { Width = width, Spacing = 6 };
            stack.Children.Add(new Border
            {
                Width = width,
                Height = width * 1.5,
                CornerRadius = new CornerRadius(7),
                Background = (Brush)Application.Current.Resources["SidebarAccentBrush"],
            });
            if (presentation.Caption != "artwork")
            {
                stack.Children.Add(new Border
                {
                    Height = 6,
                    Width = width * 0.8,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(3),
                    Background = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                    Opacity = 0.7,
                });
                if (presentation.Caption == "title_metadata")
                {
                    stack.Children.Add(new Border
                    {
                        Height = 4,
                        Width = width * 0.5,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        CornerRadius = new CornerRadius(2),
                        Background = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                        Opacity = 0.55,
                    });
                }
            }
            InterfaceCardPreview.Children.Add(stack);
        }
    }

    private void BuildInterfacePrimaryMenu()
    {
        InterfacePrimaryMenuHost.Children.Clear();
        for (var index = 0; index < _interfaceMenuDraft.Count; index++)
        {
            var item = _interfaceMenuDraft[index];
            var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(10, 7, 8, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = (index + 1).ToString(),
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var label = new TextBlock
            {
                Text = InterfaceMenuLabel(item),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            actions.Children.Add(CreateInterfaceMenuAction("\uE74A", $"Move {label.Text} up", index, -1, index == 0));
            actions.Children.Add(CreateInterfaceMenuAction("\uE74B", $"Move {label.Text} down", index, 1, index == _interfaceMenuDraft.Count - 1));
            var home = item.Type == "builtin" && item.Destination == "home";
            actions.Children.Add(CreateInterfaceMenuAction("\uE74D", home ? "Home cannot be removed" : $"Remove {label.Text}", index, 0, home));
            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);

            InterfacePrimaryMenuHost.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = row,
            });
        }
    }

    private Button CreateInterfaceMenuAction(string glyph, string name, int index, int direction, bool disabled)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            IsEnabled = !disabled,
            Tag = (index, direction),
        };
        AutomationProperties.SetName(button, name);
        button.Click += InterfaceMenuAction_Click;
        return button;
    }

    private void InterfaceMenuAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<int, int> action }) return;
        var (index, direction) = action;
        if (index < 0 || index >= _interfaceMenuDraft.Count) return;
        if (direction == 0)
        {
            _interfaceMenuDraft.RemoveAt(index);
        }
        else
        {
            var target = index + direction;
            if (target < 0 || target >= _interfaceMenuDraft.Count) return;
            (_interfaceMenuDraft[index], _interfaceMenuDraft[target]) =
                (_interfaceMenuDraft[target], _interfaceMenuDraft[index]);
        }
        _interfaceMenuDirty = true;
        SyncInterfaceControls();
    }

    private void BuildInterfaceAddMenuChoices()
    {
        InterfaceAddMenuCombo.Items.Clear();
        var current = _interfaceMenuDraft.Select(item => item.SemanticKey).ToHashSet(StringComparer.Ordinal);
        var candidates = new List<PrimaryMenuItem>
        {
            PrimaryMenuItem.Builtin("for_you"),
            PrimaryMenuItem.Builtin("calendar"),
        };
        candidates.AddRange(ViewModel.LibraryCards.Select(card => new PrimaryMenuItem
        {
            Type = "library",
            LibraryId = card.LibraryId,
            Label = card.LibraryName,
        }));
        candidates.AddRange(_uiCustomizationService.Shortcuts.Items.Select(item => item.Clone()));

        foreach (var item in candidates
                     .Where(item => !current.Contains(item.SemanticKey))
                     .DistinctBy(item => item.SemanticKey, StringComparer.Ordinal))
        {
            InterfaceAddMenuCombo.Items.Add(new ComboBoxItem
            {
                Content = InterfaceMenuLabel(item),
                Tag = new InterfaceMenuChoice(InterfaceMenuLabel(item), item),
            });
        }
        InterfaceAddMenuCombo.SelectedItem = null;
    }

    private void AddInterfaceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (InterfaceAddMenuCombo.SelectedItem is not ComboBoxItem { Tag: InterfaceMenuChoice choice }) return;
        if (_interfaceMenuDraft.Count >= 64) return;
        _interfaceMenuDraft.Add(choice.Item.Clone());
        _interfaceMenuDirty = true;
        SyncInterfaceControls();
    }

    private async void SaveInterfaceMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _uiCustomizationService.SavePrimaryMenuAsync(_interfaceMenuDraft);
            _interfaceMenuDirty = false;
            SyncInterfaceControls();
            App.Services.GetRequiredService<ToastService>().Success("Desktop navigation saved");
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error($"Could not save navigation: {ex.Message}");
        }
    }

    private async void ResetInterfaceMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _uiCustomizationService.ResetPrimaryMenuAsync();
            _interfaceMenuDraft.Clear();
            _interfaceMenuDraft.AddRange(UICustomizationService.DefaultDesktopMenu());
            _interfaceMenuDirty = false;
            SyncInterfaceControls();
            App.Services.GetRequiredService<ToastService>().Success("Desktop navigation reset");
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error($"Could not reset navigation: {ex.Message}");
        }
    }

    private static string InterfaceMenuLabel(PrimaryMenuItem item) => item.Type switch
    {
        "builtin" when item.Destination == "home" => "Home",
        "builtin" when item.Destination == "for_you" => "For You",
        "builtin" when item.Destination == "calendar" => "Calendar",
        "builtin" => item.Destination ?? "Destination",
        "library" => $"{item.Label} · Library",
        "section" => $"{item.Label} · Section",
        "collection" => $"{item.Label} · Collection",
        _ => item.Label ?? "Destination",
    };

    private async Task LoadPersonalizeSummaryAsync()
    {
        try
        {
            var favorites = await App.Services.GetRequiredService<CatalogApi>().GetFavoritesAsync();
            var count = favorites.Items.Count;
            PersonalizeFavoritesText.Text = count switch
            {
                0 => "You haven't favorited anything yet.",
                1 => "1 favorite is shaping your recommendations.",
                _ => $"{count:N0} favorites are shaping your recommendations.",
            };
        }
        catch
        {
            PersonalizeFavoritesText.Text = "Favorite count is unavailable.";
        }
    }

    private void OpenTasteSeed_Click(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(TasteSeedPage), true);

    private async void StartFeatureTour_Click(object sender, RoutedEventArgs e)
    {
        StartFeatureTourButton.IsEnabled = false;
        try
        {
            var flow = await App.Services.GetRequiredService<SettingsApi>()
                .GetOnboardingFlowAsync("web");
            var dialog = new FeatureTourDialog(flow) { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>()
                .Error($"Couldn’t start the feature tour: {ex.Message}");
        }
        finally
        {
            StartFeatureTourButton.IsEnabled = true;
        }
    }

    private async Task LoadCardOverlaySettingsAsync()
    {
        try
        {
            await _cardOverlayService.EnsureLoadedAsync();
            _cardOverlayDraft = _cardOverlayService.GetDocument();
            CardOverlaysDisabledBanner.Visibility = _cardOverlayService.Enabled
                ? Visibility.Collapsed
                : Visibility.Visible;
            CardOverlaysContentHost.Opacity = _cardOverlayService.Enabled ? 1 : 0.5;
            CardOverlaysContentHost.IsHitTestVisible = _cardOverlayService.Enabled;
            BuildCardOverlayControls();
            BuildCardOverlayPresetCards();
            UpdateCardOverlayPreview();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Failed to load card overlays: {ex.Message}";
        }
    }

    private static readonly (string Title, string Description, string[] Ids)[] OverlayGroups =
    [
        ("Technical", "Video, audio, format, and release details.",
            ["resolution", "hdr", "resolution_hdr", "audio", "audio_channels", "video_codec", "container", "aspect_ratio", "release_type", "edition", "multi_audio", "multi_sub"]),
        ("Ratings", "IMDb, TMDB, Rotten Tomatoes, and age-rating badges.",
            ["rating_imdb", "rating_tmdb", "rating_rt", "rating_rt_audience", "content_rating"]),
        ("Metadata", "Year, runtime, language, studio, and network context.",
            ["year", "runtime", "original_language", "studio", "network"]),
        ("Ribbons", "Show status, awards, and chart recognition.",
            ["show_status"]),
    ];

    private static readonly (string Label, string? Value)[] OverlayAccentOptions =
    [
        ("Default", null), ("Gold", "#f5c518"), ("Tomato", "#fa320a"),
        ("Orange", "#f97316"), ("Amber", "#f59e0b"), ("Emerald", "#10b981"),
        ("Cyan", "#06b6d4"), ("Blue", "#3b82f6"), ("Indigo", "#6366f1"),
        ("Violet", "#8b5cf6"), ("Pink", "#ec4899"), ("Slate", "#64748b"), ("White", "#ffffff"),
    ];

    private void BuildCardOverlayControls()
    {
        CardOverlayControlsContainer.Children.Clear();
        if (_cardOverlayDraft is null) return;

        var definitions = OverlayRegistry.All.ToDictionary(def => def.Id, StringComparer.Ordinal);
        foreach (var group in OverlayGroups)
        {
            var card = new Border { Style = (Style)Resources["SettingsGroupStyle"] };
            var stack = new StackPanel { Spacing = 0 };
            stack.Children.Add(new TextBlock { Text = group.Title, FontSize = 18, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(new TextBlock
            {
                Text = group.Description,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 3, 0, 12),
            });

            foreach (var id in group.Ids)
            {
                if (!definitions.TryGetValue(id, out var def) || !_cardOverlayDraft.Items.TryGetValue(id, out var config))
                    continue;
                if (stack.Children.Count > 2)
                    stack.Children.Add(new Border { Style = (Style)Resources["SettingsSeparator"] });
                stack.Children.Add(BuildCardOverlayRow(def, config));
            }
            card.Child = stack;
            CardOverlayControlsContainer.Children.Add(card);
        }
    }

    private void CardOverlaySectionTab_Click(object sender, RoutedEventArgs e)
    {
        var showStyle = sender is Button { Tag: "style" };
        CardOverlayControlsContainer.Visibility = showStyle ? Visibility.Collapsed : Visibility.Visible;
        CardOverlayStylePanel.Visibility = showStyle ? Visibility.Visible : Visibility.Collapsed;
        CardOverlayOverlaysTab.Style = (Style)Resources[showStyle ? "InactiveTabStyle" : "ActiveTabStyle"];
        CardOverlayStyleTab.Style = (Style)Resources[showStyle ? "ActiveTabStyle" : "InactiveTabStyle"];
    }

    private void OverlayPreviewVariant_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string variant }) return;
        _cardOverlayPreviewVariant = variant;
        OverlayMoviePreviewButton.Style = (Style)Resources[variant == "movie" ? "ActiveTabStyle" : "InactiveTabStyle"];
        OverlayShowPreviewButton.Style = (Style)Resources[variant == "show" ? "ActiveTabStyle" : "InactiveTabStyle"];
        UpdateCardOverlayPreview();
    }

    private FrameworkElement BuildCardOverlayRow(OverlayDef definition, OverlayItemConfig initial)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = definition.Label, FontSize = 14, FontWeight = FontWeights.Medium });
        label.Children.Add(new TextBlock
        {
            Text = OverlayDescription(definition.Id),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        var suppressed = initial.Enabled && _cardOverlayDraft is not null &&
            OverlayRegistry.SuppressesStandaloneOverlays(definition.Id, _cardOverlayDraft.Items);
        var hint = suppressed
            ? "Hidden while the combined Resolution + HDR badge is enabled."
            : OverlayAvailabilityNote(definition.Id);
        if (!string.IsNullOrWhiteSpace(hint))
        {
            label.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }
        grid.Children.Add(label);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var iconVisible = initial.ShowIcon ?? (_cardOverlayDraft?.Preset is "vibrant" or "pill");
        var iconButton = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Content = new FontIcon { Glyph = iconVisible ? "\uE91B" : "\uEB9F", FontSize = 15 },
            IsEnabled = initial.Enabled,
            Visibility = OverlaySupportsIcon(definition.Id) ? Visibility.Visible : Visibility.Collapsed,
        };
        ToolTipService.SetToolTip(iconButton, iconVisible ? "Hide icon" : "Show icon");

        var selectedAccent = initial.AccentColor;
        var accentButton = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            IsEnabled = initial.Enabled,
            Background = ParseCardOverlayAccent(selectedAccent ?? OverlayDefaultAccent(definition.Id)),
        };
        ToolTipService.SetToolTip(accentButton,
            selectedAccent is null ? "Default accent" : $"Accent: {selectedAccent}");

        var position = new ComboBox { Width = 130 };
        var positions = new[]
        {
            (OverlayPosition.TopLeft, "Top left"), (OverlayPosition.TopRight, "Top right"),
            (OverlayPosition.BottomLeft, "Bottom left"), (OverlayPosition.BottomRight, "Bottom right"),
        };
        foreach (var option in positions)
            position.Items.Add(new ComboBoxItem { Content = option.Item2, Tag = option.Item1 });
        position.SelectedIndex = Array.FindIndex(positions, option => option.Item1 == initial.Position);
        var enabled = new ToggleSwitch { IsOn = initial.Enabled };

        void Apply()
        {
            if (_cardOverlayDraft is null) return;
            var selectedPosition = (position.SelectedItem as ComboBoxItem)?.Tag is OverlayPosition pos ? pos : initial.Position;
            iconButton.IsEnabled = enabled.IsOn;
            accentButton.IsEnabled = enabled.IsOn;
            UpdateCardOverlayItem(definition.Id, new OverlayItemConfig(enabled.IsOn, selectedPosition, selectedAccent, iconVisible));
        }
        iconButton.Click += (_, _) =>
        {
            iconVisible = !iconVisible;
            iconButton.Content = new FontIcon { Glyph = iconVisible ? "\uE91B" : "\uEB9F", FontSize = 15 };
            ToolTipService.SetToolTip(iconButton, iconVisible ? "Hide icon" : "Show icon");
            Apply();
        };
        accentButton.Flyout = BuildCardOverlayAccentFlyout(
            definition.Id,
            () => selectedAccent,
            value =>
            {
                selectedAccent = value;
                accentButton.Background = ParseCardOverlayAccent(value ?? OverlayDefaultAccent(definition.Id));
                ToolTipService.SetToolTip(accentButton, value is null ? "Default accent" : $"Accent: {value}");
                Apply();
            });
        position.SelectionChanged += (_, _) => Apply();
        enabled.Toggled += (_, _) => Apply();
        controls.Children.Add(iconButton);
        controls.Children.Add(accentButton);
        controls.Children.Add(position);
        controls.Children.Add(enabled);
        Grid.SetColumn(controls, 1);
        grid.Children.Add(controls);
        return grid;
    }

    private void UpdateCardOverlayItem(string id, OverlayItemConfig config)
    {
        if (_cardOverlayDraft is null) return;
        var items = _cardOverlayDraft.Items.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        items[id] = config;
        _cardOverlayDraft = _cardOverlayDraft with { Items = items };
        UpdateCardOverlayPreview();
        ScheduleCardOverlaySave();
    }

    private static SolidColorBrush ParseCardOverlayAccent(string? value)
    {
        if (value is { Length: 7 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit))
        {
            return new SolidColorBrush(ColorHelper.FromArgb(
                255,
                Convert.ToByte(value.Substring(1, 2), 16),
                Convert.ToByte(value.Substring(3, 2), 16),
                Convert.ToByte(value.Substring(5, 2), 16)));
        }
        return new SolidColorBrush(ColorHelper.FromArgb(255, 148, 163, 184));
    }

    private Flyout BuildCardOverlayAccentFlyout(
        string overlayId,
        Func<string?> current,
        Action<string?> selected)
    {
        var flyout = new Flyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
        };
        var stack = new StackPanel { Spacing = 8, Width = 200 };
        stack.Children.Add(new TextBlock
        {
            Text = "Accent color",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 50,
        });
        var swatches = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (var column = 0; column < 6; column++)
            swatches.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        swatches.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        swatches.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 1; index < OverlayAccentOptions.Length; index++)
        {
            var option = OverlayAccentOptions[index];
            var button = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                BorderThickness = string.Equals(current(), option.Value, StringComparison.OrdinalIgnoreCase)
                    ? new Thickness(2)
                    : new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources[
                    string.Equals(current(), option.Value, StringComparison.OrdinalIgnoreCase)
                        ? "PrimaryTextBrush"
                        : "BorderBrush"],
                Background = ParseCardOverlayAccent(option.Value),
                Tag = option.Value,
            };
            ToolTipService.SetToolTip(button, option.Label);
            button.Click += (_, _) =>
            {
                selected(option.Value);
                flyout.Hide();
            };
            Grid.SetColumn(button, (index - 1) % 6);
            Grid.SetRow(button, (index - 1) / 6);
            swatches.Children.Add(button);
        }
        stack.Children.Add(swatches);
        var reset = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE711", FontSize = 12 },
                    new TextBlock { Text = "Reset to default", FontSize = 12 },
                },
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        reset.Click += (_, _) =>
        {
            selected(null);
            flyout.Hide();
        };
        stack.Children.Add(reset);
        flyout.Content = stack;
        return flyout;
    }

    private static bool OverlaySupportsIcon(string id) => id is not
        ("year" or "container" or "release_type" or "edition");

    private static string? OverlayDefaultAccent(string id) => id switch
    {
        "rating_imdb" => "#f5c518",
        "rating_tmdb" => "#01b4e4",
        "rating_rt" => "#fa320a",
        "rating_rt_audience" => "#fa6400",
        _ => null,
    };

    private static readonly (string Id, string Label, string Description)[] CardOverlayPresets =
    [
        ("minimal", "Minimal", "Near-invisible. Tiny text, no background."),
        ("classic", "Classic", "Semi-transparent dark pill with a white border. The default."),
        ("vibrant", "Vibrant", "Opaque, accent-colored badges. High contrast."),
        ("pill", "Pill", "Larger pill with more padding. Works well with icons."),
        ("square", "Square", "Blocky, high-density. Plex-inspired."),
    ];

    private void BuildCardOverlayPresetCards()
    {
        CardOverlayPresetHost.Children.Clear();
        if (_cardOverlayDraft is null) return;
        foreach (var preset in CardOverlayPresets)
        {
            var active = string.Equals(_cardOverlayDraft.Preset, preset.Id, StringComparison.Ordinal);
            var sampleConfig = new OverlayItemConfig(true, OverlayPosition.TopLeft,
                preset.Id == "vibrant" ? "#f5c518" : null);
            var content = new StackPanel { Spacing = 7 };
            var preview = new Border
            {
                Height = 48,
                CornerRadius = new CornerRadius(6),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = ColorHelper.FromArgb(255, 51, 65, 85), Offset = 0 },
                        new GradientStop { Color = ColorHelper.FromArgb(255, 15, 23, 42), Offset = 1 },
                    },
                },
                Child = global::SiloPlayer.Controls.PosterCard.BuildBadge("Sample", "resolution", sampleConfig, preset.Id),
            };
            content.Children.Add(preview);
            content.Children.Add(new TextBlock { Text = preset.Label, FontSize = 14, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock
            {
                Text = preset.Description,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
            var card = new Button
            {
                Width = 172,
                MinHeight = 124,
                Padding = new Thickness(12),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                Background = active
                    ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                    : new SolidColorBrush(Colors.Transparent),
                BorderBrush = (Brush)Application.Current.Resources[active ? "AccentBrush" : "BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Content = content,
                Tag = preset.Id,
            };
            card.Click += (_, _) => SetCardOverlayPreset(preset.Id);
            CardOverlayPresetHost.Children.Add(card);
        }
    }

    private void SetCardOverlayPreset(string preset)
    {
        if (_cardOverlayDraft is null || _cardOverlayDraft.Preset == preset) return;
        _cardOverlayDraft = _cardOverlayDraft with { Preset = preset };
        BuildCardOverlayControls();
        BuildCardOverlayPresetCards();
        UpdateCardOverlayPreview();
        ScheduleCardOverlaySave();
    }

    private void ScheduleCardOverlaySave()
    {
        if (_cardOverlayDraft is null) return;
        var snapshot = _cardOverlayDraft;
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _overlaySaveCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        _ = SaveCardOverlayAfterDelayAsync(snapshot, cts);
    }

    private async Task SaveCardOverlayAfterDelayAsync(CardOverlayPrefs snapshot, CancellationTokenSource ownerCts)
    {
        try
        {
            await Task.Delay(250, ownerCts.Token);
            await _cardOverlayService.SaveAsync(snapshot, ownerCts.Token);
            if (ReferenceEquals(_overlaySaveCts, ownerCts)) ViewModel.StatusMessage = "Setting saved";
        }
        catch (OperationCanceledException) when (ownerCts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_overlaySaveCts, ownerCts)) ViewModel.ErrorMessage = ex.Message;
        }
        finally
        {
            Interlocked.CompareExchange(ref _overlaySaveCts, null, ownerCts);
            ownerCts.Dispose();
        }
    }

    private void UpdateCardOverlayPreview()
    {
        OverlayPreviewTopLeft.Children.Clear();
        OverlayPreviewTopRight.Children.Clear();
        OverlayPreviewBottomLeft.Children.Clear();
        OverlayPreviewBottomRight.Children.Clear();
        if (_cardOverlayDraft is null) return;
        var sampleValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["resolution"] = "4K", ["hdr"] = "HDR10", ["resolution_hdr"] = "4K HDR",
            ["audio"] = "Atmos", ["audio_channels"] = "7.1", ["video_codec"] = "HEVC",
            ["container"] = "MKV", ["aspect_ratio"] = "2.39:1", ["release_type"] = "Remux",
            ["edition"] = "Director's Cut", ["multi_audio"] = "Multi-Audio", ["multi_sub"] = "CC",
            ["rating_imdb"] = "8.7", ["rating_tmdb"] = "8.4", ["rating_rt"] = "96%",
            ["rating_rt_audience"] = "92%", ["content_rating"] = "TV-MA", ["year"] = "2026",
            ["runtime"] = "1h 58m", ["original_language"] = "EN", ["studio"] = "Studio",
            ["network"] = "Network", ["show_status"] = "Returning", ["imdb_top_250"] = "Top 250",
            ["rt_certified_fresh"] = "Certified Fresh",
        };
        if (_cardOverlayPreviewVariant == "show")
        {
            sampleValues["runtime"] = "47m";
            sampleValues["year"] = "2024";
            sampleValues["show_status"] = "Returning";
        }
        foreach (var definition in OverlayRegistry.All)
        {
            if (!_cardOverlayDraft.Items.TryGetValue(definition.Id, out var config) || !config.Enabled) continue;
            if (OverlayRegistry.SuppressesStandaloneOverlays(definition.Id, _cardOverlayDraft.Items)) continue;
            if (!sampleValues.TryGetValue(definition.Id, out var value)) continue;
            var badge = global::SiloPlayer.Controls.PosterCard.BuildBadge(value, definition.Id, config, _cardOverlayDraft.Preset);
            var host = config.Position switch
            {
                OverlayPosition.TopLeft => OverlayPreviewTopLeft,
                OverlayPosition.TopRight => OverlayPreviewTopRight,
                OverlayPosition.BottomLeft => OverlayPreviewBottomLeft,
                OverlayPosition.BottomRight => OverlayPreviewBottomRight,
                _ => OverlayPreviewTopLeft,
            };
            host.Children.Add(badge);
        }
    }

    private static string OverlayDescription(string id) => id switch
    {
        "resolution" => "Video resolution (4K, 1080p, 720p, etc.)",
        "hdr" => "Dynamic range format (HDR10, DV, HLG)",
        "resolution_hdr" => "Single badge combining resolution and dynamic range (e.g. \"4K DV\", \"1080p HDR\")",
        "audio" => "Audio codec (Atmos, DTS-HD, TrueHD, etc.)",
        "audio_channels" => "Channel layout (Stereo, 5.1, 7.1)",
        "video_codec" => "Video codec (H.264, H.265, AV1)",
        "container" => "File container (MKV, MP4, etc.)",
        "aspect_ratio" => "Display aspect ratio (16:9, 2.39:1, etc.)",
        "release_type" => "Source format (REMUX, BluRay, WEB-DL, etc.)",
        "edition" => "Edition label from the best available media version",
        "multi_audio" => "Shown when the file has audio in 2+ languages",
        "multi_sub" => "Shown when the file has any subtitle track",
        "rating_imdb" => "IMDb score out of 10",
        "rating_tmdb" => "TMDB score out of 10",
        "rating_rt" => "Rotten Tomatoes critic score",
        "rating_rt_audience" => "Rotten Tomatoes audience score",
        "content_rating" => "Content rating (PG-13, TV-MA, R, etc.)",
        "year" => "Release year",
        "runtime" => "Item runtime in hours and minutes",
        "original_language" => "Original language of the content",
        "studio" => "Primary production studio (movies)",
        "network" => "Primary network (series)",
        "show_status" => "Series lifecycle: Returning, Ended, Cancelled",
        _ => "Poster card badge.",
    };

    private static string? OverlayAvailabilityNote(string id) => id == "show_status"
        ? "Populated by metadata plugins (TMDB/TVDB updates pending)"
        : null;

    // ===== Theme cards =====
    private void BuildThemeCards()
    {
        ThemeCardsContainer.Items.Clear();

        var themeService = App.Services.GetRequiredService<ThemeService>();
        var currentTheme = themeService.CurrentTheme;
        var allThemes = ThemeService.GetAllThemeInfos().Where(theme => theme.IsCurated);

        bool addedCuratedHeader = false;

        foreach (var themeInfo in allThemes)
        {
            // Add group headers
            if (themeInfo.IsCurated && !addedCuratedHeader)
            {
                ThemeCardsContainer.Items.Add(BuildSectionHeader("Featured"));
                addedCuratedHeader = true;
            }
            var isActive = themeInfo.Id == currentTheme;
            var card = BuildThemeCard(themeInfo.Id, themeInfo.Label, themeInfo.Description,
                themeInfo.PreviewAccent, themeInfo.PreviewBackground, isActive);
            ThemeCardsContainer.Items.Add(card);
        }
    }

    private static TextBlock BuildSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            Margin = new Thickness(0, 12, 0, 4),
        };
    }

    private Button BuildThemeCard(string themeId, string displayName, string description,
        string accentHex, string bgHex, bool isActive)
    {
        var accentColor = ColorFromHex(accentHex);
        var bgColor = ColorFromHex(bgHex);

        // Outer card border
        var card = new Border
        {
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            Background = isActive
                ? new SolidColorBrush(ColorFromHex("#1D2A3B"))
                : (Brush)Application.Current.Resources["SurfaceBrush"],
            BorderBrush = isActive
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x4D, accentColor.R, accentColor.G, accentColor.B))
                : (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
        };

        var outerStack = new StackPanel { Spacing = 12 };

        // Header row: name + check + swatch
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameStack.Children.Add(new TextBlock
        {
            Text = displayName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (isActive)
        {
            nameStack.Children.Add(new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        Grid.SetColumn(nameStack, 0);
        headerRow.Children.Add(nameStack);

        // Color swatch
        var swatchStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var swatch = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(accentColor),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, 255, 255, 255)),
            BorderThickness = new Thickness(1),
        };
        swatchStack.Children.Add(swatch);
        Grid.SetColumn(swatchStack, 1);
        headerRow.Children.Add(swatchStack);

        outerStack.Children.Add(headerRow);

        // Description
        if (!string.IsNullOrEmpty(description))
        {
            outerStack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // Mini preview panel (simulating the web UI)
        var previewBorder = new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Height = 70,
        };

        var previewStack = new StackPanel { Spacing = 8 };

        // First row: accent dot + line
        var previewRow1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        previewRow1.Children.Add(new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(accentColor),
        });
        previewRow1.Children.Add(new Border
        {
            Width = 64, Height = 8, CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB3, 255, 255, 255)),
        });
        previewStack.Children.Add(previewRow1);

        // Second row: simulated content blocks
        var previewRow2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        previewRow2.Children.Add(new Border
        {
            Width = 80, Height = 28, CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x18, 255, 255, 255)),
        });
        previewRow2.Children.Add(new Border
        {
            Width = 50, Height = 28, CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x12, 255, 255, 255)),
        });
        previewStack.Children.Add(previewRow2);

        previewBorder.Child = previewStack;
        outerStack.Children.Add(previewBorder);

        card.Child = outerStack;

        var themeService = App.Services.GetRequiredService<ThemeService>();
        var button = new Button
        {
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = card,
        };
        AutomationProperties.SetName(button,
            isActive ? $"{displayName} theme, selected" : $"Select {displayName} theme");
        button.PointerEntered += (_, _) => themeService.PreviewTheme(themeId);
        button.PointerExited += (_, _) => themeService.CancelThemePreview();
        button.GotFocus += (_, _) => themeService.PreviewTheme(themeId);
        button.LostFocus += (_, _) => themeService.CancelThemePreview();

        // Click handler
        button.Click += (_, _) =>
        {
            themeService.CommitThemePreview(themeId);
            ViewModel.UiTheme = themeId;
            _ = ViewModel.SaveUiThemeCommand.ExecuteAsync(null);
            BuildThemeCards();
            UpdateCurrentThemeDisplay();
        };

        return button;
    }

    private void UpdateCurrentThemeDisplay()
    {
        var themeService = App.Services.GetRequiredService<ThemeService>();
        var info = ThemeService.GetThemeInfo(themeService.CurrentTheme);
        CurrentThemeName.Text = info?.Label ?? ThemeService.GetDisplayName(themeService.CurrentTheme);
        CurrentThemeDescription.Text = info?.Description ?? "";
    }

    private void ResetTheme_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.UiTheme = "midnight-cinema";
        _ = ViewModel.SaveUiThemeCommand.ExecuteAsync(null);
        BuildThemeCards();
        UpdateCurrentThemeDisplay();
    }

    private void BuildDateTimeFormatControls()
    {
        BuildFormatButtons(DateFormatButtons,
        [
            ("auto", "Auto (Windows)"), ("DD/MM/YYYY", "DD/MM/YYYY"),
            ("MM/DD/YYYY", "MM/DD/YYYY"), ("YYYY-MM-DD", "YYYY-MM-DD")
        ], ViewModel.DateFormat, async value => await ViewModel.SetDateFormatAsync(value));
        BuildFormatButtons(TimeFormatButtons,
        [
            ("auto", "Auto (Windows)"), ("12h", "12-hour"), ("24h", "24-hour")
        ], ViewModel.TimeFormat, async value => await ViewModel.SetTimeFormatAsync(value));
        UpdateDateTimePreview();
    }

    private void BuildFormatButtons(StackPanel panel, IReadOnlyList<(string Value, string Label)> choices,
        string selected, Func<string, Task> save)
    {
        panel.Children.Clear();
        foreach (var choice in choices)
        {
            var button = new Button
            {
                Content = choice.Label,
                Tag = choice.Value,
                Style = (Style)Application.Current.Resources[choice.Value == selected ? "AccentButtonStyle" : "SecondaryButtonStyle"],
                Padding = new Thickness(12, 6, 12, 6)
            };
            button.Click += async (_, _) =>
            {
                await save(choice.Value);
                BuildDateTimeFormatControls();
            };
            panel.Children.Add(button);
        }
    }

    private void UpdateDateTimePreview()
    {
        var now = DateTimeOffset.Now;
        DateTimePreviewText.Text = $"Preview: {DateTimeDisplay.FormatDate(now)} · {DateTimeDisplay.FormatDate(now, medium: true)} · {DateTimeDisplay.FormatTime(now)}";
    }

    // ===== Library cards =====
    private void LibraryCards_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildLibraryCards();
        RebuildHomeScopeOptions();
    }

    private void RebuildHomeScopeOptions()
    {
        var selected = ViewModel.SelectedScope;
        HomeScopeComboBox.Items.Clear();
        HomeScopeComboBox.Items.Add(new ComboBoxItem { Content = "Home", Tag = "home" });
        foreach (var library in ViewModel.LibraryCards)
            HomeScopeComboBox.Items.Add(new ComboBoxItem { Content = library.LibraryName, Tag = $"library:{library.LibraryId}" });
        HomeScopeComboBox.SelectedItem = HomeScopeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, selected)) ?? HomeScopeComboBox.Items[0];
    }

    private async void HomeScopeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HomeScopeComboBox.SelectedItem is not ComboBoxItem { Tag: string scope } || scope == ViewModel.SelectedScope) return;
        ViewModel.SelectedScope = scope;
        await ViewModel.LoadHomeSectionsCommand.ExecuteAsync(null);
    }

    private void RebuildLibraryCards()
    {
        LibraryCardsContainer.Children.Clear();

        if (ViewModel.LibraryCards.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "No libraries are available for this account right now.",
                Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
                Margin = new Thickness(0, 8, 0, 0),
            };
            LibraryCardsContainer.Children.Add(emptyText);
            LibraryVisibilitySummary.Visibility = Visibility.Collapsed;
            return;
        }

        if (!ViewModel.HasSelectedProfile)
        {
            LibraryCardsContainer.Children.Add(new TextBlock
            {
                Text = "Choose a profile to manage libraries.",
                Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
                Margin = new Thickness(0, 8, 0, 0),
            });
            LibraryVisibilitySummary.Visibility = Visibility.Collapsed;
            return;
        }

        LibraryVisibilitySummary.Visibility = Visibility.Visible;

        foreach (var card in ViewModel.LibraryCards)
        {
            LibraryCardsContainer.Children.Add(BuildLibraryCard(card));
        }
    }

    private void ShowLibraryLoadingState()
    {
        LibraryVisibilitySummary.Visibility = Visibility.Collapsed;
        LibraryCardsContainer.Children.Clear();
        LibraryCardsContainer.Children.Add(new TextBlock
        {
            Text = "Loading libraries...",
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
            Margin = new Thickness(0, 8, 0, 0),
        });
    }

    private Border BuildLibraryCard(LibraryCardViewModel vm)
    {
        var cardBorder = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(20),
            BorderThickness = new Thickness(0),
        };

        var outerStack = new StackPanel { Spacing = 12 };

        // === Row 1: reorder controls, name + badges, visibility toggle ===
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dragHandle = new Button
        {
            Content = new FontIcon { Glyph = "\uE700", FontSize = 15 },
            Padding = new Thickness(6),
            MinWidth = 28,
            Margin = new Thickness(0, 0, 10, 0),
            CanDrag = true,
        };
        AutomationProperties.SetName(dragHandle, $"Drag to reorder {vm.LibraryName}");
        ToolTipService.SetToolTip(dragHandle, "Drag to reorder");
        dragHandle.DragStarting += (_, args) =>
        {
            args.Data.SetText(vm.LibraryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Data.RequestedOperation = DataPackageOperation.Move;
        };
        dragHandle.KeyDown += async (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Up)
            {
                args.Handled = true;
                await ViewModel.MoveLibraryAsync(vm.LibraryId, -1);
            }
            else if (args.Key == Windows.System.VirtualKey.Down)
            {
                args.Handled = true;
                await ViewModel.MoveLibraryAsync(vm.LibraryId, 1);
            }
        };
        Grid.SetColumn(dragHandle, 0);
        headerGrid.Children.Add(dragHandle);

        cardBorder.AllowDrop = true;
        cardBorder.DragOver += (_, args) =>
        {
            args.AcceptedOperation = DataPackageOperation.Move;
            args.DragUIOverride.Caption = $"Move before {vm.LibraryName}";
        };
        cardBorder.Drop += async (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.Text)) return;
            var value = await args.DataView.GetTextAsync();
            if (!int.TryParse(value, out var sourceId) || sourceId == vm.LibraryId) return;
            var targetIndex = ViewModel.LibraryCards.ToList().FindIndex(card => card.LibraryId == vm.LibraryId);
            await ViewModel.MoveLibraryToAsync(sourceId, targetIndex);
        };

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        headerRow.Children.Add(new TextBlock
        {
            Text = vm.LibraryName,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Type badge
        var typeBadge = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        typeBadge.Child = new TextBlock
        {
            Text = vm.LibraryType,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            FontSize = 11,
        };
        headerRow.Children.Add(typeBadge);

        // Custom badge
        var customBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = vm.HasCustomOverrides ? Visibility.Visible : Visibility.Collapsed,
        };
        customBadge.Child = new TextBlock
        {
            Text = "Custom",
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            FontSize = 11,
        };
        headerRow.Children.Add(customBadge);

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LibraryCardViewModel.HasCustomOverrides))
            {
                customBadge.Visibility = vm.HasCustomOverrides ? Visibility.Visible : Visibility.Collapsed;
            }
        };

        Grid.SetColumn(headerRow, 1);
        headerGrid.Children.Add(headerRow);

        // Visibility toggle
        var togglePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        var visibilityLabel = new TextBlock
        {
            Text = "Visible in navigation",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        togglePanel.Children.Add(visibilityLabel);

        var visibilityToggle = new ToggleSwitch
        {
            IsOn = vm.IsEnabled,
            MinWidth = 0,
            MinHeight = 0,
            OnContent = "",
            OffContent = "",
        };
        visibilityToggle.Toggled += (s, e) =>
        {
            vm.IsEnabled = visibilityToggle.IsOn;
            visibilityLabel.Text = "Visible in navigation";
        };
        togglePanel.Children.Add(visibilityToggle);

        Grid.SetColumn(togglePanel, 2);
        headerGrid.Children.Add(togglePanel);

        outerStack.Children.Add(headerGrid);

        // === Row 2: Summary text ===
        var summaryText = new TextBlock
        {
            Text = vm.SummaryText,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LibraryCardViewModel.SummaryText))
                summaryText.Text = vm.SummaryText;
        };
        outerStack.Children.Add(summaryText);

        // === Row 3: Edit button ===
        var editButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            Padding = new Thickness(0, 4, 0, 4),
            FontSize = 12,
        };
        var editButtonText = new TextBlock();
        UpdateEditButtonText(editButtonText, vm.IsExpanded);
        editButton.Content = editButtonText;

        // === Expandable section ===
        var expandPanel = new StackPanel
        {
            Spacing = 16,
            Visibility = vm.IsExpanded ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };
        expandPanel.Children.Add(new TextBlock
        {
            Text = "Override your profile's playback defaults for this library. Changes save automatically.",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        editButton.Click += (s, e) =>
        {
            vm.ToggleExpandedCommand.Execute(null);
            expandPanel.Visibility = vm.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            UpdateEditButtonText(editButtonText, vm.IsExpanded);
        };

        outerStack.Children.Add(editButton);

        // --- Spoken language dropdown ---
        expandPanel.Children.Add(BuildDropdownRow(
            "Spoken language", AudioLanguageOptions, vm.AudioLanguage,
            (tag) => vm.AudioLanguage = tag));

        // --- Subtitle language dropdown ---
        expandPanel.Children.Add(BuildDropdownRow(
            "Subtitle language", SubtitleLanguageOptions, vm.SubtitleLanguage,
            (tag) => vm.SubtitleLanguage = tag));

        // --- Subtitle behavior dropdown ---
        expandPanel.Children.Add(BuildDropdownRow(
            "Subtitle behavior", SubtitleModeOptions, vm.SubtitleMode,
            (tag) => vm.SubtitleMode = tag));

        // --- Forced subtitles dropdown ---
        expandPanel.Children.Add(BuildDropdownRow(
            "Forced subtitles", ForcedSubtitleOptions, vm.ForcedSubtitles,
            (tag) => vm.ForcedSubtitles = tag));

        // --- Reset button ---
        var resetButton = new Button
        {
            Content = "Reset to profile defaults",
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
        };
        resetButton.Click += (s, e) => vm.ResetToDefaultsCommand.Execute(null);

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(LibraryCardViewModel.AudioLanguage)
                or nameof(LibraryCardViewModel.SubtitleLanguage)
                or nameof(LibraryCardViewModel.SubtitleMode)
                or nameof(LibraryCardViewModel.ForcedSubtitles))
            {
                SyncExpandPanelCombos(expandPanel, vm);
            }
        };

        expandPanel.Children.Add(resetButton);
        outerStack.Children.Add(expandPanel);

        cardBorder.Child = outerStack;
        return cardBorder;
    }

    private static void UpdateEditButtonText(TextBlock textBlock, bool isExpanded)
    {
        textBlock.Text = isExpanded ? "\u25BC Hide playback overrides" : "\u25B6 Edit playback overrides";
    }

    private StackPanel BuildDropdownRow(string label, (string Tag, string Label)[] options, string currentValue, Action<string> onChanged)
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            FontWeight = FontWeights.Medium,
            FontSize = 12,
        });

        var combo = new ComboBox { Width = 300 };
        foreach (var (tag, lbl) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = lbl, Tag = tag });
        }

        SelectComboBoxByTag(combo, currentValue);

        bool ready = false;
        combo.Loaded += (s, e) => ready = true;

        combo.SelectionChanged += (s, e) =>
        {
            if (!ready) return;
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                onChanged(tag);
            }
        };

        row.Children.Add(combo);
        return row;
    }

    private static void SyncExpandPanelCombos(StackPanel expandPanel, LibraryCardViewModel vm)
    {
        string[] values = [vm.AudioLanguage, vm.SubtitleLanguage, vm.SubtitleMode, vm.ForcedSubtitles];
        int idx = 0;
        foreach (var child in expandPanel.Children)
        {
            if (child is StackPanel rowPanel && idx < values.Length)
            {
                foreach (var rowChild in rowPanel.Children)
                {
                    if (rowChild is ComboBox combo)
                    {
                        SelectComboBoxByTag(combo, values[idx]);
                        break;
                    }
                }
                idx++;
            }
        }
    }

    // ===== ComboBox change handlers =====
    private void QualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (QualityComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.QualityPreference = val;
            _ = ViewModel.SaveQualityPreferenceCommand.ExecuteAsync(null);
        }
    }

    private void MaxBitrateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (MaxBitrateComboBox.SelectedItem is ComboBoxItem item && item.Tag is string value)
        {
            ViewModel.MaxBitrateKbps = value;
            _ = ViewModel.SaveMaxBitrateKbpsCommand.ExecuteAsync(null);
        }
    }

    private void SpokenLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SpokenLanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.AudioLanguage = val;
            _ = ViewModel.SaveAudioLanguageCommand.ExecuteAsync(null);
        }
    }

    private void MetadataLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (MetadataLanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string value)
        {
            ViewModel.PreferredMetadataLanguage = value;
            _ = ViewModel.SavePreferredMetadataLanguageCommand.ExecuteAsync(null);
        }
    }

    private void SubtitleLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitleLanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubtitleLanguage = val;
            _ = ViewModel.SaveSubtitleLanguageCommand.ExecuteAsync(null);
        }
    }

    private void SubtitleModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitleModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubtitleMode = val;
            _ = ViewModel.SaveSubtitleModeCommand.ExecuteAsync(null);
        }
    }

    private void NextUpModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (NextUpModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.NextUpMode = val;
            _ = ViewModel.SaveNextUpModeCommand.ExecuteAsync(null);
        }
    }

    // ===== Subtitle appearance handlers =====

    private void BuildSubtitleColorSwatches()
    {
        BuildSubtitleSwatches(SubtitleColorSwatches, SubtitleColorOptions,
            hex => ViewModel.SubFontColor = hex);
        BuildSubtitleSwatches(SubtitleOutlineColorSwatches, SubtitleColorOptions,
            hex => ViewModel.SubOutlineColor = hex);
        BuildSubtitleSwatches(SubtitleBgColorSwatches, SubtitleBackgroundColorOptions,
            hex => ViewModel.SubBackgroundColor = hex);
    }

    private void BuildSubtitleSwatches(Panel host, (string Hex, string Label)[] options, Action<string> setter)
    {
        host.Children.Clear();
        foreach (var (hex, label) in options)
        {
            var color = ColorFromHex(hex);
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
            };
            var button = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(2),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                Tag = hex,
                Content = swatch,
            };
            AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
            button.Click += (_, _) =>
            {
                setter(hex);
                UpdateSubtitleColorSelection();
                UpdateSubtitlePreview();
            };
            host.Children.Add(button);
        }
    }

    private void UpdateSubtitleColorSelection()
    {
        UpdateSwatchSelection(SubtitleColorSwatches, ViewModel.SubFontColor);
        UpdateSwatchSelection(SubtitleOutlineColorSwatches, ViewModel.SubOutlineColor);
        UpdateSwatchSelection(SubtitleBgColorSwatches, ViewModel.SubBackgroundColor);
    }

    private static void UpdateSwatchSelection(Panel host, string selected)
    {
        foreach (var child in host.Children)
        {
            if (child is Button { Tag: string hex, Content: Border border } button)
            {
                var isSelected = hex.Equals(selected, StringComparison.OrdinalIgnoreCase);
                border.BorderThickness = new Thickness(isSelected ? 3 : 1);
                border.BorderBrush = isSelected
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 255, 255, 255));
                AutomationProperties.SetHelpText(button, isSelected ? "Selected" : "Not selected");
            }
        }
    }

    private void SubtitleFontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitleFontSizeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubFontSize = val;
            UpdateSubtitlePreview();
        }
    }

    private void SubtitleFontFamily_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitleFontFamilyComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubFontFamily = val;
            UpdateSubtitlePreview();
        }
    }

    private void SubtitleBgStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitleBgStyleComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubBackgroundStyle = val;
            UpdateSubtitleDependentControls();
            UpdateSubtitlePreview();
        }
    }

    private void SubtitleOutline_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ViewModel.SubOutlineEnabled = SubtitleOutlineToggle.IsOn;
        UpdateSubtitleDependentControls();
        UpdateSubtitlePreview();
    }

    private void SubtitleBgOpacity_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        // B55: 0-100 integer scale
        ViewModel.SubBackgroundOpacity = (int)Math.Round(SubtitleBgOpacitySlider.Value);
        SubtitleBgOpacityLabel.Text = $"{ViewModel.SubBackgroundOpacity}%";
        UpdateSubtitlePreview();
    }

    private void SubtitlePosition_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SubtitlePositionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ViewModel.SubPosition = val;
            UpdateSubtitlePreview();
        }
    }

    private void UpdateSubtitlePreview()
    {
        // Font size
        double fontSize = ViewModel.SubFontSize switch
        {
            "small" => 13,
            "large" => 20,
            "xlarge" => 26,
            "xxlarge" => 31,
            _ => 16,
        };
        SubtitlePreviewLine1.FontSize = fontSize;
        SubtitlePreviewLine2.FontSize = fontSize;

        // Font color
        var fontColor = ColorFromHex(ViewModel.SubFontColor);
        var fontBrush = new SolidColorBrush(fontColor);
        SubtitlePreviewLine1.Foreground = fontBrush;
        SubtitlePreviewLine2.Foreground = fontBrush;

        // Font family
        if (ViewModel.SubFontFamily == "monospace")
        {
            var ff = new FontFamily("Consolas");
            SubtitlePreviewLine1.FontFamily = ff;
            SubtitlePreviewLine2.FontFamily = ff;
        }
        else if (ViewModel.SubFontFamily == "serif")
        {
            var ff = new FontFamily("Times New Roman");
            SubtitlePreviewLine1.FontFamily = ff;
            SubtitlePreviewLine2.FontFamily = ff;
        }
        else
        {
            var ff = new FontFamily("Segoe UI");
            SubtitlePreviewLine1.FontFamily = ff;
            SubtitlePreviewLine2.FontFamily = ff;
        }

        // Background
        if (ViewModel.SubBackgroundStyle == "box")
        {
            var bgColor = ColorFromHex(ViewModel.SubBackgroundColor);
            // B55: SubBackgroundOpacity is 0-100 integer — convert to 0-255 alpha.
            byte alpha = (byte)Math.Round(ViewModel.SubBackgroundOpacity * 2.55);
            var bgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, bgColor.R, bgColor.G, bgColor.B));
            SubtitlePreviewBg1.Background = bgBrush;
            SubtitlePreviewBg2.Background = bgBrush;
        }
        else
        {
            SubtitlePreviewBg1.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            SubtitlePreviewBg2.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        // Position
        SubtitlePreviewStack.VerticalAlignment = ViewModel.SubPosition switch
        {
            "top" => VerticalAlignment.Top,
            "lower-third" => VerticalAlignment.Center,
            _ => VerticalAlignment.Bottom,
        };
    }

    private void UpdateSubtitleDependentControls()
    {
        var isBox = ViewModel.SubBackgroundStyle == "box";
        SubtitleBgOpacityRow.Opacity = isBox ? 1 : 0.4;
        SubtitleBgColorRow.Opacity = isBox ? 1 : 0.4;
        SubtitleBgOpacitySlider.IsEnabled = isBox;
        SubtitleBgColorSwatches.IsHitTestVisible = isBox;

        var usesOutline = ViewModel.SubOutlineEnabled || ViewModel.SubBackgroundStyle == "outline";
        SubtitleOutlineColorRow.Opacity = usesOutline ? 1 : 0.4;
        SubtitleOutlineColorSwatches.IsHitTestVisible = usesOutline;
    }

    private void SubtitleSave_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.SaveSubtitleAppearanceCommand.ExecuteAsync(null);
    }

    private void SubtitleDiscard_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DiscardSubtitleAppearanceCommand.Execute(null);
        SyncSubtitleAppearanceControls();
    }

    private async void SubtitleReset_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ResetSubtitleAppearanceCommand.ExecuteAsync(null);
        SyncSubtitleAppearanceControls();
    }

    // ===== Home Screen Section Handlers =====

    private void HomeSections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildHomeSectionItems();
    }

    private void RebuildHomeSectionItems()
    {
        HomeSectionItemsContainer.Children.Clear();
        HomeSectionsCountText.Text = $"{ViewModel.HomeSections.Count} {(ViewModel.HomeSections.Count == 1 ? "section" : "sections")}";

        if (ViewModel.HomeSections.Count == 0)
        {
            HomeSectionItemsContainer.Children.Add(new TextBlock
            {
                Text = "No sections configured.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        foreach (var section in ViewModel.HomeSections)
        {
            HomeSectionItemsContainer.Children.Add(BuildHomeSectionRow(section));
        }
    }

    private Border BuildHomeSectionRow(SettingsSectionEntry section)
    {
        var row = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(19),
            Padding = new Thickness(12),
            AllowDrop = ViewModel.CanEditHomeSections,
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var grip = new Button
        {
            Content = new FontIcon { Glyph = "\uE700", FontSize = 14 },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Top,
            CanDrag = ViewModel.CanEditHomeSections,
            IsEnabled = ViewModel.CanEditHomeSections,
        };
        AutomationProperties.SetName(grip, $"Drag {section.Title}");
        grip.DragStarting += (_, args) =>
        {
            args.Data.SetText(section.Id);
            args.Data.RequestedOperation = DataPackageOperation.Move;
        };
        grid.Children.Add(grip);

        var visBtn = new Button
        {
            Content = new FontIcon
            {
                Glyph = section.Hidden ? "\uED1A" : "\uE7B3",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources[section.Hidden ? "SecondaryTextBrush" : "PrimaryTextBrush"],
            },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Top,
            IsEnabled = ViewModel.CanEditHomeSections,
        };
        AutomationProperties.SetName(visBtn, $"{(section.Hidden ? "Show" : "Hide")} {section.Title}");
        visBtn.Click += async (_, _) =>
        {
            ViewModel.ToggleSectionVisibility(section);
            await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
        };
        Grid.SetColumn(visBtn, 1);
        grid.Children.Add(visBtn);

        var info = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 4 };
        titleRow.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = section.Hidden
                ? (Brush)Application.Current.Resources["SecondaryTextBrush"]
                : (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextDecorations = section.Hidden ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
        });
        titleRow.Children.Add(BuildHomeSectionBadge(HomeSectionTypeLabel(section.SectionType), false));
        if (section.Featured) titleRow.Children.Add(BuildHomeSectionBadge("Featured", true));
        titleRow.Children.Add(BuildHomeSectionBadge(section.Hidden ? "Hidden" : "Visible", false));
        info.Children.Add(titleRow);
        info.Children.Add(new TextBlock
        {
            Text = $"{HomeSectionTypeLabel(section.SectionType)} · {section.ItemLimit} items",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        Grid.SetColumn(info, 2);
        grid.Children.Add(info);

        var editBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 13 },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
            IsEnabled = ViewModel.CanEditHomeSections,
        };
        AutomationProperties.SetName(editBtn, $"Edit {section.Title}");
        ToolTipService.SetToolTip(editBtn, "Edit section");
        editBtn.Click += async (_, _) => await EditHomeSectionAsync(section);
        Grid.SetColumn(editBtn, 3);
        grid.Children.Add(editBtn);

        var delBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14, Foreground = (Brush)Application.Current.Resources["ErrorBrush"] },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
            IsEnabled = ViewModel.CanEditHomeSections,
        };
        AutomationProperties.SetName(delBtn, $"Delete {section.Title}");
        delBtn.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = section.IsCustom ? "Delete custom section?" : "Remove section?",
                Content = section.IsCustom ? "Delete this custom section?" : "Remove this section from your home screen?",
                PrimaryButtonText = section.IsCustom ? "Delete" : "Remove",
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            ViewModel.RemoveSection(section);
            await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
        };
        Grid.SetColumn(delBtn, 4);
        grid.Children.Add(delBtn);

        row.Child = grid;
        row.DragOver += (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.Text)) return;
            args.AcceptedOperation = DataPackageOperation.Move;
            args.DragUIOverride.Caption = $"Move before {section.Title}";
        };
        row.Drop += async (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.Text)) return;
            var sourceId = await args.DataView.GetTextAsync();
            var source = ViewModel.HomeSections.FirstOrDefault(candidate => candidate.Id == sourceId);
            if (source is null || ReferenceEquals(source, section)) return;
            var oldIndex = ViewModel.HomeSections.IndexOf(source);
            var newIndex = ViewModel.HomeSections.IndexOf(section);
            if (oldIndex < 0 || newIndex < 0) return;
            ViewModel.HomeSections.Move(oldIndex, newIndex);
            await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
        };
        return row;
    }

    private static Border BuildHomeSectionBadge(string text, bool accent) => new()
    {
        Background = (Brush)Application.Current.Resources[accent ? "AccentBackgroundBrush" : "SurfaceHoverBrush"],
        BorderBrush = (Brush)Application.Current.Resources[accent ? "AccentBrush" : "BorderBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(7, 2, 7, 2),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources[accent ? "AccentBrush" : "SecondaryTextBrush"],
        },
    };

    private static string HomeSectionTypeLabel(string type) => type switch
    {
        "recently_added" => "Recently Added",
        "recently_released" => "Recently Released",
        "genre" => "Genre",
        "custom_filter" => "Custom Filter",
        "random" => "Random",
        "continue_watching" => "Continue Watching",
        "recommended_for_you" => "Recommended For You",
        "because_you_watched" => "Because You Watched",
        "similar_users_liked" => "Profiles Like You Enjoyed",
        "taste_match" => "Top Picks Today",
        "next_up" => "On Deck",
        "next_in_series" => "Next in Series",
        "watchlist" => "Watchlist",
        "favorites" => "Favorites",
        "collection" => "Collection",
        _ => type.Replace('_', ' '),
    };

    private void HomeSectionsSave_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
    }

    private async void HomeSectionsAdd_Click(object sender, RoutedEventArgs e)
    {
        RecipeCatalogResponse catalog;
        try
        {
            catalog = await App.Services.GetRequiredService<SettingsApi>().GetRecipeCatalogAsync();
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error($"Could not load section recipes: {ex.Message}");
            return;
        }

        var selected = await RecipeGalleryDialog.ShowAsync(XamlRoot, catalog);
        if (selected is null)
            return;

        ViewModel.AddHomeSection(new SettingsSectionEntry
        {
            Id = Guid.NewGuid().ToString(),
            SectionType = selected.SectionType,
            Title = selected.Title,
            Featured = selected.Featured,
            ItemLimit = selected.ItemLimit,
            Hidden = false,
            IsCustom = true,
            Customized = true,
            Position = ViewModel.HomeSections.Count,
            Config = selected.Config,
        });
        await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
    }

    private async void HomeSectionsAddCustom_Click(object sender, RoutedEventArgs e)
    {
        RecipeCatalogResponse catalog;
        try
        {
            catalog = await App.Services.GetRequiredService<SettingsApi>().GetRecipeCatalogAsync();
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error($"Could not load section types: {ex.Message}");
            return;
        }
        var configured = await RecipeGalleryDialog.ShowEditorAsync(XamlRoot, catalog, null);
        if (configured is null) return;
        ViewModel.AddHomeSection(new SettingsSectionEntry
        {
            Id = Guid.NewGuid().ToString(),
            SectionType = configured.SectionType,
            Title = configured.Title,
            Featured = configured.Featured,
            ItemLimit = configured.ItemLimit,
            Hidden = false,
            IsCustom = true,
            Customized = true,
            Position = ViewModel.HomeSections.Count,
            Config = configured.Config,
        });
        await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
    }

    private async Task EditHomeSectionAsync(SettingsSectionEntry section)
    {
        RecipeCatalogResponse catalog;
        try
        {
            catalog = await App.Services.GetRequiredService<SettingsApi>().GetRecipeCatalogAsync();
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error($"Could not load section editor: {ex.Message}");
            return;
        }
        var configured = await RecipeGalleryDialog.ShowEditorAsync(XamlRoot, catalog, section);
        if (configured is null) return;
        section.SectionType = configured.SectionType;
        section.Title = configured.Title;
        section.Featured = configured.Featured;
        section.ItemLimit = configured.ItemLimit;
        section.Config = configured.Config;
        section.Customized = true;
        RebuildHomeSectionItems();
        await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
    }

    // B57: Confirm before destructive reset of all section customizations.
    private async void HomeSectionsReset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Reset section customizations",
            Content = "Reset all section customizations to defaults? This action cannot be undone.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ResetHomeSectionsCommand.ExecuteAsync(null);
        }
    }

    // ===================================================================
    // ===== Import Handlers (rebuilt to match WebUI 2026-04-10) =====
    // ===================================================================

    /// <summary>
    /// Styles for the three source cards — highlights the currently-selected one
    /// with an accent border. Called whenever the source type changes.
    /// </summary>
    private void UpdateImportSourceCardStyles()
    {
        var selected = ViewModel.ImportSourceType;
        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        var border = (Brush)Application.Current.Resources["BorderBrush"];
        ImportSourceEmbyCard.BorderBrush = selected == "emby" ? accent : border;
        ImportSourceEmbyCard.BorderThickness = new Thickness(selected == "emby" ? 2 : 1);
        ImportSourceJellyfinCard.BorderBrush = selected == "jellyfin" ? accent : border;
        ImportSourceJellyfinCard.BorderThickness = new Thickness(selected == "jellyfin" ? 2 : 1);
        ImportSourcePlexCard.BorderBrush = selected == "plex" ? accent : border;
        ImportSourcePlexCard.BorderThickness = new Thickness(selected == "plex" ? 2 : 1);
    }

    /// <summary>Clicked on one of the three source cards (Emby/Jellyfin/Plex).</summary>
    private void ImportSourceCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string source) return;
        ViewModel.ImportSourceType = source;
        UpdateImportPanelVisibility();
    }

    /// <summary>Emby sub-mode buttons — Connect / Saved.</summary>
    private void ImportEmbyMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string mode) return;
        ViewModel.ImportEmbyMode = mode;
        UpdateImportPanelVisibility();
    }

    /// <summary>Plex sub-mode buttons — OAuth / Saved.</summary>
    private void ImportPlexMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string mode) return;
        ViewModel.ImportPlexMode = mode;
        UpdateImportPanelVisibility();
    }

    /// <summary>
    /// Syncs visibility of the source-card highlight, mode selectors, and per-mode
    /// auth panels to the current ViewModel source/mode state.
    /// </summary>
    private void UpdateImportPanelVisibility()
    {
        UpdateImportSourceCardStyles();

        var source = ViewModel.ImportSourceType;

        // Mode selectors
        ImportEmbyModeSelector.Visibility = source == "emby" ? Visibility.Visible : Visibility.Collapsed;
        ImportPlexModeSelector.Visibility = source == "plex" ? Visibility.Visible : Visibility.Collapsed;

        // Style Emby mode buttons as accent/secondary based on ImportEmbyMode
        var emodeAccent = (Style)Application.Current.Resources["AccentButtonStyle"];
        var emodeSecondary = (Style)Application.Current.Resources["SecondaryButtonStyle"];
        ImportEmbyConnectModeBtn.Style = ViewModel.ImportEmbyMode == "connect" ? emodeAccent : emodeSecondary;
        ImportEmbySavedModeBtn.Style = ViewModel.ImportEmbyMode == "saved" ? emodeAccent : emodeSecondary;

        // Style Plex mode buttons
        ImportPlexOAuthModeBtn.Style = ViewModel.ImportPlexMode == "oauth" ? emodeAccent : emodeSecondary;
        ImportPlexSavedModeBtn.Style = ViewModel.ImportPlexMode == "saved" ? emodeAccent : emodeSecondary;

        // Auth panels
        ImportEmbyConnectPanel.Visibility = (source == "emby" && ViewModel.ImportEmbyMode == "connect") ? Visibility.Visible : Visibility.Collapsed;
        ImportEmbySavedPanel.Visibility   = (source == "emby" && ViewModel.ImportEmbyMode == "saved")   ? Visibility.Visible : Visibility.Collapsed;
        ImportPlexOAuthPanel.Visibility   = (source == "plex" && ViewModel.ImportPlexMode == "oauth")   ? Visibility.Visible : Visibility.Collapsed;
        ImportPlexSavedPanel.Visibility   = (source == "plex" && ViewModel.ImportPlexMode == "saved")   ? Visibility.Visible : Visibility.Collapsed;
        ImportJellyfinPanel.Visibility    = source == "jellyfin" ? Visibility.Visible : Visibility.Collapsed;

        // Saved-source panels mirror the WebUI's explicit loading and empty states.
        EmbySavedSourcesLoadingText.Visibility = ViewModel.IsLoadingImportSources ? Visibility.Visible : Visibility.Collapsed;
        EmbySavedSourcesEmptyNotice.Visibility = !ViewModel.IsLoadingImportSources && ViewModel.EmbySavedSources.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        EmbySavedSourcesFields.Visibility = !ViewModel.IsLoadingImportSources && ViewModel.EmbySavedSources.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        PlexSavedSourcesLoadingText.Visibility = ViewModel.IsLoadingImportSources ? Visibility.Visible : Visibility.Collapsed;
        PlexSavedSourcesEmptyNotice.Visibility = !ViewModel.IsLoadingImportSources && ViewModel.PlexSavedSources.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        PlexSavedSourcesFields.Visibility = !ViewModel.IsLoadingImportSources && ViewModel.PlexSavedSources.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

        // Plex OAuth sub-states
        PlexAuthPendingPanel.Visibility = ViewModel.PlexAuthPending ? Visibility.Visible : Visibility.Collapsed;
        PlexAuthPendingText.Text = "Finishing Plex sign-in…";
        PlexAuthErrorPanel.Visibility = !string.IsNullOrEmpty(ViewModel.PlexAuthError) ? Visibility.Visible : Visibility.Collapsed;
        PlexAuthErrorText.Text = ViewModel.PlexAuthError ?? "";

        bool plexConnected = ViewModel.PlexOAuthServers.Count > 0 && !ViewModel.PlexAuthPending;
        PlexAuthConnectedPanel.Visibility = plexConnected ? Visibility.Visible : Visibility.Collapsed;
        PlexAuthSignInButton.Visibility = (plexConnected || ViewModel.PlexAuthPending || !string.IsNullOrEmpty(ViewModel.PlexAuthError))
            ? Visibility.Collapsed : Visibility.Visible;

        // Emby Connect connected panel
        bool embyConnected = ViewModel.EmbyConnectServers.Count > 0;
        EmbyConnectConnectedPanel.Visibility = embyConnected ? Visibility.Visible : Visibility.Collapsed;

        // Start button enabled state
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Password box handlers (PasswordBox can't x:Bind directly to VM) -----
    private void EmbyConnectPassword_Changed(object sender, RoutedEventArgs e)
        => ViewModel.EmbyConnectPassword = EmbyConnectPasswordBox.Password;
    private void EmbySavedPassword_Changed(object sender, RoutedEventArgs e)
        => ViewModel.EmbySavedPassword = EmbySavedPasswordBox.Password;
    private void PlexSavedToken_Changed(object sender, RoutedEventArgs e)
        => ViewModel.PlexSavedToken = PlexSavedTokenBox.Password;
    private void JellyfinPassword_Changed(object sender, RoutedEventArgs e)
        => ViewModel.JellyfinPassword = JellyfinPasswordBox.Password;

    // ----- Emby Connect login -----
    private async void EmbyConnectLogin_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.EmbyConnectLoginCommand.ExecuteAsync(null);
        // Populate server combo from VM state.
        EmbyConnectServerCombo.Items.Clear();
        foreach (var server in ViewModel.EmbyConnectServers)
            EmbyConnectServerCombo.Items.Add(new ComboBoxItem { Content = server.Name, Tag = server });
        if (EmbyConnectServerCombo.Items.Count > 0) EmbyConnectServerCombo.SelectedIndex = 0;
        UpdateImportPanelVisibility();
    }

    private void EmbyConnectServer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmbyConnectServerCombo.SelectedItem is ComboBoxItem item && item.Tag is HistoryImportConnectServer srv)
            ViewModel.SelectedEmbyConnectServer = srv;
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Emby Saved -----
    private void EmbySavedSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmbySavedSourceCombo.SelectedItem is ComboBoxItem item && item.Tag is HistoryImportSource src)
            ViewModel.SelectedEmbySavedSource = src;
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Plex OAuth -----
    private async void PlexAuthStart_Click(object sender, RoutedEventArgs e)
    {
        // Show pending panel immediately.
        UpdateImportPanelVisibility();
        await ViewModel.PlexAuthStartCommand.ExecuteAsync(null);
        // Populate server combo and refresh UI.
        PlexOAuthServerCombo.Items.Clear();
        foreach (var s in ViewModel.PlexOAuthServers)
            PlexOAuthServerCombo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s });
        if (PlexOAuthServerCombo.Items.Count > 0) PlexOAuthServerCombo.SelectedIndex = 0;
        UpdateImportPanelVisibility();
    }

    private void PlexOAuthServer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlexOAuthServerCombo.SelectedItem is ComboBoxItem item && item.Tag is PlexServer srv)
            ViewModel.SelectedPlexOAuthServer = srv;
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Plex Saved -----
    private void PlexSavedSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlexSavedSourceCombo.SelectedItem is ComboBoxItem item && item.Tag is HistoryImportSource src)
            ViewModel.SelectedPlexSavedSource = src;
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Profile -----
    private void ImportProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImportProfileCombo.SelectedItem is ComboBoxItem item && item.Tag is string profileId)
            ViewModel.ImportProfileId = profileId;
        StartImportButton.IsEnabled = ViewModel.CanStartImport && !ViewModel.IsImporting;
    }

    // ----- Start import -----
    private async void StartImport_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartImportCommand.ExecuteAsync(null);
        UpdateImportPanelVisibility();
    }

    // ----- Saved sources + profiles combo population -----
    private void RebuildImportSourcesCombos()
    {
        // Emby saved sources
        EmbySavedSourceCombo.Items.Clear();
        foreach (var s in ViewModel.EmbySavedSources)
            EmbySavedSourceCombo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s });
        if (EmbySavedSourceCombo.Items.Count > 0 && EmbySavedSourceCombo.SelectedIndex < 0)
            EmbySavedSourceCombo.SelectedIndex = 0;

        // Plex saved sources
        PlexSavedSourceCombo.Items.Clear();
        foreach (var s in ViewModel.PlexSavedSources)
            PlexSavedSourceCombo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s });
        if (PlexSavedSourceCombo.Items.Count > 0 && PlexSavedSourceCombo.SelectedIndex < 0)
            PlexSavedSourceCombo.SelectedIndex = 0;
    }

    private void RebuildImportProfilesCombo()
    {
        ImportProfileCombo.Items.Clear();
        foreach (var p in ViewModel.ImportProfiles)
            ImportProfileCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        // Preselect the VM's current profile id.
        for (int i = 0; i < ImportProfileCombo.Items.Count; i++)
        {
            if (ImportProfileCombo.Items[i] is ComboBoxItem item && (item.Tag as string) == ViewModel.ImportProfileId)
            {
                ImportProfileCombo.SelectedIndex = i;
                break;
            }
        }
        if (ImportProfileCombo.SelectedIndex < 0 && ImportProfileCombo.Items.Count > 0)
            ImportProfileCombo.SelectedIndex = 0;
    }

    // ----- Run history list -----
    private void ImportRuns_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildImportRunCards();
    }

    private void RebuildImportRunCards()
    {
        ImportRunsContainer.Children.Clear();

        if (ViewModel.ImportRuns.Count == 0)
        {
            ImportRunsContainer.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Child = new TextBlock
                {
                    Text = "No imports have been started yet.",
                    FontSize = 13,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                },
            });
            return;
        }

        foreach (var run in ViewModel.ImportRuns)
        {
            ImportRunsContainer.Children.Add(BuildHistoryRunCard(run));
        }
    }

    /// <summary>Build one card for the Import history list. Click selects the run
    /// for display in the summary card above.</summary>
    private Button BuildHistoryRunCard(HistoryImportRun run)
    {
        var isActive = ViewModel.SelectedRunId == run.Id;

        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources[isActive ? "AccentBrush" : "BorderBrush"],
            BorderThickness = new Thickness(isActive ? 2 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
        };

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Source letter icon
        var (iconLetter, iconBg, iconFg) = run.SourceType switch
        {
            "emby"     => ("E", "#1DCFA1", Microsoft.UI.Colors.White),
            "jellyfin" => ("J", "#00A4DC", Microsoft.UI.Colors.White),
            "plex"     => ("P", "#E5A00D", Microsoft.UI.Colors.Black),
            _          => ("?", "#6B7280", Microsoft.UI.Colors.White),
        };
        var iconBorder = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(HexToColor(iconBg)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = iconLetter,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(iconFg),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(iconBorder, 0);
        row.Children.Add(iconBorder);

        // Title + meta
        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = $"{CapitalizeSource(run.SourceType)} import",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        var metaBits = new List<string> { FormatRelativeTime(run.CreatedAt) };
        if (run.Matched > 0) metaBits.Add($"{run.Matched} matched");
        textStack.Children.Add(new TextBlock
        {
            Text = string.Join("  \u00b7  ", metaBits),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        // Status badge
        var badge = BuildRunStatusBadge(run.Status);
        Grid.SetColumn(badge, 2);
        row.Children.Add(badge);

        card.Child = row;
        var button = new Button
        {
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = card,
        };
        AutomationProperties.SetName(button,
            $"Open {CapitalizeSource(run.SourceType)} import from {FormatRelativeTime(run.CreatedAt)}, {run.Status}");
        button.Click += (_, _) =>
        {
            ViewModel.SelectRunForDisplay(run);
            RebuildRunSummaryCard();
            RebuildImportRunCards(); // re-render to highlight active
        };
        return button;
    }

    private Border BuildRunStatusBadge(string status)
    {
        var (label, glyph, fg, bg) = status switch
        {
            "queued"    => ("Queued",    "\uE916", "#78AEFC", "#143056"),
            "running"   => ("Running",   "\uE895", "#FBBF24", "#3B2A0E"),
            "completed" => ("Completed", "\uE73E", "#4ADE80", "#0E2E18"),
            "failed"    => ("Failed",    "\uE711", "#F87171", "#3A1313"),
            "cancelled" => ("Cancelled", "\uE7A7", "#9CA3AF", "#1F2126"),
            _           => (status,      "\uE916", "#9CA3AF", "#1F2126"),
        };
        var badge = new Border
        {
            Background = new SolidColorBrush(HexToColor(bg)),
            BorderBrush = new SolidColorBrush(HexToColor(fg) with { A = 0x55 }),
            BorderThickness = new Thickness(1),
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 11, Foreground = new SolidColorBrush(HexToColor(fg)) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(HexToColor(fg)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        badge.Child = row;
        return badge;
    }

    // ----- Run summary card -----
    private void RebuildRunSummaryCard()
    {
        RunSummaryContainer.Children.Clear();
        var run = ViewModel.DisplayRun;

        // Title/subtitle state
        RunSummaryTitle.Text = ViewModel.SelectedRunId != null ? "Selected import" : "Latest import";
        RunSummarySubtitle.Text = ViewModel.SelectedRunId != null
            ? "Details from the selected import run."
            : "Results from the most recent import run.";

        if (run == null)
        {
            RunSummaryContainer.Children.Add(new TextBlock
            {
                Text = "Import summaries will appear here after you start a run.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            return;
        }

        // Header: source + status
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock
        {
            Text = $"{CapitalizeSource(run.SourceType)} import",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(BuildRunStatusBadge(run.Status));
        RunSummaryContainer.Children.Add(header);

        var relative = FormatRelativeTime(run.CreatedAt);
        if (!string.IsNullOrEmpty(relative))
        {
            RunSummaryContainer.Children.Add(new TextBlock
            {
                Text = relative,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }

        // Progress bar (when running and fetched > 0)
        bool isActive = run.Status == "running" || run.Status == "queued";
        if (isActive && run.Fetched > 0)
        {
            var progressValue = 100.0 * (run.Matched + run.Unmatched + run.Skipped) / Math.Max(1, run.Fetched);
            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(progressValue, 0, 100),
                Height = 6,
                Foreground = new SolidColorBrush(HexToColor("#FBBF24")),
                Background = new SolidColorBrush(HexToColor("#1F2937")),
            };
            RunSummaryContainer.Children.Add(progressBar);
            RunSummaryContainer.Children.Add(new TextBlock
            {
                Text = $"{run.Matched + run.Unmatched + run.Skipped} / {run.Fetched} processed",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }

        // Current WebUI uses seven columns at desktop widths. The eighth
        // (Skipped) metric wraps to the next row exactly as its responsive
        // grid does, while preserving all additive import counters.
        var metricsGrid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (int i = 0; i < 7; i++) metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void AddMetric(int index, string label, int value, string? accentColor = null)
        {
            var box = new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
            };
            var sp = new StackPanel { Spacing = 2 };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            sp.Children.Add(new TextBlock
            {
                Text = value.ToString("N0"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = (accentColor != null && value > 0)
                    ? new SolidColorBrush(HexToColor(accentColor))
                    : (Brush)Application.Current.Resources["PrimaryTextBrush"],
            });
            box.Child = sp;
            Grid.SetColumn(box, index % 7);
            Grid.SetRow(box, index / 7);
            metricsGrid.Children.Add(box);
        }
        AddMetric(0, "Fetched",  run.Fetched);
        AddMetric(1, "Matched",  run.Matched,         "#4ADE80");
        AddMetric(2, "Unmatched",run.Unmatched,       "#FBBF24");
        AddMetric(3, "Progress", run.ProgressUpdated, "#4ADE80");
        AddMetric(4, "History",  run.HistoryCreated,  "#4ADE80");
        AddMetric(5, "Watchlist", run.WatchlistAdded,   "#4ADE80");
        AddMetric(6, "Favorites", run.FavoritesImported, "#4ADE80");
        AddMetric(7, "Skipped",   run.Skipped);
        RunSummaryContainer.Children.Add(metricsGrid);

        // Error box
        if (!string.IsNullOrEmpty(run.ErrorMessage))
        {
            var errBorder = new Border
            {
                Background = new SolidColorBrush(HexToColor("#1A0E0E")),
                BorderBrush = new SolidColorBrush(HexToColor("#3A1313")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
            };
            var errRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            errRow.Children.Add(new FontIcon { Glyph = "\uE711", FontSize = 14, Foreground = new SolidColorBrush(HexToColor("#F87171")) });
            errRow.Children.Add(new TextBlock
            {
                Text = run.ErrorMessage,
                FontSize = 13,
                Foreground = new SolidColorBrush(HexToColor("#F87171")),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 540,
            });
            errBorder.Child = errRow;
            RunSummaryContainer.Children.Add(errBorder);
        }

        // Warnings
        if (run.Warnings != null && run.Warnings.Count > 0)
        {
            RunSummaryContainer.Children.Add(new TextBlock
            {
                Text = "Warnings",
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                Margin = new Thickness(0, 6, 0, 0),
            });
            foreach (var w in run.Warnings)
            {
                var wRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                wRow.Children.Add(new FontIcon { Glyph = "\uE7BA", FontSize = 12, Foreground = new SolidColorBrush(HexToColor("#FBBF24")), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
                wRow.Children.Add(new TextBlock
                {
                    Text = w,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 540,
                });
                RunSummaryContainer.Children.Add(wRow);
            }
        }

        // Unmatched samples
        if (run.UnmatchedSamples != null && run.UnmatchedSamples.Count > 0)
        {
            RunSummaryContainer.Children.Add(new TextBlock
            {
                Text = "Unmatched examples",
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                Margin = new Thickness(0, 6, 0, 0),
            });
            foreach (var s in run.UnmatchedSamples)
            {
                var card = new Border
                {
                    Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                };
                var sp = new StackPanel { Spacing = 2 };
                var titleText = s.Year > 0 ? $"{s.Title} ({s.Year})" : s.Title;
                sp.Children.Add(new TextBlock
                {
                    Text = titleText,
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                });
                sp.Children.Add(new TextBlock
                {
                    Text = $"{s.Kind} · {s.Reason}",
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                card.Child = sp;
                RunSummaryContainer.Children.Add(card);
            }
        }
    }

    // ----- Event channel wiring -----
    private Core.Services.EventChannelClient? _importEventsClient;
    private bool _importTabInitialized;

    private async Task EnsureImportTabLoadedAsync()
    {
        if (_importTabInitialized) return;
        _importTabInitialized = true;

        var loadTask = ViewModel.LoadImportTabCommand.ExecuteAsync(null);
        UpdateImportPanelVisibility();
        await loadTask;
        RebuildImportSourcesCombos();
        RebuildImportProfilesCombo();
        RebuildRunSummaryCard();
        RebuildImportRunCards();
        UpdateImportPanelVisibility();

        // Start the event channel subscription for live run updates.
        StartImportEventSubscription();
    }

    private void StartImportEventSubscription()
    {
        try
        {
            _importEventsClient ??= App.Services.GetRequiredService<Core.Services.EventChannelClient>();
            _importEventsClient.SnapshotReceived -= OnImportEventSnapshot;
            _importEventsClient.EventReceived -= OnImportEventFrame;
            _importEventsClient.SnapshotReceived += OnImportEventSnapshot;
            _importEventsClient.EventReceived += OnImportEventFrame;
            _importEventsClient.Start("history_import");
        }
        catch { /* Best-effort — polling fallback is via LoadImportRunsAsync on entry */ }
    }

    private void StopImportEventSubscription()
    {
        if (_importEventsClient == null) return;
        _importEventsClient.SnapshotReceived -= OnImportEventSnapshot;
        _importEventsClient.EventReceived -= OnImportEventFrame;
        _importEventsClient.Stop();
    }

    private void OnImportEventSnapshot(string channel, System.Text.Json.JsonElement data)
    {
        if (channel != "history_import" || data.ValueKind != System.Text.Json.JsonValueKind.Array) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var element in data.EnumerateArray())
            {
                var run = DeserializeHistoryImportRun(element);
                if (run != null) ViewModel.ApplyImportRunUpdate(run);
            }
            RebuildRunSummaryCard();
            RebuildImportRunCards();
        });
    }

    private void OnImportEventFrame(string channel, string eventName, System.Text.Json.JsonElement data)
    {
        if (channel != "history_import") return;
        DispatcherQueue.TryEnqueue(() =>
        {
            var run = DeserializeHistoryImportRun(data);
            if (run != null) ViewModel.ApplyImportRunUpdate(run);
            RebuildRunSummaryCard();
            RebuildImportRunCards();
        });
    }

    private static HistoryImportRun? DeserializeHistoryImportRun(System.Text.Json.JsonElement el)
    {
        if (el.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        try
        {
            // Reuse the API client's snake-case options.
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            };
            return System.Text.Json.JsonSerializer.Deserialize<HistoryImportRun>(el.GetRawText(), options);
        }
        catch
        {
            return null;
        }
    }

    // ----- Helpers -----
    private static string CapitalizeSource(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static Windows.UI.Color HexToColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        uint value = Convert.ToUInt32(hex, 16);
        return Windows.UI.Color.FromArgb(
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }

    private static string FormatRelativeTime(string iso)
    {
        if (!DateTime.TryParse(iso, out var dt)) return "";
        var diff = DateTimeOffset.UtcNow - dt.ToUniversalTime();
        if (diff.TotalSeconds < 60) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return DateTimeDisplay.FormatDate(dt, medium: true);
    }

    // ===== Plugin Settings Handlers =====

    private void PluginSettings_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildPluginCards();
    }

    private void RebuildPluginCards()
    {
        PluginCardsContainer.Children.Clear();

        if (ViewModel.PluginSettingsList.Count == 0)
        {
            PluginCardsContainer.Children.Add(new TextBlock
            {
                Text = "No plugins with user settings installed.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            return;
        }

        foreach (var plugin in ViewModel.PluginSettingsList)
        {
            PluginCardsContainer.Children.Add(BuildPluginCard(plugin));
        }
    }

    private Border BuildPluginCard(PluginSettingsSummary plugin)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(24, 20, 24, 20),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
        };

        var stack = new StackPanel { Spacing = 16 };

        // Header: plugin ID + version
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headerRow.Children.Add(new TextBlock
        {
            Text = plugin.PluginId,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        headerRow.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"v{plugin.Version}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            },
        });
        stack.Children.Add(headerRow);

        // Get current values for this plugin
        ViewModel.PluginSettingsValues.TryGetValue(plugin.Id, out var currentValues);
        currentValues ??= new Dictionary<string, string>();

        // Dynamic form fields based on UserConfigSchema
        foreach (var schema in plugin.UserConfigSchema)
        {
            if (schema.AdminForm?.Fields == null) continue;

            foreach (var field in schema.AdminForm.Fields)
            {
                var fieldStack = new StackPanel { Spacing = 4 };

                // Label
                fieldStack.Children.Add(new TextBlock
                {
                    Text = field.Label,
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                });

                if (!string.IsNullOrEmpty(field.Description))
                {
                    fieldStack.Children.Add(new TextBlock
                    {
                        Text = field.Description,
                        FontSize = 12,
                        Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                        TextWrapping = TextWrapping.Wrap,
                    });
                }

                currentValues.TryGetValue(field.Key, out var currentVal);

                switch (field.Control.ToUpperInvariant())
                {
                    case "SELECT" when field.Options != null:
                    {
                        var combo = new ComboBox { Width = 300 };
                        foreach (var opt in field.Options)
                        {
                            var comboItem = new ComboBoxItem { Content = opt.Label, Tag = opt.Value };
                            combo.Items.Add(comboItem);
                            if (opt.Value == currentVal)
                                combo.SelectedItem = comboItem;
                        }
                        var capturedKey = field.Key;
                        var capturedId = plugin.Id;
                        combo.SelectionChanged += (_, _) =>
                        {
                            if (combo.SelectedItem is ComboBoxItem selItem && selItem.Tag is string val)
                            {
                                if (ViewModel.PluginSettingsValues.TryGetValue(capturedId, out var vals))
                                    vals[capturedKey] = val;
                            }
                        };
                        fieldStack.Children.Add(combo);
                        break;
                    }
                    case "TOGGLE":
                    {
                        var toggle = new ToggleSwitch
                        {
                            IsOn = currentVal == "true",
                            OnContent = "",
                            OffContent = "",
                        };
                        var capturedKey = field.Key;
                        var capturedId = plugin.Id;
                        toggle.Toggled += (_, _) =>
                        {
                            if (ViewModel.PluginSettingsValues.TryGetValue(capturedId, out var vals))
                                vals[capturedKey] = toggle.IsOn ? "true" : "false";
                        };
                        fieldStack.Children.Add(toggle);
                        break;
                    }
                    default: // TEXT, TEXTAREA
                    {
                        var textBox = new TextBox
                        {
                            Text = currentVal ?? "",
                            PlaceholderText = field.Placeholder ?? "",
                            Width = 360,
                            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
                        };
                        if (field.Secret)
                        {
                            // Use PasswordBox-like styling
                        }
                        if (field.Multiline)
                        {
                            textBox.AcceptsReturn = true;
                            textBox.TextWrapping = TextWrapping.Wrap;
                            textBox.MinHeight = 80;
                        }
                        var capturedKey = field.Key;
                        var capturedId = plugin.Id;
                        textBox.TextChanged += (_, _) =>
                        {
                            if (ViewModel.PluginSettingsValues.TryGetValue(capturedId, out var vals))
                                vals[capturedKey] = textBox.Text;
                        };
                        fieldStack.Children.Add(textBox);
                        break;
                    }
                }

                stack.Children.Add(fieldStack);
            }
        }

        // Save button
        var saveBtn = new Button
        {
            Content = "Save",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Padding = new Thickness(16, 8, 16, 8),
        };
        var pluginId = plugin.Id;
        saveBtn.Click += async (_, _) =>
        {
            await ViewModel.SavePluginSettingsCommand.ExecuteAsync(pluginId);
        };
        stack.Children.Add(saveBtn);

        card.Child = stack;
        return card;
    }

    // ===== Sessions =====
    private async Task LoadSessionsAsync()
    {
        await ViewModel.LoadSessionsCommand.ExecuteAsync(null);
        RebuildSessionCards();
    }

    private void RebuildSessionCards()
    {
        SessionCardsContainer.Children.Clear();

        if (ViewModel.Sessions.Count == 0)
        {
            SessionCardsContainer.Children.Add(new TextBlock
            {
                Text = "No active sessions found.",
                Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        foreach (var session in ViewModel.Sessions)
        {
            SessionCardsContainer.Children.Add(BuildSessionCard(session));
        }
    }

    private Border BuildSessionCard(SiloPlayer.Core.Models.Auth.AuthSession session)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = session.IsCurrent
                ? (Brush)Application.Current.Resources["AccentBrush"]
                : (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: session info
        var infoStack = new StackPanel { Spacing = 4 };

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(new FontIcon
        {
            Glyph = "\uE7F7",
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        nameRow.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(session.DeviceName) ? "Unknown Device" : session.DeviceName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (session.IsCurrent)
        {
            var currentBadge = new Border
            {
                Background = (Brush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            currentBadge.Child = new TextBlock
            {
                Text = "Current",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentForegroundBrush"],
            };
            nameRow.Children.Add(currentBadge);
        }

        infoStack.Children.Add(nameRow);

        // IP and date
        var detailsText = $"IP: {session.IpAddress}";
        if (!string.IsNullOrEmpty(session.CreatedAt))
        {
            if (DateTime.TryParse(session.CreatedAt, out var created))
                detailsText += $"  |  Created: {created.ToLocalTime():g}";
            else
                detailsText += $"  |  Created: {session.CreatedAt}";
        }

        infoStack.Children.Add(new TextBlock
        {
            Text = detailsText,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });

        Grid.SetColumn(infoStack, 0);
        grid.Children.Add(infoStack);

        // Right: revoke button (not for current session)
        if (!session.IsCurrent)
        {
            var revokeButton = new Button
            {
                Content = "Revoke",
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            revokeButton.Click += async (_, _) =>
            {
                await ViewModel.RevokeSessionCommand.ExecuteAsync(session.Id);
                RebuildSessionCards();
            };
            Grid.SetColumn(revokeButton, 1);
            grid.Children.Add(revokeButton);
        }

        card.Child = grid;
        return card;
    }

    // ===== Helper =====
    private static Windows.UI.Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 0xFF;
        byte r, g, b;

        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex[..2], 16);
            r = Convert.ToByte(hex[2..4], 16);
            g = Convert.ToByte(hex[4..6], 16);
            b = Convert.ToByte(hex[6..8], 16);
        }
        else if (hex.Length == 6)
        {
            r = Convert.ToByte(hex[..2], 16);
            g = Convert.ToByte(hex[2..4], 16);
            b = Convert.ToByte(hex[4..6], 16);
        }
        else
        {
            r = g = b = 0;
        }

        return Windows.UI.Color.FromArgb(a, r, g, b);
    }

    // ===== Watch Providers Tab =====

    private async Task LoadWatchProvidersAsync()
    {
        await ViewModel.LoadWatchProvidersAsync();
        RebuildWatchProviderCards();
    }

    private void WatchProviderCards_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildWatchProviderCards();
    }

    private void RebuildWatchProviderCards()
    {
        WatchProvidersCardsPanel.Children.Clear();

        var hasProviders = ViewModel.WatchProviderCards.Count > 0;
        NoWatchProvidersText.Visibility = hasProviders ? Visibility.Collapsed : Visibility.Visible;
        if (!hasProviders) return;

        foreach (var card in ViewModel.WatchProviderCards)
            WatchProvidersCardsPanel.Children.Add(BuildWatchProviderCard(card));
    }

    private Border BuildWatchProviderCard(WatchProviderCardViewModel vm)
    {
        var connection = vm.Connection;
        var latestRun = vm.LatestRun;
        var displayName = vm.DisplayName;
        var showAuth = vm.AuthSession != null && !vm.Connected;
        var showApiKey = vm.ApiKeyPromptVisible && vm.UsesApiKey && !vm.Connected;
        var hasError = !string.IsNullOrWhiteSpace(latestRun?.Error) || !string.IsNullOrWhiteSpace(connection?.LastError);
        var statusText = showAuth ? "Activation pending" : showApiKey ? "API key required" : vm.Connected ? hasError ? "Sync error" : "Connected" : "Not connected";
        var statusKind = showAuth || showApiKey ? "pending" : vm.Connected ? hasError ? "error" : "connected" : "muted";

        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 18, 20, 18),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
        };

        var outer = new StackPanel { Spacing = 16 };

        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Spacing = 4 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = displayName,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        titleRow.Children.Add(BuildWatchProviderStatusPill(statusText, statusKind));
        titleStack.Children.Add(titleRow);
        titleStack.Children.Add(new TextBlock
        {
            Text = GetWatchProviderSubtitle(vm),
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(titleStack, 0);
        header.Children.Add(titleStack);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        if (showAuth || showApiKey)
        {
            // The active auth block owns its close action, matching the WebUI.
        }
        else if (vm.Connected)
        {
            var syncButton = new Button
            {
                Content = IsWatchProviderRunActive(latestRun) ? "Syncing..." : "Sync now",
                Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
                Padding = new Thickness(12, 7, 12, 7),
                FontSize = 13,
                IsEnabled = !vm.IsBusy && !IsWatchProviderRunActive(latestRun),
            };
            syncButton.Click += async (_, _) =>
            {
                await ViewModel.TriggerWatchProviderSyncAsync(vm);
                RebuildWatchProviderCards();
            };
            actions.Children.Add(syncButton);

            var disconnectButton = new Button
            {
                Content = "Disconnect",
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                Padding = new Thickness(12, 7, 12, 7),
                FontSize = 13,
                IsEnabled = !vm.IsBusy,
            };
            disconnectButton.Click += async (_, _) =>
            {
                var dialog = new ContentDialog
                {
                    Title = $"Disconnect {displayName}?",
                    PrimaryButtonText = "Disconnect",
                    PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                    CloseButtonText = "Cancel",
                    XamlRoot = XamlRoot,
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await ViewModel.DeleteWatchProviderConnectionAsync(vm);
                    RebuildWatchProviderCards();
                }
            };
            actions.Children.Add(disconnectButton);
        }
        else
        {
            var connect = new Button
            {
                Content = vm.CredentialsConfigured ? "Connect" : "Credentials required",
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                Padding = new Thickness(12, 7, 12, 7),
                FontSize = 13,
                IsEnabled = vm.CredentialsConfigured && !vm.IsBusy,
            };
            connect.Click += async (_, _) =>
            {
                if (vm.UsesApiKey)
                {
                    vm.ApiKeyPromptVisible = true;
                }
                else
                {
                    await ViewModel.StartWatchProviderAuthAsync(vm);
                }
                RebuildWatchProviderCards();
            };
            actions.Children.Add(connect);
        }
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        outer.Children.Add(header);

        if (showAuth && vm.AuthSession != null)
            outer.Children.Add(BuildWatchProviderAuthPanel(vm));

        if (showApiKey)
            outer.Children.Add(BuildWatchProviderApiKeyPanel(vm));

        if (vm.Connected && connection != null)
        {
            outer.Children.Add(BuildWatchProviderStats(connection, latestRun));
            var toggles = new StackPanel { Spacing = 8 };
            toggles.Children.Add(BuildWatchProviderToggle(vm, "import_watched_enabled", "Import watched history", $"Bring completed {displayName} plays into this profile.", connection.ImportWatchedEnabled));
            toggles.Children.Add(BuildWatchProviderToggle(vm, "import_progress_enabled", "Import paused progress", $"Use newer {displayName} resume points when local progress is older.", connection.ImportProgressEnabled));
            toggles.Children.Add(BuildWatchProviderToggle(vm, "export_watched_enabled", "Send watched changes", "Send local watched marks and completed plays to this provider.", connection.ExportWatchedEnabled));
            toggles.Children.Add(BuildWatchProviderToggle(vm, "export_unwatched_enabled", "Send unwatched changes", "When you mark something unwatched, remove matching history from this provider.", connection.ExportUnwatchedEnabled));

            var favoritesSupported = connection.Capabilities.ImportFavorites || connection.Capabilities.ExportFavorites;
            if (favoritesSupported)
            {
                var favoritesEnabled = connection.ImportFavoritesEnabled || connection.ExportFavoritesEnabled;
                toggles.Children.Add(BuildWatchProviderToggle(vm, "favorites_sync", "Sync favorites", $"Import {displayName} favorites and send local favorite adds.", favoritesEnabled, async isOn =>
                {
                    await ViewModel.UpdateWatchProviderConnectionAsync(vm, new Dictionary<string, object?>
                    {
                        ["import_favorites_enabled"] = isOn,
                        ["export_favorites_enabled"] = isOn,
                        ["sync_favorite_removals_enabled"] = isOn ? connection.SyncFavoriteRemovalsEnabled : false,
                    });
                }));
            }

            if (connection.Capabilities.RemoveFavorites)
            {
                var favoritesEnabled = connection.ImportFavoritesEnabled || connection.ExportFavoritesEnabled;
                toggles.Children.Add(BuildWatchProviderToggle(vm, "sync_favorite_removals_enabled", "Sync favorite removals", "Remove provider-synced favorites on the other side when explicitly unfavorited.", connection.SyncFavoriteRemovalsEnabled, isEnabled: favoritesEnabled));
            }

            if (connection.Capabilities.ScrobblePlayback)
                toggles.Children.Add(BuildWatchProviderToggle(vm, "scrobble_enabled", "Scrobble playback", "Report starts, pauses, resumes, and stops live during playback.", connection.ScrobbleEnabled));

            outer.Children.Add(toggles);
        }

        card.Child = outer;
        return card;
    }

    private Border BuildWatchProviderAuthPanel(WatchProviderCardViewModel vm)
    {
        var session = vm.AuthSession!;
        var panel = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            Opacity = 0.95,
        };

        var stack = new StackPanel { Spacing = 12 };
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headingCopy = new StackPanel { Spacing = 2 };
        headingCopy.Children.Add(new TextBlock
        {
            Text = "Copy this code first",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        headingCopy.Children.Add(new TextBlock
        {
            Text = $"{vm.DisplayName} will ask for it after the activation page opens.",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        heading.Children.Add(headingCopy);
        var cancel = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 13 },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
            IsEnabled = !vm.IsBusy,
        };
        AutomationProperties.SetName(cancel, "Cancel activation");
        cancel.Click += (_, _) =>
        {
            vm.AuthSession = null;
            RebuildWatchProviderCards();
        };
        Grid.SetColumn(cancel, 1);
        heading.Children.Add(cancel);
        stack.Children.Add(heading);

        var codeRow = new Grid { ColumnSpacing = 8 };
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var codeBox = new Border
        {
            Background = (Brush)Application.Current.Resources["AppBackgroundBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = session.UserCode,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                CharacterSpacing = 180,
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        codeRow.Children.Add(codeBox);

        var copy = new Button
        {
            Content = "Copy code",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 13,
        };
        copy.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(session.UserCode);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        };
        Grid.SetColumn(copy, 1);
        codeRow.Children.Add(copy);
        stack.Children.Add(codeRow);

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var open = new Button
        {
            Content = $"Open {vm.DisplayName} activation",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 13,
            IsEnabled = !string.IsNullOrWhiteSpace(session.VerificationUrl),
        };
        open.Click += (_, _) =>
        {
            var toast = App.Services.GetRequiredService<ToastService>();
            if (!ExternalBrowserUrlPolicy.TryGetSafeUri(session.VerificationUrl, out var verificationUri))
            {
                toast.Error("The server returned an invalid activation URL.");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(verificationUri.AbsoluteUri)
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                toast.Error($"Could not open the activation page: {ex.Message}");
            }
        };
        actionRow.Children.Add(open);

        var poll = new Button
        {
            Content = "I've entered it",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 13,
            IsEnabled = !vm.IsBusy,
        };
        poll.Click += async (_, _) =>
        {
            await ViewModel.PollWatchProviderAuthAsync(vm);
            RebuildWatchProviderCards();
        };
        actionRow.Children.Add(poll);
        stack.Children.Add(actionRow);

        panel.Child = stack;
        return panel;
    }

    private Border BuildWatchProviderApiKeyPanel(WatchProviderCardViewModel vm)
    {
        var panel = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            Opacity = 0.95,
        };

        var stack = new StackPanel { Spacing = 12 };
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headingCopy = new StackPanel { Spacing = 2 };
        headingCopy.Children.Add(new TextBlock
        {
            Text = $"Paste your {vm.DisplayName} API key",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        headingCopy.Children.Add(new TextBlock
        {
            Text = $"Find it under your account settings on the {vm.DisplayName} site.",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        heading.Children.Add(headingCopy);
        var cancel = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 13 },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
            IsEnabled = !vm.IsBusy,
        };
        AutomationProperties.SetName(cancel, "Cancel connection");
        cancel.Click += (_, _) =>
        {
            vm.ApiKey = "";
            vm.ApiKeyPromptVisible = false;
            RebuildWatchProviderCards();
        };
        Grid.SetColumn(cancel, 1);
        heading.Children.Add(cancel);
        stack.Children.Add(heading);

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var keyBox = new PasswordBox
        {
            PlaceholderText = "API key",
            Password = vm.ApiKey,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            MinWidth = 260,
        };
        row.Children.Add(keyBox);

        var connect = new Button
        {
            Content = vm.IsBusy ? "Connecting..." : "Connect",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 13,
            IsEnabled = !vm.IsBusy && !string.IsNullOrWhiteSpace(vm.ApiKey),
        };
        keyBox.PasswordChanged += (_, _) =>
        {
            vm.ApiKey = keyBox.Password;
            connect.IsEnabled = !vm.IsBusy && !string.IsNullOrWhiteSpace(keyBox.Password);
        };
        connect.Click += async (_, _) =>
        {
            vm.ApiKey = keyBox.Password;
            await ViewModel.ConnectWatchProviderApiKeyAsync(vm);
            RebuildWatchProviderCards();
        };
        Grid.SetColumn(connect, 1);
        row.Children.Add(connect);

        stack.Children.Add(row);
        panel.Child = stack;
        return panel;
    }

    private Border BuildWatchProviderStats(WatchProviderConnection connection, WatchProviderSyncRun? latestRun)
    {
        var shell = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
        };
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddWatchProviderStat(grid, 0, "Last sync", FormatWatchProviderLastSync(connection, latestRun));
        AddWatchProviderStat(grid, 1, "Watched", $"{latestRun?.InboundWatchedImported ?? 0:N0} imported");
        AddWatchProviderStat(grid, 2, "Progress", $"{latestRun?.InboundProgressImported ?? 0:N0} resumed");
        AddWatchProviderStat(grid, 3, "Exported", $"{((latestRun?.OutboundSent ?? 0) + (latestRun?.OutboundFavoritesSent ?? 0)):N0} sent");
        shell.Child = grid;
        return shell;
    }

    private static void AddWatchProviderStat(Grid grid, int column, string label, string value)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }

    private Grid BuildWatchProviderToggle(
        WatchProviderCardViewModel vm,
        string field,
        string label,
        string description,
        bool isChecked,
        Func<bool, Task>? customSave = null,
        bool isEnabled = true)
    {
        var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        row.Children.Add(text);

        var toggle = new ToggleSwitch
        {
            IsOn = isChecked,
            OnContent = "",
            OffContent = "",
            MinWidth = 64,
            IsEnabled = isEnabled && !vm.IsBusy,
        };
        toggle.Toggled += async (_, _) =>
        {
            if (customSave != null)
                await customSave(toggle.IsOn);
            else
                await ViewModel.UpdateWatchProviderConnectionAsync(vm, field, toggle.IsOn);

            RebuildWatchProviderCards();
        };
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    private Border BuildWatchProviderStatusPill(string text, string kind)
    {
        var color = kind switch
        {
            "connected" => Windows.UI.Color.FromArgb(0xFF, 52, 211, 153),
            "pending" => Windows.UI.Color.FromArgb(0xFF, 251, 191, 36),
            "error" => Windows.UI.Color.FromArgb(0xFF, 248, 113, 113),
            _ => Windows.UI.Color.FromArgb(0xFF, 148, 163, 184),
        };

        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(9, 3, 9, 3),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
            },
        };
    }

    private static bool IsWatchProviderRunActive(WatchProviderSyncRun? run)
        => run?.Status == "queued" || run?.Status == "running";

    private static string GetWatchProviderSubtitle(WatchProviderCardViewModel vm)
    {
        if (vm.AuthSession != null && !vm.Connected)
            return "Waiting for you to enter the code below.";

        if (vm.ApiKeyPromptVisible && vm.UsesApiKey && !vm.Connected)
            return $"Paste your {vm.DisplayName} API key to finish connecting.";

        var connection = vm.Connection;
        if (connection?.Connected == true)
        {
            var username = string.IsNullOrWhiteSpace(connection.ProviderUsername) ? vm.DisplayName : connection.ProviderUsername;
            return $"{username} · {FormatWatchProviderLastSync(connection, vm.LatestRun)}";
        }

        if (connection?.CredentialsConfigured == false)
            return "Server credentials required.";

        if (vm.UsesApiKey)
            return $"Connect with your {vm.DisplayName} API key to import watch history and scrobble playback.";

        return "Connect to start importing watch history and scrobbling playback.";
    }

    private static string FormatWatchProviderLastSync(WatchProviderConnection connection, WatchProviderSyncRun? latestRun)
    {
        var candidates = new[]
        {
            latestRun?.CompletedAt,
            connection.LastInboundSyncAt,
            connection.LastProgressSyncAt,
            connection.LastOutboundSyncAt,
            connection.LastFavoritesSyncAt,
        };

        var newest = candidates
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => DateTimeOffset.TryParse(value, out var parsed) ? parsed : (DateTimeOffset?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .OrderByDescending(value => value)
            .FirstOrDefault();

        return newest == default ? "Never synced" : $"Synced {FormatWatchProviderRelativeTime(newest)}";
    }

    private static string FormatWatchProviderRelativeTime(DateTimeOffset timestamp)
    {
        var seconds = Math.Max(0, (int)(DateTimeOffset.Now - timestamp.ToLocalTime()).TotalSeconds);
        if (seconds < 60) return "just now";
        var minutes = seconds / 60;
        if (minutes < 60) return $"{minutes}m ago";
        var hours = minutes / 60;
        if (hours < 24) return $"{hours}h ago";
        return $"{hours / 24}d ago";
    }

    // ===== Webhook Sync Tab =====

    private void SettingsBackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<SiloPlayer.Helpers.NavigationService>();
        if (nav.CanGoBack) nav.GoBack();
        else nav.Navigate<HomePage>();
    }

    private WebhookSyncConnection? _selectedWebhookConnection;
    private readonly Dictionary<string, ComboBox> _webhookActorSelectors = new(StringComparer.Ordinal);
    private List<Profile> _webhookProfiles = [];
    private List<WebhookSyncEventLog> _webhookEvents = [];
    private int _webhookEventsPage;
    private List<PlexBrowserResource> _webhookPlexServers = [];
    private CancellationTokenSource? _webhookPlexAuthCts;
    private readonly Dictionary<string, Button> _webhookConnectionCards = new(StringComparer.Ordinal);
    private DispatcherTimer? _webhookRefreshTimer;
    private bool _webhookRefreshBusy;

    private async Task LoadWebhookConnectionsAsync()
    {
        WebhookConnectionsPanel.Children.Clear();
        _webhookConnectionCards.Clear();
        NoConnectionsText.Text = "Loading connections...";
        NoConnectionsText.HorizontalAlignment = HorizontalAlignment.Left;
        NoConnectionsText.Visibility = Visibility.Visible;
        try
        {
            if (_webhookProfiles.Count == 0)
            {
                _webhookProfiles = (await App.Services.GetRequiredService<AuthApi>().GetProfilesAsync()).Profiles;
                WebhookDefaultProfileCombo.Items.Clear();
                foreach (var profile in _webhookProfiles)
                    WebhookDefaultProfileCombo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
                var activeProfileId = App.Services.GetRequiredService<SettingsService>().Load().LastProfileId;
                SelectComboBoxByTag(WebhookDefaultProfileCombo, activeProfileId ?? "");
                if (WebhookDefaultProfileCombo.SelectedIndex < 0 && WebhookDefaultProfileCombo.Items.Count > 0)
                    WebhookDefaultProfileCombo.SelectedIndex = 0;
                UpdateWebhookCreateButtonState();
            }
            var api = App.Services.GetRequiredService<WebhookSyncApi>();
            var connections = await api.GetConnectionsAsync();
            NoConnectionsText.Text = "No webhook connections yet. Create one above to generate a provider-specific endpoint.";
            NoConnectionsText.HorizontalAlignment = HorizontalAlignment.Left;
            NoConnectionsText.Visibility = connections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (connections.Count == 0) WebhookConnectionDetail.Visibility = Visibility.Collapsed;
            foreach (var connection in connections)
            {
                var isSelected = connection.Id == _selectedWebhookConnection?.Id;
                var card = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = (Brush)Application.Current.Resources[isSelected ? "SidebarAccentBrush" : "SurfaceBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14),
                };
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                identity.Children.Add(new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = (Brush)Application.Current.Resources[WebhookHealth(connection) switch
                    {
                        "Healthy" => "SuccessBrush",
                        "Needs attention" => "ErrorBrush",
                        _ => "WarningBrush",
                    }],
                });
                identity.Children.Add(new TextBlock { Text = connection.ServerName, FontWeight = FontWeights.SemiBold, FontSize = 14 });
                identity.Children.Add(new TextBlock { Text = FormatWebhookProvider(connection.Provider), FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], VerticalAlignment = VerticalAlignment.Center });
                var info = new StackPanel { Spacing = 3 };
                info.Children.Add(identity);
                info.Children.Add(new TextBlock
                {
                    Text = $"{connection.UserCount} user{(connection.UserCount == 1 ? "" : "s")}" +
                           (connection.LastWebhookReceivedAt.HasValue ? $" · last event {DateTimeDisplay.FormatDateTime(connection.LastWebhookReceivedAt.Value)}" : ""),
                    FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                row.Children.Add(info);
                var health = BuildWebhookHealthBadge(connection);
                Grid.SetColumn(health, 1); row.Children.Add(health);
                card.Content = row;
                card.Click += async (_, _) => await SelectWebhookConnectionAsync(connection);
                _webhookConnectionCards[connection.Id] = card;
                WebhookConnectionsPanel.Children.Add(card);
            }
            if (connections.Count > 0)
            {
                var selected = connections.FirstOrDefault(connection => connection.Id == _selectedWebhookConnection?.Id) ?? connections[0];
                await SelectWebhookConnectionAsync(selected);
            }
            EnsureWebhookRefreshTimer();
        }
        catch (Exception ex)
        {
            NoConnectionsText.Text = $"Could not load webhook connections: {ex.Message}";
            NoConnectionsText.Visibility = Visibility.Visible;
        }
    }

    private static string WebhookHealth(WebhookSyncConnection connection)
    {
        if (connection.LastWebhookErrorAt.HasValue &&
            (!connection.LastWebhookReceivedAt.HasValue || connection.LastWebhookErrorAt > connection.LastWebhookReceivedAt)) return "Needs attention";
        if (connection.LastWebhookReceivedAt.HasValue) return "Healthy";
        if (!string.IsNullOrWhiteSpace(connection.LastWebhookErrorMessage)) return "Needs attention";
        return "Waiting for first delivery";
    }

    private void EnsureWebhookRefreshTimer()
    {
        if (_webhookRefreshTimer == null)
        {
            _webhookRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _webhookRefreshTimer.Tick += async (_, _) =>
            {
                if (_webhookRefreshBusy || _selectedWebhookConnection == null) return;
                _webhookRefreshBusy = true;
                try
                {
                    _webhookEvents = await App.Services.GetRequiredService<WebhookSyncApi>()
                        .GetEventsAsync(_selectedWebhookConnection.Id);
                    BuildWebhookEvents();
                }
                catch
                {
                    // Keep the last successful table visible; the next interval retries.
                }
                finally { _webhookRefreshBusy = false; }
            };
        }
        _webhookRefreshTimer.Stop();
        _webhookRefreshTimer.Start();
    }

    private static string FormatWebhookProvider(string provider)
        => string.IsNullOrWhiteSpace(provider)
            ? "Unknown"
            : char.ToUpperInvariant(provider[0]) + provider[1..];

    private static Border BuildWebhookHealthBadge(WebhookSyncConnection connection)
    {
        var health = WebhookHealth(connection);
        var brushKey = health switch
        {
            "Healthy" => "SuccessBrush",
            "Needs attention" => "ErrorBrush",
            _ => "WarningBrush",
        };
        var foreground = (Brush)Application.Current.Resources[brushKey];
        return new Border
        {
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = health, FontSize = 10, Foreground = foreground },
        };
    }

    private void WebhookProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WebhookPlexAuthPanel == null) return;
        var provider = (WebhookProviderCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "plex";
        var isPlex = provider == "plex";
        WebhookPlexAuthPanel.Visibility = isPlex ? Visibility.Visible : Visibility.Collapsed;
        WebhookManualServerPanel.Visibility = isPlex ? Visibility.Collapsed : Visibility.Visible;
        WebhookManualServerName.PlaceholderText = provider == "emby" ? "My Emby Server" : "My Jellyfin Server";
        UpdateWebhookCreateButtonState();
    }

    private void WebhookCreateField_Changed(object sender, RoutedEventArgs e) => UpdateWebhookCreateButtonState();

    private void UpdateWebhookCreateButtonState()
    {
        if (WebhookCreateConnectionButton == null) return;
        var provider = (WebhookProviderCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "plex";
        var profileId = (WebhookDefaultProfileCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var canCreate = !string.IsNullOrWhiteSpace(profileId) &&
            (provider == "plex"
                ? WebhookPlexServerCombo.SelectedItem is ComboBoxItem { Tag: PlexBrowserResource }
                : !string.IsNullOrWhiteSpace(WebhookManualServerName.Text));
        WebhookCreateConnectionButton.IsEnabled = canCreate;
    }

    private async void WebhookPlexSignIn_Click(object sender, RoutedEventArgs e)
    {
        _webhookPlexAuthCts?.Cancel();
        _webhookPlexAuthCts?.Dispose();
        _webhookPlexAuthCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = _webhookPlexAuthCts.Token;
        WebhookPlexSignInButton.IsEnabled = false;
        WebhookPlexAuthStatus.Text = "Starting Plex sign-in…";
        WebhookPlexServerRow.Visibility = Visibility.Collapsed;
        try
        {
            var auth = App.Services.GetRequiredService<PlexBrowserAuthApi>();
            var pin = await auth.CreatePinAsync(ct);
            if (!await Windows.System.Launcher.LaunchUriAsync(auth.BuildAuthenticationUri(pin)))
                throw new InvalidOperationException("Windows could not open the Plex sign-in page.");
            WebhookPlexAuthStatus.Text = "Complete sign-in in your browser. Waiting for Plex…";

            string? token = null;
            while (!ct.IsCancellationRequested && string.IsNullOrWhiteSpace(token))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                token = await auth.CheckPinAsync(pin, ct);
            }
            if (string.IsNullOrWhiteSpace(token)) throw new TimeoutException("Plex sign-in expired.");

            _webhookPlexServers = await auth.GetServersAsync(token, ct);
            WebhookPlexServerCombo.Items.Clear();
            foreach (var server in _webhookPlexServers)
                WebhookPlexServerCombo.Items.Add(new ComboBoxItem { Content = server.Name, Tag = server });
            if (WebhookPlexServerCombo.Items.Count > 0) WebhookPlexServerCombo.SelectedIndex = 0;
            WebhookPlexServerRow.Visibility = _webhookPlexServers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            WebhookPlexAuthStatus.Text = _webhookPlexServers.Count == 0
                ? "Plex sign-in succeeded, but no media servers were found."
                : $"{_webhookPlexServers.Count} server{(_webhookPlexServers.Count == 1 ? "" : "s")} available";
            WebhookPlexSignInButton.Content = "Re-authenticate";
            UpdateWebhookCreateButtonState();
        }
        catch (OperationCanceledException)
        {
            WebhookPlexAuthStatus.Text = "Plex sign-in expired or was cancelled. Please try again.";
        }
        catch (Exception ex)
        {
            WebhookPlexAuthStatus.Text = $"Plex sign-in failed: {ex.Message}";
        }
        finally
        {
            WebhookPlexSignInButton.IsEnabled = true;
        }
    }

    private async void WebhookCreateConnection_Click(object sender, RoutedEventArgs e)
    {
        var provider = (WebhookProviderCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "plex";
        var profileId = (WebhookDefaultProfileCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var plexServer = (WebhookPlexServerCombo.SelectedItem as ComboBoxItem)?.Tag as PlexBrowserResource;
        if (string.IsNullOrWhiteSpace(profileId) ||
            (provider == "plex" && plexServer == null) ||
            (provider != "plex" && string.IsNullOrWhiteSpace(WebhookManualServerName.Text)))
        {
            ViewModel.ErrorMessage = provider == "plex"
                ? "Sign in to Plex, select a server, and choose a default profile."
                : "Enter a connection name and choose a default profile.";
            return;
        }

        var body = new Dictionary<string, object?>
        {
            ["provider"] = provider,
            ["server_name"] = provider == "plex" ? plexServer!.Name : WebhookManualServerName.Text.Trim(),
            ["default_profile_id"] = profileId,
        };
        if (plexServer != null)
        {
            body["server_id"] = plexServer.ClientIdentifier;
            body["base_url"] = plexServer.PreferredUrl;
            body["access_token"] = plexServer.AccessToken;
        }

        WebhookCreateConnectionButton.IsEnabled = false;
        var oldContent = WebhookCreateConnectionButton.Content;
        WebhookCreateConnectionButton.Content = "Creating…";
        try
        {
            var created = await App.Services.GetRequiredService<WebhookSyncApi>().CreateConnectionAsync(body);
            _selectedWebhookConnection = created.Connection;
            await LoadWebhookConnectionsAsync();
            await SelectWebhookConnectionAsync(created.Connection);
            WebhookUrlBox.Text = created.WebhookUrl;
            WebhookManualServerName.Text = "";
            ViewModel.StatusMessage = "Webhook connection created";
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Failed to create webhook connection: {ex.Message}";
        }
        finally
        {
            WebhookCreateConnectionButton.Content = oldContent;
            WebhookCreateConnectionButton.IsEnabled = true;
        }
    }

    private async Task SelectWebhookConnectionAsync(WebhookSyncConnection connection)
    {
        _selectedWebhookConnection = connection;
        foreach (var pair in _webhookConnectionCards)
            pair.Value.Background = (Brush)Application.Current.Resources[
                pair.Key == connection.Id ? "SidebarAccentBrush" : "SurfaceBrush"];
        WebhookConnectionDetail.Visibility = Visibility.Visible;
        WebhookDetailName.Text = connection.ServerName;
        WebhookDetailHealth.Text = $"{FormatWebhookProvider(connection.Provider)} webhook endpoint";
        WebhookDetailStatus.Text = !string.IsNullOrWhiteSpace(connection.LastWebhookErrorMessage) && WebhookHealth(connection) == "Needs attention"
            ? connection.LastWebhookErrorMessage
            : "Ready to receive webhook traffic";
        WebhookUrlBox.Text = connection.WebhookUrl ?? "";
        WebhookConnectionNameBox.Text = connection.ServerName;
        WebhookConnectionProfileCombo.Items.Clear();
        WebhookConnectionProfileCombo.Items.Add(new ComboBoxItem { Content = "No default profile", Tag = "" });
        foreach (var profile in _webhookProfiles)
            WebhookConnectionProfileCombo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
        SelectComboBoxByTag(WebhookConnectionProfileCombo, connection.DefaultProfileId);
        if (WebhookConnectionProfileCombo.SelectedIndex < 0) WebhookConnectionProfileCombo.SelectedIndex = 0;
        WebhookActorsHost.Children.Clear();
        WebhookEventsHost.Children.Clear();
        WebhookActorsHost.Children.Add(new TextBlock
        {
            Text = "Loading users...",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        WebhookEventsHost.Children.Add(new TextBlock
        {
            Text = "Loading deliveries...",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        BuildWebhookSetupInstructions(connection.Provider);
        try
        {
            var api = App.Services.GetRequiredService<WebhookSyncApi>();
            if (_webhookProfiles.Count == 0) _webhookProfiles = (await App.Services.GetRequiredService<AuthApi>().GetProfilesAsync()).Profiles;
            var actorsTask = api.GetProfileMappingsAsync(connection.Id);
            var eventsTask = api.GetEventsAsync(connection.Id);
            await Task.WhenAll(actorsTask, eventsTask);
            BuildWebhookActors(actorsTask.Result);
            _webhookEvents = eventsTask.Result;
            _webhookEventsPage = 0;
            BuildWebhookEvents();
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not load connection details: {ex.Message}"; }
    }

    private void BuildWebhookActors(WebhookSyncProfileMappingsResponse response)
    {
        _webhookActorSelectors.Clear();
        WebhookActorsHost.Children.Clear();
        var actors = response.Mappings.Select(mapping => (ExternalActorId: mapping.ExternalUserId, ExternalActorName: mapping.ExternalUserName, ProfileId: mapping.SiloProfileId))
            .Concat(response.DiscoveredUsers
                .Where(actor => response.Mappings.All(mapping => mapping.ExternalUserId != actor.ExternalUserId))
                .Select(actor => (ExternalActorId: actor.ExternalUserId, ExternalActorName: actor.ExternalUserName, ProfileId: (string?)null)))
            .OrderBy(actor => actor.ExternalActorName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        WebhookSaveActorsButton.Visibility = actors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (actors.Count == 0)
        {
            WebhookActorsHost.Children.Add(new TextBlock { Text = "No users discovered yet. Send a webhook event first, then map them here.", FontSize = 12 });
            return;
        }
        foreach (var actor in actors)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.Children.Add(new TextBlock { Text = actor.ExternalActorName, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.Items.Add(new ComboBoxItem { Content = "Ignore this user", Tag = "" });
            foreach (var profile in _webhookProfiles) combo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
            SelectComboBoxByTag(combo, actor.ProfileId ?? "");
            Grid.SetColumn(combo, 1); row.Children.Add(combo);
            _webhookActorSelectors[actor.ExternalActorId] = combo;
            combo.DataContext = actor.ExternalActorName;
            WebhookActorsHost.Children.Add(row);
        }
    }

    private void BuildWebhookEvents()
    {
        WebhookEventsHost.Children.Clear();
        const int pageSize = 15;
        var outcome = (WebhookEventOutcomeFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var query = WebhookEventSearchBox.Text.Trim();
        var filtered = _webhookEvents
            .Where(item => outcome == "all" || string.Equals(item.Outcome, outcome, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(query) ||
                           item.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                           (item.ErrorMessage?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                           WebhookEventUserLabel(item).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        var pageCount = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)pageSize));
        _webhookEventsPage = Math.Clamp(_webhookEventsPage, 0, pageCount - 1);
        var rangeStart = filtered.Count == 0 ? 0 : (_webhookEventsPage * pageSize) + 1;
        var rangeEnd = Math.Min((_webhookEventsPage + 1) * pageSize, filtered.Count);
        WebhookEventsPageLabel.Text = $"{rangeStart}–{rangeEnd} of {filtered.Count}";
        WebhookEventsPrevious.IsEnabled = _webhookEventsPage > 0;
        WebhookEventsNext.IsEnabled = _webhookEventsPage + 1 < pageCount;
        WebhookEventsFirst.IsEnabled = _webhookEventsPage > 0;
        WebhookEventsLast.IsEnabled = _webhookEventsPage + 1 < pageCount;
        if (filtered.Count == 0)
        {
            WebhookEventsHost.Children.Add(new TextBlock
            {
                Text = _webhookEvents.Count == 0
                    ? "No deliveries yet. Send a test event from the provider to confirm the connection."
                    : "No deliveries match the current filters.",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 12),
            });
            return;
        }
        foreach (var item in filtered.Skip(_webhookEventsPage * pageSize).Take(pageSize))
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            var badge = BuildWebhookOutcomeBadge(item.Outcome);
            row.Children.Add(badge);
            var eventStack = new StackPanel { Spacing = 2 };
            eventStack.Children.Add(new TextBlock { Text = item.Summary, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
                eventStack.Children.Add(new TextBlock { Text = item.ErrorMessage, FontSize = 11, Foreground = (Brush)Application.Current.Resources["ErrorBrush"], TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(eventStack, 1);
            row.Children.Add(eventStack);
            var itemText = new TextBlock { Text = WebhookEventAttribute(item, "matched_media_item_title") ?? "—", FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(itemText, 2);
            row.Children.Add(itemText);
            var userText = new TextBlock { Text = WebhookEventUserLabel(item), FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(userText, 3);
            row.Children.Add(userText);
            var timeText = new TextBlock { Text = DateTimeDisplay.FormatDateTime(item.ReceivedAt), FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(timeText, 4);
            row.Children.Add(timeText);

            var card = new Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(10, 9, 10, 9),
            };
            card.Click += async (_, _) => await ShowWebhookEventDetailAsync(item);
            WebhookEventsHost.Children.Add(card);
        }
    }

    private void WebhookEventFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (WebhookEventsHost == null) return;
        _webhookEventsPage = 0;
        BuildWebhookEvents();
    }

    private static Border BuildWebhookOutcomeBadge(string outcome)
    {
        var brushKey = outcome.ToLowerInvariant() switch
        {
            "applied" => "SuccessBrush",
            "unmatched" => "WarningBrush",
            "rejected" or "error" => "ErrorBrush",
            _ => "SecondaryTextBrush",
        };
        var foreground = (Brush)Application.Current.Resources[brushKey];
        return new Border
        {
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(outcome) ? "Unknown" : char.ToUpperInvariant(outcome[0]) + outcome[1..],
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground,
            },
        };
    }

    private static string? WebhookEventAttribute(WebhookSyncEventLog item, string key)
    {
        if (item.Attrs == null || !item.Attrs.TryGetValue(key, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => null,
        };
    }

    private static string WebhookEventUserLabel(WebhookSyncEventLog item)
    {
        var name = WebhookEventAttribute(item, "external_user_name");
        var id = WebhookEventAttribute(item, "external_user_id");
        return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)
            ? $"{name} ({id})"
            : name ?? id ?? "Unknown user";
    }

    private static string WebhookEventAttributeLabel(string key) => key switch
    {
        "event_kind" => "Event kind",
        "action" => "Action",
        "external_user_id" => "External user ID",
        "external_user_name" => "External user",
        "external_item_id" => "External item ID",
        "media_kind" => "Media kind",
        "matched_media_item_id" => "Matched item ID",
        "matched_media_item_title" => "Matched item",
        "profile_id" => "Profile ID",
        "client_ip" => "Client IP",
        "content_type" => "Content type",
        "user_agent" => "User agent",
        "path_pattern" => "Path pattern",
        _ => key.Replace('_', ' '),
    };

    private static bool TryFormatWebhookEventAttribute(JsonElement value, out string text)
    {
        text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => value.GetRawText(),
        };
        return !string.IsNullOrWhiteSpace(text);
    }

    private void BuildWebhookSetupInstructions(string provider)
    {
        WebhookSetupInstructionsHost.Children.Clear();
        var instructions = new StackPanel { Spacing = 7, Margin = new Thickness(0, 8, 0, 0) };
        void AddStep(string text) => instructions.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });

        if (string.Equals(provider, "plex", StringComparison.OrdinalIgnoreCase))
        {
            AddStep("1. In Plex, open Settings → Webhooks (Plex Pass required).");
            AddStep("2. Click Add Webhook and paste the URL above.");
            AddStep("3. Save. Plex sends events automatically — no per-event toggles to configure.");
        }
        else if (string.Equals(provider, "emby", StringComparison.OrdinalIgnoreCase))
        {
            AddStep("1. In the Emby dashboard, open Notifications and add a Webhooks notification.");
            AddStep("2. Paste the URL above into Url, and set Request content type to application/json.");
            AddStep("3. Enable the events listed below, then save.");
            AddStep("REQUIRED");
            AddStep("Playback → Stop — Records watch progress and completion.");
            AddStep("RECOMMENDED");
            AddStep("Users → Add to Favorites, Remove from Favorites — Syncs favorites to the mapped Silo profile.");
            AddStep("Users → Mark Played, Mark Unplayed — Needed only if your household manually marks items watched without playing them. Mark Played will duplicate Stop for normal completions, but the result is the same.");
            AddStep("SKIP");
            AddStep("Playback → Start, Pause, Unpause — Silo only records completion, not in-progress state.");
        }
        else
        {
            AddStep("1. Install the official Webhook plugin from Dashboard → Plugins → Catalog and restart Jellyfin.");
            AddStep("2. Open Dashboard → Plugins → Webhook and add a Generic Destination.");
            AddStep("3. Paste the URL above into Webhook Url.");
            AddStep("4. Under Notification Type, enable only Playback Stop. Leave Playback Progress and User Data Saved off — Silo ignores them and they generate heavy traffic.");
            AddStep("5. Paste the template below into Template and save.");
            var template = """
            {
              "provider": "jellyfin",
              "notification_type": "{{NotificationType}}",
              "timestamp": "{{UtcTimestamp}}",
              "server_name": "{{ServerName}}",
              "user": { "id": "{{UserId}}", "name": "{{{Username}}}" },
              "item": {
                "id": "{{ItemId}}", "type": "{{ItemType}}", "name": "{{{Name}}}",
                "series_name": "{{{SeriesName}}}",
                "year": {{#if_exist Year}}{{Year}}{{else}}0{{/if_exist}},
                "season_number": {{#if_exist SeasonNumber}}{{SeasonNumber}}{{else}}0{{/if_exist}},
                "episode_number": {{#if_exist EpisodeNumber}}{{EpisodeNumber}}{{else}}0{{/if_exist}},
                "runtime_ticks": {{#if_exist RunTimeTicks}}{{RunTimeTicks}}{{else}}0{{/if_exist}},
                "provider_ids": { "imdb": "{{Provider_imdb}}", "tmdb": "{{Provider_tmdb}}", "tvdb": "{{Provider_tvdb}}" }
              },
              "playback": {
                "position_ticks": {{#if_exist PlaybackPositionTicks}}{{PlaybackPositionTicks}}{{else}}0{{/if_exist}},
                "played_to_completion": {{#if_equals PlayedToCompletion 'true'}}true{{else}}false{{/if_equals}},
                "runtime_ticks": {{#if_exist RunTimeTicks}}{{RunTimeTicks}}{{else}}0{{/if_exist}}
              }
            }
            """;
            instructions.Children.Add(new TextBox
            {
                Header = "Webhook payload template",
                Text = template,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                MinHeight = 210,
                MaxHeight = 260,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
            });
            var copy = new Button { Content = "Copy template", HorizontalAlignment = HorizontalAlignment.Left };
            copy.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(template);
                Clipboard.SetContent(package);
                ViewModel.StatusMessage = "Jellyfin template copied";
            };
            instructions.Children.Add(copy);
        }

        WebhookSetupInstructionsHost.Children.Add(new Expander
        {
            Header = "Setup instructions",
            Content = instructions,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });
    }

    private async Task ShowWebhookEventDetailAsync(WebhookSyncEventLog item)
    {
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        var content = new StackPanel { Width = 500, Spacing = 16 };

        content.Children.Add(new TextBlock
        {
            Text = $"{DateTimeDisplay.FormatDateTime(item.ReceivedAt)} · HTTP {item.HttpStatus}",
            FontSize = 12,
            Foreground = secondaryBrush,
        });

        var outcomeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        outcomeRow.Children.Add(BuildWebhookOutcomeBadge(item.Outcome));
        var matchedItem = WebhookEventAttribute(item, "matched_media_item_title");
        if (!string.IsNullOrWhiteSpace(matchedItem))
        {
            outcomeRow.Children.Add(new TextBlock
            {
                Text = matchedItem,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 390,
            });
        }
        content.Children.Add(outcomeRow);

        if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
        {
            content.Children.Add(new TextBlock
            {
                Text = item.ErrorMessage,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            });
        }

        if (item.Attrs is { Count: > 0 })
        {
            var formattedAttrs = item.Attrs
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => TryFormatWebhookEventAttribute(pair.Value, out var value)
                    ? (pair.Key, Value: value)
                    : (Key: "", Value: ""))
                .Where(pair => pair.Key.Length > 0)
                .ToList();
            if (formattedAttrs.Count > 0)
            {
                var attrsSection = new StackPanel { Spacing = 6 };
                attrsSection.Children.Add(new TextBlock
                {
                    Text = "Attributes",
                    FontSize = 12,
                    Foreground = secondaryBrush,
                });
                var attrsGrid = new Grid { ColumnSpacing = 24, RowSpacing = 6 };
                attrsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                attrsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (var index = 0; index < formattedAttrs.Count; index++)
                {
                    var row = index / 2;
                    var column = index % 2;
                    while (attrsGrid.RowDefinitions.Count <= row)
                        attrsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var pair = formattedAttrs[index];
                    var cell = new StackPanel { MinWidth = 0, Spacing = 1 };
                    cell.Children.Add(new TextBlock
                    {
                        Text = WebhookEventAttributeLabel(pair.Key),
                        FontSize = 11,
                        Foreground = secondaryBrush,
                    });
                    cell.Children.Add(new TextBlock
                    {
                        Text = pair.Value,
                        FontSize = 12,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.Wrap,
                    });
                    Grid.SetRow(cell, row);
                    Grid.SetColumn(cell, column);
                    attrsGrid.Children.Add(cell);
                }
                attrsSection.Children.Add(attrsGrid);
                content.Children.Add(attrsSection);
            }
        }

        if (!string.IsNullOrWhiteSpace(item.BodyExcerpt))
        {
            content.Children.Add(new TextBox
            {
                Header = "Body excerpt",
                Text = item.BodyExcerpt,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 180,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
            });
        }
        if (!string.IsNullOrWhiteSpace(item.RequestId))
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Request ID: {item.RequestId}",
                FontSize = 11,
                Foreground = secondaryBrush,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var dialog = new ContentDialog
        {
            Title = item.Summary,
            Content = new ScrollViewer { Content = content, MaxHeight = 560 },
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void WebhookEventsPrevious_Click(object sender, RoutedEventArgs e) { _webhookEventsPage--; BuildWebhookEvents(); }
    private void WebhookEventsNext_Click(object sender, RoutedEventArgs e) { _webhookEventsPage++; BuildWebhookEvents(); }
    private void WebhookEventsFirst_Click(object sender, RoutedEventArgs e) { _webhookEventsPage = 0; BuildWebhookEvents(); }
    private void WebhookEventsLast_Click(object sender, RoutedEventArgs e)
    {
        const int pageSize = 15;
        var outcome = (WebhookEventOutcomeFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var query = WebhookEventSearchBox.Text.Trim();
        var count = _webhookEvents.Count(item =>
            (outcome == "all" || string.Equals(item.Outcome, outcome, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(query) || item.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             (item.ErrorMessage?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
             WebhookEventUserLabel(item).Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        _webhookEventsPage = Math.Max(0, (int)Math.Ceiling(count / (double)pageSize) - 1);
        BuildWebhookEvents();
    }

    private async void WebhookSaveConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWebhookConnection == null || string.IsNullOrWhiteSpace(WebhookConnectionNameBox.Text)) return;
        var button = (Button)sender;
        button.IsEnabled = false;
        try
        {
            var updated = await App.Services.GetRequiredService<WebhookSyncApi>().UpdateConnectionAsync(
                _selectedWebhookConnection.Id,
                new Dictionary<string, object?>
                {
                    ["server_name"] = WebhookConnectionNameBox.Text.Trim(),
                    ["default_profile_id"] = (WebhookConnectionProfileCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
                });
            _selectedWebhookConnection = updated;
            WebhookDetailName.Text = updated.ServerName;
            await LoadWebhookConnectionsAsync();
            ViewModel.StatusMessage = "Webhook connection updated";
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Could not update connection: {ex.Message}";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void WebhookCopyUrl_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(WebhookUrlBox.Text ?? "");
        Clipboard.SetContent(package);
        ViewModel.StatusMessage = "Webhook URL copied";
    }

    private async void WebhookRotate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWebhookConnection == null) return;
        try
        {
            var rotated = await App.Services.GetRequiredService<WebhookSyncApi>().RotateWebhookAsync(_selectedWebhookConnection.Id);
            WebhookUrlBox.Text = rotated.WebhookUrl;
            _selectedWebhookConnection.WebhookUrl = rotated.WebhookUrl;
            ViewModel.StatusMessage = "Webhook URL rotated";
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not rotate URL: {ex.Message}"; }
    }

    private async void WebhookDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWebhookConnection == null) return;
        var connection = _selectedWebhookConnection;
        WebhookDeleteConnectionButton.IsEnabled = false;
        var previousContent = WebhookDeleteConnectionButton.Content;
        WebhookDeleteConnectionButton.Content = "Deleting…";
        try
        {
            await App.Services.GetRequiredService<WebhookSyncApi>().DeleteConnectionAsync(connection.Id);
            _selectedWebhookConnection = null;
            WebhookConnectionDetail.Visibility = Visibility.Collapsed;
            await LoadWebhookConnectionsAsync();
            ViewModel.StatusMessage = "Webhook connection deleted";
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not delete connection: {ex.Message}"; }
        finally
        {
            WebhookDeleteConnectionButton.Content = previousContent;
            WebhookDeleteConnectionButton.IsEnabled = true;
        }
    }

    private async void WebhookSaveActors_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWebhookConnection == null) return;
        var mappings = _webhookActorSelectors.Select(pair => new Dictionary<string, object?>
        {
            ["external_user_id"] = pair.Key,
            ["external_user_name"] = pair.Value.DataContext as string ?? pair.Key,
            ["silo_profile_id"] = (pair.Value.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } id ? id : null,
        }).ToList();
        try
        {
            await App.Services.GetRequiredService<WebhookSyncApi>().UpdateProfileMappingsAsync(_selectedWebhookConnection.Id,
                new Dictionary<string, object?> { ["mappings"] = mappings });
            ViewModel.StatusMessage = "Profile mappings saved";
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not save actor routing: {ex.Message}"; }
    }

    // ===== Profiles Tab =====

    private async Task LoadProfilesAsync()
    {
        var generation = Interlocked.Increment(ref _profilesLoadGeneration);
        _ = LoadHouseholdSessionsAsync(showLoading: HouseholdStreamsPanel.Children.Count == 0);
        try
        {
            var authApi = App.Services.GetRequiredService<AuthApi>();
            var response = await authApi.GetProfilesAsync();
            if (generation != Volatile.Read(ref _profilesLoadGeneration)) return;
            var profiles = response.Profiles;
            ProfileCardsPanel.Children.Clear();

            if (profiles.Count == 0)
            {
                NoProfilesText.Visibility = Visibility.Visible;
                return;
            }
            NoProfilesText.Visibility = Visibility.Collapsed;

            var activeProfileId = App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>().SelectedProfileId;

            foreach (var profile in profiles)
            {
                bool isActive = profile.Id == activeProfileId;
                var card = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(16, 12, 16, 12),
                };
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Avatar
                var avatar = new Border
                {
                    Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
                    Background = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["AccentBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                };
                avatar.Child = new TextBlock
                {
                    Text = !string.IsNullOrEmpty(profile.Name) ? profile.Name[0].ToString().ToUpperInvariant() : "?",
                    FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
                {
                    avatar.Child = new Image
                    {
                        Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(profile.AvatarUrl)),
                        Stretch = Stretch.UniformToFill,
                    };
                }
                Grid.SetColumn(avatar, 0);

                // Name + badges
                var info = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                nameRow.Children.Add(new TextBlock
                {
                    Text = profile.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                });
                if (isActive)
                {
                    nameRow.Children.Add(new Border
                    {
                        Background = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"],
                        CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "Current", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["AccentBrush"],
                        }
                    });
                }
                if (profile.IsPrimary)
                {
                    // Upstream c3f2da5: primary profile badge next to Active/PIN.
                    nameRow.Children.Add(new Border
                    {
                        BorderBrush = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "Primary", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"],
                        }
                    });
                }
                if (profile.IsChild)
                    nameRow.Children.Add(ProfileBadge("Kids"));
                if (profile.HasPin)
                    nameRow.Children.Add(ProfileBadge("PIN"));
                info.Children.Add(nameRow);
                info.Children.Add(new TextBlock
                {
                    Text = BuildProfileAccessSummary(profile),
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                Grid.SetColumn(info, 1);

                var useBtn = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE77B", FontSize = 12 },
                            new TextBlock { Text = "Use" },
                        },
                    },
                    Padding = new Thickness(11, 5, 11, 5),
                    Visibility = isActive ? Visibility.Collapsed : Visibility.Visible,
                };
                useBtn.Click += async (_, _) => await UseProfileAsync(profile);

                // Edit button — opens the profile editor dialog. Visible to
                // admins, to the user's own active profile, and to primary
                // profiles managing the household. We let the server enforce
                // exact gating; the desktop shows the button for all rows.
                var editBtn = new Button
                {
                    Padding = new Thickness(11, 5, 11, 5),
                    CornerRadius = new CornerRadius(6),
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE70F", FontSize = 12 },
                            new TextBlock { Text = "Edit" },
                        },
                    },
                };
                ToolTipService.SetToolTip(editBtn, "Edit profile");
                var profileForEdit = profile;
                editBtn.Click += async (_, _) => await ShowEditProfileDialogAsync(profileForEdit);

                // Delete button (blocked for active AND primary profiles).
                // Upstream c3f2da5: primary profiles can only be removed by
                // deleting the account.
                var deleteBlocked = profiles.Count <= 1 || isActive || profile.IsPrimary;
                var deleteGuardReason = profiles.Count <= 1 ? "At least one profile is required."
                    : profile.IsPrimary ? "The primary profile can only be removed by deleting the account."
                    : isActive ? "Switch to another profile before deleting this one."
                    : null;
                var deleteBtn = new Button
                {
                    Padding = new Thickness(11, 5, 11, 5),
                    CornerRadius = new CornerRadius(6),
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                            new TextBlock { Text = "Delete" },
                        },
                    },
                    IsEnabled = !deleteBlocked,
                    Opacity = deleteBlocked ? 0.3 : 1.0,
                };
                ToolTipService.SetToolTip(deleteBtn, deleteGuardReason ?? "Delete profile");
                var capturedProfile = profile;
                deleteBtn.Click += async (_, _) =>
                {
                    var error = new TextBlock
                    {
                        Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Visibility = Visibility.Collapsed,
                    };
                    var dialogContent = new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"Delete profile \"{capturedProfile.Name}\"? This action cannot be undone.",
                                TextWrapping = TextWrapping.Wrap,
                            },
                            error,
                        },
                    };
                    var dialog = new ContentDialog
                    {
                        Title = "Delete profile",
                        Content = dialogContent,
                        PrimaryButtonText = "Delete", PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                        CloseButtonText = "Cancel", XamlRoot = this.XamlRoot, DefaultButton = ContentDialogButton.Close
                    };

                    dialog.PrimaryButtonClick += async (_, args) =>
                    {
                        args.Cancel = true;
                        var deferral = args.GetDeferral();
                        try
                        {
                            dialog.IsPrimaryButtonEnabled = false;
                            dialog.PrimaryButtonText = "Deleting...";
                            error.Visibility = Visibility.Collapsed;
                            var deleteApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AuthApi>();
                            await deleteApi.DeleteProfileAsync(capturedProfile.Id);
                            args.Cancel = false;
                        }
                        catch (Exception ex)
                        {
                            error.Text = $"Couldn't delete profile: {ex.Message}";
                            error.Visibility = Visibility.Visible;
                            dialog.PrimaryButtonText = "Delete";
                            dialog.IsPrimaryButtonEnabled = true;
                        }
                        finally
                        {
                            deferral.Complete();
                        }
                    };

                    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                        await LoadProfilesAsync();
                };
                var actionButtons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                actionButtons.Children.Add(useBtn);
                actionButtons.Children.Add(editBtn);
                actionButtons.Children.Add(deleteBtn);
                var actions = new StackPanel
                {
                    Spacing = 6,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { actionButtons },
                };
                if (deleteGuardReason != null)
                {
                    actions.Children.Add(new TextBlock
                    {
                        Text = deleteGuardReason,
                        FontSize = 11,
                        Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                        TextAlignment = TextAlignment.Right,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 300,
                    });
                }
                Grid.SetColumn(actions, 2);

                row.Children.Add(avatar);
                row.Children.Add(info);
                row.Children.Add(actions);
                card.Child = row;
                ProfileCardsPanel.Children.Add(card);
            }
        }
        catch (Exception ex)
        {
            NoProfilesText.Text = $"Failed to load profiles: {ex.Message}";
            NoProfilesText.Visibility = Visibility.Visible;
        }
    }

    private static Border ProfileBadge(string label) => new()
    {
        BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(7, 2, 7, 2),
        Child = new TextBlock { Text = label, FontSize = 10, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] }
    };

    private static string BuildProfileAccessSummary(SiloPlayer.Core.Models.Auth.Profile profile)
    {
        var rating = string.IsNullOrWhiteSpace(profile.MaxContentRating)
            ? "Any content"
            : $"{profile.MaxContentRating} max";
        var libraryCount = profile.AllowedLibraryIds?.Distinct().Count() ?? 0;
        var libraries = profile.LibraryRestrictionsEnabled
            ? $"{libraryCount} {(libraryCount == 1 ? "library" : "libraries")}"
            : "All libraries";
        var quality = (profile.MaxPlaybackQuality ?? "").Trim().ToLowerInvariant() switch
        {
            "2160p" or "4k" or "uhd" or "4320p" => "4K quality",
            "1080p" or "720p" or "480p" or "standard" => "Standard quality",
            _ => "Any quality",
        };
        return string.Join(" · ", rating, libraries, quality);
    }

    private async Task UseProfileAsync(SiloPlayer.Core.Models.Auth.Profile profile)
    {
        string? profileToken = null;
        if (profile.HasPin)
        {
            var pin = new PasswordBox
            {
                PlaceholderText = "Enter 4-digit PIN",
                MaxLength = 4,
                PasswordRevealMode = PasswordRevealMode.Hidden,
                InputScope = new InputScope
                {
                    Names = { new InputScopeName(InputScopeNameValue.Number) },
                },
            };
            var error = new TextBlock
            {
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            var content = new StackPanel
            {
                Width = 280,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "PIN", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    pin,
                    error,
                },
            };
            var dialog = new ContentDialog
            {
                Title = $"Enter PIN for {profile.Name}",
                Content = content,
                PrimaryButtonText = "Confirm",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
                XamlRoot = XamlRoot,
            };
            var verified = false;
            pin.PasswordChanged += (_, _) =>
                dialog.IsPrimaryButtonEnabled = pin.Password.Length > 0;
            dialog.Opened += (_, _) => pin.Focus(FocusState.Programmatic);
            dialog.PrimaryButtonClick += async (sender, args) =>
            {
                args.Cancel = true;
                var deferral = args.GetDeferral();
                try
                {
                    sender.IsPrimaryButtonEnabled = false;
                    sender.PrimaryButtonText = "Verifying...";
                    error.Visibility = Visibility.Collapsed;
                    var result = await App.Services.GetRequiredService<AuthApi>()
                        .VerifyPinAsync(profile.Id, pin.Password);
                    if (result.Valid && !string.IsNullOrWhiteSpace(result.ProfileToken))
                    {
                        profileToken = result.ProfileToken;
                        verified = true;
                        args.Cancel = false;
                        return;
                    }

                    error.Text = "Incorrect PIN";
                    error.Visibility = Visibility.Visible;
                    pin.Password = "";
                    pin.Focus(FocusState.Programmatic);
                }
                catch
                {
                    error.Text = "Verification failed";
                    error.Visibility = Visibility.Visible;
                    pin.Focus(FocusState.Programmatic);
                }
                finally
                {
                    sender.PrimaryButtonText = "Confirm";
                    sender.IsPrimaryButtonEnabled = pin.Password.Length > 0;
                    deferral.Complete();
                }
            };

            await dialog.ShowAsync();
            if (!verified) return;
        }

        var auth = App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>();
        auth.SelectProfile(profile.Id, profileToken, profile);
        App.Services.GetRequiredService<CatalogApi>().InvalidateLibraryCache();
        var settingsService = App.Services.GetRequiredService<SiloPlayer.Core.Services.SettingsService>();
        var settings = settingsService.Load();
        settings.LastProfileId = profile.Id;
        settingsService.Save(settings);
        await LoadProfilesAsync();
        App.MainWindowInstance?.NavigateToHome();
    }

    private void StartHouseholdSessionsPolling()
    {
        if (_householdSessionsTimer == null)
        {
            _householdSessionsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _householdSessionsTimer.Tick += async (_, _) =>
            {
                if (ProfilesPanel.Visibility == Visibility.Visible)
                    await LoadHouseholdSessionsAsync(showLoading: false);
            };
        }

        _householdSessionsTimer.Stop();
        _householdSessionsTimer.Start();
    }

    private async Task LoadHouseholdSessionsAsync(bool showLoading)
    {
        if (_householdSessionsLoading) return;
        _householdSessionsLoading = true;
        var generation = Interlocked.Increment(ref _householdSessionsLoadGeneration);

        HouseholdStreamsPanel.Visibility = Visibility.Visible;
        HouseholdStreamsPanel.Children.Clear();
        if (showLoading && _householdSessions.Count == 0)
            HouseholdStreamsPanel.Children.Add(BuildHouseholdStreamsLoadingCard());
        else
            HouseholdStreamsPanel.Children.Add(BuildHouseholdStreamsCard(_householdSessions, refreshing: true));

        try
        {
            var authApi = App.Services.GetRequiredService<AuthApi>();
            var sessions = await authApi.GetHouseholdSessionsAsync();
            if (generation != Volatile.Read(ref _householdSessionsLoadGeneration)) return;
            _householdSessions = sessions;
            HouseholdStreamsPanel.Children.Clear();
            HouseholdStreamsPanel.Children.Add(BuildHouseholdStreamsCard(sessions, refreshing: false));
        }
        catch (ApiException ex) when (ex.StatusCode is 403 or 404)
        {
            // Older servers did not expose this account-scoped route. Do not
            // leave a permanently empty error card on those versions.
            HouseholdStreamsPanel.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // React Query keeps the last successful result during a failed
            // refetch. Match that behavior and avoid making the panel flash.
            HouseholdStreamsPanel.Children.Clear();
            HouseholdStreamsPanel.Children.Add(BuildHouseholdStreamsCard(_householdSessions, refreshing: false));
        }
        finally
        {
            _householdSessionsLoading = false;
        }
    }

    private Border BuildHouseholdStreamsLoadingCard()
    {
        var card = CreateHouseholdStreamsCard();
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(BuildHouseholdStreamsHeader([], refreshing: true));
        for (var index = 0; index < 2; index++)
        {
            content.Children.Add(new Border
            {
                Height = 80,
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Opacity = 0.62,
            });
        }
        card.Child = content;
        return card;
    }

    private Border BuildHouseholdStreamsCard(IReadOnlyList<PlaybackSessionSummary> sessions, bool refreshing)
    {
        var card = CreateHouseholdStreamsCard();
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(BuildHouseholdStreamsHeader(sessions, refreshing));

        if (sessions.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No one is streaming right now.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }
        else
        {
            foreach (var session in sessions)
                stack.Children.Add(BuildHouseholdStreamRow(session));
        }

        card.Child = stack;
        return card;
    }

    private static Border CreateHouseholdStreamsCard() => new()
    {
        Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
        BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(16),
    };

    private FrameworkElement BuildHouseholdStreamsHeader(IReadOnlyList<PlaybackSessionSummary> sessions, bool refreshing)
    {
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Spacing = 4 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = "Active streams",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (sessions.Count > 0)
        {
            var live = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            live.Children.Add(new FontIcon { Glyph = "\uE720", FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            live.Children.Add(new TextBlock { Text = $"{sessions.Count} live", FontSize = 10, FontWeight = FontWeights.SemiBold });
            titleRow.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["AccentBackgroundBrush"],
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7, 2, 7, 2),
                Child = live,
            });
        }
        titleStack.Children.Add(titleRow);
        titleStack.Children.Add(new TextBlock
        {
            Text = "Playback happening on any profile in this account.",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(titleStack);

        if (refreshing)
        {
            var progress = new ProgressRing { Width = 16, Height = 16, IsActive = true, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(progress, 1);
            header.Children.Add(progress);
        }
        return header;
    }

    private FrameworkElement BuildHouseholdStreamRow(PlaybackSessionSummary session)
    {
        var row = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
        };
        var layout = new Grid { ColumnSpacing = 12 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var poster = new Border
        {
            Width = 40,
            Height = 56,
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (!string.IsNullOrWhiteSpace(session.PosterUrl) && Uri.TryCreate(session.PosterUrl, UriKind.Absolute, out var posterUri))
            poster.Child = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(posterUri), Stretch = Stretch.UniformToFill };
        layout.Children.Add(poster);

        var content = new StackPanel { Spacing = 6 };
        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        badgeRow.Children.Add(ProfileBadge(string.IsNullOrWhiteSpace(session.ProfileName) ? "Profile" : session.ProfileName.Trim()));
        badgeRow.Children.Add(ProfileBadge(session.IsPaused ? "\u23F8  Paused" : "\u25B6  Playing"));
        var method = session.PlayMethod?.Trim().ToLowerInvariant() switch
        {
            "direct" => "Direct play",
            "remux" => "Remux",
            "transcode" or "hls" => "Transcode",
            _ => "Unknown",
        };
        var bitrate = SessionDisplayText.FormatBitrate(session.StreamBitrateKbps);
        badgeRow.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(bitrate) ? method : $"{method} · {bitrate}",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (session.IsJellyfinClient) badgeRow.Children.Add(ProfileBadge("JF"));
        content.Children.Add(badgeRow);

        var title = SessionDisplayText.GetTitle(session);
        if (!string.IsNullOrWhiteSpace(session.ContentId) && session.MediaFileId > 0)
        {
            var contentId = session.ContentId;
            var fileId = session.MediaFileId;
            var titleLink = new HyperlinkButton
            {
                Content = title,
                Padding = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            };
            titleLink.Click += (_, _) => _ = App.Services.GetRequiredService<PlayerService>().PlayAsync(contentId, fileId: fileId);
            content.Children.Add(titleLink);
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var subtitle = SessionDisplayText.GetSubtitle(session);
        if (!string.IsNullOrWhiteSpace(subtitle))
            content.Children.Add(new TextBlock { Text = subtitle, FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextTrimming = TextTrimming.CharacterEllipsis });

        var meta = new[]
        {
            FormatHouseholdStreamElapsed(session.StartedAt),
            SessionDisplayText.GetClientLabel(session),
            session.ClientIp?.Trim() ?? "",
            session.NodeDisplayName?.Trim() ?? "",
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        content.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", meta),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(content, 1);
        layout.Children.Add(content);
        row.Child = layout;

        return row;
    }

    private static string FormatHouseholdStreamElapsed(string startedAt)
    {
        if (!DateTimeOffset.TryParse(startedAt, out var started)) return "";
        var minutes = Math.Max(0, (int)Math.Floor((DateTimeOffset.UtcNow - started.ToUniversalTime()).TotalMinutes));
        if (minutes < 60) return $"{minutes}m";
        var hours = minutes / 60;
        var remainder = minutes % 60;
        return remainder > 0 ? $"{hours}h {remainder}m" : $"{hours}h";
    }

    private async void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var saved = await Dialogs.ProfileEditorDialog.ShowAsync(XamlRoot, profile: null);
        if (saved != null)
            await LoadProfilesAsync();
    }

    /// <summary>
    /// Opens the current WebUI-equivalent editor for identity, PIN, avatars,
    /// content limits, playback quality, and library access.
    /// </summary>
    private async Task ShowEditProfileDialogAsync(SiloPlayer.Core.Models.Auth.Profile profile)
    {
        try
        {
            var result = await Dialogs.ProfileEditorDialog.ShowWithContextAsync(XamlRoot, profile);
            var saved = result.Profile;
            if (saved == null) return;

            var auth = App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>();
            if (string.Equals(saved.Id, auth.SelectedProfileId, StringComparison.Ordinal))
            {
                var profileToken = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>().ProfileToken;
                if (string.IsNullOrWhiteSpace(profileToken) &&
                    !string.IsNullOrWhiteSpace(result.Pin) &&
                    saved.HasPin)
                {
                    try
                    {
                        var verification = await App.Services.GetRequiredService<SiloPlayer.Core.Api.AuthApi>()
                            .VerifyPinAsync(saved.Id, result.Pin);
                        if (verification.Valid && !string.IsNullOrWhiteSpace(verification.ProfileToken))
                        {
                            profileToken = verification.ProfileToken;
                        }
                        else
                        {
                            App.Services.GetRequiredService<SiloPlayer.Services.ToastService>()
                                .Error("Profile saved, but PIN verification failed");
                            await LoadProfilesAsync();
                            return;
                        }
                    }
                    catch
                    {
                        App.Services.GetRequiredService<SiloPlayer.Services.ToastService>()
                            .Error("Profile saved, but PIN verification failed");
                        await LoadProfilesAsync();
                        return;
                    }
                }

                auth.SelectProfile(saved.Id, profileToken, saved);
            }
            await LoadProfilesAsync();
        }
        catch (Exception ex)
        {
            var errDialog = new ContentDialog
            {
                Title = "Couldn't update profile",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot,
            };
            await errDialog.ShowAsync();
        }
    }
}
