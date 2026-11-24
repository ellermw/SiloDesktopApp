using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    // Show Next Up (playback.next_up_prompt_seconds), official silo-server 3b2b32168 (#4502).
    // -1 until the effective setting loads: nothing opens early meanwhile, so a viewer
    // who chose "At end" and resumes near the end is never shown Next Up on the default.
    private const int NextUpPromptUnloaded = -1;
    private readonly object _nextUpPromptGate = new();
    private int _nextUpPromptSeconds = NextUpPromptUnloaded;
    private long _nextUpPromptRevision;
    private CancellationTokenSource? _nextUpPromptRead;

    private int? NextUpPromptSeconds
    {
        get { var value = Volatile.Read(ref _nextUpPromptSeconds); return value < 0 ? null : value; }
    }

    /// <summary>Whether Next Up opens early at <paramref name="mediaPosition"/> of the file now playing.</summary>
    private bool IsNextUpPromptDue(double mediaPosition)
    {
        var part = PlaybackPartSequence.PartOf(WatchDetail, ActiveMediaFileId);
        return NextUpPromptTiming.IsDue(NextUpPromptSeconds, CurrentMediaDuration, mediaPosition,
            part?.Index ?? 0, part?.Count ?? 1);
    }

    /// <summary>
    /// Reads the viewer's Show Next Up lead time. <paramref name="reset"/> forgets the old value first
    /// (a new file); a setting changed in Settings keeps it until the new one arrives.
    /// A failed read, or a server without the key, uses the contract default as official does.
    /// </summary>
    private async Task RefreshNextUpPromptSecondsAsync(CancellationToken ct, bool reset)
    {
        var authority = _activeAuthority;
        var playback = Volatile.Read(ref _playRequestGeneration);
        var file = Volatile.Read(ref _fileLoadGeneration);
        var identity = authority.SettingsApi.CaptureSettingsIdentityContext();
        CancellationTokenSource read;
        long revision;
        lock (_nextUpPromptGate)
        {
            _nextUpPromptRead?.Cancel();
            read = CancellationTokenSource.CreateLinkedTokenSource(ct,
                _authorityLifetimeCts.Token, _playbackCts?.Token ?? CancellationToken.None);
            _nextUpPromptRead = read;
            revision = ++_nextUpPromptRevision;
            if (reset) Volatile.Write(ref _nextUpPromptSeconds, NextUpPromptUnloaded);
        }
        bool Current() => !_closing && revision == Volatile.Read(ref _nextUpPromptRevision) &&
            ReferenceEquals(authority, _activeAuthority) && authority.SettingsApi.IsCurrentSettingsIdentityContext(identity) &&
            playback == Volatile.Read(ref _playRequestGeneration) && file == Volatile.Read(ref _fileLoadGeneration);
        try
        {
            int? seconds = null;
            if (string.IsNullOrWhiteSpace(identity.Identity.ProfileId)) seconds = NextUpPromptTiming.DefaultSeconds;
            for (var attempt = 0; seconds is null && attempt < 3; attempt++)
            {
                read.Token.ThrowIfCancellationRequested();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(read.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var effective = await authority.SettingsApi.GetContractEffectiveSettingsAsync(
                        identity, [NextUpPromptTiming.SettingKey], ct: deadline.Token).ConfigureAwait(false);
                    seconds = NextUpPromptTiming.Read(effective);
                }
                catch (ApiException ex) when (ex.StatusCode is 404 or 405 or 422 or 501)
                {
                    // No effective settings, or no such key: official's default.
                    seconds = NextUpPromptTiming.DefaultSeconds;
                }
                catch (OperationCanceledException) when (read.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    LogToFile("state_trace.txt", $"Next Up prompt setting unavailable: {ex.Message}");
                }
                if (seconds is null && attempt < 2) await Task.Delay(250 << attempt, read.Token).ConfigureAwait(false);
            }
            // A failed read of a refreshed setting keeps what the viewer last had.
            seconds ??= NextUpPromptSeconds ?? NextUpPromptTiming.DefaultSeconds;
            lock (_nextUpPromptGate)
            {
                if (!Current() || read.IsCancellationRequested) return;
                Volatile.Write(ref _nextUpPromptSeconds, seconds.Value);
            }
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        finally
        {
            lock (_nextUpPromptGate)
                if (ReferenceEquals(_nextUpPromptRead, read)) _nextUpPromptRead = null;
            read.Dispose();
        }
    }
}
