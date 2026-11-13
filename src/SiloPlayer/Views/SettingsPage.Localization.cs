using SiloPlayer.Core.Localization;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

/// <summary>
/// Interface language for the retained Settings page. Static XAML labels carry
/// explicit <c>loc:Localized</c> keys; this partial refreshes the text Settings
/// builds in code (directory cards, sidebar entries, search status, section
/// headings) when the interface language changes while the page is retained.
/// Routing tags, saved values and English search terms never change.
/// </summary>
public sealed partial class SettingsPage
{
    private IDisposable? _localeRegistration;
    private string? _settingsLocaleLanguage;
    private readonly List<Action> _localeRefreshers = [];

    /// <summary>Catalog text for a fixed Settings label; <paramref name="english"/> is the fallback.</summary>
    private static string L(string key, string english) => AppStrings.Get(key, fallback: english);

    private static string L(string key, string english, IReadOnlyDictionary<string, object?> arguments)
        => AppStrings.Get(key, arguments, english);

    /// <summary>Message arguments; display values only (names, preformatted counts), never wire values.</summary>
    internal static Dictionary<string, object?> Args(params (string Name, object? Value)[] values)
    {
        var arguments = new Dictionary<string, object?>(values.Length);
        foreach (var (name, value) in values) arguments[name] = value;
        return arguments;
    }

    /// <summary>Formats a displayed count in the interface's number format.</summary>
    private static string Count(long value) => value.ToString("N0", AppStrings.FormatCulture);

    private static string SettingsMatchCount(int matches)
        => AppStrings.Get("web.settings.search.matchCount", new Dictionary<string, object?> { ["count"] = matches, ["count_text"] = Count(matches) },
            $"{matches} {(matches == 1 ? "match" : "matches")}");

    /// <summary>Localized label and description plus the stable English tag and terms, so either language finds the section.</summary>
    private static string SettingsSearchText(string tag, string label, string description, string englishTerms)
        => $"{label} {description} {tag} {englishTerms}";

    /// <summary>Runs <paramref name="refresh"/> again whenever the interface language changes while Settings is retained.</summary>
    private void OnLocaleRefresh(Action refresh) => _localeRefreshers.Add(refresh);

    private void InitializeSettingsLocalization()
    {
        // A flyout owner is set in code, not keyed: its content is rewritten only on construction and on a real language change.
        HomeLayoutMoreButton.Content = L("web.ui.pageMoreMenu.more", "More");
        _localeRegistration ??= AppLocalization.Register(this, RefreshSettingsLocale);
    }

    private void RefreshSettingsLocale()
    {
        RefreshInlineChoices();
        foreach (var (group, key, english) in new (Panel Group, string Key, string English)[]
        {
            (PlaybackNavGroup, "web.settings.nav.group.playback", "Playback"),
            (AppearanceNavGroup, "web.settings.nav.group.appearance", "Appearance"),
            (HomeDiscoveryNavGroup, "web.settings.nav.group.homeDiscovery", "Home & Discovery"),
            (ConnectionsNavGroup, "web.settings.nav.group.connections", "Connections"),
            (AccountNavGroup, "web.settings.nav.group.account", "Account"),
        })
            if (group.Children.FirstOrDefault() is TextBlock heading)
                heading.Text = AppStrings.Uppercase(L(key, english));

        // Labels whose initial XAML text code later rewrites.
        RefreshRetained(SynchronizeQualityDescription);
        RefreshRetained(() => UpdateAccountPasswordPanel());
        RefreshRetained(RefreshWebhookActionLabels);

        // Text built in code already used the language of its first build; rebuild
        // only after the language actually changes (not on the first load, where an
        // in-flight load still owns the loading states).
        var previous = _settingsLocaleLanguage;
        _settingsLocaleLanguage = AppStrings.Language;
        if (previous is null || previous == AppStrings.Language) return;

        HomeLayoutMoreButton.Content = L("web.ui.pageMoreMenu.more", "More");
        // The directory, its cards and the shell's sidebar entries are rebuilt from keys.
        BuildSettingsOverview();
        SettingsHeaderSubtitle.Text = _showingSettingsOverview
            ? AppStrings.Get("web.settings.layout.subtitle")
            : "Manage your playback preferences, libraries, and display options.";
        SettingsSearchBox_TextChanged(SettingsSearchBox, null!);

        // Retained text built in code: re-render from the current view-model state.
        // Selections, drafts and saved values are untouched; only labels change.
        RefreshRetained(RebuildSessionCards);
        RefreshRetained(RenderAccountSignIn);
        RefreshRetained(RenderNotInterested);
        RefreshRetained(BuildAccessibilityControls);
        RefreshRetained(UpdateDateTimePreview);
        RefreshRetained(SyncSubtitlePreferenceStatus);
        RefreshRetained(BuildInterfacePrimaryMenu);
        RefreshRetained(() => { foreach (var card in ViewModel.LibraryCards) card.RefreshLocale(); RebuildLibraryCards(); });
        RefreshRetained(RebuildHomeSectionItems);
        RefreshRetained(RebuildRunSummaryCard);
        RefreshRetained(RebuildImportRunCards);
        RefreshRetained(RebuildWatchProviderCards);
        RefreshRetained(BuildWebhookEvents);
        if (_cardOverlayDraft is not null)
        {
            RefreshRetained(BuildCardOverlayGeneralControls);
            RefreshRetained(BuildCardOverlayControls);
            RefreshRetained(BuildCardOverlayPresetCards);
        }
        RefreshRetained(SynchronizeUpdateSettings);
        foreach (var combo in new[] { SpokenLanguageComboBox, SubtitleLanguageComboBox })
            if (combo.Items.FirstOrDefault() is ComboBoxItem { Tag: "" } unset)
                unset.Content = ReferenceEquals(combo, SpokenLanguageComboBox)
                    ? L("settingsManifest.playback.audio_language.unsetLabel", "No preference")
                    : L("settingsManifest.playback.subtitle_language.unsetLabel", "None");
        if (MetadataLanguageComboBox.Items.Count > 1)
        {
            ((ComboBoxItem)MetadataLanguageComboBox.Items[0]).Content = L("web.settings.metadataLanguage.libraryDefault", "Library default");
            ((ComboBoxItem)MetadataLanguageComboBox.Items[1]).Content = L("web.settings.metadataLanguage.originalLanguage", "Original language");
        }
        // Read-only sections built from a server read re-read only while shown; each
        // loader keeps its own owner checks and late-response retirement.
        // Sections holding in-progress edits or transfers
        // (Your Devices, Your Data, navigation/card drafts) keep their text until reopened.
        if (ConnectAppsPanel.Visibility == Visibility.Visible) _ = LoadConnectAppsAsync();
        if (ProfilesPanel.Visibility == Visibility.Visible) _ = LoadProfilesAsync();
        if (WebhookSyncPanel.Visibility == Visibility.Visible && WebhookAvailable) RefreshWebhookLocale();
        if (PersonalizePanel.Visibility == Visibility.Visible) _ = LoadPersonalizeSummaryAsync();
        if (RequestsPanel.Visibility == Visibility.Visible) _ = LoadRequestPreferenceAsync();
        foreach (var refresh in _localeRefreshers) refresh();
    }

