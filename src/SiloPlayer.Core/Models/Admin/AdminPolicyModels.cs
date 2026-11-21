using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminPolicyCapability
{
    public bool Enabled { get; set; }
    public bool EditorAvailable { get; set; }
    public List<string> DecisionTypes { get; set; } = [];
    public long Generation { get; set; }
    public bool Degraded { get; set; }
    public string? DegradedReason { get; set; }
    public List<string> DegradedDomains { get; set; } = [];
    public long EvalTimeouts { get; set; }
}
public sealed class AdminPolicyDocument
{
    public string Id { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonRequired] public bool Enabled { get; set; }
    [JsonRequired] public string? ActiveVersionId { get; set; }
    public AdminPolicyVersion? ActiveVersion { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public override string ToString() => nameof(AdminPolicyDocument);
}
public sealed record AdminPolicySnapshot(AdminPolicyDocument Value, string ETag)
{
    public override string ToString() => nameof(AdminPolicySnapshot);
}
public sealed class AdminPolicyVersion
{
    public string Id { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public int VersionNumber { get; set; }
    public string SourceSha256 { get; set; } = "";
    public bool CompiledOk { get; set; }
    public string? CompileError { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? Comment { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? Source { get; set; }
    public override string ToString() => nameof(AdminPolicyVersion);
}
public sealed class AdminPolicyValidation
{
    [JsonRequired] public bool CompiledOk { get; set; }
    public List<AdminPolicyCompileIssue> Errors { get; set; } = [];
}
public sealed class AdminPolicyCompileIssue
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Message { get; set; } = "";
}
public sealed class AdminPolicySimulationResult
{
    public JsonElement Decision { get; set; }
    public long EvalTimeNs { get; set; }
    public long Generation { get; set; }
    public override string ToString() => nameof(AdminPolicySimulationResult);
}
public sealed class AdminPolicyApplication
{
    [JsonRequired] public bool LocalApplied { get; set; }
    public long LoadedGeneration { get; set; }
    [JsonRequired] public bool PublicationFailed { get; set; }
}
public sealed class AdminPolicyApplyResult
{
    [JsonRequired] public bool Persisted { get; set; }
    [JsonRequired] public AdminPolicyDocument Document { get; set; } = new();
    [JsonRequired] public long PersistedGeneration { get; set; }
    [JsonRequired] public AdminPolicyApplication Application { get; set; } = new();
    [JsonIgnore] public string ApplicationMessage => !Application.LocalApplied
        ? AdminText("savedNotReloaded", new { generation = PersistedGeneration })
        : Application.PublicationFailed ? AdminText("savedNotifyFailed", new { generation = PersistedGeneration, loaded = Application.LoadedGeneration })
        : AdminText("savedLoaded", new { generation = PersistedGeneration, loaded = Application.LoadedGeneration });
    private static string AdminText(string key, object values) => SiloPlayer.Core.Services.Admin.AdminText.T("web.admin.common.policy." + key, values);
}
public sealed class AdminPolicyVendor
{
    public string Path { get; set; } = "";
    public string Source { get; set; } = "";
    public override string ToString() => nameof(AdminPolicyVendor);
}
public sealed class AdminPolicyDecision
{
    public string Id { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string DecisionName { get; set; } = "";
    public long PolicyGeneration { get; set; }
    public string? UserId { get; set; }
    public string? ProfileId { get; set; }
    public string? SessionId { get; set; }
    public string? RequestId { get; set; }
    public string? NodeId { get; set; }
    public bool? Allowed { get; set; }
    public long EvalTimeNs { get; set; }
    public string InputDigest { get; set; } = "";
    public JsonElement InputSample { get; set; }
    public JsonElement ResultSample { get; set; }
    public string? Error { get; set; }
    public override string ToString() => nameof(AdminPolicyDecision);
}
public sealed record AdminPolicyDecisionQuery(string? DecisionName = null, string? UserId = null, string? Allowed = null, string? From = null, string? To = null);
