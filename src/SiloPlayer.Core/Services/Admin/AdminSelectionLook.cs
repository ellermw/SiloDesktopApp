namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The selected look of the account-resource pages' segmented controls, saved views and profile tabs: the official hover fill under the foreground
/// label, whatever the accent colour is. In a Windows contrast theme the app maps that fill and that text to the window and window text colours, which
/// would make a selected tab identical to the page; the platform's own highlight pair (highlight fill, highlight text) is what tells the selected option
/// apart there, so nothing is replaced.
/// </summary>
public static class AdminSelectionLook
{
    public static readonly IReadOnlyList<string> CheckedBackgroundKeys = ["ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed"];
    public static readonly IReadOnlyList<string> CheckedForegroundKeys = ["ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed"];
    public static readonly IReadOnlyList<string> CheckedBorderKeys = ["ToggleButtonBorderBrushChecked", "ToggleButtonBorderBrushCheckedPointerOver", "ToggleButtonBorderBrushCheckedPressed"];

    /// <summary>Whether the checked state's fill, text and border are replaced with the official look: never in a Windows contrast theme.</summary>
    public static bool ReplacesCheckedLook(bool systemContrast) => !systemContrast;

    /// <summary>The resource keys written onto a toggle button for its checked state under the given theme.</summary>
    public static IReadOnlyList<string> KeysReplaced(bool systemContrast) =>
        ReplacesCheckedLook(systemContrast) ? [.. CheckedBackgroundKeys, .. CheckedForegroundKeys, .. CheckedBorderKeys] : [];
}
