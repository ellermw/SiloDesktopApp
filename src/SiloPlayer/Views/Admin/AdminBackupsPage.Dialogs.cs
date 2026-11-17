using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Present = SiloPlayer.Core.Services.Admin.AdminBackupPresentation;
using UiKit = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminBackupsPage
{
    /// <summary>A dialog whose actions live in its content; <paramref name="build"/> receives a close callback.</summary>
    private async Task RunDialogAsync(string title, string id, MonitorAuthorityLease? owner, Func<Action, UIElement> build, Action? closed = null)
    {
        if (_dialog != null || XamlRoot == null) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title };
        dialog.Resources["ContentDialogMaxWidth"] = 560d;
        AutomationProperties.SetAutomationId(dialog, id);
        dialog.Content = new ScrollViewer { Content = build(() => dialog.Hide()), MaxHeight = 640, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _dialog = dialog;
        using var retired = owner?.Token.Register(() => DispatcherQueue.TryEnqueue(() => { if (ReferenceEquals(_dialog, dialog)) dialog.Hide(); }));
        try { await dialog.ShowAsync(); }
        catch (System.Runtime.InteropServices.COMException) { }
        finally
        {
            if (ReferenceEquals(_dialog, dialog)) _dialog = null;
            closed?.Invoke();
        }
    }

    private static StackPanel Footer(params Button[] buttons)
    {
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0) };
        foreach (var button in buttons) footer.Children.Add(button);
        return footer;
    }

    // ── Delete confirmations ─────────────────────────────────────────

    private async Task ConfirmDeleteBackupAsync(AdminBackup backup)
    {
        var controller = Controller; if (controller?.State.Owner is not { IsCurrent: true } owner) return;
        var description = T("web.admin.backups.table.deleteDescription", new { taken = AdminBackupText.DateTimeText(backup.CreatedAt!.Value) });
        if (await ShowDialogAsync(T("web.admin.backups.table.deleteTitle"), Text(description), T("web.admin.backups.table.deleteConfirm"), T("common.cancel"), true, owner, "BackupsDeleteDialog")
            && ReferenceEquals(controller, Controller) && owner.IsCurrent && ReferenceEquals(owner, controller.State.Owner))
            await controller.DeleteBackupAsync(backup.Id!, owner);
    }

    private async Task ConfirmDeleteUploadAsync(AdminBackupUpload upload, string label)
    {
        var controller = Controller; if (controller?.State.Owner is not { IsCurrent: true } owner) return;
        if (await ShowDialogAsync(T("web.admin.backups.uploads.deleteTitle"), Text(T("web.admin.backups.uploads.deleteDescription", new { label })),
                T("web.admin.backups.uploads.deleteConfirm"), T("common.cancel"), true, owner, "BackupsDeleteUploadDialog")
            && ReferenceEquals(controller, Controller) && owner.IsCurrent && ReferenceEquals(owner, controller.State.Owner))
            await controller.DeleteUploadAsync(upload.Id!, owner);
    }

    // ── Download ─────────────────────────────────────────────────────

    /// <summary>
    /// Official download dialog. Links stay only in this dialog; each file goes to a path the admin picks,
    /// streamed without account headers and verified before it replaces anything already there.
    /// </summary>
    private async Task OpenDownloadDialogAsync(AdminBackup backup)
    {
        var controller = Controller; if (controller?.State.Owner is not { IsCurrent: true } owner) return;
        var links = await controller.CreateDownloadLinksAsync(backup.Id!, "full");
        if (links == null || !ReferenceEquals(controller, Controller) || !_active) return;
        _downloadCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _downloadCancel = cancel;
        IReadOnlyList<AdminBackupDownloadLink> restoreSetLinks = [];
        var restoreSetPending = false;
        StackPanel? root = null;
        void Build(Action close)
        {
            root!.Children.Clear();
            var taken = AdminBackupText.DateTimeText(backup.CreatedAt!.Value);
            var description = links[0].ExpiresAt is { } expires
                ? T("web.admin.backups.download.descriptionWithExpiry", new { taken, hint = Present.DownloadHint(backup), time = AdminBackupText.TimeText(expires) })
                : T("web.admin.backups.download.description", new { taken, hint = Present.DownloadHint(backup) });
            root.Children.Add(Text(description, 14, true));
            var otherKey = Present.EncryptedWithOtherKey(backup);
            if (backup.Encrypted == true)
            {
                var key = otherKey ? backup.KeyCheck is { Length: > 0 } ? "web.admin.backups.download.encryptedOtherKeyWithCheck" : "web.admin.backups.download.encryptedOtherKey" : "web.admin.backups.download.encrypted";
                root.Children.Add(Rich(AdminBackupText.Runs(key, new { key_check = backup.KeyCheck ?? "" },
                    new Dictionary<string, string> { ["decrypt"] = Present.DecryptCommand, ["restore"] = Present.RestoreFolderCommand }), 13, true));
            }
            foreach (var link in links) root.Children.Add(DownloadRow(controller, backup, link, "full", cancel.Token));
            if (backup.Encrypted == true && Present.CanRestoreParts(backup))
            {
                if (restoreSetLinks.Count > 0) foreach (var link in restoreSetLinks) root.Children.Add(DownloadRow(controller, backup, link, "restore-set", cancel.Token));
                else
                {
                    var restoreSet = Action(T("web.admin.backups.download.restoreSet") + (restoreSetPending ? "…" : ""), "BackupsDownloadRestoreSet");
                    restoreSet.IsEnabled = !otherKey && !restoreSetPending;
                    restoreSet.Click += async (_, _) =>
                    {
                        restoreSetPending = true; Build(close);
                        var fresh = await controller.CreateDownloadLinksAsync(backup.Id!, "restore-set");
                        restoreSetPending = false;
                        if (fresh != null) restoreSetLinks = fresh;
                        if (ReferenceEquals(_downloadCancel, cancel)) Build(close);
                    };
                    root.Children.Add(restoreSet);
                    if (otherKey) root.Children.Add(Text(Present.OtherKeyReason(), 12, true));
                }
            }
            var done = Action(T("common.done"), "BackupsDownloadClose");
            done.Click += (_, _) => close();
            root.Children.Add(Footer(done));
        }
        await RunDialogAsync(T("web.admin.backups.download.title"), "BackupsDownloadDialog", owner, close =>
        {
            root = new StackPanel { Spacing = 12 };
            Build(close);
            return root;
        }, () =>
        {
            // Closing stops any transfer and forgets the signed links.
            cancel.Cancel();
            if (ReferenceEquals(_downloadCancel, cancel)) _downloadCancel = null;
            links = []; restoreSetLinks = [];
        });
    }

    private FrameworkElement DownloadRow(AdminBackupsController controller, AdminBackup backup, AdminBackupDownloadLink link, string scope, CancellationToken token)
    {
        var owner = controller.State.Owner;
        var row = new Grid { ColumnSpacing = 12, Padding = new(12, 8, 12, 8), CornerRadius = new(10), BorderThickness = new(1), BorderBrush = UiKit.Brush("BorderBrush") };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var info = new StackPanel { Spacing = 2 };
        var name = Text(link.Name!); name.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
        info.Children.Add(name);
        info.Children.Add(Text(DashboardFormat.FileSize(link.SizeBytes, "0 B"), 12, true));
        var progress = Text("", 12, true); progress.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(progress, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(progress, "BackupsDownloadState-" + link.Name);
        info.Children.Add(progress);
        row.Children.Add(info);
        var button = Action(AdminBackupText.Visible("web.admin.backups.download.buttonFile", new { name = link.Name }), "BackupsDownloadFile-" + link.Name);
        AutomationProperties.SetName(button, AdminBackupText.Accessible("web.admin.backups.download.buttonFile", new { name = link.Name }));
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            var running = true;
            void Show(string text) { progress.Text = text; progress.Visibility = Visibility.Visible; }
            try
            {
                // The suggested name is a known object name: no path separators can come from the server.
                var path = await PickSavePathAsync(link.Name!);
                if (path == null || token.IsCancellationRequested || !controller.IsCurrent(owner)) return;
                var reporter = new Progress<long>(bytes => DispatcherQueue.TryEnqueue(() =>
                {
                    if (running && !token.IsCancellationRequested) Show(Present.ProgressLabel(bytes, Math.Max(1, link.SizeBytes ?? 1)));
                }));
                var result = await controller.DownloadAsync(backup, link, scope, path, reporter, token);
                Show(T(result.HashVerified ? "configure.native.backups.download.savedVerified" : "configure.native.backups.download.saved"));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || !controller.IsCurrent(owner)) { }
            catch (Exception error) when (error is TimeoutException or OperationCanceledException) { Show(T("configure.native.backups.download.timeout")); }
            catch (AdminBackupLinkExpiredException) { Show(T("configure.native.backups.download.expired")); }
            catch (AdminBackupDownloadMismatchException) { Show(T("configure.native.backups.download.mismatch")); }
            catch (Exception) { Show(T("configure.native.backups.download.failed")); }
            finally { running = false; if (!token.IsCancellationRequested) button.IsEnabled = true; }
        };
        Grid.SetColumn(button, 1); row.Children.Add(button);
        return row;
    }

    // ── Encryption key ───────────────────────────────────────────────

    /// <summary>Official KeyBox: why the key matters, its check value, how to check a copy, and the "I keep a copy" box.</summary>
    private static (StackPanel Box, CheckBox Kept) KeyBox(string keyCheck, Action changed)
    {
        var box = new StackPanel { Spacing = 8, Padding = new(16), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, .3),
            Background = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, .05) };
        var heading = Heading(T("web.admin.backups.key.heading"), 14, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3);
        box.Children.Add(heading);
        box.Children.Add(Text(T("web.admin.backups.key.explanation"), 13));
        box.Children.Add(Text(T("web.admin.backups.key.check"), 12, true));
        var check = Text(keyCheck, 16); check.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"); check.IsTextSelectionEnabled = true;
        AutomationProperties.SetName(check, T("web.admin.backups.key.check") + " " + keyCheck);
        AutomationProperties.SetAutomationId(check, "BackupsKeyCheck");
        box.Children.Add(check);
        box.Children.Add(Rich(AdminBackupText.Runs("web.admin.backups.key.checkHelp"), 13, true));
        var kept = new CheckBox { Content = T("web.admin.backups.key.kept") };
        AutomationProperties.SetName(kept, T("web.admin.backups.key.kept"));
        AutomationProperties.SetAutomationId(kept, "BackupsKeyKept");
        AutomationProperties.SetIsRequiredForForm(kept, true);
        kept.Checked += (_, _) => changed();
        kept.Unchecked += (_, _) => changed();
        box.Children.Add(kept);
        return (box, kept);
    }

    private static void Toast(string message, bool error)
    {
        var toast = App.Services.GetService(typeof(SiloPlayer.Services.ToastService)) as SiloPlayer.Services.ToastService;
        if (error) toast?.Error(message); else toast?.Success(message);
    }

    private async Task OpenConfirmKeyAsync()
    {
        var controller = Controller; if (controller?.State is not { Owner.IsCurrent: true, List: { } list } state) return;
        if (!Present.KeyConfirmationNeeded(list)) return;
        var keyCheck = list.KeyCheck!;
        await RunDialogAsync(T("web.admin.backups.key.confirmTitle"), "BackupsConfirmKeyDialog", state.Owner, close =>
        {
            var root = new StackPanel { Spacing = 12 };
            root.Children.Add(Text(T("web.admin.backups.key.confirmDescription"), 14, true));
            var cancel = Action(T("common.cancel"), "BackupsConfirmKeyCancel");
            var confirm = Action(T("web.admin.backups.confirmKey.button"), "BackupsConfirmKeySubmit", primary: true);
            CheckBox? keptBox = null;
            var (box, kept) = KeyBox(keyCheck, () => confirm.IsEnabled = keptBox?.IsChecked == true);
            keptBox = kept;
            confirm.IsEnabled = false;
            root.Children.Add(box);
            cancel.Click += (_, _) => close();
            confirm.Click += async (_, _) =>
            {
                confirm.IsEnabled = false;
                var problem = await controller.AcknowledgeKeyAsync(keyCheck);
                if (problem == null)
                {
                    Toast(T("web.admin.backups.key.confirmed") + " " + T("web.admin.backups.key.confirmedDescription"), false);
                    close();
                }
                else
                {
                    Toast(T("web.admin.backups.key.confirmFailed") + " " + problem, true);
                    confirm.IsEnabled = kept.IsChecked == true;
                }
            };
            root.Children.Add(Footer(cancel, confirm));
            return root;
        });
    }

    // ── S3 setup ─────────────────────────────────────────────────────

    private async Task OpenS3SetupAsync()
    {
        var controller = Controller; if (controller?.State is not { Owner.IsCurrent: true, List: { } list } state) return;
        if (state.Capabilities?.Destinations?.Contains("s3") != true) return;
        var initial = Present.S3FieldsFromSettings(key => state.PersistedValues.GetValueOrDefault(key, ""));
        var keyCheck = list.KeyCheck!;
        PasswordBox? secretBox = null;
        await RunDialogAsync(T("web.admin.backups.s3.title"), "BackupsS3Dialog", state.Owner, close =>
        {
            var root = new StackPanel { Spacing = 12 };
            root.Children.Add(Text(T("web.admin.backups.s3.description"), 14, true));
            TextBox Field(string labelKey, string value, string id, bool mono, string placeholder = "")
            {
                var box = new TextBox { Header = T(labelKey), Text = value, PlaceholderText = placeholder, IsSpellCheckEnabled = false };
                if (mono) box.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
                AutomationProperties.SetName(box, T(labelKey)); AutomationProperties.SetAutomationId(box, id);
                root.Children.Add(box);
                return box;
            }
            var endpoint = Field("web.admin.backups.s3.endpoint", initial.Endpoint, "BackupsS3Endpoint", true, Present.EndpointExample);
            var region = Field("web.admin.backups.s3.region", initial.Region, "BackupsS3Region", false);
            var bucket = Field("web.admin.backups.s3.bucket", initial.Bucket, "BackupsS3Bucket", true);
            var folder = Field("web.admin.backups.s3.folder", initial.KeyPrefix, "BackupsS3Folder", true);
            var access = Field("web.admin.backups.s3.accessKey", initial.AccessKey, "BackupsS3AccessKey", true);
            secretBox = new PasswordBox { Header = T("web.admin.backups.s3.secretKey"), PlaceholderText = state.SecretConfigured ? T("web.admin.backups.s3.secretSet") : "" };
            AutomationProperties.SetName(secretBox, T("web.admin.backups.s3.secretKey")); AutomationProperties.SetAutomationId(secretBox, "BackupsS3Secret");
            root.Children.Add(secretBox);
            var pathStyle = new ToggleSwitch { Header = T("web.admin.backups.s3.pathStyle"), IsOn = initial.PathStyle };
            AutomationProperties.SetName(pathStyle, T("web.admin.backups.s3.pathStyle")); AutomationProperties.SetAutomationId(pathStyle, "BackupsS3PathStyle");
            root.Children.Add(pathStyle);
            AdminBackupS3Fields Fields() => new(endpoint.Text, region.Text, bucket.Text, folder.Text, access.Text, secretBox!.Password, pathStyle.IsOn);
            (AdminBackupS3Test Body, AdminBackupS3TestResult Result)? outcome = null;
            var testing = false;
            var test = Action(T("web.admin.backups.s3.test"), "BackupsS3Test");
            var result = new StackPanel();
            AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite);
            var testRow = new StackPanel { Spacing = 8 }; testRow.Children.Add(test); testRow.Children.Add(result);
            root.Children.Add(testRow);
            var cancel = Action(T("common.cancel"), "BackupsS3Cancel");
            var save = Action(T("web.admin.backups.s3.save"), "BackupsS3Save", primary: true);
            var blockerText = Text("", 12, true); AutomationProperties.SetAutomationId(blockerText, "BackupsS3Blocker");
            var saving = false;
            var reloading = false;
            var reload = Action(T("configure.native.backups.s3.reload"), "BackupsS3Reload");
            var recovery = Text("", 13, true);
            CheckBox? keptBox = null;
            var (keyBox, kept) = KeyBox(keyCheck, () => Refresh());
            keptBox = kept;
            root.Children.Add(keyBox);
            void Refresh()
            {
                var fields = Fields();
                var needsReload = controller.State.Conflict || controller.State.SettingsOutcomeUnknown;
                reload.Visibility = needsReload || reloading ? Visibility.Visible : Visibility.Collapsed;
                reload.IsEnabled = !saving && !testing && !reloading;
                test.IsEnabled = !testing && !saving && !reloading && !needsReload && fields.Endpoint.Trim().Length > 0 && fields.Bucket.Trim().Length > 0;
                result.Children.Clear();
                if (Present.CurrentS3Test(fields, outcome) is { } current && !testing) result.Children.Add(Status(current.Message, current.Ok ? "ok" : "warn", "BackupsS3Result"));
                var blocker = needsReload ? T("configure.native.backups.settingsChanged") : Present.S3SaveBlocker(fields, outcome, keptBox?.IsChecked == true);
                blockerText.Text = blocker ?? "";
                blockerText.Visibility = blocker == null ? Visibility.Collapsed : Visibility.Visible;
                save.IsEnabled = blocker == null && !saving && !reloading && !testing;
                if (blocker != null) AutomationProperties.SetHelpText(save, blocker); else AutomationProperties.SetHelpText(save, "");
            }
            foreach (var box in new[] { endpoint, region, bucket, folder, access }) box.TextChanged += (_, _) => Refresh();
            secretBox.PasswordChanged += (_, _) => Refresh();
            pathStyle.Toggled += (_, _) => Refresh();
            test.Click += async (_, _) =>
            {
                var fields = Fields(); var body = Present.S3TestBody(fields);
                testing = true; Refresh();
                var answer = await controller.TestS3Async(fields);
                testing = false;
                if (answer != null) outcome = (body, answer);
                Refresh();
            };
            reload.Click += async (_, _) =>
            {
                reloading = true;
                outcome = null; // The stored secret can change even when its configured flag stays true.
                kept.IsChecked = false;
                Refresh();
                var loaded = await controller.ReloadS3SettingsAsync();
                reloading = false;
                if (!state.Owner.IsCurrent || !ReferenceEquals(controller, Controller)) return;
                secretBox!.PlaceholderText = controller.State.SecretConfigured ? T("web.admin.backups.s3.secretSet") : "";
                recovery.Text = T(loaded ? "configure.native.backups.s3.reloaded" : "configure.native.backups.s3.reloadFailed");
                Refresh();
            };
            cancel.Click += (_, _) => close();
            save.Click += async (_, _) =>
            {
                saving = true; Refresh();
                var problem = await controller.SaveS3Async(Fields(), keyCheck);
                saving = false;
                if (problem == null) { close(); return; }
                Toast(problem, true);
                Refresh();
            };
            Refresh();
            root.Children.Add(blockerText);
            root.Children.Add(recovery);
            root.Children.Add(Footer(cancel, reload, save));
            return root;
        }, () => { if (secretBox != null) secretBox.Password = ""; });
    }

    // ── Upload ───────────────────────────────────────────────────────

    private async Task OpenUploadDialogAsync()
    {
        var controller = Controller; if (controller?.State is not { Owner.IsCurrent: true, Capabilities: { Upload: true } capabilities } state) return;
        MonitorAuthorityLease lease;
        try { lease = _authority.Capture(); } catch (OperationCanceledException) { return; }
        if (lease.Epoch != state.Owner.Epoch) { lease.Dispose(); return; }
        RetireUpload();
        var session = new AdminBackupUploadSession(App.Services.GetService(typeof(AdminBackupsApi)) as AdminBackupsApi ?? throw new InvalidOperationException("Backups API is not registered."),
            lease, capabilities.MaxUploadBytes ?? AdminBackupIdentifiers.MaxUploadBytes);
        _upload = session;
        StackPanel? root = null;
        Action? closeDialog = null;
        string? fileProblem = null;
        string? restoreUploadId = null;
        async Task ChooseFileAsync(Func<Task<(string Name, Stream Stream)?>> pick)
        {
            Stream? unowned = null;
            try
            {
                var picked = await pick();
                if (picked is not { } file) return;
                unowned = file.Stream;
                if (!ReferenceEquals(_upload, session) || !lease.IsCurrent) return;
                if (!unowned.CanRead || !unowned.CanSeek) throw new IOException("A readable, seekable file is required.");
                fileProblem = null;
                var running = session.StartAsync(file.Name, unowned);
                unowned = null; // StartAsync now owns it, including cancellation and retirement.
                await running;
                _ = controller.RefreshUploadsAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (ReferenceEquals(_upload, session) && lease.IsCurrent)
                {
                    fileProblem = T("configure.native.backups.fileUnreadable");
                    Render();
                }
            }
            finally
            {
                try { unowned?.Dispose(); } catch { /* A failed/retired selection must not escape the UI handler. */ }
            }
        }
        void Render()
        {
            if (root == null || closeDialog == null || !ReferenceEquals(_upload, session)) return;
            var phase = session.Phase;
            root.Children.Clear();
            root.Children.Add(Text(T("web.admin.backups.upload.description"), 14, true));
            if (fileProblem != null) root.Children.Add(Alert(fileProblem));
            var footer = new List<Button>();
            switch (phase.Step)
            {
                case AdminBackupUploadStep.Choose:
                    var drop = new StackPanel { Spacing = 12, Padding = new(24, 40, 24, 40), HorizontalAlignment = HorizontalAlignment.Stretch };
                    var dropText = Text(T("web.admin.backups.upload.drop"), 14, true); dropText.TextAlignment = TextAlignment.Center; dropText.HorizontalAlignment = HorizontalAlignment.Center;
                    drop.Children.Add(dropText);
                    var choose = Action(T("web.admin.backups.upload.choose"), "BackupsUploadChoose"); choose.HorizontalAlignment = HorizontalAlignment.Center;
                    choose.Click += async (_, _) => await ChooseFileAsync(PickUploadAsync);
                    drop.Children.Add(choose);
                    var area = new Border { Child = drop, CornerRadius = new(12), BorderThickness = new(1), BorderBrush = UiKit.Brush("BorderBrush"), AllowDrop = true };
                    area.DragOver += (_, args) => args.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                    area.Drop += async (_, args) =>
                    {
                        await ChooseFileAsync(async () =>
                        {
                            if (!args.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return null;
                            var items = await args.DataView.GetStorageItemsAsync();
                            if (items.FirstOrDefault() is not Windows.Storage.StorageFile dropped || !ReferenceEquals(_upload, session)) return null;
                            return (dropped.Name, new FileStream(dropped.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous));
                        });
                    };
                    root.Children.Add(area);
                    var cancelChoose = Action(T("common.cancel"), "BackupsUploadCancel");
                    cancelChoose.Click += async (_, _) => { await session.DiscardAsync(); _ = controller.RefreshUploadsAsync(); closeDialog(); };
                    footer.Add(cancelChoose);
                    break;
                case AdminBackupUploadStep.Uploading:
                    var sent = phase.Upload?.ReceivedBytes ?? 0;
                    var status = new StackPanel { Spacing = 6 };
                    var line = new Grid { ColumnSpacing = 12 };
                    line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    var fileName = Text(phase.FileName ?? "", 14); fileName.TextTrimming = TextTrimming.CharacterEllipsis; fileName.TextWrapping = TextWrapping.NoWrap;
                    line.Children.Add(fileName);
                    var amount = Text(Present.ProgressLabel(sent, phase.FileSize), 13, true); Grid.SetColumn(amount, 1); line.Children.Add(amount);
                    status.Children.Add(line);
                    var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = Present.ProgressPercent(sent, phase.FileSize) };
                    AutomationProperties.SetName(bar, T("web.admin.backups.upload.progressLabel")); AutomationProperties.SetAutomationId(bar, "BackupsUploadProgress");
                    status.Children.Add(bar);
                    if (phase.Notice is { } notice) status.Children.Add(Text(T(notice), 12, true));
                    AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
                    root.Children.Add(status);
                    var cancelUpload = Action(T("common.cancel"), "BackupsUploadCancel");
                    cancelUpload.Click += async (_, _) => { await session.DiscardAsync(); _ = controller.RefreshUploadsAsync(); closeDialog(); };
                    footer.Add(cancelUpload);
                    break;
                case AdminBackupUploadStep.Checking:
                    var checking = Text(T("web.admin.backups.upload.checking"));
                    AutomationProperties.SetLiveSetting(checking, AutomationLiveSetting.Polite); AutomationProperties.SetAutomationId(checking, "BackupsUploadChecking");
                    root.Children.Add(checking);
                    break;
                case AdminBackupUploadStep.Opened:
                    var opened = Text(Present.OpenedSentence(phase.Upload!)); AutomationProperties.SetAutomationId(opened, "BackupsUploadOpened");
                    root.Children.Add(opened);
                    var next = Action(T("web.admin.backups.upload.chooseWhat"), "BackupsUploadChooseWhat", primary: true);
                    next.Click += (_, _) => { restoreUploadId = phase.Upload!.Id!; closeDialog(); };
                    footer.Add(next);
                    break;
                case AdminBackupUploadStep.Failed:
                    var copy = AdminBackupUploadSession.CopyOf(phase.Failure!);
                    var alert = new StackPanel { Spacing = 6 };
                    AutomationProperties.SetLiveSetting(alert, AutomationLiveSetting.Assertive); AutomationProperties.SetAutomationId(alert, "BackupsUploadFailure");
                    var title = Text(copy.Title); title.FontWeight = FontWeights.Medium; alert.Children.Add(title);
                    alert.Children.Add(Text(copy.Body, 13, true));
                    if (copy.Command != null) { var command = Text(copy.Command, 12); command.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"); command.IsTextSelectionEnabled = true; alert.Children.Add(command); }
                    if (copy.Detail != null) alert.Children.Add(Text(copy.Detail, 12, true));
                    root.Children.Add(alert);
                    var dismiss = Action(T(copy.Resumable ? "common.cancel" : "web.admin.backups.upload.close"), "BackupsUploadDismiss");
                    dismiss.Click += async (_, _) => { await session.DiscardAsync(); _ = controller.RefreshUploadsAsync(); closeDialog(); };
                    footer.Add(dismiss);
                    if (copy.Resumable && phase.Upload != null)
                    {
                        var again = Action(T(phase.Failure!.Kind == "check_failed" ? "web.admin.backups.upload.checkAgain" : "common.tryAgain"), "BackupsUploadTryAgain", primary: true);
                        again.Click += async (_, _) => { await session.ResumeAsync(); _ = controller.RefreshUploadsAsync(); };
                        footer.Add(again);
                    }
                    else
                    {
                        var another = Action(T("web.admin.backups.upload.chooseAnother"), "BackupsUploadChooseAnother", primary: true);
                        another.Click += async (_, _) => { await session.DiscardAsync(); _ = controller.RefreshUploadsAsync(); };
                        footer.Add(another);
                    }
                    break;
            }
            if (footer.Count > 0) root.Children.Add(Footer([.. footer]));
        }
        session.Changed += () => DispatcherQueue.TryEnqueue(Render);
        await RunDialogAsync(T("web.admin.backups.upload.title"), "BackupsUploadDialog", lease, close =>
        {
            root = new StackPanel { Spacing = 12 };
            closeDialog = close;
            Render();
            return root;
        }, () =>
        {
            // A closed dialog starts over next time; whatever it was sending stops (a checked upload stays listed).
            if (ReferenceEquals(_upload, session))
            {
                RetireUpload();
            }
        });
        // Navigation may need the settings leave guard's dialog; wait for upload dialog cleanup first.
        if (restoreUploadId != null && _active && state.Owner.IsCurrent && ReferenceEquals(controller, Controller) && ReferenceEquals(state.Owner, controller.State.Owner))
            AdminShellPage.Open(Present.UploadRestoreRequest(restoreUploadId));
    }
}
