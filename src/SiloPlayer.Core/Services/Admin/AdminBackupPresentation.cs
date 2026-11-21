using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One run of formatted rich text: a tag name (b, muted, code, link, label, sr) or null for plain text.</summary>
public sealed record AdminBackupTextRun(string Text, string? Tag);

/// <summary>
/// Formats the pinned Backups messages, whose official text carries rich-text tags.
/// <see cref="Visible"/> drops <c>&lt;sr&gt;</c> (screen-reader only) content and keeps the rest;
/// <see cref="Accessible"/> keeps everything; <see cref="Runs"/> returns tagged runs to render.
/// Empty tags such as <c>&lt;decrypt&gt;&lt;/decrypt&gt;</c> take their text from <paramref name="fills"/>.
/// </summary>
public static partial class AdminBackupText
{
    [GeneratedRegex("<([a-z]+)>(.*?)</\\1>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    public static IReadOnlyList<AdminBackupTextRun> Runs(string key, object? values = null, IReadOnlyDictionary<string, string>? fills = null)
    {
        var text = AdminText.T(key, values);
        var runs = new List<AdminBackupTextRun>();
        var position = 0;
        foreach (Match match in TagPattern().Matches(text))
        {
            if (match.Index > position) runs.Add(new(text[position..match.Index], null));
            var tag = match.Groups[1].Value;
            var inner = match.Groups[2].Value;
            if (inner.Length == 0 && fills != null && fills.TryGetValue(tag, out var fill)) inner = fill;
            runs.Add(new(inner, tag));
            position = match.Index + match.Length;
        }
        if (position < text.Length) runs.Add(new(text[position..], null));
        return runs;
    }

    public static string Visible(string key, object? values = null, IReadOnlyDictionary<string, string>? fills = null) =>
        string.Concat(Runs(key, values, fills).Where(run => run.Tag != "sr").Select(run => run.Text));

    public static string Accessible(string key, object? values = null, IReadOnlyDictionary<string, string>? fills = null) =>
        string.Concat(Runs(key, values, fills).Select(run => run.Text));

    /// <summary>The longest third-party name (backup, account, profile, folder or server name) the pages show in one piece.</summary>
    public const int PlainLimit = 200;

    /// <summary>
    /// Third-party text as plain display text: control characters, line and paragraph separators and
    /// bidirectional formatting (overrides, embeddings, isolates, marks) cannot fake a line or reorder
    /// the text around a name. Line breaks and tabs become one space, other controls and bidi formatting
    /// are dropped, invalid UTF-16 becomes U+FFFD, and a name longer than <paramref name="limit"/>
    /// characters ends in an ellipsis. Joiners stay, so joined emoji keep their shape.
    /// </summary>
    public static string Plain(string? value, int limit = PlainLimit)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var builder = new StringBuilder(Math.Min(value.Length, 256));
        var count = 0;
        var pendingSpace = false;
        foreach (var rune in value.EnumerateRunes())
        {
            var code = rune.Value;
            if (IsBidiFormatting(code)) continue;
            if (code is 0x2028 or 0x2029 or 0x85 or (>= 9 and <= 13)) { pendingSpace = builder.Length > 0; continue; }
            if (Rune.IsControl(rune) || code is 0xFFFE or 0xFFFF) continue;
            // A space right after a replaced line break is that break's space.
            if (code == ' ' && pendingSpace) continue;
            if (pendingSpace)
            {
                pendingSpace = false;
                if (builder.Length > 0 && builder[^1] != ' ') { builder.Append(' '); count++; }
            }
            if (count >= limit) { builder.Append('…'); return builder.ToString().TrimEnd(); }
            builder.Append(rune.ToString());
            count++;
        }
        return builder.ToString().Trim();
    }

    private static bool IsBidiFormatting(int code) => code is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);

