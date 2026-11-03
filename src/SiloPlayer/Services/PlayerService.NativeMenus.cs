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
    private sealed record NativeAudioOwner(string Token, MpvPlayer Mpv, MpvVideoWindow Video,
        AudioTrackInfo[] Tracks, Func<bool> IsCurrent);

    // Identity invalidation is synchronous, even when media loading runs away
    // from the UI dispatcher. Presentation and its semaphore lease are UI-owned.
    private long _nativeAudioEpoch;
    private long _nativeAudioPublication;
    private readonly DispatcherQueue? _nativeMenuDispatcher = DispatcherQueue.GetForCurrentThread();
    private NativeAudioOwner? _nativeAudioOwner;
    private readonly PlaybackMenuRequestGate _nativeAudioGate = new();
    private PlaybackAudioMenu? _nativeAudioMenu;

    private void PublishNativeAudioScope()
    {
        var authority = _activeAuthority;
        var context = authority.ApiClient.CaptureContext();
        var manager = _playbackManager;
        var session = manager?.CurrentSession;
        var version = ActiveVersion;
        var tracks = version?.AudioTracks?.ToArray() ?? [];
        var mpv = _mpv;
        var video = _videoWindow;
        var content = ContentId;
        var playGeneration = Volatile.Read(ref _playRequestGeneration);
        var fileGeneration = Volatile.Read(ref _fileLoadGeneration);
        var playbackFileGeneration = Volatile.Read(ref _playbackFileGeneration);
        var epoch = Volatile.Read(ref _nativeAudioEpoch);
        var publication = Interlocked.Increment(ref _nativeAudioPublication);
        bool Current() => !_closing && !_postRollActive &&
            State is PlayerState.Expanded or PlayerState.Fullscreen &&
            epoch == Volatile.Read(ref _nativeAudioEpoch) &&
            playGeneration == Volatile.Read(ref _playRequestGeneration) &&
            fileGeneration == Volatile.Read(ref _fileLoadGeneration) &&
            playbackFileGeneration == Volatile.Read(ref _playbackFileGeneration) &&
            ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentContext(context) &&
            ReferenceEquals(manager, _playbackManager) && ReferenceEquals(session, manager?.CurrentSession) &&
            ReferenceEquals(version, ActiveVersion) && content == ContentId &&
            ReferenceEquals(mpv, _mpv) && ReferenceEquals(video, _videoWindow) &&
            video?.Hwnd != IntPtr.Zero && version?.AudioTracks is { } current && current.SequenceEqual(tracks);
        if (mpv == null || video == null || session == null || tracks.Length <= 1)
        {
            RetireNativeAudioMenu();
            return;
        }
        var owner = new NativeAudioOwner(Guid.NewGuid().ToString("N"), mpv, video, tracks, Current);
        var dispatcher = App.MainWindowInstance?.DispatcherQueue ?? _nativeMenuDispatcher;
        void Publish()
        {
            if (publication != Volatile.Read(ref _nativeAudioPublication) || !owner.IsCurrent()) return;
            _nativeAudioMenu?.Close(PlaybackMenuCloseReason.OwnerRetired);
            _nativeAudioOwner = owner;
            _nativeAudioGate.ResetOwner(owner.Token);
            owner.Mpv.SendScriptMessage("osc-native-audio-scope", owner.Token);
        }
        if (dispatcher?.HasThreadAccess == true) Publish();
        else dispatcher?.TryEnqueue(Publish);
    }

    private void RetireNativeAudioMenu()
    {
        Interlocked.Increment(ref _nativeAudioEpoch);
        Interlocked.Increment(ref _nativeAudioPublication);
        var owner = _nativeAudioOwner;
        var dispatcher = App.MainWindowInstance?.DispatcherQueue ?? _nativeMenuDispatcher;
        void Retire()
        {
            if (!ReferenceEquals(owner, _nativeAudioOwner)) return;
            _nativeAudioMenu?.Close(PlaybackMenuCloseReason.OwnerRetired);
            _nativeAudioOwner = null;
            _nativeAudioGate.ResetOwner("");
            owner?.Mpv.SendScriptMessage("osc-native-audio-scope", "");
        }
        if (dispatcher?.HasThreadAccess == true) Retire();
        else dispatcher?.TryEnqueue(Retire);
    }

    private async Task HandleNativeAudioOpenAsync(PlaybackMenuRequest request)
    {
        var owner = _nativeAudioOwner;
        if (owner == null || owner.Token != request.OwnerToken || !owner.IsCurrent() || IsLoading || _switchingContent)
        {
            // The acknowledgement remains scoped; a late request cannot clear
            // the newer owner's pending input guard.
            _mpv?.SendScriptMessage("osc-native-menu-closed", request.OwnerToken,
                request.Sequence.ToString(CultureInfo.InvariantCulture), "true");
            return;
        }
        if (!_nativeAudioGate.TryOpen(request))
        {
            // Replays of the active request already have a live presenter.
            // A rejected, previously closed request still needs a scoped ack
            // so its Lua pending guard can advance to a fresh sequence.
            if (_nativeAudioRequest is not { } active || active.Sequence != request.Sequence)
                owner.Mpv.SendScriptMessage("osc-native-menu-closed", request.OwnerToken,
                    request.Sequence.ToString(CultureInfo.InvariantCulture), "true");
            return;
        }
        _nativeAudioRequest = request;
        _nativeAudioMenu?.Close(PlaybackMenuCloseReason.OwnerRetired);
        var lease = false;
        PlaybackAudioMenu? menu = null;
        void Finish(PlaybackMenuCloseReason reason)
        {
            if (ReferenceEquals(_nativeAudioMenu, menu)) _nativeAudioMenu = null;
            _nativeAudioGate.TryClose(request.OwnerToken, request.Sequence);
            if (ReferenceEquals(_nativeAudioRequest, request)) _nativeAudioRequest = null;
            if (lease) { lease = false; _playbackDialogGate.Release(); }
            owner.Mpv.SendScriptMessage("osc-native-menu-closed", request.OwnerToken,
                request.Sequence.ToString(CultureInfo.InvariantCulture),
                "true");
        }
        try
        {
            lease = await _playbackDialogGate.WaitAsync(0);
            if (!lease || !owner.IsCurrent() || !_nativeAudioGate.IsCurrent(request))
            {
                Finish(PlaybackMenuCloseReason.OwnerRetired);
                return;
            }
            var currentTrack = _playbackManager?.CurrentSession?.AudioTrackIndex ?? -1;
            var rows = owner.Tracks.Select((track, index) => DescribeAudioMenuChoice(track, index, currentTrack)).ToArray();
            menu = new PlaybackAudioMenu(owner.Video, request.Anchor, rows,
                () => owner.IsCurrent() && _nativeAudioGate.IsCurrent(request),
                index =>
                {
                    if (owner.IsCurrent() && index >= 0 && index < owner.Tracks.Length && index != currentTrack)
                        _ = SelectNativeAudioAsync(owner, index);
                }, Finish, root => App.Services.GetService<AccessibilityService>()?.ApplySaved(root));
            _nativeAudioMenu = menu;
            await menu.ShowAsync();
            if (!owner.IsCurrent()) menu.Close(PlaybackMenuCloseReason.OwnerRetired);
        }
        catch (Exception)
        {
            if (menu != null) menu.Close(PlaybackMenuCloseReason.OwnerRetired);
            else Finish(PlaybackMenuCloseReason.OwnerRetired);
            if (owner.IsCurrent()) ShowNotice("Audio menu unavailable", "Please try opening Audio again.", "error");
        }
    }

    private async Task SelectNativeAudioAsync(NativeAudioOwner owner, int sourceIndex)
    {
        try
        {
            if (owner.IsCurrent()) await SwitchAudioTrackCoreAsync(sourceIndex, owner.IsCurrent);
        }
        catch (Exception)
        {
            if (owner.IsCurrent()) ShowNotice("Audio unavailable", "Please try selecting the track again.", "error");
        }
    }

    private void HandleNativeMenuInput(string token, string sequence, string action)
    {
        var owner = _nativeAudioOwner;
        if (owner?.Token != token || !owner.IsCurrent() ||
            !long.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return;
        // The request gate verifies the exact sequence without changing ownership.
        if (_nativeAudioRequest is { } request && request.Sequence == number && _nativeAudioGate.IsCurrent(request))
            _nativeAudioMenu?.Input(action);
    }

    private PlaybackMenuRequest? _nativeAudioRequest;

    private static PlaybackAudioChoice DescribeAudioMenuChoice(AudioTrackInfo track, int index, int current)
    {
        var language = LanguageCodeToName(track.Language);
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
            track.Bitrate is > 0 ? $"{track.Bitrate.Value / 1000d:0.#} kbps" : "",
            track.SampleRate is > 0 ? $"{track.SampleRate.Value / 1000d:0.#} kHz" : "",
            track.BitDepth is > 0 ? $"{track.BitDepth}-bit" : "",
        }.Where(text => text.Length > 0));
        return new PlaybackAudioChoice(index, title, metadata, VersionRanking.AudioTrackLabel(track), channels, track.Default, index == current);
    }

    private static string AudioMenuText(string? primary, string? alternate, string fallback)
        => !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : !string.IsNullOrWhiteSpace(alternate) ? alternate.Trim() : fallback;
}
