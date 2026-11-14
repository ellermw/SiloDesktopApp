using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    // This presentation draft never replaces the backend's selection owner.
    private sealed record NativeSubtitlePending(NativeMenuOwner Owner, int Index);
    private NativeSubtitlePending? _nativeSubtitlePending;
    private int ExecutedSubtitleIndex => _playbackManager?.CurrentSession?.MediaFileId == _executedSubtitleFileId
        ? _executedSubtitleTrack?.Index ?? -1 : -1;
    private ContentDialog? _nativeSubtitleDialog;
    private async Task HandoffNativeSubtitleDialogAsync(string action, PlaybackMenuRequest request, PlaybackUiSnapshot owner, Func<bool> canOpen)
    {
        try
        {
            if (!owner.IsCurrent() || !canOpen()) return;
            if (action == "add") await ShowSubtitleSearchDialogAsync(request);
            else if (action == "appearance") await ShowSubtitleAppearanceDialogAsync(request);
            else await ShowSubtitleAiDialogAsync(request);
        }
        catch (Exception)
        {
            if (owner.IsCurrent()) ShowNotice("Subtitle tool unavailable", "Please try opening the subtitle tool again.", "error");
        }
        finally
        {
            // Early refusal and exceptions must complete the same handshake as
            // an opened dialog. A duplicate scoped completion is ignored by Lua.
            NotifySubtitleDialogClosed(owner, request);
        }
    }
    private async Task ShowOwnedSubtitleDialogAsync(ContentDialog dialog, PlaybackUiSnapshot owner, IntPtr hwnd)
    {
        if (!owner.IsCurrent()) return;
        _nativeSubtitleDialog = dialog;
        var watch = _nativeMenuDispatcher!.CreateTimer();
        watch.Interval = TimeSpan.FromMilliseconds(100);
        watch.Tick += (_, _) => { if (!owner.IsCurrent()) dialog.Hide(); };
        watch.Start();
        try { await PlaybackDialogHost.ShowContentDialogAsync(dialog, PlaybackDialogHost.IsForegroundOwnedBy(hwnd)); }
        finally
        {
            watch.Stop();
            if (ReferenceEquals(_nativeSubtitleDialog, dialog)) _nativeSubtitleDialog = null;
        }
    }

    private Action<string>? _nativeSubtitleAction;
    private sealed record NativeSubtitleAiCapability(PlaybackAuthorityScope Authority,
        SiloPlayer.Core.Api.ApiIdentityContext Context, PlaybackStartResponse? Session, long FileGeneration, bool Available);
    private NativeSubtitleAiCapability? _nativeSubtitleAiCapability;
    private bool NativeSubtitleAiAvailable => _nativeSubtitleAiCapability is { Available: true } capability &&
        ReferenceEquals(capability.Authority, _activeAuthority) && capability.Authority.ApiClient.IsCurrentIdentityContext(capability.Context) &&
        ReferenceEquals(capability.Session, _playbackManager?.CurrentSession) &&
        capability.FileGeneration == Volatile.Read(ref _playbackFileGeneration);

    private sealed record NativeSubtitleIdentity(int Index, int File, string? Track, string? Sync,
        string Language, string? Codec, string Label, string? Source, string? Delivery,
        bool Forced, bool HearingImpaired, bool Unplayable, bool? NativeSelectable, string Url,
        int? StreamIndex, string? ContainerTrackId, string? NativeTitle);
    private static NativeSubtitleIdentity CaptureNativeSubtitleIdentity(SubtitleTrackInfo track)
        => new(track.Index, track.MediaFileId, track.TrackId, track.SyncKey, track.Language,
            track.Codec, track.Label, track.Source, track.Delivery, track.Forced, track.HearingImpaired, track.Unplayable, track.NativeSelectable, track.Url,
            track.StreamIndex, track.ContainerTrackId, track.NativeTitle);

    private PlaybackChoiceMenu CreateNativeSubtitleMenu(NativeMenuOwner owner, PlaybackMenuRequest request,
        Action<PlaybackMenuCloseReason> finish)
    {
        var session = _playbackManager!.CurrentSession!;
        var tracks = session.SubtitleUrls.ToArray();
        var identities = tracks.Select(CaptureNativeSubtitleIdentity).ToArray();
        bool InventoryCurrent() => owner.IsCurrent() && ReferenceEquals(session, _playbackManager?.CurrentSession) &&
            session.SubtitleUrls.Select(CaptureNativeSubtitleIdentity).SequenceEqual(identities);
        bool Current() => InventoryCurrent() && _nativeMenuGate.IsCurrent(request);
        // Route availability belongs to this menu's captured inventory/file owner,
        // not its footer timer. Read native tracks and sidecar URLs only once.
        var sidecarIndices = _playbackManager.GetSubtitleUrls()
            .Where(pair => Uri.TryCreate(pair.FullUrl, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
            .Select(pair => pair.Track.Index).ToHashSet();
        // A SID or bytes already loaded stay a route for a row whose URL a refresh blanked.
        lock (_subtitleTrackStateLock) sidecarIndices.UnionWith(CachedSubtitleSidecarIndices());
        var useNative = CanUseNativeEmbeddedSubtitleTrack() && tracks.Any(track =>
            !track.Unplayable && string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<NativeSubtitleTrack> nativeTracks = useNative ? owner.Mpv.GetEmbeddedSubtitleTracks() : [];
        var ffmpegIndices = useNative && owner.Mpv.HasFfmpegTrackIndices();
        var containerIds = useNative && owner.Mpv.HasContainerTrackIds();
        var availableTracks = tracks.Where(track => !track.Unplayable &&
            (IsUnsupportedBitmapSubtitle(track) || sidecarIndices.Contains(track.Index) ||
                useNative && string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase) &&
                ResolveNativeEmbeddedSidFromInventory(track, nativeTracks, ffmpegIndices, containerIds) > 0))
            .ToHashSet();
        (int Current, int? Pending, int? Preference) SelectionState()
        {
            var pending = _nativeSubtitlePending is { } draft && draft.Owner.IsCurrent() ? draft.Index : (int?)null;
            return (ExecutedSubtitleIndex, pending, _pendingSubtitlePreferenceIndex);
        }
        var choiceState = SelectionState();
        var footer = new StackPanel { Spacing = 6, Margin = new Thickness(12, 6, 12, 10) };
        var delayStatus = new TextBlock { FontSize = 11, Opacity = .7 };
        var delay = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var delayValue = new TextBlock { MinWidth = 74, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        PlaybackChoiceMenu? menu = null;
        var timer = _nativeMenuDispatcher!.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(150);
        System.Action refresh = () => { };
        void Action(string id)
        {
            if (!Current()) return;
            switch (id)
            {
                case "earlier": if (CanChangeDelay()) _ = SetSubtitleOffsetAsync(SubtitleOffsetMs - 100); break;
                case "later": if (CanChangeDelay()) _ = SetSubtitleOffsetAsync(SubtitleOffsetMs + 100); break;
                case "reset-delay": if (CanChangeDelay()) _ = SetSubtitleOffsetAsync(0); break;
                case "sync": case "reset-timing": case "reload-sync":
                    var key = ActiveTiming().Key;
                    if (key != null) _ = SubtitleSyncActionAsync(id == "reload-sync" ? "" : key,
                        id == "sync" ? "sync" : id == "reset-timing" ? "reset" : "reload");
                    break;
                case "add": case "appearance": case "ai":
                    if (id == "ai" && !NativeSubtitleAiAvailable) return;
                    var dialogOwner = CapturePlaybackUiSnapshot();
                    owner.Mpv.SendScriptMessage("osc-native-subtitle-dialog-opening", request.OwnerToken, request.Sequence.ToString(CultureInfo.InvariantCulture));
                    menu?.Close(PlaybackMenuCloseReason.Selection);
                    // Close synchronously releases this exact menu's dialog lease.
                    _ = HandoffNativeSubtitleDialogAsync(id, request, dialogOwner, InventoryCurrent);
                    return;
            }
            refresh();
        }
        // Footer labels re-render with the interface language (see Localize below).
        var footerText = new List<Action>();
        Button Button(Func<string> title, Func<string> name, string id)
        {
            var button = new Button { Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Stretch };
            void Text() { button.Content = title(); AutomationProperties.SetName(button, name()); }
            Text(); footerText.Add(Text);
            Action click = () => Action(id); button.Tag = click; button.Click += (_, _) => click();
            return button;
        }
        Func<string> Key(string key, string english) => () => HudText(key, english);
        Func<string> Step(string key, string english) => () => AppStrings.Get(key,
            new Dictionary<string, object?> { ["step"] = 100 }, fallback: english);
        var earlier = Button(() => "−", Step("web.subtitles.menu.delayEarlier", "Subtitle delay 100ms earlier"), "earlier");
        var later = Button(() => "+", Step("web.subtitles.menu.delayLater", "Subtitle delay 100ms later"), "later");
        var resetDelay = Button(Key("web.subtitles.menu.resetDelay", "Reset"), Key("web.subtitles.menu.resetDelayLabel", "Reset subtitle delay"), "reset-delay");
        delay.Children.Add(earlier); delay.Children.Add(delayValue); delay.Children.Add(later); delay.Children.Add(resetDelay);
        footer.Children.Add(delayStatus); footer.Children.Add(delay);
        var syncStatus = new TextBlock { FontSize = 12, Opacity = .8, TextWrapping = TextWrapping.Wrap, MaxLines = 3 };
        footer.Children.Add(syncStatus);
        var syncActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var sync = Button(Key("web.subtitles.menu.syncToAudio", "Sync to audio"), Key("web.subtitles.menu.syncToAudio", "Sync to audio"), "sync");
        var resetTiming = Button(Key("web.subtitles.menu.resetTiming", "Reset timing"), Key("web.subtitles.menu.resetTiming", "Reset timing"), "reset-timing");
        // Native-only: the catalog has no reload-status action.
        var reload = Button(() => "Reload status", () => "Reload subtitle sync status", "reload-sync");
        syncActions.Children.Add(sync); syncActions.Children.Add(resetTiming); syncActions.Children.Add(reload); footer.Children.Add(syncActions);
        footer.Children.Add(Button(Key("web.subtitles.menu.addSubtitles", "Add Subtitles…"), Key("web.subtitles.search.title", "Add Subtitles"), "add"));
        var ai = Button(Key("web.subtitles.menu.translateWithAi", "Translate with AI…"), Key("web.subtitles.translate.titleTranslate", "Translate subtitles with AI"), "ai"); footer.Children.Add(ai);
        footer.Children.Add(Button(Key("web.subtitles.menu.appearance", "Appearance…"), Key("web.subtitles.menu.appearance", "Appearance…"), "appearance"));
        bool CanChangeDelay() => ExecutedSubtitleIndex >= 0 && SubtitlePreferencesLoaded;
        (string? Key, string Status, bool Busy, bool Sync, bool Reset, bool Reload) ActiveTiming()
        {
            lock (_subtitleSyncGate)
            {
                var active = tracks.FirstOrDefault(track => track.Index == ExecutedSubtitleIndex);
                var key = active == null ? null : SubtitleSyncPolicy.Key(active);
                _subtitleSyncEntries.TryGetValue(key ?? "", out var entry);
                return (key,
                    entry?.Forbidden == true ? HudText("web.subtitles.menu.timingForbidden", "This server doesn't allow changing subtitle timing.") : entry?.Error ??
                        (entry == null ? "" : entry.State.Sync?.Status == "failed" ? SubtitleSyncPolicy.Failure(entry.State.Sync.Failure) : SubtitleSyncPolicy.Status(entry.State)),
                    entry?.Busy == true || SubtitleSyncPolicy.InProgress(entry?.State.Sync),
                    entry != null && !entry.Forbidden && !entry.Unsupported && _subtitleSyncCapability?.State == "available" &&
                        (entry.State.Source != "external" || _subtitleSyncCapability.External),
                    entry != null && !entry.Forbidden && !entry.State.Timing.IsIdentity,
                    key != null && (_subtitleSyncInventoryError || entry?.Error != null));
            }
        }
        IReadOnlyList<PlaybackMenuChoice> Choices((int Current, int? Pending, int? Preference) state)
        {
            var current = state.Current;
            var choices = new List<PlaybackMenuChoice>
            {
                new("-1", HudText("web.subtitles.menu.off", "Off"), "", [], current < 0, IsPending: state.Pending < 0 || state.Preference == -1),
            };
            foreach (var track in tracks.OrderBy(track => track.Source switch { "external" => 1, "downloaded" => 2, _ => 0 }))
            {
                var language = LanguageCodeToName(track.Language);
                var title = string.IsNullOrWhiteSpace(language) ? HudText("web.languageName.unknown", "Unknown") : language;
                var detail = track.Label == title || track.Label == track.Language ||
                    string.Equals(track.Label.TrimStart('.'), track.Codec, StringComparison.OrdinalIgnoreCase) ? "" : track.Label;
                var badges = new[] { track.Codec?.ToUpperInvariant() ?? "", track.Source switch
                    { "ai_generated" or "generated" => "AI Generated", "downloaded" => HudText("web.subtitles.menu.source.downloaded", "Downloaded"),
                        "external" => HudText("web.subtitles.menu.source.external", "External"), _ => HudText("web.subtitles.menu.source.embedded", "Embedded") },
                    track.Forced ? HudText("web.subtitles.translate.forced", "Forced") : "",
                    track.HearingImpaired ? HudText("web.subtitles.translate.sdh", "SDH") : "" }.Where(text => text.Length > 0).ToArray();
                var available = availableTracks.Contains(track);
                choices.Add(new(track.Index.ToString(CultureInfo.InvariantCulture), title, detail, badges,
                    track.Index == current, IsEnabled: available,
                    DisabledReason: available ? "" : "Subtitle unavailable",
                    IsPending: track.Index == state.Pending || track.Index == state.Preference));
            }
            return choices;
        }
        void Refresh()
        {
            if (!Current()) { menu?.Close(PlaybackMenuCloseReason.OwnerRetired); return; }
            var offset = SubtitleOffsetMs;
            var shownDelay = offset == 0 ? "0" : offset.ToString("+0;-0", AppStrings.FormatCulture);
            delayValue.Text = AppStrings.Get("web.subtitles.menu.delayValue", new Dictionary<string, object?> { ["delay"] = shownDelay },
                fallback: shownDelay + " ms");
            delayStatus.Text = SubtitlePreferenceError ?? (!SubtitlePreferencesLoaded ? "Loading subtitle preferences" :
                _subtitleOffsetDraft.HasValue ? "Saving subtitle delay…" : HudText("web.subtitles.menu.delay", "Delay"));
            earlier.IsEnabled = CanChangeDelay() && offset > -10000; later.IsEnabled = CanChangeDelay() && offset < 10000;
            resetDelay.IsEnabled = CanChangeDelay() && offset != 0;
            var timing = ActiveTiming(); syncStatus.Text = timing.Status;
            syncStatus.Visibility = timing.Status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            sync.Visibility = timing.Sync ? Visibility.Visible : Visibility.Collapsed; sync.IsEnabled = !timing.Busy;
            sync.Content = timing.Busy ? HudText("web.subtitles.menu.syncing", "Syncing…") : HudText("web.subtitles.menu.syncToAudio", "Sync to audio");
            resetTiming.Visibility = timing.Reset ? Visibility.Visible : Visibility.Collapsed; resetTiming.IsEnabled = !timing.Busy;
            reload.Visibility = timing.Reload ? Visibility.Visible : Visibility.Collapsed;
            ai.Visibility = NativeSubtitleAiAvailable ? Visibility.Visible : Visibility.Collapsed;
            var nextState = SelectionState();
            if (nextState != choiceState)
            {
                choiceState = nextState;
                menu?.UpdateChoices(Choices(choiceState));
            }
        }
        var action = (Action<string>)Action; _nativeSubtitleAction = action;
        void Closed(PlaybackMenuCloseReason reason)
        {
            timer.Stop();
            if (ReferenceEquals(_nativeSubtitleAction, action)) _nativeSubtitleAction = null;
            finish(reason);
        }
        menu = new PlaybackChoiceMenu(owner.Video, request.Anchor, HudText("web.subtitles.menu.heading", "Subtitles"), Choices(choiceState), Current,
            id => { if (InventoryCurrent() && int.TryParse(id, out var index)) _ = SelectNativeSubtitleAsync(owner, index); },
            Closed, root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root), footer);
        menu.Localize(() => HudText("web.subtitles.menu.heading", "Subtitles"), () =>
        {
            foreach (var text in footerText) text();
            Refresh();
            return Choices(choiceState);
        });
        refresh = Refresh; timer.Tick += (_, _) => Refresh(); Refresh(); timer.Start();
        _ = SubtitleSyncActionAsync("", "refresh");
        return menu;
    }

    private void InvokeNativeSubtitleAction(string action) => _nativeSubtitleAction?.Invoke(action);

    private async Task SelectNativeSubtitleAsync(NativeMenuOwner owner, int index)
    {
        if (!owner.IsCurrent()) return;
        var pending = new NativeSubtitlePending(owner, index);
        _nativeSubtitlePending = pending;
        // Bitmap and fallback URL sub-adds can download synchronously in mpv.
        // Publish pending state on the dispatcher, but keep selection I/O off it.
        try
        {
            await Task.Run(() => owner.IsCurrent() && ReferenceEquals(_nativeSubtitlePending, pending)
                ? SelectSubtitleByServerIndexAsync(index) : Task.FromResult(false));
        }
        catch (Exception) { if (owner.IsCurrent()) ShowNotice("Subtitle unavailable", "Please try selecting the subtitle again.", "error"); }
        finally { if (ReferenceEquals(_nativeSubtitlePending, pending)) _nativeSubtitlePending = null; }
    }
}
