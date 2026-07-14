using System.Collections.Specialized;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.HistoryImport;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Models.WatchProviders;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    private readonly CardOverlayService _cardOverlayService;
    private CardOverlayPrefs? _cardOverlayDraft;
    private string _cardOverlayPreviewVariant = "movie";
    private bool _suppressOverlayEvents;
    private CancellationTokenSource? _overlaySaveCts;
    private CancellationTokenSource? _themeCssSaveCts;
    private bool _themeCssLoaded;
    private bool _rememberLibraryPagesLoaded;
    // Start suppressed — handlers that fire during XAML parse (before all sibling
    // x:Name fields are assigned) would otherwise null-ref on their forward references
    // and surface as a cryptic "Failed to assign to RangeBase.Value" XamlParseException.
    // Set back to false after the page finishes loading.
    private bool _suppressEvents = true;

    // Language options for preferred audio language.
    private static readonly (string Tag, string Label)[] AudioLanguageOptions =
    [
        ("", "Profile default"),
        ("original", "Original"),
        ("en", "English"),
        ("es", "Spanish"),
        ("fr", "French"),
        ("de", "German"),
        ("it", "Italian"),
        ("pt", "Portuguese"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("zh", "Chinese"),
        ("ru", "Russian"),
        ("ar", "Arabic"),
        ("hi", "Hindi"),
    ];

    // Language options for subtitles (includes "None")
    private static readonly (string Tag, string Label)[] SubtitleLanguageOptions =
    [
        ("", "Profile default"),
        ("none", "None"),
        ("en", "English"),
        ("es", "Spanish"),
        ("fr", "French"),
        ("de", "German"),
        ("it", "Italian"),
        ("pt", "Portuguese"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("zh", "Chinese"),
        ("ru", "Russian"),
        ("ar", "Arabic"),
        ("hi", "Hindi"),
    ];

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
        this.InitializeComponent();

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
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        await ViewModel.LoadCommand.ExecuteAsync(null);
        SyncComboBoxes();
        await LoadRememberLibraryPagesAsync();
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

        if (e.Parameter is string requestedTab)
        {
            var button = requestedTab switch
            {
                "Playback" => PlaybackTab,
                "Subtitles" or "SubtitleAppearance" => SubtitlesTab,
                "Appearance" => AppearanceTab,
                "ThemeEditor" => ThemeEditorTab,
                "Accessibility" => AccessibilityTab,
                "HomeScreen" => HomeScreenTab,
                "CardOverlays" => CardOverlaysTab,
                "Personalize" => PersonalizeTab,
                "Libraries" => LibrariesTab,
                "Import" or "HistoryImport" => ImportTab,
                "WebhookSync" => WebhookSyncTab,
                "WatchProviders" => WatchProvidersTab,
                "Notifications" or "NotificationsSettings" => NotificationsSettingsTab,
                "Profiles" => ProfilesTab,
                _ => null,
            };
            if (button is not null)
                Tab_Click(button, new RoutedEventArgs());
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // Stop the history_import event channel subscription when leaving Settings.
        StopImportEventSubscription();
        _themeCssSaveCts?.Cancel();
        _themeCssSaveCts?.Dispose();
        _themeCssSaveCts = null;
        base.OnNavigatedFrom(e);
    }

    private void SyncComboBoxes()
    {
        _suppressEvents = true;

        SelectComboBoxByTag(QualityComboBox, ViewModel.QualityPreference);
        SelectComboBoxByTag(SubtitleLanguageComboBox, ViewModel.SubtitleLanguage);
        SelectComboBoxByTag(SubtitleModeComboBox, ViewModel.SubtitleMode);
        SelectComboBoxByTag(NextUpModeComboBox, ViewModel.NextUpMode);

        SelectComboBoxByTag(SpokenLanguageComboBox, ViewModel.AudioLanguage);
        SelectComboBoxByTag(MetadataLanguageComboBox, ViewModel.PreferredMetadataLanguage);

        _suppressEvents = false;
    }

    private void AudioPassthroughToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        var settingsService = App.Services.GetRequiredService<SiloPlayer.Core.Services.SettingsService>();
        var settings = settingsService.Load();
        settings.AudioBitstreamPassthrough = AudioPassthroughToggle.IsOn;
        settingsService.Save(settings);
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
    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SettingsSearchBox is null || SettingsSearchStatus is null)
            return;

        var query = SettingsSearchBox.Text.Trim();
        var entries = new (Button Button, string SearchText)[]
        {
            (PlaybackTab, "playback quality language skipping video spoken metadata auto skip intros credits recaps preview auto play next up episodes"),
            (SubtitlesTab, "subtitles subtitle language behavior forced captions font size family color outline background opacity position preview"),
            (AppearanceTab, "appearance theme profile dark light custom date time format clock reset cinema"),
            (ThemeEditorTab, "theme editor customize colors css design tokens token overrides custom css community themes preview"),
            (AccessibilityTab, "accessibility readability contrast motion transparency text size weight high contrast preview"),
            (HomeScreenTab, "home screen sections layout rows continue watching next up recently added library order scope reset"),
            (CardOverlaysTab, "card overlays poster badges overlay accent color preset preview icon position styling"),
            (PersonalizeTab, "personalize taste profile recommendations ratings likes dislikes refine"),
            (LibrariesTab, "libraries library visibility access disabled order playback preferences spoken subtitle forced remember"),
            (ImportTab, "history import emby jellyfin plex watched mapping sync fetched matched unmatched progress skipped"),
            (WebhookSyncTab, "webhook sync plex emby jellyfin intake progress watched connections deliveries server url token"),
            (WatchProvidersTab, "watch providers trakt import export scrobble favorites history progress removals"),
            (NotificationsSettingsTab, "notifications new episodes email discord browser push webhooks per episode alerts digest url"),
            (ProfilesTab, "profiles profile names pin access rules primary household library create delete"),
        };

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = 0;
        foreach (var entry in entries)
        {
            var visible = tokens.Length == 0 || tokens.All(token =>
                entry.SearchText.Contains(token, StringComparison.OrdinalIgnoreCase));
            entry.Button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) matches++;
        }

        PlaybackNavGroup.Visibility = PlaybackTab.Visibility == Visibility.Visible || SubtitlesTab.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        AppearanceNavGroup.Visibility = new[] { AppearanceTab, ThemeEditorTab, AccessibilityTab, HomeScreenTab, CardOverlaysTab, PersonalizeTab }
            .Any(button => button.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        LibraryDataNavGroup.Visibility = new[] { LibrariesTab, ImportTab, WebhookSyncTab, WatchProvidersTab }
            .Any(button => button.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        AccountNavGroup.Visibility = NotificationsSettingsTab.Visibility == Visibility.Visible || ProfilesTab.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;

        SettingsSearchStatus.Text = tokens.Length == 0
            ? "14 settings sections"
            : matches == 0 ? "No matching settings" : $"{matches} {(matches == 1 ? "match" : "matches")}";
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton || clickedButton.Tag is not string tag)
            return;

        var tabs = new[] { AppearanceTab, PlaybackTab, LibrariesTab, SubtitlesTab, HomeScreenTab, CardOverlaysTab, PersonalizeTab, ImportTab, WebhookSyncTab, WatchProvidersTab, NotificationsSettingsTab, ProfilesTab, ThemeEditorTab, AccessibilityTab, PluginsTab, SessionsTab };
        foreach (var tab in tabs)
        {
            tab.Style = (Style)Resources["InactiveTabStyle"];
        }

        clickedButton.Style = (Style)Resources["ActiveTabStyle"];

        AppearancePanel.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
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
        ThemeEditorPanel.Visibility = tag == "ThemeEditor" ? Visibility.Visible : Visibility.Collapsed;
        AccessibilityPanel.Visibility = tag == "Accessibility" ? Visibility.Visible : Visibility.Collapsed;
        SessionsPanel.Visibility = tag == "Sessions" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "Profiles")
        {
            _ = LoadProfilesAsync();
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
        else if (tag == "NotificationsSettings" && NotificationsSettingsHost.Content is null)
        {
            NotificationsSettingsHost.Content = new global::SiloPlayer.Controls.NotificationSettingsControl();
        }
    }

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

    private async Task LoadCardOverlaySettingsAsync()
    {
        try
        {
            await _cardOverlayService.EnsureLoadedAsync();
            _cardOverlayDraft = _cardOverlayService.GetDocument();
            CardOverlaysDisabledBanner.Visibility = _cardOverlayService.Enabled
                ? Visibility.Collapsed
                : Visibility.Visible;
            CardOverlayControlsContainer.Opacity = _cardOverlayService.Enabled ? 1 : 0.5;
            CardOverlayControlsContainer.IsHitTestVisible = _cardOverlayService.Enabled;
            _suppressOverlayEvents = true;
            SelectComboBoxByTag(CardOverlayPresetCombo, _cardOverlayDraft.Preset);
            _suppressOverlayEvents = false;
            BuildCardOverlayControls();
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
            ["show_status", "imdb_top_250", "rt_certified_fresh"]),
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
        grid.Children.Add(label);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var iconToggle = new ToggleSwitch { Header = "Icon", IsOn = initial.ShowIcon ?? (_cardOverlayDraft?.Preset is "vibrant" or "pill") };
        var accent = new ComboBox { Width = 105, PlaceholderText = "Accent" };
        var accentIndex = 0;
        for (var index = 0; index < OverlayAccentOptions.Length; index++)
        {
            var option = OverlayAccentOptions[index];
            accent.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
            if (string.Equals(option.Value, initial.AccentColor, StringComparison.OrdinalIgnoreCase)) accentIndex = index;
        }
        accent.SelectedIndex = accentIndex;

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
            if (_suppressOverlayEvents || _cardOverlayDraft is null) return;
            var selectedAccent = (accent.SelectedItem as ComboBoxItem)?.Tag as string;
            var selectedPosition = (position.SelectedItem as ComboBoxItem)?.Tag is OverlayPosition pos ? pos : initial.Position;
            UpdateCardOverlayItem(definition.Id, new OverlayItemConfig(enabled.IsOn, selectedPosition, selectedAccent, iconToggle.IsOn));
        }
        iconToggle.Toggled += (_, _) => Apply();
        accent.SelectionChanged += (_, _) => Apply();
        position.SelectionChanged += (_, _) => Apply();
        enabled.Toggled += (_, _) => Apply();
        controls.Children.Add(iconToggle);
        controls.Children.Add(accent);
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

    private void CardOverlayPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressOverlayEvents || _cardOverlayDraft is null ||
            CardOverlayPresetCombo.SelectedItem is not ComboBoxItem { Tag: string preset }) return;
        _cardOverlayDraft = _cardOverlayDraft with { Preset = preset };
        BuildCardOverlayControls();
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
        "resolution" => "Video resolution, such as 4K or 1080p.",
        "hdr" => "HDR format, including HDR10 and Dolby Vision.",
        "resolution_hdr" => "Combined resolution and HDR badge; suppresses the standalone pair.",
        "audio" => "Primary audio format, including Atmos where available.",
        "audio_channels" => "Channel layout such as 5.1 or 7.1.",
        "video_codec" => "Video codec such as HEVC, AV1, or H.264.",
        "container" => "Media container such as MKV or MP4.",
        "aspect_ratio" => "Encoded display aspect ratio.",
        "release_type" => "Release source such as Remux or WEB-DL.",
        "edition" => "Edition label when the title has one.",
        "multi_audio" => "Shows when multiple audio tracks are available.",
        "multi_sub" => "Shows when subtitle tracks are available.",
        "rating_imdb" => "IMDb community rating.",
        "rating_tmdb" => "TMDB community rating.",
        "rating_rt" => "Rotten Tomatoes critic score.",
        "rating_rt_audience" => "Rotten Tomatoes audience score.",
        "content_rating" => "Age or content classification.",
        "year" => "Release year.",
        "runtime" => "Movie or episode runtime.",
        "original_language" => "Original spoken language.",
        "studio" => "Primary production studio.",
        "network" => "Original television network.",
        "show_status" => "Current series production status.",
        "imdb_top_250" => "IMDb Top 250 recognition when available.",
        "rt_certified_fresh" => "Certified Fresh recognition when available.",
        _ => "Poster card badge.",
    };

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

    private Border BuildThemeCard(string themeId, string displayName, string description,
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
        card.PointerEntered += (_, _) => themeService.PreviewTheme(themeId);
        card.PointerExited += (_, _) => themeService.CancelThemePreview();

        // Click handler
        card.Tapped += (_, _) =>
        {
            themeService.CommitThemePreview(themeId);
            ViewModel.UiTheme = themeId;
            _ = ViewModel.SaveUiThemeCommand.ExecuteAsync(null);
            BuildThemeCards();
            UpdateCurrentThemeDisplay();
        };

        return card;
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
                Text = "No libraries found.",
                Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
                Margin = new Thickness(0, 8, 0, 0),
            };
            LibraryCardsContainer.Children.Add(emptyText);
            return;
        }

        foreach (var card in ViewModel.LibraryCards)
        {
            LibraryCardsContainer.Children.Add(BuildLibraryCard(card));
        }
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

        var reorderPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 10, 0) };
        var moveUp = new Button { Content = new FontIcon { Glyph = "\uE70E", FontSize = 12 }, Padding = new Thickness(6), MinWidth = 28 };
        var moveDown = new Button { Content = new FontIcon { Glyph = "\uE70D", FontSize = 12 }, Padding = new Thickness(6), MinWidth = 28 };
        ToolTipService.SetToolTip(moveUp, "Move library up");
        ToolTipService.SetToolTip(moveDown, "Move library down");
        moveUp.Click += async (_, _) => await ViewModel.MoveLibraryAsync(vm.LibraryId, -1);
        moveDown.Click += async (_, _) => await ViewModel.MoveLibraryAsync(vm.LibraryId, 1);
        reorderPanel.Children.Add(moveUp);
        reorderPanel.Children.Add(moveDown);
        Grid.SetColumn(reorderPanel, 0);
        headerGrid.Children.Add(reorderPanel);

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
                Tag = hex,
            };
            ToolTipService.SetToolTip(swatch, label);
            swatch.Tapped += (_, _) =>
            {
                setter(hex);
                UpdateSubtitleColorSelection();
                UpdateSubtitlePreview();
            };
            host.Children.Add(swatch);
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
            if (child is Border border && border.Tag is string hex)
            {
                var isSelected = hex.Equals(selected, StringComparison.OrdinalIgnoreCase);
                border.BorderThickness = new Thickness(isSelected ? 3 : 1);
                border.BorderBrush = isSelected
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 255, 255, 255));
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
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // grip icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // title + type
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // actions

        // Grip icon
        var gripIcon = new FontIcon
        {
            Glyph = "\uE700",
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(gripIcon, 0);
        grid.Children.Add(gripIcon);

        // Title + type badge
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = section.Hidden
                ? (Brush)Application.Current.Resources["SecondaryTextBrush"]
                : (Brush)Application.Current.Resources["PrimaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        var typeBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
        };
        typeBadge.Child = new TextBlock
        {
            Text = section.SectionType,
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        };
        titleStack.Children.Add(typeBadge);

        Grid.SetColumn(titleStack, 1);
        grid.Children.Add(titleStack);

        // Action buttons
        var actionStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        // Move up
        var upBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE70E", FontSize = 12 },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
        };
        upBtn.Click += async (_, _) =>
        {
            ViewModel.MoveSectionUp(section);
            await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
        };
        actionStack.Children.Add(upBtn);

        // Move down
        var downBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE70D", FontSize = 12 },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
        };
        downBtn.Click += async (_, _) =>
        {
            ViewModel.MoveSectionDown(section);
            await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
        };
        actionStack.Children.Add(downBtn);

        // Visibility toggle
        var eyeIcon = section.Hidden ? "\uED1A" : "\uE7B3"; // Eye off : Eye
        var visBtn = new Button
        {
            Content = new FontIcon { Glyph = eyeIcon, FontSize = 14 },
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(6),
        };
        visBtn.Click += async (_, _) =>
        {
            ViewModel.ToggleSectionVisibility(section);
            await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
        };
        actionStack.Children.Add(visBtn);

        // Delete (only custom sections)
        // B58: Confirm before destructive removal. Web copy distinguishes
        // "Delete custom section?" from "Remove section?" — only IsCustom
        // sections expose this button so we use the "Delete custom section" copy.
        // Both custom and server-default rows can be removed from the active scope.
        {
            var delBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 14, Foreground = (Brush)Application.Current.Resources["ErrorBrush"] },
                Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                Padding = new Thickness(6),
            };
            delBtn.Click += async (_, _) =>
            {
                var dialog = new ContentDialog
                {
                    Title = section.IsCustom ? "Delete custom section" : "Remove section",
                    Content = section.IsCustom
                        ? $"Delete \"{section.Title}\"? This action cannot be undone."
                        : $"Remove \"{section.Title}\" from this screen? You can restore it by resetting to defaults.",
                    PrimaryButtonText = section.IsCustom ? "Delete" : "Remove",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Close,
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    ViewModel.RemoveSection(section);
                    await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
                }
            };
            actionStack.Children.Add(delBtn);
        }

        Grid.SetColumn(actionStack, 2);
        grid.Children.Add(actionStack);

        row.Child = grid;
        return row;
    }

    private void HomeSectionsSave_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
    }

    private async void HomeSectionsAdd_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<RecipeChoice> allChoices;
        try
        {
            var catalog = await App.Services.GetRequiredService<SettingsApi>().GetRecipeCatalogAsync();
            allChoices = catalog.Categories
                .SelectMany(category => category.Value)
                .Where(definition => !definition.AdminOnly)
                .SelectMany(definition => definition.Presets.Select(preset => new RecipeChoice(definition, preset)))
                .OrderBy(choice => choice.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error($"Could not load section recipes: {ex.Message}");
            return;
        }

        var search = new TextBox { PlaceholderText = "Search recipes…" };
        var list = new ListView { Height = 290, SelectionMode = ListViewSelectionMode.Single, DisplayMemberPath = nameof(RecipeChoice.DisplayName) };
        list.ItemsSource = allChoices;
        var description = new TextBlock { Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap };
        var title = new TextBox { Header = "Section title", PlaceholderText = "Section title" };
        var itemLimit = new NumberBox { Header = "Items", Minimum = 1, Maximum = 100, Value = 20, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var featured = new CheckBox { Content = "Featured section" };
        var content = new StackPanel { Spacing = 12, Width = 600 };
        content.Children.Add(search);
        content.Children.Add(list);
        content.Children.Add(description);
        content.Children.Add(title);
        content.Children.Add(itemLimit);
        content.Children.Add(featured);

        var dialog = new ContentDialog
        {
            Title = "Add a section",
            Content = content,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not RecipeChoice choice) return;
            dialog.IsPrimaryButtonEnabled = true;
            description.Text = choice.Description;
            title.Text = choice.DisplayName;
        };
        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            list.ItemsSource = string.IsNullOrWhiteSpace(query) ? allChoices : allChoices
                .Where(choice => choice.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) || choice.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || list.SelectedItem is not RecipeChoice selected)
            return;

        ViewModel.AddHomeSection(new SettingsSectionEntry
        {
            Id = Guid.NewGuid().ToString(),
            SectionType = selected.Definition.Type,
            Title = string.IsNullOrWhiteSpace(title.Text) ? selected.DisplayName : title.Text.Trim(),
            Featured = featured.IsChecked == true,
            ItemLimit = double.IsNaN(itemLimit.Value) ? 20 : (int)itemLimit.Value,
            Hidden = false,
            IsCustom = true,
            Customized = true,
            Position = ViewModel.HomeSections.Count,
            Config = new Dictionary<string, object>(selected.Preset.DefaultParams)
        });
        await ViewModel.SaveHomeSectionsCommand.ExecuteAsync(null);
    }

    private sealed record RecipeChoice(RecipeDefinition Definition, GalleryPreset Preset)
    {
        public string DisplayName => Preset.DisplayName;
        public string Description => Preset.DescriptionLong ?? Preset.DescriptionShort;
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

        // Plex OAuth sub-states
        PlexAuthPendingPanel.Visibility = ViewModel.PlexAuthPending ? Visibility.Visible : Visibility.Collapsed;
        PlexAuthPendingText.Text = string.IsNullOrEmpty(ViewModel.PlexAuthStatus)
            ? "Waiting for approval in browser..."
            : ViewModel.PlexAuthStatus;
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
    private Border BuildHistoryRunCard(HistoryImportRun run)
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
        card.Tapped += (_, _) =>
        {
            ViewModel.SelectRunForDisplay(run);
            RebuildRunSummaryCard();
            RebuildImportRunCards(); // re-render to highlight active
        };
        return card;
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

        // Metrics grid (6 boxes)
        var metricsGrid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (int i = 0; i < 6; i++) metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void AddMetric(int col, string label, int value, string? accentColor = null)
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
            Grid.SetColumn(box, col);
            metricsGrid.Children.Add(box);
        }
        AddMetric(0, "Fetched",  run.Fetched);
        AddMetric(1, "Matched",  run.Matched,         "#4ADE80");
        AddMetric(2, "Unmatched",run.Unmatched,       "#FBBF24");
        AddMetric(3, "Progress", run.ProgressUpdated, "#4ADE80");
        AddMetric(4, "History",  run.HistoryCreated,  "#4ADE80");
        AddMetric(5, "Skipped",  run.Skipped);
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

        await ViewModel.LoadImportTabCommand.ExecuteAsync(null);
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
        return dt.ToLocalTime().ToString("MMM d, yyyy");
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
            var cancel = new Button
            {
                Content = "Cancel",
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                Padding = new Thickness(12, 7, 12, 7),
                FontSize = 13,
                IsEnabled = !vm.IsBusy,
            };
            cancel.Click += (_, _) =>
            {
                vm.AuthSession = null;
                vm.ApiKey = "";
                vm.ApiKeyPromptVisible = false;
                RebuildWatchProviderCards();
            };
            actions.Children.Add(cancel);
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
                    Content = "Silo will stop importing history, syncing progress, and scrobbling playback for this provider.",
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
        stack.Children.Add(new TextBlock
        {
            Text = "Copy this code first",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{vm.DisplayName} will ask for it after the activation page opens.",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

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
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(session.VerificationUrl) { UseShellExecute = true });
            }
            catch { }
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
        stack.Children.Add(new TextBlock
        {
            Text = $"Paste your {vm.DisplayName} API key",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"Find it under your account settings on the {vm.DisplayName} site.",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

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
        keyBox.PasswordChanged += (_, _) => vm.ApiKey = keyBox.Password;
        row.Children.Add(keyBox);

        var connect = new Button
        {
            Content = vm.IsBusy ? "Connecting..." : "Connect",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 13,
            IsEnabled = !vm.IsBusy,
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
            return "Waiting for you to enter the activation code.";

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

    private async Task LoadWebhookConnectionsAsync()
    {
        WebhookConnectionsPanel.Children.Clear();
        try
        {
            var api = App.Services.GetRequiredService<WebhookSyncApi>();
            var connections = await api.GetConnectionsAsync();
            NoConnectionsText.Visibility = connections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var connection in connections)
            {
                var card = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14),
                };
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel { Spacing = 3 };
                info.Children.Add(new TextBlock { Text = connection.ServerName, FontWeight = FontWeights.SemiBold, FontSize = 14 });
                info.Children.Add(new TextBlock
                {
                    Text = $"{char.ToUpperInvariant(connection.Provider[0]) + connection.Provider[1..]} · {WebhookHealth(connection)} · {connection.ActorCount} actors",
                    FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                });
                row.Children.Add(info);
                var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 12 };
                Grid.SetColumn(chevron, 1); row.Children.Add(chevron);
                card.Content = row;
                card.Click += async (_, _) => await SelectWebhookConnectionAsync(connection);
                WebhookConnectionsPanel.Children.Add(card);
            }
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
        return "Waiting for first delivery";
    }

    private async void AddConnectionBtn_Click(object sender, RoutedEventArgs e)
    {
        try { _webhookProfiles = (await App.Services.GetRequiredService<AuthApi>().GetProfilesAsync()).Profiles; }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not load profiles: {ex.Message}"; return; }

        var provider = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        provider.Items.Add(new ComboBoxItem { Content = "Plex", Tag = "plex" });
        provider.Items.Add(new ComboBoxItem { Content = "Emby", Tag = "emby" });
        provider.Items.Add(new ComboBoxItem { Content = "Jellyfin", Tag = "jellyfin" });
        provider.SelectedIndex = 0;
        var name = new TextBox { PlaceholderText = "Server name" };
        var serverId = new TextBox { PlaceholderText = "Plex server ID" };
        var baseUrl = new TextBox { PlaceholderText = "https://plex.example.com" };
        var token = new PasswordBox { PlaceholderText = "Plex access token" };
        var profile = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Default Silo profile" };
        foreach (var p in _webhookProfiles) profile.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        if (profile.Items.Count > 0) profile.SelectedIndex = 0;
        var plexFields = new StackPanel { Spacing = 10 };
        plexFields.Children.Add(serverId); plexFields.Children.Add(baseUrl); plexFields.Children.Add(token);
        var form = new StackPanel { Width = 440, Spacing = 10 };
        form.Children.Add(provider); form.Children.Add(name); form.Children.Add(plexFields); form.Children.Add(profile);
        provider.SelectionChanged += (_, _) => plexFields.Visibility = (provider.SelectedItem as ComboBoxItem)?.Tag as string == "plex" ? Visibility.Visible : Visibility.Collapsed;

        var dialog = new ContentDialog { Title = "Add a connection", PrimaryButtonText = "Create", CloseButtonText = "Cancel", XamlRoot = XamlRoot, Content = form };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var providerValue = (provider.SelectedItem as ComboBoxItem)?.Tag as string ?? "plex";
        var profileId = (profile.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(profileId)) { ViewModel.ErrorMessage = "Server name and profile are required."; return; }
        var body = new Dictionary<string, object?>
        {
            ["provider"] = providerValue, ["server_name"] = name.Text.Trim(), ["default_profile_id"] = profileId,
        };
        if (providerValue == "plex")
        {
            body["server_id"] = serverId.Text.Trim(); body["base_url"] = baseUrl.Text.Trim(); body["access_token"] = token.Password;
        }
        try
        {
            var created = await App.Services.GetRequiredService<WebhookSyncApi>().CreateConnectionAsync(body);
            await LoadWebhookConnectionsAsync();
            await SelectWebhookConnectionAsync(created.Connection);
            WebhookUrlBox.Text = created.WebhookUrl;
            ViewModel.StatusMessage = "Webhook connection created";
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Failed to create webhook connection: {ex.Message}"; }
    }

    private async Task SelectWebhookConnectionAsync(WebhookSyncConnection connection)
    {
        _selectedWebhookConnection = connection;
        WebhookConnectionDetail.Visibility = Visibility.Visible;
        WebhookDetailName.Text = connection.ServerName;
        WebhookDetailHealth.Text = $"{connection.Provider.ToUpperInvariant()} · {WebhookHealth(connection)}";
        WebhookUrlBox.Text = connection.WebhookUrl ?? "";
        WebhookActorsHost.Children.Clear();
        WebhookEventsHost.Children.Clear();
        try
        {
            var api = App.Services.GetRequiredService<WebhookSyncApi>();
            if (_webhookProfiles.Count == 0) _webhookProfiles = (await App.Services.GetRequiredService<AuthApi>().GetProfilesAsync()).Profiles;
            var actorsTask = api.GetActorsAsync(connection.Id);
            var eventsTask = api.GetEventsAsync(connection.Id);
            await Task.WhenAll(actorsTask, eventsTask);
            BuildWebhookActors(actorsTask.Result);
            _webhookEvents = eventsTask.Result;
            _webhookEventsPage = 0;
            BuildWebhookEvents();
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not load connection details: {ex.Message}"; }
    }

    private void BuildWebhookActors(WebhookSyncActorsResponse response)
    {
        _webhookActorSelectors.Clear();
        var actors = response.Mappings.Select(mapping => (ExternalActorId: mapping.ExternalActorId, ExternalActorName: mapping.ExternalActorName, ProfileId: mapping.ProfileId))
            .Concat(response.DiscoveredActors
                .Where(actor => response.Mappings.All(mapping => mapping.ExternalActorId != actor.ExternalActorId))
                .Select(actor => (ExternalActorId: actor.ExternalActorId, ExternalActorName: actor.ExternalActorName, ProfileId: (string?)null)))
            .ToList();
        WebhookSaveActorsButton.Visibility = actors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (actors.Count == 0)
        {
            WebhookActorsHost.Children.Add(new TextBlock { Text = "No external actors discovered yet.", FontSize = 12 });
            return;
        }
        foreach (var actor in actors)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.Children.Add(new TextBlock { Text = actor.ExternalActorName, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.Items.Add(new ComboBoxItem { Content = "Ignore", Tag = "" });
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
        const int pageSize = 10;
        var pageCount = Math.Max(1, (int)Math.Ceiling(_webhookEvents.Count / (double)pageSize));
        _webhookEventsPage = Math.Clamp(_webhookEventsPage, 0, pageCount - 1);
        WebhookEventsPageLabel.Text = $"Page {_webhookEventsPage + 1} of {pageCount}";
        WebhookEventsPrevious.IsEnabled = _webhookEventsPage > 0;
        WebhookEventsNext.IsEnabled = _webhookEventsPage + 1 < pageCount;
        if (_webhookEvents.Count == 0)
        {
            WebhookEventsHost.Children.Add(new TextBlock { Text = "No deliveries received yet.", FontSize = 12 });
            return;
        }
        foreach (var item in _webhookEvents.Skip(_webhookEventsPage * pageSize).Take(pageSize))
        {
            var card = new Border { Background = (Brush)Application.Current.Resources["SurfaceBrush"], CornerRadius = new CornerRadius(10), Padding = new Thickness(12) };
            var stack = new StackPanel { Spacing = 3 };
            stack.Children.Add(new TextBlock { Text = $"{item.Outcome.ToUpperInvariant()} · HTTP {item.HttpStatus} · {DateTimeDisplay.FormatDateTime(item.ReceivedAt)}", FontSize = 11, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(new TextBlock { Text = item.Summary, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(item.ErrorMessage)) stack.Children.Add(new TextBlock { Text = item.ErrorMessage, FontSize = 11, Foreground = (Brush)Application.Current.Resources["ErrorBrush"], TextWrapping = TextWrapping.Wrap });
            card.Child = stack; WebhookEventsHost.Children.Add(card);
        }
    }

    private void WebhookEventsPrevious_Click(object sender, RoutedEventArgs e) { _webhookEventsPage--; BuildWebhookEvents(); }
    private void WebhookEventsNext_Click(object sender, RoutedEventArgs e) { _webhookEventsPage++; BuildWebhookEvents(); }

    private async void WebhookEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWebhookConnection == null) return;
        var name = new TextBox { Text = _selectedWebhookConnection.ServerName };
        var profile = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var p in _webhookProfiles) profile.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        SelectComboBoxByTag(profile, _selectedWebhookConnection.DefaultProfileId);
        var form = new StackPanel { Width = 400, Spacing = 10 };
        form.Children.Add(new TextBlock { Text = "Server name" }); form.Children.Add(name);
        form.Children.Add(new TextBlock { Text = "Default profile" }); form.Children.Add(profile);
        var dialog = new ContentDialog { Title = "Edit connection", Content = form, PrimaryButtonText = "Save", CloseButtonText = "Cancel", XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var updated = await App.Services.GetRequiredService<WebhookSyncApi>().UpdateConnectionAsync(_selectedWebhookConnection.Id,
                new Dictionary<string, object?>
                {
                    ["server_name"] = name.Text.Trim(),
                    ["default_profile_id"] = (profile.SelectedItem as ComboBoxItem)?.Tag as string,
                });
            await LoadWebhookConnectionsAsync();
            await SelectWebhookConnectionAsync(updated);
            ViewModel.StatusMessage = "Webhook connection updated";
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not update connection: {ex.Message}"; }
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
        var dialog = new ContentDialog { Title = "Delete connection?", Content = $"Delete {_selectedWebhookConnection.ServerName} and its routing history?", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await App.Services.GetRequiredService<WebhookSyncApi>().DeleteConnectionAsync(_selectedWebhookConnection.Id);
            _selectedWebhookConnection = null;
            WebhookConnectionDetail.Visibility = Visibility.Collapsed;
            await LoadWebhookConnectionsAsync();
            ViewModel.StatusMessage = "Webhook connection deleted";
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not delete connection: {ex.Message}"; }
    }

    private async void WebhookSaveActors_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWebhookConnection == null) return;
        var mappings = _webhookActorSelectors.Select(pair => new Dictionary<string, object?>
        {
            ["external_actor_id"] = pair.Key,
            ["external_actor_name"] = pair.Value.DataContext as string ?? pair.Key,
            ["continuum_profile_id"] = (pair.Value.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } id ? id : null,
        }).ToList();
        try
        {
            await App.Services.GetRequiredService<WebhookSyncApi>().UpdateActorsAsync(_selectedWebhookConnection.Id,
                new Dictionary<string, object?> { ["mappings"] = mappings });
            ViewModel.StatusMessage = "Actor routing saved";
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Could not save actor routing: {ex.Message}"; }
    }

    // ===== Profiles Tab =====

    private async Task LoadProfilesAsync()
    {
        ProfileCardsPanel.Children.Clear();
        _ = LoadHouseholdSessionsAsync();
        try
        {
            var authApi = App.Services.GetRequiredService<AuthApi>();
            var response = await authApi.GetProfilesAsync();
            var profiles = response.Profiles;

            if (profiles.Count == 0)
            {
                NoProfilesText.Visibility = Visibility.Visible;
                return;
            }
            NoProfilesText.Visibility = Visibility.Collapsed;

            var activeProfileId = App.Services.GetRequiredService<SiloPlayer.Core.Services.SettingsService>().Load().LastProfileId;

            foreach (var profile in profiles)
            {
                bool isActive = profile.Id == activeProfileId;
                var card = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"],
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(16, 12, 16, 12),
                };
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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
                            Text = "Active", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
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
                    Content = "Use",
                    Padding = new Thickness(11, 5, 11, 5),
                    Visibility = isActive ? Visibility.Collapsed : Visibility.Visible,
                };
                useBtn.Click += async (_, _) => await UseProfileAsync(profile);
                Grid.SetColumn(useBtn, 2);

                // Edit button — opens the profile editor dialog. Visible to
                // admins, to the user's own active profile, and to primary
                // profiles managing the household. We let the server enforce
                // exact gating; the desktop shows the button for all rows.
                var editBtn = new Button
                {
                    Width = 32, Height = 32, Padding = new Thickness(0),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(6),
                    Content = new FontIcon { Glyph = "\uE70F", FontSize = 12 }, // Edit
                };
                ToolTipService.SetToolTip(editBtn, "Edit profile");
                var profileForEdit = profile;
                editBtn.Click += async (_, _) => await ShowEditProfileDialogAsync(profileForEdit);
                Grid.SetColumn(editBtn, 3);

                // Delete button (blocked for active AND primary profiles).
                // Upstream c3f2da5: primary profiles can only be removed by
                // deleting the account.
                var deleteBlocked = isActive || profile.IsPrimary;
                var deleteBtn = new Button
                {
                    Width = 32, Height = 32, Padding = new Thickness(0),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(6),
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                    IsEnabled = !deleteBlocked,
                    Opacity = deleteBlocked ? 0.3 : 1.0,
                };
                ToolTipService.SetToolTip(deleteBtn,
                    profile.IsPrimary ? "The primary profile can only be removed by deleting the account."
                    : isActive ? "Can't delete active profile"
                    : "Delete profile");
                var capturedProfile = profile;
                deleteBtn.Click += async (_, _) =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Delete profile", Content = $"Delete \"{capturedProfile.Name}\"?",
                        PrimaryButtonText = "Delete", PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                        CloseButtonText = "Cancel", XamlRoot = this.XamlRoot, DefaultButton = ContentDialogButton.Close
                    };
                    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        try
                        {
                            var deleteApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AuthApi>();
                            await deleteApi.DeleteProfileAsync(capturedProfile.Id);
                            await LoadProfilesAsync();
                        }
                        catch { }
                    }
                };
                Grid.SetColumn(deleteBtn, 4);

                row.Children.Add(avatar);
                row.Children.Add(info);
                row.Children.Add(useBtn);
                row.Children.Add(editBtn);
                row.Children.Add(deleteBtn);
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
        var parts = new List<string>();
        parts.Add(profile.LibraryRestrictionsEnabled
            ? $"{profile.AllowedLibraryIds?.Count ?? 0} allowed libraries"
            : "All libraries");
        if (!string.IsNullOrWhiteSpace(profile.MaxContentRating)) parts.Add($"Up to {profile.MaxContentRating}");
        if (!string.IsNullOrWhiteSpace(profile.MaxPlaybackQuality)) parts.Add(profile.MaxPlaybackQuality);
        return string.Join(" · ", parts);
    }

    private async Task UseProfileAsync(SiloPlayer.Core.Models.Auth.Profile profile)
    {
        string? profileToken = null;
        if (profile.HasPin)
        {
            var pin = new PasswordBox { PlaceholderText = "Enter 4-digit PIN", MaxLength = 4 };
            var dialog = new ContentDialog { Title = profile.Name, Content = pin, PrimaryButtonText = "Confirm", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                var result = await App.Services.GetRequiredService<AuthApi>().VerifyPinAsync(profile.Id, pin.Password);
                if (!result.Valid) throw new InvalidOperationException("Incorrect PIN.");
                profileToken = result.ProfileToken;
            }
            catch (Exception ex)
            {
                App.Services.GetRequiredService<ToastService>().Error(ex.Message);
                return;
            }
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

    private async Task LoadHouseholdSessionsAsync()
    {
        HouseholdStreamsPanel.Children.Clear();

        try
        {
            var authApi = App.Services.GetRequiredService<AuthApi>();
            var sessions = await authApi.GetHouseholdSessionsAsync();
            HouseholdStreamsPanel.Visibility = Visibility.Visible;
            HouseholdStreamsPanel.Children.Add(BuildHouseholdStreamsCard(sessions));
        }
        catch (ApiException ex) when (ex.StatusCode is 403 or 404)
        {
            HouseholdStreamsPanel.Visibility = Visibility.Collapsed;
        }
        catch
        {
            HouseholdStreamsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private Border BuildHouseholdStreamsCard(IReadOnlyList<AdminSession> sessions)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
        };

        var stack = new StackPanel { Spacing = 12 };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Spacing = 2 };
        titleStack.Children.Add(new TextBlock
        {
            Text = "Active streams",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Playback currently running across this Silo household.",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        Grid.SetColumn(titleStack, 0);
        header.Children.Add(titleStack);

        var countBadge = new Border
        {
            Background = (Brush)Application.Current.Resources[sessions.Count > 0 ? "AccentBackgroundBrush" : "SurfaceBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 4, 10, 4),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = sessions.Count.ToString(),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources[sessions.Count > 0 ? "AccentBrush" : "SecondaryTextBrush"],
            },
        };
        Grid.SetColumn(countBadge, 1);
        header.Children.Add(countBadge);
        stack.Children.Add(header);

        if (sessions.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No active household streams.",
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

    private FrameworkElement BuildHouseholdStreamRow(AdminSession session)
    {
        var row = new Grid
        {
            ColumnSpacing = 12,
            Margin = new Thickness(0, 6, 0, 0),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new FontIcon
        {
            Glyph = session.IsPaused ? "\uE769" : "\uE768",
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 0, 0),
        });

        var textStack = new StackPanel { Spacing = 3 };
        var title = string.IsNullOrWhiteSpace(session.SeriesName) ? session.MediaTitle : session.SeriesName!;
        textStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(title) ? "Unknown media" : title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var subtitleParts = new List<string>();
        var episodeLabel = FormatEpisodeLabel(session);
        if (!string.IsNullOrWhiteSpace(episodeLabel)) subtitleParts.Add(episodeLabel);
        var profileLabel = string.IsNullOrWhiteSpace(session.ProfileName) ? session.Username : session.ProfileName!;
        if (!string.IsNullOrWhiteSpace(profileLabel)) subtitleParts.Add(profileLabel);
        if (!string.IsNullOrWhiteSpace(session.ClientIp)) subtitleParts.Add(session.ClientIp!);

        textStack.Children.Add(new TextBlock
        {
            Text = subtitleParts.Count == 0 ? "Playback session" : string.Join("  |  ", subtitleParts),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var metaParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.PlayMethod)) metaParts.Add(Labelize(session.PlayMethod));
        if (!string.IsNullOrWhiteSpace(session.SourceVideoResolution)) metaParts.Add(session.SourceVideoResolution!);
        if (!string.IsNullOrWhiteSpace(session.SourceVideoCodec)) metaParts.Add(session.SourceVideoCodec!);
        if (session.FileDuration is > 0) metaParts.Add(FormatStreamDuration(session.FileDuration.Value));
        metaParts.Add(session.HasPlaybackControl ? "Controllable" : "Viewing only");

        textStack.Children.Add(new TextBlock
        {
            Text = string.Join("  |  ", metaParts),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        var statusBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 3, 9, 3),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = session.IsPaused ? "Paused" : "Playing",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            },
        };
        Grid.SetColumn(statusBadge, 2);
        row.Children.Add(statusBadge);

        return row;
    }

    private static string FormatEpisodeLabel(AdminSession session)
    {
        var number = session.SeasonNumber.HasValue && session.EpisodeNumber.HasValue
            ? $"S{session.SeasonNumber.Value:00}E{session.EpisodeNumber.Value:00}"
            : "";
        var episodeName = string.IsNullOrWhiteSpace(session.EpisodeName) ? session.MediaTitle : session.EpisodeName!;
        if (string.IsNullOrWhiteSpace(episodeName)) return number;
        return string.IsNullOrWhiteSpace(number) ? episodeName : $"{number} - {episodeName}";
    }

    private static string FormatStreamDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{duration.Minutes}m";
    }

    private static string Labelize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var normalized = value.Replace('_', ' ').Trim().ToLowerInvariant();
        return normalized.Length == 1
            ? normalized.ToUpperInvariant()
            : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private async void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Profile name", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var pinBox = new PasswordBox { PlaceholderText = "PIN (optional, 4 digits)", CornerRadius = new CornerRadius(6), FontSize = 13 };

        var form = new StackPanel { Width = 380, Spacing = 14 };
        form.Children.Add(new TextBlock { Text = "Name", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
        form.Children.Add(nameBox);
        form.Children.Add(new TextBlock { Text = "PIN", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
        form.Children.Add(pinBox);

        var dialog = new ContentDialog
        {
            Title = "Create Profile", PrimaryButtonText = "Create", CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot, Content = form, DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            try
            {
                var authApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AuthApi>();
                await authApi.CreateProfileAsync(nameBox.Text.Trim());
                await LoadProfilesAsync();
            }
            catch { }
        }
    }

    /// <summary>
    /// Profile editor (webui parity, commit c3f2da5). Edits the profile's
    /// name and optionally sets a new PIN or clears the existing one. PIN
    /// clear is exposed only when the profile currently has a PIN.
    /// </summary>
    private async Task ShowEditProfileDialogAsync(SiloPlayer.Core.Models.Auth.Profile profile)
    {
        var nameBox = new TextBox
        {
            Text = profile.Name,
            PlaceholderText = "Profile name",
            CornerRadius = new CornerRadius(6),
            FontSize = 13,
        };
        var pinBox = new PasswordBox
        {
            PlaceholderText = profile.HasPin ? "New PIN (leave blank to keep)" : "PIN (optional, 4 digits)",
            CornerRadius = new CornerRadius(6),
            FontSize = 13,
        };
        var removePinToggle = new ToggleSwitch
        {
            IsOn = false,
            OnContent = "Remove existing PIN",
            OffContent = "Keep existing PIN",
        };

        var form = new StackPanel { Width = 380, Spacing = 14 };
        form.Children.Add(new TextBlock
        {
            Text = "Name", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        });
        form.Children.Add(nameBox);
        form.Children.Add(new TextBlock
        {
            Text = "PIN", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
        });
        form.Children.Add(pinBox);
        // "Remove PIN" toggle only makes sense if the profile currently has one.
        if (profile.HasPin)
        {
            form.Children.Add(removePinToggle);
            // When user flips the toggle on, disable the new-PIN field so they
            // can't accidentally submit both a new PIN and a clear request.
            removePinToggle.Toggled += (_, _) =>
            {
                pinBox.IsEnabled = !removePinToggle.IsOn;
                if (removePinToggle.IsOn) pinBox.Password = "";
            };
        }

        var dialog = new ContentDialog
        {
            Title = "Edit Profile",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = form,
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(nameBox.Text)) return;

        try
        {
            var authApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AuthApi>();
            string? pin = null;
            if (profile.HasPin && removePinToggle.IsOn)
            {
                pin = ""; // Empty string signals the server to clear the PIN.
            }
            else if (!string.IsNullOrEmpty(pinBox.Password))
            {
                pin = pinBox.Password;
            }
            await authApi.UpdateProfileAsync(profile.Id, nameBox.Text.Trim(), pin);
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
