using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>A run, cancel or schedule write may have reached the server, but no usable acknowledgement arrived. It is never resent.</summary>
public sealed class AdminTaskOutcomeUnknownException() : Exception("The task command outcome is unknown.");

/// <summary>The schedule changed since it was captured (412). The draft and original validator are kept; nothing is replayed.</summary>
public sealed class AdminTaskScheduleConflictException() : Exception("The task schedule changed on the server.");

/// <summary>The schedule read had no strong validator, so it cannot become an editing baseline.</summary>
public sealed class AdminTaskScheduleRevisionException() : Exception("Schedule revision unavailable. Reload before editing.");

/// <summary>
/// Configure › Scheduled Tasks wire operations (official <c>internal/apiv2/admin_tasks.go</c> and
/// <c>admin_task_metrics.go</c>): list, read, run, cancel, metrics, schedule read/write and history.
/// This is the scheduled-task surface, not the retained-job adapter. Every call is bound to a
/// captured admin lease, has a deadline that also covers reading the bounded body, and is sent
/// once without authentication replay or automatic retry. Responses are validated before use;
/// problem text is replaced by a safe code.
/// </summary>
public sealed partial class AdminTasksApi(SiloApiClient client)
{
    public const int MaxTasks = 1000;
    /// <summary>The server's <c>maxItems</c> for a schedule write.</summary>
    public const int MaxTriggers = 32;
    /// <summary>Persisted schedules predating the write bound are still shown truthfully up to this size.</summary>
    public const int MaxStoredTriggers = 64;
    public const int HistoryPageSize = 20;
    public const int MaxHistoryPageSize = 200;
    public const int MaxSamples = 50;
    public const int MaxKeyLength = 256;
    /// <summary>The largest <c>interval_ms</c>/<c>max_runtime_ms</c> the API accepts.</summary>
    public const long MaxDurationMs = 9_223_372_036_854;
    public const string MetricsTaskKey = "refresh_metadata";
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private const int MaxCursorLength = 4096;
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CommandDeadline = TimeSpan.FromSeconds(30);

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^([01][0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();

    /// <summary>
    /// A task key that is safe to send as one path segment: Silo's own keys and plugin keys
    /// (<c>plugin:&lt;installation&gt;:&lt;capability&gt;</c>). Anything else is refused before a request.
    /// </summary>
    public static bool IsTaskKey(string? key) => key is { Length: > 0 and <= MaxKeyLength } && KeyPattern().IsMatch(key) && !key.Contains("..", StringComparison.Ordinal);

    /// <summary>A daily or weekly time the server accepts (<c>15:04</c>).</summary>
    public static bool IsTimeOfDay(string? value) => value != null && TimePattern().IsMatch(value);

    public async Task<IReadOnlyList<AdminTaskInfo>> ListAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, ReadDeadline);
        var body = await ReadAsync<ListWire>(lease, HttpMethod.Get, "/api/v2/admin/tasks", timeout.Token).ConfigureAwait(false);
        if (body.Items == null) throw new InvalidDataException("Invalid task list.");
        if (body.Items.Length > MaxTasks) throw new InvalidDataException("The task list exceeds the supported size.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tasks = new List<AdminTaskInfo>(body.Items.Length);
        foreach (var item in body.Items)
        {
            var task = Validate(item);
            if (!seen.Add(task.Key)) throw new InvalidDataException("Duplicate task.");
            tasks.Add(task);
        }
        return tasks.AsReadOnly();
    }

    public async Task<AdminTaskInfo> GetAsync(MonitorAuthorityLease lease, string key, CancellationToken ct = default)
    {
        var path = TaskPath(key);
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, ReadDeadline);
        var task = Validate(await ReadAsync<TaskWire>(lease, HttpMethod.Get, path, timeout.Token).ConfigureAwait(false));
        return task.Key == key ? task : throw new InvalidDataException("The task response names another task.");
    }

