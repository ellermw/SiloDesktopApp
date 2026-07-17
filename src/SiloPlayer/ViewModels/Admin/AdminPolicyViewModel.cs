using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminPolicyViewModel(AdminApi api) : ObservableObject
{
    public ObservableCollection<PolicyDocument> Documents { get; } = [];
    public ObservableCollection<PolicyVendorModule> VendorModules { get; } = [];
    public ObservableCollection<PolicyVersionSummary> Versions { get; } = [];
    public ObservableCollection<PolicyDecisionEntry> Decisions { get; } = [];
    [ObservableProperty] private PolicyCapability? _capability;
    [ObservableProperty] private PolicyDocument? _selectedDocument;
    [ObservableProperty] private PolicyVendorModule? _selectedVendor;
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string _comment = "";
    [ObservableProperty] private string _decisionFilter = "";
    [ObservableProperty] private string _decisionUserId = "";
    [ObservableProperty] private string _decisionAllowed = "All";
    [ObservableProperty] private string _decisionFrom = "";
    [ObservableProperty] private string _decisionTo = "";
    [ObservableProperty] private string? _decisionNextCursor;
    [ObservableProperty] private string _simulationInput = "{}";
    [ObservableProperty] private string _simulationResult = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    public bool IsAvailable => Capability?.Enabled == true && Capability.EditorAvailable;
    private readonly List<string> _decisionCursorStack = [];
    private string? _decisionCursor;

    public async Task LoadAsync()
    {
        IsLoading = true; ErrorMessage = null;
        try
        {
            Capability = await api.GetPolicyCapabilityAsync(); OnPropertyChanged(nameof(IsAvailable));
            if (!IsAvailable) return;
            var docs = api.GetPolicyDocumentsAsync(); var vendor = api.GetPolicyVendorAsync(); var decisions = api.GetPolicyDecisionsAsync("?limit=25");
            await Task.WhenAll(docs, vendor, decisions); Replace(Documents, docs.Result); Replace(VendorModules, vendor.Result); Replace(Decisions, decisions.Result.Entries); DecisionNextCursor = decisions.Result.NextCursor;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }
    public async Task SelectDocumentAsync(PolicyDocument document)
    {
        SelectedDocument = document; Source = document.ActiveVersion?.Source ?? "";
        Replace(Versions, await api.GetPolicyVersionsAsync(document.Id));
        if (string.IsNullOrEmpty(Source) && document.ActiveVersion is { VersionNumber: var n }) Source = (await api.GetPolicyVersionAsync(document.Id, n)).Source ?? "";
    }
    public async Task CreateDocumentAsync(string domain, string name) => await Busy(async () => { var d = await api.CreatePolicyDocumentAsync(domain, name); Documents.Add(d); await SelectDocumentAsync(d); });
    public async Task SaveVersionAsync() => await Busy(async () => { if (SelectedDocument is null) return; var valid = await api.ValidatePolicyAsync(SelectedDocument.Domain, Source); if (!valid.CompiledOk) throw new InvalidOperationException(string.Join(Environment.NewLine, valid.Errors.Select(e => $"{e.Row}:{e.Col} {e.Message}"))); var created = await api.CreatePolicyVersionAsync(SelectedDocument.Id, Source, Comment); await api.ActivatePolicyVersionAsync(SelectedDocument.Id, created.VersionNumber); StatusMessage = $"Version {created.VersionNumber} saved and activated."; await SelectDocumentAsync(SelectedDocument); });
    public async Task ActivateAsync(PolicyVersionSummary version) => await Busy(async () => { if (SelectedDocument is null) return; var result = await api.ActivatePolicyVersionAsync(SelectedDocument.Id, version.VersionNumber); SelectedDocument.ActiveVersionId = result.ActiveVersionId; StatusMessage = $"Version {version.VersionNumber} activated."; await SelectDocumentAsync(SelectedDocument); });
    public async Task ToggleEnabledAsync(bool enabled) => await Busy(async () => { if (SelectedDocument is not null) { await api.SetPolicyDocumentEnabledAsync(SelectedDocument.Id, enabled); SelectedDocument.Enabled = enabled; } });
    public async Task ToggleDocumentEnabledAsync(PolicyDocument document, bool enabled) => await Busy(async () => { await api.SetPolicyDocumentEnabledAsync(document.Id, enabled); document.Enabled = enabled; });
    public async Task DeleteAsync() => await Busy(async () => { if (SelectedDocument is null) return; await api.DeletePolicyDocumentAsync(SelectedDocument.Id); Documents.Remove(SelectedDocument); SelectedDocument = null; Versions.Clear(); Source = ""; });
    public async Task SimulateAsync() => await Busy(async () => { var domain = SelectedDocument?.Domain ?? Capability?.DecisionTypes.FirstOrDefault() ?? ""; var input = JsonSerializer.Deserialize<object>(SimulationInput) ?? new { }; var result = await api.SimulatePolicyAsync(domain, Source, input); SimulationResult = JsonSerializer.Serialize(result.Decision, new JsonSerializerOptions { WriteIndented = true }); });
    public async Task FilterDecisionsAsync() => await Busy(async () =>
    {
        _decisionCursor = null; _decisionCursorStack.Clear();
        await LoadDecisionsAsync();
    });
    public async Task ResetDecisionFiltersAsync() => await Busy(async () =>
    {
        DecisionFilter = ""; DecisionUserId = ""; DecisionAllowed = "All"; DecisionFrom = ""; DecisionTo = "";
        _decisionCursor = null; _decisionCursorStack.Clear();
        await LoadDecisionsAsync();
    });
    public async Task NextDecisionPageAsync() => await Busy(async () =>
    {
        if (string.IsNullOrWhiteSpace(DecisionNextCursor)) return;
        _decisionCursorStack.Add(_decisionCursor ?? ""); _decisionCursor = DecisionNextCursor;
        await LoadDecisionsAsync();
    });
    public async Task PreviousDecisionPageAsync() => await Busy(async () =>
    {
        if (_decisionCursorStack.Count == 0) return;
        var index = _decisionCursorStack.Count - 1; _decisionCursor = _decisionCursorStack[index]; _decisionCursorStack.RemoveAt(index);
        if (string.IsNullOrWhiteSpace(_decisionCursor)) _decisionCursor = null;
        await LoadDecisionsAsync();
    });
    public bool HasPreviousDecisionPage => _decisionCursorStack.Count > 0;
    private async Task LoadDecisionsAsync()
    {
        var query = new List<string> { "limit=25" };
        if (!string.IsNullOrWhiteSpace(DecisionFilter)) query.Add("decision_name=" + Uri.EscapeDataString(DecisionFilter.Trim()));
        if (int.TryParse(DecisionUserId, out var userId)) query.Add("user_id=" + userId);
        if (DecisionAllowed == "Allowed") query.Add("allowed=true"); else if (DecisionAllowed == "Denied") query.Add("allowed=false");
        if (!string.IsNullOrWhiteSpace(DecisionFrom)) query.Add("from=" + Uri.EscapeDataString(DecisionFrom.Trim()));
        if (!string.IsNullOrWhiteSpace(DecisionTo)) query.Add("to=" + Uri.EscapeDataString(DecisionTo.Trim()));
        if (!string.IsNullOrWhiteSpace(_decisionCursor)) query.Add("cursor=" + Uri.EscapeDataString(_decisionCursor));
        var result = await api.GetPolicyDecisionsAsync("?" + string.Join("&", query));
        Replace(Decisions, result.Entries); DecisionNextCursor = result.NextCursor;
    }
    public Task<PolicyDecisionEntry> GetDecisionAsync(long id) => api.GetPolicyDecisionAsync(id);
    private async Task Busy(Func<Task> work) { if (IsBusy) return; IsBusy = true; ErrorMessage = null; StatusMessage = null; try { await work(); } catch (Exception ex) { ErrorMessage = ex.Message; } finally { IsBusy = false; } }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
}
