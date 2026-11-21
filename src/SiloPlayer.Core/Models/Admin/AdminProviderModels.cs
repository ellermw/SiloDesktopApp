using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminSubtitleProvider
{
    public string ProviderName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool HasApiKey { get; set; }
    public bool HasCredentials { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class AdminSubtitleProvidersResponse
{
    public List<AdminSubtitleProvider> Providers { get; set; } = [];
}

/// <summary>Canonical configuration plus the strong revision that protects exactly one write.</summary>
public sealed class AdminSubtitleProviderEditor
{
    private int _consumed;
    internal AdminSubtitleProviderEditor(AdminSubtitleProvider provider, string etag, string accountId, long epoch)
    {
        AccountId = accountId; Epoch = epoch;
        Provider = provider.ProviderName; Enabled = provider.Enabled; HasApiKey = provider.HasApiKey;
        HasCredentials = provider.HasCredentials; UpdatedAt = provider.UpdatedAt; ETag = etag;
    }
    public string Provider { get; }
    public bool Enabled { get; }
    public bool HasApiKey { get; }
    public bool HasCredentials { get; }
    public DateTimeOffset? UpdatedAt { get; }
    internal string ETag { get; }
    /// <summary>The authority generation (server and account) that produced the strong ETag; a write must never cross it.</summary>
    internal string AccountId { get; }
    internal long Epoch { get; }
    internal void Consume()
    {
        if (Interlocked.Exchange(ref _consumed, 1) != 0)
            throw new InvalidOperationException("Reload saved provider configuration before saving again.");
    }
    public override string ToString() => nameof(AdminSubtitleProviderEditor);
}

public sealed class AdminSubtitleProviderChange
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool? ClearCredentials { get; set; }
    public override string ToString() => nameof(AdminSubtitleProviderChange);
}

public enum AdminSubtitleLocalApply { Applied, NotConfigured, Unsupported, Failed }

/// <summary>
/// Why a subtitle provider tile is in the official "save not confirmed" state. Every kind disables the tile's fields and Save
/// until that one provider has been read again; the kind only says what the server answered.
/// </summary>
public enum AdminSubtitleSaveFailure
{
    /// <summary>A definite refusal (4xx other than 408 and 412).</summary>
    Refused,
    /// <summary>412: the provider changed on the server after the editor was read.</summary>
    Conflict,
    /// <summary>408, 5xx, a lost answer, a timeout or an unusable acknowledgement after the request was sent.</summary>
    OutcomeUnknown,
}

public sealed class AdminSubtitleProviderSaved
{
    public string SavedRevision { get; set; } = "";
    [JsonPropertyName("local_apply")]
    public string LocalApplyWire { get; set; } = "";
    [JsonIgnore]
    public AdminSubtitleLocalApply LocalApply => LocalApplyWire switch
    {
        "applied" => AdminSubtitleLocalApply.Applied,
        "not_configured" => AdminSubtitleLocalApply.NotConfigured,
        "unsupported" => AdminSubtitleLocalApply.Unsupported,
        "failed" => AdminSubtitleLocalApply.Failed,
        _ => throw new InvalidDataException("Invalid subtitle provider save acknowledgement."),
    };
    public string? LocalAppliedRevision { get; set; }
    public override string ToString() => nameof(AdminSubtitleProviderSaved);
}

public sealed class AdminSubtitleProviderTestResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public override string ToString() => nameof(AdminSubtitleProviderTestResult);
}

