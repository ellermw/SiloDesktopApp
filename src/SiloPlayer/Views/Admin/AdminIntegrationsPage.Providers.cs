using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminIntegrationsPage
{
    private string? _editingProviderId, _providerTarget;
    private bool _providerDirty, _editorSyncing;
    private ComboBox? _providerType, _providerKeyMode;
    private TextBox? _providerName, _providerUrl;
    private PasswordBox? _providerKey;
    private readonly Dictionary<string, CheckBox> _providerCapabilities = new(StringComparer.Ordinal);
    private readonly List<Button> _providerEditorActions = [];

    private void BuildProviders()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(AdminSettingsUi.Title("AI providers"));
        panel.Children.Add(AdminSettingsUi.Text("Connect providers once, then choose which jobs use them. API keys are write-only.", 13, true));
        foreach (var provider in _providers)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            AddAction(actions, "Models", "AdminAiProviderModels-" + provider.Id, Ready, async () => await RunCommandAsync(async lease =>
            {
                var result = await _api.GetProviderModelsAsync(lease, provider.Id);
                if (!lease.IsCurrent || !_active) return;
                if (result.Success) _models[provider.Id] = result.Models;
                SetDomainStatus(lease, result.Success ? result.Models.Count == 0 ? "The provider did not list models. Enter a model name manually." : string.Join(", ", result.Models.Select(model => model.Id)) : result.Message);
            }));
            AddAction(actions, "Edit", "AdminAiProviderEdit-" + provider.Id, Ready, () => { BeginProviderEdit(provider); return Task.CompletedTask; });
            AddAction(actions, "Delete", "AdminAiProviderDelete-" + provider.Id, Ready && provider.UsedBy.Count == 0,
                () => RunCommandAsync(async lease => { await _api.DeleteProviderAsync(lease, provider.Id); _models.Remove(provider.Id); await _controller!.ReloadAsync(true); },
                    () => _providers.Any(current => current.Id == provider.Id && current.UsedBy.Count == 0), "Delete provider", "Delete " + provider.Name + "? Its saved API key will be discarded."));
            panel.Children.Add(Group(provider.Name, provider.Type + " · " + string.Join(", ", provider.Capabilities) + " · " + (provider.HasApiKey ? "API key saved" : "No API key") +
                (provider.UsedBy.Count == 0 ? " · Not in use" : " · Used by " + string.Join(", ", provider.UsedBy)), actions));
        }
        AddAction(panel, "Add provider", "AdminAiProviderAdd", Ready && _providerTypes.Count > 0, () => { BeginProviderAdd(null); return Task.CompletedTask; });
        _domain.Children.Add(panel);
        if (_route == AdminRoute.AiSettings) BuildChatGptAccounts();
    }
    private void BeginProviderAdd(string? target)
    {
        if (_providerDirty) { _domainError = "Save or cancel the current provider editor first."; if (_controller != null) Render(_controller.State); return; }
        ClearProviderEditor(); _providerTarget = target; BuildProviderEditor(null);
    }
    private void BeginProviderEdit(AdminAiProvider provider)
    {
        if (_providerDirty) { _domainError = "Save or cancel the current provider editor first."; if (_controller != null) Render(_controller.State); return; }
        ClearProviderEditor(); _editingProviderId = provider.Id; BuildProviderEditor(provider);
    }
    private void BuildProviderEditor(AdminAiProvider? editing)
    {
        _editorSyncing = true;
        var capability = _providerTarget switch { "ai.speech_provider_id" => "speech", "recommendations.embedding_provider_id" => "embeddings", _ => "chat" };
        var types = _providerTypes.Where(type => editing != null || type.Capabilities.Count == 0 || type.Capabilities.Contains(capability)).ToArray();
        _providerType = AdminSettingsUi.Select("Provider type", "AdminAiProviderType", types.Select(type => (type.Type, type.Label)));
        _providerName = AdminSettingsUi.Input("Provider name", "AdminAiProviderName"); _providerUrl = AdminSettingsUi.Input("Base URL", "AdminAiProviderUrl");
        _providerKey = AdminSettingsUi.Secret("API key", "AdminAiProviderKey");
        _providerKeyMode = AdminSettingsUi.Select("Saved API key", "AdminAiProviderKeyMode", [("keep", editing?.HasApiKey == true ? "Keep saved key" : "No saved key"), ("replace", "Use a new key"), ("clear", "Clear saved key")]);
        _providerKeyMode.SelectedIndex = editing?.HasApiKey == true ? 0 : 1;
        var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var (value, label) in new[] { ("chat", "Chat"), ("embeddings", "Embeddings"), ("speech", "Speech") })
        {
            var check = new CheckBox { Content = label }; _providerCapabilities[value] = check; caps.Children.Add(check);
            check.Checked += (_, _) => ProviderEdited(); check.Unchecked += (_, _) => ProviderEdited();
        }
        _providerType.SelectedItem = _providerType.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == editing?.Type) ?? _providerType.Items.OfType<ComboBoxItem>().FirstOrDefault();
        ApplyProviderType(editing);
        _providerType.SelectionChanged += (_, _) => { if (_editorSyncing) return; ApplyProviderType(null); ProviderEdited(); };
        _providerName.TextChanged += (_, _) => ProviderEdited(); _providerUrl.TextChanged += (_, _) => ProviderEdited();
        _providerKey.PasswordChanged += (_, _) => { if (_editorSyncing || _providerKey?.Password.Length == 0) return; if (_providerKeyMode != null) _providerKeyMode.SelectedItem = _providerKeyMode.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "replace"); ProviderEdited(); };
        _providerKeyMode.SelectionChanged += (_, _) => { if (_providerKey != null && (_providerKeyMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "replace") _providerKey.Password = ""; ProviderEdited(); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        AddEditorAction(actions, "Test connection", "AdminAiProviderTest", TestProviderAsync);
        AddEditorAction(actions, editing == null ? "Connect provider" : "Save provider", "AdminAiProviderSave", SaveProviderAsync);
        AddEditorAction(actions, "Cancel", "AdminAiProviderCancel", () => { ClearProviderEditor(); return Task.CompletedTask; });
        _providerEditorHost.Children.Add(Group(editing == null ? "Add provider" : "Edit provider", "Keep leaves the saved key unchanged. Clear removes it. Tests and saves clear the password immediately; re-enter a new key to send it again.",
            Field("Provider type", "", _providerType), Field("Name", "", _providerName), Field("Base URL", "", _providerUrl), Field("API key", "", _providerKey),
            Field("Saved key", "", _providerKeyMode), caps, actions));
        _editorSyncing = false; UpdateProviderEditorEnabled();
    }
    private void AddEditorAction(StackPanel panel, string label, string id, Func<Task> action)
    {
        var button = AdminSettingsUi.Action(label, id); button.Click += (_, _) => _interactionTask = InvokeInteractionAsync(action); panel.Children.Add(button); _providerEditorActions.Add(button);
    }
    private void ProviderEdited() { if (!_editorSyncing) _providerDirty = true; }
    private void ApplyProviderType(AdminAiProvider? editing)
    {
        var type = _providerTypes.FirstOrDefault(candidate => candidate.Type == (_providerType?.SelectedItem as ComboBoxItem)?.Tag?.ToString());
        if (type == null || _providerName == null || _providerUrl == null) return;
        _editorSyncing = true;
        _providerName.Text = editing?.Name ?? type.Label; _providerUrl.Text = editing?.BaseUrl ?? type.DefaultBaseUrl;
        var capability = _providerTarget switch { "ai.speech_provider_id" => "speech", "recommendations.embedding_provider_id" => "embeddings", _ => "chat" };
        foreach (var (name, control) in _providerCapabilities) { control.IsChecked = (editing?.Capabilities ?? (type.Capabilities.Count > 0 ? type.Capabilities : new[] { capability })).Contains(name); control.IsEnabled = type.Capabilities.Count == 0; }
        if (_providerKey != null) _providerKey.Password = "";
        _editorSyncing = false;
    }
    private AdminAiProviderDraft CaptureProviderDraft()
    {
        if (_providerType == null || _providerName == null || _providerUrl == null || _providerKey == null) throw new InvalidOperationException("No provider editor.");
        var secret = _providerKey.Password; _providerKey.Password = "";
        var keyMode = (_providerKeyMode?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var type = _providerTypes.FirstOrDefault(candidate => candidate.Type == (_providerType.SelectedItem as ComboBoxItem)?.Tag?.ToString()) ?? throw new InvalidOperationException("Choose a provider type.");
        var editing = _providers.FirstOrDefault(provider => provider.Id == _editingProviderId);
        if (!AdminIntegrationPolicy.IsProviderEndpoint(_providerUrl.Text.Trim()) || !Uri.TryCreate(_providerUrl.Text.Trim(), UriKind.Absolute, out var url)) throw new InvalidOperationException("Enter an HTTP or HTTPS provider URL without credentials, query parameters, or fragments.");
        if (string.IsNullOrWhiteSpace(_providerName.Text)) throw new InvalidOperationException("Enter a provider name.");
        if (keyMode == "replace" && secret == "" && (type.KeyRequired || editing?.HasApiKey == true)) throw new InvalidOperationException("Re-enter the new API key before testing or saving.");
        if (type.KeyRequired && secret == "" && editing?.HasApiKey != true) throw new InvalidOperationException("Enter the required API key.");
        if (keyMode == "keep" && editing?.HasApiKey == true && (!Uri.TryCreate(editing.BaseUrl, UriKind.Absolute, out var savedUrl) || savedUrl.Authority != url.Authority || savedUrl.Scheme != url.Scheme)) throw new InvalidOperationException("Re-enter the API key when changing the provider origin.");
        var capabilities = _providerCapabilities.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
        if (capabilities.Length == 0) throw new InvalidOperationException("Select at least one provider capability.");
        return new(type.Type, _providerName.Text.Trim(), url.AbsoluteUri.TrimEnd('/'), keyMode == "clear" ? "" : keyMode == "keep" ? null : secret, capabilities);
    }
    private async Task TestProviderAsync()
    {
        AdminAiProviderDraft? draft = null;
        try
        {
            draft = CaptureProviderDraft(); var id = _editingProviderId;
            await RunCommandAsync(async lease =>
            {
                var result = await _api.CheckProviderAsync(lease, draft, id);
                SetDomainStatus(lease, (result.Success ? "Connection succeeded. " : "Connection failed. ") + result.Message + (draft.ApiKey is { Length: > 0 } ? " Re-enter the API key before saving." : ""));
            });
        }
        catch (InvalidOperationException error) { if (_active && _controller != null) { _domainError = error.Message; Render(_controller.State); } }
        finally { draft = null; if (_providerKey != null) _providerKey.Password = ""; }
    }
    private async Task SaveProviderAsync()
    {
        AdminAiProviderDraft? draft = null;
        try
        {
            draft = CaptureProviderDraft(); var id = _editingProviderId; var target = _providerTarget;
            await RunCommandAsync(async lease =>
            {
                var provider = id == null ? await _api.CreateProviderAsync(lease, draft) : await _api.UpdateProviderAsync(lease, id, draft);
                if (!lease.IsCurrent || !_active) return;
                _models.Remove(provider.Id);
                _providers = _providers.Where(existing => existing.Id != provider.Id).Append(provider).ToArray();
                if (target != null)
                {
                    _controller!.Set(target, provider.Id);
                    if (target == "ai.text_provider_id") _controller.Set("ai.auth_mode", "api_key");
                    await CommitFieldAsync(target);
                }
                ClearProviderEditor(); SetDomainStatus(lease, target == null ? "Provider connected. Choose it for a job below." : "Provider connected and selected for this job.");
            });
        }
        catch (InvalidOperationException error) { if (_active && _controller != null) { _domainError = error.Message; Render(_controller.State); } }
        finally { draft = null; if (_providerKey != null) _providerKey.Password = ""; }
    }
    private void UpdateProviderEditorEnabled()
    {
        foreach (var action in _providerEditorActions) action.IsEnabled = Ready;
        if (_providerType != null) _providerType.IsEnabled = Ready && _editingProviderId == null;
        if (_providerName != null) _providerName.IsEnabled = Ready;
        if (_providerUrl != null) _providerUrl.IsEnabled = Ready;
        if (_providerKey != null) _providerKey.IsEnabled = Ready;
        if (_providerKeyMode != null) _providerKeyMode.IsEnabled = Ready;
        var type = _providerTypes.FirstOrDefault(candidate => candidate.Type == (_providerType?.SelectedItem as ComboBoxItem)?.Tag?.ToString());
        foreach (var check in _providerCapabilities.Values) check.IsEnabled = Ready && type?.Capabilities.Count == 0;
    }
    private void ClearProviderEditor()
    {
        _editorSyncing = true;
        if (_providerKey != null) _providerKey.Password = "";
        if (_providerName != null) _providerName.Text = "";
        if (_providerUrl != null) _providerUrl.Text = "";
        _providerEditorHost.Children.Clear(); _providerCapabilities.Clear(); _providerEditorActions.Clear();
        _providerKey = null; _providerName = null; _providerUrl = null; _providerType = null; _providerKeyMode = null;
        _editingProviderId = null; _providerTarget = null; _providerDirty = false; _editorSyncing = false;
    }
}
