using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public sealed record AdminAiCapabilities(string Model, IReadOnlyList<string> ReasoningLevels, IReadOnlyList<string> ServiceTiers, bool ChatGptSignIn);
public sealed record AdminChatGptAccount(string ClientId, string Email, bool Connected, bool NeedsLogin, bool PlanIneligible, bool UsageLimited)
{
    public bool Authenticated => !NeedsLogin && !PlanIneligible && (Connected || UsageLimited);
    public bool Selectable => !PlanIneligible && !NeedsLogin && (Connected || UsageLimited);
    public bool Ready => Authenticated && !UsageLimited;
    public override string ToString() => nameof(AdminChatGptAccount);
}
public sealed record AdminChatGptStatus(string ActiveClientId, IReadOnlyList<AdminChatGptAccount> Accounts, bool LoginPending, string LoginResult, string AttemptId, bool FirstConnection)
{
    public AdminChatGptAccount? Active => Accounts.FirstOrDefault(account => account.ClientId == ActiveClientId);
    public override string ToString() => nameof(AdminChatGptStatus);
}
public sealed record AdminChatGptLogin(string AttemptId, DateTimeOffset ExpiresAt, Uri AuthorizationUrl, string CallbackUri)
{
    public override string ToString() => nameof(AdminChatGptLogin);
}
public sealed record AdminChatGptModel(string Id, string Name);
public sealed record AdminRecommendationRun(string Status, string? StartedAt, string? CompletedAt, string Error, IReadOnlyDictionary<string, long> Counts);
public sealed record AdminRecommendationReset(long Embeddings, long TasteProfiles, long TasteClusters, long CachedRows);
public sealed record AdminJellyfinPrerequisite(string Name, bool Available, string Detail);

public sealed partial class AdminIntegrationsApi
{
    public async Task<AdminAiCapabilities> GetCapabilitiesAsync(MonitorAuthorityLease lease, string model, CancellationToken ct = default)
    {
        if (model.Length > 256) throw new ArgumentException("Invalid model.", nameof(model));
        var body = await ReadAsync<CapabilitiesWire>(lease, HttpMethod.Get, "/api/v2/admin/ai/capabilities?model=" + Uri.EscapeDataString(model), null, ReadDeadline, ct).ConfigureAwait(false);
        return new(model, Strings(body.ReasoningLevels, 16, 32), Strings(body.OpenaiServiceTiers ?? [], 16, 32), body.ChatgptSignIn == true);
    }

    public async Task<AdminChatGptStatus> GetChatGptStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        ChatStatus(await ReadAsync<ChatStatusWire>(lease, HttpMethod.Get, "/api/v2/admin/ai/chatgpt", null, ReadDeadline, ct).ConfigureAwait(false));

    public async Task<AdminChatGptLogin> StartChatGptLoginAsync(MonitorAuthorityLease lease, string? clientId = null, CancellationToken ct = default)
    {
        if (clientId != null) RequireAccount(clientId);
        var body = await CommandAsync<ChatLoginWire>(lease, HttpMethod.Post, "/api/v2/admin/ai/chatgpt/login", new { client_id = clientId ?? "" }, ct).ConfigureAwait(false);
        if (body.AttemptId is not { Length: > 0 and <= 256 } || !DateTimeOffset.TryParse(body.ExpiresAt, out var expiry) ||
            !Uri.TryCreate(body.AuthorizationUrl, UriKind.Absolute, out var url) || !AdminIntegrationPolicy.IsAuthorizationUrl(url) ||
            !AdminIntegrationPolicy.IsCallbackEndpoint(body.CallbackUri)) throw new AdminIntegrationsOutcomeUnknownException();
        return new(body.AttemptId, expiry, url, body.CallbackUri!);
    }

