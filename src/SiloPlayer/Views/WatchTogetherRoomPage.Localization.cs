using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

/// <summary>
/// Interface-language presentation for the Watch Party room, picker and
/// participants. Every state-dependent label is assigned through
/// <see cref="Localize"/>, which keeps the closure that worded it (with the room
/// data captured at that moment) and replays it after a language change. Room
/// codes, member names, titles, server row titles and wire states never change.
/// </summary>
public sealed partial class WatchTogetherRoomPage
{
    // Strong references on purpose: a WinUI element created in code can lose its
    // managed wrapper to GC while the native element stays on screen, so a weak
    // table would silently drop its refresh. Rebuilt containers release their
    // entries through a scope (see ForgetLocalized).
    private readonly Dictionary<DependencyObject, (string? Scope, Dictionary<string, Action> Slots)> _localized = [];
    private readonly List<(DateTime At, Func<string> Message)> _activity = [];
    private Func<string>? _compactActivity;

    internal static string T(string key, string fallback, IReadOnlyDictionary<string, object?>? arguments = null)
        => AppStrings.Get(key, arguments, fallback);
    private static string Upper(string text) => AppStrings.Uppercase(text);
    private static string Count(int count) => count.ToString("N0", AppStrings.FormatCulture);
    private static Dictionary<string, object?> CountArguments(int count) => new() { ["count"] = count, ["count_text"] = Count(count) };

    /// <summary>Assigns interface text now and again after every language change while the element lives.</summary>
    private void Localize(DependencyObject target, string slot, Action apply, string? scope = null)
    {
        apply();
        if (!_localized.TryGetValue(target, out var entry)) _localized[target] = entry = (scope, []);
        entry.Slots[slot] = apply;
    }

    private void LocalizeText(TextBlock target, Func<string> text, string? scope = null) => Localize(target, "text", () => target.Text = text(), scope);

    /// <summary>Releases the entries of a rebuilt container (shelves, seasons, episode rows, start label).</summary>
    private void ForgetLocalized(string scope)
    {
        foreach (var target in _localized.Where(entry => entry.Value.Scope == scope).Select(entry => entry.Key).ToList())
            _localized.Remove(target);
    }

    private void InitializeLocalization()
    {
        AppLocalization.Register(this, RefreshLocalizedText);
        Localize(_compactActivityLabel, "text", () => _compactActivityLabel.Text = _compactActivity?.Invoke() ?? T("web.watchTogether.rail.activity", "Activity"));
        LocalizeText(_modeOwner, () => T("web.watchTogether.strip.modeYou", "· you"));
        LocalizeText(_inviteLabel, () => T("web.watchTogether.strip.inviteLink", "Invite link"));
        Localize(ModeButton, "name", () => AutomationProperties.SetName(ModeButton, T("web.watchTogether.strip.changeMode", "Change how the room picks")));
        Localize(RailSheetButton, "name", () => AutomationProperties.SetName(RailSheetButton, T("web.watchTogether.rail.openLabel", "Open people and activity")));
        Localize(CopyInviteButton, "name", () => AutomationProperties.SetName(CopyInviteButton, T("web.watchTogether.strip.copyInvite", "Copy invite link")));
        Localize(StageCodeButton, "name", () => AutomationProperties.SetName(StageCodeButton, T("web.watchTogether.strip.copyCode", "Copy room code " + ViewModel.DisplayCode,
            new Dictionary<string, object?> { ["code"] = ViewModel.DisplayCode })));
        Localize(CopyCodeButton, "name", () => AutomationProperties.SetName(CopyCodeButton, T("web.watchTogether.strip.copyCode", "Copy room code " + ViewModel.DisplayCode,
            new Dictionary<string, object?> { ["code"] = ViewModel.DisplayCode })));
        Localize(LeaveButton, "name", () => AutomationProperties.SetName(LeaveButton, T("web.watchTogether.strip.leaveLabel", "Leave room")));
        Localize(ClearSearchButton, "name", () => AutomationProperties.SetName(ClearSearchButton, T("web.watchTogether.browse.hide", "Hide browse")));
        Localize(HostSearchBox, "name", () =>
        {
            AutomationProperties.SetName(HostSearchBox, T("web.watchTogether.browse.searchLabel", "Search movies and series"));
            HostSearchBox.PlaceholderText = T("web.watchTogether.browse.searchPlaceholder", "Search movies, series, episodes…");
        });
        LocalizeShelfChoices();
    }

    private static readonly (string Key, string English)[] ShelfChoices =
    [
        ("web.watchTogether.browse.chipAll", "All"), ("web.watchTogether.browse.chipMovies", "Movies"),
        ("web.watchTogether.browse.chipSeries", "Series"), ("web.watchTogether.browse.chipTogether", "Together"),
        ("web.watchTogether.browse.chipWatchlists", "Watchlists"), ("web.watchTogether.browse.chipRecent", "Recent"),
    ];

