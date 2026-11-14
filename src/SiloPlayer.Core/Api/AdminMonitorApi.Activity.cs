using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Activity page reads and session actions: live playback sessions, sequenced
/// playback commands, terminate, IP lookup and the per-session FFmpeg console.
/// Every call takes the caller's captured <see cref="MonitorAuthorityLease"/>,
/// checks it before sending and after every response, and never recaptures.
/// </summary>
public partial class AdminMonitorApi
{
    /// <summary>Sessions per page while draining; the WebUI reads 100 at a time.</summary>
    public const int ActivitySessionPageSize = 100;
    /// <summary>Most pages drained before the list is refused as too large to show.</summary>
    public const int ActivityMaxSessionPages = 100;
    /// <summary>FFmpeg console rows per session.</summary>
    public const int ActivityFfmpegRows = 12;
    public const int ActivityIpLookupDays = 30;
    public const int ActivityIpLookupPageSize = 50;

    private static readonly Regex ActivityNonNegativeInteger = new("^(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex ActivityPositiveInteger = new("^[1-9][0-9]*$", RegexOptions.CultureInvariant);

    /// <summary>Process-wide, as the WebUI's module-level maps: several surfaces may command one session.</summary>
    public AdminActivityCommandLedger ActivityCommandLedger { get; } = new();

    private static CancellationToken ActivityToken(MonitorAuthorityLease lease, CancellationToken ct, out CancellationTokenSource? linked)
    {
        linked = ct.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct) : null;
        return linked?.Token ?? lease.Token;
    }

