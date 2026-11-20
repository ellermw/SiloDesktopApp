namespace SiloPlayer.Core.Services.Admin;

/// <summary>The four mutually exclusive import sources (WebUI <c>importSource</c>).</summary>
public enum AdminCatalogImportSourceKind { LocalPath, ExportJob, BucketArtifact, RemoteUrl }

/// <summary>Background job (<c>/import-jobs</c>, 202) or import-and-wait (<c>/import</c>, committed on 200).</summary>
public enum AdminCatalogImportExecution { Queued, Synchronous }

public enum AdminCatalogConflictMode { SkipExisting, OverwriteExisting }

/// <summary>One path rewrite row with a stable identity (WebUI <c>PathRewriteRow</c>). Values are server paths, kept as typed.</summary>
public sealed record AdminCatalogPathRewriteRow(string Id, string From, string To)
{
    public bool IsBlank => From.Trim().Length == 0 && To.Trim().Length == 0;
    public bool IsComplete => From.Trim().Length > 0 && To.Trim().Length > 0;
    public bool IsPartial => !IsBlank && !IsComplete;
}

/// <summary>
/// The Import Catalog dialog draft (official <c>AdminCatalogMaintenance.tsx</c> state and
/// <c>adminCatalogMaintenancePathRewrites.ts</c>). Immutable: every edit returns a new draft.
/// Every path here is a path on the SERVER (often Linux); it is never checked with Windows
/// file-system APIs. Only the selected source's value is ever sent.
/// </summary>
public sealed record AdminCatalogImportDraft
{
    public const string DefaultLocalPath = "/catalog-seeds/";
    public const int MaxValueLength = 4096;
    public const int MaxRewrites = 200;
    private static long _nextRowId;

    public AdminCatalogImportSourceKind Source { get; init; } = AdminCatalogImportSourceKind.LocalPath;
    public AdminCatalogImportExecution Execution { get; init; } = AdminCatalogImportExecution.Queued;
    public string LocalPath { get; init; } = DefaultLocalPath;
    public string ExportJobId { get; init; } = "";
    public string ArtifactKey { get; init; } = "";
    public string RemoteUrl { get; init; } = "";
    public AdminCatalogConflictMode ConflictMode { get; init; } = AdminCatalogConflictMode.SkipExisting;
    public IReadOnlyList<AdminCatalogPathRewriteRow> Rewrites { get; init; } = [NewRow()];

    /// <summary>The draft the dialog opens with and returns to after a successful import.</summary>
    public static AdminCatalogImportDraft Initial() => new();

    public static AdminCatalogPathRewriteRow NewRow() => new("path-rewrite-" + Interlocked.Increment(ref _nextRowId), "", "");

    /// <summary>Whether anything differs from <see cref="Initial"/> (row identities aside).</summary>
    public bool IsDirty => Source != AdminCatalogImportSourceKind.LocalPath || Execution != AdminCatalogImportExecution.Queued
        || LocalPath != DefaultLocalPath || ExportJobId.Length > 0 || ArtifactKey.Length > 0 || RemoteUrl.Length > 0
        || ConflictMode != AdminCatalogConflictMode.SkipExisting || Rewrites.Count != 1 || !Rewrites[0].IsBlank;

    /// <summary>The selected source has a value (WebUI <c>isImportSubmitDisabled</c> without the pending part).</summary>
    public bool HasSelectedSource => SelectedValue.Length > 0;

    /// <summary>The selected source's wire value: local and remote values trimmed as WebUI sends them, keys unchanged.</summary>
    public string SelectedValue => Source switch
    {
        AdminCatalogImportSourceKind.LocalPath => LocalPath.Trim(),
        AdminCatalogImportSourceKind.ExportJob => ExportJobId,
        AdminCatalogImportSourceKind.BucketArtifact => ArtifactKey,
        _ => RemoteUrl.Trim(),
    };

    /// <summary>Rows with exactly one side filled; they block submission instead of being dropped silently.</summary>
    public IReadOnlyList<string> PartialRewriteIds => Rewrites.Where(row => row.IsPartial).Select(row => row.Id).ToArray();

    public bool HasOversizedValue => SelectedValue.Length > MaxValueLength || SelectedValue.Any(char.IsControl) || Rewrites.Count > MaxRewrites
        || Rewrites.Any(row => row.From.Length > MaxValueLength || row.To.Length > MaxValueLength || row.From.Any(char.IsControl) || row.To.Any(char.IsControl));

    public bool CanSubmit => HasSelectedSource && PartialRewriteIds.Count == 0 && !HasOversizedValue;

    public AdminCatalogImportDraft WithSource(AdminCatalogImportSourceKind source) => this with { Source = source };
    public AdminCatalogImportDraft AddRewrite() => Rewrites.Count >= MaxRewrites ? this : this with { Rewrites = [.. Rewrites, NewRow()] };

    /// <summary>Removing the only row leaves one fresh empty row (WebUI <c>removePathRewrite</c>).</summary>
    public AdminCatalogImportDraft RemoveRewrite(string id) => Rewrites.All(row => row.Id != id) ? this
        : this with { Rewrites = Rewrites.Count == 1 ? [NewRow()] : Rewrites.Where(row => row.Id != id).ToArray() };

    public AdminCatalogImportDraft UpdateRewrite(string id, string? from = null, string? to = null) => this with
    {
        Rewrites = Rewrites.Select(row => row.Id == id ? row with { From = from ?? row.From, To = to ?? row.To } : row).ToArray(),
    };

    /// <summary>The exact request to send; refuses a draft that cannot be submitted.</summary>
    public AdminCatalogImportRequest ToRequest()
    {
        if (!CanSubmit) throw new InvalidOperationException("The import draft is incomplete.");
        return new AdminCatalogImportRequest(Source, SelectedValue, Execution, ConflictMode,
            Rewrites.Where(row => row.IsComplete).Select(row => new AdminCatalogPathRewrite(row.From, row.To)).ToArray());
    }
}

/// <summary>One sent path rewrite (v2 <c>PathRewrite</c> <c>{from,to}</c>), exactly as typed.</summary>
public sealed record AdminCatalogPathRewrite(string From, string To);

/// <summary>An immutable import request: one source, conflict mode and the complete rewrite rows.</summary>
public sealed record AdminCatalogImportRequest(AdminCatalogImportSourceKind Source, string SourceValue, AdminCatalogImportExecution Execution,
    AdminCatalogConflictMode ConflictMode, IReadOnlyList<AdminCatalogPathRewrite> PathRewrites)
{
    public static string SourceMember(AdminCatalogImportSourceKind source) => source switch
    {
        AdminCatalogImportSourceKind.LocalPath => "local_path",
        AdminCatalogImportSourceKind.ExportJob => "export_job_id",
        AdminCatalogImportSourceKind.BucketArtifact => "artifact_key",
        _ => "remote_url",
    };

    /// <summary>The v2 <c>AdminCatalogImportRequest</c> body: exactly one source member, never a stale hidden one.</summary>
    public Dictionary<string, object?> ToBody()
    {
        if (string.IsNullOrEmpty(SourceValue)) throw new InvalidOperationException("An import source is required.");
        return new Dictionary<string, object?>
        {
            [SourceMember(Source)] = SourceValue,
            ["conflict_mode"] = ConflictMode == AdminCatalogConflictMode.OverwriteExisting ? "overwrite_existing" : "skip_existing",
            ["path_rewrites"] = PathRewrites.Select(rewrite => (object?)new Dictionary<string, object?> { ["from"] = rewrite.From, ["to"] = rewrite.To }).ToList(),
        };
    }
}