    public static object Counted(long count, object? extra = null)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = count, ["count_text"] = AdminText.FormatNumber(count) };
        if (extra != null)
            foreach (var property in extra.GetType().GetProperties()) values[property.Name] = property.GetValue(extra);
        return values;
    }

    // ── Dates as the backup pages print them ─────────────────────────

    private static CultureInfo Culture => CultureInfo.CurrentCulture;
    public static DateTime Local(DateTimeOffset value) => value.ToLocalTime().DateTime;
    /// <summary>"Oct 30, 2026, 4:00 AM" (official backupDateTime).</summary>
    public static string DateTimeText(DateTimeOffset value) => Local(value).ToString("MMM d, yyyy", Culture) + ", " + Local(value).ToString("t", Culture);
    public static string TimeText(DateTimeOffset value) => Local(value).ToString("t", Culture);
    public static string Weekday(DateTimeOffset value) => Local(value).ToString("dddd", Culture);
    public static string ShortDate(DateTimeOffset value) => Local(value).ToString("MMM d", Culture);
    /// <summary>"Sunday, Oct 30" (official restore heading takenDate).</summary>
    public static string TakenDate(DateTimeOffset value) => Local(value).ToString("dddd, MMM d", Culture);

    /// <summary>Whole local calendar days from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static int CalendarDays(DateTimeOffset from, DateTimeOffset to) => (int)Math.Round((Local(to).Date - Local(from).Date).TotalDays);
}

/// <summary>Which storage the default destination lives in.</summary>
public enum AdminBackupLocation { Local, Bucket, None }

/// <summary>Official RetentionPlan: what rotation does after the next successful backup.</summary>
public sealed record AdminBackupRetentionPlan(IReadOnlyList<AdminBackup> Kept, IReadOnlySet<string> Doomed, string? ProtectedId);

/// <summary>Official warningDetails OmissionGroup.</summary>
public sealed record AdminBackupOmissionGroup(string Reason, string Cause, string Summary, string Detail, IReadOnlyList<string> Labels);
public sealed record AdminBackupMissingGroup(string Source, IReadOnlyList<string> Keys);

/// <summary>Official s3SetupModel S3Fields; the secret is typed only, never read back.</summary>
public sealed record AdminBackupS3Fields(string Endpoint, string Region, string Bucket, string KeyPrefix, string AccessKey, string SecretKey, bool PathStyle)
{
    public override string ToString() => nameof(AdminBackupS3Fields);
}

/// <summary>
/// Ports of the official Backups presentation helpers (web/src/pages/admin-backups/
/// backupPresentation.ts, warningDetails.ts, s3SetupModel.ts, uploadModel.ts and
/// UploadedBackups.tsx at 8d253adbf).
/// </summary>
public static class AdminBackupPresentation
{
    public const string EnabledKey = "backup.enabled";
    public const string TimeKey = "backup.time";
    public const string KeepKey = "backup.keep";
    public const string IntroKey = "backup.include_intro_fingerprints";
    public const string SubtitlesKey = "backup.include_subtitles";
    public const string DestinationKey = "backup.destination";
    public const string PrivateBucketKey = "s3.private_bucket";
    public static readonly IReadOnlyList<string> ScheduleKeys = [EnabledKey, TimeKey, KeepKey];
    public static readonly IReadOnlyList<string> ContentKeys = [IntroKey, SubtitlesKey];
    public static readonly IReadOnlyList<string> SettingKeys = [EnabledKey, TimeKey, KeepKey, IntroKey, SubtitlesKey, DestinationKey];
    public const string S3Endpoint = "backup.s3.endpoint", S3Region = "backup.s3.region", S3Bucket = "backup.s3.bucket", S3KeyPrefix = "backup.s3.key_prefix",
        S3AccessKey = "backup.s3.access_key", S3SecretKey = "backup.s3.secret_key", S3PathStyle = "backup.s3.path_style";
    public static readonly IReadOnlyList<string> S3Keys = [S3Endpoint, S3Region, S3Bucket, S3KeyPrefix, S3AccessKey, S3SecretKey, S3PathStyle];
    public const int RowsShown = 4;
    public const string DecryptCommand = "silo backup decrypt <file>.age";
    public const string RestoreFolderCommand = "silo backup restore --from <folder>";
    public const string UploadDecryptCommand = "silo backup decrypt restore-set.tar.age -o restore-set.tar";
    public const string EndpointExample = "https://s3.us-east-1.amazonaws.com";
    public const string NoValue = "—";

    private static string T(string key, object? values = null) => AdminText.T(key, values);

    public static AdminBackupLocation DefaultLocation(bool hasDefault, string? destination, IReadOnlyList<AdminBackup> items, string privateBucket)
    {
        if (!hasDefault) return AdminBackupLocation.None;
        if (destination != "s3" && items.Count > 0)
            return items.Any(item => IsBucketLocation(item.Location ?? "")) ? AdminBackupLocation.Bucket : AdminBackupLocation.Local;
        return privateBucket.Trim().Length > 0 ? AdminBackupLocation.Bucket : AdminBackupLocation.Local;
    }

