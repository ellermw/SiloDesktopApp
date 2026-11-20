using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed class AdminPolicyController(AdminPolicyApi api, AdminMonitorAuthority authority) : AdminAccessController(authority)
{
    public AdminPolicyCapability? Capability { get; private set; }
    public bool Available => Current && Capability is { Enabled: true, EditorAvailable: true };
    public string Tab { get; private set; } = "documents";
    public IReadOnlyList<AdminPolicyDocument> Documents { get; private set; } = [];
    public string? DocumentCursor { get; private set; }
    public AdminPolicySnapshot? Snapshot { get; private set; }
    public AdminPolicySnapshot? Review { get; private set; }
    public IReadOnlyList<AdminPolicyVersion> Versions { get; private set; } = [];
    public string? VersionCursor { get; private set; }
    public AdminPolicyVersion? SelectedVersion { get; private set; }
    public AdminPolicyVersion? SavedVersion { get; private set; }
    public string Draft { get; private set; } = "";
    private string _seed = "";
    private string? _validated;
    public string Comment { get; private set; } = "";
    public bool Dirty => Current && (Draft != _seed || Comment.Length > 0);
    public bool Validated => _validated == Draft && Validation?.CompiledOk == true;
    public AdminPolicyValidation? Validation { get; private set; }
    public AdminPolicySimulationResult? Simulation { get; private set; }
    public IReadOnlyList<AdminPolicyVendor> Vendor { get; private set; } = [];
    public IReadOnlyList<AdminPolicyDecision> Decisions { get; private set; } = [];
    public AdminPolicyDecision? Decision { get; private set; }
    public AdminPolicyDecisionQuery Filters { get; private set; } = new();
    public string? DecisionCursor { get; private set; }
    public string? NextDecisionCursor { get; private set; }
    private readonly List<string?> _previous = [];
    public bool HasPrevious => _previous.Count > 0;
    public int EditorRevision { get; private set; }
    protected override void Reset()
    {
        Capability = null; Tab = "documents"; Documents = []; DocumentCursor = null; Snapshot = Review = null; Versions = []; VersionCursor = null;
        SelectedVersion = SavedVersion = null; Draft = _seed = Comment = ""; _validated = null; Validation = null; Simulation = null; Vendor = []; Decisions = []; Decision = null;
        Filters = new(); DecisionCursor = NextDecisionCursor = null; _previous.Clear(); EditorRevision++;
    }
    public Task OpenAsync(string tab = "documents", string? id = null) => OpenPageAsync(async owner =>
    {
        var capability = await api.CapabilityAsync(owner).ConfigureAwait(false); Publish(owner, () => Capability = capability);
        if (!Available) return;
        await LoadTabAsync(owner, NormalizeTab(tab)).ConfigureAwait(false);
        if (Tab == "documents" && id != null) await LoadEditorAsync(owner, id).ConfigureAwait(false);
    });
    public Task SelectTabAsync(string tab) => RunAsync(owner => Available ? LoadTabAsync(owner, NormalizeTab(tab)) : Task.CompletedTask);
    private static string NormalizeTab(string tab) => tab is "vendor" or "decisions" ? tab : "documents";
    private async Task LoadTabAsync(MonitorAuthorityLease owner, string tab)
    {
        if (tab == "vendor")
        {
            var vendor = await api.VendorAsync(owner).ConfigureAwait(false); Publish(owner, () => { Vendor = vendor; Tab = tab; });
        }
        else if (tab == "decisions")
        {
            var page = await api.DecisionsAsync(owner, Filters).ConfigureAwait(false);
            Publish(owner, () => { Decisions = page.Items; NextDecisionCursor = page.Page!.NextCursor; DecisionCursor = null; Decision = null; _previous.Clear(); Tab = tab; });
        }
        else
        {
            var page = await api.DocumentsAsync(owner).ConfigureAwait(false); Publish(owner, () => { Documents = page.Items; DocumentCursor = page.Page!.NextCursor; Tab = tab; });
        }
    }
    private async Task EnsureCapabilityAsync(MonitorAuthorityLease owner)
    {
        var capability = await api.CapabilityAsync(owner).ConfigureAwait(false); Publish(owner, () => Capability = capability);
        if (!capability.Enabled || !capability.EditorAvailable) throw new AdminAccessException("dependency_unavailable", 503);
    }
    public Task MoreDocumentsAsync() => RunAsync(async owner =>
    {
        if (!Available || DocumentCursor == null || Documents.Count >= 1000) return;
        var page = await api.DocumentsAsync(owner, DocumentCursor).ConfigureAwait(false);
        Publish(owner, () => { Documents = Documents.Concat(page.Items).DistinctBy(d => d.Id).Take(1000).ToArray(); DocumentCursor = page.Page!.NextCursor; });
    });
    public Task SelectAsync(string? id) => RunAsync(async owner =>
    {
        if (!Available) return;
        if (id != null) await LoadEditorAsync(owner, id).ConfigureAwait(false);
        else Publish(owner, () => { Snapshot = Review = null; Draft = _seed = Comment = ""; _validated = null; Validation = null; SavedVersion = SelectedVersion = null; EditorRevision++; });
    });
    private async Task LoadEditorAsync(MonitorAuthorityLease owner, string id)
    {
        var snapshot = await api.GetAsync(owner, id).ConfigureAwait(false);
        var versions = await api.VersionsAsync(owner, id).ConfigureAwait(false);
        var seed = snapshot.Value.ActiveVersion;
        if (seed?.Source == null && versions.Items.FirstOrDefault() is { } latest) seed = await api.VersionAsync(owner, id, latest.Id).ConfigureAwait(false);
        var source = seed?.Source ?? DefaultSource(snapshot.Value.Domain);
        Publish(owner, () =>
        {
            Snapshot = snapshot; Review = null; ReviewRequired = false; Versions = versions.Items; VersionCursor = versions.Page!.NextCursor;
            Draft = _seed = source; Comment = ""; _validated = null; Validation = null; Simulation = null; SavedVersion = seed;
            SelectedVersion = null; EditorRevision++;
        });
    }
    public Task CreateAsync(string domain, string name, bool confirmed) => RunAsync(async owner =>
    {
        if (!confirmed || Capability?.DecisionTypes.Contains(domain) != true) return;
        await EnsureCapabilityAsync(owner).ConfigureAwait(false);
        var document = await api.CreateAsync(owner, domain, name).ConfigureAwait(false);
        // The server owns the canonical override list: re-read its first page after the create instead of splicing the row in locally.
        await LoadTabAsync(owner, "documents").ConfigureAwait(false);
        await LoadEditorAsync(owner, document.Id).ConfigureAwait(false);
    }, write: true);
    public void SetDraft(string source)
    {
        lock (Gate) { if (!Available || Busy || source.Length > 262144) return; Draft = source; Simulation = null; } Notify();
    }
    public void SetComment(string comment) { lock (Gate) { if (!Available || Busy || comment.Length > 10000) return; Comment = comment; } Notify(); }
    public void Discard()
    {
        lock (Gate) { if (!Current || Busy) return; Draft = _seed; Comment = ""; _validated = null; Validation = null; Simulation = null; EditorRevision++; } Notify();
    }
    public Task ValidateAsync() => RunAsync(async owner =>
    {
        if (!Available || Snapshot == null) return; var source = Draft; var document = Snapshot;
        var result = await api.ValidateAsync(owner, document.Value.Domain, source).ConfigureAwait(false);
        Publish(owner, () => { Validation = result; _validated = source; Message = AdminText.T(result.CompiledOk ? "web.admin.access.policy.editor.validationPassed" : "web.admin.access.policy.editor.validationFailed"); });
    });
    public Task SaveAsync(bool confirmed) => RunAsync(async owner =>
    {
        if (!confirmed || !Available || Snapshot == null || !Validated) return;
        await EnsureCapabilityAsync(owner).ConfigureAwait(false);
        var document = Snapshot; var source = Draft; var version = await api.SaveVersionAsync(owner, document.Value.Id, source, Comment).ConfigureAwait(false);
        Publish(owner, () =>
        {
            SavedVersion = version; Versions = new[] { version }.Concat(Versions).DistinctBy(v => v.Id).Take(1000).ToArray();
            _seed = source; Comment = ""; ReviewRequired = true; Review = null;
            if (!version.CompiledOk) { _validated = source; Validation = new() { CompiledOk = false, Errors = [new() { Message = version.CompileError ?? "Saved draft did not compile." }] }; }
            Message = AdminText.T(version.CompiledOk ? "web.admin.access.policy.editor.savedVersion" : "web.admin.access.policy.editor.savedVersionNotCompiled", new { version = version.VersionNumber });
        });
    }, write: true);
    public Task ReviewAsync() => RunAsync(async owner =>
    {
        if (!Available || Snapshot == null) return; var review = await api.GetAsync(owner, Snapshot.Value.Id).ConfigureAwait(false); Publish(owner, () => Review = review);
    });
    public void AdoptReview()
    {
        lock (Gate)
        {
            if (!Available || Busy || Review == null || Snapshot?.Value.Id != Review.Value.Id) return;
            Snapshot = Review; Review = null; ReviewRequired = false; Message = AdminText.T("web.admin.access.policy.editor.revisionAdopted");
        }
        Notify();
    }
    public Task LoadSavedSourceAsync(bool confirmed) => RunAsync(async owner =>
    {
        if (confirmed && Available && Snapshot != null) await LoadEditorAsync(owner, Snapshot.Value.Id).ConfigureAwait(false);
    });
    public Task MoreVersionsAsync() => RunAsync(async owner =>
    {
        if (!Available || Snapshot == null || VersionCursor == null || Versions.Count >= 1000) return;
        var page = await api.VersionsAsync(owner, Snapshot.Value.Id, VersionCursor).ConfigureAwait(false);
        Publish(owner, () => { Versions = Versions.Concat(page.Items).DistinctBy(v => v.Id).Take(1000).ToArray(); VersionCursor = page.Page!.NextCursor; });
    });
    public Task ViewVersionAsync(string id) => RunAsync(async owner =>
    {
        if (!Available || Snapshot == null) return;
        var version = await api.VersionAsync(owner, Snapshot.Value.Id, id).ConfigureAwait(false); Publish(owner, () => SelectedVersion = version);
    });
    public Task ActivateAsync(AdminPolicySnapshot reviewed, AdminPolicyVersion version, bool confirmed) => RunAsync(async owner =>
    {
        if (!confirmed || !Available || !ReferenceEquals(reviewed, Snapshot) || !version.CompiledOk || version.DocumentId != reviewed.Value.Id ||
            !(ReferenceEquals(version, SelectedVersion) || ReferenceEquals(version, SavedVersion) && version.Source == Draft)) return;
        await EnsureCapabilityAsync(owner).ConfigureAwait(false);
        var result = await api.ActivateAsync(owner, reviewed, version.Id).ConfigureAwait(false);
        Publish(owner, () => { ReviewRequired = true; Review = null; Message = result.ApplicationMessage; });
    }, write: true);
    public Task SetEnabledAsync(AdminPolicySnapshot reviewed, bool enabled, bool confirmed) => RunAsync(async owner =>
    {
        if (!confirmed || !Available || !ReferenceEquals(Snapshot, reviewed)) return;
        await EnsureCapabilityAsync(owner).ConfigureAwait(false);
        var result = await api.EnableAsync(owner, reviewed, enabled).ConfigureAwait(false);
        Publish(owner, () => { ReviewRequired = true; Review = null; Message = result.ApplicationMessage; });
    }, write: true);
    public Task DeleteAsync(AdminPolicySnapshot reviewed, bool confirmed) => RunAsync(async owner =>
    {
        if (!confirmed || !Available || !ReferenceEquals(Snapshot, reviewed) || reviewed.Value.ActiveVersionId != null) return;
        await EnsureCapabilityAsync(owner).ConfigureAwait(false); await api.DeleteAsync(owner, reviewed).ConfigureAwait(false);
        Publish(owner, () => { Snapshot = null; Draft = _seed = Comment = ""; EditorRevision++; }); await LoadTabAsync(owner, "documents").ConfigureAwait(false);
    }, write: true);
    public Task SimulateAsync(string input) => RunAsync(async owner =>
    {
        if (!Available || Snapshot == null) return;
        var result = await api.SimulateAsync(owner, Snapshot.Value.Domain, Draft, input).ConfigureAwait(false); Publish(owner, () => Simulation = result);
    });
    public Task ApplyFiltersAsync(AdminPolicyDecisionQuery query) => RunAsync(async owner =>
    {
        if (!Available) return;
        var page = await api.DecisionsAsync(owner, query).ConfigureAwait(false);
        Publish(owner, () => { Filters = query; Decisions = page.Items; Decision = null; DecisionCursor = null; NextDecisionCursor = page.Page!.NextCursor; _previous.Clear(); });
    });
    public Task PageDecisionsAsync(bool forward) => RunAsync(async owner =>
    {
        if (!Available || forward && (NextDecisionCursor == null || _previous.Count >= 100) || !forward && _previous.Count == 0) return;
        var cursor = forward ? NextDecisionCursor : _previous[^1]; var page = await api.DecisionsAsync(owner, Filters, cursor).ConfigureAwait(false);
        Publish(owner, () => { if (forward) _previous.Add(DecisionCursor); else _previous.RemoveAt(_previous.Count - 1); DecisionCursor = cursor; NextDecisionCursor = page.Page!.NextCursor; Decisions = page.Items; Decision = null; });
    });
    public Task SelectDecisionAsync(string id) => RunAsync(async owner =>
    {
        if (!Available || !Decisions.Any(d => d.Id == id)) return;
        var decision = await api.DecisionAsync(owner, id).ConfigureAwait(false); Publish(owner, () => Decision = decision);
    });
    public static string DefaultSource(string domain) => $"package silo_custom.{domain}\n\nimport rego.v1\n\noverride(base_decision, input) := base_decision if {{\n  input.schema_version == 1\n}}\n";
}
