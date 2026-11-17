using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Presentation = SiloPlayer.Core.Services.Admin.AdminMaintenancePresentation;
using AdminSettingsUi = SiloPlayer.Controls.AdminSettingsUi;
using UI = SiloPlayer.Views.Admin.AdminLibrariesUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The Import Catalog Seed dialog (official <c>AdminCatalogMaintenance.tsx</c> form): Import Source
/// (Local File, Local Export Job, Bucket Artifact, Remote URL), Execution with its help, the selected
/// source's field and pickers, Conflict Mode, Path Rewrites, the validation note and Import Catalog.
/// The controller owns the draft; this view mirrors it. Closing resets the draft unless an import is
/// in flight; only the submitted draft is reset by success. Every value is a SERVER path, key or URL.
/// </summary>
public sealed partial class AdminMaintenancePage
{
    // Example paths and URLs shown as placeholders; not translated (official constants).
    private const string LocalPathExample = "/catalog-seeds/my-catalog.json.gz";
    private const string RemoteUrlExample = "https://example.com/catalog-seeds/export.json.gz";
    private const string RewriteFromExample = "/srv/media";
    private const string RewriteToExample = "/media";

    private static readonly AdminCatalogImportSourceKind[] SourceOrder =
        [AdminCatalogImportSourceKind.LocalPath, AdminCatalogImportSourceKind.ExportJob, AdminCatalogImportSourceKind.BucketArtifact, AdminCatalogImportSourceKind.RemoteUrl];

    private ImportView? _importView;

    private sealed class ImportView
    {
        internal required ContentDialog Dialog;
        internal required MonitorAuthorityLease Owner;
        internal readonly ComboBox Source = Select("MaintenanceImportSource");
        internal readonly ComboBox Execution = Select("MaintenanceImportExecution");
        internal readonly TextBlock ExecutionHelp = UI.Text("", 12, true);
        internal readonly ComboBox Conflict = Select("MaintenanceImportConflict");
        internal readonly StackPanel LocalPanel = new() { Spacing = 8 }, ExportPanel = new() { Spacing = 8 }, BucketPanel = new() { Spacing = 8 }, RemotePanel = new() { Spacing = 8 };
        internal readonly TextBox LocalPath = Input("MaintenanceImportLocalPath", LocalPathExample);
        internal readonly TextBox RemoteUrl = Input("MaintenanceImportRemoteUrl", RemoteUrlExample);
        internal readonly StackPanel LocalDetected = new() { Spacing = 8 };
        internal readonly ComboBox LocalChoices = Select("MaintenanceImportLocalChoices");
        internal readonly ComboBox ExportChoices = Select("MaintenanceImportExportChoices");
        internal readonly ComboBox BucketChoices = Select("MaintenanceImportBucketChoices");
        internal Button LocalRefresh = null!, LocalMore = null!, LocalRetry = null!, BucketRefresh = null!, BucketMore = null!, BucketRetry = null!;
        internal readonly StackPanel RewriteRows = new() { Spacing = 8 };
        internal Button AddRewrite = null!;
        internal readonly StackPanel RequiredRoots = new() { Spacing = 4, Visibility = Visibility.Collapsed };
        internal readonly Border Unknown = new() { Visibility = Visibility.Collapsed, Padding = new(12), CornerRadius = new(6), BorderThickness = new(1) };
        internal Button Acknowledge = null!;
        internal readonly TextBlock Error = UI.Text("", 13);
        internal Button Submit = null!;
        internal object? RenderedLocal, RenderedBucket, RenderedExports;
        internal string RenderedRows = "";
        internal readonly Dictionary<string, (TextBox From, TextBox To, TextBlock Error)> Rows = new(StringComparer.Ordinal);
        internal bool Syncing;
    }

    private static ComboBox Select(string automationId)
    {
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }

    private static TextBox Input(string automationId, string placeholder)
    {
        var box = new TextBox { PlaceholderText = placeholder, HorizontalAlignment = HorizontalAlignment.Stretch, IsSpellCheckEnabled = false };
        AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }

