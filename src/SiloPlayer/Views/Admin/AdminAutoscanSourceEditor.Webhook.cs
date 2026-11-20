using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using P = SiloPlayer.Core.Services.Admin.AdminAutoscanPresentation;

namespace SiloPlayer.Views.Admin;

internal sealed partial class AdminAutoscanSourceEditor
{
    private PasswordBox _endpoint = new();
    private Button? _copyEndpoint, _generateEndpoint, _rotateEndpoint;
    private TextBlock? _endpointStatus;
    private StackPanel? _providerHost, _webhookInstructions;
    private FrameworkElement WebhookSection()
    {
        var section = new StackPanel { Spacing = 12 };
        _endpoint = new PasswordBox { PasswordRevealMode = PasswordRevealMode.Hidden, IsEnabled = false };
        AutomationProperties.SetAutomationId(_endpoint, "AutoscanWebhookUrl"); AutomationProperties.SetName(_endpoint, T("webhook.endpoint.fieldLabel"));
        // Keep the credential-bearing URL masked; only the explicit Copy action exports it.
        section.Children.Add(Field(T("webhook.endpoint.label"), _endpoint));
        _endpointStatus = U.Text("", 12, true); section.Children.Add(_endpointStatus);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _copyEndpoint = U.Action(T("webhook.copyButton"), "AutoscanWebhookCopy"); _copyEndpoint.Click += (_, _) => { if (Current && SourceId() is { } id) _page.CopyWebhook(id, _owner); };
        _generateEndpoint = U.Action(T("webhook.endpoint.generate"), "AutoscanWebhookGenerate");
        _generateEndpoint.Click += async (_, _) =>
        {
            if (!Current || Busy || SourceId() is not { } id) return;
            _saving = true; SyncState();
            try { await _controller.ConfigureWebhookAsync(id, false, _owner); }
            finally { _saving = false; if (Current) SyncState(); }
        };
        _rotateEndpoint = U.Action(T("webhook.endpoint.rotate"), "AutoscanWebhookRotate"); _rotateEndpoint.Click += async (_, _) => { if (Current && SourceId() is { } id) await _page.RotateAsync(id, _owner); };
        actions.Children.Add(_copyEndpoint); actions.Children.Add(_generateEndpoint); actions.Children.Add(_rotateEndpoint); section.Children.Add(actions);
        var reload = U.Action("Reload endpoint state", "AutoscanWebhookReload"); reload.Click += async (_, _) => { if (Current) { await _controller.ReloadAsync(); SyncWebhook(); } }; section.Children.Add(reload);
        section.Children.Add(U.Text(T("webhook.endpoint.alreadySetUp"), 12, true));
        _providerHost = new StackPanel { Spacing = 8 }; section.Children.Add(_providerHost);
        _webhookInstructions = new StackPanel { Spacing = 12 }; section.Children.Add(_webhookInstructions);
        RenderWebhookInstructions(); SyncWebhook(); return section;
    }
    private void RenderWebhookInstructions()
    {
        if (_webhookInstructions == null) return;
        var section = _webhookInstructions; section.Children.Clear();
        var provider = _draft == null ? "auto" : ProviderConnectionConfig.Scalar(_draft.Config.GetValueOrDefault("webhook_provider"));
        if (provider is not ("auto" or "sonarr" or "radarr")) provider = _draft == null ? "auto" : P.Provider(_draft.Descriptor);
        section.Children.Add(U.Text(T("webhook.instructions.copyStep"), 14));
        section.Children.Add(U.Text(T("webhook.instructions.settingsStep"), 14));
        section.Children.Add(U.Text(P.ProviderName(provider) + " → Settings → Connect → + → Webhook", 13));
        section.Children.Add(U.Text(T("webhook.instructions.pasteURL"), 12, true));
        section.Children.Add(U.Text(T("webhook.instructions.triggersStep"), 14));
        foreach (var (label, reason, required) in Triggers(provider))
        {
            section.Children.Add(U.Text(required ? label : T("webhook.instructions.optionalTrigger", ("trigger", label)), 13));
            section.Children.Add(U.Text(T("webhook.trigger." + reason), 12, true));
        }
        section.Children.Add(U.Text(T("webhook.instructions.otherTriggers"), 12, true)); section.Children.Add(U.Text(T("webhook.instructions.testDelivery"), 12, true));
    }
    private string? SourceId() => _baseline?.Id ?? _createdId;
    private void SyncWebhook()
    {
        if (_endpointStatus == null || !Current || SourceId() is not { } id) return;
        var state = _controller.State; var source = state.Sources.FirstOrDefault(s => s.Id == id);
        var url = _controller.SafeWebhookUrl(id, _owner);
        _endpoint.Password = url ?? "";
        var blocked = Busy || state.Busy || state.Loading || state.Stale;
        _copyEndpoint!.IsEnabled = !blocked && url != null;
        _generateEndpoint!.Visibility = source?.WebhookConfigured == true ? Visibility.Collapsed : Visibility.Visible;
        _generateEndpoint.IsEnabled = !blocked && source != null;
        _rotateEndpoint!.Visibility = source?.WebhookConfigured == true ? Visibility.Visible : Visibility.Collapsed;
        _rotateEndpoint.IsEnabled = !blocked;
        _endpointStatus.Text = blocked ? T("webhook.endpoint.reloadBeforeUse") : url != null ? "URL ready to copy. It contains a secret and stays masked here." : source?.WebhookConfigured == true ? T("webhook.endpoint.unavailable", ("suffix", source.WebhookSecretSuffix ?? "")) : T("sourceDialog.webhookFailedBody");
    }
    private static IEnumerable<(string Label, string Reason, bool Required)> Triggers(string provider)
    {
        yield return ("On File Import", "fileImport", true); yield return ("On File Upgrade", "fileUpgrade", true); yield return ("On Rename", "rename", false);
        if (provider == "sonarr") { yield return ("On Episode File Delete", "episodeFileDelete", false); yield return ("On Series Delete", "seriesDelete", false); }
        else if (provider == "radarr") { yield return ("On Movie File Delete", "movieFileDelete", false); yield return ("On Movie Delete", "movieDelete", false); }
        else { yield return ("On Episode File Delete / On Movie File Delete", "anyFileDelete", false); yield return ("On Series Delete / On Movie Delete", "anyTitleDelete", false); }
    }
}
