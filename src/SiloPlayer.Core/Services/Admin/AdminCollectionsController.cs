using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Page-owned authority, single-write gate and bounded reads. No background catalog walk or permanent polling.</summary>
public sealed class AdminCollectionsController : IDisposable
{
    private readonly AdminCollectionsApi _api;
    private readonly AdminLibrariesApi _libraries;
    private readonly object _stateGate = new();
    private int _readGeneration;
    private CancellationTokenSource? _reads;
    public AdminAuthorityLifetime Lifetime { get; }
    public MonitorAuthorityLease? Owner => Lifetime.Owner;
    public AdminCollectionsList? List { get; private set; }
    public AdminCollectionsBoard? Board { get; private set; }
    public IReadOnlyList<AdminLibrary> Libraries { get; private set; } = [];
    public AdminCollectionsCapabilities? Capabilities { get; private set; }
    public AdminCollectionSnapshot? Editor { get; private set; }
    public JsonObject? Draft { get; private set; }
    private string? _savedDraft;
    private string? _createdId;
    /// <summary>A create the server confirmed. While its canonical editor cannot be read, Save reloads this identity and never creates again.</summary>
    public string? CreatedId { get { lock (_stateGate) return _createdId; } }
    /// <summary>The starter-pack job this page observes. It belongs to the current authority and is dropped with it.</summary>
    public string? StarterJobId { get; private set; }
    public int StarterJobReads { get; private set; }
    public void BeginStarterJob(string id) { lock (_stateGate) { StarterJobId = id; StarterJobReads = 0; } }
    public int CountStarterJobRead() { lock (_stateGate) return ++StarterJobReads; }
    public void EndStarterJob() { lock (_stateGate) { StarterJobId = null; StarterJobReads = 0; } }
    public bool OutcomeUnknown { get; private set; }
    public bool Dirty => Owner?.IsCurrent == true && Draft != null && Draft.ToJsonString() != _savedDraft;
    public bool Busy { get; private set; }
    public bool Loading { get; private set; }
    public string? Error { get; private set; }
    public event Action? Retired;