/// <summary>Write-only subtitle credential draft. No secret is included in diagnostics.</summary>
public sealed class AdminSubtitleProviderDraft
{
    private bool _initiallyConfigured;
    private bool _enabledExplicit;
    public AdminSubtitleProviderDraft(string provider, bool enabled, bool hasApiKey, bool hasCredentials)
    {
        Provider = provider; Enabled = enabled; HasApiKey = hasApiKey; HasCredentials = hasCredentials;
        _initiallyConfigured = provider == "opensubtitles" ? hasCredentials : hasApiKey;
    }
    public string Provider { get; }
    public bool Enabled { get; private set; }
    public bool HasApiKey { get; private set; }
    public bool HasCredentials { get; private set; }
    public string ApiKey { get; private set; } = "";
    public string Username { get; private set; } = "";
    public string Password { get; private set; } = "";
    public bool SaveAttempted { get; private set; }
    public bool Dirty => _enabledExplicit || ApiKey.Length > 0 || Username.Length > 0 || Password.Length > 0;
    public void SetEnabled(bool value) { Enabled = value; _enabledExplicit = true; }
    public void SetApiKey(string value) { ApiKey = value; AutoEnable(value.Trim().Length > 0); }
    public void SetUsername(string value) { Username = value; AutoEnable(value.Trim().Length > 0 && Password.Length > 0); }
    public void SetPassword(string value) { Password = value; AutoEnable(Username.Trim().Length > 0 && value.Length > 0); }
    private void AutoEnable(bool credentialTyped) { if (!_initiallyConfigured && !_enabledExplicit && credentialTyped) Enabled = true; }
    public AdminSubtitleProviderChange CaptureChange(bool clear = false)
    {
        if (SaveAttempted) throw new InvalidOperationException("Reload saved provider configuration before saving again.");
        SaveAttempted = true;
        if (clear) return new() { Enabled = false, ClearCredentials = true };
        return StagedChange();
    }
    /// <summary>The staged values for a save or test. A credential left blank is omitted, so the saved one is kept and never replaced by an empty value.</summary>
    public AdminSubtitleProviderChange StagedChange() => Provider == "opensubtitles"
        ? new() { Enabled = Enabled, Username = Username.Trim().Length > 0 ? Username : null, Password = Password.Length > 0 ? Password : null }
        : new() { Enabled = Enabled, ApiKey = ApiKey.Trim().Length > 0 ? ApiKey : null };
    /// <summary>
    /// The tile's own Reload, as the official <c>loadEditor()</c> does it: the provider was read again, so every draft is discarded
    /// (typed credentials cleared, the enable switch follows the server again) and one new save attempt is permitted.
    /// </summary>
    public void ResetToSavedConfiguration(AdminSubtitleProviderEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (!string.Equals(editor.Provider, Provider, StringComparison.Ordinal))
            throw new ArgumentException("The editor belongs to another provider.", nameof(editor));
        ApiKey = Username = Password = "";
        _enabledExplicit = false;
        HasApiKey = editor.HasApiKey;
        HasCredentials = editor.HasCredentials;
        _initiallyConfigured = Provider == "opensubtitles" ? HasCredentials : HasApiKey;
        Enabled = editor.Enabled;
        SaveAttempted = false;
    }
    /// <summary>
    /// A successful save, as the official <c>resetDrafts()</c>: typed credentials are cleared and the enable switch no longer holds an
    /// unsaved choice. The tile stays off (<see cref="SaveAttempted"/> is kept) until the provider is read again, because the strong ETag
    /// it holds was spent by that one write. The switch shows the value that was saved.
    /// </summary>
    public void ResetAfterSave(AdminSubtitleProviderChange saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ApiKey = Username = Password = "";
        _enabledExplicit = false;
        Enabled = saved.Enabled;
    }
    public void ClearSecrets() { ApiKey = Username = Password = ""; }
    public override string ToString() => nameof(AdminSubtitleProviderDraft);
}

public sealed class AdminMarkerProvidersResponse
{
    public List<AdminMarkerProvider> Providers { get; set; } = [];
}