    public static bool KeyConfirmationNeeded(AdminBackupList? list) => list is { Encrypted: true, KeyConfirmed: false };
    public static bool EncryptedWithOtherKey(AdminBackup backup) => backup.Encrypted == true && backup.KeyMatches != true;
    public static string OtherKeyNote(AdminBackup backup) => backup.KeyCheck is { Length: > 0 } check
        ? T("web.admin.backups.table.otherKeyNoteWithCheck", new { key_check = check }) : T("web.admin.backups.table.otherKeyNote");
    public static string OtherKeyReason() => T("web.admin.backups.table.otherKeyReason");

    public static string KindLabel(string? kind) => T(kind switch
    {
        "manual" => "web.admin.backups.kind.manual", "before-restore" => "web.admin.backups.kind.beforeRestore",
        "before-upgrade" => "web.admin.backups.kind.beforeUpgrade", _ => "web.admin.backups.kind.nightly",
    });

    public static string StatusLabel(string? status) => T(status switch
    {
        "complete_with_warnings" => "web.admin.backups.status.completeWithWarnings", "in_progress" => "web.admin.backups.status.inProgress",
        "incomplete" => "web.admin.backups.status.incomplete", "unverified" => "web.admin.backups.status.unverified",
        "unreadable" => "web.admin.backups.status.unreadable", _ => "web.admin.backups.status.complete",
    });

    public static string? KnownSiloVersion(string? version)
    {
        var trimmed = version?.Trim();
        return !string.IsNullOrEmpty(trimmed) && !trimmed.Equals("unavailable", StringComparison.OrdinalIgnoreCase) ? trimmed : null;
    }

    public static string ShortServerId(string serverId) => serverId.Split('-')[0] is { Length: > 0 } head ? head : serverId;
    public static string? SourceServerName(string? serverName) => AdminBackupText.Plain(serverName) is { Length: > 0 } name ? name : null;

    public static bool IsFinished(AdminBackup backup) => backup.Status is "complete" or "complete_with_warnings" ||
        backup.Status == "unverified" && backup.Objects is { Length: > 0 };

    public static bool CanRestoreParts(AdminBackup backup) => IsFinished(backup) &&
        (backup.Objects ?? []).Any(entry => entry.Name is "restore-set.tar" or "restore-set.tar.age");

    public static AdminRouteRequest RestoreRequest(string backupId) => new(AdminRoute.Backups) { Segments = [backupId, "restore"] };
    public static AdminRouteRequest UploadRestoreRequest(string uploadId) => new(AdminRoute.Backups) { Segments = ["uploads", uploadId, "restore"] };

    /// <summary>Where a running restore shows its progress, from the operation holding the lock.</summary>
    public static AdminRouteRequest? ActiveRestoreRequest(AdminBackupActive active) =>
        active.UploadId is { } upload ? UploadRestoreRequest(upload) : active.BackupId is { } backup ? RestoreRequest(backup) : null;

    public static string ActiveOperationText(AdminBackupActive active) => active.Operation switch
    {
        "backup" => T("web.admin.backups.active.backingUp", new { time = AdminBackupText.TimeText(active.StartedAt!.Value) }),
        "restore" => T("web.admin.backups.active.restoreRunning"),
        "upload" => T("web.admin.backups.active.checkingUpload"),
        _ => active.UploadId != null ? T("web.admin.backups.active.deletingUpload") : T("web.admin.backups.active.deleting"),
    };

    private static (string Day, string Date) DayValues(DateTimeOffset date, DateTimeOffset now)
    {
        var days = AdminBackupText.CalendarDays(now, date);
        if (days == 0) return ("today", "");
        if (days == 1) return ("tomorrow", "");
        if (days == -1) return ("yesterday", "");
        if (days > -7 && days < 7) return ("other", AdminBackupText.Weekday(date));
        return ("other", AdminBackupText.ShortDate(date));
    }

    private static (string Copy, string Date, string Day) CopyValues(DateTimeOffset date, DateTimeOffset now)
    {
        var days = AdminBackupText.CalendarDays(now, date);
        var day = AdminBackupText.Weekday(date);
        if (days == 0) return ("today", "", day);
        if (days == -1) return ("yesterday", "", day);
        if (days > -7) return ("weekday", "", day);
        return ("other", AdminBackupText.ShortDate(date), day);
    }

    /// <summary>The next nightly run from the backup task's own schedule, or null when unknown or passed.</summary>
    public static DateTimeOffset? NextBackupRun(DateTimeOffset now, DateTimeOffset? taskNextRunAt) => taskNextRunAt is { } next && next > now ? next : null;