    public AdminCollectionsController(AdminCollectionsApi api, AdminLibrariesApi libraries, AdminMonitorAuthority authority)
    {
        _api = api; _libraries = libraries; Lifetime = new(authority);
        Lifetime.Retired += OnRetired;
    }
    private void Clear() { lock (_stateGate) { ++_readGeneration; _reads?.Cancel(); _reads = null; List = null; Board = null; Libraries = []; Capabilities = null; Editor = null; Draft = null; _savedDraft = null; _createdId = null; StarterJobId = null; StarterJobReads = 0; OutcomeUnknown = false; Error = null; Loading = false; } }
    private (int Generation, CancellationTokenSource Reads) BeginRead(MonitorAuthorityLease owner)
    {
        lock (_stateGate)
        {
            _reads?.Cancel(); var reads = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); _reads = reads;
            return (++_readGeneration, reads);
        }
    }
    private void OnRetired() { Clear(); Retired?.Invoke(); }
    public void Suspend() { Lifetime.Retire(); Clear(); }
    public void Open() { Lifetime.Resume(); Lifetime.Open(); }
    public async Task LoadAsync(string? library, bool arrange, bool editorOnly = false)
    {
        var owner = Owner ?? throw new MonitorAuthorityRetiredException();
        var (generation, reads) = BeginRead(owner);
        lock (_stateGate) { Loading = true; Error = null; }
        try
        {
            var capabilities = await _api.CapabilitiesAsync(owner, reads.Token).ConfigureAwait(false);
            var libraries = await _libraries.ListAsync(owner, reads.Token).ConfigureAwait(false);
            if (!libraries.IsComplete) throw new InvalidDataException("Libraries could not be completely read.");
            lock (_stateGate) { owner.ThrowIfRetired(); if (generation != _readGeneration) return; Capabilities = capabilities; Libraries = libraries.Items; }
            library ??= arrange ? libraries.Items.FirstOrDefault()?.Id : null;
            var board = arrange && library != null && capabilities.Groups ? await _api.BoardAsync(owner, library, reads.Token).ConfigureAwait(false) : null;
            var list = editorOnly ? new AdminCollectionsList() : board?.List ?? await _api.ListAsync(owner, library, reads.Token).ConfigureAwait(false);
            lock (_stateGate)
            {
                owner.ThrowIfRetired();
                if (generation != _readGeneration) return;
                Capabilities = capabilities; Libraries = libraries.Items; Board = board; List = list;
                // Only a re-read of the canonical list answers a lost create; an editor-only load reads none.
                if (!editorOnly) OutcomeUnknown = false;
            }
        }
        finally { lock (_stateGate) { if (generation == _readGeneration && owner.IsCurrent) Loading = false; if (ReferenceEquals(_reads, reads)) _reads = null; } reads.Dispose(); }
    }
    private static readonly string[] EditableFields = ["title", "description", "slug", "library_ids", "collection_type", "visibility", "query_definition", "sort_config", "source_config", "source_url", "sync_schedule"];
    public async Task EditAsync(string? id, string kind = "manual", string? library = null, string? importSource = null)
    {
        // A confirmed create whose canonical read failed is reloaded by identity. A fresh draft here would create it again.
        lock (_stateGate) { if (id == null && Editor == null && _createdId != null) id = _createdId; }
        var owner = Owner ?? throw new MonitorAuthorityRetiredException(); var (generation, reads) = BeginRead(owner);
        try
        {
        var editor = id == null ? null : await _api.GetAsync(owner, id, reads.Token).ConfigureAwait(false);
        var draft = new JsonObject();
        if (editor != null)
        {
            foreach (var key in EditableFields) if (editor.Value.TryGetPropertyValue(key, out var value)) draft[key] = value?.DeepClone();
            if (draft["library_ids"] is not JsonArray { Count: > 0 } && editor.Value["library_id"] is { } lib) draft["library_ids"] = new JsonArray(lib.DeepClone());
        }
        else
        {
            draft = new() { ["title"] = "", ["description"] = "", ["collection_type"] = kind == "synced" ? "mdblist" : kind, ["visibility"] = "visible", ["library_ids"] = library == null ? new JsonArray() : new JsonArray(library),
                // Smart rules name their own libraries (none: every library). Opened from a library, a new one starts there.
                ["query_definition"] = new JsonObject { ["library_ids"] = AdminCollectionsApi.RuleLibraryIds(library == null ? new JsonArray() : new JsonArray(library)), ["match"] = "all", ["groups"] = new JsonArray() }, ["sort_config"] = new JsonObject(), ["source_config"] = new JsonObject() };
            if (kind == "synced")
            {
                var requested = importSource == "tmdb_chart" ? "tmdb" : importSource;
                var source = requested != null && Capabilities?.ImportSources.Contains(requested) == true ? requested : Capabilities?.ImportSources.FirstOrDefault() ?? "mdblist";
                draft["collection_type"] = source == "mdblist" ? "mdblist" : "tmdb";
                draft["source_config"] = new JsonObject { ["mode"] = source == "mdblist" ? "mdblist_json" : source == "tmdb_list" ? "tmdb_list" : "tmdb_preset" };
            }
        }
        lock (_stateGate)
        {
            owner.ThrowIfRetired(); if (generation != _readGeneration) return;
            Editor = editor; _createdId = editor == null ? null : AdminCollectionsApi.CanonicalId(editor.Value["id"]);
            // Reading a collection answers its own state. A fresh draft answers nothing about a create whose outcome is unknown.
            if (editor != null) OutcomeUnknown = false;
            Draft = draft; _savedDraft = draft.ToJsonString();
        }
        }
        finally { lock (_stateGate) { if (ReferenceEquals(_reads, reads)) _reads = null; } reads.Dispose(); }
    }
    /// <summary>Reload the open editor: the saved collection, or the confirmed create whose canonical read failed, or a fresh draft.</summary>
    public Task ReloadEditorAsync(string kind = "manual", string? library = null, string? importSource = null) =>
        EditAsync(Editor == null ? null : AdminCollectionsApi.CanonicalId(Editor.Value["id"]), kind, library, importSource);
    public void CloseEditor() { lock (_stateGate) { ++_readGeneration; _reads?.Cancel(); _reads = null; Editor = null; Draft = null; _savedDraft = null; _createdId = null; } }
    public JsonObject? StoredDefaultSort
    {
        get
        {
            if (Draft is null || Draft["sort_config"] is not JsonObject config) return null;
            return config.TryGetPropertyValue("field", out var node) && node is JsonValue value && value.TryGetValue<string>(out var sortField) && !string.IsNullOrWhiteSpace(sortField) ? config : null;
        }
    }
    /// <summary>A stored default sort wins over the rules Order, so changing Order clears it. Other sort settings stay.</summary>
    public static JsonObject WithoutDefaultSort(JsonObject? config)
    {
        var rest = (JsonObject?)config?.DeepClone() ?? new JsonObject();
        rest.Remove("field"); rest.Remove("order");
        return rest;
    }
    public void ClearStoredDefaultSort() { if (Draft != null && Draft["sort_config"] is JsonObject config) Draft["sort_config"] = WithoutDefaultSort(config); }
    /// <summary>The libraries a smart collection draws its titles from. Independent of where it shows; none means every library.</summary>
    public void SetRuleLibraries(IEnumerable<string> libraries)
    {
        if (Draft == null) throw new InvalidOperationException("Open a collection first.");
        var rules = Draft["query_definition"] as JsonObject ?? new JsonObject();
        rules["library_ids"] = AdminCollectionsApi.RuleLibraryIds(new JsonArray(libraries.Distinct().Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()));
        Draft["query_definition"] = rules;
    }
    public void Discard() { if (_savedDraft != null) Draft = JsonNode.Parse(_savedDraft)!.AsObject(); }
    /// <summary>At least one rule has a field, an operator and a filled value. With none, a smart collection would match every title.</summary>
    public static bool HasCompleteRule(QueryDefinition rules) => rules.Groups.Any(group => group.Rules.Any(rule => !string.IsNullOrEmpty(rule.Field) && !string.IsNullOrEmpty(rule.Op) && Filled(rule.Value)));
    private static bool Filled(object? value) => value switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        JsonElement element => element.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(element.GetString()),
            JsonValueKind.Array => element.GetArrayLength() > 0 && element.EnumerateArray().All(item => Filled(item)),
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            _ => true
        },
        System.Collections.IEnumerable items => items.Cast<object?>().Any() && items.Cast<object?>().All(Filled),
        _ => true
    };
    public static void NormalizeChart(JsonObject config)
    {
        var preset = config["preset"]?.ToString() ?? "trending";
        if (preset is not ("trending" or "popular" or "top_rated" or "now_playing" or "upcoming" or "airing_today" or "on_the_air")) preset = "trending";
        string[] allowed = preset switch { "trending" => ["movie", "tv", "all"], "now_playing" or "upcoming" => ["movie"], "airing_today" or "on_the_air" => ["tv"], _ => ["movie", "tv"] };
        var media = config["media_type"]?.ToString() ?? "all";
        config["mode"] = "tmdb_preset"; config["preset"] = preset; config["media_type"] = allowed.Contains(media) ? media : allowed[0];
        if (preset != "trending") config.Remove("time_window");
        else if (config["time_window"]?.ToString() is not ("day" or "week")) config["time_window"] = "day";
    }
    public IReadOnlyList<string> ChangedDraftFields() => Draft == null || _savedDraft == null ? [] :
        EditableFields.Where(key => !JsonNode.DeepEquals(Draft[key], JsonNode.Parse(_savedDraft)![key])).ToArray();
    /// <summary>Explicit review adopts the fresh validator and selected local fields, without sending a write.</summary>
    public void Rebase(AdminCollectionSnapshot latest, IReadOnlySet<string> keepLocal)
    {
        var owner = Owner ?? throw new MonitorAuthorityRetiredException(); owner.ThrowIfRetired(); latest.Owner.ThrowIfRetired();
        if (Editor == null || latest.Path != Editor.Path || latest.Value["id"]?.ToString() != Editor.Value["id"]?.ToString() || latest.Owner.Epoch != owner.Epoch || latest.Owner.AccountId != owner.AccountId)
            throw new MonitorAuthorityRetiredException();
        AdminCollectionsApi.Strong(latest.ETag);
        var next = new JsonObject(); foreach (var key in EditableFields) if (latest.Value.TryGetPropertyValue(key, out var value)) next[key] = value?.DeepClone();
        if (next["library_ids"] is not JsonArray { Count: > 0 } && latest.Value["library_id"] is { } library) next["library_ids"] = new JsonArray(library.DeepClone());
        var baseline = next.ToJsonString();
        foreach (var key in keepLocal.Where(EditableFields.Contains)) if (Draft?.TryGetPropertyValue(key, out var value) == true) next[key] = value?.DeepClone();
        Editor = latest; Draft = next; _savedDraft = baseline; OutcomeUnknown = false;
    }
    public async Task<T> CommandAsync<T>(Func<MonitorAuthorityLease, Task<T>> command)
    {
        if (Busy) throw new InvalidOperationException("A collection change is already in progress.");
        if (OutcomeUnknown) throw new InvalidOperationException("The outcome is unknown. Reload and review before making another change.");
        using var lease = Lifetime.CaptureAction(); Busy = true; Error = null;
        try { var result = await command(lease).ConfigureAwait(false); lease.ThrowIfRetired(); return result; }
        catch (Exception error)
        {
            if (lease.IsCurrent) { Error = Message(error); OutcomeUnknown = error is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException || error is ApiException { StatusCode: >= 500 }; }
            throw;
        }
        finally { Busy = false; }
    }
    public static string Message(Exception error) => error switch
    {
        ApiException { StatusCode: 412 } => "It changed since you opened this. Reload and review before trying again.",
        ApiException { StatusCode: 409 } => "The collection changed or is used by a section. Reload and review it.",
        ApiException { StatusCode: 401 or 403 } => "Administrator access is required. Sign in again.",
        ApiException => "The server could not complete the collection request.",
        HttpRequestException or TaskCanceledException => "The outcome is unknown. Reload and review before making another change. The request was not replayed.",
        AdminCollectionChangedException or InvalidDataException or ArgumentException or InvalidOperationException => error.Message,
        _ => "The collection request failed. Reload and review before trying again."
    };
    public async Task<string> SaveAsync()
    {
        if (OutcomeUnknown) throw new InvalidOperationException("The outcome is unknown. Reload and review before saving again.");
        if (Editor == null && _createdId != null) { await EditAsync(_createdId).ConfigureAwait(false); return _createdId; }
        var draft = Draft ?? throw new InvalidOperationException("Open a collection first.");
        if (string.IsNullOrWhiteSpace(draft["title"]?.GetValue<string>())) throw new ArgumentException("Name it, then create it.");
        if (draft["library_ids"] is not JsonArray { Count: > 0 }) throw new ArgumentException("Pick libraries first.");
        var body = (JsonObject)draft.DeepClone();
        if (body["collection_type"]?.GetValue<string>() == "smart")
        {
            var rules = body["query_definition"] as JsonObject ?? throw new ArgumentException("Add at least one rule.");
            var typed = rules.Deserialize<QueryDefinition>(AdminCollectionsApi.Json)!;
            if (!HasCompleteRule(typed)) throw new ArgumentException("Add at least one rule.");
            // The rules keep their own libraries. Where the collection shows (library_ids) never rewrites them.
            rules["library_ids"] = AdminCollectionsApi.RuleLibraryIds(rules["library_ids"]);
        }
        else body.Remove("query_definition");
        var imported = Editor == null && body["collection_type"]?.GetValue<string>() is "mdblist" or "tmdb";
        var result = await CommandAsync(lease =>
        {
            if (!imported) return _api.SaveAsync(lease, Editor, body);
            var source = body["source_config"] as JsonObject ?? new();
            var mode = source["mode"]?.GetValue<string>();
            var provider = body["collection_type"]!.GetValue<string>() == "mdblist" ? "mdblist" : mode == "tmdb_list" ? "tmdb_list" : "tmdb";
            var request = new JsonObject();
            foreach (var key in new[] { "title", "description", "library_ids", "sort_config", "sync_schedule" })
                if (body[key] != null) request[key] = body[key]!.DeepClone();
            foreach (var key in new[] { "url", "preset", "media_type", "time_window", "limit" })
                if (source[key] != null) request[key] = source[key]!.DeepClone();
            return _api.ImportAsync(lease, provider, request);
        }).ConfigureAwait(false);
        var id = AdminCollectionsApi.CanonicalId(imported ? result.Value["collection"]?["id"] : result.Value["id"]);
        if (id == null)
        {
            // The server may have written something. A reply without a usable identity is an unconfirmed outcome, not "nothing was sent".
            lock (_stateGate) { OutcomeUnknown = true; Error = "The server answered without identifying the saved collection."; }
            throw new InvalidDataException("The server answered without identifying the saved collection. Reload the collections list and review it before saving again.");
        }
        lock (_stateGate) { _createdId = id; }
        if (imported && body["visibility"]?.GetValue<string>() == "hidden")
        {
            await CommandAsync(async lease => await _api.SaveAsync(lease, await _api.GetAsync(lease, id).ConfigureAwait(false), new() { ["visibility"] = "hidden" }).ConfigureAwait(false)).ConfigureAwait(false);
        }
        // Persisted metadata may be normalized. Reload a strong canonical editor; no write is replayed if this read fails.
        await EditAsync(id).ConfigureAwait(false);
        return id;
    }
    public void Dispose() { Lifetime.Retired -= OnRetired; Lifetime.Dispose(); Clear(); Retired = null; }
}
