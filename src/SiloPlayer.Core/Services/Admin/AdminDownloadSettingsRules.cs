using System.Globalization;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminDownloadFieldProblem
{
    /// <summary>A required numeric value was left empty (a limit box with Unlimited off).</summary>
    Blank,
    NotInteger,
    OutOfRange,
    NotDuration,
    DurationNotPositive,
    /// <summary>Downloads per period is limited but the period length is not a positive duration.</summary>
    PeriodRequired,
    /// <summary>A prepared-file directory that neither server platform could read as absolute.</summary>
    RelativePath,
}

/// <summary>One inline validation failure for a Downloads settings key. Bounds are in stored units.</summary>
public sealed record AdminDownloadFieldError(AdminDownloadFieldProblem Problem, long Min = 0, long Max = 0)
{
    public override string ToString() => nameof(AdminDownloadFieldError);
}

/// <summary>
/// Manage > Downloads > Settings keys, groups, validators and unit conversions, mirrored from the official
/// <c>DownloadsSettings.tsx</c>, <c>settingsPathDefaults.ts</c>, <c>LimitField.tsx</c> and the server's
/// <c>NormalizeAdminSetting</c>/<c>LoadFromDB</c> (silo-server abd85d0c). The server stays the authority; these
/// checks only keep an obviously rejected write from being sent.
/// </summary>
public static class AdminDownloadSettingsRules
{
    public const string Enabled = "download.enabled";
    public const string TranscodeEnabled = "download.transcode_enabled";
    public const string MaxConcurrentPrepares = "download.max_concurrent_prepares";
    public const string ArtifactDir = "download.artifact_dir";
    public const string ArtifactMaxBytes = "download.artifact_max_bytes";
    public const string DiskCeilingPercent = "download.artifact_disk_ceiling_percent";
    public const string ServerBandwidth = "download.server_bandwidth_mbps";
    public const string UserBandwidth = "download.user_bandwidth_mbps";
    public const string MaxConcurrentPerUser = "download.max_concurrent_per_user";
    public const string MaxPerPeriod = "download.max_per_period";
    public const string PeriodDuration = "download.period_duration";
    public const string LocalTranscodeFallback = "download.local_transcode_fallback";
    public const string CacheHours = "download.artifact_cache_hours";
    /// <summary>Read-only here: the Playback page owns it; Downloads derives its directory default from the saved value.</summary>
    public const string TranscodeDir = "playback.transcode_dir";

    /// <summary>Decimal GB, matching how drives and object stores are sold and reported.</summary>
    public const long BytesPerGigabyte = 1_000_000_000;
    public const long MaxMbps = 73_786_976_294_838; // math.MaxInt64 / 125000
    public const int MinDiskCeiling = 50, MaxDiskCeiling = 95, MaxCacheHours = 720;
    public const string DefaultTranscodeDir = "/tmp/silo-transcode";
    private const string ArtifactDirectoryName = "silo-download-artifacts";
    private const long DefaultPeriodNanoseconds = 24L * 3600 * 1_000_000_000;

    /// <summary>Always shown: whether downloads exist, whether Silo converts them, where and how much it prepares.</summary>
    public static IReadOnlyList<string> EssentialKeys { get; } = Array.AsReadOnly([Enabled, TranscodeEnabled, MaxConcurrentPrepares,
        ArtifactDir, ArtifactMaxBytes, DiskCeilingPercent, ServerBandwidth]);
    /// <summary>Enforced per user, listed first under their own heading.</summary>
    public static IReadOnlyList<string> PerUserKeys { get; } = Array.AsReadOnly([UserBandwidth, MaxConcurrentPerUser, MaxPerPeriod, PeriodDuration]);
    public static IReadOnlyList<string> WholeServerKeys { get; } = Array.AsReadOnly([LocalTranscodeFallback, CacheHours]);
    public static IReadOnlyList<string> AdvancedKeys { get; } = Array.AsReadOnly(PerUserKeys.Concat(WholeServerKeys).ToArray());
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(EssentialKeys.Concat(AdvancedKeys).ToArray());