    public static AdminBackupRetentionPlan RetentionPlan(IReadOnlyList<AdminBackup> items, long keep)
    {
        var newestFirst = items.Where(item => item.Kind is "nightly" or "manual" && IsFinished(item)).OrderByDescending(item => item.CreatedAt).ToList();
        var room = (int)Math.Max(1, keep) - 1;
        var newestClean = newestFirst.FirstOrDefault(item => item.Clean == true)?.Id;
        var past = newestFirst.Skip(room).Select(item => item.Id!).ToList();
        var kept = newestFirst.AsEnumerable().Reverse().ToArray();
        return new(kept, past.Where(id => id != newestClean).ToHashSet(StringComparer.Ordinal),
            newestClean != null && past.Contains(newestClean) ? newestClean : null);
    }

    public static string RetentionNote(long keep, AdminBackupRetentionPlan plan, DateTimeOffset? next, bool running, DateTimeOffset now)
    {
        var sentences = new List<string> { T("web.admin.backups.retention.keeps", new { keep, keep_text = AdminText.FormatNumber(keep) }) };
        var when = "other";
        if (running) when = "running";
        else if (next is { } upcoming)
        {
            var days = AdminBackupText.CalendarDays(now, upcoming);
            if (days == 0) when = "today"; else if (days == 1) when = "tomorrow";
        }
        var doomed = plan.Kept.Where(item => plan.Doomed.Contains(item.Id!)).ToList();
        if (doomed.Count > 0)
        {
            if (doomed.Count == 1)
            {
                var copy = CopyValues(doomed[0].CreatedAt!.Value, now);
                sentences.Add(AdminBackupText.Visible("web.admin.backups.retention.oneDeleted", new { copy = copy.Copy, date = copy.Date, day = copy.Day, when }));
            }
            else sentences.Add(T("web.admin.backups.retention.manyDeleted", AdminBackupText.Counted(doomed.Count, new { when })));
        }
        if (plan.Kept.FirstOrDefault(item => item.Id == plan.ProtectedId) is { } protectedCopy)
        {
            var copy = CopyValues(protectedCopy.CreatedAt!.Value, now);
            sentences.Add(AdminBackupText.Visible("web.admin.backups.retention.protected", new { copy = copy.Copy, date = copy.Date, day = copy.Day, when }));
        }
        return string.Join(" ", sentences);
    }

    /// <summary>
    /// Whether the server refuses every backup until the admin acts: no storage to write them to, the SQLite user store,
    /// or encryption with an unconfirmed key. The nightly run still fires and fails, so the page forecasts none (official backupsPaused).
    /// </summary>
    public static bool BackupsPaused(bool configured, IReadOnlyList<AdminBackupWarning> warnings) =>
        !configured || warnings.Any(warning => warning.Code is "sqlite_user_store" or "key_not_confirmed");

    /// <summary>The status line: the last success and the next run. A paused server shows no next run; its warning says why.</summary>
    public static (string Last, string? Next) SchedulePhrases(DateTimeOffset? lastSuccessAt, bool enabled, bool paused, bool busy, DateTimeOffset? next, DateTimeOffset now)
    {
        string last;
        if (lastSuccessAt is { } lastDate)
        {
            var day = DayValues(lastDate, now);
            last = T("web.admin.backups.schedule.lastFinished", new { day = day.Day, date = day.Date, time = AdminBackupText.TimeText(lastDate) });
        }
        else last = T("web.admin.backups.schedule.noneYet");
        string? upcoming;
        if (busy) upcoming = null;
        else if (!enabled) upcoming = T("web.admin.backups.schedule.off");
        else if (paused) upcoming = null;
        else if (next is { } run)
        {
            var day = DayValues(run, now);
            upcoming = T(lastSuccessAt != null ? "web.admin.backups.schedule.next" : "web.admin.backups.schedule.first",
                new { day = day.Day, date = day.Date, time = AdminBackupText.TimeText(run) });
        }
        else upcoming = T("web.admin.backups.schedule.unknown");
        return (last, upcoming);
    }

