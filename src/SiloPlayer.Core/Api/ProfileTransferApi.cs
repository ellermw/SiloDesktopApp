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
        // A document near the limit takes long enough to scan that a UI-thread caller would stall.
        await Task.Run(() => ValidateImportDocument(document), ct).ConfigureAwait(false);
        var result = await client.SendRawJsonRequestAsync<ProfileImportResult>(context, HttpMethod.Post,
            "/api/v2/profile/import", document, ct).ConfigureAwait(false);
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
        // Only top-level names matter, so scan the tokens in place instead of materializing the document.
        bool isObject = false, hasAccessField = false, formatMismatch = false;
        try
        {
            var reader = new Utf8JsonReader(document);
            reader.Read();
            isObject = reader.TokenType == JsonTokenType.StartObject;
            while (reader.Read())
            {
                if (!isObject || reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName) continue;
                if (AccessFields.Contains(reader.GetString()!)) hasAccessField = true;
                if (!reader.ValueTextEquals("format")) continue;
                reader.Read();
                formatMismatch = reader.TokenType != JsonTokenType.String || !reader.ValueTextEquals("silo-profile-export");
            }
        }
        catch (JsonException) { throw new ApiException("validation_failed", "Not a valid profile export: the file is not valid JSON.", 422); }
        if (!isObject)
            throw new ApiException("validation_failed", "Not a valid profile export.", 422);
        if (hasAccessField)
            throw new ApiException("validation_failed", "A profile import cannot carry account or access fields.", 422);
        if (formatMismatch)
            throw new ApiException("validation_failed", "Not a valid profile export: unrecognized format.", 422);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