    private void LocalizeShelfChoices()
    {
        Localize(ShelfChipsPanel, "chips", () =>
        {
            // The never-shown ComboBox only holds the selected index (its items
            // are not rewritten, so a language change cannot disturb selection);
            // the chips are the visible, keyed filter.
            foreach (var chip in ShelfChipsPanel.Children.OfType<Button>())
                if (chip.Tag is int index && index < ShelfChoices.Length) chip.Content = T(ShelfChoices[index].Key, ShelfChoices[index].English);
            UpdateShelfChips();
            AutomationProperties.SetName(ShelfChipsPanel, T("web.watchTogether.browse.filterLabel", "Filter"));
        });
    }

    private void RefreshLocalizedText()
    {
        foreach (var (_, entry) in _localized.ToList())
            foreach (var apply in entry.Slots.Values.ToList()) apply();
        RenderActivity();
        UpdateConnectionUi();
        UpdateSuggestionsUi();
        if (ViewModel.Room is { } room) UpdateMembers(room.Members);
        foreach (var panel in _memberDots.Values.SelectMany(panels => panels)) DrawMemberDots(panel);
        UpdatePickerRows();
        UpdatePickerConfirmation();
        ViewModel.RefreshLocalizedText();
    }

    // ── Activity: retained structured entries re-render in the current language. ──
    private void AddActivity(Func<string> message)
    {
        _activity.Insert(0, (DateTime.Now, message));
        if (_activity.Count > 80) _activity.RemoveRange(80, _activity.Count - 80);
        _compactActivity = message;
        RenderActivity();
    }

    private void RenderActivity()
    {
        _compactActivityLabel.Text = _compactActivity?.Invoke() ?? T("web.watchTogether.rail.activity", "Activity");
        ActivityList.Children.Clear();
        foreach (var (at, message) in _activity)
            ActivityList.Children.Add(new TextBlock { Text = $"{at.ToString("t", AppStrings.FormatCulture)} · {message()}", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8 });
    }

    private static Func<string> MemberActivity(string key, string english, string who)
        => () => T(key, english, new Dictionary<string, object?> { ["who"] = who });

    // ── Shared participant/state wording. ──
    private static string MemberStateText(string state) => state switch
    {
        "watched" => T("web.watchTogether.dots.watched", "watched"),
        "in_progress" => T("web.watchTogether.dots.inProgress", "in progress"),
        "unseen" => T("web.watchTogether.dots.unseen", "unseen"),
        _ => state.Replace('_', ' '),
    };

    private static string NameList(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        2 => T("web.watchTogether.common.nameAnd", $"{names[0]} and {names[1]}", new Dictionary<string, object?> { ["first"] = names[0], ["second"] = names[1] }),
        // No upstream list pattern beyond two names; the comma join is a desktop fallback.
        _ => string.Join(", ", names),
    };

    private static string SpoilerRiskText(SiloPlayer.Core.Services.WatchPartyPickerPolicy.SpoilerRisk risk)
        => T("web.watchTogether.series.spoilerRisk",
            $"{NameList(risk.Names)} {(risk.Names.Count == 1 ? "hasn't" : "haven't")} seen E{risk.EpisodeNumber}. Heads up.",
            new Dictionary<string, object?> { ["count"] = risk.Names.Count, ["names"] = NameList(risk.Names), ["episode"] = risk.EpisodeNumber });

    private static string SeasonLabel(int seasonNumber)
        => seasonNumber == 0 ? T("web.watchTogether.common.specials", "Specials")
            : T("web.watchTogether.common.season", $"Season {seasonNumber}", new Dictionary<string, object?> { ["number"] = seasonNumber });

    private static string EpisodeCountText(int count)
        => T("web.detail.seasons.episodeCount", count == 1 ? "1 episode" : $"{count} episodes", CountArguments(count));

    private static string ModeLabel(bool vote)
        => vote ? T("watchParty.options.everyoneVotes", "Everyone votes") : T("web.watchTogether.common.hostPicks", "Host picks");

    private string ActionLabel()
        => !ViewModel.IsHost || ViewModel.IsVoteMode ? T("watchParty.button.suggest", "Suggest")
            : ViewModel.IsPlaying ? "Play now" // No upstream key for the in-playback switch action; English fallback.
            : T("web.watchTogether.candidate.stageIt", "Stage it");

    private void ShowTerminalState(Func<string> title, Func<string> description, Func<string> action)
    {
        RoomWorkspace.Visibility = Visibility.Collapsed;
        RoomContent.Visibility = Visibility.Collapsed;
        Localize(TerminalStatePanel, "text", () =>
        {
            TerminalStateTitle.Text = title();
            TerminalStateDescription.Text = description();
            TerminalStateAction.Content = action();
        });
        TerminalStatePanel.Visibility = Visibility.Visible;
        TerminalStateAction.Focus(FocusState.Programmatic);
    }

    /// <summary>A dialog opened from the room follows language and direction while it is open.</summary>
    private static void LocalizeDialog(ContentDialog dialog, Action refresh)
        => AppLocalization.Register(dialog, () => { AppLocalization.ApplyDirection(dialog); refresh(); });
}