    public static string? LastRunProblem(AdminBackupLastRun? lastRun, DateTimeOffset now)
    {
        if (lastRun is not { Status: "failed" or "interrupted", StartedAt: { } started }) return null;
        var day = DayValues(started, now);
        var kind = lastRun.Kind == "manual" ? "manual" : "other";
        var error = lastRun.Error?.Trim();
        var failed = lastRun.Status == "failed";
        if (!string.IsNullOrEmpty(error))
        {
            var sentence = AdminBackupValidation.SafeServerText(error);
            if (!sentence.EndsWith('.')) sentence += ".";
            return T(failed ? "web.admin.backups.lastRun.failedWithError" : "web.admin.backups.lastRun.interruptedWithError",
                new { kind, day = day.Day, date = day.Date, time = AdminBackupText.TimeText(started), error = sentence });
        }
        return T(failed ? "web.admin.backups.lastRun.failed" : "web.admin.backups.lastRun.interrupted",
            new { kind, day = day.Day, date = day.Date, time = AdminBackupText.TimeText(started) });
    }

    public static IReadOnlyList<AdminBackupWarning> VisibleWarnings(IReadOnlyList<AdminBackupWarning> warnings, bool backingUp, bool neverBackedUp) =>
        warnings.Where(warning => warning.Code != "disabled" && !((backingUp || neverBackedUp) && warning.Code == "no_recent_backup")).ToArray();

    public static bool IsInfoWarning(AdminBackupWarning warning) => warning.Code == "no_backups_at_destination";

    public static string WarningText(AdminBackupWarning warning) => warning.Code == "key_not_confirmed"
        ? T("web.admin.backups.warning.keyNotConfirmed") : AdminBackupValidation.SafeServerText(warning.Message ?? "");

    public static string? LockBlocker(bool busy, AdminBackupActive? active)
    {
        if (!busy && active == null) return null;
        return active?.Operation switch
        {
            "backup" => T("web.admin.backups.lock.backup"),
            "delete" => active.UploadId != null ? T("web.admin.backups.lock.deleteUpload") : T("web.admin.backups.lock.delete"),
            "upload" => T("web.admin.backups.lock.upload"),
            "restore" => T("web.admin.backups.lock.restore"),
            _ => T("web.admin.backups.lock.anotherTask"),
        };
    }

    public static string? BackUpNowBlocker(bool configured, string? lockReason, IReadOnlyList<AdminBackupWarning> warnings, bool destinationAvailable)
    {
        if (!configured) return T("web.admin.backups.backUpNow.noStorage");
        if (warnings.FirstOrDefault(warning => warning.Code == "sqlite_user_store") is { } sqlite) return AdminBackupValidation.SafeServerText(sqlite.Message ?? "");
        if (warnings.Any(warning => warning.Code == "key_not_confirmed")) return T("web.admin.backups.backUpNow.keyNotConfirmed");
        if (lockReason != null) return lockReason;
        return destinationAvailable ? null : T("web.admin.backups.backUpNow.unreachable");
    }

    /// <summary>Official backupErrorMessage: the server's own detail, or "didn't answer" without one.</summary>
    public static string ErrorMessage(Exception? error)
    {
        if (error is not ApiException api) return T("web.admin.backups.error.noAnswer");
        if (api.StatusCode == 404) return T("web.admin.backups.error.notFound");
        var text = api.ProblemDetail ?? api.ProblemTitle;
        return string.IsNullOrWhiteSpace(text) ? T("web.admin.backups.error.noAnswer") : AdminBackupValidation.SafeServerText(text);
    }

    public static bool IsBucketLocation(string location) => location.StartsWith("s3://", StringComparison.OrdinalIgnoreCase);

    private static string ShellPath(string path) => Regex.IsMatch(path, "^[\\w./:@+-]+$", RegexOptions.CultureInvariant)
        ? path : "'" + path.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    public static string RestoreCommand(string? location)
    {
        if (string.IsNullOrEmpty(location)) return "silo backup restore --from <" + T("web.admin.backups.restoreCommand.folderPlaceholder") + ">";
        var from = IsBucketLocation(location) ? location.TrimEnd('/') : location;
        return "silo backup restore --from " + ShellPath(from);
    }

    /// <summary>folder, bucket or download (official FullRestoreSource).</summary>
    public static string CommandHint(string source, bool encrypted) => T(source switch
    {
        "download" => "web.admin.backups.command.hintDownload",
        "bucket" => encrypted ? "web.admin.backups.command.hintBucketEncrypted" : "web.admin.backups.command.hintBucket",
        _ => "web.admin.backups.command.hintFolder",
    });

    public static string DownloadHint(AdminBackup backup) => T(backup.Kind == "before-upgrade" ? "web.admin.backups.download.hintBeforeUpgrade"
        : backup.Encrypted == true ? "web.admin.backups.download.hintEncrypted" : "web.admin.backups.download.hint");

