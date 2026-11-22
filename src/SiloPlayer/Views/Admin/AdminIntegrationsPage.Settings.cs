using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminIntegrationsPage
{
    private readonly HashSet<string> _fieldCommits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _modelChecks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComboBox> _modelPickers = new(StringComparer.Ordinal);
    // What a choice, model or schedule shows while nothing is saved. The AI tuning numbers (jobs at once, lines per request, surrounding lines,
    // audio per request, transcriptions per account) have no entry: a number field shows what it holds, empty included, because the settings
    // snapshot already carries every default (official #4816).
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["ai.auth_mode"] = "api_key", ["ai.chat_model"] = "gpt-6-luna", ["ai.asr_model"] = "whisper-1",
        ["subtitle_ai.transcribe_quota_period"] = "month",
        ["metadata_ai.on_view"] = "off", ["recommendations.embeddings_cron"] = "0 3 * * *", ["recommendations.taste_profiles_cron"] = "0 4 * * *",
        ["recommendations.cowatch_cron"] = "30 4 * * *", ["recommendations.recommendations_cron"] = "0 5 * * *",
        ["recommendations.taste_decay_half_life_days"] = "180", ["recommendations.diversity_lambda"] = "0.7"
    };
    private string Value(string key, string fallback = "")
    {
        var state = _controller?.State;
        var value = state?.Values.GetValueOrDefault(key, "") ?? "";
        return value.Length > 0 || state?.DirtyKeys.Contains(key) == true ? value : fallback.Length > 0 ? fallback : Defaults.GetValueOrDefault(key, "");
    }
    private FrameworkElement BuildSettings()
    {
        var host = new StackPanel { Spacing = 16 };
        if (_route == AdminRoute.AiSettings)
        {
            host.Children.Add(Group("Text model", "Choose the provider and model used for translation and metadata.",
                ProviderField("ai.text_provider_id", "Text provider"), ModelField("ai.chat_model", "Text model"),
                Select("ai.reasoning_effort", "Reasoning level", [("", "Default")]),
                Select("ai.service_tier", "Service tier", [("", "Default")]),
                ActionRow("Test text model", "AdminAiTextCheck", () => CheckModelAsync("ai_chat", AdminIntegrationsSettings.TextKeys))));
            host.Children.Add(Group("Speech to text", "Connect a speech-capable provider before enabling transcription.",
                ProviderField("ai.speech_provider_id", "Speech provider"), ModelField("ai.asr_model", "Speech model"),
                ActionRow("Test speech model", "AdminAiSpeechCheck", () => CheckModelAsync("ai_transcription", AdminIntegrationsSettings.SpeechKeys))));
            host.Children.Add(Group("Features", "A model must be ready before its feature can be enabled. Existing features can always be turned off.",
                Toggle("subtitle_ai.enabled", "Translate subtitles", "Use the text model to translate subtitle files"),
                Toggle("subtitle_ai.transcribe_enabled", "Create subtitles from audio", "Use the speech model to create subtitle files"),
                Toggle("metadata_ai.enabled", "Translate descriptions", "Use AI translation for metadata"),
                Select("metadata_ai.on_view", "Description translation for viewers", [("off", "Off"), ("button", "Button"), ("auto", "Automatic")])));
            host.Children.Add(Group("Advanced", "Job controls and per-account transcription allowance.",
                Text("ai.max_concurrent_jobs", "Jobs running at once", "Maximum simultaneous AI jobs", true), Text("subtitle_ai.batch_size", "Subtitle lines per request", "Lines in one translation request", true),
                Text("subtitle_ai.context_neighbors", "Surrounding lines", "Context lines sent with each subtitle", true), Text("subtitle_ai.asr_chunk_seconds", "Audio per request", "60 to 600 seconds", true),
                Text("subtitle_ai.transcribe_quota_jobs", "Transcriptions per account", "Zero means unlimited", true),
                Select("subtitle_ai.transcribe_quota_period", "Allowance resets", [("day", "Daily"), ("week", "Weekly"), ("month", "Monthly")])));
        }
        else if (_route == AdminRoute.Recommendations)
        {
            host.Children.Add(Group("General", "Changes are committed individually when you leave a field.", Toggle("recommendations.enabled", "Enable recommendations", "Build personalized rows for profiles")));
            host.Children.Add(Group("Embeddings", "The stored model and dimensions are locked once embeddings exist. Reset before changing the embedding space.",
                ProviderField("recommendations.embedding_provider_id", "Embedding provider"), ModelField("recommendations.embedding_model", "Embedding model"),
                ActionRow("Test connection", "AdminRecommendationCheck", () => CheckModelAsync("recommendations_embedding", AdminIntegrationsSettings.EmbeddingKeys))));
            host.Children.Add(Group("Schedule", "Standard five-field cron expressions.",
                Text("recommendations.embeddings_cron", "Embeddings", "Default: 0 3 * * *"), Text("recommendations.taste_profiles_cron", "Taste profiles", "Default: 0 4 * * *"),
                Text("recommendations.cowatch_cron", "Co-watch", "Default: 30 4 * * *"), Text("recommendations.recommendations_cron", "Recommendations", "Default: 0 5 * * *")));
            host.Children.Add(Group("Advanced", "Ranking policy applied to every recommendation row.", Text("recommendations.taste_decay_half_life_days", "Taste half-life", "Days before a signal halves", true), Text("recommendations.diversity_lambda", "Diversity", "Maximum marginal relevance weight")));
        }
        else
        {
            host.Children.Add(Group("Jellyfin", "Allow Jellyfin-compatible clients to discover and sign in to Silo.",
                Toggle("jellyfin_compat.enabled", "Allow Jellyfin clients to connect", "Runs the Jellyfin compatibility proxy"),
                Text("jellyfin_compat.public_url", "Jellyfin server address", "Public address advertised to Jellyfin clients"), Text("jellyfin_compat.server_name", "Server name", "Name shown in Jellyfin clients"),
                Text("jellyfin_compat.emulated_server_version", "Jellyfin version to report", "Version emulated for Jellyfin clients"),
                Toggle("jellyfin_compat.web_enabled", "Jellyfin web player", "Requires the proxy and an installed compatible web player")));
            host.Children.Add(Group("Jellyfin advanced", "Session behavior and managed web player paths.",
                ReadOnly("jellyfin_compat.server_id", "Server ID", "Read-only. Changing it makes every Jellyfin app see a new server."),
                Text("jellyfin_compat.session_ttl", "Stay signed in for", "Compatibility login session lifetime"), Text("jellyfin_compat.playback_session_ttl", "End playback sessions after", "Inactive compatibility playback lifetime"),
                // Official switch (server #4466), off by default. It allows playback without sign-in, so only the admin's own action on this switch changes it.
                Toggle(AdminIntegrationsSettings.AddressStreamGrant, AdminText.T("web.admin.settings.compatibilityProxies.advanced.addressStreamGrant"),
                    AdminText.T("web.admin.settings.compatibilityProxies.advanced.addressStreamGrantDescription")),
                Text("jellyfin_compat.web_version", "Web player version to install", "Leave empty for the pinned compatible release"),
                Text("jellyfin_compat.web_install_dir", "Web player install folder", "Root folder for managed web files"), ReadOnly("jellyfin_compat.web_dir", "Active web directory", "Managed by the Jellyfin Web installer.")));
            host.Children.Add(Group("Audiobookshelf", "Beta compatibility for Audiobookshelf apps.", Toggle("audiobookshelf_compat.enabled", "Allow Audiobookshelf apps to connect", "This compatibility surface is beta.")));
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        _discard = AdminSettingsUi.Action("Discard", "AdminIntegrationsDiscard"); _discard.Click += (_, _) => { _controller?.Discard(); ClearProviderEditor(); };
        _save = AdminSettingsUi.Action("Save changes", "AdminIntegrationsSave", primary: true); _save.Click += (_, _) => _saveTask = SaveSettingsAsync();
        actions.Children.Add(_discard); actions.Children.Add(_save); host.Children.Add(actions); return host;
    }

    private async Task SaveSettingsAsync()
    {
        var controller = _controller; if (!Ready || controller == null) return;
        var error = _route == AdminRoute.AiSettings ? AdminIntegrationPolicy.ValidateAi(controller.State, _providers, _capabilities, _connection, _accountModels) : null;
        if (error != null) { _domainError = error; Render(controller.State); return; }
        await controller.SaveAsync();
    }
    private Task CommitFieldAsync(string key)
    {
        if (!_active || _route != AdminRoute.Recommendations || _controller?.State.DirtyKeys.Contains(key) != true) return Task.CompletedTask;
        _fieldCommits.Add(key);
        if (!_fieldCommitTask.IsCompleted) return _fieldCommitTask;
        return _fieldCommitTask = CommitFieldsCoreAsync();
    }
    private async Task CommitFieldsCoreAsync()
    {
        var controller = _controller;
        while (controller != null && Current(controller) && _fieldCommits.Count > 0)
        {
            if (controller.State is { Conflict: true } or { OutcomeUnknown: true } || _commandUnknown || _busy) return;
            var keys = _fieldCommits.ToArray(); _fieldCommits.Clear();
            await controller.SaveFieldsAsync(keys);
            if (controller.State.Error != null) return;
        }
    }
    private async Task CheckModelAsync(string kind, IReadOnlyList<string> keys) => await RunCommandAsync(async lease =>
    {
        var state = _controller!.State;
        var values = keys.ToDictionary(key => key, key => Value(key));
        var result = await _api.CheckModelAsync(lease, kind, values, state.DirtyKeys.Where(keys.Contains).ToArray());
        SetDomainStatus(lease, (result.Success ? "Connection succeeded. " : "Connection failed. ") + result.Message);
    });

    private bool FeatureReady(string key) => key switch
    {
        "subtitle_ai.enabled" or "metadata_ai.enabled" => TextReady,
        "subtitle_ai.transcribe_enabled" => AdminIntegrationPolicy.ProviderReady(_providers, Value("ai.speech_provider_id"), "speech", Value("ai.asr_model")),
        "jellyfin_compat.web_enabled" => Value("jellyfin_compat.enabled") == "true" && _jellyfinWeb is { Installed: true, OperationRunning: false },
        _ => true
    };
    private bool TextReady => Value("ai.auth_mode") == "chatgpt" ? _connection?.Active is { Ready: true } && _accountModels.Any(model => model.Id == Value("ai.chat_model"))
        : AdminIntegrationPolicy.ProviderReady(_providers, Value("ai.text_provider_id"), "chat", Value("ai.chat_model"));

    private FrameworkElement ProviderField(string key, string label)
    {
        var combo = AdminSettingsUi.Select(label, "AdminIntegration-" + key, []); _fields[key] = combo;
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || _controller == null || combo.SelectedItem is not ComboBoxItem item) return;
            var id = item.Tag?.ToString() ?? "";
            if (id == "__add") { BeginProviderAdd(key); Render(_controller.State); return; }
            if (key == "ai.text_provider_id")
            {
                _controller.Set("ai.auth_mode", id == "__chatgpt" ? "chatgpt" : "api_key");
                if (id != "__chatgpt") _controller.Set(key, id);
            }
            else _controller.Set(key, id);
            RequestModelRefresh();
            _ = CommitFieldAsync(key);
        };
        return Field(key, label, "Choose an existing provider or add one", combo);
    }
    private FrameworkElement ModelField(string key, string label)
    {
        var input = AdminSettingsUi.Input(label, "AdminIntegration-" + key); _fields[key] = input;
        var combo = AdminSettingsUi.Select("Suggested " + label, "AdminIntegration-" + key + "-Models", []); combo.Width = 160; combo.PlaceholderText = "Choose model"; _modelPickers[key] = combo;
        input.TextChanged += (_, _) =>
        {
            // TextChanged also arrives later for programmatic updates, including a displayed fallback; ignore that echo.
            if (_syncing || _controller == null || !ReferenceEquals(_fields.GetValueOrDefault(key), input) || input.Text == Value(key)) return;
            _controller.Set(key, input.Text);
            if (key == "ai.chat_model") { _modelDebounce.Stop(); _modelDebounce.Start(); }
        };
        input.LostFocus += (_, _) => { _ = CommitFieldAsync(key); };
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || _controller == null || combo.SelectedItem is not ComboBoxItem item) return;
            _controller.Set(key, item.Tag?.ToString() ?? "");
            if (key == "ai.chat_model") { _modelDebounce.Stop(); RequestModelRefresh(); }
            _ = CommitFieldAsync(key);
        };
        return Field(key, label, "Choose a suggestion or enter a model name. ChatGPT requires an available account model.", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { input, combo } });
    }
    private void UpdateChoices()
    {
        foreach (var (key, capability) in new[] { ("ai.text_provider_id", "chat"), ("ai.speech_provider_id", "speech"), ("recommendations.embedding_provider_id", "embeddings") })
        {
            if (!_fields.TryGetValue(key, out var element) || element is not ComboBox combo) continue;
            var choices = new List<(string, string)> { ("", "None") };
            choices.AddRange(_providers.Where(provider => provider.Capabilities.Contains(capability)).Select(provider => (provider.Id, provider.Name)));
            if (key == "ai.text_provider_id" && (_capabilities?.ChatGptSignIn == true || Value("ai.auth_mode") == "chatgpt")) choices.Add(("__chatgpt", "ChatGPT plan"));
            var current = Value(key);
            if (current != "" && !choices.Any(item => item.Item1 == current)) choices.Add((current, "Unavailable provider (choose another)"));
            choices.Add(("__add", "Add provider…")); SetChoices(combo, choices);
        }
        if (_fields.GetValueOrDefault("ai.reasoning_effort") is ComboBox reasoning)
        {
            var levels = new List<(string, string)> { ("", "Default") }; levels.AddRange((_capabilities?.ReasoningLevels ?? []).Select(level => (level, level)));
            var value = Value("ai.reasoning_effort"); if (value != "" && !levels.Any(choice => choice.Item1 == value)) levels.Add((value, value + " (unsupported)"));
            SetChoices(reasoning, levels);
        }
        if (_fields.GetValueOrDefault("ai.service_tier") is ComboBox service)
            SetChoices(service, new[] { ("", "Default") }.Concat((_capabilities?.ServiceTiers ?? []).Where(tier => tier is "priority" or "ultrafast").Select(tier => (tier, tier))));
        foreach (var (key, providerKey, kind) in new[] { ("ai.chat_model", "ai.text_provider_id", "chat"), ("ai.asr_model", "ai.speech_provider_id", "speech"), ("recommendations.embedding_model", "recommendations.embedding_provider_id", "embeddings") })
        {
            if (!_modelPickers.TryGetValue(key, out var combo)) continue;
            var choices = key == "ai.chat_model" && Value("ai.auth_mode") == "chatgpt" ? _accountModels.Select(model => (model.Id, model.Name))
                : (_models.GetValueOrDefault(Value(providerKey)) ?? []).Where(model => model.Kind == kind).Select(model => (model.Id, model.Id));
            SetChoices(combo, choices);
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == Value(key));
        }
    }
    private static void SetChoices(ComboBox combo, IEnumerable<(string Value, string Label)> choices)
    {
        var items = choices.DistinctBy(choice => choice.Value).ToArray();
        if (combo.Items.OfType<ComboBoxItem>().Select(item => (item.Tag?.ToString(), item.Content?.ToString())).SequenceEqual(items.Select(item => ((string?)item.Value, (string?)item.Label)))) return;
        combo.Items.Clear(); foreach (var (value, label) in items) combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
    }
    private FrameworkElement Text(string key, string label, string description, bool number = false)
    {
        var control = AdminSettingsUi.Input(label, "AdminIntegration-" + key, number: number); _fields[key] = control;
        // TextChanged also arrives later for programmatic updates, including a displayed fallback; ignore that echo.
        control.TextChanged += (_, _) => { if (!_syncing && ReferenceEquals(_fields.GetValueOrDefault(key), control) && control.Text != Value(key)) _controller?.Set(key, control.Text); };
        control.LostFocus += (_, _) => { _ = CommitFieldAsync(key); };
        return Field(key, label, description, control);
    }
    private FrameworkElement ReadOnly(string key, string label, string description)
    {
        var control = AdminSettingsUi.Text("", 13, true); control.MaxWidth = 280; control.IsTextSelectionEnabled = true; _fields[key] = control;
        AutomationProperties.SetAutomationId(control, "AdminIntegration-" + key); AutomationProperties.SetName(control, label);
        return Field(key, label, description, control);
    }
    private FrameworkElement Select(string key, string label, IEnumerable<(string Value, string Label)> choices)
    {
        var control = AdminSettingsUi.Select(label, "AdminIntegration-" + key, choices); _fields[key] = control;
        control.SelectionChanged += (_, _) => { if (!_syncing && control.SelectedItem is ComboBoxItem item) { _controller?.Set(key, item.Tag?.ToString() ?? ""); _ = CommitFieldAsync(key); } };
        return Field(key, label, "", control);
    }
    private FrameworkElement Toggle(string key, string label, string description)
    {
        var control = new ToggleSwitch { OffContent = "Off", OnContent = "On", HorizontalAlignment = HorizontalAlignment.Right }; _fields[key] = control;
        AutomationProperties.SetAutomationId(control, "AdminIntegration-" + key); AutomationProperties.SetName(control, label);
        control.Toggled += (_, _) =>
        {
            // A ToggleSwitch reports its state again after it is realized, also while the control is locked: only a change the admin made
            // (an enabled switch whose state differs from the value shown) is a choice.
            if (_syncing || !control.IsEnabled || !ReferenceEquals(_fields.GetValueOrDefault(key), control)) return;
            var wanted = control.IsOn ? "true" : "false";
            if (wanted == Value(key)) return;
            if (key == "jellyfin_compat.enabled") _controller?.SetCompatibilityApi(control.IsOn);
            else _controller?.Set(key, wanted);
            _ = CommitFieldAsync(key);
        };
        return Field(key, label, description, control);
    }
    private readonly Dictionary<string, TextBlock> _environmentNotes = new(StringComparer.Ordinal);
    /// <summary>A row for something that is not a server setting (provider editor, account picker): no environment note.</summary>
    private FrameworkElement Field(string label, string description, FrameworkElement control) => Field(null, label, description, control);
    private FrameworkElement Field(string? key, string label, string description, FrameworkElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 14, 0, 0), ColumnSpacing = 20 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 2 }; copy.Children.Add(AdminSettingsUi.Text(label)); if (description != "") copy.Children.Add(AdminSettingsUi.Text(description, 12, true));
        // The official note for a setting the server's environment sets; collapsed until the status names the key.
        var note = AdminSettingsUi.Text("", 12, true); note.Visibility = Visibility.Collapsed; copy.Children.Add(note); if (key != null) _environmentNotes[key] = note;
        row.Children.Add(copy); Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }
    private static Border Group(string title, string description, params FrameworkElement[] children)
    {
        var stack = new StackPanel { Spacing = 6 }; var heading = AdminSettingsUi.Text(title, 17); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        stack.Children.Add(heading); if (description != "") stack.Children.Add(AdminSettingsUi.Text(description, 13, true)); foreach (var child in children) stack.Children.Add(child);
        return new Border { Child = stack, Padding = new Thickness(20), CornerRadius = new CornerRadius(16), Background = AdminMonitorUi.Solid(AdminMonitorUi.Surface, .55), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .55), BorderThickness = new Thickness(1) };
    }
    private FrameworkElement ActionRow(string label, string id, Func<Task> action)
    {
        var button = AdminSettingsUi.Action(label, id); button.Click += (_, _) => _interactionTask = InvokeInteractionAsync(action);
        var kind = id == "AdminAiTextCheck" ? "ai_chat" : id == "AdminAiSpeechCheck" ? "ai_transcription" : "recommendations_embedding";
        _modelChecks[kind] = button; button.IsEnabled = false; return button;
    }
    private void AddAction(StackPanel panel, string label, string id, bool enabled, Func<Task> action)
    {
        var button = AdminSettingsUi.Action(label, id); button.IsEnabled = enabled;
        button.Click += (_, _) => _interactionTask = InvokeInteractionAsync(action); panel.Children.Add(button);
    }
    private static async Task InvokeInteractionAsync(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { } }
}
