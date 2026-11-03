using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public readonly record struct LibraryPageStateSnapshot(bool Remember, string? Search);

/// <summary>One confirmed document and one write queue per injected settings client.</summary>
public sealed class LibraryPageStateStore(SettingsApi settings)
{
    internal sealed class Session
    {
        private SemaphoreSlim _operations = new(1, 1);
        private SettingsRequestContext? _operationOwner;
        internal readonly object Sync = new();
        internal long Generation;
        internal Document? Confirmed;
        internal event Action? Invalidated;
        internal SemaphoreSlim OperationsFor(SettingsRequestContext owner, SettingsApi settings)
        {
            lock (Sync)
            {
                if (!settings.IsCurrentSettingsContext(owner)) throw new OperationCanceledException("Library settings owner changed.");
                if (_operationOwner != owner) { _operationOwner = owner; _operations = new(1, 1); }
                return _operations;
            }
        }
        internal void Invalidate()
        {
            lock (Sync) { Generation++; Confirmed = null; }
            Invalidated?.Invoke();
        }
    }
    internal sealed record Document(SettingsRequestContext Owner, long Generation, bool Remember, Dictionary<string, string> Libraries);
    private Session State => settings.LibraryStateSession;
    private static readonly string[] Keys = ["ui.remember_library_page_state", "ui.library_page_state"];
    public static bool IsRelevantKey(string? key) => key is "ui.remember_library_page_state" or "ui.library_page_state";
    public long Revision { get { lock (State.Sync) return State.Generation; } }
    public event Action? Invalidated { add => State.Invalidated += value; remove => State.Invalidated -= value; }
    public void Invalidate() => State.Invalidate();

    public bool TryGetConfirmed(int libraryId, out LibraryPageStateSnapshot snapshot)
    {
        var owner = settings.CaptureSettingsContext();
        lock (State.Sync)
        {
            if (State.Confirmed is { } saved && saved.Owner == owner && saved.Generation == State.Generation)
            {
                snapshot = Snapshot(saved, libraryId);
                return true;
            }
        }
        snapshot = default;
        return false;
    }

    public void NotifyRealtime(string channel, string eventName, JsonElement data)
    {
        if (channel != "user_settings" || eventName != "user_settings.changed" || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String || !IsRelevantKey(key.GetString())) return;
        if (data.TryGetProperty("profile_id", out var profile) && profile.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(profile.GetString()) && profile.GetString() != settings.CaptureContext().ProfileId) return;
        Invalidate();
    }

    public async Task<string?> ReadAsync(int libraryId, CancellationToken ct = default)
    {
        var result = await ReadSnapshotAsync(libraryId, ct);
        return result.Remember ? result.Search : null;
    }

    public async Task<LibraryPageStateSnapshot> ReadSnapshotAsync(int libraryId, CancellationToken ct = default)
    {
        var owner = settings.CaptureSettingsContext();
        var operations = State.OperationsFor(owner, settings);
        await operations.WaitAsync(ct);
        try
        {
            while (true)
            {
                EnsureOwner(owner, ct);
                long generation;
                lock (State.Sync)
                {
                    generation = State.Generation;
                    if (State.Confirmed is { } saved && saved.Owner == owner && saved.Generation == generation)
                        return Snapshot(saved, libraryId);
                }
                var (remember, libraries, _) = await ReadDocumentAsync(owner, ct).WaitAsync(ct);
                EnsureOwner(owner, ct);
                lock (State.Sync)
                {
                    if (generation != State.Generation) continue;
                    var saved = new Document(owner, generation, remember, libraries);
                    State.Confirmed = saved;
                    return Snapshot(saved, libraryId);
                }
            }
        }
        finally { operations.Release(); }
    }

    public async Task WriteAsync(int libraryId, string search, CancellationToken ct = default)
    {
        var owner = settings.CaptureSettingsContext();
        var operations = State.OperationsFor(owner, settings);
        await operations.WaitAsync(ct);
        try
        {
            while (true)
            {
                EnsureOwner(owner, ct);
                long generation;
                lock (State.Sync) generation = State.Generation;
                // Merge into the authoritative document so concurrent libraries survive.
                var (remember, libraries, value) = await ReadDocumentAsync(owner, ct);
                EnsureOwner(owner, ct);
                Task? write = null;
                lock (State.Sync)
                {
                    if (generation != State.Generation) continue;
                    if (remember && value != null && libraries.GetValueOrDefault(libraryId.ToString()) != search)
                    {
                        var id = libraryId.ToString();
                        libraries[id] = search;
                        var saved = (JsonObject)value["libraries"]!;
                        if (saved[id] is not JsonObject entry)
                        {
                            entry = new JsonObject();
                            saved[id] = entry;
                        }
                        entry["search"] = search;
                        write = settings.WriteLibraryStateDocumentAsync(owner, value, ct);
                    }
                }
                if (write != null) await write;
                EnsureOwner(owner, ct);
                lock (State.Sync)
                    if (generation == State.Generation) State.Confirmed = new(owner, generation, remember, libraries);
                return;
            }
        }
        finally { operations.Release(); }
    }

    private static LibraryPageStateSnapshot Snapshot(Document document, int libraryId)
        => new(document.Remember, document.Remember ? document.Libraries.GetValueOrDefault(libraryId.ToString()) : null);
    private void EnsureOwner(SettingsRequestContext owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!settings.IsCurrentSettingsContext(owner)) throw new OperationCanceledException("Library settings owner changed.", ct);
    }
    private async Task<(bool Remember, Dictionary<string, string> Libraries, JsonObject? Value)> ReadDocumentAsync(SettingsRequestContext owner, CancellationToken ct)
    {
        var response = await settings.GetContractEffectiveSettingsAsync(owner.Request, Keys, deviceId: owner.DeviceId, ct: ct);
        var remember = response.Settings.FirstOrDefault(entry => entry.Key == Keys[0])?.Value;
        var enabled = remember is not { ValueKind: JsonValueKind.False } && !(remember is { ValueKind: JsonValueKind.String } text && text.GetString() == "false");
        var libraries = new Dictionary<string, string>(StringComparer.Ordinal);
        var state = response.Settings.FirstOrDefault(entry => entry.Key == Keys[1])?.Value;
        JsonObject? value = null;
        try
        {
            if (state is null or { ValueKind: JsonValueKind.Null })
                value = new JsonObject { ["version"] = 1, ["libraries"] = new JsonObject() };
            else
            {
                if (state is { ValueKind: JsonValueKind.String } encoded)
                    state = JsonSerializer.Deserialize<JsonElement>(encoded.GetString() ?? "");
                if (state is { ValueKind: JsonValueKind.Object } doc &&
                    doc.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number &&
                    version.TryGetInt32(out var number) && number == 1 &&
                    doc.TryGetProperty("libraries", out var saved) && saved.ValueKind == JsonValueKind.Object)
                {
                    // Keep the original document, including entries/fields this client
                    // cannot interpret. Only the edited library's search is replaced.
                    value = JsonNode.Parse(doc.GetRawText())!.AsObject();
                    foreach (var entry in saved.EnumerateObject())
                        if (entry.Value.ValueKind == JsonValueKind.Object &&
                            entry.Value.TryGetProperty("search", out var search) && search.ValueKind == JsonValueKind.String)
                            libraries[entry.Name] = search.GetString() ?? "";
                }
            }
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        // A present but unrecognized document is not an empty writable document.
        return (enabled, libraries, value);
    }
}
