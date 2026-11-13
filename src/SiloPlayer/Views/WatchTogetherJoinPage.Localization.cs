using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

/// <summary>
/// Interface-language presentation for the Watch Party hub. Static copy uses
/// <see cref="Localized"/> keys in XAML; state-dependent text (headline,
/// emphasized hint, recent-room statuses and the view model's message) is
/// re-rendered here. Room codes are data and never change.
/// </summary>
public sealed partial class WatchTogetherJoinPage
{
    private bool _autoJoining;
    // Strong references: code-created rows may otherwise lose their managed wrapper to GC
    // while still on screen. The list is cleared whenever the recent list is rebuilt.
    private readonly Dictionary<Button, Func<string>> _recentRowText = [];

    private static string T(string key, string fallback) => AppStrings.Get(key, fallback: fallback);
    private static string RecentChecking() => T("web.watchTogether.hub.recentChecking", "Checking…");
    private static string RecentEnded() => T("web.watchTogether.hub.recentEnded", "Ended");
    private static string RecentRejoin() => T("web.watchTogether.hub.rejoin", "Rejoin");
    private static string RecentUnknown() => T("web.watchTogether.hub.recentUnknown", "Status unavailable") + " · " + RecentRejoin();

    private void InitializeLocalization() => AppLocalization.Register(this, RefreshLocalizedText);

    private void RefreshLocalizedText()
    {
        EyebrowText.Text = AppStrings.Uppercase(T("watchParty.title", "Watch Party"));
        ApplyHeadline();
        ApplyFromTitleHint();
        foreach (var (row, text) in _recentRowText) row.Content = text();
        ViewModel.RefreshLocalizedText();
    }

    private void ApplyHeadline()
        => HeadlineText.Text = _autoJoining
            ? T("web.watchTogether.hub.joiningTitle", "Joining Watch Party")
            : T("web.watchTogether.hub.headline", "Watch together, wherever everyone is.");

    private void SetRecentRow(Button row, string code, Func<string> status)
    {
        string Text() => $"{code} · {status()}";
        _recentRowText[row] = Text;
        row.Content = Text();
    }

    /// <summary>
    /// The catalog hint carries the menu item as a rich-text element, which the
    /// formatter prints as plain text. Emphasize it only when the translated
    /// hint contains the translated menu title; otherwise show plain text.
    /// </summary>
    private void ApplyFromTitleHint()
    {
        var hint = T("web.watchTogether.hub.fromTitleHint", "Already know what you want to watch? Open its page and choose Start a party with this from the menu.");
        var item = T("web.watchTogether.startSheet.title", "Start a party with this");
        FromTitleHint.Inlines.Clear();
        var index = hint.IndexOf(item, StringComparison.Ordinal);
        if (index < 0) { FromTitleHint.Inlines.Add(new Run { Text = hint }); return; }
        FromTitleHint.Inlines.Add(new Run { Text = hint[..index] });
        FromTitleHint.Inlines.Add(new Run { Text = item, Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] });
        FromTitleHint.Inlines.Add(new Run { Text = hint[(index + item.Length)..] });
    }
}