    /// <summary>Starts the task on the responding process. The acknowledgement does not persist work intent.</summary>
    public Task<AdminTaskInfo> RunAsync(MonitorAuthorityLease lease, string key, CancellationToken ct = default) => CommandAsync(lease, key, "/run", ct);

    /// <summary>Requests cancellation on the responding process; completed effects remain.</summary>
    public Task<AdminTaskInfo> CancelAsync(MonitorAuthorityLease lease, string key, CancellationToken ct = default) => CommandAsync(lease, key, "/cancel", ct);

    private async Task<AdminTaskInfo> CommandAsync(MonitorAuthorityLease lease, string key, string command, CancellationToken ct)
    {
        var path = TaskPath(key) + command;
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, CommandDeadline);
        TaskWire body;
        try
        {
            body = (await client.MonitorLogsSendAsync<TaskWire>(lease, HttpMethod.Post, path, ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminTaskOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminTaskOutcomeUnknownException(); }
        AdminTaskInfo task;
        try { task = Validate(body); }
        catch (InvalidDataException) { throw new AdminTaskOutcomeUnknownException(); }
        return task.Key == key ? task : throw new AdminTaskOutcomeUnknownException();
    }

    /// <summary>Bounded <c>refresh_metadata</c> queue metrics; every other task has none.</summary>
    public async Task<AdminTaskMetrics> GetMetricsAsync(MonitorAuthorityLease lease, string key, CancellationToken ct = default)
    {
        if (key != MetricsTaskKey) throw new ArgumentException("Only refresh_metadata has queue metrics.", nameof(key));
        var path = TaskPath(key) + "/metrics";
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, ReadDeadline);
        var body = await ReadAsync<MetricsWire>(lease, HttpMethod.Get, path, timeout.Token).ConfigureAwait(false);
        if (body.Total is not { } total || body.Due is not { } due || body.Leased is not { } leased || total < 0 || due < 0 || leased < 0 ||
            body.ReasonCounts == null || body.AttemptBuckets == null || body.DueSamples == null || body.RecentErrors == null ||
            body.ReasonCounts.Length > MaxSamples || body.AttemptBuckets.Length > MaxSamples || body.DueSamples.Length > MaxSamples || body.RecentErrors.Length > MaxSamples)
            throw new InvalidDataException("Invalid task metrics.");
        return new(total, due, leased, body.OldestDueAt, body.OldestLeaseExpiresAt,
            Array.AsReadOnly(body.ReasonCounts.Select(entry => new AdminTaskMetricCount(Bound(Label(entry?.Reason), 64), Count(entry?.Count))).ToArray()),
            Array.AsReadOnly(body.AttemptBuckets.Select(entry => new AdminTaskAttemptBucket(Bound(Label(entry?.Label), 32), Count(entry?.Count))).ToArray()),
            Array.AsReadOnly(body.DueSamples.Select(Sample).ToArray()), Array.AsReadOnly(body.RecentErrors.Select(Sample).ToArray()));
    }

