using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

public sealed record SubtitleAiDialogSelection(
    string Mode,
    int SourceIndex,
    string SourceLanguage,
    string TargetLanguage);

/// <summary>
/// In-player AI subtitle dialog matching the current WebUI modal. Submission
/// remains inside the modal so API failures do not tear down the player or
/// force the user to reopen the workflow.
/// </summary>
public sealed partial class SubtitleAiDialog : ContentDialog
{
    private readonly IReadOnlyList<SubtitleTrackInfo> _subtitleTracks;
    private readonly IReadOnlyList<AudioTrackInfo> _audioTracks;
    private readonly bool _canTranslate;
    private readonly bool _canTranscribe;
    private SubtitleAiQuota? _quota;
    private readonly Func<SubtitleAiDialogSelection, Task> _submitAsync;
    private readonly Func<Task<SubtitleAiQuota?>>? _refreshQuotaAsync;
    private string _mode;
    private bool _submitting;
    public bool FocusOnOpen { get; set; } = true;

    public SubtitleAiDialog(
        IReadOnlyList<SubtitleTrackInfo> subtitleTracks,
        IReadOnlyList<AudioTrackInfo> audioTracks,
        bool translateEnabled,
        bool transcribeEnabled,
        SubtitleAiQuota? quota,
        Func<SubtitleAiDialogSelection, Task> submitAsync,
        Func<Task<SubtitleAiQuota?>>? refreshQuotaAsync = null)
    {
        InitializeComponent();
        _subtitleTracks = subtitleTracks;
        _audioTracks = audioTracks;
        _canTranslate = translateEnabled && subtitleTracks.Count > 0;
        _canTranscribe = transcribeEnabled && audioTracks.Count > 0;
        _quota = quota;
        _submitAsync = submitAsync;
        _refreshQuotaAsync = refreshQuotaAsync;
        _mode = _canTranslate ? "subtitles" : "audio";

        foreach (var language in MediaLanguageCatalog.All)
            TargetLanguageComboBox.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
        TargetLanguageComboBox.SelectedIndex = TargetLanguageComboBox.Items
            .Cast<ComboBoxItem>()
            .Select((item, index) => (item, index))
            .First(pair => string.Equals(pair.item.Tag?.ToString(), "en", StringComparison.Ordinal))
            .index;

        ModeTabs.Visibility = _canTranslate && _canTranscribe
            ? Visibility.Visible
            : Visibility.Collapsed;
        Opened += (_, _) => { if (FocusOnOpen) SourceComboBox.Focus(FocusState.Programmatic); };
        UpdateMode();
        // An open dialog follows the interface language; the chosen source,
        // target language and any in-flight submission stay as they are.
        AppLocalization.Register(this, () =>
        {
            AppLocalization.ApplyDirection(this);
            if (_submitting) SubmitButton.Content = T("web.subtitles.translate.starting", "Starting…");
            else UpdateMode(preserveSourceSelection: true);
        });
    }

    private static string T(string key, string english) => AppStrings.Get(key, fallback: english);

