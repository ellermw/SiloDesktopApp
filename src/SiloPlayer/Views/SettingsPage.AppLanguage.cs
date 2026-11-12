using SiloPlayer.Controls;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private AppLanguageSettingsGroup? _appLanguageGroup;

    /// <summary>The app-language group leads Accessibility, as on the web; it loads when the panel opens.</summary>
    private void InitializeAppLanguageSettings()
    {
        _appLanguageGroup = new AppLanguageSettingsGroup { GroupStyle = (Style)Resources["SettingsGroupStyle"] };
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
