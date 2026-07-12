using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Services;

/// <summary>
/// Four corners a card overlay badge can live in. Mirrors the WebUI
/// OverlayPosition type in web/src/lib/overlays.
/// </summary>
public enum OverlayPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Per-overlay config: whether it shows and which corner it lives in.
/// </summary>
public record OverlayItemConfig(
    bool Enabled,
    OverlayPosition Position,
    string? AccentColor = null,
    bool? ShowIcon = null);

/// <summary>
/// Flat data bag passed to the renderer, extracted from a MediaItem once and
/// shared across all value extractors in OverlayRegistry.
/// </summary>
public sealed class OverlayData
{
    public string? Resolution { get; set; }
    public string? Hdr { get; set; }
    public string? Audio { get; set; }
    public string? AudioChannels { get; set; }
    public string? VideoCodec { get; set; }
    public string? Container { get; set; }
    public string? AspectRatio { get; set; }
    public string? ReleaseType { get; set; }
    public string? Edition { get; set; }
    public bool MultiAudio { get; set; }
    public bool MultiSub { get; set; }
    public double? RatingImdb { get; set; }
    public double? RatingTmdb { get; set; }
    public int? RatingRtCritic { get; set; }
    public int? RatingRtAudience { get; set; }
    public string? ContentRating { get; set; }
    public int? Year { get; set; }
    public int? Runtime { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? Studio { get; set; }
    public string? Network { get; set; }
    public string? ShowStatus { get; set; }

    public static OverlayData FromMediaItem(MediaItem item) => new()
    {
        Resolution = item.OverlaySummary?.Resolution,
        Hdr = item.OverlaySummary?.Hdr,
        Audio = item.OverlaySummary?.Audio,
        AudioChannels = item.OverlaySummary?.AudioChannels,
        VideoCodec = item.OverlaySummary?.VideoCodec,
        Container = item.OverlaySummary?.Container,
        AspectRatio = item.OverlaySummary?.AspectRatio,
        ReleaseType = item.OverlaySummary?.ReleaseType,
        Edition = item.OverlaySummary?.Edition,
        MultiAudio = item.OverlaySummary?.MultiAudio == true,
        MultiSub = item.OverlaySummary?.MultiSub == true,
        RatingImdb = item.RatingImdb,
        RatingTmdb = item.RatingTmdb,
        RatingRtCritic = item.RatingRtCritic,
        RatingRtAudience = item.RatingRtAudience,
        ContentRating = item.ContentRating,
        Year = item.Year > 0 ? item.Year : null,
        Runtime = item.Runtime > 0 ? item.Runtime : null,
        OriginalLanguage = item.OriginalLanguage,
        Studio = item.Studios.FirstOrDefault(),
        Network = item.Networks.FirstOrDefault(),
        ShowStatus = item.ShowStatus,
    };
}

/// <summary>
/// A single overlay definition: stable ID, display metadata, default corner,
/// default enabled state, and a value extractor.
/// </summary>
public sealed record OverlayDef(
    string Id,
    string Label,
    OverlayPosition DefaultPosition,
    bool DefaultEnabled,
    Func<OverlayData, string?> GetValue);

/// <summary>
/// Version 2 server/WebUI overlay document. Desktop currently consumes preset
/// and order for compatibility, while rendering still uses the native badge
/// style and registry order.
/// </summary>
public sealed record CardOverlayPrefs(
    int Version,
    string Preset,
    IReadOnlyList<string> Order,
    Dictionary<string, OverlayItemConfig> Items);

/// <summary>
/// Static list of supported card overlays, in WebUI order and defaults.
/// </summary>
public static class OverlayRegistry
{
    public static readonly IReadOnlyList<OverlayDef> All =
    [
        new("resolution",         "Resolution",       OverlayPosition.TopLeft,     true,  d => FormatResolution(d.Resolution)),
        new("hdr",                "HDR",              OverlayPosition.TopLeft,     true,  d => string.IsNullOrEmpty(d.Hdr) ? null : d.Hdr),
        new("resolution_hdr",     "Resolution + HDR", OverlayPosition.TopLeft,     false, d => FormatResolutionHdr(d.Resolution, d.Hdr)),
        new("audio",              "Audio",            OverlayPosition.TopLeft,     true,  d => string.IsNullOrEmpty(d.Audio) ? null : d.Audio),
        new("audio_channels",     "Audio Channels",   OverlayPosition.TopLeft,     false, d => string.IsNullOrEmpty(d.AudioChannels) ? null : d.AudioChannels),
        new("video_codec",        "Video Codec",      OverlayPosition.TopLeft,     false, d => string.IsNullOrEmpty(d.VideoCodec) ? null : d.VideoCodec),
        new("container",          "Container",        OverlayPosition.BottomLeft,  false, d => string.IsNullOrEmpty(d.Container) ? null : d.Container),
        new("aspect_ratio",       "Aspect Ratio",     OverlayPosition.BottomRight, false, d => string.IsNullOrEmpty(d.AspectRatio) ? null : d.AspectRatio),
        new("release_type",       "Release Type",     OverlayPosition.BottomLeft,  true,  d => string.IsNullOrEmpty(d.ReleaseType) ? null : d.ReleaseType),
        new("edition",            "Edition",          OverlayPosition.BottomLeft,  false, d => string.IsNullOrEmpty(d.Edition) ? null : d.Edition),
        new("multi_audio",        "Multi-Audio",      OverlayPosition.BottomRight, false, d => d.MultiAudio ? "Multi-Audio" : null),
        new("multi_sub",          "Subtitles",        OverlayPosition.BottomRight, false, d => d.MultiSub ? "CC" : null),
        new("rating_imdb",        "IMDb",             OverlayPosition.TopRight,    false, d => d.RatingImdb.HasValue ? d.RatingImdb.Value.ToString("0.0") : null),
        new("rating_tmdb",        "TMDB",             OverlayPosition.TopRight,    false, d => d.RatingTmdb.HasValue ? d.RatingTmdb.Value.ToString("0.0") : null),
        new("rating_rt",          "RT",               OverlayPosition.TopRight,    false, d => d.RatingRtCritic.HasValue ? $"{d.RatingRtCritic.Value}%" : null),
        new("rating_rt_audience", "RT Audience",      OverlayPosition.TopRight,    false, d => d.RatingRtAudience.HasValue ? $"{d.RatingRtAudience.Value}%" : null),
        new("content_rating",     "Age Rating",       OverlayPosition.BottomRight, false, d => string.IsNullOrEmpty(d.ContentRating) ? null : d.ContentRating),
        new("year",               "Year",             OverlayPosition.BottomLeft,  false, d => d.Year is > 0 ? d.Year.Value.ToString() : null),
        new("runtime",            "Runtime",          OverlayPosition.BottomLeft,  false, d => FormatRuntime(d.Runtime)),
        new("original_language",  "Language",         OverlayPosition.BottomLeft,  false, d => string.IsNullOrEmpty(d.OriginalLanguage) ? null : d.OriginalLanguage.ToUpperInvariant()),
        new("studio",             "Studio",           OverlayPosition.BottomRight, false, d => string.IsNullOrEmpty(d.Studio) ? null : d.Studio),
        new("network",            "Network",          OverlayPosition.BottomRight, false, d => string.IsNullOrEmpty(d.Network) ? null : d.Network),
        new("show_status",        "Show Status",      OverlayPosition.TopRight,    false, d => FormatShowStatus(d.ShowStatus)),
        new("imdb_top_250",       "IMDb Top 250",     OverlayPosition.TopRight,    false, _ => null),
        new("rt_certified_fresh", "Certified Fresh",  OverlayPosition.TopRight,    false, _ => null),
    ];