    public async Task CompleteChatGptLoginAsync(MonitorAuthorityLease lease, string callbackUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callbackUrl) || callbackUrl.Length > 32768) throw new ArgumentException("Invalid callback URL.", nameof(callbackUrl));
        using var timeout = Timeout(lease, ct, CommandDeadline);
        try { await client.AdminIntegrationsCompleteAsync(lease, "/api/v2/admin/ai/chatgpt/login/complete", new { callback_url = callbackUrl }, MaxBytes, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminIntegrationsOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminIntegrationsOutcomeUnknownException(); }
    }

    public async Task<AdminChatGptStatus> SelectChatGptAccountAsync(MonitorAuthorityLease lease, string clientId, CancellationToken ct = default)
    {
        var result = await CommandAsync<ChatStatusWire>(lease, HttpMethod.Post, AccountPath(clientId) + "/select", null, ct).ConfigureAwait(false);
        try { return ChatStatus(result); } catch (InvalidDataException) { throw new AdminIntegrationsOutcomeUnknownException(); }
    }

    public async Task<bool> DisconnectChatGptAccountAsync(MonitorAuthorityLease lease, string clientId, CancellationToken ct = default)
    {
        var result = await CommandAsync<DisconnectWire>(lease, HttpMethod.Delete, AccountPath(clientId), null, ct).ConfigureAwait(false);
        return result.RevocationConfirmed ?? throw new AdminIntegrationsOutcomeUnknownException();
    }

    public async Task<IReadOnlyList<AdminChatGptModel>> GetChatGptModelsAsync(MonitorAuthorityLease lease, string clientId, CancellationToken ct = default)
    {
        RequireAccount(clientId);
        var body = await ReadAsync<ChatModelsWire>(lease, HttpMethod.Get, "/api/v2/admin/ai/chatgpt/models?client_id=" + Uri.EscapeDataString(clientId), null, ReadDeadline, ct).ConfigureAwait(false);
        if (body.Models == null || body.Models.Length > 256 || body.Models.Any(model => model.Id is not { Length: > 0 and <= 256 })) throw new InvalidDataException("Invalid account model list.");
        return body.Models.Select(model => new AdminChatGptModel(model.Id!, Safe(model.Name, 256))).ToArray();
    }

    public async Task<AdminSettingsConnectionResult> CheckModelAsync(MonitorAuthorityLease lease, string kind,
        IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> dirtyKeys, CancellationToken ct = default)
    {
        var allowed = kind switch
        {
            "ai_chat" => AdminIntegrationsSettings.TextKeys,
            "ai_transcription" => AdminIntegrationsSettings.SpeechKeys,
            "recommendations_embedding" => AdminIntegrationsSettings.EmbeddingKeys,
            _ => throw new ArgumentException("Invalid connection check.", nameof(kind))
        };
        if (values.Keys.Any(key => !allowed.Contains(key)) || dirtyKeys.Any(key => !allowed.Contains(key))) throw new ArgumentException("Invalid check fields.", nameof(values));
        var body = await CommandAsync<ModelCheckWire>(lease, HttpMethod.Post, "/api/v2/admin/settings/check/" + kind,
            new { values, dirty_keys = dirtyKeys }, ct).ConfigureAwait(false);
        return new(body.Success ?? throw new InvalidDataException("Invalid check result."), Safe(body.Message));
    }

    public async Task PatchCompatibilityAsync(MonitorAuthorityLease lease, IReadOnlyDictionary<string, string> values, string etag, CancellationToken ct = default)
    {
        AdminSettingsForm.RequireStrongETag(etag);
        if (values.Count == 0 || values.Keys.Any(key => !AdminIntegrationsSettings.CompatibilityPatchKeys.Contains(key))) throw new ArgumentException("Invalid compatibility fields.", nameof(values));
        var body = new Dictionary<string, object>();
        foreach (var (key, value) in values)
        {
            if (key is "jellyfin_compat.enabled" or "jellyfin_compat.web_enabled")
            {
                if (!bool.TryParse(value, out var flag)) throw new ArgumentException("Invalid compatibility flag.", nameof(values));
                body[key["jellyfin_compat.".Length..]] = flag;
            }
            else body[key["jellyfin_compat.".Length..]] = value;
        }
        using var timeout = Timeout(lease, ct, CommandDeadline);
        try { await client.AdminIntegrationsSendAsync<JsonElement>(lease, HttpMethod.Patch, "/api/v2/admin/jellyfin-compat/settings", body, MaxBytes, timeout.Token, etag).ConfigureAwait(false); }
        catch (AdminSettingsConflictException) { throw; }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminIntegrationsOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminIntegrationsOutcomeUnknownException(); }
    }

    private static AdminChatGptStatus ChatStatus(ChatStatusWire body)
    {
        if (body.Accounts == null || body.Accounts.Length > 128 || body.Accounts.Any(account => account.ClientId is not { Length: > 0 and <= 256 })) throw new InvalidDataException("Invalid connection status.");
        return new(Bound(body.ActiveClientId, 256) ?? "", body.Accounts.Select(account => new AdminChatGptAccount(account.ClientId!, Safe(account.Email, 320), account.Connected, account.NeedsLogin, account.PlanIneligible, account.UsageLimited)).ToArray(), body.LoginPending, Bound(body.LoginResult, 32) ?? "idle", Bound(body.AttemptId, 256) ?? "", body.LoginFirstConnection);
    }
    private static void RequireAccount(string id) { if (string.IsNullOrWhiteSpace(id) || id.Length > 256) throw new ArgumentException("Invalid account.", nameof(id)); }
    private static string AccountPath(string id) { RequireAccount(id); return "/api/v2/admin/ai/chatgpt/accounts/" + Uri.EscapeDataString(id); }
    private static string Safe(string? value, int max = 500) => LocalLog.RedactDiagnosticText(Bound(value, max) ?? "");
    private static string PublicEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")) return "Configured address (not a valid HTTP URL)";
        return new UriBuilder(url) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri;
    }
    private static AdminRecommendationRun? Run(RunWire? run)
    {
        if (run == null) return null;
        var counts = (run.Result ?? []).Where(entry => entry.Key.Length <= 80 && entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt64(out _)).Take(32).ToDictionary(entry => entry.Key, entry => entry.Value.GetInt64());
        return new(Bound(run.Status, 64) ?? "", Bound(run.StartedAt, 64), Bound(run.CompletedAt, 64), Safe(run.Error), counts);
    }
    private static AdminJellyfinWebStatus WebReceipt(JellyfinWebWire body, string kind)
    {
        if (body.Operation?.Id is not { Length: > 0 and <= 256 } || body.Operation.Kind != kind || body.Operation.State is not ("running" or "succeeded" or "failed")) throw new AdminIntegrationsOutcomeUnknownException();
        return WebStatus(body);
    }
    private static AdminJellyfinWebStatus WebStatus(JellyfinWebWire body) => new(!string.IsNullOrWhiteSpace(body.InstalledVersion), body.WebEnabled == true,
        Bound(body.InstalledVersion, 128), Bound(body.InstallRoot, 1024), Bound(body.InstallPath, 1024),
        body.Operation?.State == "running" || body.WebState is "installing" or "removing", Bound(body.Operation?.Kind, 64),
        body.Operation?.ProgressPercent is { } progress ? Math.Clamp(progress, 0, 100) : null, Safe(body.LastError ?? body.Operation?.Error))
    {
        ApiEnabled = body.Enabled, ApiState = Safe(body.ApiState, 32), WebState = Safe(body.WebState, 32), Listen = Safe(body.Listen, 256),
        PublicUrl = PublicEndpoint(body.PublicUrl), PinnedVersion = Safe(body.PinnedVersion, 128), InstallerReady = body.InstallerReady,
        RestartRequired = body.RestartRequired, Phase = Safe(body.Operation?.Phase, 64), OperationMessage = Safe(body.Operation?.Message),
        OperationId = Bound(body.Operation?.Id, 256) ?? "", OperationState = Bound(body.Operation?.State, 32) ?? "",
        OperationStartedAt = Bound(body.Operation?.StartedAt, 64), OperationCompletedAt = Bound(body.Operation?.CompletedAt, 64),
        Prerequisites = (body.Prerequisites ?? []).Take(32).Select(item => new AdminJellyfinPrerequisite(Safe(item.Name, 80), item.Available, Safe(item.Message ?? item.Command))).ToArray(),
        Details = new Dictionary<string, string> { ["Reported version"] = Safe(body.EmulatedServerVersion, 128), ["Current operation"] = Safe(body.Operation?.Kind, 64) + " " + Safe(body.Operation?.State, 32), ["Downloaded from"] = PublicEndpoint(body.SourceUrl), ["Tag"] = Safe(body.Tag, 128), ["Commit"] = Safe(body.CommitSha, 128), ["Checksum"] = Safe(body.Checksum, 256), ["Installed at"] = Safe(body.InstalledAt, 80), ["License"] = body.LicensePresent ? "Present" : "Missing", ["Provenance"] = body.ProvenancePresent ? "Present" : "Missing" }
    };

    private sealed record CapabilitiesWire { public string[]? ReasoningLevels { get; init; } public string[]? OpenaiServiceTiers { get; init; } public bool? ChatgptSignIn { get; init; } }
    private sealed record ChatAccountWire { public string? ClientId { get; init; } public string? Email { get; init; } public bool Connected { get; init; } public bool NeedsLogin { get; init; } public bool PlanIneligible { get; init; } public bool UsageLimited { get; init; } }
    private sealed record ChatStatusWire { public string? ActiveClientId { get; init; } public ChatAccountWire[]? Accounts { get; init; } public bool LoginPending { get; init; } public string? LoginResult { get; init; } public string? AttemptId { get; init; } public bool LoginFirstConnection { get; init; } }
    private sealed record ChatLoginWire { public string? AttemptId { get; init; } public string? ExpiresAt { get; init; } public string? AuthorizationUrl { get; init; } public string? CallbackUri { get; init; } }
    private sealed record ChatModelsWire { public ChatModelWire[]? Models { get; init; } }
    private sealed record ChatModelWire { public string? Id { get; init; } public string? Name { get; init; } }
    private sealed record DisconnectWire { public bool? RevocationConfirmed { get; init; } }
    private sealed record ModelCheckWire { public bool? Success { get; init; } public string? Message { get; init; } }
    private sealed record PrerequisiteWire { public string? Name { get; init; } public bool Available { get; init; } public string? Message { get; init; } public string? Command { get; init; } }
    private sealed record JellyfinWebWire
    {
        public bool Enabled { get; init; } public string? ApiState { get; init; } public string? Listen { get; init; } public string? PublicUrl { get; init; }
        public bool? WebEnabled { get; init; } public string? WebState { get; init; } public string? PinnedVersion { get; init; } public string? InstalledVersion { get; init; }
        public string? InstallRoot { get; init; } public string? InstallPath { get; init; } public string? LastError { get; init; } public OperationWire? Operation { get; init; }
        public bool InstallerReady { get; init; } public bool RestartRequired { get; init; } public PrerequisiteWire[]? Prerequisites { get; init; }
        public string? Tag { get; init; } public string? CommitSha { get; init; } public string? Checksum { get; init; } public string? InstalledAt { get; init; }
        public string? SourceUrl { get; init; } public string? EmulatedServerVersion { get; init; }
        public bool LicensePresent { get; init; } public bool ProvenancePresent { get; init; }
    }
}
