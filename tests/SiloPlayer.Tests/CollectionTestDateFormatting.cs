namespace SiloPlayer.Helpers;

// Collection view-model tests isolate the WinUI settings lookup used only for
// a display date. These tests exercise requests/rules, not date presentation.
internal static class DateTimeDisplay
{
    public static string FormatDate(DateTimeOffset value, bool medium = false) => value.ToString("yyyy-MM-dd");
}
