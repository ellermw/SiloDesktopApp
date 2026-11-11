using System.Text.Json;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Api;

public sealed class ProfileTransferApi(SiloApiClient client)
{
    internal SiloApiClient Client => client;
    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);

    public Task<ProfileExportCapability> GetCapabilityAsync(ApiRequestContext context, CancellationToken ct = default)
        => client.SendRequestAsync<ProfileExportCapability>(context, HttpMethod.Get,
            "/api/v2/capabilities/profile-export", null, ct);

    public async Task<byte[]> ExportAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        var data = await client.SendRawBytesRequestAsync(context, HttpMethod.Get,
            "/api/v2/profile/export", ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context))
            throw new OperationCanceledException("The profile session changed.", ct);
        return data;
    }

    public async Task<ProfileImportResult> ImportAsync(ApiRequestContext context, byte[] document, int maxImportBytes, CancellationToken ct = default)
    {
        if (document.Length > maxImportBytes && maxImportBytes > 0)
            throw new ApiException("validation_failed", $"The file is too large ({FormatSize(document.Length)}). The limit is {FormatSize(maxImportBytes)}.", 422);
        ValidateImportDocument(document);
        var body = JsonSerializer.Deserialize<JsonElement>(document);
        var result = await client.SendRequestAsync<ProfileImportResult>(context, HttpMethod.Post,
            "/api/v2/profile/import", body, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context))
            throw new OperationCanceledException("The profile session changed.", ct);
        return result;
    }

    private static readonly HashSet<string> AccessFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "account", "accounts", "profiles", "profile", "username", "email",
        "password_hash", "pin_hash", "role", "permissions", "policy", "group",
        "effective_policy", "library_ids", "allowed_library_ids",
        "library_restrictions_enabled", "max_content_rating", "max_advisory_age",
        "require_advisory_age", "max_playback_quality", "is_child", "is_primary", "is_owner"
    };

    private static void ValidateImportDocument(byte[] document)
    {
        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(document); }
        catch (JsonException) { throw new ApiException("validation_failed", "Not a valid profile export: the file is not valid JSON.", 422); }
        if (root.ValueKind != JsonValueKind.Object)
            throw new ApiException("validation_failed", "Not a valid profile export.", 422);
        foreach (var prop in root.EnumerateObject())
        {
            if (AccessFields.Contains(prop.Name))
                throw new ApiException("validation_failed", "A profile import cannot carry account or access fields.", 422);
        }
        if (root.TryGetProperty("format", out var format) &&
            format.GetString() != "silo-profile-export")
            throw new ApiException("validation_failed", "Not a valid profile export: unrecognized format.", 422);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