    // ── Warning details ──────────────────────────────────────────────

    private static readonly Regex Uuid = new("\\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NumericId = new("\\b(profile|account|user|item|library|collection)( id)? \\d+\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ByteCount = new("\\b\\d+ bytes\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Limit = new("\\(\\d+\\)", RegexOptions.CultureInvariant);
    private static readonly Regex Reading = new("(?:^|: )reading ([^:]+?)(?::|$)", RegexOptions.CultureInvariant);

    private static string NormalizeCause(string message) => Limit.Replace(ByteCount.Replace(NumericId.Replace(Uuid.Replace(message, "<id>"),
        match => match.Groups[1].Value + match.Groups[2].Value + " <id>"), "<n> bytes"), "(<n>)").Trim();

    private static string TechnicalDetail(string cause)
    {
        var error = cause.IndexOf("ERROR: ", StringComparison.Ordinal);
        return error >= 0 ? cause[(error + "ERROR: ".Length)..] : cause;
    }

    private static string PlainSummary(string reason, string cause, int count)
    {
        if (reason == "too_large") return T("web.admin.backups.warnings.tooLarge", new { count });
        var reading = Reading.Match(cause);
        return reading.Success && reading.Groups[1].Value.Trim().Length > 0
            ? T("web.admin.backups.warnings.partUnreadable", new { count, part = reading.Groups[1].Value.Trim() })
            : T("web.admin.backups.warnings.dataUnreadable", new { count });
    }

    public static IReadOnlyList<AdminBackupOmissionGroup> GroupOmissions(IReadOnlyList<AdminBackupOmitted> omitted)
    {
        var groups = new Dictionary<string, (string Reason, string Cause, string Detail, List<string> Labels)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var entry in omitted)
        {
            var cause = entry.Cause?.Trim() is { Length: > 0 } named ? named : NormalizeCause(entry.Message ?? "");
            var detail = entry.Error?.Trim() is { Length: > 0 } error ? error : TechnicalDetail(NormalizeCause(entry.Message ?? ""));
            var key = entry.Reason + "\0" + cause + "\0" + detail;
            if (!groups.TryGetValue(key, out var group)) { group = (entry.Reason!, cause, detail, []); groups[key] = group; order.Add(key); }
            group.Labels.Add(AdminBackupText.Plain(entry.Label));
        }
        return order.Select(key => groups[key]).Select(group => new AdminBackupOmissionGroup(group.Reason, group.Cause,
                PlainSummary(group.Reason, group.Cause, group.Labels.Count), AdminBackupValidation.SafeServerText(group.Detail), group.Labels))
            .OrderByDescending(group => group.Labels.Count).ToArray();
    }

    public static string OmissionGroupTitle(AdminBackupOmissionGroup group) =>
        T("web.admin.backups.warnings.groupTitle", AdminBackupText.Counted(group.Labels.Count, new { summary = group.Summary }));

    public const int NamesShown = 5;
    public static string NameSample(IReadOnlyList<string> names)
    {
        var list = string.Join(", ", names.Take(NamesShown));
        var more = names.Count - NamesShown;
        return more > 0 ? T("web.admin.backups.warnings.nameSample", new { names = list, more }) : list;
    }

    public static IReadOnlyList<AdminBackupMissingGroup> GroupMissingBlobs(IReadOnlyList<AdminBackupMissingBlob> blobs)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var blob in blobs)
        {
            var source = blob.Source?.Trim() is { Length: > 0 } named ? named : "file";
            if (!groups.TryGetValue(source, out var keys)) { keys = []; groups[source] = keys; order.Add(source); }
            keys.Add(AdminBackupText.Plain(blob.Key, 400));
        }
        return order.Select(source => new AdminBackupMissingGroup(source, groups[source])).OrderByDescending(group => group.Keys.Count).ToArray();
    }

    public static string MissingGroupTitle(AdminBackupMissingGroup group) => T(group.Source.EndsWith("artwork", StringComparison.Ordinal)
        ? "web.admin.backups.warnings.missingArtwork" : "web.admin.backups.warnings.missingGroup", AdminBackupText.Counted(group.Keys.Count, new { source = group.Source }));

    public static string OmittedHeadline(int count, bool restore) => T(restore ? "web.admin.backups.warnings.omittedRestore" : "web.admin.backups.warnings.omittedBackup",
        AdminBackupText.Counted(count));

    public static string MissingHeadline(long listed, long total)
    {
        var values = new { count = total, count_text = total == 1 ? "1" : AdminText.FormatNumber(total), listed = AdminText.FormatNumber(listed) };
        return total > listed && total != 1 ? T("web.admin.backups.warnings.missingHeadlineFirst", values) : T("web.admin.backups.warnings.missingHeadline", values);
    }

    /// <summary>Every warning group as plain text for an issue report (official warningDetailsReport, English).</summary>
    public static string WarningDetailsReport(AdminBackup backup)
    {
        var lines = new List<string> { T("web.admin.backups.warnings.reportBackup", new { id = backup.Id }) };
        var omitted = backup.RestoreSetOmitted ?? [];
        var missing = backup.MissingBlobs ?? [];
        if (omitted.Length > 0)
        {
            lines.Add(""); lines.Add(T("web.admin.backups.warnings.omittedHeadline", AdminBackupText.Counted(omitted.Length)));
            foreach (var group in GroupOmissions(omitted))
            {
                lines.Add(""); lines.Add(OmissionGroupTitle(group) + " (" + group.Reason + ")");
                lines.Add("  " + group.Detail); lines.Add("  " + string.Join(", ", group.Labels));
            }
        }
        if (missing.Length > 0)
        {
            lines.Add(""); lines.Add(MissingHeadline(missing.Length, backup.MissingBlobsTotal ?? missing.Length));
            foreach (var group in GroupMissingBlobs(missing))
            {
                lines.Add(""); lines.Add(MissingGroupTitle(group));
                lines.AddRange(group.Keys.Select(key => "  " + key));
            }
        }
        return string.Join("\n", lines);
    }

    // ── S3 setup ─────────────────────────────────────────────────────

    public static AdminBackupS3Fields S3FieldsFromSettings(Func<string, string> read) =>
        new(read(S3Endpoint), read(S3Region), read(S3Bucket), read(S3KeyPrefix), read(S3AccessKey), "", read(S3PathStyle) == "true");

    public static AdminBackupS3Test S3TestBody(AdminBackupS3Fields fields) => new()
    {
        Endpoint = fields.Endpoint.Trim(), Bucket = fields.Bucket.Trim(), Region = fields.Region.Trim(), KeyPrefix = fields.KeyPrefix.Trim(),
        AccessKey = fields.AccessKey.Trim(), SecretKey = fields.SecretKey, PathStyle = fields.PathStyle,
    };

    /// <summary>The test result for the fields as they are now, or null when they changed since (official currentS3Test).</summary>
    public static AdminBackupS3TestResult? CurrentS3Test(AdminBackupS3Fields fields, (AdminBackupS3Test Body, AdminBackupS3TestResult Result)? outcome) =>
        outcome is { } tested && tested.Body == S3TestBody(fields) ? tested.Result : null;

    public static string? S3SaveBlocker(AdminBackupS3Fields fields, (AdminBackupS3Test Body, AdminBackupS3TestResult Result)? outcome, bool keyKept)
    {
        if (fields.Endpoint.Trim().Length == 0 || fields.Bucket.Trim().Length == 0) return T("web.admin.backups.s3.blocker.fields");
        var test = CurrentS3Test(fields, outcome);
        if (test == null) return T("web.admin.backups.s3.blocker.untested");
        if (!test.Ok) return T("web.admin.backups.s3.blocker.failed");
        return keyKept ? null : T("web.admin.backups.s3.blocker.keyKept");
    }

    /// <summary>The settings batch that switches backups to the bucket; the secret only when typed.</summary>
    public static Dictionary<string, string> S3SettingsValues(AdminBackupS3Fields fields)
    {
        var body = S3TestBody(fields);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DestinationKey] = "s3", [S3Endpoint] = body.Endpoint, [S3Region] = body.Region, [S3Bucket] = body.Bucket,
            [S3KeyPrefix] = body.KeyPrefix, [S3AccessKey] = body.AccessKey, [S3PathStyle] = fields.PathStyle ? "true" : "false",
        };
        if (body.SecretKey.Length > 0) values[S3SecretKey] = body.SecretKey;
        return values;
    }

    // ── Upload ───────────────────────────────────────────────────────

    private const long Kilobyte = 1024, Megabyte = Kilobyte * 1024, Gigabyte = Megabyte * 1024;

    private static string Decimal(double value, int digits, int minimum = -1) =>
        value.ToString(digits == 0 ? "#,0" : minimum == 0 ? "#,0." + new string('#', digits) : "#,0." + new string('0', digits), CultureInfo.CurrentCulture);

    public static string ProgressLabel(long sent, long total)
    {
        var (unit, label, digits) = total >= Gigabyte ? (Gigabyte, "web.admin.common.units.gigabytes", 1)
            : total >= Megabyte ? (Megabyte, "web.admin.common.units.megabytes", 0) : (Kilobyte, "web.admin.common.units.kilobytes", 0);
        var scale = Math.Pow(10, digits);
        string Show(long bytes, Func<double, double> round) => Decimal(round(bytes / (double)unit * scale) / scale, digits);
        var totalText = Show(total, Math.Round);
        var sentText = sent >= total ? totalText : Show(sent, Math.Floor);
        return T("web.admin.backups.upload.progress", new { sent = sentText, total = totalText, unit = T(label) });
    }

    public static int ProgressPercent(long sent, long total) => total <= 0 ? 0 : (int)Math.Min(100, Math.Max(0, Math.Floor(sent * 100d / total)));

    public static string LimitLabel(long bytes) => bytes >= Gigabyte
        ? Decimal(bytes / (double)Gigabyte, 1, 0) + " GB" : Decimal(bytes / (double)Megabyte, 1, 0) + " MB";

    /// <summary>restore_set, encrypted or not_a_restore_set from the first 512 bytes (official sniffRestoreSet).</summary>
    public static string SniffRestoreSet(ReadOnlySpan<byte> head)
    {
        static string Ascii(ReadOnlySpan<byte> bytes, int start, int end)
        {
            var builder = new StringBuilder();
            for (var position = start; position < Math.Min(end, bytes.Length); position++)
            {
                if (bytes[position] == 0) break;
                builder.Append((char)bytes[position]);
            }
            return builder.ToString();
        }
        var start = Ascii(head, 0, 64);
        if (start.StartsWith("age-encryption.org/", StringComparison.Ordinal) || start.StartsWith("-----BEGIN AGE ENCRYPTED FILE-----", StringComparison.Ordinal)) return "encrypted";
        if (head.Length < 512 || Ascii(head, 257, 262) != "ustar") return "not_a_restore_set";
        var name = Ascii(head, 0, 100);
        return name == "restore-set.json" || name.EndsWith("/restore-set.json", StringComparison.Ordinal) ? "restore_set" : "not_a_restore_set";
    }

    public static string OpenedSentence(AdminBackupUpload upload)
    {
        if (upload.Source is not { } source) return T("web.admin.backups.upload.opened");
        var server = SourceServerName(source.ServerName) ?? T("web.admin.backups.upload.anotherServer");
        var taken = AdminBackupText.DateTimeText(source.CreatedAt!.Value);
        return KnownSiloVersion(source.SiloVersion) is { } version
            ? T("web.admin.backups.upload.openedFromWithVersion", new { server, taken, version })
            : T("web.admin.backups.upload.openedFrom", new { server, taken });
    }

    public static string UploadLabel(AdminBackupUpload upload) => upload.Source is { } source
        ? T("web.admin.backups.uploads.label", new { server = SourceServerName(source.ServerName) ?? T("web.admin.backups.uploads.anotherServer"),
            taken = AdminBackupText.DateTimeText(source.CreatedAt!.Value) })
        : T("web.admin.backups.uploads.unfinished", new { date = AdminBackupText.DateTimeText(upload.CreatedAt!.Value) });

    public static string UploadDetails(AdminBackupUpload upload)
    {
        var parts = new List<string> { DashboardFormat.FileSize(upload.SizeBytes, NoValue) };
        if (upload.Source is { } source && SourceServerName(source.ServerName) == null)
            parts.Add(T("web.admin.backups.uploads.serverId", new { id = ShortServerId(source.ServerId!) }));
        if (KnownSiloVersion(upload.Source?.SiloVersion) is { } version) parts.Add(T("web.admin.backups.uploads.siloVersion", new { version }));
        if (upload.Status != "complete") parts.Add(T("web.admin.backups.uploads.deletedAfter"));
        else if (upload.ExpiresAt is { } expires) parts.Add(T("web.admin.backups.uploads.keptUntil", new { date = AdminBackupText.DateTimeText(expires) }));
        return string.Join(" · ", parts);
    }

    /// <summary>Every part arrived but the check never finished; completing it again checks it.</summary>
    public static bool FullyReceived(AdminBackupUpload upload) => upload.Status == "uploading" && upload.ReceivedParts == upload.PartCount;
}