    private void RefreshWebhookLocale()
    {
        // Update display labels in place: loading connection details would overwrite
        // the unsaved name, default profile, and user mapping selections.
        if (WebhookConnectionProfileCombo.Items.FirstOrDefault() is Microsoft.UI.Xaml.Controls.ComboBoxItem defaultProfile)
            defaultProfile.Content = L("web.settings.webhooks.detail.noDefaultProfile", "No default profile");
        foreach (var selector in _webhookActorSelectors.Values)
            if (selector.Items.FirstOrDefault() is Microsoft.UI.Xaml.Controls.ComboBoxItem ignore)
                ignore.Content = L("web.settings.webhooks.mapping.ignore", "Ignore this user");
        if (_selectedWebhookConnection is { } connection)
        {
            WebhookDetailHealth.Text = L("web.settings.webhooks.detail.description", "{provider} webhook endpoint", Args(("provider", FormatWebhookProvider(connection.Provider))));
            BuildWebhookSetupInstructions(connection.Provider);
        }
        RefreshWebhookActionLabels();
        BuildWebhookEvents();
    }

    /// <summary>One retained re-render; a failure leaves that section as it was and never blocks the others.</summary>
    private static void RefreshRetained(Action refresh)
    {
        try { refresh(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine("Settings locale refresh skipped a section: " + ex.Message);
        }
    }

    private bool _refreshingInlineChoices;
    private void RefreshInlineChoices()
    {
        var previous = _refreshingInlineChoices;
        _refreshingInlineChoices = true;
        try
        {
            SiloPlayer.Services.Localized.RefreshItems(InterfacePosterSizeCombo);
            SiloPlayer.Services.Localized.RefreshItems(QualityComboBox);
            SiloPlayer.Services.Localized.RefreshItems(MaxBitrateComboBox);
            SiloPlayer.Services.Localized.RefreshItems(SubtitleModeComboBox);
            SiloPlayer.Services.Localized.RefreshItems(SubtitleFontSizeComboBox);
            SiloPlayer.Services.Localized.RefreshItems(SubtitleFontFamilyComboBox);
            SiloPlayer.Services.Localized.RefreshItems(SubtitleBgStyleComboBox);
            SiloPlayer.Services.Localized.RefreshItems(SubtitlePositionComboBox);
            SiloPlayer.Services.Localized.RefreshItems(NextUpModeComboBox);
            SiloPlayer.Services.Localized.RefreshItems(HomeScopeComboBox);
            SiloPlayer.Services.Localized.RefreshItems(WebhookEventOutcomeFilter);
        }
        finally { _refreshingInlineChoices = previous; }
    }
}