    private void SubtitleMode_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting || !_canTranslate) return;
        _mode = "subtitles";
        UpdateMode();
    }

    private void AudioMode_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting || !_canTranscribe) return;
        _mode = "audio";
        UpdateMode();
    }

    private void UpdateMode(bool preserveSourceSelection = false)
    {
        var previousSourceIndex = preserveSourceSelection ? SourceComboBox.SelectedIndex : -1;
        var fromAudio = _mode == "audio";
        DialogTitleText.Text = fromAudio
            ? T("web.subtitles.translate.titleGenerate", "Generate subtitles with AI")
            : T("web.subtitles.translate.titleTranslate", "Translate subtitles with AI");
        SourceLabel.Text = fromAudio ? T("web.subtitles.translate.audioTrack", "Audio track") : T("web.subtitles.translate.translateFrom", "Translate from");
        TargetLabel.Text = fromAudio ? T("web.subtitles.translate.subtitleLanguage", "Subtitle language") : T("web.subtitles.translate.translateTo", "Translate to");
        SubmitButton.Content = fromAudio ? T("web.subtitles.translate.generate", "Generate") : T("web.subtitles.translate.translate", "Translate");

        var active = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(38, 255, 255, 255));
        var inactive = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        SubtitleModeButton.Background = fromAudio ? inactive : active;
        AudioModeButton.Background = fromAudio ? active : inactive;
        SubtitleModeButton.Foreground = new SolidColorBrush(
            fromAudio
                ? Microsoft.UI.ColorHelper.FromArgb(128, 255, 255, 255)
                : Microsoft.UI.Colors.White);
        AudioModeButton.Foreground = new SolidColorBrush(
            fromAudio
                ? Microsoft.UI.Colors.White
                : Microsoft.UI.ColorHelper.FromArgb(128, 255, 255, 255));

        SourceComboBox.Items.Clear();
        if (fromAudio)
        {
            for (var index = 0; index < _audioTracks.Count; index++)
            {
                var track = _audioTracks[index];
                var language = PlayerService.LanguageCodeToName(track.Language);
                var layout = string.IsNullOrWhiteSpace(track.Layout) ? "" : $" · {track.Layout}";
                var defaultLabel = track.Default ? " · " + T("web.subtitles.translate.audioDefault", "default") : "";
                SourceComboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{language}{layout}{defaultLabel}",
                    Tag = index,
                });
            }
        }
        else
        {
            var labels = SubtitleSourceLabels.Build(_subtitleTracks, PlayerService.LanguageCodeToName);
            foreach (var track in _subtitleTracks)
            {
                SourceComboBox.Items.Add(new ComboBoxItem
                {
                    Content = labels[track],
                    Tag = track,
                });
            }
        }
        if (SourceComboBox.Items.Count > 0)
            SourceComboBox.SelectedIndex = previousSourceIndex >= 0
                && previousSourceIndex < SourceComboBox.Items.Count
                    ? previousSourceIndex
                    : 0;

        var quotaExhausted = fromAudio && _quota?.Limited == true && _quota.Remaining <= 0;
        QuotaText.Visibility = fromAudio && _quota?.Limited == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        QuotaText.Foreground = new SolidColorBrush(
            quotaExhausted
                ? Microsoft.UI.ColorHelper.FromArgb(230, 253, 224, 71)
                : Microsoft.UI.ColorHelper.FromArgb(90, 255, 255, 255));
        QuotaText.Text = _quota?.Limited == true ? QuotaMessage(_quota, quotaExhausted) : "";

        HelpText.Text = fromAudio
            ? T("web.subtitles.translate.noteAudioTranslate", "The audio is transcribed on the server (and translated if the language differs) — longer files take a while. The finished track is saved for everyone.")
            : T("web.subtitles.translate.noteSubtitles", "Playback pauses while the first lines are translated, then resumes with subtitles streaming in. The finished track is saved for everyone.");
        SubmitButton.IsEnabled = !_submitting && SourceComboBox.Items.Count > 0 && !quotaExhausted;
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting || SourceComboBox.SelectedItem is not ComboBoxItem sourceItem)
            return;

        var targetLanguage = (TargetLanguageComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
        SubtitleAiDialogSelection selection;
        if (_mode == "audio")
        {
            var index = sourceItem.Tag is int selectedIndex ? selectedIndex : 0;
            selection = new SubtitleAiDialogSelection(
                "audio",
                index,
                _audioTracks.ElementAtOrDefault(index)?.Language ?? "",
                targetLanguage);
        }
        else if (sourceItem.Tag is SubtitleTrackInfo track)
        {
            selection = new SubtitleAiDialogSelection(
                "subtitles",
                track.Index,
                track.Language,
                targetLanguage);
        }
        else
        {
            return;
        }

        SetSubmitting(true);
        ErrorPanel.Visibility = Visibility.Collapsed;
        try
        {
            await _submitAsync(selection);
            Hide();
        }
        catch (Exception ex)
        {
            if (ex is SiloPlayer.Core.Api.ApiException { ErrorCode: "quota_exceeded" } &&
                _refreshQuotaAsync != null)
            {
                try { _quota = await _refreshQuotaAsync(); } catch { }
            }
            ErrorText.Text = ex is SiloPlayer.Core.Api.ApiException api
                ? ApiProblemText.Display(api, string.IsNullOrWhiteSpace(api.Message) ? "Couldn't start AI subtitles." : null)
                : string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't start AI subtitles." : ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
            SetSubmitting(false);
        }
    }

    private void SetSubmitting(bool submitting)
    {
        _submitting = submitting;
        SubtitleModeButton.IsEnabled = !submitting && _canTranslate;
        AudioModeButton.IsEnabled = !submitting && _canTranscribe;
        SourceComboBox.IsEnabled = !submitting;
        TargetLanguageComboBox.IsEnabled = !submitting;
        CancelButton.IsEnabled = !submitting;
        SubmitProgress.IsActive = submitting;
        SubmitProgress.Visibility = submitting ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.Content = submitting
            ? T("web.subtitles.translate.starting", "Starting…")
            : _mode == "audio" ? T("web.subtitles.translate.generate", "Generate") : T("web.subtitles.translate.translate", "Translate");
        if (!submitting)
            UpdateMode(preserveSourceSelection: true);
        else
            SubmitButton.IsEnabled = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_submitting)
            Hide();
    }

    // WebUI quotaPeriods.ts names the rolling window ("24 hours", "7 days",
    // "30 days"); a period this build does not know keeps the desktop wording.
    private static string QuotaMessage(SubtitleAiQuota quota, bool exhausted)
    {
        string? window = quota.Period switch
        {
            "day" or "daily" => AppStrings.Get("web.subtitles.quotaWindow.day", new Dictionary<string, object?> { ["hours"] = 24 }, fallback: ""),
            "week" or "weekly" => AppStrings.Get("web.subtitles.quotaWindow.week", new Dictionary<string, object?> { ["days"] = 7 }, fallback: ""),
            "month" or "monthly" => AppStrings.Get("web.subtitles.quotaWindow.month", new Dictionary<string, object?> { ["days"] = 30 }, fallback: ""),
            _ => null,
        };
        var english = exhausted
            ? $"You've used all {quota.Limit} transcriptions for the {FormatQuotaPeriod(quota.Period)}. Try again later."
            : $"{quota.Remaining} of {quota.Limit} transcriptions left for the {FormatQuotaPeriod(quota.Period)}.";
        if (string.IsNullOrEmpty(window)) return english;
        var args = new Dictionary<string, object?>
        {
            ["limit"] = quota.Limit, ["limit_text"] = quota.Limit.ToString("N0", AppStrings.FormatCulture),
            ["remaining"] = quota.Remaining.ToString("N0", AppStrings.FormatCulture), ["period"] = window,
        };
        return AppStrings.Get(exhausted ? "web.subtitles.translate.quotaExhausted" : "web.subtitles.translate.quotaRemaining", args, fallback: english);
    }

    private static string FormatQuotaPeriod(string period) => period switch
    {
        "hour" or "hourly" => "last hour",
        "day" or "daily" => "last day",
        "week" or "weekly" => "last week",
        "month" or "monthly" => "last month",
        _ => string.IsNullOrWhiteSpace(period) ? "current period" : period,
    };
}
