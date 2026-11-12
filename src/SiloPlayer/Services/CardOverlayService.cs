using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

/// <summary>
/// Corners and vertically centred edge rails. Mirrors the WebUI
/// OverlayPosition type in web/src/lib/overlays.
/// </summary>
public enum OverlayPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    LeftEdge,
    RightEdge,
}

/// <summary>
/// Per-overlay config: whether it shows and which corner it lives in.
/// </summary>
public record OverlayItemConfig(
    bool Enabled,
    OverlayPosition Position,
    string? AccentColor = null,
    bool? ShowIcon = null)
{
    public string? Image { get; init; }
    public IReadOnlyDictionary<string, string>? ValueImages { get; init; }
}

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
    public int? AdvisoryAge { get; set; }
    public int? Year { get; set; }
    public int? Runtime { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? Studio { get; set; }
    public string? Network { get; set; }
    public string? ShowStatus { get; set; }
    public string? RequestStatus { get; set; }

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
        AdvisoryAge = item.AdvisoryAge,
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
/// Version 2 server/WebUI overlay document. Desktop consumes the server's
/// preset, item settings, positions, and stable display order.
/// </summary>
public sealed record CardOverlayPrefs(
    int Version,
    string Preset,
    IReadOnlyList<string> Order,
    Dictionary<string, OverlayItemConfig> Items)
{
    public JsonElement? OriginalDocument { get; init; }
    public OverlayStyle? Style { get; init; }
}

/// <summary>
/// Static list of supported card overlays, in WebUI order and defaults.
/// </summary>
public static class OverlayRegistry
{
    public static readonly IReadOnlyList<OverlayDef> All =
    [
        new("resolution",         "Resolution",       OverlayPosition.TopLeft,     true,  d => FormatResolution(d.Resolution)),
        new("hdr",                "HDR / Dolby Vision", OverlayPosition.TopLeft,     true,  d => string.IsNullOrEmpty(d.Hdr) ? null : d.Hdr),
        new("resolution_hdr",     "Resolution + HDR (combined)", OverlayPosition.TopLeft, false, d => FormatResolutionHdr(d.Resolution, d.Hdr)),
        new("resolution_hdr_audio", "Resolution + HDR + Audio (combined)", OverlayPosition.TopLeft, false,
            d => JoinParts(FormatResolution(d.Resolution), FormatHdr(d.Hdr), d.Audio)),
        new("audio",              "Audio Codec",      OverlayPosition.TopLeft,     true,  d => string.IsNullOrEmpty(d.Audio) ? null : d.Audio),
        new("audio_channels",     "Audio Channels",   OverlayPosition.TopLeft,     false, d => string.IsNullOrEmpty(d.AudioChannels) ? null : d.AudioChannels),
        new("video_codec",        "Video Codec",      OverlayPosition.TopLeft,     false, d => string.IsNullOrEmpty(d.VideoCodec) ? null : d.VideoCodec),
        new("container",          "Container",        OverlayPosition.BottomLeft,  false, d => string.IsNullOrEmpty(d.Container) ? null : d.Container),
        new("aspect_ratio",       "Aspect Ratio",     OverlayPosition.BottomRight, false, d => string.IsNullOrEmpty(d.AspectRatio) ? null : d.AspectRatio),
        new("release_type",       "Release Type",     OverlayPosition.BottomLeft,  true,  d => string.IsNullOrEmpty(d.ReleaseType) ? null : d.ReleaseType),
        new("edition",            "Edition",          OverlayPosition.BottomLeft,  false, d => string.IsNullOrEmpty(d.Edition) ? null : d.Edition),
        new("multi_audio",        "Multi-Audio",      OverlayPosition.BottomRight, false, d => d.MultiAudio ? "Multi-Audio" : null),
        new("multi_sub",          "Subtitles Available", OverlayPosition.BottomRight, false, d => d.MultiSub ? "CC" : null),
        new("rating_imdb",        "IMDb Rating",      OverlayPosition.TopRight,    false, d => d.RatingImdb.HasValue ? d.RatingImdb.Value.ToString("0.0") : null),
        new("rating_tmdb",        "TMDB Rating",      OverlayPosition.TopRight,    false, d => d.RatingTmdb.HasValue ? d.RatingTmdb.Value.ToString("0.0") : null),
        new("rating_rt",          "RT Critics",       OverlayPosition.TopRight,    false, d => d.RatingRtCritic.HasValue ? $"{d.RatingRtCritic.Value}%" : null),
        new("rating_rt_audience", "RT Audience",      OverlayPosition.TopRight,    false, d => d.RatingRtAudience.HasValue ? $"{d.RatingRtAudience.Value}%" : null),
        new("content_rating",     "Age Rating",       OverlayPosition.BottomRight, false, d => string.IsNullOrEmpty(d.ContentRating) ? null : d.ContentRating),
        new("year",               "Year",             OverlayPosition.BottomLeft,  false, d => d.Year is > 0 ? d.Year.Value.ToString() : null),
        new("runtime",            "Runtime",          OverlayPosition.BottomLeft,  false, d => FormatRuntime(d.Runtime)),
        new("original_language",  "Language",         OverlayPosition.BottomLeft,  false, d => string.IsNullOrEmpty(d.OriginalLanguage) ? null : d.OriginalLanguage.ToUpperInvariant()),
        new("studio",             "Studio",           OverlayPosition.BottomRight, false, d => string.IsNullOrEmpty(d.Studio) ? null : d.Studio),
        new("network",            "Network",          OverlayPosition.BottomRight, false, d => string.IsNullOrEmpty(d.Network) ? null : d.Network),
        new("show_status",        "Show Status",      OverlayPosition.TopRight,    false, d => FormatShowStatus(d.ShowStatus)),
        new("request_status",     "Request Status",   OverlayPosition.TopLeft,     true, d => d.RequestStatus),
        new("advisory_age", "Advisory Age", OverlayPosition.TopRight, false, d => d.AdvisoryAge > 0 ? $"{d.AdvisoryAge}+" : null),
    ];

    public static string? ValueKey(string id, OverlayData data) => id switch
    {
        "resolution" => data.Resolution, "hdr" => data.Hdr, "audio" => data.Audio, "audio_channels" => data.AudioChannels,
        "resolution_hdr" => string.IsNullOrEmpty(data.Resolution) ? null : string.Join(" ", new[] { data.Resolution, data.Hdr }.Where(value => !string.IsNullOrEmpty(value))),
        "resolution_hdr_audio" => string.Join(" ", new[] { data.Resolution, data.Hdr, data.Audio }.Where(value => !string.IsNullOrEmpty(value))),
        "video_codec" => data.VideoCodec, "container" => data.Container, "aspect_ratio" => data.AspectRatio, "release_type" => data.ReleaseType,
        "edition" => data.Edition, "content_rating" => data.ContentRating, "original_language" => data.OriginalLanguage, "studio" => data.Studio, "network" => data.Network,
        "show_status" => data.ShowStatus,
        _ => null,
    };

    public static Dictionary<string, OverlayItemConfig> DefaultPrefs =>
        All.ToDictionary(def => def.Id, def => new OverlayItemConfig(def.DefaultEnabled, def.DefaultPosition));

    public static bool SuppressesStandaloneOverlays(string overlayId, IReadOnlyDictionary<string, OverlayItemConfig> prefs, int manifestRevision = int.MaxValue)
    {
        if (manifestRevision >= 26 && overlayId is "resolution" or "hdr" or "audio" or "resolution_hdr" &&
            prefs.TryGetValue("resolution_hdr_audio", out var allCombined) && allCombined.Enabled)
            return true;
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
        var suffix = FormatHdr(hdr);
        return $"{res} {suffix}";
    }

    private static string? FormatHdr(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : value.Contains("DV", StringComparison.OrdinalIgnoreCase) || value.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase) ? "DV"
        : value.Contains("HDR10+", StringComparison.OrdinalIgnoreCase) ? "HDR10+" : "HDR";

    private static string? JoinParts(params string?[] parts)
    {
        var value = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return value.Length == 0 ? null : value;
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
            "returning" or "returning series" or "continuing" or "in_production" or "in production" => "Returning",
            "ended" => "Ended",
            "cancelled" or "canceled" => "Cancelled",
            "upcoming" or "planned" => "Upcoming",
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
    private int _loadGeneration;

    public CardOverlayService(SettingsApi settingsApi)
    {
        _settingsApi = settingsApi;
    }

    public bool Enabled => _enabled;
    public bool ProfileBadgesEnabled { get; private set; } = true;
    public bool QuickActionsEnabled { get; private set; } = true;
    public string QuickActionMode { get; private set; } = "both";
    public bool HasProfileOverride { get; private set; }
    public bool GeneralPreferencesAvailable { get; private set; }
    public event Action? Changed;
    public bool IsLoaded => _initialized;
    public string Preset => _document.Preset;
    public OverlayStyle? Style => _document.Style;
    public bool AdvancedAvailable => !_manifestUnavailable && ManifestRevision >= 25;
    public bool SupportsImages { get; private set; }
    public IReadOnlyList<OverlayLibraryImage> Images { get; private set; } = [];
    public event Action? ImagesInvalidated;
    private readonly Dictionary<string, Task<byte[]?>> _images = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _imageReads = new(4, 4);
    private CancellationTokenSource _imageOwner = new();

    // Images and their editor belong to the account/profile, not to one request
    // generation: reconnect catch-up retires requests without changing the owner.
    public bool IsCurrentImageOwner(ApiIdentityContext owner) => _settingsApi.IsCurrentOverlayImageOwner(owner);
    public ApiIdentityContext CaptureImageOwner() => _settingsApi.CaptureOverlayImageOwner();

    public Task<byte[]?> GetImageAsync(string reference)
    {
        if (!SupportsImages || !SiloApiClient.IsOverlayImageRef(reference) || _imageIdentity is not { } owner
            || !_settingsApi.IsCurrentOverlayImageOwner(owner)) return Task.FromResult<byte[]?>(null);
        lock (_images)
        {
            if (_images.TryGetValue(reference, out var pending)) return pending;
            if (_images.Count >= 64)
            {
                var retired = _images.FirstOrDefault(pair => pair.Value.IsCompleted).Key;
                // Every slot is still reading. Wait for one to settle instead of
                // answering null, which would leave this badge plain for good.
                if (retired is null) return RetryImageAsync(Task.WhenAny(_images.Values), reference);
                _images.Remove(retired);
            }
            return _images[reference] = ReadImageAsync(reference, owner, _imageOwner.Token);
        }
    }

    private async Task<byte[]?> RetryImageAsync(Task settled, string reference)
    {
        await settled;
        return await GetImageAsync(reference);
    }

    private async Task<byte[]?> ReadImageAsync(string reference, ApiIdentityContext owner, CancellationToken ct)
    {
        // Leave GetImageAsync's lock first, so a failed read finds its own cache entry.
        await Task.Yield();
        try
        {
            await _imageReads.WaitAsync(ct);
            try
            {
                // Capture request authority when the read starts; only the owning
                // identity has to have survived the wait.
                var context = _settingsApi.CaptureContext();
                if (!_settingsApi.IsCurrentOverlayImageOwner(owner)) throw new OperationCanceledException("The selected profile changed.", ct);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                return await _settingsApi.GetOverlayImageAsync(reference, context, deadline.Token);
            }
            finally { _imageReads.Release(); }
        }
        // Content that fails validation is the server's answer; a missing image
        // already returned null. Both stay cached.
        catch (InvalidDataException) { return null; }
        catch
        {
            // Timeouts, transport and server errors can recover, so a later mount
            // must retry. A cancelled owner has already cleared the cache, and any
            // entry under this reference then belongs to its successor.
            lock (_images) if (!ct.IsCancellationRequested) _images.Remove(reference);
            return null;
        }
    }
    public Dictionary<string, OverlayItemConfig>? GetPrefs() => _enabled && ProfileBadgesEnabled ? _document.Items : null;

    public CardOverlayPrefs GetDocument() => _document with
    {
        Order = _document.Order.ToArray(),
        Items = _document.Items.ToDictionary(pair => pair.Key, pair => pair.Value with { Position = DisplayPosition(pair.Value.Position) })
    };
    public int ManifestRevision { get; private set; }
    private bool _manifestUnavailable;
    private ApiRequestContext? _manifestContext;
    private ApiIdentityContext? _imageIdentity;

    public OverlayPosition DisplayPosition(OverlayPosition position) => _manifestUnavailable ? position : PositionForRevision(position, ManifestRevision);

    public static OverlayPosition PositionForRevision(OverlayPosition position, int revision) => position switch
    {
        OverlayPosition.TopRight when revision >= 25 => OverlayPosition.RightEdge,
        OverlayPosition.LeftEdge when revision < 25 => OverlayPosition.TopLeft,
        OverlayPosition.RightEdge when revision < 25 => OverlayPosition.TopRight,
        _ => position,
    };

    public bool OffersOverlay(string id) => _manifestUnavailable || ManifestRevision >= IntroducedInRevision(id);

    public bool SuppressesStandalone(string id, IReadOnlyDictionary<string, OverlayItemConfig> prefs) =>
        OverlayRegistry.SuppressesStandaloneOverlays(id, prefs, _manifestUnavailable ? int.MaxValue : ManifestRevision);

    private static int IntroducedInRevision(string id) => id switch
    {
        "request_status" => 15,
        "advisory_age" => 13,
        "resolution_hdr_audio" => 26,
        _ => 0,
    };

    public IReadOnlyList<OverlayDef> GetOrderedDefinitions()
    {
        if (_document.Order.Count == 0)
            return OverlayRegistry.All.Where(d => OffersOverlay(d.Id)).ToArray();

        var configuredOrder = _document.Order
            .Select((id, index) => (id, index))
            .GroupBy(pair => pair.id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
        return OverlayRegistry.All
            .Where(d => OffersOverlay(d.Id))
            .Select((definition, registryIndex) => (definition, registryIndex))
            .OrderBy(pair => configuredOrder.TryGetValue(pair.definition.Id, out var index) ? index : int.MaxValue)
            .ThenBy(pair => pair.registryIndex)
            .Select(pair => pair.definition)
            .ToArray();
    }

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        var context = _settingsApi.CaptureContext();
        // Captured after the request context, so a context that is still current
        // at publication proves this identity never changed either.
        var owner = _settingsApi.CaptureOverlayImageOwner();
        var generation = Volatile.Read(ref _loadGeneration);
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var enabled = true;
            var manifestRevision = 0;
            var manifestUnavailable = false;
            var supportsImages = false;
            IReadOnlyList<OverlayLibraryImage> images = [];
            try
            {
                var capabilities = await _settingsApi.GetContractCapabilitiesAsync(context, ct);
                manifestRevision = capabilities.Revision;
                supportsImages = manifestRevision >= 25 && capabilities.SupportsOverlayImages;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { manifestUnavailable = true; }
            var document = BuildDefaultDocument();
            try
            {
                var config = await _settingsApi.GetOverlayConfigAsync(ct);
                enabled = config.Enabled;
                if (supportsImages) images = (config.Images ?? []).Where(image => SiloApiClient.IsOverlayImageRef(image.Ref)
                    && image.Width is > 0 and <= 600 && image.Height is > 0 and <= 300).Take(256).ToArray();
                if (!string.IsNullOrWhiteSpace(config.Defaults))
                    document = ParsePrefs(config.Defaults!) ?? document;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }

            try
            {
                var userSetting = await _settingsApi.GetSettingAsync("card_overlays", ct);
                if (!string.IsNullOrWhiteSpace(userSetting?.Value))
                {
                    var parsed = ParsePrefs(userSetting!.Value);
                    if (parsed != null) document = parsed;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }

            ct.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _loadGeneration))
                return;

            var profileEnabled = true;
            var quickEnabled = true;
            var quickMode = "both";
            var hasOverride = false;
            var generalAvailable = false;
            try
            {
                var keys = manifestRevision >= 8 ? new[] { "ui.card_quick_actions_enabled", "ui.card_quick_actions", "ui.card_overlays_enabled", "ui.card_overlays" } : new[] { "ui.card_overlays" };
                var effective = await _settingsApi.GetContractEffectiveSettingsAsync(keys, ct: ct);
                profileEnabled = effective.Settings.FirstOrDefault(entry => entry.Key == "ui.card_overlays_enabled")?.Value.ValueKind != JsonValueKind.False;
                quickEnabled = effective.Settings.FirstOrDefault(entry => entry.Key == "ui.card_quick_actions_enabled")?.Value.ValueKind != JsonValueKind.False;
                quickMode = effective.Settings.FirstOrDefault(entry => entry.Key == "ui.card_quick_actions")?.Value.GetString() ?? "both";
                hasOverride = effective.Settings.FirstOrDefault(entry => entry.Key == "ui.card_overlays")?.Scope == "profile";
                generalAvailable = manifestRevision >= 8;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
            ct.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _loadGeneration) || !_settingsApi.IsCurrentContext(context)) return;
            SupportsImages = supportsImages;
            Images = images;
            ManifestRevision = manifestRevision;
            _manifestUnavailable = manifestUnavailable;
            _manifestContext = manifestUnavailable ? null : context;
            _imageIdentity = owner;
            _enabled = enabled;
            _document = document;
            ProfileBadgesEnabled = profileEnabled;
            QuickActionsEnabled = quickEnabled;
            QuickActionMode = quickMode;
            HasProfileOverride = hasOverride;
            GeneralPreferencesAvailable = generalAvailable;
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _loadGeneration);
        _imageOwner.Cancel();
        _imageOwner.Dispose();
        _imageOwner = new();
        lock (_images) _images.Clear();
        SupportsImages = false;
        Images = [];
        ImagesInvalidated?.Invoke();
        _initialized = false;
        _manifestUnavailable = false;
        _manifestContext = null;
        _imageIdentity = null;
        ManifestRevision = 0;
        _enabled = true;
        _document = BuildDefaultDocument();
        ProfileBadgesEnabled = QuickActionsEnabled = true;
        QuickActionMode = "both";
        HasProfileOverride = false;
        GeneralPreferencesAvailable = false;
    }

    public async Task SaveAsync(CardOverlayPrefs prefs, CancellationToken ct = default)
    {
        var context = _settingsApi.CaptureContext();
        var owner = _settingsApi.CaptureOverlayImageOwner();
        var generation = Volatile.Read(ref _loadGeneration);
        // Finish any preference reload before replacing its document, so its
        // general preferences and manifest are not left at invalidation defaults.
        await EnsureLoadedAsync(ct);
        await _initLock.WaitAsync(ct);
        try
        {
            void RequireCurrentLoad()
            {
                ct.ThrowIfCancellationRequested();
                if (generation != Volatile.Read(ref _loadGeneration) || !_settingsApi.IsCurrentContext(context))
                    throw new OperationCanceledException("Card overlay preferences changed. Reload before saving.", ct);
            }

            var manifestRevision = ManifestRevision;
            RequireCurrentLoad();
            if (_manifestContext != context)
            {
                // Unknown or unavailable is not proof of an older contract.
                var capabilities = await _settingsApi.GetContractCapabilitiesAsync(context, ct);
                RequireCurrentLoad();
                manifestRevision = capabilities.Revision;
                ManifestRevision = manifestRevision;
                _manifestUnavailable = false;
                _manifestContext = context;
                _imageIdentity = owner;
                SupportsImages = manifestRevision >= 25 && capabilities.SupportsOverlayImages;
            }
            var normalized = NormalizeDocument(prefs);
            foreach (var definition in OverlayRegistry.All)
            {
                // An unavailable manifest can expose a badge the server cannot
                // write. Do not report success after dropping any of its edits.
                if (manifestRevision < IntroducedInRevision(definition.Id) &&
                    !SameItem(normalized.Items[definition.Id], _document.Items[definition.Id], manifestRevision))
                    throw new InvalidOperationException($"The server manifest does not support changing {definition.Label}. Reload card overlay settings before saving.");
                // Images need revision 25. Below it the serializer keeps the stored
                // ones, so an image edit is reported the same way.
                if (manifestRevision < 25 && !SameImages(normalized.Items[definition.Id], _document.Items[definition.Id]))
                    throw new InvalidOperationException($"The server manifest does not support changing {definition.Label} images. Reload card overlay settings before saving.");
            }
            if (manifestRevision < 25)
            {
                // Likewise the look and the plate/band presets: the stored values
                // would be written back and the save would still claim success.
                var unsupported = (normalized.Style ?? new()) != (_document.Style ?? new()) ? "the badge look"
                    : normalized.Preset is "plate" or "band" && normalized.Preset != _document.Preset ? $"to the {normalized.Preset} preset"
                    : null;
                if (unsupported is not null)
                    throw new InvalidOperationException($"The server manifest does not support changing {unsupported}. Reload card overlay settings before saving.");
            }
            var json = SerializeForRevision(normalized, manifestRevision);
            RequireCurrentLoad();
            await _settingsApi.SetContractSettingValueAsync(context, "ui.card_overlays", "profile",
                JsonSerializer.Deserialize<JsonElement>(json), ct: ct);
            RequireCurrentLoad();
            Interlocked.Increment(ref _loadGeneration);
            _document = ParsePrefs(json)!;
            _initialized = true;
            HasProfileOverride = true;
        }
        finally
        {
            _initLock.Release();
        }
        Changed?.Invoke();
    }

    public async Task RefreshPreferencesAsync(CancellationToken ct = default)
    {
        Invalidate();
        await EnsureLoadedAsync(ct);
        Changed?.Invoke();
    }

    public async Task RestoreServerDefaultsAsync(CancellationToken ct = default)
    {
        await _settingsApi.DeleteSettingAsync("card_overlays", ct);
        await RefreshPreferencesAsync(ct);
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

                result[def.Id] = new OverlayItemConfig(enabled, position, accentColor, showIcon)
                {
                    Image = entry.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.String ? image.GetString() : null,
                    // A repeated name is enumerated once per occurrence. As with
                    // TryGetProperty, the last one wins.
                    ValueImages = entry.TryGetProperty("valueImages", out var values) && values.ValueKind == JsonValueKind.Object
                        ? values.EnumerateObject().GroupBy(pair => pair.Name, StringComparer.Ordinal).Select(group => group.Last())
                            .Where(pair => pair.Value.ValueKind == JsonValueKind.String).Take(256)
                            .ToDictionary(pair => pair.Name, pair => pair.Value.GetString()!, StringComparer.Ordinal) : null,
                };
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

            return NormalizeDocument(new CardOverlayPrefs(version, preset, order, result) { OriginalDocument = root.Clone(), Style = root.TryGetProperty("style", out var style) ? OverlayStyle.Parse(style) : null });
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
        "left-edge" => OverlayPosition.LeftEdge,
        "right-edge" => OverlayPosition.RightEdge,
        _ => null,
    };

    public static string FormatPosition(OverlayPosition position) => position switch
    {
        OverlayPosition.TopLeft => "top-left",
        OverlayPosition.TopRight => "top-right",
        OverlayPosition.BottomLeft => "bottom-left",
        OverlayPosition.BottomRight => "bottom-right",
        OverlayPosition.LeftEdge => "left-edge",
        OverlayPosition.RightEdge => "right-edge",
        _ => "top-left",
    };

    private static CardOverlayPrefs BuildDefaultDocument() =>
        new(2, "classic", [], OverlayRegistry.DefaultPrefs);

    private static CardOverlayPrefs NormalizeDocument(CardOverlayPrefs prefs)
    {
        // Rendering falls back to classic for unknown presets; saving must not
        // replace a newer server's preset (including plate and band).
        var preset = string.IsNullOrWhiteSpace(prefs.Preset) ? "classic" : prefs.Preset;
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
        var order = prefs.Order.Distinct(StringComparer.Ordinal).ToArray();
        return new CardOverlayPrefs(Math.Max(2, prefs.Version), preset, order, items) { OriginalDocument = prefs.OriginalDocument, Style = prefs.Style };
    }

    private static string SerializePrefs(CardOverlayPrefs prefs)
        => SerializeForRevision(prefs, int.MaxValue);

    private static string SerializeForRevision(CardOverlayPrefs prefs, int manifestRevision)
    {
        var root = prefs.OriginalDocument is JsonElement original ? ToNode(original) as JsonObject ?? new() : new JsonObject();
        var items = root["items"] as JsonObject ?? new JsonObject();
        foreach (var def in OverlayRegistry.All)
        {
            // Older manifests reject newly introduced IDs. Preserve any stored
            // entry, but don't invent one until that manifest is supported.
            if (manifestRevision < IntroducedInRevision(def.Id)) continue;
            if (!prefs.Items.TryGetValue(def.Id, out var config)) continue;
            var item = items[def.Id] as JsonObject ?? new JsonObject();
            item["enabled"] = config.Enabled;
            item["position"] = FormatPosition(PositionForRevision(config.Position, manifestRevision));
            if (config.AccentColor is null) item.Remove("accentColor");
            else item["accentColor"] = config.AccentColor;
            if (config.ShowIcon is null) item.Remove("showIcon");
            else item["showIcon"] = config.ShowIcon;
            if (manifestRevision >= 25)
            {
                if (config.Image is not null) item["image"] = config.Image;
                else if (item["image"] is JsonValue imageValue && imageValue.TryGetValue<string>(out _)) item.Remove("image");
                if (config.ValueImages is not null)
                {
                    var values = item["valueImages"] as JsonObject ?? new JsonObject();
                    foreach (var key in values.Select(pair => pair.Key).Take(256).ToArray())
                        if (values[key] is JsonValue value && value.TryGetValue<string>(out _) && !config.ValueImages.ContainsKey(key)) values.Remove(key);
                    foreach (var pair in config.ValueImages) values[pair.Key] = pair.Value;
                    item["valueImages"] = values;
                }
            }
            items[def.Id] = item;
        }
        if (manifestRevision >= 25 && prefs.Style is not null)
            prefs.Style.WriteTo(root);
        root["version"] = prefs.Version;
        // Preserve stored newer fields, but do not introduce rev-25 presets to
        // servers that cannot accept them.
        root["preset"] = manifestRevision < 25 && prefs.Preset is "plate" or "band"
            ? root["preset"]?.GetValue<string>() ?? "classic" : prefs.Preset;
        root["order"] = JsonSerializer.SerializeToNode(prefs.Order.Where(id =>
            manifestRevision >= IntroducedInRevision(id) || items.ContainsKey(id)));
        if (root["items"] is not JsonObject) root["items"] = items;
        return root.ToJsonString();
    }

    // JsonObject rejects the repeated names a stored document may carry, which
    // would fail every save. They collapse the way the parser read them: the
    // last one wins.
    private static JsonNode? ToNode(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return new JsonArray(element.EnumerateArray().Select(ToNode).ToArray());
        if (element.ValueKind != JsonValueKind.Object) return JsonValue.Create(element);
        var node = new JsonObject();
        foreach (var property in element.EnumerateObject()) node[property.Name] = ToNode(property.Value);
        return node;
    }

    // Positions are compared as the manifest would store them: drafts carry the
    // display mapping, the loaded document the stored one.
    private static bool SameItem(OverlayItemConfig draft, OverlayItemConfig stored, int manifestRevision)
    {
        if (draft.Enabled != stored.Enabled || draft.AccentColor != stored.AccentColor || draft.ShowIcon != stored.ShowIcon
            || PositionForRevision(draft.Position, manifestRevision) != PositionForRevision(stored.Position, manifestRevision)) return false;
        return SameImages(draft, stored);
    }

    private static bool SameImages(OverlayItemConfig draft, OverlayItemConfig stored)
    {
        if (draft.Image != stored.Image) return false;
        var draftImages = draft.ValueImages;
        var storedImages = stored.ValueImages;
        if ((draftImages?.Count ?? 0) != (storedImages?.Count ?? 0)) return false;
        if (draftImages is null || storedImages is null || ReferenceEquals(draftImages, storedImages)) return true;
        foreach (var pair in draftImages)
            if (!storedImages.TryGetValue(pair.Key, out var reference) || reference != pair.Value) return false;
        return true;
    }

    private static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}