public sealed class AdminMarkerProvider
{
    public string Provider { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? SourceType { get; set; }
    public string? PluginId { get; set; }
    public string? PluginInstallationId { get; set; }
    public string? CapabilityId { get; set; }
    public bool IsSubmitter { get; set; }
    public bool FetchEnabled { get; set; }
    public long FetchPriority { get; set; }
    public double FetchMinConfidence { get; set; }
    public bool ContributeEnabled { get; set; }
    public bool ContributeAutoLocal { get; set; }
    public double ContributeMinConfidence { get; set; }
    public bool DefaultContributeEnabled { get; set; }
}

public sealed class AdminMarkerProviderValidation
{
    public bool Valid { get; set; }
    public AdminMarkerUserStats? Stats { get; set; }
    public string? Error { get; set; }
}

public sealed class AdminMarkerUserStats
{
    public long Total { get; set; }
    public long Accepted { get; set; }
    public long Pending { get; set; }
    public long Rejected { get; set; }
    public double AcceptanceRate { get; set; }
    public long CurrentStreak { get; set; }
    public long BestStreak { get; set; }
}

/// <summary>Changed-fields-only marker draft; confidence is edited as 0-100 and sent as 0-1.</summary>
public sealed partial class AdminMarkerProviderDraft
{
    private readonly AdminMarkerProvider _saved;
    public AdminMarkerProviderDraft(AdminMarkerProvider saved)
    {
        _saved = saved; FetchEnabled = saved.FetchEnabled; FetchPriority = saved.FetchPriority.ToString(CultureInfo.InvariantCulture);
        // Raw saved values: the submitter gate is display-only, so an untouched tile never writes a contribute field.
        FetchMinimumPercent = Percent(saved.FetchMinConfidence); ContributeEnabled = saved.ContributeEnabled;
        ContributeAutoLocal = saved.ContributeAutoLocal;
        ContributeMinimumPercent = Percent(saved.ContributeMinConfidence);
    }
    public bool FetchEnabled { get; set; }
    public string FetchPriority { get; set; }
    public string FetchMinimumPercent { get; set; }
    public bool ContributeEnabled { get; set; }
    public bool ContributeAutoLocal { get; set; }
    public string ContributeMinimumPercent { get; set; }
    public bool IsValid => TryPriority(out _) && TryPercent(FetchMinimumPercent, out _) && TryPercent(ContributeMinimumPercent, out _);
    public bool AdvancedDirty => !TryPriority(out var priority) || priority != _saved.FetchPriority ||
        !TryConfidence(FetchMinimumPercent, _saved.FetchMinConfidence, out var fetchConfidence) || fetchConfidence != _saved.FetchMinConfidence ||
        !TryConfidence(ContributeMinimumPercent, _saved.ContributeMinConfidence, out var contributeConfidence) || contributeConfidence != _saved.ContributeMinConfidence;
    public bool Dirty => IsValid ? CapturePatch().Count > 0 : true;
    /// <summary>The server value this draft is edited against.</summary>
    public AdminMarkerProvider Saved => _saved;
    /// <summary>True between <see cref="BeginSave"/> and the acknowledgement or <see cref="AbandonSave"/>.</summary>
    public bool SaveInFlight => _submitted != null;

    private readonly record struct Fields(bool FetchEnabled, string FetchPriority, string FetchMinimumPercent,
        bool ContributeEnabled, bool ContributeAutoLocal, string ContributeMinimumPercent);
    private Fields? _submitted;
    private Fields Current() => new(FetchEnabled, FetchPriority, FetchMinimumPercent, ContributeEnabled, ContributeAutoLocal, ContributeMinimumPercent);

    /// <summary>
    /// Captures the changed-field patch for exactly one send and remembers what was submitted, so edits typed
    /// while that request is pending can be told apart from the values it carried.
    /// </summary>
    public IReadOnlyDictionary<string, object?> BeginSave()
    {
        if (_submitted != null) throw new InvalidOperationException("A marker provider save is already in flight.");
        var patch = CapturePatch();
        _submitted = Current();
        return patch;
    }

    /// <summary>A failed or retired send leaves the draft exactly as the admin has it.</summary>
    public void AbandonSave() => _submitted = null;

