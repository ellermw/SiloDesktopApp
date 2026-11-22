using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>How an installation's status dot is drawn. Tones are semantic; the page maps them to its palette.</summary>
public enum AdminPluginTone { Muted, Success, Warning, Destructive }

/// <summary>Official pluginStatus: label, dot, hover title, whether an admin must act, and the sort rank.</summary>
public sealed record AdminPluginStatus(string Label, AdminPluginTone Tone, string? Title, bool Attention, int Rank, bool Colored);

/// <summary>One capability kind (official pluginCapabilities): job key, label, and whether it is a primary job.</summary>
public sealed record AdminPluginKind(string Job, string Label, bool Primary);

public enum AdminPluginFilter { All, Attention, Update, Off }

/// <summary>
/// Pure presentation rules of the official Plugins pages (web/src/lib/pluginPresentation.ts, pluginStatus.ts,
/// pluginStatusIndicator.ts, pluginConfigReady.ts, pluginCapabilities.ts, taskTrigger.ts) at
/// 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e. Text that comes from plugins or repositories is untrusted: nothing here
/// returns markup, and links go through <see cref="SafeLink"/> only.
/// </summary>
public static class AdminPluginPresentation
{
    // ── names, tiers, links ────────────────────────────────────────────────

    public static string Name(AdminPlugin plugin) => Name(plugin.PluginId, plugin.Presentation);

    public static string Name(string pluginId, AdminPluginInfo? presentation)
    {
        var display = Clean(presentation?.DisplayName, NameLimit);
        if (display.Length > 0) return display;
        var stripped = Regex.Replace(Clean(pluginId, IdLimit), "^silo[._-]?", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        var parts = Regex.Split(stripped, "[._-]+", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Where(part => part.Length > 0)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]);
        var derived = string.Join(' ', parts);
        return derived.Length > 0 ? derived : Clean(pluginId, IdLimit);
    }

    /// <summary>The publisher's name as shown: one line, no direction controls, bounded. Empty when there is none.</summary>
    public static string Publisher(AdminPluginInfo? presentation) => Clean(presentation?.PublisherName, NameLimit);

    public static string Summary(AdminPluginInfo? presentation, IEnumerable<AdminPluginCapabilityInfo> capabilities)
    {
        var summary = Clean(presentation?.Summary, 600);
        if (summary.Length > 0) return summary;
        var fallback = capabilities.Select(c => Clean(c.Description, 600)).FirstOrDefault(d => d.Length > 0);
        return fallback ?? AdminPluginsText.T("web.admin.plugins.tile.noDescription");
    }

    /// <summary>The installation's tier: its repository's source kind, or "unverified" with no repository (upload, archive link, removed repository).</summary>
    public static string Tier(AdminPlugin installation) => installation.RepositoryId == null ? "unverified" : installation.SourceKind;

    /// <summary>The newer version an admin can install now (official pendingUpdateVersion), or null.</summary>
    public static string? PendingUpdate(AdminPlugin installation)
    {
        if (!string.IsNullOrEmpty(installation.EnvManaged?.VersionPin) || Tier(installation) == "unverified" || installation.UpdatesPaused) return null;
        return string.IsNullOrEmpty(installation.AvailableVersion) ? null : installation.AvailableVersion;
    }

    public static string SourceLabel(string tier) => AdminPluginsText.T(tier switch
    {
        "silo" => "web.admin.plugins.source.silo",
        "approved_community" => "web.admin.plugins.source.approvedCommunity",
        "unverified" => "web.admin.plugins.source.unverified",
        _ => "web.admin.plugins.source.external",
    });

    /// <summary>The notice community, external and unverified plugins carry. Silo's own have none.</summary>
    public static string? TierNotice(string tier) => tier switch
    {
        "silo" => null,
        "approved_community" => AdminPluginsText.T("web.admin.plugins.source.approvedCommunityNotice"),
        "unverified" => AdminPluginsText.T("web.admin.plugins.source.unverifiedNotice"),
        _ => AdminPluginsText.T("web.admin.plugins.source.externalNotice"),
    };