    /// <summary>Reads the persisted schedule with its strong validator, the baseline of one edit.</summary>
    public async Task<AdminTaskSchedule> GetScheduleAsync(MonitorAuthorityLease lease, string key, CancellationToken ct = default)
    {
        var path = TaskPath(key) + "/triggers";
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, ReadDeadline);
        ApiResponse<ScheduleWire> response;
        try { response = await client.MonitorLogsSendAsync<ScheduleWire>(lease, HttpMethod.Get, path, ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false); }
        catch (ApiException error) { throw Sanitize(error); }
        if (!AdminValidators.IsStrong(response.ETag)) throw new AdminTaskScheduleRevisionException();
        return new(key, ValidateSchedule(response.Body, key), response.ETag!);
    }

    /// <summary>
    /// Replaces the captured schedule once, guarded by the validator captured when editing began.
    /// 412 is a conflict; a timeout, server failure, transport failure or malformed receipt is an
    /// unknown outcome. Neither is ever replayed.
    /// </summary>
    public async Task<AdminTaskSchedule> UpdateScheduleAsync(MonitorAuthorityLease lease, string key, IReadOnlyList<AdminTaskTrigger> triggers, string etag,
        CancellationToken ct = default)
    {
        var path = TaskPath(key) + "/triggers";
        AdminValidators.RequireStrong(etag);
        var body = ScheduleBody(triggers);
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, CommandDeadline);
        ApiResponse<ScheduleWire> response;
        try
        {
            response = await client.MonitorLogsSendAsync<ScheduleWire>(lease, HttpMethod.Put, path, body, etag, timeout.Token, MaxResponseBytes).ConfigureAwait(false);
        }
        catch (LoggingSettingsConflictException) { throw new AdminTaskScheduleConflictException(); }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminTaskOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminTaskOutcomeUnknownException(); }
        if (!AdminValidators.IsStrong(response.ETag)) throw new AdminTaskOutcomeUnknownException();
        try { return new(key, ValidateSchedule(response.Body, key), response.ETag!); }
        catch (InvalidDataException) { throw new AdminTaskOutcomeUnknownException(); }
    }

    /// <summary>One page of persisted history; <paramref name="cursor"/> is the previous page's opaque continuation.</summary>
    public async Task<AdminTaskHistoryPage> GetHistoryAsync(MonitorAuthorityLease lease, string key, string? cursor, int limit = HistoryPageSize,
        CancellationToken ct = default)
    {
        var path = TaskPath(key) + "/history";
        if (limit is < 1 or > MaxHistoryPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
        if (cursor != null && cursor.Length is 0 or > MaxCursorLength) throw new ArgumentException("Invalid history cursor.", nameof(cursor));
        var query = "?limit=" + limit.ToString(CultureInfo.InvariantCulture) + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, ReadDeadline);
        var body = await ReadAsync<HistoryWire>(lease, HttpMethod.Get, path + query, timeout.Token).ConfigureAwait(false);
        if (body.Items == null || body.Page == null || body.Items.Length > limit) throw new InvalidDataException("Invalid task history.");
        var hasMore = body.Page.HasMore == true;
        var next = hasMore ? body.Page.NextCursor : null;
        if (hasMore && (next is not { Length: > 0 and <= MaxCursorLength } || next == cursor || body.Items.Length == 0))
            throw new InvalidDataException("Invalid task history continuation.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<AdminTaskExecution>(body.Items.Length);
        foreach (var item in body.Items)
        {
            var execution = Execution(item, requireId: true);
            if (execution.TaskKey != key || !seen.Add(execution.Id!)) throw new InvalidDataException("Invalid task history entry.");
            items.Add(execution);
        }
        return new(items.AsReadOnly(), next, hasMore);
    }


    private static string TaskPath(string key) =>
        IsTaskKey(key) ? "/api/v2/admin/tasks/" + Uri.EscapeDataString(key) : throw new ArgumentException("Invalid task key.", nameof(key));

    private static AdminTaskInfo Validate(TaskWire? task)
    {
        if (task is not { Key: { } key, Name: { Length: > 0 } name, Category: { Length: > 0 and <= 64 } category, State: { Length: > 0 and <= 64 } state, Triggers: { } triggers } ||
            !IsTaskKey(key) || task.Progress is not { } progress || !double.IsFinite(progress) || triggers.Length > MaxStoredTriggers)
            throw new InvalidDataException("Invalid task.");
        return new(key, Bound(name, 200), Bound(task.Description ?? "", 2000), category, state, progress, task.ManualOnly == true,
            task.LastExecution == null ? null : Execution(task.LastExecution, requireId: false),
            Array.AsReadOnly(triggers.Select(Trigger).ToArray()), task.NextRunAt, task.StartedAt);
    }

    private static IReadOnlyList<AdminTaskTrigger> ValidateSchedule(ScheduleWire body, string key)
    {
        if (body.TaskKey != key || body.Triggers == null || body.Triggers.Length > MaxStoredTriggers) throw new InvalidDataException("Invalid task schedule.");
        return Array.AsReadOnly(body.Triggers.Select(Trigger).ToArray());
    }

    private static AdminTaskTrigger Trigger(TriggerWire? trigger)
    {
        if (trigger is not { Type: { Length: > 0 and <= 32 } type } || trigger.IntervalMs is < 0 or > MaxDurationMs || trigger.MaxRuntimeMs is < 0 or > MaxDurationMs ||
            trigger.DayOfWeek is < 0 or > 6 || trigger.TimeOfDay is { Length: > 16 })
            throw new InvalidDataException("Invalid task trigger.");
        return new(type, trigger.IntervalMs, trigger.TimeOfDay, trigger.DayOfWeek, trigger.MaxRuntimeMs);
    }

    private static AdminTaskExecution Execution(ExecutionWire? execution, bool requireId)
    {
        if (execution is not { TaskKey: { } taskKey, Status: { Length: > 0 and <= 32 } status, StartedAt: { } started, CompletedAt: { } completed, DurationMs: >= 0 } ||
            !IsTaskKey(taskKey) || (execution.Steps?.Length ?? 0) > MaxSamples)
            throw new InvalidDataException("Invalid task execution.");
        var id = IdText(execution.Id);
        if (requireId && id is not { Length: > 0 and <= 128 }) throw new InvalidDataException("Invalid task execution.");
        var steps = (execution.Steps ?? []).Select(step => step is { Key: { Length: > 0 } stepKey, Name: { } stepName, Status: { Length: > 0 and <= 32 } stepStatus }
            ? new AdminTaskStep(Bound(stepKey, 128), Title(stepName, 200), stepStatus) : throw new InvalidDataException("Invalid task step.")).ToArray();
        AdminTaskMarkerResult? result = null;
        if (execution.ResultData is { } data)
        {
            if (data.Submitted is not { } submitted || data.Skipped is not { } skipped || data.Invalid is not { } invalid || data.Failed is not { } failed ||
                data.RetryAfterSeconds is not { } retry)
                throw new InvalidDataException("Invalid task result.");
            result = new(submitted, skipped, invalid, failed, retry);
        }
        var message = string.IsNullOrWhiteSpace(execution.ErrorMessage) ? null : Bound(LocalLog.RedactDiagnosticText(execution.ErrorMessage.Trim()), 500);
        return new(id, taskKey, started, completed, status, execution.DurationMs!.Value, message, Array.AsReadOnly(steps), result);
    }

    private static AdminTaskDebtSample Sample(DebtSampleWire? sample)
    {
        if (sample is not { NextRefreshAt: { } next, AttemptCount: >= 0 } || IdText(sample.ContentId) is not { Length: > 0 and <= 128 } rawContentId ||
            Title(rawContentId, 128) is not { Length: > 0 } contentId)
            throw new InvalidDataException("Invalid task metric sample.");
        var error = string.IsNullOrWhiteSpace(sample.LastError) ? "" : SafeSampleError;
        // The title, the series title, the item type and the content ID (shown when there is no title) are composed into one
        // sentence on the page: each is reduced to one bounded line with no control, separator or direction character.
        return new(Bound(sample.TargetType ?? "", 64), contentId, Title(sample.Title, 300), Title(sample.Type, 64), sample.ReasonMask ?? 0,
            next, sample.LastAttemptAt, sample.AttemptCount!.Value, error,
            Title(OptionalText(sample.SeriesTitle), 300), OptionalNumber(sample.SeasonNumber), OptionalNumber(sample.EpisodeNumber));
    }

    /// <summary>
    /// Server text about the user's own media, shown on one line: the plugin-label rule (<see cref="AdminPluginText"/>: whitespace and
    /// line or paragraph separators become one space, control, format and direction characters are removed, the result is bounded at a
    /// whole character) except that U+200C and U+200D are kept where they join characters (emoji sequences, Persian).
    /// </summary>
    private static string Title(string? value, int max) => AdminPluginText.Plain(value, max, keepJoiners: true);

    /// <summary>An optional text field: any other JSON kind (a number, an object) counts as absent instead of failing the whole read.</summary>
    private static string? OptionalText(JsonElement? value) => value is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    /// <summary>
    /// An optional season or episode number: an integer in 0..int.MaxValue. A fraction, an exponent out of range, a negative number
    /// or another JSON kind counts as absent instead of failing the whole read.
    /// </summary>
    private static int? OptionalNumber(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Number } number && number.TryGetInt64(out var integer) && integer is >= 0 and <= int.MaxValue ? (int)integer : null;

    private static string Label(string? value) => value is { Length: > 0 } label ? label : throw new InvalidDataException("Invalid task metric.");
    private static int Count(int? value) => value is >= 0 ? value.Value : throw new InvalidDataException("Invalid task metric.");

    internal const string SafeSampleError ="Refresh failed. Inspect administrator diagnostics for details.";

    private static JsonElement ScheduleBody(IReadOnlyList<AdminTaskTrigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        if (triggers.Count > MaxTriggers) throw new ArgumentException("Too many triggers.", nameof(triggers));
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("triggers");
            foreach (var trigger in triggers)
            {
                if (trigger == null || !AdminTaskTriggerTypes.All.Contains(trigger.Type)) throw new ArgumentException("Invalid trigger type.", nameof(triggers));
                if (trigger.Type == AdminTaskTriggerTypes.Interval && trigger.IntervalMs is not (>= 1 and <= MaxDurationMs))
                    throw new ArgumentException("An interval trigger needs a positive interval.", nameof(triggers));
                if (trigger.Type is AdminTaskTriggerTypes.Daily or AdminTaskTriggerTypes.Weekly && !IsTimeOfDay(trigger.TimeOfDay))
                    throw new ArgumentException("A daily or weekly trigger needs HH:MM.", nameof(triggers));
                if (trigger.IntervalMs is < 0 or > MaxDurationMs || trigger.MaxRuntimeMs is < 0 or > MaxDurationMs || trigger.DayOfWeek is < 0 or > 6 ||
                    (trigger.TimeOfDay != null && !IsTimeOfDay(trigger.TimeOfDay)))
                    throw new ArgumentException("Invalid trigger value.", nameof(triggers));
                writer.WriteStartObject();
                writer.WriteString("type", trigger.Type);
                if (trigger.IntervalMs is { } interval) writer.WriteNumber("interval_ms", interval);
                if (trigger.TimeOfDay is { } time) writer.WriteString("time_of_day", time);
                if (trigger.DayOfWeek is { } day) writer.WriteNumber("day_of_week", day);
                if (trigger.MaxRuntimeMs is { } runtime) writer.WriteNumber("max_runtime_ms", runtime);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private async Task<T> ReadAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path, CancellationToken ct)
    {
        try { return (await client.MonitorLogsSendAsync<T>(lease, method, path, ct: ct, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body; }
        catch (ApiException error) { throw Sanitize(error); }
    }

    /// <summary>Cuts at <paramref name="max"/> UTF-16 units without splitting a surrogate pair, and marks the cut.</summary>
    private static string Bound(string value, int max)
    {
        if (value.Length <= max) return value;
        var cut = max > 0 && char.IsHighSurrogate(value[max - 1]) && char.IsLowSurrogate(value[max]) ? max - 1 : max;
        return value[..cut] + "…";
    }

    private static string? IdText(JsonElement? value) => value?.ValueKind switch
    {
        JsonValueKind.String => value.Value.GetString(),
        JsonValueKind.Number => value.Value.GetRawText(),
        _ => null,
    };

    private static CancellationTokenSource Timeout(MonitorAuthorityLease lease, CancellationToken ct, TimeSpan deadline)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(deadline);
        return linked;
    }

    private static string SafeCode(string? code) => code is "permission_denied" or "profile_unverified" or "profile_verification_required"
        or "demo_restricted" or "dependency_unavailable" or "validation_failed" or "not_found" or "conflict" or "precondition_required"
        or "precondition_failed" or "capability_disabled" or "capability_not_configured" or "capability_unsupported" or "rate_limited"
        or "authentication_required"
        ? code : "unknown";

    /// <summary>Problem titles and details are server text; the page shows its own localized message instead.</summary>
    private static ApiException Sanitize(ApiException error) => new(SafeCode(error.ErrorCode), "Task request failed.", error.StatusCode)
    {
        ProblemType = SafeCode(error.ProblemType) is var code && code != "unknown" ? code : null,
    };

    private sealed record ListWire { public TaskWire?[]? Items { get; init; } }
    private sealed record TaskWire
    {
        public string? Key { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Category { get; init; }
        public string? State { get; init; }
        public double? Progress { get; init; }
        public bool? ManualOnly { get; init; }
        public ExecutionWire? LastExecution { get; init; }
        public TriggerWire?[]? Triggers { get; init; }
        public DateTimeOffset? NextRunAt { get; init; }
        public DateTimeOffset? StartedAt { get; init; }
    }
    private sealed record TriggerWire
    {
        public string? Type { get; init; }
        public long? IntervalMs { get; init; }
        public string? TimeOfDay { get; init; }
        public int? DayOfWeek { get; init; }
        public long? MaxRuntimeMs { get; init; }
    }
    private sealed record ExecutionWire
    {
        public JsonElement? Id { get; init; }
        public string? TaskKey { get; init; }
        public DateTimeOffset? StartedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
        public string? Status { get; init; }
        public long? DurationMs { get; init; }
        public string? ErrorMessage { get; init; }
        public StepWire?[]? Steps { get; init; }
        public MarkerResultWire? ResultData { get; init; }
        public override string ToString() => nameof(ExecutionWire);
    }
    private sealed record StepWire { public string? Key { get; init; } public string? Name { get; init; } public string? Status { get; init; } }
    private sealed record MarkerResultWire
    {
        public int? Submitted { get; init; }
        public int? Skipped { get; init; }
        public int? Invalid { get; init; }
        public int? Failed { get; init; }
        public int? RetryAfterSeconds { get; init; }
    }
    private sealed record ScheduleWire { public string? TaskKey { get; init; } public TriggerWire?[]? Triggers { get; init; } }
    private sealed record HistoryWire { public ExecutionWire?[]? Items { get; init; } public PageWire? Page { get; init; } }
    private sealed record PageWire
    {
        public string? NextCursor { get; init; }
        public bool? HasMore { get; init; }
        public override string ToString() => nameof(PageWire);
    }
    private sealed record MetricsWire
    {
        public int? Total { get; init; }
        public int? Due { get; init; }
        public int? Leased { get; init; }
        public DateTimeOffset? OldestDueAt { get; init; }
        public DateTimeOffset? OldestLeaseExpiresAt { get; init; }
        public CountWire?[]? ReasonCounts { get; init; }
        public BucketWire?[]? AttemptBuckets { get; init; }
        public DebtSampleWire?[]? DueSamples { get; init; }
        public DebtSampleWire?[]? RecentErrors { get; init; }
    }
    private sealed record CountWire { public string? Reason { get; init; } public int? Count { get; init; } }
    private sealed record BucketWire { public string? Label { get; init; } public int? Count { get; init; } }
    private sealed record DebtSampleWire
    {
        public string? TargetType { get; init; }
        public JsonElement? ContentId { get; init; }
        public string? Title { get; init; }
        public string? Type { get; init; }
        // Optional parts of the composed title: read as raw JSON so a value of another kind or out of range is only ignored.
        public JsonElement? SeriesTitle { get; init; }
        public JsonElement? SeasonNumber { get; init; }
        public JsonElement? EpisodeNumber { get; init; }
        public long? ReasonMask { get; init; }
        public DateTimeOffset? NextRefreshAt { get; init; }
        public DateTimeOffset? LastAttemptAt { get; init; }
        public int? AttemptCount { get; init; }
        public string? LastError { get; init; }
        public override string ToString() => nameof(DebtSampleWire);
    }
}