    private static TextBlock Label(string text, FrameworkElement? target = null)
    {
        var label = UI.Strong(text, 14);
        if (target != null) AutomationProperties.SetName(target, text);
        return label;
    }

    private async Task ShowImportDialogAsync()
    {
        var controller = _controller;
        if (controller == null || _dialog != null || XamlRoot == null || controller.State.Owner is not { IsCurrent: true } owner) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = T("web.admin.catalogMaintenance.import.title"),
            CloseButtonText = AppStrings.Get("common.cancel", fallback: "Cancel"),
        };
        dialog.Resources["ContentDialogMaxWidth"] = 672d;
        var view = new ImportView { Dialog = dialog, Owner = owner };
        dialog.Content = BuildImportContent(view);
        if (dialog.Content is FrameworkElement content) content.Width = Math.Max(240, Math.Min(672, XamlRoot.Size.Width - 32) - 58);
        AutomationProperties.SetAutomationId(dialog, "MaintenanceImportDialog");
        _dialog = dialog; _importView = view;
        controller.SetDialogOpen(true);
        var queue = DispatcherQueue;
        using var retired = owner.Token.Register(() => queue.TryEnqueue(() => { if (ReferenceEquals(_dialog, dialog)) dialog.Hide(); }));
        RenderImportDialog(controller.State);
        try { await dialog.ShowAsync(); }
        catch (System.Runtime.InteropServices.COMException) { }
        finally
        {
            if (ReferenceEquals(_dialog, dialog)) _dialog = null;
            if (ReferenceEquals(_importView, view)) _importView = null;
            if (ReferenceEquals(controller, _controller)) controller.SetDialogOpen(false);
        }
    }

    private void OnImportCompleted()
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active && ReferenceEquals(controller, _controller) && _importView is { } view && controller!.IsCurrentOwner(view.Owner)) view.Dialog.Hide();
        });
    }

    private FrameworkElement BuildImportContent(ImportView view)
    {
        var form = new StackPanel { Spacing = 16 };
        form.Children.Add(UI.Text(T("web.admin.catalogMaintenance.import.description"), 14, true));

        // Import Source.
        foreach (var key in new[] { "sourceLocalFile", "sourceExportJob", "sourceBucketArtifact", "sourceRemoteUrl" })
            view.Source.Items.Add(new ComboBoxItem { Content = T("web.admin.catalogMaintenance.import." + key) });
        view.Source.SelectionChanged += (_, _) => Edit(view, draft => view.Source.SelectedIndex is >= 0 and < 4 && SourceOrder[view.Source.SelectedIndex] != draft.Source
            ? draft.WithSource(SourceOrder[view.Source.SelectedIndex]) : draft);
        form.Children.Add(Field(Label(T("web.admin.catalogMaintenance.import.sourceLabel"), view.Source), view.Source));

        // Execution.
        view.Execution.Items.Add(new ComboBoxItem { Content = T("web.admin.catalogMaintenance.import.executionQueued") });
        view.Execution.Items.Add(new ComboBoxItem { Content = T("web.admin.catalogMaintenance.import.executionSynchronous") });
        view.Execution.SelectionChanged += (_, _) => Edit(view, draft =>
        {
            var execution = view.Execution.SelectedIndex == 1 ? AdminCatalogImportExecution.Synchronous : AdminCatalogImportExecution.Queued;
            return view.Execution.SelectedIndex >= 0 && execution != draft.Execution ? draft with { Execution = execution } : draft;
        });
        form.Children.Add(Field(Label(T("web.admin.catalogMaintenance.import.executionLabel"), view.Execution), view.Execution, view.ExecutionHelp));

        // Local File: path, detected files (Refresh, choose), help, Load more and Retry.
        view.LocalPath.TextChanged += (_, _) => Edit(view, draft => view.LocalPath.Text == draft.LocalPath ? draft : draft with { LocalPath = view.LocalPath.Text });
        view.LocalPanel.Children.Add(Label(T("web.admin.catalogMaintenance.import.filePathLabel"), view.LocalPath));
        view.LocalPanel.Children.Add(view.LocalPath);
        view.LocalRefresh = ListButton(view, AdminMaintenanceListKind.LocalSources, T("web.admin.catalogMaintenance.refresh"), "MaintenanceImportLocalRefresh", ListAction.Refresh);
        var detectedHeader = new Grid();
        detectedHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); detectedHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var detectedLabel = UI.Text(T("web.admin.catalogMaintenance.import.detectedFiles"), 12, true); detectedLabel.VerticalAlignment = VerticalAlignment.Center;
        detectedHeader.Children.Add(detectedLabel);
        Grid.SetColumn(view.LocalRefresh, 1); detectedHeader.Children.Add(view.LocalRefresh);
        view.LocalChoices.PlaceholderText = T("web.admin.catalogMaintenance.import.selectDetectedFile");
        AutomationProperties.SetName(view.LocalChoices, T("web.admin.catalogMaintenance.import.selectDetectedFile"));
        view.LocalChoices.SelectionChanged += (_, _) =>
        {
            if (view.Syncing || view.LocalChoices.SelectedItem is not ComboBoxItem { Tag: string key }) return;
            Edit(view, draft => draft with { LocalPath = key });
            view.Syncing = true;
            try { view.LocalChoices.SelectedIndex = -1; }
            finally { view.Syncing = false; }
        };
        view.LocalDetected.Children.Add(detectedHeader); view.LocalDetected.Children.Add(view.LocalChoices);
        view.LocalPanel.Children.Add(view.LocalDetected);
        view.LocalPanel.Children.Add(RichHelp(T("web.admin.catalogMaintenance.import.filePathHelp")));
        view.LocalMore = ListButton(view, AdminMaintenanceListKind.LocalSources, T("web.admin.catalogMaintenance.import.loadMoreLocal"), "MaintenanceImportLocalMore", ListAction.More);
        view.LocalRetry = ListButton(view, AdminMaintenanceListKind.LocalSources, T("web.admin.catalogMaintenance.import.retryLocal"), "MaintenanceImportLocalRetry", ListAction.Restart);
        view.LocalPanel.Children.Add(view.LocalMore); view.LocalPanel.Children.Add(view.LocalRetry);
        form.Children.Add(view.LocalPanel);

        // Local Export Job: completed export choice and help.
        view.ExportChoices.PlaceholderText = T("web.admin.catalogMaintenance.import.chooseExport");
        view.ExportChoices.SelectionChanged += (_, _) =>
        {
            if (view.ExportChoices.SelectedItem is ComboBoxItem { Tag: string id }) Edit(view, draft => draft.ExportJobId == id ? draft : draft with { ExportJobId = id });
        };
        view.ExportPanel.Children.Add(Label(T("web.admin.catalogMaintenance.import.completedExportLabel"), view.ExportChoices));
        view.ExportPanel.Children.Add(view.ExportChoices);
        view.ExportPanel.Children.Add(UI.Text(T("web.admin.catalogMaintenance.import.exportJobHelp"), 12, true));
        form.Children.Add(view.ExportPanel);

        // Bucket Artifact: label with Refresh, Load more and Retry, the seed choice and help.
        view.BucketRefresh = ListButton(view, AdminMaintenanceListKind.BucketSources, T("web.admin.catalogMaintenance.refresh"), "MaintenanceImportBucketRefresh", ListAction.Refresh);
        view.BucketMore = ListButton(view, AdminMaintenanceListKind.BucketSources, T("web.admin.catalogMaintenance.import.loadMoreBucket"), "MaintenanceImportBucketMore", ListAction.More);
        view.BucketRetry = ListButton(view, AdminMaintenanceListKind.BucketSources, T("web.admin.catalogMaintenance.import.retryBucket"), "MaintenanceImportBucketRetry", ListAction.Restart);
        var bucketHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bucketHeader.Children.Add(Label(T("web.admin.catalogMaintenance.import.bucketLabel"), view.BucketChoices));
        bucketHeader.Children.Add(view.BucketRefresh); bucketHeader.Children.Add(view.BucketMore); bucketHeader.Children.Add(view.BucketRetry);
        view.BucketPanel.Children.Add(Wrap(bucketHeader));
        view.BucketChoices.PlaceholderText = T("web.admin.catalogMaintenance.import.chooseBucketSeed");
        view.BucketChoices.SelectionChanged += (_, _) =>
        {
            if (view.BucketChoices.SelectedItem is ComboBoxItem { Tag: string key }) Edit(view, draft => draft.ArtifactKey == key ? draft : draft with { ArtifactKey = key });
        };
        view.BucketPanel.Children.Add(view.BucketChoices);
        view.BucketPanel.Children.Add(UI.Text(T("web.admin.catalogMaintenance.import.bucketHelp"), 12, true));
        form.Children.Add(view.BucketPanel);

        // Remote URL.
        view.RemoteUrl.TextChanged += (_, _) => Edit(view, draft => view.RemoteUrl.Text == draft.RemoteUrl ? draft : draft with { RemoteUrl = view.RemoteUrl.Text });
        view.RemotePanel.Children.Add(Label(T("web.admin.catalogMaintenance.import.remoteUrlLabel"), view.RemoteUrl));
        view.RemotePanel.Children.Add(view.RemoteUrl);
        view.RemotePanel.Children.Add(RichHelp(T("web.admin.catalogMaintenance.import.remoteUrlHelp")));
        form.Children.Add(view.RemotePanel);

        // Conflict Mode.
        view.Conflict.Items.Add(new ComboBoxItem { Content = T("web.admin.catalogMaintenance.import.conflictSkip") });
        view.Conflict.Items.Add(new ComboBoxItem { Content = T("web.admin.catalogMaintenance.import.conflictOverwrite") });
        view.Conflict.SelectionChanged += (_, _) => Edit(view, draft =>
        {
            var mode = view.Conflict.SelectedIndex == 1 ? AdminCatalogConflictMode.OverwriteExisting : AdminCatalogConflictMode.SkipExisting;
            return view.Conflict.SelectedIndex >= 0 && mode != draft.ConflictMode ? draft with { ConflictMode = mode } : draft;
        });
        form.Children.Add(Field(Label(T("web.admin.catalogMaintenance.import.conflictLabel"), view.Conflict), view.Conflict));

        // Path Rewrites.
        var rewrites = new StackPanel { Spacing = 12 };
        var rewriteHeading = new StackPanel { Spacing = 4 };
        rewriteHeading.Children.Add(UI.Strong(T("web.admin.catalogMaintenance.import.rewritesLabel"), 14));
        rewriteHeading.Children.Add(RichHelp(T("web.admin.catalogMaintenance.import.rewritesHelp")));
        rewrites.Children.Add(rewriteHeading);
        AutomationProperties.SetAutomationId(view.RewriteRows, "MaintenanceImportRewrites");
        rewrites.Children.Add(view.RewriteRows);
        AutomationProperties.SetAutomationId(view.RequiredRoots, "MaintenanceImportRequiredRoots");
        AutomationProperties.SetLiveSetting(view.RequiredRoots, AutomationLiveSetting.Assertive);
        rewrites.Children.Add(view.RequiredRoots);
        view.AddRewrite = UI.Action(T("web.admin.catalogMaintenance.import.addRewrite"), "MaintenanceImportAddRewrite", "plus");
        view.AddRewrite.HorizontalAlignment = HorizontalAlignment.Left;
        view.AddRewrite.Click += (_, _) => Edit(view, draft => draft.AddRewrite());
        rewrites.Children.Add(view.AddRewrite);
        form.Children.Add(rewrites);

        var note = new Border
        {
            Child = UI.Text(T("web.admin.catalogMaintenance.import.validationNote"), 12, true), Padding = new(12), CornerRadius = new(6),
            BorderThickness = new(1), BorderBrush = UI.Tint("BorderBrush", 0.6), Background = UI.Tint("MutedBrush", 0.3),
        };
        form.Children.Add(note);

        // Unknown outcome after an unconfirmed import: kept until the administrator has checked.
        var unknown = new StackPanel { Spacing = 8 };
        unknown.Children.Add(UI.Strong(T("admin.native.maintenance.importUnknown.title"), 14));
        unknown.Children.Add(UI.Text(T("admin.native.maintenance.importUnknown.body"), 13));
        view.Acknowledge = UI.Action(T("admin.native.maintenance.importUnknown.acknowledge"), "MaintenanceImportAcknowledge");
        view.Acknowledge.HorizontalAlignment = HorizontalAlignment.Left;
        view.Acknowledge.Click += (_, _) => { if (_controller is { } controller && controller.IsCurrentOwner(view.Owner)) controller.AcknowledgeImportOutcome(); };
        unknown.Children.Add(view.Acknowledge);
        view.Unknown.Child = unknown;
        view.Unknown.BorderBrush = UI.Solid(UI.Warning, 0.5); view.Unknown.Background = UI.Solid(UI.Warning, 0.08);
        AutomationProperties.SetAutomationId(view.Unknown, "MaintenanceImportUnknown");
        AutomationProperties.SetLiveSetting(view.Unknown, AutomationLiveSetting.Assertive);
        form.Children.Add(view.Unknown);

        view.Error.Foreground = UI.Solid(UI.Destructive);
        view.Error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(view.Error, "MaintenanceImportError");
        AutomationProperties.SetLiveSetting(view.Error, AutomationLiveSetting.Assertive);
        form.Children.Add(view.Error);

        view.Submit = UI.Action(T("web.admin.catalogMaintenance.import.submit"), "MaintenanceImportSubmit", style: "AccentButtonStyle", height: 36);
        view.Submit.HorizontalAlignment = HorizontalAlignment.Stretch;
        view.Submit.Click += async (_, _) => { if (_controller is { } controller && controller.IsCurrentOwner(view.Owner)) await controller.SubmitImportAsync(); };
        form.Children.Add(view.Submit);
        return form;
    }

    private enum ListAction { Refresh, More, Restart }

    private Button ListButton(ImportView view, AdminMaintenanceListKind kind, string label, string automationId, ListAction action)
    {
        var button = action == ListAction.Refresh
            ? UI.Action(label, automationId, "refresh-cw", style: "GhostButtonStyle")
            : UI.Action(label, automationId);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Click += async (_, _) =>
        {
            if (_controller is not { } controller || !controller.IsCurrentOwner(view.Owner)) return;
            await (action switch
            {
                ListAction.Refresh => controller.RefreshAsync(kind),
                ListAction.More => controller.LoadMoreAsync(kind),
                _ => controller.RestartAsync(kind),
            });
        };
        return button;
    }

    private static StackPanel Field(TextBlock label, FrameworkElement control, FrameworkElement? help = null)
    {
        var field = new StackPanel { Spacing = 8 };
        field.Children.Add(label); field.Children.Add(control);
        if (help != null) field.Children.Add(help);
        return field;
    }

    /// <summary>Applies one draft edit for the dialog's own owner; programmatic syncing and unchanged values are ignored.</summary>
    private void Edit(ImportView view, Func<AdminCatalogImportDraft, AdminCatalogImportDraft> edit)
    {
        if (view.Syncing || _controller is not { } controller || !controller.IsCurrentOwner(view.Owner)) return;
        controller.EditDraft(edit);
    }

    private void RenderImportDialog(AdminMaintenanceState state)
    {
        if (_importView is not { } view) return;
        if (_controller is not { } controller || !controller.IsCurrentOwner(view.Owner)) { view.Dialog.Hide(); return; }
        var draft = state.Draft;
        var editable = !state.ImportPending;
        view.Syncing = true;
        try
        {
            view.Source.SelectedIndex = Array.IndexOf(SourceOrder, draft.Source);
            view.Execution.SelectedIndex = draft.Execution == AdminCatalogImportExecution.Synchronous ? 1 : 0;
            view.ExecutionHelp.Text = T(draft.Execution == AdminCatalogImportExecution.Queued
                ? "web.admin.catalogMaintenance.import.executionQueuedHelp" : "web.admin.catalogMaintenance.import.executionSynchronousHelp");
            view.Conflict.SelectedIndex = draft.ConflictMode == AdminCatalogConflictMode.OverwriteExisting ? 1 : 0;
            if (view.LocalPath.Text != draft.LocalPath) view.LocalPath.Text = draft.LocalPath;
            if (view.RemoteUrl.Text != draft.RemoteUrl) view.RemoteUrl.Text = draft.RemoteUrl;
            view.LocalPanel.Visibility = Show(draft.Source == AdminCatalogImportSourceKind.LocalPath);
            view.ExportPanel.Visibility = Show(draft.Source == AdminCatalogImportSourceKind.ExportJob);
            view.BucketPanel.Visibility = Show(draft.Source == AdminCatalogImportSourceKind.BucketArtifact);
            view.RemotePanel.Visibility = Show(draft.Source == AdminCatalogImportSourceKind.RemoteUrl);

            // Detected local files: shown only when some exist; choosing one fills File Path.
            var local = state.LocalSources;
            view.LocalDetected.Visibility = Show(local.Items.Count > 0);
            if (!ReferenceEquals(view.RenderedLocal, local.Items))
            {
                view.RenderedLocal = local.Items;
                view.LocalChoices.Items.Clear();
                foreach (var source in local.Items) view.LocalChoices.Items.Add(new ComboBoxItem { Content = Presentation.SourceChoice(source), Tag = source.Key });
            }
            view.LocalChoices.SelectedIndex = -1;
            view.LocalRefresh.IsEnabled = !local.IsFetching;
            view.LocalMore.Visibility = Show(local.HasMore); view.LocalMore.IsEnabled = !local.IsFetching;
            view.LocalRetry.Visibility = Show(local.Failure != null);

            // Completed exports.
            var completed = state.CompletedExports;
            if (!ReferenceEquals(view.RenderedExports, state.Exports.Items))
            {
                view.RenderedExports = state.Exports.Items;
                view.ExportChoices.Items.Clear();
                if (completed.Count == 0) view.ExportChoices.Items.Add(new ComboBoxItem { Content = T("web.admin.catalogMaintenance.import.noCompletedExports"), IsEnabled = false });
                foreach (var job in completed) view.ExportChoices.Items.Add(new ComboBoxItem { Content = Presentation.ExportChoice(job), Tag = job.Id });
            }
            view.ExportChoices.SelectedIndex = IndexOfTag(view.ExportChoices, draft.ExportJobId);

            // Bucket seeds.
            var bucket = state.BucketSources;
            if (!ReferenceEquals(view.RenderedBucket, bucket.Items))
            {
                view.RenderedBucket = bucket.Items;
                view.BucketChoices.Items.Clear();
                if (bucket.Items.Count == 0) view.BucketChoices.Items.Add(new ComboBoxItem { Content = T("web.admin.catalogMaintenance.import.noBucketSeeds"), IsEnabled = false });
                foreach (var source in bucket.Items) view.BucketChoices.Items.Add(new ComboBoxItem { Content = Presentation.SourceChoice(source), Tag = source.Key });
            }
            view.BucketChoices.SelectedIndex = IndexOfTag(view.BucketChoices, draft.ArtifactKey);
            view.BucketRefresh.IsEnabled = !bucket.IsFetching;
            view.BucketMore.Visibility = Show(bucket.HasMore); view.BucketMore.IsEnabled = !bucket.IsFetching;
            view.BucketRetry.Visibility = Show(bucket.Failure != null);

            RenderRewrites(view, draft, editable);
            view.RequiredRoots.Children.Clear();
            if (state.RequiredRewriteRoots.Count > 0)
            {
                var heading = UI.Text(T("admin.native.maintenance.rewriteRequired"), 13); heading.Foreground = UI.Solid(UI.Destructive);
                view.RequiredRoots.Children.Add(heading);
                foreach (var root in state.RequiredRewriteRoots) view.RequiredRoots.Children.Add(UI.Mono(root, 12, muted: false));
            }
            view.RequiredRoots.Visibility = Show(state.RequiredRewriteRoots.Count > 0);

            view.Unknown.Visibility = Show(state.ImportOutcomeUnknown);
            var error = state.RequiredRewriteRoots.Count > 0 ? null : state.ImportError;
            if (error != null) AdminSettingsUi.UpdateLiveText(view.Error, error);
            view.Error.Visibility = Show(error != null);

            foreach (var control in new Control[] { view.Source, view.Execution, view.Conflict, view.LocalPath, view.RemoteUrl, view.LocalChoices, view.ExportChoices, view.BucketChoices, view.AddRewrite })
                control.IsEnabled = editable;
            view.AddRewrite.IsEnabled = editable && draft.Rewrites.Count < AdminCatalogImportDraft.MaxRewrites;
            view.Submit.Content = T(state.ImportPending ? "web.admin.catalogMaintenance.import.importing" : "web.admin.catalogMaintenance.import.submit");
            AutomationProperties.SetName(view.Submit, (string)view.Submit.Content);
            view.Submit.IsEnabled = state.CanSubmitImport;
        }
        finally { view.Syncing = false; }
    }

    private void RenderRewrites(ImportView view, AdminCatalogImportDraft draft, bool editable)
    {
        var ids = string.Join("|", draft.Rewrites.Select(row => row.Id));
        if (ids != view.RenderedRows)
        {
            view.RenderedRows = ids;
            view.RewriteRows.Children.Clear();
            view.Rows.Clear();
            foreach (var row in draft.Rewrites)
            {
                var id = row.Id;
                var from = Input("MaintenanceRewriteFrom_" + id, RewriteFromExample);
                var to = Input("MaintenanceRewriteTo_" + id, RewriteToExample);
                AutomationProperties.SetName(from, T("web.admin.catalogMaintenance.import.rewritesLabel") + " " + RewriteFromExample);
                AutomationProperties.SetName(to, T("web.admin.catalogMaintenance.import.rewritesLabel") + " " + RewriteToExample);
                from.TextChanged += (_, _) => Edit(view, current => current.Rewrites.FirstOrDefault(candidate => candidate.Id == id) is { } existing && existing.From != from.Text
                    ? current.UpdateRewrite(id, from: from.Text) : current);
                to.TextChanged += (_, _) => Edit(view, current => current.Rewrites.FirstOrDefault(candidate => candidate.Id == id) is { } existing && existing.To != to.Text
                    ? current.UpdateRewrite(id, to: to.Text) : current);
                var remove = UI.IconButton("trash-2", T("web.admin.catalogMaintenance.import.removeRewrite"), "MaintenanceRewriteRemove_" + id, 36, 16);
                remove.Click += (_, _) => Edit(view, current => current.RemoveRewrite(id));
                var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
                grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                grid.Children.Add(from);
                Grid.SetColumn(to, 1); grid.Children.Add(to);
                Grid.SetColumn(remove, 2); grid.Children.Add(remove);
                var error = UI.Text(T("admin.native.maintenance.rewritePartial"), 12);
                error.Foreground = UI.Solid(UI.Destructive); error.Visibility = Visibility.Collapsed;
                AutomationProperties.SetAutomationId(error, "MaintenanceRewriteError_" + id);
                AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
                var block = new StackPanel { Spacing = 4 };
                block.Children.Add(grid); block.Children.Add(error);
                view.RewriteRows.Children.Add(block);
                view.Rows[id] = (from, to, error);
            }
        }
        foreach (var row in draft.Rewrites)
        {
            if (!view.Rows.TryGetValue(row.Id, out var controls)) continue;
            if (controls.From.Text != row.From) controls.From.Text = row.From;
            if (controls.To.Text != row.To) controls.To.Text = row.To;
            controls.From.IsEnabled = controls.To.IsEnabled = editable;
            controls.Error.Visibility = Show(row.IsPartial);
        }
        foreach (var block in view.RewriteRows.Children.OfType<StackPanel>())
            if (block.Children[0] is Grid { Children.Count: 3 } grid && grid.Children[2] is Button remove) remove.IsEnabled = editable;
    }

    private static int IndexOfTag(ComboBox box, string tag)
    {
        if (tag.Length == 0) return -1;
        for (var index = 0; index < box.Items.Count; index++)
            if (box.Items[index] is ComboBoxItem { Tag: string value } && value == tag) return index;
        return -1;
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