    /// <summary>A verified signature's source repository as owner/name, else as sent.</summary>
    public static string SignatureRepositoryLabel(string sourceRepo)
    {
        if (Uri.TryCreate(sourceRepo, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.Trim('/');
            return Clean(path.Length > 0 ? path : sourceRepo, IdLimit);
        }
        return Clean(sourceRepo, IdLimit);
    }

    /// <summary>A version as shown: one line, no direction controls, bounded.</summary>
    public static string Version(string? version) => Clean(version, VersionLimit);

    /// <summary>A repository's name as shown.</summary>
    public static string RepositoryName(string? name) => Clean(name, NameLimit);

    /// <summary>The only way a plugin or repository supplied URL becomes openable: http or https, through the app's safe link policy.</summary>
    public static Uri? SafeLink(string? value) => ExternalBrowserUrlPolicy.TryGetSafeUri(value, out var uri) ? uri : null;

    public static IReadOnlyList<(string Label, Uri Url)> ResourceLinks(AdminPluginInfo? presentation, string? repoUrl)
    {
        var links = new List<(string, Uri)>();
        void Add(string key, params string?[] candidates)
        {
            foreach (var candidate in candidates)
                if (SafeLink(candidate) is { } url) { links.Add((AdminPluginsText.T(key), url)); return; }
        }
        Add("web.admin.plugins.rail.sourceCode", presentation?.SourceUrl, repoUrl);
        Add("web.admin.plugins.rail.changelog", presentation?.ChangelogUrl);
        Add("web.admin.plugins.rail.support", presentation?.SupportUrl);
        return links;
    }

    public static string License(string? spdx)
    {
        var value = Clean(spdx, NameLimit);
        return value.Length == 0 || value == "NOASSERTION" ? AdminPluginsText.T("web.admin.plugins.rail.licenseNotSpecified") : value;
    }

    public static string HumanizeConfigKey(string value) =>
        Clean(string.Join(' ', Clean(value, IdLimit).Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..])), NameLimit);

    public static string ConfigTitle(AdminPluginSchema schema) => Clean(schema.Title, NameLimit) is { Length: > 0 } title ? title : HumanizeConfigKey(schema.Key);

    // ── capability kinds ───────────────────────────────────────────────────

    private static readonly (string Type, string Job, string Key, bool Primary)[] Kinds =
    [
        ("metadata_provider.v1", "metadata", "web.admin.plugins.capabilities.metadata", true),
        ("image_resolver.v1", "artwork", "web.admin.plugins.capabilities.artwork", true),
        ("marker_provider.v1", "markers", "web.admin.plugins.capabilities.markers", true),
        ("scan_source.v1", "scanning", "web.admin.plugins.capabilities.scanning", true),
        ("request_router.v1", "requests", "web.admin.plugins.capabilities.requests", true),
        ("watch_sync_provider.v1", "watch-sync", "web.admin.plugins.capabilities.watchSync", true),
        ("auth_provider.v1", "sign-in", "web.admin.plugins.capabilities.signIn", true),
        ("scheduled_task.v1", "tasks", "web.admin.plugins.capabilities.scheduledTasks", true),
        ("media_analyzer.v1", "analysis", "web.admin.plugins.capabilities.mediaAnalysis", true),
        ("audiobook_backend.v1", "audiobooks", "web.admin.plugins.capabilities.audiobooks", true),
        ("ebook_backend.v1", "ebooks", "web.admin.plugins.capabilities.ebooks", true),
        ("network_access_provider.v1", "network", "web.admin.plugins.capabilities.networkAccess", true),
        ("event_consumer.v1", "events", "web.admin.plugins.capabilities.events", false),
        ("http_routes.v1", "pages", "web.admin.plugins.capabilities.webPages", false),
    ];

    public static AdminPluginKind Kind(string type)
    {
        var index = Array.FindIndex(Kinds, kind => kind.Type == type);
        if (index >= 0) return new(Kinds[index].Job, AdminPluginsText.T(Kinds[index].Key), Kinds[index].Primary);
        // An unknown type shows its own prefix, humanized, not catalog text.
        var shown = Clean(type, IdLimit);
        var prefix = shown.Split('.')[0];
        var label = prefix.Replace('_', ' ');
        label = label.Length > 0 ? char.ToUpperInvariant(label[0]) + label[1..] : shown;
        return new(prefix, label, false);
    }

    private static int KindOrder(string type)
    {
        var index = Array.FindIndex(Kinds, kind => kind.Type == type);
        return index < 0 ? 999 : index;
    }

    /// <summary>A plugin's primary capability kinds, deduplicated and in the fixed order.</summary>
    public static IReadOnlyList<AdminPluginKind> PrimaryKinds(IEnumerable<AdminPluginCapabilityInfo> capabilities) =>
        capabilities.Select(c => c.Type).Distinct(StringComparer.Ordinal).Where(type => Kind(type).Primary)
            .OrderBy(KindOrder).Select(Kind).ToArray();

    public static string CapabilityList(IEnumerable<AdminPluginCapabilityInfo> capabilities) =>
        AdminTasksText.JoinList(PrimaryKinds(capabilities).Select(kind => kind.Label).ToArray());

    /// <summary>The jobs present in <paramref name="sets"/>, in display order, for the catalog filter.</summary>
    public static IReadOnlyList<AdminPluginKind> CatalogJobs(IEnumerable<IEnumerable<AdminPluginCapabilityInfo>> sets)
    {
        var seen = new Dictionary<string, AdminPluginKind>(StringComparer.Ordinal);
        foreach (var set in sets) foreach (var kind in PrimaryKinds(set)) seen[kind.Job] = kind;
        var order = Kinds.Select(k => k.Job).ToList();
        return seen.Values.OrderBy(kind => order.IndexOf(kind.Job)).ToArray();
    }

    public static bool HasJob(IEnumerable<AdminPluginCapabilityInfo> capabilities, string job) => PrimaryKinds(capabilities).Any(kind => kind.Job == job);

    // ── configuration readiness and status ─────────────────────────────────

    private static Func<string, bool> SavedChecker(AdminPlugin plugin)
    {
        var saved = plugin.GlobalConfigs.GroupBy(c => c.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        return key =>
        {
            if (!saved.TryGetValue(key, out var config)) return false;
            if (config.ConfiguredSecrets.Count > 0) return true;
            // Any saved non-null value counts (false and 0 are chosen values); only a blank string does not.
            return config.Value.Values.Any(value => value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => false,
                JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
                _ => true,
            });
        };
    }

    /// <summary>The required global config entries with no saved value: the page's "Needs setup" rule.</summary>
    public static IReadOnlyList<AdminPluginSchema> MissingRequired(AdminPlugin plugin)
    {
        var filled = SavedChecker(plugin);
        return plugin.GlobalConfigSchema.Where(schema => schema.Required && !filled(schema.Key)).ToArray();
    }

    private static AdminPluginStatus Indicator(AdminPlugin plugin)
    {
        if (!plugin.Enabled) return new(AdminPluginsText.T("web.admin.plugins.statusIndicator.off"), AdminPluginTone.Muted, null, false, 4, false);
        var runtime = plugin.Runtime ?? new AdminPluginRuntime();
        if (!runtime.Resident)
            return runtime.State == "failed"
                ? new(AdminPluginsText.T("web.admin.plugins.statusIndicator.failed"), AdminPluginTone.Destructive, runtime.LastError, false, 3, false)
                : new(AdminPluginsText.T("web.admin.plugins.statusIndicator.active"), AdminPluginTone.Success, null, false, 3, false);
        return runtime.State switch
        {
            "running" => new(AdminPluginsText.T("web.admin.plugins.statusIndicator.running"), AdminPluginTone.Success, null, false, 3, false),
            "starting" => new(AdminPluginsText.T("web.admin.plugins.statusIndicator.starting"), AdminPluginTone.Warning, null, false, 3, false),
            "backoff" => new(AdminPluginsText.T("web.admin.plugins.statusIndicator.restarting", new { restart_count = runtime.RestartCount }), AdminPluginTone.Warning, runtime.LastError, false, 3, false),
            "failed" => new(AdminPluginsText.T("web.admin.plugins.statusIndicator.failed"), AdminPluginTone.Destructive, runtime.LastError, false, 3, false),
            _ => new(AdminPluginsText.T("web.admin.plugins.statusIndicator.stopped"), AdminPluginTone.Muted, runtime.LastError, false, 3, false),
        };
    }

    /// <summary>Official pluginStatus. A plugin that is off never needs attention; the Off filter covers it.</summary>
    public static AdminPluginStatus Status(AdminPlugin plugin)
    {
        var indicator = Indicator(plugin);
        var runtime = plugin.Runtime ?? new AdminPluginRuntime();
        if (!plugin.Enabled) return indicator with { Attention = false, Rank = 4 };
        if (runtime.State == "failed") return indicator with { Attention = true, Rank = 0, Colored = true };
        if (runtime.Resident && runtime.State == "backoff") return indicator with { Attention = true, Rank = 1, Colored = true };
        var missing = MissingRequired(plugin);
        if (missing.Count > 0)
            return new(AdminPluginsText.T("web.admin.plugins.status.needsSetup"), AdminPluginTone.Warning,
                AdminPluginsText.T("web.admin.plugins.status.missing", new { settings = AdminTasksText.JoinList(missing.Select(m => string.IsNullOrEmpty(m.Title) ? m.Key : m.Title).ToArray()) }), true, 2, true);
        if (plugin.UpdateFailure != null || plugin.Signature?.State == "invalid") return indicator with { Attention = true, Rank = 2 };
        return indicator with { Attention = false, Rank = 3 };
    }

    public static AdminPluginFilter ParseFilter(string? raw) => raw switch
    {
        "attention" => AdminPluginFilter.Attention, "update" => AdminPluginFilter.Update, "off" => AdminPluginFilter.Off, _ => AdminPluginFilter.All,
    };

    public static string FilterKey(AdminPluginFilter filter) => filter switch
    {
        AdminPluginFilter.Attention => "attention", AdminPluginFilter.Update => "update", AdminPluginFilter.Off => "off", _ => "all",
    };

    public static bool MatchesFilter(AdminPlugin plugin, AdminPluginFilter filter) => filter switch
    {
        AdminPluginFilter.Attention => Status(plugin).Attention,
        AdminPluginFilter.Update => PendingUpdate(plugin) != null,
        AdminPluginFilter.Off => !plugin.Enabled,
        _ => true,
    };

    private static int CompareNames(string a, string b) => string.Compare(a, b, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    /// <summary>Status rank, then display name; the catalog's presentation stands in for older manifests, as on the tiles.</summary>
    public static IReadOnlyList<AdminPlugin> SortInstalled(IEnumerable<AdminPlugin> installations, Func<AdminPlugin, AdminPluginInfo?> presentationFor)
    {
        var keyed = installations.Select(p => (Plugin: p, Rank: Status(p).Rank, Name: Name(p.PluginId, presentationFor(p)))).ToList();
        keyed.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : CompareNames(a.Name, b.Name));
        return keyed.Select(k => k.Plugin).ToArray();
    }

    public static bool Matches(string query, string pluginId, AdminPluginInfo? presentation, IEnumerable<AdminPluginCapabilityInfo> capabilities, string sourceKind, string? repositoryName)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0) return true;
        var parts = new List<string?>
        {
            pluginId, Name(pluginId, presentation), presentation?.Summary, presentation?.DescriptionMarkdown, presentation?.PublisherName, repositoryName, SourceLabel(sourceKind),
        };
        foreach (var capability in capabilities) { parts.Add(capability.DisplayName); parts.Add(capability.Description); parts.Add(capability.Type); parts.Add(Kind(capability.Type).Label); }
        var text = string.Join('\n', parts.Where(part => !string.IsNullOrEmpty(part)));
        return text.Contains(normalized, StringComparison.CurrentCultureIgnoreCase);
    }

    // ── scheduled-task bindings (official taskTrigger.ts) ──────────────────

    private static readonly Regex TimeOfDay = new("^([01]?[0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
    private const long MaxIntervalMs = 9_223_372_036_854;

    private static bool Runnable(AdminTaskTrigger trigger) => trigger.Type switch
    {
        "startup" => true,
        "interval" => (trigger.IntervalMs ?? 0) > 0 && (trigger.IntervalMs ?? 0) <= MaxIntervalMs,
        "daily" => TimeOfDay.IsMatch(trigger.TimeOfDay ?? ""),
        "weekly" => TimeOfDay.IsMatch(trigger.TimeOfDay ?? "") && (trigger.DayOfWeek ?? 0) is >= 0 and <= 6,
        _ => false,
    };

    private static AdminTaskTrigger? BindingTrigger(IReadOnlyDictionary<string, JsonElement>? raw)
    {
        if (raw == null || !raw.TryGetValue("type", out var type) || type.ValueKind != JsonValueKind.String) return null;
        long? Integer(string field, out bool bad)
        {
            bad = false;
            if (!raw.TryGetValue(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && Math.Abs(number) <= 9_007_199_254_740_991L) return number;
            bad = true; return null;
        }
        // The server unmarshals this map into a trigger and ignores it if a known field has the wrong JSON type.
        var interval = Integer("interval_ms", out var badInterval); var day = Integer("day_of_week", out var badDay); var runtime = Integer("max_runtime_ms", out var badRuntime);
        if (badInterval || badDay || badRuntime) return null;
        string? time = null;
        if (raw.TryGetValue("time_of_day", out var timeValue) && timeValue.ValueKind != JsonValueKind.Null)
        {
            if (timeValue.ValueKind != JsonValueKind.String) return null;
            time = timeValue.GetString();
        }
        var trigger = new AdminTaskTrigger(type.GetString() ?? "", interval, time, day is { } d ? (int)Math.Clamp(d, int.MinValue, int.MaxValue) : null, runtime);
        return Runnable(trigger) ? trigger : null;
    }

    /// <summary>The schedule a plugin task runs on as the server picks it: a runnable binding trigger, else the manifest default, else once at server start.</summary>
    public static AdminTaskTrigger TaskTrigger(IReadOnlyDictionary<string, JsonElement>? binding, AdminPluginTrigger? defaultSchedule)
    {
        if (BindingTrigger(binding) is { } fromBinding) return fromBinding;
        if (defaultSchedule != null) return new(defaultSchedule.Type, defaultSchedule.IntervalMs, defaultSchedule.TimeOfDay, defaultSchedule.DayOfWeek, defaultSchedule.MaxRuntimeMs);
        return new("startup", null, null, null, null);
    }

    /// <summary>The Tasks page key of a plugin's scheduled task (plugin:&lt;installation&gt;:&lt;capability&gt;).</summary>
    public static string TaskKey(string installationId, string capabilityId) => "plugin:" + installationId + ":" + capabilityId;

    // ── untrusted text ─────────────────────────────────────────────────────

    public const int NameLimit = 120;
    public const int IdLimit = 200;
    public const int VersionLimit = 64;
    public const int LineLimit = 400;

    /// <summary>Line and paragraph separators and the Unicode direction controls: invisible, and able to reorder or break text.</summary>
    private static bool IsBreakOrDirection(char c) => c is '\u2028' or '\u2029' || IsDirection(c);

    /// <summary>Whether the text holds a control character, a line or paragraph separator or a direction control.</summary>
    public static bool HasHiddenControls(string? text) => text != null && text.Any(c => char.IsControl(c) || IsBreakOrDirection(c));

    private static bool IsDirection(char c) => c is '\u061C' or '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');

    /// <summary>
    /// The one gate every piece of text from a plugin, a publisher, a repository or the server passes before it is shown as a
    /// single line: control characters, line and paragraph separators and direction controls become spaces, runs of spaces
    /// collapse, and the result is trimmed and cut to <paramref name="max"/> characters (an ellipsis marks a cut).
    /// A text that cannot contain a line break cannot forge a line of the page's own statements.
    /// </summary>
    public static string Clean(string? text, int max = LineLimit)
    {
        if (string.IsNullOrEmpty(text) || max <= 0) return "";
        var clean = new StringBuilder(Math.Min(text.Length, max + 16)); var pendingSpace = false;
        foreach (var c in text)
        {
            if (c == ' ' || char.IsControl(c) || IsBreakOrDirection(c)) { pendingSpace = clean.Length > 0; continue; }
            if (pendingSpace) { clean.Append(' '); pendingSpace = false; }
            clean.Append(c);
            // Stop reading input well past the bound; only the first max + 1 characters can matter.
            if (clean.Length > max) break;
        }
        if (clean.Length <= max) return clean.ToString();
        var end = Math.Max(0, max - 1);
        if (end > 0 && char.IsHighSurrogate(clean[end - 1])) end--;
        return clean.ToString(0, end).TrimEnd() + "…";
    }

    /// <summary>Removes the direction controls from text that may keep its own line breaks (README, changelog and setup Markdown).</summary>
    public static string StripDirectionControls(string? text) =>
        string.IsNullOrEmpty(text) ? "" : text.Any(IsDirection) ? new string(text.Where(c => !IsDirection(c)).ToArray()) : text;

    private static readonly Regex UserInfo = new(@"(?i)(https?://)[^\s/@]+@", RegexOptions.None, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SecretQuery = new(@"(?i)([?&](?:token|key|api[_-]?key|secret|password|passwd|sig|signature|auth|authorization|credential|access[_-]?token|x-amz-[a-z-]+)=)[^\s&#]*", RegexOptions.None, TimeSpan.FromMilliseconds(100));
    // An auth scheme word (Authorization: Bearer x) or a filler word (password is x) after the key is not the value: it is skipped, and the text after it is what gets removed.
    private static readonly Regex SecretPair = new(@"(?i)\b(password|passwd|secret|token|api[_-]?key|authorization|bearer|client[_-]?secret)\b(\s*[:=]\s*|\s+)(?:(?:bearer|basic|digest|negotiate|token|is|was)(?:\s*[:=]\s*|\s+)){0,2}[^\s,;]+", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Text a server, plugin or log returned that the page shows: secret values the admin typed and credential-looking
    /// text are removed first, and only then is the result truncated, so a cut can never expose half of a secret.
    /// </summary>
    public static string Redact(string? text, int max = 400, IEnumerable<string>? secrets = null)
    {
        if (string.IsNullOrEmpty(text)) return "";
        // Invisible direction controls cannot be used to split a keyword the patterns below look for.
        var result = StripDirectionControls(text);
        foreach (var secret in (secrets ?? []).Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s.Length))
            result = result.Replace(secret, "[redacted]", StringComparison.Ordinal);
        result = UserInfo.Replace(result, "$1[redacted]@");
        result = SecretQuery.Replace(result, "$1[redacted]");
        result = SecretPair.Replace(result, "$1$2[redacted]");
        // Secrets are gone; what remains goes through the same single-line gate as every other third-party text.
        return Clean(result, max);
    }
}
