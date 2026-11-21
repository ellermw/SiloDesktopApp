using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public sealed partial class AdminUserResourcesApi
{
    private const string Devices = "/api/v2/admin/devices";
    public Task<JsonElement> DeviceCapabilitiesAsync(MonitorAuthorityLease owner, CancellationToken ct = default) => ReadAsync(owner, Devices + "/capabilities", ct);
    /// <summary>The whole fleet, a bounded number of pages, as the official list reads it; a larger fleet is refused with the official message.</summary>
    public Task<IReadOnlyList<JsonElement>> ListAllDevicesAsync(MonitorAuthorityLease owner, CancellationToken ct = default) =>
        ConfigurationAsync(owner, cursor => ListDevicesAsync(owner, cursor, ct), maxPages: 100, maxRows: 10_000, tooManyMessage: Services.Admin.AdminUserResourceText.T("web.admin.errors.devicesTooMany"));
    public Task<AdminResourcePage> ListDevicesAsync(MonitorAuthorityLease owner, string? cursor = null, CancellationToken ct = default) =>
        PageAsync(owner, Devices, cursor, row => NumericId(row.Text("user_id")) + "/" + row.Text("device_id"), ct);
    public async Task<JsonElement> GetDeviceAsync(MonitorAuthorityLease owner, string userId, string deviceId, CancellationToken ct = default)
    {
        var body = await ReadAsync(owner, DevicePath(userId, deviceId), ct).ConfigureAwait(false);
        if (body.Text("user_id") != userId || body.Text("device_id") != deviceId || body.Member("profiles")?.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Device identity changed.");
        return body;
    }
    public Task ResetLearnedLimitAsync(MonitorAuthorityLease owner, string userId, string deviceId, string connectionId, CancellationToken ct = default) =>
        SendAsync(owner, HttpMethod.Delete, DevicePath(userId, deviceId) + "/connections/" + Segment(connectionId) + "/learned-limit", ct: ct);
    public Task<AdminResourcePage> ListAccountValuesAsync(MonitorAuthorityLease owner, string userId, string? cursor = null, CancellationToken ct = default) =>
        PageAsync(owner, Users + "/" + NumericId(userId) + "/settings/values", cursor, SettingIdentity, ct);
    public async Task<AdminDeviceValues> GetDeviceValuesAsync(MonitorAuthorityLease owner, string userId)
    {
        int? revision = null;
        var items = await ConfigurationAsync(owner, async cursor =>
        {
            var page = await ListAccountValuesAsync(owner, userId, cursor).ConfigureAwait(false);
            if (page.Revision == null || revision != null && page.Revision != revision) throw new InvalidDataException("Setting contract revision changed during paging.");
            revision = page.Revision; return page;
        }).ConfigureAwait(false);
        return new(items, revision!.Value);
    }
    public async Task SetDeviceSettingAsync(MonitorAuthorityLease owner, string userId, string profileId, string deviceId, string key, JsonElement value, CancellationToken ct = default)
    {
        var path = SettingPath(userId, profileId, deviceId, key);
        if (value.ValueKind == JsonValueKind.Undefined || value.GetRawText().Length > 64 * 1024) throw new ArgumentException("Invalid setting value.");
        var response = await SendAsync(owner, HttpMethod.Put, path, new { value }, ct: ct).ConfigureAwait(false);
        if (response.Body.Text("key") != key || response.Body.Text("scope") != "profile_device" || response.Body.Text("profile_id") != profileId || response.Body.Text("device_id") != deviceId)
            throw new AdminResourceOutcomeUnknownException();
    }
    public Task DeleteDeviceSettingAsync(MonitorAuthorityLease owner, string userId, string profileId, string deviceId, string key, CancellationToken ct = default) =>
        SendAsync(owner, HttpMethod.Delete, SettingPath(userId, profileId, deviceId, key), ct: ct);
    /// <summary>No bulk endpoint exists. Snapshot keys are sent once; stop on first uncertain failure. A 404 already satisfies reset.</summary>
    public async Task<AdminDeviceResetResult> ResetDeviceProfileAsync(MonitorAuthorityLease owner, string userId, string profileId, string deviceId, IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        if (keys.Count > 200 || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count) throw new ArgumentException("Invalid override set.");
        var completed = 0;
        foreach (var key in keys)
        {
            owner.ThrowIfRetired();
            try { await DeleteDeviceSettingAsync(owner, userId, profileId, deviceId, key, ct).ConfigureAwait(false); completed++; }
            catch (ApiException error) when (error.StatusCode == 404) { completed++; }
            catch (AdminResourceOutcomeUnknownException) { return new(completed, true); }
            catch (ApiException error) { return new(completed, false, error.StatusCode); }
        }
        return new(completed, false);
    }
    private static string DevicePath(string userId, string deviceId) => Devices + "/" + NumericId(userId) + "/" + Segment(deviceId);
    private static string SettingPath(string userId, string profileId, string deviceId, string key) => Users + "/" + NumericId(userId) + "/settings/values/" + Segment(key, 128) +
        "?scope=profile_device&profile_id=" + Segment(profileId) + "&device_id=" + Segment(deviceId, 128);
    private static string SettingIdentity(JsonElement row)
    {
        if (row.Text("key").Length == 0 || row.Text("scope").Length == 0) throw new InvalidDataException("Invalid setting identity.");
        return JsonSerializer.Serialize(new[] { row.Text("key"), row.Text("scope"), row.Text("profile_id"), row.Text("client_family"), row.Text("device_id"), row.Text("library_id"), row.Text("series_id") });
    }
}
