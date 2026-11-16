using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using R = SiloPlayer.Core.Services.Admin.AdminStorageRules;

namespace SiloPlayer.Views.Admin;

/// <summary>Active/recovery/failed/restart panels and the managed transition review dialog (official StorageSettings.tsx).</summary>
public sealed partial class AdminStorageSettingsPage
{
    private static readonly Windows.UI.Color Emerald500 = Windows.UI.Color.FromArgb(255, 0x10, 0xB9, 0x81);
    private Border _activePanel = null!, _recoveryPanel = null!, _failedPanel = null!, _manualPanel = null!;
    private readonly TextBlock _activeTitle = Strong(""), _activePhase = U.Text("", 12, true), _activeVerified = U.Text("", 12, true);
    private readonly Button _activeRefresh = U.Action(T("web.admin.settings.storage.active.refresh"), "StorageTransitionRefresh");
    private readonly Button _activeCancel = U.Action(T("web.admin.settings.storage.active.cancel"), "StorageTransitionCancel");
    private readonly TextBlock _recoveryTitle = Strong(""), _recoveryMessage = U.Text("", 12, true), _recoveryAttention = U.Text("", 12);
    private readonly Grid _recoveryBar = new() { Height = 6, CornerRadius = new(3), Margin = new(0, 12, 0, 0) };
    private readonly TextBlock _failedTitle = Strong(""), _failedBody = U.Text("", 12, true), _failedVerified = U.Text("", 12, true);
    private readonly Button _failedDismiss = U.Action(T("web.admin.settings.storage.dismiss"), "StorageFailedDismiss");
    private readonly Button _manualDismiss = U.Action(T("web.admin.settings.storage.dismiss"), "StorageManualRestartDismiss");

    // Review dialog parts, rebuilt in place while one review is open.
    private ContentDialog? _review;
    private readonly TextBlock _reviewDescription = U.Text(T("web.admin.settings.storage.dialog.description"), 14, true);
    private readonly StackPanel _sourceChecking = new() { Spacing = 8, Padding = new(16) };
    private Border _sourceCheckingBox = null!;
    private Border _sourceFailed = null!;
    private readonly TextBlock _sourceFailedText = Strong("");
    private readonly Button _sourceRetry = U.Action(T("web.admin.settings.storage.dialog.retryCheck"), "StorageSourceRetry");
    private Border _sourceOk = null!;
    private readonly TextBlock _targetTitle = Strong(""), _targetLine = U.Text("", 12, true), _targetPrivateLine = U.Text("", 12, true);
    private readonly TextBlock _endpointMissing = U.Text(T("web.admin.settings.storage.dialog.privateEndpointMissing"), 12);
    private readonly StackPanel _reviewLocal = new() { Spacing = 8 };
    private readonly TextBox _reviewLocalPath = U.Input(T("web.admin.settings.storage.dialog.localPath"), "StorageReviewLocalPath", R.LocalPathExample);
    private readonly TextBlock _reviewS3Note = U.Text(T("web.admin.settings.storage.dialog.s3ValuesNote"), 12, true);
    private readonly RadioButton[] _policies = new RadioButton[3];
    private readonly TextBlock[] _policyTitles = new TextBlock[3], _policyDescriptions = new TextBlock[3], _policyReasons = new TextBlock[3];
    private readonly StackPanel _notes = new() { Spacing = 4, Margin = new(0, 8, 0, 0) };
    private readonly Button _reviewCancel = U.Action(T("common.cancel"), "StorageReviewCancel");
    private readonly Button _reviewQueue = U.Action(T("web.admin.settings.storage.dialog.queue"), "StorageReviewQueue", primary: true);
    private StackPanel? _reviewContent;

    private static TextBlock Strong(string text, double size = 14)
    {
        var block = U.Text(text, size); block.FontWeight = FontWeights.Medium; return block;
    }

