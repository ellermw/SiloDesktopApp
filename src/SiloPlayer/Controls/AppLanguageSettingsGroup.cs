using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

/// <summary>
/// Settings › Accessibility › Language: the profile's <c>ui.app_language</c>.
/// It edits the profile's own value and then clears this device's value, so the
/// choice follows the person across devices. The list offers "Match device",
/// the shipped languages by their own names, and a stored language this app
/// does not ship (by name), so saving here never clears another app's choice.
/// Hidden on servers that do not know the setting.
/// </summary>
public sealed partial class AppLanguageSettingsGroup : UserControl
{
    // Catalog gaps: silo-translations has these only in web wording ("browser",
    // "the web app"). Until it has desktop-neutral keys they show in English.
    internal const string UnavailableEnglish = "This language isn’t available in this app yet, so the app follows your device here.";
    internal const string DeviceClearFailedEnglish = "Saved, but this device still uses its own language.";
    internal const string EarlierSaveDeviceClearPendingEnglish = "Your earlier choice was saved, but this device still uses its own language.";

    private readonly AppLanguagePreference _preference;
    private readonly AppLocaleCoordinator _coordinator;
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _description = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _label = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _unavailable = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Button _retry = new() { Visibility = Visibility.Collapsed };
    private ApiRequestContext? _owner;
    private SettingsIdentityContext? _deviceClearOwner;
    private string? _storedTag;
    private bool _populating;
    private bool _saving;
    private long _loadGeneration;

    public AppLanguageSettingsGroup()
        : this(App.Services.GetRequiredService<AppLanguagePreference>(), App.Services.GetRequiredService<AppLocaleCoordinator>()) { }

    public AppLanguageSettingsGroup(AppLanguagePreference preference, AppLocaleCoordinator coordinator)
    {
        _preference = preference;
        _coordinator = coordinator;
        Picker = new ComboBox { MinWidth = 256, HorizontalAlignment = HorizontalAlignment.Left };
        Picker.SelectionChanged += OnSelectionChanged;
        _retry.Click += async (_, _) => await SaveAsync(_storedTag, clearDeviceOnly: true);
        var secondary = Application.Current.Resources.TryGetValue("SecondaryTextBrush", out var brush) ? brush as Brush : null;
        _description.Foreground = secondary;
        _unavailable.Foreground = secondary;
        var panel = new StackPanel { Spacing = 12, Children = { _title, _description, _label, Picker, _unavailable, _status, _retry } };
        Group = new Border { Child = panel };
        Content = Group;
        Visibility = Visibility.Collapsed;
        AppLocalization.Register(this, RefreshText);
        Loaded += (_, _) => { _coordinator.Loaded -= OnCoordinatorLoaded; _coordinator.Loaded += OnCoordinatorLoaded; };
        Unloaded += (_, _) => _coordinator.Loaded -= OnCoordinatorLoaded;
    }

    public ComboBox Picker { get; }
    public Border Group { get; }
    public TextBlock UnavailableNote => _unavailable;
    public TextBlock StatusText => _status;
    public TextBlock TitleText => _title;
    public TextBlock LabelText => _label;

    /// <summary>The Settings page's group style, applied to the outer border.</summary>
    public Style? GroupStyle { get => Group.Style; set => Group.Style = value; }

    /// <summary>Tags in picker order; null is Match device.</summary>
    public IReadOnlyList<string?> OfferedTags => Picker.Items.OfType<ComboBoxItem>().Select(item => item.Tag as string).ToArray();

    /// <summary>Loads the current owner's value unless it is already shown. Call when the panel becomes visible.</summary>
    public Task EnsureLoadedAsync()
    {
        var context = _preference.CaptureContext();
        if (_owner is { } owner && owner == context) return Task.CompletedTask;
        return LoadAsync(context);
    }

    private async Task LoadAsync(ApiRequestContext context)
    {
        var generation = ++_loadGeneration;
        var settingsOwner = _preference.CaptureSettingsOwner();
        if (_deviceClearOwner != settingsOwner) _deviceClearOwner = null;
        _owner = null;
        if (context.ProfileId == null) { Visibility = Visibility.Collapsed; return; }
        (bool Supported, string? ProfileTag) state;
        try
        {
            state = await _preference.ReadPickerAsync(context);
            if (_deviceClearOwner == settingsOwner && !await _preference.HasDeviceValueAsync(context)
                && generation == _loadGeneration && _preference.IsCurrentContext(context)) _deviceClearOwner = null;
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or OperationCanceledException)
        {
            if (generation == _loadGeneration)
            {
                if (_deviceClearOwner == settingsOwner && _preference.IsCurrentContext(context))
                {
                    _owner = context;
                    ShowStatus(_statusKind == StatusKind.SaveFailedDevicePending ? _statusKind : StatusKind.DeviceClear);
                }
                else Visibility = Visibility.Collapsed;
            }
            return;
        }
        if (generation != _loadGeneration || !_preference.IsCurrentContext(context)) return;
        _owner = context;
        Visibility = state.Supported ? Visibility.Visible : Visibility.Collapsed;
        _storedTag = state.ProfileTag;
        if (_deviceClearOwner == settingsOwner) ShowStatus(_statusKind == StatusKind.SaveFailedDevicePending ? _statusKind : StatusKind.DeviceClear);
        else HideStatus();
        Populate();
    }