    public static Dictionary<string, OverlayItemConfig> DefaultPrefs =>
        All.ToDictionary(def => def.Id, def => new OverlayItemConfig(def.DefaultEnabled, def.DefaultPosition));

    public static bool SuppressesStandaloneOverlays(string overlayId, IReadOnlyDictionary<string, OverlayItemConfig> prefs)
    {
        return (overlayId == "resolution" || overlayId == "hdr") &&
            prefs.TryGetValue("resolution_hdr", out var combined) &&
            combined.Enabled;
    }

    private static string? FormatResolution(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Equals("2160p", StringComparison.OrdinalIgnoreCase)) return "4K";
        if (trimmed.Equals("4320p", StringComparison.OrdinalIgnoreCase)) return "8K";
        if (trimmed.EndsWith('p')) return trimmed.ToLowerInvariant();
        return trimmed.ToUpperInvariant();
    }

    private static string? FormatResolutionHdr(string? resolution, string? hdr)
    {
        var res = FormatResolution(resolution);
        if (res == null) return null;
        if (string.IsNullOrWhiteSpace(hdr)) return res;
        var suffix = hdr.Contains("DV", StringComparison.OrdinalIgnoreCase) ? "DV" : "HDR";
        return $"{res} {suffix}";
    }

    private static string? FormatRuntime(int? minutes)
    {
        if (minutes is null or <= 0) return null;
        var hours = minutes.Value / 60;
        var mins = minutes.Value % 60;
        return hours > 0 ? $"{hours}h {mins}m" : $"{mins}m";
    }

