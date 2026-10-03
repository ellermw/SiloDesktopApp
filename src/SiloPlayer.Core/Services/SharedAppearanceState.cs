using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>Server-owned appearance. Only supported literal colors cross into the native UI.</summary>
public sealed class SharedAppearanceState(SettingsApi api)
{
    private long _revision;
    private Task? _inFlight;
    private ApiRequestContext? _context;
    private DateTimeOffset? _lastRefresh;
    public IReadOnlyDictionary<string, string> Colors { get; private set; } = new Dictionary<string, string>();
    public event Action? Changed;

    public void Reset()
    {
        _revision++;
        _context = null;
        _lastRefresh = null;
        Colors = new Dictionary<string, string>();
        Changed?.Invoke();
    }

    public Task RefreshAsync(CancellationToken ct = default)
        => _inFlight = RefreshCoreAsync(ct);

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        var context = api.CaptureAppearanceContext();
        if (_context != context) Reset();
        _context = context;
        var revision = ++_revision;
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context.BaseUrl)) return;
        try
        {
            var document = await api.GetSharedAppearanceAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (revision != _revision) return;
            if (!api.IsAppearanceContextCurrent(context)) { Reset(); return; }
            _lastRefresh = DateTimeOffset.UtcNow;
            Colors = ParseColors(document.Vars);
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            if (revision != _revision) return;
            Colors = new Dictionary<string, string>();
            _lastRefresh = DateTimeOffset.UtcNow;
            Changed?.Invoke();
        }
    }

    public Task RefreshIfStaleAsync(CancellationToken ct = default)
        => _context == api.CaptureAppearanceContext() && _inFlight is { IsCompleted: false }
            ? _inFlight
            : _context != api.CaptureAppearanceContext() || _lastRefresh is null ||
           DateTimeOffset.UtcNow - _lastRefresh >= TimeSpan.FromMinutes(1)
            ? RefreshAsync(ct) : Task.CompletedTask;
    private static readonly HashSet<string> ColorTokens = new(StringComparer.Ordinal)
    {
        "background", "foreground", "card", "card-foreground", "popover", "popover-foreground",
        "primary", "primary-foreground", "secondary", "secondary-foreground", "muted", "muted-foreground",
        "accent", "accent-foreground", "destructive", "destructive-foreground", "border", "input", "ring",
        "chart-1", "chart-2", "chart-3", "chart-4", "chart-5", "sidebar", "sidebar-foreground",
        "sidebar-primary", "sidebar-primary-foreground", "sidebar-accent", "sidebar-accent-foreground",
        "sidebar-border", "sidebar-ring", "surface", "surface-hover", "surface-raised", "ambient"
    };

    private static IReadOnlyDictionary<string, string> ParseColors(string? json)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json) || json.Length > 131072) return colors;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return colors;
            foreach (var token in document.RootElement.EnumerateObject())
            {
                if (!ColorTokens.Contains(token.Name) || token.Value.ValueKind != JsonValueKind.String) continue;
                var value = token.Value.GetString()!.Trim();
                if (!value.StartsWith('#') || value.Length is not (4 or 5 or 7 or 9) || !value[1..].All(Uri.IsHexDigit)) continue;
                var hex = value[1..].ToUpperInvariant();
                if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(c => new string(c, 2)));
                // CSS eight-digit hex is RRGGBBAA; native colors use AARRGGBB.
                colors[token.Name] = "#" + (hex.Length == 8 ? hex[6..] + hex[..6] : hex);
            }
        }
        catch (JsonException) { }
        return colors;
    }
}
