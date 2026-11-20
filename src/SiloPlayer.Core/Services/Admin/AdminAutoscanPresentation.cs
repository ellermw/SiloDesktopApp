using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminAutoscanTargets(IReadOnlyList<AdminLibrary> Libraries, bool Unknown, bool Unresolvable);
public static class AdminAutoscanPresentation
{
    public static AdminAutoscanDescriptor Descriptor(AdminAutoscanAvailableSource? plugin) => plugin?.Descriptor is { DeliveryModes.Count: > 0 } descriptor ? descriptor : new() { DeliveryModes = ["poll"] };
    public static string TypeName(AdminAutoscanSource source, IReadOnlyList<AdminAutoscanAvailableSource> available) =>
        available.FirstOrDefault(p => p.PluginId == source.PluginId && p.CapabilityId == source.CapabilityId)?.DisplayName is { Length: > 0 } name ? name : source.CapabilityId;
    public static string Provider(AdminAutoscanDescriptor descriptor, IReadOnlyDictionary<string, string>? config = null)
    {
        var chosen = config?.GetValueOrDefault("webhook_provider");
        return chosen is "sonarr" or "radarr" ? chosen : descriptor.ConnectionKinds.Count == 1 && descriptor.ConnectionKinds[0] is "sonarr" or "radarr" ? descriptor.ConnectionKinds[0] : "auto";
    }
    public static string Title(AdminAutoscanSource source, IReadOnlyList<AdminAutoscanAvailableSource> available) =>
        !string.IsNullOrWhiteSpace(source.Label) ? source.Label.Trim() : source.DeliveryMode == "webhook" && source.SourceConfig.GetValueOrDefault("webhook_provider") is "sonarr" or "radarr" ?
        ProviderName(source.SourceConfig["webhook_provider"]) : TypeName(source, available);
    public static string ProviderName(string provider) => provider == "sonarr" ? "Sonarr" : provider == "radarr" ? "Radarr" : "Sonarr/Radarr";
    public static string Interval(int seconds)
    {
        if (seconds < 60) return T("sources.interval.seconds", ("seconds", seconds));
        var minutes = (int)Math.Floor(seconds / 60d + .5);
        if (minutes < 60) return T("sources.interval.minutes", ("minutes", minutes));
        if (minutes % 1440 == 0) return T("sources.interval.days", ("count", minutes / 1440), ("count_text", minutes / 1440));
        return minutes % 60 == 0 ? T("sources.interval.hours", ("hours", minutes / 60)) : T("sources.interval.hoursMinutes", ("hours", minutes / 60), ("minutes", minutes % 60));
    }
    public static string Subtitle(AdminAutoscanSource source, AdminAutoscanState state)
    {
        var title = Title(source, state.Available); var type = TypeName(source, state.Available); var parts = new List<string>();
        if (source.DeliveryMode == "webhook")
        {
            parts.Add(title == type ? T("sources.subtitle.webhook") : type);
            var provider = source.SourceConfig.GetValueOrDefault("webhook_provider");
            if (provider is "sonarr" or "radarr")
            { if (!string.IsNullOrWhiteSpace(source.Label)) parts.Add(ProviderName(provider)); }
            else if (source.SourceConfig.GetValueOrDefault("webhook_provider") == "auto" || source.PluginId == "silo.autoscan.arr-webhook") parts.Add(T("sources.subtitle.autoDetects"));
        }
        else
        {
            if (title != type) parts.Add(type);
            if (state.Connections.FirstOrDefault(c => c.Id == source.ConnectionId) is { } connection) parts.Add(T("sources.subtitle.via", ("connection", connection.Name)));
            var seconds = source.PollIntervalSeconds is int own && state.Settings != null ? Math.Max(own, state.Settings.DefaultPollIntervalSeconds) : source.PollIntervalSeconds ?? state.Settings?.DefaultPollIntervalSeconds;
            parts.Add(seconds is int value ? T("sources.subtitle.pollsEvery", ("interval", Interval(value))) : T("sources.subtitle.pollsDefault"));
        }
        var result = string.Join(" · ", parts); return result.Length == 0 ? result : char.ToUpperInvariant(result[0]) + result[1..];
    }
    public static (string Text, bool Error) Health(AdminAutoscanSource source)
    {
        if (!source.Enabled) return (T("sources.status.off"), false);
        if (source.DeliveryMode == "webhook")
        {
            if (DateTimeOffset.TryParse(source.WebhookLastErrorAt, out var error) && (!DateTimeOffset.TryParse(source.WebhookLastReceivedAt, out var received) || error >= received)) return (T("sources.status.deliveryFailed"), true);
            if (source.WebhookLastReceivedAt != null) return (T("sources.status.lastDeliveryAt", ("time", Relative(source.WebhookLastReceivedAt))), false);
            return (T(source.WebhookConfigured ? "sources.status.noDeliveries" : "sources.status.noWebhookUrl"), false);
        }
        if (!string.IsNullOrEmpty(source.LastError)) return (T("sources.status.lastPollFailed"), true);
        return source.LastRunAt != null ? (T("sources.status.lastCheckedAt", ("time", Relative(source.LastRunAt))), false) : (T("sources.status.notRun"), false);
    }
    private static string Relative(string time)
    {
        if (!DateTimeOffset.TryParse(time, out var parsed)) return T("sources.row.unknownTime");
        return DashboardFormat.RelativeTime(parsed, DateTimeOffset.UtcNow, floor: true, T("sources.row.justNow")) ?? T("sources.row.unknownTime");
    }
    public static string ErrorDetail(AdminAutoscanSource source, AdminAutoscanDescriptor descriptor)
    {
        var text = source.DeliveryMode == "webhook" ? source.WebhookLastErrorMessage ?? source.LastError ?? "" : source.LastError ?? "";
        if (!string.IsNullOrEmpty(source.WebhookUrl)) text = text.Replace(source.WebhookUrl, "<url redacted>", StringComparison.Ordinal);
        var secretKeys = descriptor.ConfigForm?.Fields.Where(f => f.Secret || f.Control == "PASSWORD").Select(f => f.Key).ToHashSet() ?? [];
        foreach (var (key, value) in source.SourceConfig)
            if (value.Length > 0 && (secretKeys.Contains(key) || key.Contains("secret", StringComparison.OrdinalIgnoreCase) || key.Contains("token", StringComparison.OrdinalIgnoreCase) || key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("api_key", StringComparison.OrdinalIgnoreCase)))
                text = text.Replace(value, "<redacted>", StringComparison.Ordinal);
        return LocalLog.RedactDiagnosticText(text);
    }
    private static string Normalize(string path) => path.Trim().TrimEnd('/');
    public static AdminAutoscanTargets Targets(AdminAutoscanSource source, AdminAutoscanDescriptor descriptor, IReadOnlyList<AdminLibrary> libraries)
    {
        var paths = source.PathRewrites.Select(r => Normalize(r.To)).Where(p => p.Length > 0).ToArray();
        if (paths.Length == 0)
        {
            var fields = descriptor.ConfigForm?.Fields ?? [];
            paths = source.SourceConfig.Where(pair => (fields.Count == 0 || fields.Any(f => f.Key == pair.Key && !f.Secret)) && !pair.Key.Contains("secret", StringComparison.OrdinalIgnoreCase))
                .SelectMany(pair => pair.Value.Split('\n')).Select(Normalize).Where(p => p.StartsWith('/')).ToArray();
        }
        if (paths.Length == 0)
        { var unknown = source.DeliveryMode == "webhook" || descriptor.EmitsNativePaths || source.ConnectionId != null || descriptor.Connection == "required" || descriptor.ConfigForm?.Fields.Count is not > 0; return new([], unknown, !unknown); }
        return new(libraries.Where(l => l.Enabled && l.Paths.Any(root => paths.Any(path => Within(path, Normalize(root))))).ToArray(), false, false);
    }
    private static bool Within(string a, string b) => a.Length > 0 && b.Length > 0 && (a == b || a.StartsWith(b + '/', StringComparison.Ordinal) || b.StartsWith(a + '/', StringComparison.Ordinal));
    /// <summary>The row's Feeds column: only an unknown or empty result is words; otherwise the library names.</summary>
    public static string TargetsText(AdminAutoscanTargets targets) => targets.Unknown ? T("sources.targets.unknown") : targets.Libraries.Count == 0 ? T("sources.targets.none") : string.Join(", ", targets.Libraries.Select(l => l.Name));
    /// <summary>describeTargets: the edit dialog's "Feeds" summary.</summary>
    public static string DescribeTargets(AdminAutoscanTargets targets) => targets.Unresolvable ? T("sources.targets.noPaths") : targets.Unknown ? T("sources.targets.unknown") : targets.Libraries.Count == 0 ? T("sources.targets.noMatch") : string.Join(", ", targets.Libraries.Select(l => l.Name));
    private static string? Kind(string type) => type.Trim().ToLowerInvariant() switch { "movie" or "movies" => "movie", "series" or "show" or "shows" or "tv" or "tvshows" => "tv", "mixed" => "mixed", _ => null };
    public static string? Fill(string? fillFrom, IReadOnlyList<AdminLibrary> libraries)
    {
        var want = fillFrom switch { "library_paths_movie" => "movie", "library_paths_tv" => "tv", _ => null };
        return want == null ? null : string.Join("\n", libraries.Where(l => l.Enabled && (Kind(l.Type) == want || Kind(l.Type) == "mixed")).SelectMany(l => l.Paths).Select(p => p.Trim()).Where(p => p.Length > 0).Distinct());
    }
    public static List<AdminAutoscanPathRewrite> SeedMappings(string provider, IReadOnlyList<AdminLibrary> libraries)
    {
        var want = provider == "sonarr" ? "tv" : provider == "radarr" ? "movie" : null;
        var paths = libraries.Where(l => l.Enabled && Kind(l.Type) != null && (want == null || Kind(l.Type) == want || Kind(l.Type) == "mixed")).SelectMany(l => l.Paths).Select(Normalize).Where(p => p.StartsWith('/')).Distinct();
        return paths.GroupBy(p => "/" + string.Join('/', p.Split('/', StringSplitOptions.RemoveEmptyEntries).Take(2))).Select(group =>
        {
            var split = group.Select(p => p.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToArray();
            var shared = split[0].TakeWhile((part, index) => split.All(parts => parts.Length > index && parts[index] == part));
            return new AdminAutoscanPathRewrite { To = "/" + string.Join('/', shared) };
        }).ToList();
    }
    public static IReadOnlyList<string> ExpandedRoots(string root, IEnumerable<string> paths)
    {
        var prefix = Normalize(root);
        var children = paths.Select(Normalize).Where(p => p.StartsWith(prefix + '/', StringComparison.Ordinal)).Select(p => prefix + '/' + p[(prefix.Length + 1)..].Split('/')[0]).Distinct().ToArray();
        return children.Length > 1 ? children : [];
    }
    public static string? WebhookUrl(string? value, string server)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(server.TrimEnd('/') + '/', UriKind.Absolute, out var origin) || !Uri.TryCreate(origin, value, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) return null;
        var builder = new UriBuilder(uri);
        builder.Path = System.Text.RegularExpressions.Regex.Replace(builder.Path, "/api/v1/autoscan/webhooks/([^/]+)$", "/api/v2/autoscan/webhooks/$1", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return builder.Uri.AbsoluteUri;
    }
    private static string T(string key, params (string Name, object? Value)[] values) => AdminAutoscanText.T(key, values);
}