    private static string? FormatShowStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "returning" or "returning series" or "in_production" or "in production" => "Returning",
            "ended" => "Ended",
            "cancelled" or "canceled" => "Cancelled",
            _ => value,
        };
    }
}

/// <summary>
/// Loads and caches card overlay preferences. Priority:
/// 1. User setting card_overlays
/// 2. Admin defaults from /settings/overlay-config
/// 3. built-in defaults
/// </summary>
public class CardOverlayService
{
    private readonly SettingsApi _settingsApi;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _enabled = true;
    private CardOverlayPrefs _document = BuildDefaultDocument();

    public CardOverlayService(SettingsApi settingsApi)
    {
        _settingsApi = settingsApi;
    }

    public bool Enabled => _enabled;
    public string Preset => _document.Preset;
    public Dictionary<string, OverlayItemConfig>? GetPrefs() => _enabled ? _document.Items : null;

    public CardOverlayPrefs GetDocument() => new(
        2,
        _document.Preset,
        _document.Order.ToArray(),
        _document.Items.ToDictionary(pair => pair.Key, pair => pair.Value));

    public async Task EnsureLoadedAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            try
            {
                var config = await _settingsApi.GetOverlayConfigAsync();
                _enabled = config.Enabled;
                if (!string.IsNullOrWhiteSpace(config.Defaults))
                    _document = ParsePrefs(config.Defaults!) ?? _document;
            }
            catch { }

            try
            {
                var userSetting = await _settingsApi.GetSettingAsync("card_overlays");
                if (!string.IsNullOrWhiteSpace(userSetting?.Value))
                {
                    var parsed = ParsePrefs(userSetting!.Value);
                    if (parsed != null) _document = parsed;
                }
            }
            catch { }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Invalidate()
    {
        _initialized = false;
    }

    public async Task SaveAsync(CardOverlayPrefs prefs, CancellationToken ct = default)
    {
        var normalized = NormalizeDocument(prefs);
        await _settingsApi.PutSettingAsync("card_overlays", SerializePrefs(normalized), ct);
        _document = normalized;
        _initialized = true;
    }

    private static CardOverlayPrefs? ParsePrefs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var root = doc.RootElement;
            var source = root;
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
                source = items;
            var version = root.TryGetProperty("version", out var versionElement) && versionElement.TryGetInt32(out var parsedVersion)
                ? parsedVersion
                : 1;

