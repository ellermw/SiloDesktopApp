using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public sealed class AdminNotificationOperationUnknownException() : Exception("The operation outcome is unknown. Reload and review before taking another action.");

/// <summary>Official v2 admin notification operations at 217ff25. One-shot writes, refreshable reads,
/// 20-second deadline, 512-KiB response bound. Server channels have no conditional-write validator.</summary>
public sealed class AdminNotificationSettingsApi(SiloApiClient client)
{
    public const string ChannelPath = "/api/v2/admin/notifications/server-channels";
    private const string Root = "/api/v2/admin/notifications/";
    public static IReadOnlyList<string> EventKeys { get; } = Array.AsReadOnly(new[]
    {
        "notify_new_movies", "notify_new_episodes", "notify_new_audiobooks", "notify_new_ebooks",
        "notify_request_submitted", "notify_request_approved", "notify_request_declined", "notify_request_fulfilled",
    });
    private static string Id(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException("Invalid channel identity.");
        return Uri.EscapeDataString(value);
    }
    private static CancellationTokenSource Timeout(MonitorAuthorityLease owner)
    {
        owner.ThrowIfRetired(); var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20)); return timeout;
    }
    public async Task<AdminNotificationChannelPage> GetChannelsAsync(MonitorAuthorityLease owner, string? cursor = null)
    {
        if (cursor?.Length > 4096) throw new InvalidDataException("Invalid channel continuation.");
        var path = ChannelPath + "?limit=100" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
        var root = await Read(owner, path).ConfigureAwait(false);
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 100 ||
            !root.TryGetProperty("page", out var page) || !page.TryGetProperty("has_more", out var more) || more.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Invalid notification channel page.");
        string? next = null;
        if (more.GetBoolean())
        {
            next = Required(page, "next_cursor", 4096);
            if (next == cursor) throw new InvalidDataException("Invalid channel continuation.");
        }
        var rows = items.EnumerateArray().Select(ParseChannel).ToArray();
        if (rows.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != rows.Length) throw new InvalidDataException("Duplicate channel identity.");
        return new(Array.AsReadOnly(rows), next);
    }
    private async Task<JsonElement> Read(MonitorAuthorityLease owner, string path)
    {
        using var timeout = Timeout(owner);
        try { return await client.AdminNotificationReadAsync(owner, path, timeout.Token).ConfigureAwait(false); }
        catch (ApiException error) { throw AdminServerSettingsApi.Sanitize(error); }
    }
    private async Task<T> Write<T>(MonitorAuthorityLease owner, HttpMethod method, string path, object? body, Func<JsonElement, T> parse)
    {
        using var timeout = Timeout(owner);
        try
        {
            var result = await client.MonitorLogsSendAsync<JsonElement>(owner, method, path, body, ct: timeout.Token, maxBytes: 512 * 1024).ConfigureAwait(false);
            owner.ThrowIfRetired(); return parse(result.Body);
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { throw; }
        catch (ApiException error) when (error.StatusCode < 500 && error.StatusCode != 408) { throw AdminServerSettingsApi.Sanitize(error); }
        catch (Exception) { owner.ThrowIfRetired(); throw new AdminNotificationOperationUnknownException(); }
    }
    public Task<AdminNotificationRelayRegistration> RegisterRelayAsync(MonitorAuthorityLease owner, string url)
    {
        url = url.Trim();
        if (url.Length > 2048 || url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != "https" || parsed.UserInfo.Length > 0 ||
            parsed.AbsolutePath != "/" || parsed.Query.Length > 0 || parsed.Fragment.Length > 0))
            throw new ArgumentException("Enter an HTTPS relay origin without credentials, or leave it blank for the default.");
        return Write(owner, HttpMethod.Post, Root + "push/relay/register", new { relay_url = url }, r =>
        {
            var acknowledgedUrl = Required(r, "relay_url", 2048);
            if (!string.Equals(acknowledgedUrl.TrimEnd('/'), url.Length == 0 ? "https://push.siloserver.org" : url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected relay registration target.");
            if (!Boolean(r, "api_key_configured") || Instant(r, "expires_at") == null) throw new InvalidDataException("Invalid relay registration acknowledgement.");
            return new AdminNotificationRelayRegistration(Required(r, "deployment_id", 512), Required(r, "key_prefix", 256), Required(r, "expires_at", 128), Optional(r, "relay_request_id", 512));
        });
    }
    public Task ClearRelayAsync(MonitorAuthorityLease owner) => Delete(owner, Root + "push/relay");
    public Task<AdminNotificationTestResult> TestDiscordAsync(MonitorAuthorityLease owner) => Write(owner, HttpMethod.Post, Root + "discord/test", null, ParseTest);
    public Task<AdminNotificationTestResult> TestChannelAsync(MonitorAuthorityLease owner, string id) => Write(owner, HttpMethod.Post, ChannelPath + "/" + Id(id) + "/test", null, ParseTest);
    public Task<AdminNotificationChannelReceipt> SaveChannelAsync(MonitorAuthorityLease owner, string? id, IReadOnlyDictionary<string, object> input)
    {
        var allowed = EventKeys.Concat(["name", "url", "enabled"]).ToHashSet(StringComparer.Ordinal);
        if (input.Count == 0 || input.Keys.Any(k => !allowed.Contains(k))) throw new ArgumentException("Invalid channel update.");
        var body = new Dictionary<string, object>(input, StringComparer.Ordinal);
        foreach (var (key, value) in body)
        {
            if (key is "name" or "url")
            {
                if (value is not string text || text.Length > (key == "name" ? 64 : 4096) || string.IsNullOrWhiteSpace(text)) throw new ArgumentException("A channel name and destination are required.");
            }
            else if (value is not bool) throw new ArgumentException("Invalid channel event selection.");
        }
        if (id == null && (!body.ContainsKey("name") || !body.ContainsKey("url"))) throw new ArgumentException("A channel name and destination are required.");
        return Write(owner, id == null ? HttpMethod.Post : HttpMethod.Put, ChannelPath + (id == null ? "" : "/" + Id(id)), body, r =>
        {
            var acknowledgedId = Required(r, "id", 256);
            if (id != null && acknowledgedId != id) throw new InvalidDataException("Unexpected channel update target.");
            return new AdminNotificationChannelReceipt(acknowledgedId, Optional(r, "signing_secret", 4096));
        });
    }
    public Task<string> RotateChannelAsync(MonitorAuthorityLease owner, string id) => Write(owner, HttpMethod.Post, ChannelPath + "/" + Id(id) + "/rotate-secret", null,
        r => Required(r, "signing_secret", 4096));
    public Task DeleteChannelAsync(MonitorAuthorityLease owner, string id) => Delete(owner, ChannelPath + "/" + Id(id));
    private async Task Delete(MonitorAuthorityLease owner, string path)
    {
        using var timeout = Timeout(owner);
        try { await client.AdminNotificationDeleteAsync(owner, path, timeout.Token, acceptMissing: path.StartsWith(ChannelPath + "/", StringComparison.Ordinal)).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { throw; }
        catch (ApiException error) when (error.StatusCode < 500 && error.StatusCode != 408) { throw AdminServerSettingsApi.Sanitize(error); }
        catch (Exception) { owner.ThrowIfRetired(); throw new AdminNotificationOperationUnknownException(); }
    }
    private static AdminNotificationTestResult ParseTest(JsonElement root)
    {
        var ok = Boolean(root, "ok");
        if (!root.TryGetProperty("duration_ms", out var duration) || !duration.TryGetInt64(out var ms) || ms < 0) throw new InvalidDataException("Invalid test acknowledgement.");
        return new(ok, ms, Integer(root, "http_status"), Safe(Optional(root, "message", 4096)));
    }
    private static AdminNotificationChannel ParseChannel(JsonElement root) => new()
    {
        Id = Required(root, "id", 256), Name = Safe(Required(root, "name", 64))!, Type = Required(root, "type", 16) is var type && type is "generic" or "discord" ? type : throw new InvalidDataException("Invalid channel type."),
        UrlHost = Safe(Required(root, "url_host", 512))!, Enabled = Boolean(root, "enabled"),
        Events = new System.Collections.ObjectModel.ReadOnlyDictionary<string, bool>(EventKeys.ToDictionary(k => k, k => Boolean(root, k))),
        ConsecutiveFailures = Integer(root, "consecutive_failures") ?? 0, DisabledReason = Safe(Optional(root, "disabled_reason", 1024)),
        LastSuccessAt = Instant(root, "last_success_at"), LastFailureAt = Instant(root, "last_failure_at"),
        LastFailureStatus = Integer(root, "last_failure_status"), LastFailureMessage = Safe(Optional(root, "last_failure_message", 4096)),
    };
    private static string Required(JsonElement root, string key, int max) => Optional(root, key, max) is { Length: > 0 } text ? text : throw new InvalidDataException("Invalid notification acknowledgement.");
    private static string? Optional(JsonElement root, string key, int max)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > max) throw new InvalidDataException("Invalid notification data.");
        return value.GetString();
    }
    private static bool Boolean(JsonElement root, string key) => root.TryGetProperty(key, out var b) && b.ValueKind is JsonValueKind.True or JsonValueKind.False ? b.GetBoolean() : throw new InvalidDataException("Invalid notification data.");
    private static int? Integer(JsonElement root, string key) => !root.TryGetProperty(key, out var n) || n.ValueKind == JsonValueKind.Null ? null : n.TryGetInt32(out var value) ? value : throw new InvalidDataException("Invalid notification data.");
    private static DateTimeOffset? Instant(JsonElement root, string key) => Optional(root, key, 128) is { } raw ? DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var time) ? time : throw new InvalidDataException("Invalid channel time.") : null;
    private static string? Safe(string? raw) => raw == null ? null : LocalLog.RedactDiagnosticText(raw.ReplaceLineEndings(" "));
}
