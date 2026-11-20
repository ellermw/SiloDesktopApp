using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public sealed class AdminProviderConflictException() : Exception("Provider settings changed on the server. Reload and review your draft.");
public sealed class AdminProviderOutcomeUnknownException() : Exception("The provider update outcome is unknown. Reload and review before saving again.");
public sealed class AdminProviderTestOutcomeUnknownException() : Exception("The provider test outcome is unknown. Do not repeat it automatically.");
public sealed class AdminProviderLimitException(int limit) : Exception("The provider response exceeds the supported size.")
{
    public int Limit { get; } = limit;
}

/// <summary>
/// Configure > Providers wire operations pinned to Silo Server 217ff25. Reads and pagination are
/// bounded. Every editor/write is authority-bound and single-attempt; credential material is never
/// included in an exception or diagnostic representation.
/// </summary>
public sealed partial class AdminProvidersApi(SiloApiClient client)
{
    public const int MaxSubtitleProviders = 32;
    public const int MaxMarkerProviders = 64;
    public const int MaxPluginInstallations = 1000;
    public const int MaxPluginPages = 20;
    private const int MaxResponseBytes = 1024 * 1024;
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan WriteDeadline = TimeSpan.FromSeconds(30);

    [GeneratedRegex("^[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PositiveRevision();
    [GeneratedRegex("^(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex NonNegativeRevision();

    public async Task<IReadOnlyList<AdminSubtitleProvider>> ListSubtitleProvidersAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        using var timeout = Timeout(lease, ct, ReadDeadline);
        var response = await ReadAsync<AdminSubtitleProvidersResponse>(lease, HttpMethod.Get,
            "/api/v2/admin/subtitle-providers", timeout.Token).ConfigureAwait(false);
        if (response.Providers == null || response.Providers.Count > MaxSubtitleProviders)
            throw new AdminProviderLimitException(MaxSubtitleProviders);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in response.Providers)
            ValidateSubtitle(provider, seen);
        return Array.AsReadOnly(response.Providers.OrderBy(provider => SubtitleOrder(provider.ProviderName)).ThenBy(provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<AdminSubtitleProviderEditor> GetSubtitleProviderAsync(MonitorAuthorityLease lease, string provider, CancellationToken ct = default)
    {
        RequireProvider(provider);
        using var timeout = Timeout(lease, ct, ReadDeadline);
        ApiResponse<AdminSubtitleProvider> response;
        try
        {
            response = await client.MonitorLogsSendAsync<AdminSubtitleProvider>(lease, HttpMethod.Get,
                SubtitlePath(provider), ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false);
        }
        catch (ApiException error) { throw AdminServerSettingsApi.Sanitize(error); }
        AdminSettingsForm.RequireStrongETag(response.ETag);
        ValidateSubtitle(response.Body, null);
        if (!string.Equals(response.Body.ProviderName, provider, StringComparison.Ordinal))
            throw new InvalidDataException("Subtitle provider identity changed in transit.");
        return new(response.Body, response.ETag!);
    }

    public async Task<AdminSubtitleProviderSaved> SaveSubtitleProviderAsync(MonitorAuthorityLease lease,
        AdminSubtitleProviderEditor editor, AdminSubtitleProviderChange change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(change);
        RequireProvider(editor.Provider);
        AdminSettingsForm.RequireStrongETag(editor.ETag);
        ct.ThrowIfCancellationRequested();
        lease.ThrowIfRetired();
        editor.Consume(); // consumption precedes the network call: no caller can replay any outcome.
        using var timeout = Timeout(lease, ct, WriteDeadline);
        try
        {
            var saved = (await client.MonitorLogsSendAsync<AdminSubtitleProviderSaved>(lease, HttpMethod.Put,
                SubtitlePath(editor.Provider), SubtitleBody(change), editor.ETag, timeout.Token, MaxResponseBytes).ConfigureAwait(false)).Body;
            ValidateSaved(saved);
            return saved;
        }
        catch (LoggingSettingsConflictException) { throw new AdminProviderConflictException(); }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminProviderOutcomeUnknownException(); }
        catch (ApiException error) { throw AdminServerSettingsApi.Sanitize(error); }
        catch (AdminProviderOutcomeUnknownException) { throw; }
        catch (Exception) { throw new AdminProviderOutcomeUnknownException(); }
    }

    public async Task<AdminSubtitleProviderTestResult> TestSubtitleProviderAsync(MonitorAuthorityLease lease,
        string provider, AdminSubtitleProviderChange change, CancellationToken ct = default)
    {
        RequireProvider(provider);
        ArgumentNullException.ThrowIfNull(change);
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, WriteDeadline);
        try
        {
            var result = (await client.MonitorLogsSendAsync<AdminSubtitleProviderTestResult>(lease, HttpMethod.Post,
                SubtitlePath(provider) + "/test", SubtitleBody(change), ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body;
            if (!result.Success && result.Error is { Length: > 1000 }) result.Error = result.Error[..1000] + "...";
            result.Error = result.Error == null ? null : RedactCredentialEchoes(LocalLog.RedactDiagnosticText(result.Error),
                change.ApiKey, change.Username, change.Password);
            return result;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminProviderTestOutcomeUnknownException(); }
        catch (ApiException error) { throw AdminServerSettingsApi.Sanitize(error); }
        catch (AdminProviderTestOutcomeUnknownException) { throw; }
        catch (Exception) { throw new AdminProviderTestOutcomeUnknownException(); }
    }

    public async Task<IReadOnlyList<AdminMarkerProvider>> ListMarkerProvidersAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        using var timeout = Timeout(lease, ct, ReadDeadline);
        var response = await ReadAsync<AdminMarkerProvidersResponse>(lease, HttpMethod.Get,
            "/api/v2/admin/markers/providers", timeout.Token).ConfigureAwait(false);
        if (response.Providers == null || response.Providers.Count > MaxMarkerProviders)
            throw new AdminProviderLimitException(MaxMarkerProviders);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in response.Providers) ValidateMarker(provider, seen);
        return Array.AsReadOnly(response.Providers.ToArray());
    }

    public async Task<AdminMarkerProvider> UpdateMarkerProviderAsync(MonitorAuthorityLease lease, string provider,
        IReadOnlyDictionary<string, object?> patch, CancellationToken ct = default)
    {
        RequireProvider(provider);
        ArgumentNullException.ThrowIfNull(patch);
        var allowed = new HashSet<string>(["fetch_enabled", "fetch_priority", "fetch_min_confidence", "contribute_enabled", "contribute_auto_local", "contribute_min_confidence"], StringComparer.Ordinal);
        if (patch.Count == 0 || patch.Keys.Any(key => !allowed.Contains(key)))
            throw new ArgumentException("Invalid marker provider update.", nameof(patch));
        foreach (var (key, value) in patch)
        {
            var valid = key switch
            {
                "fetch_enabled" or "contribute_enabled" or "contribute_auto_local" => value is bool,
                "fetch_priority" => value is long,
                "fetch_min_confidence" or "contribute_min_confidence" => value is double number && double.IsFinite(number) && number is >= 0 and <= 1,
                _ => false,
            };
            if (!valid) throw new ArgumentException("Invalid marker provider update value.", nameof(patch));
        }
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, WriteDeadline);
        try
        {
            var result = (await client.MonitorLogsSendAsync<AdminMarkerProvider>(lease, HttpMethod.Put,
                MarkerPath(provider), patch, ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body;
            ValidateMarker(result, null);
            if (!string.Equals(result.Provider, provider, StringComparison.Ordinal)) throw new AdminProviderOutcomeUnknownException();
            return result;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminProviderOutcomeUnknownException(); }
        catch (ApiException error) { throw AdminServerSettingsApi.Sanitize(error); }
        catch (AdminProviderOutcomeUnknownException) { throw; }
        catch (Exception) { throw new AdminProviderOutcomeUnknownException(); }
    }

    public async Task<AdminMarkerProviderValidation> ValidateMarkerProviderAsync(MonitorAuthorityLease lease,
        string provider, CancellationToken ct = default)
    {
        RequireProvider(provider);
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, WriteDeadline);
        try
        {
            var result = (await client.MonitorLogsSendAsync<AdminMarkerProviderValidation>(lease, HttpMethod.Post,
                MarkerPath(provider) + "/validate", ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body;
            if (result.Error is { Length: > 1000 }) result.Error = result.Error[..1000] + "...";
            result.Error = result.Error == null ? null : LocalLog.RedactDiagnosticText(result.Error);
            if (result.Stats is { } stats && (stats.Total < 0 || stats.Accepted < 0 || stats.Pending < 0 || stats.Rejected < 0 ||
                !double.IsFinite(stats.AcceptanceRate) || stats.AcceptanceRate is < 0 or > 1 || stats.CurrentStreak < 0 || stats.BestStreak < 0))
                throw new InvalidDataException("Invalid marker provider statistics.");
            return result;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminProviderTestOutcomeUnknownException(); }
        catch (ApiException error) { throw AdminServerSettingsApi.Sanitize(error); }
        catch (AdminProviderTestOutcomeUnknownException) { throw; }
        catch (Exception) { throw new AdminProviderTestOutcomeUnknownException(); }
    }

    public async Task<IReadOnlyList<AdminPluginInstallation>> ListPluginInstallationsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var items = new List<AdminPluginInstallation>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var installationIds = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var pageNumber = 0; pageNumber < MaxPluginPages; pageNumber++)
        {
            using var timeout = Timeout(lease, ct, ReadDeadline);
            var path = "/api/v2/admin/plugins/installations?limit=100" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var page = await ReadAsync<AdminPluginInstallationsPage>(lease, HttpMethod.Get, path, timeout.Token).ConfigureAwait(false);
            if (page.Items == null || page.Items.Count > 100 || items.Count + page.Items.Count > MaxPluginInstallations)
                throw new AdminProviderLimitException(MaxPluginInstallations);
            foreach (var installation in page.Items)
            {
                ValidateInstallation(installation);
                if (!installationIds.Add(installation.Id)) throw new InvalidDataException("Duplicate plugin installation.");
            }
            items.AddRange(page.Items);
            if (page.Page?.HasMore != true) return Array.AsReadOnly(items.ToArray());
            cursor = page.Page.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 2048 || !cursors.Add(cursor))
                throw new InvalidDataException("Invalid plugin installation pagination.");
        }
        throw new AdminProviderLimitException(MaxPluginPages);
    }

    private async Task<T> ReadAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path, CancellationToken ct)
    {
        try
        {
            return (await client.MonitorLogsSendAsync<T>(lease, method, path, ct: ct,
                maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body;
        }
        catch (ApiException error) { throw AdminServerSettingsApi.Sanitize(error); }
    }

    private static string SubtitlePath(string provider) => "/api/v2/admin/subtitle-providers/" + Uri.EscapeDataString(provider);
    private static string MarkerPath(string provider) => "/api/v2/admin/markers/providers/" + Uri.EscapeDataString(provider);
    private static int SubtitleOrder(string provider) => provider switch { "opensubtitles" => 0, "subdl" => 1, "subsource" => 2, _ => 3 };

    private static IReadOnlyDictionary<string, object?> SubtitleBody(AdminSubtitleProviderChange change)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal) { ["enabled"] = change.Enabled };
        if (change.ApiKey != null) body["api_key"] = change.ApiKey;
        if (change.Username != null) body["username"] = change.Username;
        if (change.Password != null) body["password"] = change.Password;
        if (change.ClearCredentials == true) body["clear_credentials"] = true;
        return new ReadOnlyDictionary<string, object?>(body);
    }

    private static void RequireProvider(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider) || provider.Length > 256 || provider.Any(character => char.IsControl(character) || character is '/' or '\\'))
            throw new ArgumentException("Invalid provider identity.", nameof(provider));
    }

    private static void ValidateSubtitle(AdminSubtitleProvider provider, HashSet<string>? seen)
    {
        if (provider == null) throw new InvalidDataException("Invalid subtitle provider.");
        RequireProvider(provider.ProviderName);
        if (seen != null && !seen.Add(provider.ProviderName)) throw new InvalidDataException("Duplicate subtitle provider.");
    }

    private static void ValidateSaved(AdminSubtitleProviderSaved saved)
    {
        if (saved == null || !PositiveRevision().IsMatch(saved.SavedRevision) ||
            saved.LocalAppliedRevision is { } applied && !NonNegativeRevision().IsMatch(applied))
            throw new AdminProviderOutcomeUnknownException();
        var local = saved.LocalApply;
        if (local == AdminSubtitleLocalApply.Applied && saved.LocalAppliedRevision == null)
            throw new AdminProviderOutcomeUnknownException();
    }

    private static void ValidateMarker(AdminMarkerProvider provider, HashSet<string>? seen)
    {
        if (provider == null) throw new InvalidDataException("Invalid marker provider.");
        RequireProvider(provider.Provider);
        if (seen != null && !seen.Add(provider.Provider)) throw new InvalidDataException("Duplicate marker provider.");
        if (!double.IsFinite(provider.FetchMinConfidence) || provider.FetchMinConfidence is < 0 or > 1 ||
            !double.IsFinite(provider.ContributeMinConfidence) || provider.ContributeMinConfidence is < 0 or > 1)
            throw new InvalidDataException("Invalid marker provider confidence.");
    }

    private static void ValidateInstallation(AdminPluginInstallation installation)
    {
        if (installation == null || string.IsNullOrWhiteSpace(installation.Id) || installation.Id.Length > 128 ||
            string.IsNullOrWhiteSpace(installation.PluginId) || installation.PluginId.Length > 256 ||
            installation.Capabilities == null || installation.Capabilities.Count > 128 ||
            installation.GlobalConfigSchema == null || installation.GlobalConfigSchema.Count > 128 ||
            installation.GlobalConfigs == null || installation.GlobalConfigs.Count > 128)
            throw new InvalidDataException("Invalid plugin installation.");
        if (installation.Capabilities.Any(capability => capability == null || string.IsNullOrWhiteSpace(capability.Id) || capability.Id.Length > 256 ||
            string.IsNullOrWhiteSpace(capability.Type) || capability.Type.Length > 256 || capability.DisplayName == null || capability.DisplayName.Length > 500 ||
            capability.ConfigSchema == null || capability.ConfigSchema.Count > 128) ||
            installation.GlobalConfigSchema.Any(schema => schema == null || string.IsNullOrWhiteSpace(schema.Key) || schema.Key.Length > 256) ||
            installation.GlobalConfigs.Any(config => config == null || string.IsNullOrWhiteSpace(config.Key) || config.Key.Length > 256 ||
                config.Value == null || config.Value.Count > 128 || config.ConfiguredSecrets == null || config.ConfiguredSecrets.Count > 128))
            throw new InvalidDataException("Invalid plugin configuration inventory.");
    }

    private static string RedactCredentialEchoes(string message, params string?[] values)
    {
        foreach (var value in values.Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.Ordinal).OrderByDescending(value => value!.Length))
            message = message.Replace(value!, "<redacted>", StringComparison.Ordinal);
        return message;
    }

    private static CancellationTokenSource Timeout(MonitorAuthorityLease lease, CancellationToken ct, TimeSpan deadline)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(deadline);
        return linked;
    }
}
