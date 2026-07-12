namespace SiloPlayer.Core.Models.Admin;

public sealed class PolicyCapability { public bool Enabled { get; set; } public bool EditorAvailable { get; set; } public List<string> DecisionTypes { get; set; } = []; public long Generation { get; set; } }
public sealed class PolicyVendorModule { public string Path { get; set; } = ""; public string Source { get; set; } = ""; }
public sealed class PolicyCompileIssue { public int Row { get; set; } public int Col { get; set; } public string Message { get; set; } = ""; }
public class PolicyVersionSummary { public long Id { get; set; } public long DocumentId { get; set; } public int VersionNumber { get; set; } public string SourceSha256 { get; set; } = ""; public bool CompiledOk { get; set; } public string? CompileError { get; set; } public int? CreatedByUserId { get; set; } public string? Comment { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class PolicyVersion : PolicyVersionSummary { public string? Source { get; set; } }
public sealed class PolicyDocument { public long Id { get; set; } public string Domain { get; set; } = ""; public string Name { get; set; } = ""; public bool Enabled { get; set; } public long? ActiveVersionId { get; set; } public PolicyVersion? ActiveVersion { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } }
public sealed class PolicyCreateVersionResult { public long Id { get; set; } public int VersionNumber { get; set; } public bool CompiledOk { get; set; } }
public sealed class PolicyActivateVersionResult { public long ActiveVersionId { get; set; } public long Generation { get; set; } }
public sealed class PolicySetDocumentEnabledResult { public long Id { get; set; } public bool Enabled { get; set; } public long Generation { get; set; } }
public sealed class PolicyValidateResult { public bool CompiledOk { get; set; } public List<PolicyCompileIssue> Errors { get; set; } = []; }
public sealed class PolicySimulateResult { public object? Decision { get; set; } public long EvalTimeNs { get; set; } public long Generation { get; set; } }
public sealed class PolicyDecisionEntry { public long Id { get; set; } public DateTimeOffset Timestamp { get; set; } public string DecisionName { get; set; } = ""; public long PolicyGeneration { get; set; } public int? UserId { get; set; } public string? ProfileId { get; set; } public string? SessionId { get; set; } public string? RequestId { get; set; } public string? NodeId { get; set; } public bool? Allowed { get; set; } public long EvalTimeNs { get; set; } public string InputDigest { get; set; } = ""; public object? InputSample { get; set; } public object? ResultSample { get; set; } public string? Error { get; set; } }
public sealed class PolicyDecisionListResult { public List<PolicyDecisionEntry> Entries { get; set; } = []; public string? NextCursor { get; set; } }