            var result = OverlayRegistry.DefaultPrefs;
            foreach (var def in OverlayRegistry.All)
            {
                if (!source.TryGetProperty(def.Id, out var entry) || entry.ValueKind != JsonValueKind.Object)
                    continue;

                var enabled = def.DefaultEnabled;
                if (entry.TryGetProperty("enabled", out var en))
                {
                    if (en.ValueKind == JsonValueKind.True) enabled = true;
                    else if (en.ValueKind == JsonValueKind.False) enabled = false;
                }

                var position = def.DefaultPosition;
                if (entry.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.String)
                    position = ParsePosition(pos.GetString() ?? "") ?? def.DefaultPosition;

                string? accentColor = null;
                if (entry.TryGetProperty("accentColor", out var accent) && accent.ValueKind == JsonValueKind.String)
                {
                    var candidate = accent.GetString();
                    if (IsHexColor(candidate)) accentColor = candidate;
                }

                bool? showIcon = null;
                if (entry.TryGetProperty("showIcon", out var icon))
                {
                    if (icon.ValueKind == JsonValueKind.True) showIcon = true;
                    else if (icon.ValueKind == JsonValueKind.False) showIcon = false;
                }

                result[def.Id] = new OverlayItemConfig(enabled, position, accentColor, showIcon);
            }

            var preset = root.TryGetProperty("preset", out var presetElement) && presetElement.ValueKind == JsonValueKind.String
                ? presetElement.GetString() ?? "classic"
                : "classic";
            var order = new List<string>();
            if (root.TryGetProperty("order", out var orderElement) && orderElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in orderElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } id)
                        order.Add(id);
                }
            }

            return NormalizeDocument(new CardOverlayPrefs(version, preset, order, result));
        }
        catch
        {
            return null;
        }
    }

    private static OverlayPosition? ParsePosition(string s) => s switch
    {
        "top-left" => OverlayPosition.TopLeft,
        "top-right" => OverlayPosition.TopRight,
        "bottom-left" => OverlayPosition.BottomLeft,
        "bottom-right" => OverlayPosition.BottomRight,
        _ => null,
    };

    public static string FormatPosition(OverlayPosition position) => position switch
    {
        OverlayPosition.TopLeft => "top-left",
        OverlayPosition.TopRight => "top-right",
        OverlayPosition.BottomLeft => "bottom-left",
        OverlayPosition.BottomRight => "bottom-right",
        _ => "top-left",
    };

    private static CardOverlayPrefs BuildDefaultDocument() =>
        new(2, "classic", [], OverlayRegistry.DefaultPrefs);

    private static CardOverlayPrefs NormalizeDocument(CardOverlayPrefs prefs)
    {
        var preset = prefs.Preset is "minimal" or "classic" or "vibrant" or "pill" or "square"
            ? prefs.Preset
            : "classic";
        var items = OverlayRegistry.DefaultPrefs;
        foreach (var def in OverlayRegistry.All)
        {
            if (!prefs.Items.TryGetValue(def.Id, out var value))
                continue;
            items[def.Id] = value with
            {
                AccentColor = IsHexColor(value.AccentColor) ? value.AccentColor : null,
            };
        }
        var known = OverlayRegistry.All.Select(def => def.Id).ToHashSet(StringComparer.Ordinal);
        var order = prefs.Order.Where(known.Contains).Distinct(StringComparer.Ordinal).ToArray();
        return new CardOverlayPrefs(2, preset, order, items);
    }

    private static string SerializePrefs(CardOverlayPrefs prefs)
    {
        var items = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var def in OverlayRegistry.All)
        {
            var config = prefs.Items[def.Id];
            var item = new Dictionary<string, object?>
            {
                ["enabled"] = config.Enabled,
                ["position"] = FormatPosition(config.Position),
            };
            if (config.AccentColor is not null) item["accentColor"] = config.AccentColor;
            if (config.ShowIcon is bool showIcon) item["showIcon"] = showIcon;
            items[def.Id] = item;
        }
        var root = new Dictionary<string, object?>
        {
            ["version"] = 2,
            ["preset"] = prefs.Preset,
            ["order"] = prefs.Order,
            ["items"] = items,
        };
        return JsonSerializer.Serialize(root);
    }

    private static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}
