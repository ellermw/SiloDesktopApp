using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminIntegrationsPage
{
    private readonly List<UIElement> _domainParts = [];
    private string _domainShape = "";

    private void BuildDomain()
    {
        if (!_active) return;
        _domainParts.Clear();
        if (_route == AdminRoute.AiSettings) BuildProviders();
        else if (_route == AdminRoute.Recommendations) { BuildRecommendationJobs(); BuildProviders(); }
        else BuildJellyfinWeb();
        var parts = _domainParts.ToArray(); _domainParts.Clear();
        var fresh = parts.SelectMany(part => DomainNodes(part)).ToArray();
        var shape = DomainShape(fresh);
        // Polls and settings edits come here constantly: keep the live controls, with their focus and expanded state, unless what they show changed.
        if (shape == _domainShape && _domain.Children.Count == parts.Length) return;
        var live = _domain.Children.SelectMany(part => DomainNodes(part)).ToArray();
        var expanded = live.OfType<Expander>().Where(open => open.IsExpanded).Select(open => open.Header?.ToString()).ToHashSet();
        var focused = XamlRoot == null ? null : Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var focusState = focused is Control { FocusState: not FocusState.Unfocused } origin ? origin.FocusState : FocusState.Programmatic;
        while (focused != null && !(focused is Control && Array.IndexOf(live, focused) >= 0)) focused = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(focused);
        var focusId = focused == null ? "" : AutomationProperties.GetAutomationId(focused);
        _domain.Children.Clear(); foreach (var part in parts) _domain.Children.Add(part);
        _domainShape = shape;
        foreach (var expander in fresh.OfType<Expander>()) if (expanded.Contains(expander.Header?.ToString())) expander.IsExpanded = true;
        if (focusId != "" && fresh.OfType<Control>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == focusId) is { IsEnabled: true } target)
        {
            RoutedEventHandler? loaded = null;
            loaded = (_, _) => { target.Loaded -= loaded; target.Focus(focusState); };
            target.Loaded += loaded;
        }
    }
    /// <summary>A built domain tree in document order. It is walked by ownership because a fresh tree is not loaded yet.</summary>
    private static IEnumerable<object> DomainNodes(object? node)
    {
        if (node == null) yield break;
        yield return node;
        if (node is Panel panel) { foreach (var child in panel.Children) foreach (var inner in DomainNodes(child)) yield return inner; }
        else if (node is Border border) { foreach (var inner in DomainNodes(border.Child)) yield return inner; }
        else if (node is ItemsControl list) { foreach (var item in list.Items) foreach (var inner in DomainNodes(item)) yield return inner; }
        else if (node is ContentControl content)
        {
            if (content is Expander expander) foreach (var inner in DomainNodes(expander.Header)) yield return inner;
            foreach (var inner in DomainNodes(content.Content)) yield return inner;
        }
    }
    /// <summary>Everything the domain panel shows: structure, identity, text, enablement, selection and progress.</summary>
    private static string DomainShape(IEnumerable<object> nodes)
    {
        var shape = new System.Text.StringBuilder();
        foreach (var node in nodes)
        {
            shape.Append(node.GetType().Name).Append('\u001f');
            if (node is string label) shape.Append(label);
            if (node is FrameworkElement element) shape.Append(AutomationProperties.GetAutomationId(element)).Append('\u001f').Append(element.Tag?.ToString());
            if (node is Control control) shape.Append(control.IsEnabled ? "\u001f1" : "\u001f0");
            if (node is TextBlock text) shape.Append('\u001f').Append(text.Text);
            else if (node is ProgressBar progress) shape.Append('\u001f').Append(progress.IsIndeterminate ? "?" : progress.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if (node is ComboBox choice) shape.Append('\u001f').Append((choice.SelectedItem as ComboBoxItem)?.Tag?.ToString());
            shape.Append('\u001e');
        }
        return shape.ToString();
    }
    private void BuildRecommendationJobs()
    {
        var status = _recommendations;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(AdminSettingsUi.Title("Recommendation jobs"));
        if (status?.LockConflict is { Length: > 0 }) panel.Children.Add(AdminSettingsUi.Alert("Embedding configuration needs review", status.LockConflict));
        foreach (var (label, kind, job) in new[]
        {
            ("Embeddings", RecommendationJobKind.Embeddings, status?.Embeddings), ("Taste profiles", RecommendationJobKind.TasteProfiles, status?.TasteProfiles),
            ("Co-watch", RecommendationJobKind.Cowatch, status?.Cowatch), ("Recommendations", RecommendationJobKind.Recommendations, status?.Recommendations)
        })
        {
            var contents = new StackPanel { Spacing = 6 };
            contents.Children.Add(AdminSettingsUi.Text(job == null ? "Loading status…" : (job.Running ? "Running · " : "") + job.Count.ToString("N0") + (job.Total is { } total ? " / " + total.ToString("N0") : " records"), 14));
            AddRun(contents, "Last full run", job?.LastRun);
            if (job?.LastPartialRun != null) AddRun(contents, kind == RecommendationJobKind.Embeddings ? "Last catch-up pass" : "Last global-row build", job.LastPartialRun);
            if (kind == RecommendationJobKind.TasteProfiles)
            {
                contents.Children.Add(AdminSettingsUi.Text($"Stale profiles waiting for a rebuild: {status?.StaleSweepPending ?? 0:N0}", 13, true));
                AddRun(contents, "Last stale-profile sweep", status?.StaleSweepLastRun);
            }
            if (kind == RecommendationJobKind.Recommendations) contents.Children.Add(AdminSettingsUi.Text("Cache refreshed: " + (status?.CacheRefreshedAt ?? "Not yet") + ". Cache freshness is not job health.", 13, true));
            AddAction(contents, job?.Running == true ? "Running" : "Run now", "AdminRecommendationRun-" + kind, Ready && CanTrigger(kind),
                () => RunCommandAsync(async lease => { await _api.TriggerRecommendationJobAsync(lease, kind); SetDomainStatus(lease, "Job started on this server. This is not a durable job receipt."); }, () => CanTrigger(kind)));
            panel.Children.Add(Group(label, "", contents));
        }
        var rawLock = _controller?.State.PersistedValues.GetValueOrDefault("recommendations.embedding_lock", "") ?? "";
        if (rawLock.Length is > 0 and <= 8192)
        {
            try
            {
                using var document = JsonDocument.Parse(rawLock);
                var root = document.RootElement;
                var model = root.GetProperty("model").GetString(); var source = root.GetProperty("source_dimensions").GetInt32();
                var stored = root.TryGetProperty("storage_dimensions", out var dimensions) ? dimensions.GetInt32() : 3072;
                panel.Children.Add(Group("Embedding lock", "The provider model and dimensions must match this stored embedding space. Reset embeddings before changing it.",
                    AdminSettingsUi.Text($"Model: {LocalLog.RedactDiagnosticText(model ?? "")} · Source dimensions: {source} · Storage dimensions: {stored}", 13)));
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { panel.Children.Add(AdminSettingsUi.Alert("The embedding lock could not be read. Reload before changing embedding settings.")); }
        }
        AddAction(panel, "Reset embeddings", "AdminRecommendationReset", Ready && AdminIntegrationPolicy.CanReset(status),
            () => RunCommandAsync(async lease =>
            {
                var deleted = await _api.ResetEmbeddingsAsync(lease);
                if (!_active || !lease.IsCurrent) return;
                await _controller!.ReloadAsync(true);
                SetDomainStatus(lease, $"Deleted {deleted.Embeddings:N0} embeddings, cleared {deleted.TasteProfiles:N0} taste profiles, deleted {deleted.TasteClusters:N0} taste clusters and {deleted.CachedRows:N0} profile cache rows. Global rows are kept.");
            }, () => AdminIntegrationPolicy.CanReset(_recommendations), "Reset embeddings", "Delete item embeddings and taste clusters, clear taste profile vectors, and remove per-profile cached recommendations? Global rows are kept. Rebuild embeddings afterward."));
        _domainParts.Add(panel);
    }
    private bool CanTrigger(RecommendationJobKind kind)
    {
        var status = _recommendations; if (status == null) return false;
        var job = kind switch { RecommendationJobKind.Embeddings => status.Embeddings, RecommendationJobKind.TasteProfiles => status.TasteProfiles, RecommendationJobKind.Cowatch => status.Cowatch, _ => status.Recommendations };
        if (job.Running || (kind != RecommendationJobKind.Cowatch && status.LockConflict != "")) return false;
        return kind != RecommendationJobKind.Embeddings || AdminIntegrationPolicy.ProviderReady(_providers,
            _controller?.State.PersistedValues.GetValueOrDefault("recommendations.embedding_provider_id", "") ?? "", "embeddings",
            _controller?.State.PersistedValues.GetValueOrDefault("recommendations.embedding_model", "") ?? "");
    }
    private static void AddRun(StackPanel panel, string label, AdminRecommendationRun? run)
    {
        if (run == null) { panel.Children.Add(AdminSettingsUi.Text(label + ": Not yet", 12, true)); return; }
        string Time(string? value) => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var instant) ? DateTimeDisplay.FormatDateTime(instant) : "Unknown";
        panel.Children.Add(AdminSettingsUi.Text(label + ": " + run.Status + " · Started " + Time(run.StartedAt) + " · Finished " + Time(run.CompletedAt), 12, true));
        if (run.Counts.Count > 0) panel.Children.Add(AdminSettingsUi.Text(string.Join(" · ", run.Counts.Select(count => count.Key + ": " + count.Value.ToString("N0"))), 12, true));
        if (run.Error != "") panel.Children.Add(AdminSettingsUi.Alert("Last run failed", run.Error));
    }
    private void BuildJellyfinWeb()
    {
        var status = _jellyfinWeb;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(AdminSettingsUi.Title("Jellyfin Web"));
        panel.Children.Add(AdminSettingsUi.Text(status == null ? "Loading managed web player status…" :
            $"Jellyfin API: {status.ApiState} · Web player: {status.WebState}" + (status.Installed ? " · Installed " + status.Version : " · Not installed"), 14));
        if (status != null)
        {
            panel.Children.Add(AdminSettingsUi.Text("Listen: " + status.Listen + " · Public address: " + status.PublicUrl + " · Pinned version: " + status.PinnedVersion, 13, true));
            if (status.OperationRunning)
            {
                panel.Children.Add(AdminSettingsUi.Text((status.Operation ?? "Operation") + " · " + status.Phase + " · " + status.OperationMessage, 13));
                var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = status.Progress ?? 0, IsIndeterminate = status.Progress == null };
                AutomationProperties.SetAutomationId(progress, "AdminJellyfinWebProgress"); AutomationProperties.SetName(progress, "Jellyfin Web operation progress"); panel.Children.Add(progress);
            }
            if (!string.IsNullOrEmpty(status.Error)) panel.Children.Add(AdminSettingsUi.Alert("Jellyfin Web operation failed", status.Error));
            if (!status.InstallerReady) panel.Children.Add(AdminSettingsUi.Alert("The server is missing tools required to install Jellyfin Web."));
            foreach (var tool in status.Prerequisites) panel.Children.Add(AdminSettingsUi.Text(tool.Name + ": " + (tool.Available ? "Available" : "Missing") + " · " + tool.Detail, 13, true));
            if (_controller?.State.DirtyKeys.Any(key => key is "jellyfin_compat.web_version" or "jellyfin_compat.web_install_dir") == true)
                panel.Children.Add(AdminSettingsUi.Alert("Save the web player version and installation folder before installing or removing managed files."));
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        AddAction(actions, status?.WebState == "update_available" ? "Update" : "Install / update", "AdminJellyfinWebInstall", Ready && _controller != null && AdminIntegrationPolicy.CanInstall(status, _controller.State),
            () => RunCommandAsync(async lease =>
            {
                var version = _controller!.State.PersistedValues.GetValueOrDefault("jellyfin_compat.web_version", "");
                var receipt = await _api.InstallJellyfinWebAsync(lease, version);
                AcceptWebOperation(lease, receipt); SetDomainStatus(lease, "Jellyfin Web installation requested. Waiting for server progress.");
            }, () => _controller != null && AdminIntegrationPolicy.CanInstall(_jellyfinWeb, _controller.State)));
        var serving = status is { ApiEnabled: true, Enabled: true };
        AddAction(actions, serving ? "Disable web player" : "Enable web player", "AdminJellyfinWebToggle", Ready && _controller != null && AdminIntegrationPolicy.CanToggleWeb(status, _controller.State),
            () => RunCommandAsync(async lease =>
            {
                _controller!.Set("jellyfin_compat.web_enabled", serving ? "false" : "true");
                await _controller.SaveFieldsAsync(["jellyfin_compat.web_enabled"]);
            }, () => _controller != null && AdminIntegrationPolicy.CanToggleWeb(_jellyfinWeb, _controller.State)));
        AddAction(actions, "Remove managed files", "AdminJellyfinWebRemove", Ready && CanRemoveWeb(),
            () => RunCommandAsync(async lease =>
            {
                var receipt = await _api.RemoveJellyfinWebAsync(lease); AcceptWebOperation(lease, receipt);
                if (lease.IsCurrent && _active) await _controller!.ReloadAsync(true);
                SetDomainStatus(lease, "Managed file removal requested. Waiting for server progress.");
            }, CanRemoveWeb,
                "Remove Jellyfin Web", "Remove managed Jellyfin Web files from the saved install folder? The compatibility API remains available."));
        panel.Children.Add(actions);
        if (status != null)
        {
            var details = new StackPanel { Spacing = 6 };
            details.Children.Add(AdminSettingsUi.Text("Install root: " + status.InstallRoot + " · Active directory: " + status.ActiveDirectory, 13, true));
            foreach (var (name, value) in status.Details.Where(detail => detail.Value != "")) details.Children.Add(AdminSettingsUi.Text(name + ": " + value, 13, true));
            var expander = new Expander { Header = "Details", Content = details, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(expander, "AdminJellyfinWebDetails"); panel.Children.Add(expander);
        }
        _domainParts.Add(panel);
    }
    private void AcceptWebOperation(MonitorAuthorityLease lease, AdminJellyfinWebStatus receipt)
    {
        if (!_active || !lease.IsCurrent) return;
        _jellyfinWeb = receipt; _acceptedWebOperation = receipt.OperationRunning ? receipt : null;
        _webReceiptDeadline = receipt.OperationRunning ? DateTimeOffset.UtcNow.AddSeconds(30) : null;
    }
    private bool CanRemoveWeb() => _jellyfinWeb is { OperationRunning: false, WebState: "installed" or "update_available" or "failed" } && _controller != null &&
        !_controller.State.DirtyKeys.Any(key => key is "jellyfin_compat.web_version" or "jellyfin_compat.web_install_dir");
}
