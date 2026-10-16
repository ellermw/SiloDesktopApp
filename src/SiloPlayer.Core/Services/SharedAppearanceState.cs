using System.Globalization;
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
            var document = await api.GetServerBrandingAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (revision != _revision) return;
            if (!api.IsAppearanceContextCurrent(context)) { Reset(); return; }
            _lastRefresh = DateTimeOffset.UtcNow;
            Colors = AccentColors(document.AccentColor);
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
    // Branding is the only public appearance override. CSS, font and arbitrary token overrides are retired.
    private static IReadOnlyDictionary<string, string> AccentColors(string? accent)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        var value = accent?.Trim();
        if (value == null || !value.StartsWith('#') || value.Length is not (4 or 7) || !value[1..].All(Uri.IsHexDigit)) return colors;
        var hex = value[1..].ToUpperInvariant();
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        var normalized = "#" + hex;
        colors["primary"] = colors["ring"] = colors["sidebar-primary"] = normalized;
        static double Luminance(int rgb)
        {
            static double Channel(int c) => c / 255d <= .04045 ? c / 255d / 12.92 : Math.Pow((c / 255d + .055) / 1.055, 2.4);
            return .2126 * Channel((rgb >> 16) & 255) + .7152 * Channel((rgb >> 8) & 255) + .0722 * Channel(rgb & 255);
        }
        var luminance = Luminance(int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        var dark = Luminance(0x141417);
        if (1.05 / (luminance + .05) > (luminance + .05) / (dark + .05))
            colors["primary-foreground"] = colors["sidebar-primary-foreground"] = "#FFFFFF";
        return colors;
    }
}
