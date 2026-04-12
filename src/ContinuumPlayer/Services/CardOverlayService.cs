using System.Text.Json;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Services;

/// <summary>
/// 4 corners a card overlay badge can live in. Mirrors the webui
/// <c>OverlayPosition</c> type in <c>web/src/lib/cardOverlays.ts</c>.
/// </summary>
public enum OverlayPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Per-overlay config: whether it shows + which corner it lives in.
/// </summary>
public record OverlayItemConfig(bool Enabled, OverlayPosition Position);

/// <summary>
/// Flat "data bag" passed to the renderer — extracted from a MediaItem once
/// and shared across all 9 value extractors in <see cref="OverlayRegistry"/>.
/// </summary>
public sealed class OverlayData
{
    public string? Resolution { get; set; }
    public string? Hdr { get; set; }
    public string? Audio { get; set; }
    public string? ReleaseType { get; set; }
    public double? RatingImdb { get; set; }
    public double? RatingTmdb { get; set; }
    public int? RatingRtCritic { get; set; }
    public int? RatingRtAudience { get; set; }
    public string? OriginalLanguage { get; set; }

    public static OverlayData FromMediaItem(MediaItem item) => new()
    {
        Resolution = item.OverlaySummary?.Resolution,
        Hdr = item.OverlaySummary?.Hdr,
        Audio = item.OverlaySummary?.Audio,
        ReleaseType = item.OverlaySummary?.ReleaseType,
        RatingImdb = item.RatingImdb,
        RatingTmdb = item.RatingTmdb,
        RatingRtCritic = item.RatingRtCritic,
        RatingRtAudience = item.RatingRtAudience,
        OriginalLanguage = item.OriginalLanguage,
    };
}

/// <summary>
/// A single overlay definition — a stable ID, display metadata, default
/// corner, and a function that pulls the badge's display text out of the
/// shared <see cref="OverlayData"/>. Mirrors the webui <c>OVERLAY_REGISTRY</c>.
/// </summary>
public sealed record OverlayDef(
    string Id,
    string Label,
    OverlayPosition DefaultPosition,
    Func<OverlayData, string?> GetValue);

/// <summary>
/// Static list of all supported card overlays, in the same order and with
/// the same defaults as the webui. PosterCard iterates this list to decide
/// which badges to render per item.
/// </summary>
public static class OverlayRegistry
{
    public static readonly IReadOnlyList<OverlayDef> All =
    [
        new OverlayDef("resolution",        "Resolution",    OverlayPosition.TopLeft,     d => string.IsNullOrEmpty(d.Resolution) ? null : d.Resolution.ToUpperInvariant()),
        new OverlayDef("hdr",               "HDR",           OverlayPosition.TopLeft,     d => string.IsNullOrEmpty(d.Hdr) ? null : d.Hdr),
        new OverlayDef("audio",             "Audio",         OverlayPosition.TopLeft,     d => string.IsNullOrEmpty(d.Audio) ? null : d.Audio),
        new OverlayDef("release_type",      "Release Type",  OverlayPosition.BottomLeft,  d => string.IsNullOrEmpty(d.ReleaseType) ? null : d.ReleaseType),
        new OverlayDef("rating_imdb",       "IMDb",          OverlayPosition.TopRight,    d => d.RatingImdb.HasValue ? d.RatingImdb.Value.ToString("0.0") : null),
        new OverlayDef("rating_tmdb",       "TMDB",          OverlayPosition.TopRight,    d => d.RatingTmdb.HasValue ? d.RatingTmdb.Value.ToString("0.0") : null),
        new OverlayDef("rating_rt",         "RT",            OverlayPosition.TopRight,    d => d.RatingRtCritic.HasValue ? $"{d.RatingRtCritic.Value}%" : null),
        new OverlayDef("rating_rt_audience","RT Audience",   OverlayPosition.TopRight,    d => d.RatingRtAudience.HasValue ? $"{d.RatingRtAudience.Value}%" : null),
        new OverlayDef("original_language", "Language",      OverlayPosition.BottomLeft,  d => string.IsNullOrEmpty(d.OriginalLanguage) ? null : d.OriginalLanguage.ToUpperInvariant()),
    ];

