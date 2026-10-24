using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>The detail selector's explicit choice, reset when the displayed file changes.</summary>
public sealed class PrePlayAudioSelection(SiloApiClient client, CatalogApi catalog)
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly object _gate = new();
    private ApiRequestContext _owner = client.CaptureContext();
    private int? _selectedIndex, _savedIndex;
    private long _generation, _choice;
    public int? SelectedIndex
    {
        get { lock (_gate) return client.IsCurrentContext(_owner) ? _selectedIndex : null; }
    }

    public void Clear()
    {
        lock (_gate) { ++_generation; ++_choice; _selectedIndex = _savedIndex = null; _owner = client.CaptureContext(); }
    }

    public async Task SelectAsync(string preferenceId, int? index, AudioTrackInfo? track, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferenceId);
        if (index.HasValue && (index < 0 || track == null)) throw new ArgumentException("The selected audio track is unavailable.");
        var owner = client.CaptureContext();
        long generation, choice;
        lock (_gate)
        {
            if (_owner != owner) Clear();
            generation = _generation; choice = ++_choice; _selectedIndex = index;
        }
        // Capture the full signature before a later version/navigation can replace its inventory.
        var preference = index.HasValue ? CreatePreference(index.Value, track!) : null;
        // Process even cancellation in click order, so rollback uses the last confirmed write.
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (preference == null) await catalog.DeleteAudioPrefsAsync(owner, preferenceId, ct).ConfigureAwait(false);
            else await catalog.SetAudioPrefsAsync(owner, preferenceId, preference, ct).ConfigureAwait(false);
            lock (_gate) if (_generation == generation && client.IsCurrentContext(owner)) _savedIndex = index;
        }
        catch
        {
            lock (_gate) if (_generation == generation && _choice == choice)
                _selectedIndex = client.IsCurrentContext(owner) ? _savedIndex : null;
            throw;
        }
        finally { _writes.Release(); }
    }

    public static Core.Models.Catalog.AudioPreference CreatePreference(int index, AudioTrackInfo track) => new()
    {
        AudioTrackIndex = index,
        AudioLanguage = track.Language,
        TrackSignature = new()
        {
            Language = track.Language, Title = track.Title, EmbeddedTitle = track.EmbeddedTitle,
            Codec = track.Codec, Layout = track.Layout, Channels = Math.Max(0, track.Channels ?? 0), Default = track.Default,
        },
    };
}