    private void BuildTransitionPanels()
    {
        // Active transition.
        var activeText = new StackPanel { Spacing = 4 };
        AutomationProperties.SetAutomationId(_activeTitle, "StorageActiveTitle");
        AutomationProperties.SetAutomationId(_activePhase, "StorageActivePhase");
        AutomationProperties.SetName(_activeVerified, T("web.admin.settings.storage.active.verifiedLabel"));
        AutomationProperties.SetAutomationId(_activeVerified, "StorageActiveVerified");
        _activeVerified.Margin = new(0, 8, 0, 0);
        activeText.Children.Add(_activeTitle); activeText.Children.Add(_activePhase); activeText.Children.Add(_activeVerified);
        var activeActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        activeActions.Children.Add(_activeRefresh); activeActions.Children.Add(_activeCancel);
        _activePanel = Card(activeText, activeActions, null, 0, 0, "status");
        AutomationProperties.SetAutomationId(_activePanel, "StorageActiveTransition");
        _activeRefresh.Click += (_, _) => _controller?.RefreshTransitions();
        _activeCancel.Click += async (_, _) => { if (_controller != null) await _controller.CancelTransitionAsync(); };
        _fields.Children.Add(_activePanel);

        // Recovery after restart.
        var recoveryText = new StackPanel { Spacing = 4 };
        _recoveryAttention.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        AutomationProperties.SetAutomationId(_recoveryTitle, "StorageRecoveryTitle");
        recoveryText.Children.Add(_recoveryTitle); recoveryText.Children.Add(_recoveryMessage); recoveryText.Children.Add(_recoveryAttention);
        _recoveryBar.Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .2);
        _recoveryBar.ColumnDefinitions.Add(new() { Width = new(0, GridUnitType.Star) });
        _recoveryBar.ColumnDefinitions.Add(new() { Width = new(100, GridUnitType.Star) });
        _recoveryBar.Children.Add(new Border { CornerRadius = new(3), Background = U.Accent });
        AutomationProperties.SetAutomationId(_recoveryBar, "StorageRecoveryProgress");
        recoveryText.Children.Add(_recoveryBar);
        _recoveryPanel = Card(recoveryText, null, null, 0, 0, "status");
        AutomationProperties.SetAutomationId(_recoveryPanel, "StorageRecovery");
        _fields.Children.Add(_recoveryPanel);

        // Failed or cancelled transition.
        var failedText = new StackPanel { Spacing = 4 };
        AutomationProperties.SetAutomationId(_failedTitle, "StorageFailedTitle");
        AutomationProperties.SetAutomationId(_failedBody, "StorageFailedBody");
        failedText.Children.Add(_failedTitle); failedText.Children.Add(_failedBody); failedText.Children.Add(_failedVerified);
        _failedPanel = Card(failedText, _failedDismiss, AdminMonitorUi.Red500, .25, .05, "alert");
        AutomationProperties.SetAutomationId(_failedPanel, "StorageFailedTransition");
        _failedDismiss.Click += (_, _) => { if (_controller?.State.LatestTransition is { } job) _controller.DismissTransition(job.Id); };
        _fields.Children.Add(_failedPanel);

        // Committed, but the host could not restart itself.
        var manualText = new StackPanel { Spacing = 4 };
        manualText.Children.Add(Strong(T("web.admin.settings.storage.manualRestart.title")));
        manualText.Children.Add(U.Text(T("web.admin.settings.storage.manualRestart.body"), 12, true));
        _manualPanel = Card(manualText, _manualDismiss, AdminMonitorUi.Amber500, .25, .05, "alert");
        AutomationProperties.SetAutomationId(_manualPanel, "StorageManualRestart");
        _manualDismiss.Click += (_, _) => { if (_controller?.State.LatestTransition is { } job) _controller.DismissTransition(job.Id); };
        _fields.Children.Add(_manualPanel);