    /// <summary>
    /// Kometa-inspired default prefs (matches webui <c>DEFAULT_PREFS</c>) —
    /// tech trio on, ratings + language opt-in.
    /// </summary>
    public static Dictionary<string, OverlayItemConfig> DefaultPrefs => new()
    {
        ["resolution"]        = new(true,  OverlayPosition.TopLeft),
        ["hdr"]               = new(true,  OverlayPosition.TopLeft),
        ["audio"]             = new(true,  OverlayPosition.TopLeft),
        ["release_type"]      = new(true,  OverlayPosition.BottomLeft),
        ["rating_imdb"]       = new(false, OverlayPosition.TopRight),
        ["rating_tmdb"]       = new(false, OverlayPosition.TopRight),
        ["rating_rt"]         = new(false, OverlayPosition.TopRight),
        ["rating_rt_audience"]= new(false, OverlayPosition.TopRight),
        ["original_language"] = new(false, OverlayPosition.BottomLeft),
    };
}

/// <summary>
/// Loads and caches the card overlay preferences (server-wide kill switch,
/// admin defaults, and per-user override). Priority:
///   1. User setting <c>card_overlays</c>
///   2. Admin defaults from <c>/settings/overlay-config</c>
///   3. <see cref="OverlayRegistry.DefaultPrefs"/>
/// Admin kill switch collapses to <c>null</c> — PosterCard hides all badges.
/// </summary>
public class CardOverlayService
{
    private readonly SettingsApi _settingsApi;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _enabled = true;
    private Dictionary<string, OverlayItemConfig> _prefs = OverlayRegistry.DefaultPrefs;

    public CardOverlayService(SettingsApi settingsApi)
    {
        _settingsApi = settingsApi;
    }

    /// <summary>
    /// Current resolved prefs. Returns null when the admin kill switch is
    /// off (overlays disabled server-wide). Safe to call before
    /// <see cref="EnsureLoadedAsync"/> — returns hard-coded defaults until
    /// the server call lands.
    /// </summary>
    public Dictionary<string, OverlayItemConfig>? GetPrefs() => _enabled ? _prefs : null;

    /// <summary>
    /// One-shot server fetch. Subsequent calls are no-ops. Called
    /// opportunistically by the first PosterCard that binds; failures are
    /// swallowed and built-in defaults remain in effect.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            // Admin config — kill switch + default prefs.
            try
            {
                var config = await _settingsApi.GetOverlayConfigAsync();
                _enabled = config.Enabled;
                if (!string.IsNullOrWhiteSpace(config.Defaults))
                    _prefs = ParsePrefs(config.Defaults!) ?? _prefs;
            }
            catch { /* admin config optional */ }

            // Per-user override — takes priority if present.
            try
            {
                var userSetting = await _settingsApi.GetSettingAsync("card_overlays");
                if (!string.IsNullOrWhiteSpace(userSetting?.Value))
                {
                    var parsed = ParsePrefs(userSetting!.Value);
                    if (parsed != null) _prefs = parsed;
                }
            }
            catch { /* user override optional */ }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Force a re-fetch on the next <see cref="EnsureLoadedAsync"/> call.
    /// Call this from SettingsPage after the user saves new overlay prefs.
    /// </summary>
    public void Invalidate()
    {
        _initialized = false;
    }

    private static Dictionary<string, OverlayItemConfig>? ParsePrefs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, OverlayItemConfig>(OverlayRegistry.DefaultPrefs);
            foreach (var def in OverlayRegistry.All)
            {
                if (!doc.RootElement.TryGetProperty(def.Id, out var entry)) continue;
                bool enabled = entry.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True;
                OverlayPosition position = def.DefaultPosition;
                if (entry.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.String)
                    position = ParsePosition(pos.GetString() ?? "") ?? def.DefaultPosition;
                result[def.Id] = new OverlayItemConfig(enabled, position);
            }
            return result;
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
}