    /// <summary>
    /// Drains every live session. Rows are keyed by session id (a refresh may
    /// find sessions inserted behind a cursor); a failure on any page publishes
    /// nothing new.
    /// </summary>
    public async Task<IReadOnlyList<AdminActivitySession>> ListActivitySessionsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var token = ActivityToken(lease, ct, out var linked);
        using var linkedScope = linked;
        var rows = new Dictionary<string, AdminActivitySession>(StringComparer.Ordinal);
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var pageIndex = 0; pageIndex < ActivityMaxSessionPages; pageIndex++)
        {
            lease.ThrowIfRetired();
            var path = $"/api/v2/admin/sessions?limit={ActivitySessionPageSize}" +
                       (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var page = await client.SendRequestAsync<ApiCollectionPage<AdminActivitySession>>(
                lease.Context, HttpMethod.Get, path, null, token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (page?.Page == null) throw new InvalidDataException("Invalid session page.");
            foreach (var row in page.Items ?? [])
            {
                ValidateActivitySession(row);
                if (!rows.ContainsKey(row.SessionId)) order.Add(row.SessionId);
                rows[row.SessionId] = row;
            }
            if (!page.Page.HasMore) return order.Select(id => rows[id]).ToArray();
            var next = page.Page.NextCursor;
            if (string.IsNullOrEmpty(next) || !seen.Add(next)) throw new InvalidDataException("Invalid session continuation.");
            cursor = next;
        }
        throw new InvalidDataException(AdminText.T("web.admin.errors.sessionsTooMany"));
    }

    private static void ValidateActivitySession(AdminActivitySession row)
    {
        if (string.IsNullOrWhiteSpace(row.SessionId)) throw new InvalidDataException("Unsupported session identifier.");
        foreach (var id in new[] { row.UserId, row.MediaFileId, row.RequestedMediaFileId })
            if (!ActivityNonNegativeInteger.IsMatch(id ?? "")) throw new InvalidDataException("Unsupported session identifier.");
        foreach (var id in new[] { row.RoutingExecutionNodeId, row.RoutingEgressNodeId })
            if (id != null && !ActivityNonNegativeInteger.IsMatch(id)) throw new InvalidDataException("Unsupported session identifier.");
    }

    /// <summary>The intent a repeated command must share to reuse an unsettled identity (no secrets).</summary>
    public static string ActivityCommandIntent(MonitorAuthorityLease lease, string action, string? message)
    {
        var context = lease.Context;
        // Transport catch-up rebinds requests, but must not change an unknown
        // command's identity. Other context changes retire the lease's epoch.
        return JsonSerializer.Serialize(new object?[]
        {
            context.BaseUrl,
            context.AuthenticationGeneration,
            context.AccessGeneration,
            context.ProfileId,
            lease.AccountId,
            lease.Epoch,
            action,
            action == "message" ? message ?? "" : null,
        });
    }

    /// <summary>
    /// Sends one sequenced pause, resume, stop or message command. It is never
    /// retried automatically; repeating the same intent under the same lease after
    /// an unknown outcome resends the same identity so the server replays its receipt.
    /// </summary>
    public async Task<AdminActivityCommandReceipt> SendActivityCommandAsync(MonitorAuthorityLease lease, string sessionId,
        string action, string? message = null, CancellationToken ct = default)
    {
        if (action is not ("pause" or "resume" or "stop" or "message"))
            throw new ArgumentException($"Unsupported session command: {action}", nameof(action));
        var text = message?.Trim() ?? "";
        if (action == "message" && text.Length == 0)
            throw new ArgumentException(AdminText.T("web.admin.sessions.playback.toast.messageRequired"), nameof(message));
        var token = ActivityToken(lease, ct, out var linked);
        using var linkedScope = linked;
        lease.ThrowIfRetired();
        var intent = ActivityCommandIntent(lease, action, text);
        var identity = ActivityCommandLedger.Begin(sessionId, intent);
        var body = new Dictionary<string, object>
        {
            ["command_id"] = identity.CommandId,
            ["sequence"] = identity.Sequence,
        };
        if (action == "message") body["message"] = text;
        var path = $"/api/v2/admin/sessions/{Uri.EscapeDataString(sessionId)}/{action}";
        AdminActivityCommandReceipt receipt;
        try
        {
            receipt = await client.SendAdminActivityCommandAsync<AdminActivityCommandReceipt>(lease.Context, path, body, token)
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (IsActivityOutcomeUnknown(error, token)) ActivityCommandLedger.RecordOutcomeUnknown(sessionId, intent, identity);
            else ActivityCommandLedger.Settle(sessionId, identity);
            if (error is AdminActivityCommandException refused &&
                AdminActivityCommandLedger.ParseLatestSequence(refused.LatestSequenceHeader) is { } latest)
                ActivityCommandLedger.ObserveLatestSequence(sessionId, latest);
            throw;
        }
        ActivityCommandLedger.Settle(sessionId, identity);
        lease.ThrowIfRetired();
        if (!string.Equals(receipt.CommandId, identity.CommandId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The server answered a different command identity.");
        return receipt;
    }

    /// <summary>Whether an error leaves open whether the server applied the command.</summary>
    public static bool IsActivityOutcomeUnknown(Exception error, CancellationToken token = default) => error switch
    {
        MonitorAuthorityRetiredException => false,
        ApiException api => api.StatusCode >= 500,
        // A context change or caller/lease cancellation is settled, as the WebUI's
        // stale-authority refusal is; only a transport timeout is uncertain.
        OperationCanceledException when token.IsCancellationRequested => false,
        TaskCanceledException { InnerException: TimeoutException } => true,
        OperationCanceledException => false,
        _ => true,
    };

    /// <summary>
    /// Terminate revokes the session's playback authority durably first, then
    /// notifies the client best-effort. It converges on repeat; never auto-replayed.
    /// </summary>
    public async Task<AdminActivityTerminateReceipt> TerminateActivitySessionAsync(MonitorAuthorityLease lease, string sessionId,
        string? reason = null, CancellationToken ct = default)
    {
        var token = ActivityToken(lease, ct, out var linked);
        using var linkedScope = linked;
        lease.ThrowIfRetired();
        var body = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(reason)) body["reason"] = reason;
        var receipt = await client.SendRequestWithoutRefreshAsync<AdminActivityTerminateReceipt>(lease.Context, HttpMethod.Post,
            $"/api/v2/admin/sessions/{Uri.EscapeDataString(sessionId)}/terminate", body, token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!string.Equals(receipt.SessionId, sessionId, StringComparison.Ordinal))
            throw new InvalidDataException("The server answered for a different playback session.");
        return receipt;
    }

    public async Task<AdminActivityCommandCapabilities> GetActivityCommandCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var token = ActivityToken(lease, ct, out var linked);
        using var linkedScope = linked;
        lease.ThrowIfRetired();
        var capabilities = await client.SendRequestAsync<AdminActivityCommandCapabilities>(lease.Context, HttpMethod.Get,
            "/api/v2/admin/sessions/command-capabilities", null, token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return capabilities;
    }

    /// <summary>
    /// One page of accounts seen from <paramref name="ip"/> in the last 30 days.
    /// <paramref name="priorCursors"/> are cursors already read for this lookup;
    /// a continuation that repeats one is refused rather than looping.
    /// </summary>
    public async Task<AdminActivityIpUserPage> LookupActivityIpUsersAsync(MonitorAuthorityLease lease, string ip, string? cursor,
        IReadOnlyCollection<string?> priorCursors, CancellationToken ct = default)
    {
        var token = ActivityToken(lease, ct, out var linked);
        using var linkedScope = linked;
        lease.ThrowIfRetired();
        var path = $"/api/v2/admin/ips?ip={Uri.EscapeDataString(ip)}&days={ActivityIpLookupDays}&limit={ActivityIpLookupPageSize}" +
                   (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
        var page = await client.SendRequestAsync<ApiCollectionPage<AdminActivityIpUser>>(lease.Context, HttpMethod.Get, path, null, token)
            .ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (page?.Items == null) throw new InvalidDataException(AdminText.T("web.admin.errors.activityItemsInvalid"));
        foreach (var row in page.Items)
            if (!ActivityPositiveInteger.IsMatch(row.UserId ?? "")) throw new InvalidDataException("Unsupported account ID in IP history.");
        var next = NextActivityIpCursor(page.Page, cursor);
        if (next != null && priorCursors.Contains(next))
            throw new InvalidDataException(AdminText.T("web.admin.errors.activityCursorRepeated"));
        return new AdminActivityIpUserPage(page.Items, next);
    }

    private static string? NextActivityIpCursor(ApiPageInfo? page, string? cursor)
    {
        if (page == null ||
            (page.HasMore && (string.IsNullOrEmpty(page.NextCursor) || page.NextCursor == cursor)) ||
            (!page.HasMore && !string.IsNullOrEmpty(page.NextCursor)))
            throw new InvalidDataException(AdminText.T("web.admin.errors.activityPageInvalid"));
        return page.HasMore ? page.NextCursor : null;
    }

    /// <summary>The newest FFmpeg rows logged for one playback session (inline console only).</summary>
    public async Task<IReadOnlyList<AdminActivityLogRow>> ListActivityFfmpegRowsAsync(MonitorAuthorityLease lease, string playbackSessionId,
        CancellationToken ct = default)
    {
        var token = ActivityToken(lease, ct, out var linked);
        using var linkedScope = linked;
        lease.ThrowIfRetired();
        var path = "/api/v2/admin/logs/app?playback_session_id=" + Uri.EscapeDataString(playbackSessionId) +
                   $"&component=ffmpeg&limit={ActivityFfmpegRows}";
        var page = await client.SendRequestAsync<ApiCollectionPage<AdminActivityLogRow>>(lease.Context, HttpMethod.Get, path, null, token)
            .ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (page?.Page == null || (page.Page.HasMore && string.IsNullOrEmpty(page.Page.NextCursor)))
            throw new InvalidDataException("Log response is missing pagination metadata.");
        foreach (var row in page.Items ?? [])
            if (!ActivityPositiveInteger.IsMatch(row.Id ?? "") || !long.TryParse(row.Id, out _))
                throw new InvalidDataException("Log identifier cannot be represented by this client.");
        return page.Items ?? [];
    }
}