        BuildReviewParts();
    }

    private void RenderTransitionPanels(AdminStorageSettingsState state)
    {
        var job = state.LatestTransition;
        var verified = job?.VerifiedObjects is { } count ? T("web.admin.settings.storage.verifiedObjects", AdminText.Count(count)) : null;

        var active = job is { IsActive: true };
        _activePanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active)
        {
            U.UpdateLiveText(_activeTitle, T(job!.Status == "queued" ? "web.admin.settings.storage.active.queued" : "web.admin.settings.storage.active.running"));
            U.UpdateLiveText(_activePhase, T(R.PhaseKey(job.Phase)));
            _activeVerified.Text = verified ?? "";
            _activeVerified.Visibility = verified != null ? Visibility.Visible : Visibility.Collapsed;
            _activeCancel.Content = T(state.Cancelling ? "web.admin.settings.storage.active.cancelling" : "web.admin.settings.storage.active.cancel");
            AutomationProperties.SetName(_activeCancel, _activeCancel.Content.ToString());
            _activeCancel.IsEnabled = !state.Cancelling;
        }

        var recovery = state.ManagedTransitionsAvailable ? state.Recovery : null;
        var recovering = recovery?.RecoveryPending == true;
        _recoveryPanel.Visibility = recovering ? Visibility.Visible : Visibility.Collapsed;
        if (recovering)
        {
            U.UpdateLiveText(_recoveryTitle, T(R.RecoveryTitleKey(recovery!.RecoveryState)));
            _recoveryMessage.Text = T(R.RecoveryMessageKey(recovery.RecoveryState));
            _recoveryAttention.Text = recovery.RecoveryError != null ? T("web.admin.settings.storage.recovery.needsAttention") : "";
            _recoveryAttention.Visibility = recovery.RecoveryError != null ? Visibility.Visible : Visibility.Collapsed;
            var percent = Math.Clamp(recovery.RecoveryProgressPercent, 0, 100);
            _recoveryBar.ColumnDefinitions[0].Width = new(percent, GridUnitType.Star);
            _recoveryBar.ColumnDefinitions[1].Width = new(100 - percent, GridUnitType.Star);
            AutomationProperties.SetName(_recoveryBar, AdminText.FormatNumber(percent) + "%");
        }

        var failed = job is { IsFailedOrCancelled: true } && job.Id != state.DismissedTransitionId;
        _failedPanel.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        if (failed)
        {
            var cancelled = job!.Status == "cancelled";
            U.UpdateLiveText(_failedTitle, T(cancelled ? "web.admin.settings.storage.failed.titleCancelled" : "web.admin.settings.storage.failed.titleFailed"));
            _failedBody.Text = T("web.admin.settings.storage.failed.body", new { reason = cancelled ? T("web.admin.settings.storage.failed.canceledReason") : T(R.FailureKey(job.FailureCategory)) });
            _failedVerified.Text = verified ?? "";
            _failedVerified.Visibility = verified != null ? Visibility.Visible : Visibility.Collapsed;
        }

        _manualPanel.Visibility = job is { ManualRestartRequired: true } && job.Id != state.DismissedTransitionId ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Review dialog ────────────────────────────────────────

    private void BuildReviewParts()
    {
        AutomationProperties.SetName(_sourceChecking, T("web.admin.settings.storage.dialog.checkingSource"));
        AutomationProperties.SetLiveSetting(_sourceChecking, AutomationLiveSetting.Polite);
        _sourceChecking.Children.Add(U.Skeleton(16, 192)); _sourceChecking.Children.Add(U.Skeleton(12, 448));
        _sourceCheckingBox = new Border
        {
            Child = _sourceChecking, CornerRadius = new(10), BorderThickness = new(1),
            BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .08),
        };
        AutomationProperties.SetAutomationId(_sourceFailedText, "StorageSourceFailedText");
        var failed = new StackPanel { Spacing = 4 };
        failed.Children.Add(_sourceFailedText);
        failed.Children.Add(U.Text(T("web.admin.settings.storage.dialog.sourceUnavailableHint"), 12, true));
        _sourceRetry.Margin = new(0, 8, 0, 0); _sourceRetry.HorizontalAlignment = HorizontalAlignment.Left;
        failed.Children.Add(_sourceRetry);
        _sourceRetry.Click += (_, _) => _controller?.RetrySourceHealth();
        var failedRow = new Grid { ColumnSpacing = 12 };
        failedRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); failedRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var icon = AdminMonitorUi.Icon("alert-triangle", 16, AdminMonitorUi.Destructive); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new(0, 2, 0, 0);
        failedRow.Children.Add(icon); Grid.SetColumn(failed, 1); failedRow.Children.Add(failed);
        _sourceFailed = new Border
        {
            Child = failedRow, Padding = new(16), CornerRadius = new(10), BorderThickness = new(1),
            BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Destructive, .3), Background = AdminMonitorUi.Solid(AdminMonitorUi.Destructive, .05),
        };
        AutomationProperties.SetLiveSetting(_sourceFailed, AutomationLiveSetting.Assertive);
        var ok = new StackPanel { Spacing = 4 };
        var reachable = Strong(T("web.admin.settings.storage.dialog.sourceReachable")); reachable.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Emerald400);
        ok.Children.Add(reachable); ok.Children.Add(U.Text(T("web.admin.settings.storage.dialog.copyOptionsAvailable"), 12, true));
        _sourceOk = new Border
        {
            Child = ok, Padding = new(16, 12, 16, 12), CornerRadius = new(10), BorderThickness = new(1),
            BorderBrush = AdminMonitorUi.Solid(Emerald500, .25), Background = AdminMonitorUi.Solid(Emerald500, .05),
        };
        AutomationProperties.SetLiveSetting(_sourceOk, AutomationLiveSetting.Polite);

        _endpointMissing.FontWeight = FontWeights.Medium; _endpointMissing.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        AutomationProperties.SetAutomationId(_endpointMissing, "StorageEndpointMissing");
        AutomationProperties.SetLiveSetting(_endpointMissing, AutomationLiveSetting.Assertive);
        _reviewLocalPath.Width = double.NaN; _reviewLocalPath.HorizontalAlignment = HorizontalAlignment.Stretch;
        _reviewLocal.Children.Add(Strong(T("web.admin.settings.storage.dialog.localPath")));
        _reviewLocal.Children.Add(_reviewLocalPath);
        _reviewLocalPath.TextChanging += (_, _) =>
        {
            if (!_syncing && _controller?.State.Review is { } review && review.LocalPath != _reviewLocalPath.Text) _controller.SetReviewLocalPath(_reviewLocalPath.Text);
        };
        AutomationProperties.SetAutomationId(_targetTitle, "StorageReviewTarget");
        AutomationProperties.SetAutomationId(_targetLine, "StorageReviewTargetLine");

        for (var index = 0; index < 3; index++)
        {
            var title = _policyTitles[index] = Strong("");
            var description = _policyDescriptions[index] = U.Text("", 12, true);
            var reason = _policyReasons[index] = U.Text("", 12);
            reason.FontWeight = FontWeights.Medium; reason.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
            var text = new StackPanel { Spacing = 4 };
            text.Children.Add(title); text.Children.Add(description); text.Children.Add(reason);
            var radio = _policies[index] = new RadioButton
            {
                GroupName = "storage-transition-policy", Content = text, HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new(12), BorderThickness = new(1), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .8), CornerRadius = new(10),
                UseSystemFocusVisuals = true,
            };
            radio.Checked += (_, _) =>
            {
                if (_syncing || radio.Tag is not string value || _controller?.State.Review is not { } review || review.Policy == value) return;
                _controller.SetReviewPolicy(value);
            };
        }
        _reviewCancel.Click += (_, _) => _controller?.CloseReview();
        _reviewQueue.Click += async (_, _) => { if (_controller != null) await _controller.QueueTransitionAsync(); };
    }

    private StackPanel ReviewContent()
    {
        var content = new StackPanel { Spacing = 20 };
        content.Children.Add(_reviewDescription);
        content.Children.Add(_sourceCheckingBox); content.Children.Add(_sourceFailed); content.Children.Add(_sourceOk);
        var target = new StackPanel { Spacing = 8 };
        var targetLabel = Strong(T("web.admin.settings.storage.dialog.target"));
        target.Children.Add(targetLabel);
        var box = new StackPanel { Spacing = 2 };
        box.Children.Add(_targetTitle); box.Children.Add(_targetLine); box.Children.Add(_targetPrivateLine);
        var boxBorder = new Border
        {
            Child = box, Padding = new(12, 10, 12, 10), CornerRadius = new(10), BorderThickness = new(1),
            BorderBrush = U.Brush("BorderBrush"), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .08),
        };
        AutomationProperties.SetName(boxBorder, targetLabel.Text);
        AutomationProperties.SetLabeledBy(boxBorder, targetLabel);
        target.Children.Add(boxBorder);
        content.Children.Add(target);
        content.Children.Add(_endpointMissing);
        content.Children.Add(_reviewLocal); content.Children.Add(_reviewS3Note);
        var policies = new StackPanel { Spacing = 8 };
        var legend = Strong(T("web.admin.settings.storage.policy.legend"));
        AutomationProperties.SetHeadingLevel(legend, AutomationHeadingLevel.Level3);
        policies.Children.Add(legend);
        foreach (var radio in _policies) policies.Children.Add(radio);
        content.Children.Add(policies);
        var notesTitle = Strong(T("web.admin.settings.storage.notes.title"), 12); notesTitle.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Amber500);
        var notes = new StackPanel(); notes.Children.Add(notesTitle); notes.Children.Add(_notes);
        content.Children.Add(new Border
        {
            Child = notes, Padding = new(16), CornerRadius = new(10), BorderThickness = new(1),
            BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, .25), Background = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, .05),
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(_reviewCancel); actions.Children.Add(_reviewQueue);
        content.Children.Add(actions);
        return content;
    }

    private void RenderReview(AdminStorageSettingsState state)
    {
        var review = state.Owner?.IsCurrent == true ? state.Review : null;
        if (review == null || state.ArtworkStorage is not { } status || !_active) { HideReview(); return; }
        if (_review == null) ShowReview();
        if (_review == null) return;
        var plan = R.Plan(state, status, review.Backend, state.SourceHealth, state.SourceHealthFailed);
        _review!.Title = T(plan.PrivateOnly ? "web.admin.settings.storage.dialog.titlePrivate" : "web.admin.settings.storage.dialog.titleArtwork");

        var sourceUsesS3 = R.CurrentSourceUsesS3(state, status);
        var unavailable = plan.SourceHealthUnavailable;
        _sourceCheckingBox.Visibility = sourceUsesS3 && state.SourceHealthPending ? Visibility.Visible : Visibility.Collapsed;
        _sourceFailed.Visibility = sourceUsesS3 && !state.SourceHealthPending && unavailable ? Visibility.Visible : Visibility.Collapsed;
        _sourceOk.Visibility = sourceUsesS3 && !state.SourceHealthPending && !unavailable && state.SourceHealth != null ? Visibility.Visible : Visibility.Collapsed;
        if (_sourceFailed.Visibility == Visibility.Visible)
            U.UpdateLiveText(_sourceFailedText, state.SourceHealth?.Message ?? T("web.admin.settings.storage.dialog.sourceCheckFailed"));
        _sourceRetry.Content = T(state.SourceHealthFetching ? "web.admin.settings.storage.dialog.checking" : "web.admin.settings.storage.dialog.retryCheck");
        AutomationProperties.SetName(_sourceRetry, _sourceRetry.Content.ToString());
        _sourceRetry.IsEnabled = !state.SourceHealthFetching;

        string Bucket(string key) => state.Get(key);
        string Endpoint(string key) => R.JsTrim(state.Get(key));
        var privateBucket = R.JsTrim(state.Get("s3.private_bucket"));
        var privateLabel = privateBucket.Length > 0
            ? Endpoint("s3.private_endpoint").Length > 0
                ? T("web.admin.settings.storage.target.privateBucket", new { bucket = privateBucket, endpoint = Endpoint("s3.private_endpoint") })
                : T("web.admin.settings.storage.target.privateBucketNoEndpoint", new { bucket = privateBucket })
            : T(review.Backend == "local" ? "web.admin.settings.storage.target.noPrivateBucketLocal" : "web.admin.settings.storage.target.noPrivateBucketS3");
        var publicBucket = Bucket("s3.public_bucket");
        var publicEndpoint = Endpoint("s3.public_endpoint");
        var publicLabel = publicBucket.Length > 0
            ? publicEndpoint.Length > 0 ? T("web.admin.settings.storage.target.publicBucket", new { bucket = publicBucket, endpoint = publicEndpoint })
                : T("web.admin.settings.storage.target.publicBucketNoEndpoint", new { bucket = publicBucket })
            : publicEndpoint.Length > 0 ? T("web.admin.settings.storage.target.publicNoBucket", new { endpoint = publicEndpoint })
                : T("web.admin.settings.storage.target.publicNoBucketNoEndpoint");
        _targetTitle.Text = T(plan.PrivateOnly ? "web.admin.settings.storage.dialog.targetPrivate"
            : review.Backend == "local" ? "web.admin.settings.storage.dialog.targetLocal" : "web.admin.settings.storage.dialog.targetS3");
        _targetLine.Text = plan.PrivateOnly ? privateLabel : review.Backend == "local" ? T("web.admin.settings.storage.dialog.targetLocalSource") : publicLabel;
        var privateLine = review.Backend == "s3" && plan.PrivateLocationChanging && !plan.PrivateOnly;
        _targetPrivateLine.Text = privateLine ? privateLabel : "";
        _targetPrivateLine.Visibility = privateLine ? Visibility.Visible : Visibility.Collapsed;
        _endpointMissing.Visibility = plan.PrivateEndpointMissing ? Visibility.Visible : Visibility.Collapsed;

        _reviewLocal.Visibility = !plan.PrivateOnly && review.Backend == "local" ? Visibility.Visible : Visibility.Collapsed;
        _reviewS3Note.Visibility = !plan.PrivateOnly && review.Backend == "s3" ? Visibility.Visible : Visibility.Collapsed;
        _syncing = true;
        try
        {
            if (_reviewLocalPath.Text != review.LocalPath) _reviewLocalPath.Text = review.LocalPath;
            _reviewLocalPath.IsEnabled = !state.Queuing;
            var rows = R.PolicyRows(plan.PrivateOnly, plan.PrivateLocationChanging);
            for (var index = 0; index < rows.Count; index++)
            {
                var (value, titleKey, descriptionKey) = rows[index];
                var disabled = value != "start_fresh" && plan.CopyPolicyUnavailable;
                var radio = _policies[index];
                radio.Tag = value;
                _policyTitles[index].Text = T(titleKey);
                _policyDescriptions[index].Text = T(descriptionKey);
                _policyReasons[index].Text = disabled ? T(plan.SourceHealthUnavailable ? "web.admin.settings.storage.policy.unavailableSource" : "web.admin.settings.storage.policy.unavailableNoPrivateBucket") : "";
                _policyReasons[index].Visibility = disabled ? Visibility.Visible : Visibility.Collapsed;
                AutomationProperties.SetName(radio, T(titleKey));
                AutomationProperties.SetHelpText(radio, T(descriptionKey) + (disabled ? " " + _policyReasons[index].Text : ""));
                AutomationProperties.SetAutomationId(radio, "StoragePolicy_" + value);
                radio.IsEnabled = !disabled && !state.Queuing;
                radio.IsChecked = review.Policy == value;
            }
        }
        finally { _syncing = false; }

        _notes.Children.Clear();
        foreach (var key in R.NoteKeys(plan, review.Backend, R.CurrentSourceIsS3(status), review.Policy))
        {
            var bullet = new Grid { ColumnSpacing = 6 };
            bullet.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); bullet.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var dot = U.Text("•", 12, true); AutomationProperties.SetAccessibilityView(dot, AccessibilityView.Raw);
            var text = U.Text(T(key), 12, true); Grid.SetColumn(text, 1);
            bullet.Children.Add(dot); bullet.Children.Add(text);
            _notes.Children.Add(bullet);
        }

        _reviewCancel.IsEnabled = !state.Queuing;
        _reviewQueue.Content = T(state.Queuing ? "web.admin.settings.storage.dialog.queuing" : "web.admin.settings.storage.dialog.queue");
        AutomationProperties.SetName(_reviewQueue, _reviewQueue.Content.ToString());
        _reviewQueue.IsEnabled = AdminStorageSettingsController.CanQueue(state);
    }

    private void ShowReview()
    {
        if (_dialog != null || XamlRoot == null) return;
        _reviewContent ??= ReviewContent();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Content = new ScrollViewer { Content = _reviewContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
        // sm:max-w-2xl. The dialog owns its Cancel/Queue actions so their order and states do not depend on template internals.
        dialog.Resources["ContentDialogMaxWidth"] = 672d;
        AutomationProperties.SetAutomationId(dialog, "StorageTransitionReview");
        dialog.Closing += (_, args) => { if (_active && _controller?.State.Queuing == true) args.Cancel = true; };
        dialog.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_review, dialog)) return;
            // Escape or light dismissal: close the review without queuing anything.
            _review = null;
            if (dialog.Content is ScrollViewer viewer) viewer.Content = null;
            _controller?.CloseReview();
        };
        _review = dialog;
        _ = ShowAsync(dialog);
    }

    private async Task ShowAsync(ContentDialog dialog)
    {
        try { await dialog.ShowAsync(); }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another dialog owns the root: close the review; the draft stays staged for a later Save.
            if (!ReferenceEquals(_review, dialog)) return;
            _review = null;
            if (dialog.Content is ScrollViewer viewer) viewer.Content = null;
            _controller?.CloseReview();
        }
    }

    private void HideReview()
    {
        if (_review is not { } dialog) return;
        _review = null;
        dialog.Hide();
        // Detach the shared parts so the next review can host them.
        if (dialog.Content is ScrollViewer viewer) viewer.Content = null;
    }
}