    /// <summary>
    /// Reconciles a canonical value while retaining only fields the admin changed. While a save is in flight a
    /// field counts as changed when it differs from what was submitted, so an edit typed during the request
    /// survives the acknowledgement even when it returns to the old saved value.
    /// </summary>
    public AdminMarkerProviderDraft Rebase(AdminMarkerProvider saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (!string.Equals(saved.Provider, _saved.Provider, StringComparison.Ordinal))
            throw new ArgumentException("The provider identity changed.", nameof(saved));
        if (ReferenceEquals(saved, _saved)) return this;
        var next = new AdminMarkerProviderDraft(saved);
        if (_submitted is { } sent)
        {
            if (FetchEnabled != sent.FetchEnabled) next.FetchEnabled = FetchEnabled;
            if (FetchPriority != sent.FetchPriority) next.FetchPriority = FetchPriority;
            if (FetchMinimumPercent != sent.FetchMinimumPercent) next.FetchMinimumPercent = FetchMinimumPercent;
            if (ContributeEnabled != sent.ContributeEnabled) next.ContributeEnabled = ContributeEnabled;
            if (ContributeAutoLocal != sent.ContributeAutoLocal) next.ContributeAutoLocal = ContributeAutoLocal;
            if (ContributeMinimumPercent != sent.ContributeMinimumPercent) next.ContributeMinimumPercent = ContributeMinimumPercent;
            return next;
        }
        if (FetchEnabled != _saved.FetchEnabled) next.FetchEnabled = FetchEnabled;
        if (!TryPriority(out var priority) || priority != _saved.FetchPriority) next.FetchPriority = FetchPriority;
        if (!TryConfidence(FetchMinimumPercent, _saved.FetchMinConfidence, out var fetchConfidence) || fetchConfidence != _saved.FetchMinConfidence) next.FetchMinimumPercent = FetchMinimumPercent;
        if (ContributeEnabled != _saved.ContributeEnabled) next.ContributeEnabled = ContributeEnabled;
        if (ContributeAutoLocal != _saved.ContributeAutoLocal) next.ContributeAutoLocal = ContributeAutoLocal;
        if (!TryConfidence(ContributeMinimumPercent, _saved.ContributeMinConfidence, out var contributeConfidence) || contributeConfidence != _saved.ContributeMinConfidence) next.ContributeMinimumPercent = ContributeMinimumPercent;
        return next;
    }
    public IReadOnlyDictionary<string, object?> CapturePatch()
    {
        if (!TryPriority(out var priority) || !TryConfidence(FetchMinimumPercent, _saved.FetchMinConfidence, out var fetchConfidence) ||
            !TryConfidence(ContributeMinimumPercent, _saved.ContributeMinConfidence, out var contributeConfidence))
            throw new InvalidOperationException("Provider priority and confidence values must be valid.");
        var patch = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (FetchEnabled != _saved.FetchEnabled) patch["fetch_enabled"] = FetchEnabled;
        if (priority != _saved.FetchPriority) patch["fetch_priority"] = priority;
        if (fetchConfidence != _saved.FetchMinConfidence) patch["fetch_min_confidence"] = fetchConfidence;
        // Raw comparisons: the page's sharing toggle switches automatic sharing off itself, so a stored
        // automatic value under sharing that is off is not an edit.
        if (ContributeEnabled != _saved.ContributeEnabled) patch["contribute_enabled"] = ContributeEnabled;
        if (ContributeAutoLocal != _saved.ContributeAutoLocal) patch["contribute_auto_local"] = ContributeAutoLocal;
        if (_saved.IsSubmitter && contributeConfidence != _saved.ContributeMinConfidence)
            patch["contribute_min_confidence"] = contributeConfidence;
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(patch);
    }
    private bool TryPriority(out long value)
    {
        value = 0;
        var input = FetchPriority.Trim();
        return IntegerPattern().IsMatch(input) &&
            long.TryParse(input, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
    /// <summary>
    /// An unchanged percentage keeps the saved 0-1 value exact; multiplying and dividing can otherwise introduce floating-point drift.
    /// The comparison uses the rounded percentage the field shows, so the displayed text always maps back to the saved value.
    /// </summary>
    private static bool TryConfidence(string input, double saved, out double value)
    {
        if (!TryPercent(input, out value)) return false;
        if (double.TryParse(Percent(saved), NumberStyles.Float, CultureInfo.InvariantCulture, out var savedPercent) && double.Parse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture) == savedPercent) value = saved;
        return true;
    }
    private static bool TryPercent(string input, out double value)
    {
        var parsed = double.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent);
        value = percent / 100d;
        return parsed && double.IsFinite(percent) && percent is >= 0 and <= 100;
    }
    /// <summary>The percentage shown in the field, rounded so binary noise such as 55.000000000000007 never reaches the admin.</summary>
    private static string Percent(double value) => Math.Round(value * 100d, 6).ToString("0.######", CultureInfo.InvariantCulture);
    [GeneratedRegex("^[+-]?[0-9]+$", RegexOptions.CultureInvariant)] private static partial Regex IntegerPattern();
}

public sealed class AdminPluginInstallationsPage
{
    public List<AdminPluginInstallation> Items { get; set; } = [];
    public AdminProviderPageInfo? Page { get; set; }
}
public sealed class AdminProviderPageInfo { public bool HasMore { get; set; } public string? NextCursor { get; set; } }
public sealed class AdminPluginInstallation
{
    [JsonConverter(typeof(SiloPlayer.Core.Json.StringOrNumberJsonConverter))]
    public string Id { get; set; } = "";
    public string PluginId { get; set; } = "";
    public bool Enabled { get; set; }
    public List<AdminPluginCapability> Capabilities { get; set; } = [];
    public List<AdminPluginConfigSchema> GlobalConfigSchema { get; set; } = [];
    public List<AdminPluginConfigValue> GlobalConfigs { get; set; } = [];
}
public sealed class AdminPluginCapability
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public List<AdminPluginConfigSchema> ConfigSchema { get; set; } = [];
}
public sealed class AdminPluginConfigSchema
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string JsonSchema { get; set; } = "";
    public bool Required { get; set; }
}
public sealed class AdminPluginConfigValue
{
    public string Key { get; set; } = "";
    public Dictionary<string, JsonElement> Value { get; set; } = [];
    public List<string> ConfiguredSecrets { get; set; } = [];
}

