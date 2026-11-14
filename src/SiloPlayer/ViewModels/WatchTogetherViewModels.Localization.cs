using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.ViewModels;

/// <summary>
/// Retained interface text for Watch Party messages. A message keeps how it
/// was worded (explicit key, server problem or English fallback) so a language
/// change re-renders the visible message without repeating the request. Error
/// codes, statuses and room authority are unchanged; only display text moves.
/// </summary>
internal static class WatchPartyText
{
    public static string Get(string key, string fallback, IReadOnlyDictionary<string, object?>? arguments = null)
        => AppStrings.Get(key, arguments, fallback);

    /// <summary>A keyed action failure; server problem text follows for API failures.</summary>
    public static Func<string> ActionFailure(string key, string fallback, Exception exception) => () =>
    {
        var title = Get(key, fallback);
        return exception is ApiException api ? $"{title}: {ApiProblemText.Display(api)}" : title;
    };
}

public partial class WatchTogetherJoinViewModel
{
    private Func<string>? _errorSource;
    private string? _errorText;

    internal void SetLocalizedError(Func<string> text) => ErrorMessage = _errorText = (_errorSource = text)();

    /// <summary>Re-renders the visible message after a language change.</summary>
    public void RefreshLocalizedText()
    {
        if (_errorSource != null && _errorText != null && ErrorMessage == _errorText)
            ErrorMessage = _errorText = _errorSource();
    }

    private void SetJoinFailure(Exception exception)
    {
        if (exception is ApiException api)
            SetLocalizedError(api.StatusCode switch
            {
                404 => () => WatchPartyText.Get("web.watchTogether.common.roomNotFound", "Room not found."),
                410 => () => WatchPartyText.Get("web.watchTogether.common.roomInactive", "That room is no longer active."),
                _ => () => ApiProblemText.Display(api),
            });
        else SetLocalizedError(() => WatchPartyText.Get("web.watchTogether.hub.joinFailed", "Failed to join room."));
    }
}

public partial class WatchTogetherRoomViewModel
{
    private Func<string>? _errorSource, _noticeSource;
    private string? _errorText, _noticeText;

    internal void SetLocalizedError(Func<string> text) => ErrorMessage = _errorText = (_errorSource = text)();
    internal void SetLocalizedNotice(Func<string> text) => NoticeMessage = _noticeText = (_noticeSource = text)();

    /// <summary>Re-renders retained room messages after a language change.</summary>
    public void RefreshLocalizedText()
    {
        if (_errorSource != null && _errorText != null && ErrorMessage == _errorText)
            ErrorMessage = _errorText = _errorSource();
        if (_noticeSource != null && _noticeText != null && NoticeMessage == _noticeText)
            NoticeMessage = _noticeText = _noticeSource();
    }
}
