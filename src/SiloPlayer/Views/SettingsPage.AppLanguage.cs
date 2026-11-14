using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Controls;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private AppLanguageSettingsGroup? _appLanguageGroup;

    /// <summary>The app-language group leads Accessibility, as on the web; it loads when the panel opens.</summary>
    private void InitializeAppLanguageSettings()
    {
        // As the window does for its coordinator: a host without the interface-language
        // services shows Accessibility without this group instead of failing the page.
        if (App.Services.GetService<AppLanguagePreference>() is not { } preference
            || App.Services.GetService<AppLocaleCoordinator>() is not { } coordinator) return;
        _appLanguageGroup = new AppLanguageSettingsGroup(preference, coordinator) { GroupStyle = (Style)Resources["SettingsGroupStyle"] };
        AccessibilityPanel.Children.Insert(1, _appLanguageGroup);
    }

    private void ActivateAppLanguageSettings()
    {
        if (_appLanguageGroup is { } group) _ = group.EnsureLoadedAsync();
    }

    /// <summary>Returns a renamed row to the administrator's title, with that title's catalog key for display.</summary>
    private static void RestoreDefaultTitle(SiloPlayer.Core.Models.Home.SettingsSectionEntry section)
    {
        section.Title = section.DefaultTitle!;
        section.TitleKey = section.DefaultTitleKey;
        section.TitleArgs = SiloPlayer.Core.Localization.SectionTitles.CopyArguments(section.DefaultTitleArgs);
        section.KeyedTitle = section.DefaultTitleKey == null ? null : section.DefaultTitle;
    }
}
