using System.Collections.Concurrent;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public sealed class LibraryPageStateStore(SettingsApi settings)
{
    private static readonly ConcurrentDictionary<ApiRequestContext, SemaphoreSlim> Gates = new();
    private static readonly string[] Keys = ["ui.remember_library_page_state", "ui.library_page_state"];

    public async Task<string?> ReadAsync(int libraryId, CancellationToken ct = default)
    {
        var context = settings.CaptureContext();
        var (remember, libraries) = await ReadDocumentAsync(ct);
        if (!settings.IsCurrentContext(context)) throw new OperationCanceledException("Library profile changed.", ct);
        return remember && libraries.TryGetValue(libraryId.ToString(), out var search) ? search : null;
    }

    public async Task WriteAsync(int libraryId, string search, CancellationToken ct = default)
    {
        var context = settings.CaptureContext();
        var gate = Gates.GetOrAdd(context, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!settings.IsCurrentContext(context)) return;
            var (remember, libraries) = await ReadDocumentAsync(ct);
            if (!remember || !settings.IsCurrentContext(context)) return;
            libraries[libraryId.ToString()] = search;
            var document = new { version = 1, libraries = libraries.ToDictionary(pair => pair.Key, pair => new { search = pair.Value }) };
            await settings.SetContractSettingValueAsync("ui.library_page_state", "profile_device", document, ct: ct);
        }
        finally { gate.Release(); }
    }

    private async Task<(bool Remember, Dictionary<string, string> Libraries)> ReadDocumentAsync(CancellationToken ct)
    {
        var response = await settings.GetContractEffectiveSettingsAsync(Keys, ct: ct);
        var remember = response.Settings.FirstOrDefault(entry => entry.Key == Keys[0])?.Value;
        var enabled = remember is not { ValueKind: JsonValueKind.False } && !(remember is { ValueKind: JsonValueKind.String } text && text.GetString() == "false");
        var libraries = new Dictionary<string, string>(StringComparer.Ordinal);
        var state = response.Settings.FirstOrDefault(entry => entry.Key == Keys[1])?.Value;
        try
        {
            if (state is { ValueKind: JsonValueKind.String } encoded) state = JsonSerializer.Deserialize<JsonElement>(encoded.GetString() ?? "{}");
            if (state is { ValueKind: JsonValueKind.Object } doc && doc.TryGetProperty("version", out var version) && version.GetInt32() == 1 && doc.TryGetProperty("libraries", out var saved))
                foreach (var entry in saved.EnumerateObject())
                    if (entry.Value.TryGetProperty("search", out var search) && search.ValueKind == JsonValueKind.String) libraries[entry.Name] = search.GetString() ?? "";
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        return (enabled, libraries);
    }
}