    private void OnCoordinatorLoaded(object? sender, AppLanguageRead read)
    {
        // Another device or a realtime change updated the setting: show the stored value again.
        if (!_saving && _preference.IsCurrentContext(read.Context)) _ = LoadAsync(read.Context);
    }

    private void Populate()
    {
        _populating = true;
        try
        {
            Picker.Items.Clear();
            Picker.Items.Add(new ComboBoxItem { Tag = null, Content = AppStrings.Get("settings.appLanguage.matchDevice") });
            var shippedTag = AppLanguageMatcher.MatchShipped(_storedTag);
            if (_storedTag != null && shippedTag == null)
                Picker.Items.Add(new ComboBoxItem { Tag = _storedTag, Content = StoredLanguageName(_storedTag) });
            foreach (var language in MessageCatalog.ShippedLanguages)
                Picker.Items.Add(new ComboBoxItem
                {
                    Tag = language.Tag,
                    // An endonym renders in its own language's fonts and direction.
                    Content = new TextBlock
                    {
                        Text = language.Endonym,
                        Language = language.Tag,
                        FlowDirection = language.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    },
                });
            Picker.SelectedIndex = IndexOf(shippedTag ?? _storedTag);
            _unavailable.Visibility = _storedTag != null && shippedTag == null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally { _populating = false; }
    }

    private int IndexOf(string? tag)
    {
        for (var index = 0; index < Picker.Items.Count; index++)
            if (Picker.Items[index] is ComboBoxItem { Tag: var itemTag } && Equals(itemTag as string, tag)) return index;
        return 0;
    }

    private static string StoredLanguageName(string tag)
    {
        var language = AppLanguageMatcher.LanguageOf(tag);
        var name = language.Length == 0 ? tag : AppStrings.Get("language." + language, fallback: tag);
        return string.Equals(language, tag, StringComparison.OrdinalIgnoreCase) || name == tag ? name : $"{name} ({tag})";
    }

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_populating || Picker.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag as string;
        if (tag == (AppLanguageMatcher.MatchShipped(_storedTag) ?? _storedTag) && _deviceClearOwner == null) return;
        await SaveAsync(tag);
    }

    private async Task SaveAsync(string? tag, bool clearDeviceOnly = false)
    {
        // One save at a time: two in flight could land in either order.
        if (_saving || _owner is not { } context) return;
        if (!_preference.IsCurrentContext(context)) { _ = EnsureLoadedAsync(); return; }
        var settingsOwner = _preference.CaptureSettingsOwner();
        if (clearDeviceOnly && _deviceClearOwner != settingsOwner) return;
        _saving = true;
        Picker.IsEnabled = false;
        HideStatus();
        try
        {
            var result = clearDeviceOnly
                ? await _preference.ClearDeviceValueAsync(settingsOwner)
                : await _preference.SaveAsync(context, tag);
            if (_preference.CaptureSettingsOwner() != settingsOwner) return;
            _deviceClearOwner = result.DeviceValueCleared ? null : settingsOwner;
            if (!_preference.IsCurrentContext(context)) return;
            _storedTag = tag;
            Populate();
            if (!result.DeviceValueCleared) ShowStatus(StatusKind.DeviceClear);
            await _coordinator.RefreshAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or OperationCanceledException or ArgumentException)
        {
            if (!_preference.IsCurrentContext(context)) return;
            // Say what this attempt did: a new choice that failed is not the earlier device clear.
            if (_deviceClearOwner == settingsOwner) ShowStatus(clearDeviceOnly ? StatusKind.DeviceClear : StatusKind.SaveFailedDevicePending);
            else ShowStatus(StatusKind.SaveFailed);
            Populate();
        }
        finally
        {
            _saving = false;
            Picker.IsEnabled = true;
            if (!_preference.IsCurrentContext(context)) _ = EnsureLoadedAsync();
        }
    }

    private enum StatusKind { None, DeviceClear, SaveFailed, SaveFailedDevicePending }
    private StatusKind _statusKind;

    private void ShowStatus(StatusKind kind)
    {
        _statusKind = kind;
        _status.Text = kind switch
        {
            StatusKind.DeviceClear => DeviceClearFailedEnglish,
            StatusKind.SaveFailedDevicePending => AppStrings.Get("web.settings.accessibility.appLanguageSaveFailed") + ". " + EarlierSaveDeviceClearPendingEnglish,
            _ => AppStrings.Get("web.settings.accessibility.appLanguageSaveFailed"),
        };
        _status.Visibility = Visibility.Visible;
        // Retry only finishes the earlier device clear; it never resends a profile value.
        _retry.Visibility = kind is StatusKind.DeviceClear or StatusKind.SaveFailedDevicePending ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideStatus()
    {
        _statusKind = StatusKind.None;
        _status.Visibility = Visibility.Collapsed;
        _retry.Visibility = Visibility.Collapsed;
    }

    private void RefreshText()
    {
        _title.Text = AppStrings.Get("web.settings.accessibility.languageTitle");
        _description.Text = AppStrings.Get("settingsManifest.ui.app_language.description");
        _label.Text = AppStrings.Get("settings.control.appLanguage");
        _retry.Content = AppStrings.Get("common.tryAgain");
        _unavailable.Text = UnavailableEnglish;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Picker, _label.Text);
        // Labels follow the language; the selection stays the stored value.
        if (Picker.Items.Count > 0) Populate();
        if (_statusKind != StatusKind.None) ShowStatus(_statusKind);
    }
}