    /// <summary>The server's effective value for a limit field's "Unlimited" is "0" on every limit row this page has.</summary>
    public const string Unlimited = "0";
    public static bool IsUnlimited(string value, string unlimitedValue = Unlimited) => value.Trim() == unlimitedValue;

    // ---------------------------------------------------------------- unit conversion (LimitField)

    /// <summary>
    /// What the admin reads for a stored value: stored units per displayed unit, three decimals kept so float
    /// division never surfaces as 53.68709…1. Mirrors <c>toDisplayUnits</c> (JavaScript <c>toFixed(3)</c> then Number).
    /// </summary>
    public static string ToDisplayUnits(string stored, long scale)
    {
        if (scale == 1) return stored;
        var trimmed = stored.Trim();
        if (trimmed.Length == 0) return "";
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed)) return stored;
        var text = (parsed / scale).ToString("F3", CultureInfo.InvariantCulture);
        if (text.Contains('.')) text = text.TrimEnd('0').TrimEnd('.');
        return text is "-0" or "" ? "0" : text;
    }

    /// <summary>
    /// What is stored for typed text: rounded to whole stored units with exact decimal arithmetic (never float drift),
    /// text that is not a finite number is kept as typed so the validator can say why. Mirrors <c>toStoredUnits</c>.
    /// </summary>
    public static string ToStoredUnits(string displayed, long scale)
    {
        if (scale == 1) return displayed;
        var trimmed = displayed.Trim();
        if (trimmed.Length == 0) return "";
        // Beyond decimal's range the text is far past any int64 byte count: keep it as typed so the validator reports it.
        if (!decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)) return displayed;
        try { return Math.Floor(checked(exact * scale) + 0.5m).ToString("0", CultureInfo.InvariantCulture); }
        catch (OverflowException) { return displayed; }
    }

    /// <summary>
    /// Stages typed limit text, never Unlimited: every integer spelling of zero (including rounded bytes)
    /// becomes an incomplete limit. The checkbox stages the sentinel separately.
    /// </summary>
    public static string ToLimitDraft(string displayed, long scale)
    {
        var value = ToStoredUnits(displayed, scale);
        return TryInteger(value, out var parsed) && parsed == 0 ? "" : value;
    }

    // ---------------------------------------------------------------- prepared-file directory (settingsPathDefaults.ts)

    /// <summary>
    /// Where prepared downloads are written for stored values: the configured directory, otherwise a
    /// <c>silo-download-artifacts</c> sibling of the (possibly defaulted) transcode directory. Mirrors
    /// <c>config.EffectiveDownloadArtifactDir</c>; the server cleans the transcode path first so a trailing
    /// slash never nests the artifact root inside the transcode root.
    /// </summary>
    public static string EffectiveArtifactDirectory(string artifactDir, string transcodeDir)
    {
        if (artifactDir.Length != 0) return artifactDir;
        var baseDirectory = transcodeDir.Length == 0 ? DefaultTranscodeDir : transcodeDir;
        return CleanPath(DirName(CleanPath(baseDirectory)) + "/" + ArtifactDirectoryName);
    }

    private static string DirName(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return CleanPath(lastSlash < 0 ? "" : path[..(lastSlash + 1)]);
    }

    /// <summary><c>filepath.Clean</c> for slash-separated paths; an empty result is ".".</summary>
    public static string CleanPath(string path)
    {
        var rooted = path.StartsWith('/');
        var resolved = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (resolved.Count > 0 && resolved[^1] != "..") resolved.RemoveAt(resolved.Count - 1);
                else if (!rooted) resolved.Add(".."); // a relative path keeps the ".." it cannot resolve; "/.." is "/"
                continue;
            }
            resolved.Add(segment);
        }
        var cleaned = (rooted ? "/" : "") + string.Join('/', resolved);
        return cleaned.Length == 0 ? "." : cleaned;
    }

    // ---------------------------------------------------------------- validators

    /// <summary>
    /// Inline errors for the staged values. Only edited keys are checked (an untouched legacy value is never blamed),
    /// except that touching either member of the per-period pair checks the pair, as the server's dependency group does.
    /// </summary>
    public static IReadOnlyDictionary<string, AdminDownloadFieldError> Validate(IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> dirty)
    {
        var errors = new Dictionary<string, AdminDownloadFieldError>(StringComparer.Ordinal);
        string Value(string key) => values.GetValueOrDefault(key, "");
        foreach (var key in dirty)
        {
            var error = key switch
            {
                MaxConcurrentPrepares or ArtifactMaxBytes or MaxConcurrentPerUser or MaxPerPeriod => Integer(Value(key), 0, long.MaxValue),
                ServerBandwidth or UserBandwidth => Integer(Value(key), 0, MaxMbps),
                DiskCeilingPercent => Integer(Value(key), MinDiskCeiling, MaxDiskCeiling),
                CacheHours => Integer(Value(key), 0, MaxCacheHours),
                PeriodDuration => PositiveDuration(Value(key)),
                ArtifactDir => AbsolutePath(Value(key)),
                _ => null,
            };
            if (error != null) errors[key] = error;
        }
        var pairTouched = dirty.Contains(MaxPerPeriod) || dirty.Contains(PeriodDuration);
        if (pairTouched && !errors.ContainsKey(MaxPerPeriod) && !errors.ContainsKey(PeriodDuration)
            && TryInteger(Value(MaxPerPeriod), out var perPeriod) && perPeriod > 0)
        {
            var period = Value(PeriodDuration);
            // The server backfills a missing period with its 24h default; an edited empty one was already rejected above.
            var seconds = period.Length == 0 ? DefaultPeriodNanoseconds : ParseDurationNanoseconds(period.Trim()) ?? 0;
            if (seconds <= 0) errors[PeriodDuration] = new(AdminDownloadFieldProblem.PeriodRequired);
        }
        return errors;
    }

    /// <summary>Limit rows: an empty box with Unlimited off asks for a number or Unlimited rather than a range.</summary>
    public static bool IsLimitKey(string key) => key is ArtifactMaxBytes or ServerBandwidth or UserBandwidth or MaxConcurrentPerUser or MaxPerPeriod;

    /// <summary>The inline message for a failed field, in the admin catalog's English.</summary>
    public static string Describe(string key, AdminDownloadFieldError error)
    {
        string Range() => error.Max == long.MaxValue
            ? AdminText.T("configure.native.downloads.error.integerMin", new { min = AdminText.FormatNumber(error.Min) })
            : AdminText.T("configure.native.downloads.error.integerRange", new { min = AdminText.FormatNumber(error.Min), max = AdminText.FormatNumber(error.Max) });
        return error.Problem switch
        {
            AdminDownloadFieldProblem.Blank when IsLimitKey(key) => AdminText.T("configure.native.downloads.error.blankLimit"),
            AdminDownloadFieldProblem.Blank or AdminDownloadFieldProblem.NotInteger or AdminDownloadFieldProblem.OutOfRange =>
                key == ArtifactMaxBytes ? AdminText.T("configure.native.downloads.error.sizeMin") : Range(),
            AdminDownloadFieldProblem.NotDuration => AdminText.T("configure.native.downloads.error.duration"),
            AdminDownloadFieldProblem.DurationNotPositive => AdminText.T("configure.native.downloads.error.durationNotPositive"),
            AdminDownloadFieldProblem.PeriodRequired => AdminText.T("configure.native.downloads.error.periodRequired"),
            _ => AdminText.T("configure.native.downloads.error.relativePath"),
        };
    }

    private static bool TryInteger(string raw, out long value) =>
        long.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static AdminDownloadFieldError? Integer(string raw, long min, long max)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return new(AdminDownloadFieldProblem.Blank, min, max);
        if (TryInteger(trimmed, out var parsed)) return parsed < min || parsed > max ? new(AdminDownloadFieldProblem.OutOfRange, min, max) : null;
        // A value of digits that overflows an int64 is out of range; anything else is not an integer.
        var digits = trimmed.TrimStart('+', '-');
        return digits.Length > 0 && digits.All(char.IsAsciiDigit) && trimmed.Length - digits.Length <= 1
            ? new(AdminDownloadFieldProblem.OutOfRange, min, max) : new(AdminDownloadFieldProblem.NotInteger, min, max);
    }

    private static AdminDownloadFieldError? PositiveDuration(string raw)
    {
        var nanoseconds = ParseDurationNanoseconds(raw.Trim());
        if (nanoseconds == null) return new(AdminDownloadFieldProblem.NotDuration);
        return nanoseconds <= 0 ? new(AdminDownloadFieldProblem.DurationNotPositive) : null;
    }

    /// <summary>The server reads <c>filepath.IsAbs</c> on its own platform; only paths neither platform reads as absolute are flagged.</summary>
    private static AdminDownloadFieldError? AbsolutePath(string raw)
    {
        var path = raw.Trim();
        if (path.Length == 0) return null; // blank is the derived default
        var rooted = path[0] is '/' or '\\' || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\');
        return rooted ? null : new(AdminDownloadFieldProblem.RelativePath);
    }

    /// <summary>
    /// The server's duration grammar: a whole number of days ("30d") or Go <c>time.ParseDuration</c>. Returns nanoseconds,
    /// or null when the text is not a duration. Day arithmetic wraps exactly as Go's int64 multiplication does.
    /// </summary>
    public static long? ParseDurationNanoseconds(string text)
    {
        if (text.Length == 0) return null;
        if (text.Length > 1 && text[^1] == 'd' && text[..^1].All(char.IsAsciiDigit))
            return long.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var days)
                ? unchecked(days * 24L * 3_600_000_000_000L) : null;
        return GoParseDuration(text);
    }

    private static readonly (string Unit, ulong Nanoseconds)[] Units =
    [
        ("ns", 1), ("us", 1_000), ("µs", 1_000), ("μs", 1_000), ("ms", 1_000_000),
        ("s", 1_000_000_000), ("m", 60_000_000_000), ("h", 3_600_000_000_000),
    ];

    /// <summary>Go <c>time.ParseDuration</c>: [-+]?([0-9]*(\.[0-9]*)?[a-z]+)+, with its overflow rules; "0" alone is zero.</summary>
    private static long? GoParseDuration(string text)
    {
        const ulong Limit = 1UL << 63;
        var s = text;
        var negative = false;
        if (s.Length > 0 && s[0] is '-' or '+') { negative = s[0] == '-'; s = s[1..]; }
        if (s == "0") return 0;
        if (s.Length == 0) return null;
        ulong total = 0;
        while (s.Length > 0)
        {
            if (!(s[0] == '.' || char.IsAsciiDigit(s[0]))) return null;
            var before = s.Length;
            ulong whole = 0, fraction = 0; double scale = 1;
            var index = 0;
            while (index < s.Length && char.IsAsciiDigit(s[index]))
            {
                if (whole > Limit / 10) return null;
                whole = whole * 10 + (ulong)(s[index] - '0');
                if (whole > Limit) return null;
                index++;
            }
            s = s[index..];
            var pre = before != s.Length;
            var post = false;
            if (s.Length > 0 && s[0] == '.')
            {
                s = s[1..];
                var afterPoint = s.Length;
                index = 0; var overflow = false;
                while (index < s.Length && char.IsAsciiDigit(s[index]))
                {
                    if (!overflow)
                    {
                        if (fraction > (long.MaxValue) / 10) overflow = true;
                        else
                        {
                            var next = fraction * 10 + (ulong)(s[index] - '0');
                            if (next > Limit) overflow = true; else { fraction = next; scale *= 10; }
                        }
                    }
                    index++;
                }
                s = s[index..];
                post = afterPoint != s.Length;
            }
            if (!pre && !post) return null;
            index = 0;
            while (index < s.Length && s[index] != '.' && !char.IsAsciiDigit(s[index])) index++;
            if (index == 0) return null;
            var unitText = s[..index]; s = s[index..];
            ulong unit = 0; var known = false;
            foreach (var (name, nanoseconds) in Units) if (name == unitText) { unit = nanoseconds; known = true; break; }
            if (!known) return null;
            if (whole > Limit / unit) return null;
            whole *= unit;
            if (fraction > 0)
            {
                whole += (ulong)((double)fraction * ((double)unit / scale));
                if (whole > Limit) return null;
            }
            total += whole;
            if (total > Limit) return null;
        }
        if (negative) return total == Limit ? long.MinValue : -(long)total;
        return total > long.MaxValue ? null : (long)total;
    }
}
