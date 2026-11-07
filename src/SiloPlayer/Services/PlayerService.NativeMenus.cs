using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private sealed record NativeMenuOwner(string Token, MpvPlayer Mpv, MpvVideoWindow Video, Func<bool> IsCurrent);
    private long _nativeMenuEpoch;
    private long _nativeMenuPublication;
    private readonly DispatcherQueue? _nativeMenuDispatcher = DispatcherQueue.GetForCurrentThread();
    private NativeMenuOwner? _nativeMenuOwner;
    private readonly PlaybackMenuRequestGate _nativeMenuGate = new();
    private PlaybackChoiceMenu? _nativeMenu;
    private PlaybackMenuRequest? _nativeMenuRequest;

    private void PublishNativeMenuScopes()
    {
        var authority = _activeAuthority;
        // Access refreshes retire reads, not the accepted playback identity.
        var context = authority.ApiClient.CaptureIdentityContext();
        var manager = _playbackManager;
        var session = manager?.CurrentSession;
        var transport = _activeTransportPlan;
        // A completed server session can leave a playable direct stream and
        // its last OSC inventory. Only this explicit fallback enables local Audio.
        var legacyAudio = manager != null && session == null &&
            transport?.TransportKind == PlaybackTransportKind.DirectProgressive;
        var version = ActiveVersion;
        var versions = Versions.ToArray();
        var audio = version?.AudioTracks?.ToArray() ?? [];
        var chapters = version?.Chapters?.ToArray() ?? [];
        var qualities = session?.AvailableQualities?.ToArray() ?? [];
        var roomPlayback = IsWatchTogetherPlayback;
        var mpv = _mpv; var video = _videoWindow; var content = ContentId;
        var play = Volatile.Read(ref _playRequestGeneration);
        var file = Volatile.Read(ref _fileLoadGeneration);
        var playbackFile = Volatile.Read(ref _playbackFileGeneration);
        var epoch = Volatile.Read(ref _nativeMenuEpoch);
        var publication = Interlocked.Increment(ref _nativeMenuPublication);
        bool Current() => !_closing && !_postRollActive && !_switchingContent && !IsLoading && _playbackTerminalState == null &&
            roomPlayback == IsWatchTogetherPlayback && versions.SequenceEqual(Versions) &&
            (legacyAudio ? ReferenceEquals(transport, _activeTransportPlan) :
                (version?.AudioTracks?.SequenceEqual(audio) ?? audio.Length == 0)) &&
            (version?.Chapters?.SequenceEqual(chapters) ?? chapters.Length == 0) &&
            (session?.AvailableQualities?.SequenceEqual(qualities) ?? qualities.Length == 0) &&
            State is PlayerState.Expanded or PlayerState.Fullscreen or PlayerState.PictureInPicture && epoch == Volatile.Read(ref _nativeMenuEpoch) &&
            play == Volatile.Read(ref _playRequestGeneration) && file == Volatile.Read(ref _fileLoadGeneration) &&
            playbackFile == Volatile.Read(ref _playbackFileGeneration) &&
            ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentIdentityContext(context) &&
            ReferenceEquals(manager, _playbackManager) && ReferenceEquals(session, manager?.CurrentSession) &&
            ReferenceEquals(version, ActiveVersion) && content == ContentId &&
            ReferenceEquals(mpv, _mpv) && ReferenceEquals(video, _videoWindow) && video?.Hwnd != IntPtr.Zero;
        if (mpv == null || video == null || (!legacyAudio && session == null) ||
            _closing || _postRollActive || _switchingContent || IsLoading || _playbackTerminalState != null)
        { RetireNativeMenus(); return; }
        var owner = new NativeMenuOwner(Guid.NewGuid().ToString("N"), mpv, video, Current);
        var dispatcher = App.MainWindowInstance?.DispatcherQueue ?? _nativeMenuDispatcher;
        void Publish()
        {
            if (publication != Volatile.Read(ref _nativeMenuPublication) || !owner.IsCurrent()) return;
            if (_nativeMenuOwner is { } retained && retained.IsCurrent()) owner = retained;
            else
            {
                _nativeMenu?.Close(PlaybackMenuCloseReason.OwnerRetired);
                _nativeMenuOwner = owner; _nativeMenuGate.ResetOwner(owner.Token);
            }
            mpv.SendScriptMessage("osc-native-audio-scope", legacyAudio ? "legacy" : version?.AudioTracks?.Count > 1 ? owner.Token : "");
            mpv.SendScriptMessage("osc-native-quality-scope", session != null && ((!IsWatchTogetherPlayback && Versions.Count > 1) || AvailableQualities.Count > 1) ? owner.Token : "");
            mpv.SendScriptMessage("osc-native-party-scope", App.Services.GetService<WatchTogetherCoordinator>()?.ActiveRoom != null ? owner.Token : "");
            mpv.SendScriptMessage("osc-native-chapters-scope", version?.Chapters?.Count > 0 ? owner.Token : "");
            mpv.SendScriptMessage("osc-native-more-scope", owner.Token);
        }
        if (dispatcher?.HasThreadAccess == true) Publish(); else dispatcher?.TryEnqueue(Publish);
    }

    private void RetireNativeMenus()
    {
        Interlocked.Increment(ref _nativeMenuEpoch); Interlocked.Increment(ref _nativeMenuPublication);
        var owner = _nativeMenuOwner;
        var mpv = owner?.Mpv ?? _mpv;
        var dispatcher = App.MainWindowInstance?.DispatcherQueue ?? _nativeMenuDispatcher;
        void Retire()
        {
            if (!ReferenceEquals(owner, _nativeMenuOwner)) return;
            _nativePartyEndDialog?.Hide();
            _nativeMenu?.Close(PlaybackMenuCloseReason.OwnerRetired);
            _nativeMenuOwner = null; _nativeMenuGate.ResetOwner("");
            // Empty scopes advertise host capability without allowing legacy
            // fallback while the playback surface is unavailable.
            mpv?.SendScriptMessage("osc-native-audio-scope", "");
            mpv?.SendScriptMessage("osc-native-quality-scope", "");
            mpv?.SendScriptMessage("osc-native-party-scope", "");
            mpv?.SendScriptMessage("osc-native-chapters-scope", "");
            mpv?.SendScriptMessage("osc-native-more-scope", "");
        }
        if (dispatcher?.HasThreadAccess == true) Retire(); else dispatcher?.TryEnqueue(Retire);
    }

    private async Task HandleNativeMenuOpenAsync(PlaybackMenuRequest request)
    {
        var owner = _nativeMenuOwner;
        void Ack(PlaybackMenuCloseReason reason = PlaybackMenuCloseReason.OwnerRetired) =>
            (owner?.Mpv ?? _mpv)?.SendScriptMessage("osc-native-menu-closed", request.OwnerToken,
                request.Sequence.ToString(CultureInfo.InvariantCulture),
                reason is PlaybackMenuCloseReason.OutsidePointer or PlaybackMenuCloseReason.PointerSelection ? "true" : "false");
        // A late request or temporary busy state must not retire a current owner.
        if (owner == null || owner.Token != request.OwnerToken || !owner.IsCurrent() || IsLoading || _switchingContent)
        { Ack(); return; }
        if (!_nativeMenuGate.TryOpen(request))
        {
            if (_nativeMenuRequest is not { } active || active.Sequence != request.Sequence) Ack();
            return;
        }
        _nativeMenuRequest = request;
        _nativeMenu?.Close(PlaybackMenuCloseReason.OwnerRetired);
        var lease = false; PlaybackChoiceMenu? menu = null;
        void Finish(PlaybackMenuCloseReason reason)
        {
            if (ReferenceEquals(_nativeMenu, menu)) _nativeMenu = null;
            _nativeMenuGate.TryClose(request.OwnerToken, request.Sequence);
            if (ReferenceEquals(_nativeMenuRequest, request)) _nativeMenuRequest = null;
            if (lease) { lease = false; _playbackDialogGate.Release(); }
            Ack(reason);
        }
        try
        {
            lease = await _playbackDialogGate.WaitAsync(0);
            if (!lease || !owner.IsCurrent() || !_nativeMenuGate.IsCurrent(request))
            { Finish(PlaybackMenuCloseReason.OwnerRetired); return; }
            menu = request.Kind switch
            {
                "audio" => CreateNativeAudioMenu(owner, request, Finish),
                "quality" => CreateNativeQualityMenu(owner, request, Finish),
                "party" => CreateNativePartyMenu(owner, request, Finish),
                "chapters" => CreateNativeChaptersMenu(owner, request, Finish),
                "more" => CreateNativeMoreMenu(owner, request, Finish),
                _ => null,
            };
            if (menu == null) { Finish(PlaybackMenuCloseReason.OwnerRetired); return; }
            _nativeMenu = menu;
            await menu.ShowAsync();
            if (!owner.IsCurrent()) menu.Close(PlaybackMenuCloseReason.OwnerRetired);
        }
        catch (Exception)
        {
            if (menu != null) menu.Close(PlaybackMenuCloseReason.OwnerRetired); else Finish(PlaybackMenuCloseReason.OwnerRetired);
            if (owner.IsCurrent()) ShowNotice("Menu unavailable", "Please try opening the menu again.", "error");
        }
    }

    private PlaybackChoiceMenu? CreateNativeAudioMenu(NativeMenuOwner owner, PlaybackMenuRequest request,
        Action<PlaybackMenuCloseReason> finish)
    {
        var tracks = ActiveVersion?.AudioTracks?.ToArray() ?? [];
        if (tracks.Length <= 1) return null;
        bool InventoryCurrent() => owner.IsCurrent() && ActiveVersion?.AudioTracks is { } current && current.SequenceEqual(tracks);
        var current = _playbackManager?.CurrentSession?.AudioTrackIndex ?? -1;
        return new PlaybackAudioMenu(owner.Video, request.Anchor,
            tracks.Select((track, index) => DescribeAudioMenuChoice(track, index, current)).ToArray(),
            () => InventoryCurrent() && _nativeMenuGate.IsCurrent(request),
            index =>
            {
                if (InventoryCurrent() && index >= 0 && index < tracks.Length && index != current)
                    _ = SelectNativeAudioAsync(owner, index, InventoryCurrent);
            }, finish, root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root));
    }

    private async Task SelectNativeAudioAsync(NativeMenuOwner owner, int sourceIndex, Func<bool> current)
    {
        try { if (current()) await SwitchAudioTrackCoreAsync(sourceIndex, current); }
        catch (Exception) { if (owner.IsCurrent()) ShowNotice("Audio unavailable", "Please try selecting the track again.", "error"); }
    }

    private void HandleNativeMenuInput(string token, string sequence, string action)
    {
        var owner = _nativeMenuOwner;
        if (owner?.Token != token || !owner.IsCurrent() ||
            !long.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return;
        if (_nativeMenuRequest is { } request && request.Sequence == number && _nativeMenuGate.IsCurrent(request))
            _nativeMenu?.Input(action);
    }

    private static PlaybackAudioChoice DescribeAudioMenuChoice(AudioTrackInfo track, int index, int current)
    {
        var language = string.IsNullOrWhiteSpace(track.Language) ? "" : LanguageCodeToName(track.Language.Trim());
        var title = AudioMenuText(track.Title, track.EmbeddedTitle, language.Length > 0 ? language : $"Track {index + 1}");
        var channels = track.Channels switch
        {
            1 => "Mono", 2 => "Stereo", 6 => "5.1", 8 => "7.1",
            > 0 => $"{track.Channels} ch", _ => AudioMenuText(track.Layout, "", ""),
        };
        var metadata = string.Join(" · ", new[]
        {
            !string.Equals(language, title, StringComparison.OrdinalIgnoreCase) ? language : "",
            AudioMenuText(track.Layout, "", ""),
            track.Bitrate is > 0 ? $"{track.Bitrate.Value:0.#} kbps" : "",
            track.SampleRate is > 0 ? $"{track.SampleRate.Value / 1000d:0.#} kHz" : "",
            track.BitDepth is > 0 ? $"{track.BitDepth}-bit" : "",
        }.Where(text => text.Length > 0));
        return new PlaybackAudioChoice(index, title, metadata, VersionRanking.AudioTrackLabel(track), channels, track.Default, index == current);
    }

    private static string AudioMenuText(string? primary, string? alternate, string fallback)
        => !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : !string.IsNullOrWhiteSpace(alternate) ? alternate.Trim() : fallback;
}
