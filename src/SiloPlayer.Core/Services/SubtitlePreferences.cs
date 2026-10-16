using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Settings;
using System.Text.Json;

namespace SiloPlayer.Core.Services;

public sealed record SubtitlePreferenceSnapshot(ApiRequestContext Context, SubtitleAppearance Appearance,
    string AppearanceSource, int OffsetMs, string OffsetSource, bool SupportsProfileTiming)
{
    public bool IsAppearanceCustomized => AppearanceSource != "default";
}

/// <summary>Profile subtitle defaults, resolved for one captured account/device owner.</summary>
public sealed class SubtitlePreferences(SettingsApi api)
{
    // One SettingsApi owns these lanes. A reservation records explicit intent
    // before a player's debounce, while the gate covers HTTP plus shadow cleanup.
    private readonly object _writeLock = new();
    private readonly Dictionary<(ApiRequestContext Context, string Key), WriteLane> _writes = [];
    internal sealed class WriteLane { internal long Latest; internal readonly SemaphoreSlim Gate = new(1, 1); }
    public sealed class WriteIntent
    {
        internal WriteIntent(ApiRequestContext context, string key, WriteLane lane, long sequence)
            => (Context, Key, Lane, Sequence) = (context, key, lane, sequence);
        internal ApiRequestContext Context { get; }
        internal string Key { get; }
        internal WriteLane Lane { get; }
        internal long Sequence { get; }
    }
    private WriteIntent Reserve(ApiRequestContext context, string key)
    {
        EnsureCurrent(context, default);
        lock (_writeLock)
        {
            if (!_writes.TryGetValue((context, key), out var lane)) _writes[(context, key)] = lane = new();
            return new(context, key, lane, ++lane.Latest);
        }
    }
    public WriteIntent ReserveOffsetIntent(SubtitlePreferenceSnapshot snapshot) => Reserve(snapshot.Context, OffsetKey);
    public bool IsLatestIntent(WriteIntent intent)
    {
        lock (_writeLock) return intent.Sequence == intent.Lane.Latest && api.IsCurrentContext(intent.Context);
    }
    public async Task WaitForOffsetWritesAsync(WriteIntent intent, CancellationToken ct = default)
    {
        EnsureCurrent(intent.Context, ct);
        await intent.Lane.Gate.WaitAsync(ct);
        try { EnsureCurrent(intent.Context, ct); }
        finally { intent.Lane.Gate.Release(); }
    }
    private async Task<bool> MutateAsync(SubtitlePreferenceSnapshot snapshot, string key, Func<Task> write,
        CancellationToken ct, WriteIntent? intent = null)
    {
        EnsureCurrent(snapshot.Context, ct);
        intent ??= Reserve(snapshot.Context, key);
        if (intent.Context != snapshot.Context || intent.Key != key) throw new ArgumentException("Preference intent belongs to another owner or key.", nameof(intent));
        await intent.Lane.Gate.WaitAsync(ct);
        try
        {
            EnsureCurrent(snapshot.Context, ct);
            // A newer surface intent supersedes queued/debounced work. Once
            // dispatched, an accepted write is allowed to settle before the next.
            if (!IsLatestIntent(intent)) return false;
            await write();
            EnsureCurrent(snapshot.Context, ct);
            if (IsLatestIntent(intent)) Publish(snapshot.Context, key, ct);
            return true;
        }
        finally { intent.Lane.Gate.Release(); }
    }
    public const string AppearanceKey = "playback.subtitle_appearance";
    public const string OffsetKey = "player.subtitle_sync_ms";
    public static int ClampOffset(int milliseconds) => Math.Clamp(milliseconds, -10000, 10000);
    public async Task<SubtitlePreferenceSnapshot> LoadAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        SettingsContractCapabilities? capabilities;
        try { capabilities = await api.GetContractCapabilitiesAsync(context, ct); }
        catch (ApiException ex) when (ex.StatusCode == 404) { capabilities = null; }
        EnsureCurrent(context, ct);
        var response = await api.GetContractEffectiveSettingsAsync(context, [AppearanceKey, OffsetKey], ct: ct);
        EnsureCurrent(context, ct);
        var appearance = response.Settings.FirstOrDefault(entry => entry.Key == AppearanceKey);
        var offset = response.Settings.FirstOrDefault(entry => entry.Key == OffsetKey);
        var milliseconds = offset?.Value.ValueKind == JsonValueKind.Number && offset.Value.TryGetInt32(out var value) ? ClampOffset(value) : 0;
        return new(context, SubtitleAppearance.Parse(appearance?.Value.GetRawText()), appearance?.Source ?? "default",
            milliseconds, offset?.Source ?? "default", capabilities is { ApiVersion: 1, Revision: >= 19 });
    }

    public async Task SaveAppearanceAsync(SubtitlePreferenceSnapshot snapshot, SubtitleAppearance value, CancellationToken ct = default)
    {
        EnsureCurrent(snapshot.Context, ct);
        using var document = JsonDocument.Parse(value.ToJson());
        await MutateAsync(snapshot, AppearanceKey, () => api.SaveProfileDefaultAsync(snapshot.Context, AppearanceKey,
            document.RootElement.Clone(), snapshot.AppearanceSource == "profile_device", ct), ct);
    }

    public async Task ResetAppearanceAsync(SubtitlePreferenceSnapshot snapshot, CancellationToken ct = default)
    {
        await MutateAsync(snapshot, AppearanceKey, () => api.ResetProfileDefaultAsync(snapshot.Context, AppearanceKey, ct), ct);
    }

    public Task<bool> SaveOffsetAsync(SubtitlePreferenceSnapshot snapshot, int milliseconds, CancellationToken ct = default, WriteIntent? intent = null)
    {
        return MutateAsync(snapshot, OffsetKey, () => snapshot.SupportsProfileTiming
            ? api.SaveProfileDefaultAsync(snapshot.Context, OffsetKey, ClampOffset(milliseconds), snapshot.OffsetSource == "profile_device", ct)
            : api.SetContractSettingValueAsync(snapshot.Context, OffsetKey, "profile_device", ClampOffset(milliseconds), ct: ct), ct, intent);
    }

    public async Task ResetOffsetAsync(SubtitlePreferenceSnapshot snapshot, CancellationToken ct = default)
    {
        await MutateAsync(snapshot, OffsetKey, async () =>
        {
            if (snapshot.SupportsProfileTiming) await api.ResetProfileDefaultAsync(snapshot.Context, OffsetKey, ct);
            else
            {
                try { await api.DeleteContractSettingValueAsync(snapshot.Context, OffsetKey, "profile_device", ct: ct); }
                catch (ApiException ex) when (ex.StatusCode == 404) { }
            }
        }, ct);
    }

    private void Publish(ApiRequestContext context, string key, CancellationToken ct)
    {
        EnsureCurrent(context, ct);
        api.NotifyLocalizedSettingChanged(key);
    }

    private void EnsureCurrent(ApiRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!api.IsCurrentContext(context)) throw new OperationCanceledException("The subtitle preference owner changed.", ct);
    }
}