public enum AdminPluginProviderKind { Watch, Metadata, Marker }
public sealed record AdminPluginProviderTile(string InstallationId, string PluginId, string CapabilityId,
    string DisplayName, AdminPluginProviderKind Kind, bool Enabled, bool ConfigurationReady);

public static class AdminProviderPresentation
{
    /// <summary>
    /// Third-party text (a provider name) for a confirmation or dialog title: control characters and line or paragraph separators
    /// become a space, direction controls are dropped (joiners that join characters stay), runs of spaces collapse and the length is
    /// bounded without cutting a surrogate pair.
    /// </summary>
    public static string DialogText(string? value, int maxLength = 64)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var builder = new System.Text.StringBuilder(Math.Min(value.Length, maxLength + 1));
        var pendingSpace = false;
        foreach (var rune in value.EnumerateRunes())
        {
            var code = rune.Value;
            if (IsDirectionControl(code)) continue;
            if (code is 0x2028 or 0x2029 || System.Text.Rune.IsControl(rune) || System.Text.Rune.IsWhiteSpace(rune)) { pendingSpace = builder.Length > 0; continue; }
            if (pendingSpace) { builder.Append(' '); pendingSpace = false; }
            builder.Append(rune.ToString());
        }
        var text = builder.ToString();
        if (text.Length <= maxLength) return text;
        var cut = maxLength;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut].TrimEnd() + "…";
    }

    private static bool IsDirectionControl(int code) =>
        code is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);

    public static IReadOnlyList<AdminPluginProviderTile> PluginTiles(IEnumerable<AdminPluginInstallation> installations)
    {
        var tiles = new List<AdminPluginProviderTile>();
        foreach (var installation in installations)
        foreach (var capability in installation.Capabilities)
        {
            var family = capability.Type.Split('.', 2)[0];
            var kind = family switch
            {
                "watch_sync_provider" => AdminPluginProviderKind.Watch,
                "metadata_provider" => AdminPluginProviderKind.Metadata,
                "marker_provider" => AdminPluginProviderKind.Marker,
                _ => (AdminPluginProviderKind?)null,
            };
            if (kind is { } providerKind)
                tiles.Add(new(installation.Id, installation.PluginId, capability.Id,
                    string.IsNullOrWhiteSpace(capability.DisplayName) ? installation.PluginId : capability.DisplayName,
                    providerKind, installation.Enabled, ConfigurationReady(installation)));
        }
        return Array.AsReadOnly(tiles.OrderBy(tile => tile.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray());
    }

    public static bool ConfigurationReady(AdminPluginInstallation installation)
    {
        if (installation.GlobalConfigSchema.Count == 0) return true;
        bool Filled(string key)
        {
            var value = installation.GlobalConfigs.FirstOrDefault(item => item.Key == key);
            if (value == null) return false;
            if (value.ConfiguredSecrets.Count > 0) return true;
            return value.Value.Values.Any(item => item.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => false,
                JsonValueKind.String => !string.IsNullOrWhiteSpace(item.GetString()),
                _ => true,
            });
        }
        if (!installation.GlobalConfigSchema.Any(schema => Filled(schema.Key))) return false;
        return installation.GlobalConfigSchema.Where(schema => schema.Required).All(schema => Filled(schema.Key));
    }
}
