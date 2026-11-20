using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Delete, restore and add-to-other-libraries: the official DeleteRowDialog, DeleteRowsDialog, RestoreDialog and AddToOtherLibrariesDialog.</summary>
public sealed partial class AdminHomeSectionsPage
{
    private static Style? Resource(string key) => Application.Current.Resources.TryGetValue(key, out var value) ? value as Style : null;

    /// <summary>Runs <paramref name="work"/> as the dialog's primary action without letting the dialog close; the work decides by returning true.</summary>
    private static void KeepOpenWhile(ContentDialog dialog, Func<ContentDialog, Task<bool>> work)
    {
        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            try
            {
                sender.IsPrimaryButtonEnabled = false;
                if (await work(sender)) { args.Cancel = false; }
            }
            finally { sender.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
    }

    private async Task<bool> ShowDialogAsync(ContentDialog dialog, MonitorAuthorityLease owner)
    {
        if (_dialog != null || !_active || !owner.IsCurrent) return false;
        _dialog = dialog;
        using var cancelled = owner.Token.Register(() => DispatcherQueue.TryEnqueue(() => { if (ReferenceEquals(_dialog, dialog)) dialog.Hide(); }));
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { return false; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    // ── Delete ───────────────────────────────────────────────

    private async Task DeleteAsync(IReadOnlyList<string> ids)
    {
        var controller = _controller; if (controller?.CanWrite != true || _dialog != null || ids.Count == 0) return;
        var revision = _editor == null ? ++_dialogRevision : _dialogRevision;
        AdminHomeSectionsDeletePlan plan;
        // Each row's version is read before the confirmation, so a row that changes meanwhile fails instead of going.
        try { plan = await controller.PrepareDeletesAsync(ids); }
        catch (Exception) { if (_active && revision == _dialogRevision) Toasts?.Error(T("web.admin.homeRows.page.loadRowsFailed")); return; }
        if (!_active || revision != _dialogRevision || !ReferenceEquals(controller, _controller)) return;
        var owner = controller.State.Owner!; var count = ids.Count; var many = count > 1;
        if (plan.Sessions.Count == 0) { await RunDeleteAsync(controller, plan, ids, null); return; }
        var first = plan.Sessions[0].Row;
        var conflict = U.Text(T("web.home.rows.delete.conflict"), 13); conflict.Visibility = Visibility.Collapsed;
        var error = U.Text("", 13); error.Visibility = Visibility.Collapsed; AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(U.Text(many ? T("web.home.rows.deleteMany.description", AdminHomeSectionsText.Count(count, ("page", PageLabel))) : T("web.home.rows.delete.description", new { page = PageLabel })));
        panel.Children.Add(conflict); panel.Children.Add(error); panel.Children.Add(U.Text(T("web.home.rows.cantUndo"), 13, true));
        var dialog = new ContentDialog
        {
            Title = many ? T("web.home.rows.deleteMany.title", AdminHomeSectionsText.Count(count)) : T("web.home.rows.deleteTitle", new { row = RowTitle(first) }), Content = panel,
            PrimaryButtonText = many ? T("web.home.rows.deleteMany.confirm", AdminHomeSectionsText.Count(count)) : T("web.home.rows.deleteRow"),
            CloseButtonText = AdminHomeSectionsText.T("common.cancel"), DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot, PrimaryButtonStyle = Resource("DestructiveButtonStyle")
        };
        var deleteLabel = dialog.PrimaryButtonText; var reloading = false;
        KeepOpenWhile(dialog, async sender =>
        {
            if (reloading)
            {
                try { plan = await controller.PrepareDeletesAsync(ids); reloading = false; conflict.Visibility = Visibility.Collapsed; sender.PrimaryButtonText = deleteLabel; }
                catch (Exception) { error.Text = T("web.admin.homeRows.page.loadRowsFailed"); error.Visibility = Visibility.Visible; }
                return false;
            }
            error.Visibility = Visibility.Collapsed;
            var result = await controller.DeleteAsync(plan, (done, total) => DispatcherQueue.TryEnqueue(() => { if (many) sender.PrimaryButtonText = T("web.admin.homeRows.delete.progress", new { done = (long)done, total = (long)total }); }));
            if (!_active || !owner.IsCurrent) return true;
            if (many || result.FailedIds.Count == 0 || result.Unknown) { Report(result, ids, many); return true; }
            if (result.Conflict) { reloading = true; conflict.Visibility = Visibility.Visible; sender.PrimaryButtonText = T("web.home.rows.delete.reloadRow"); }
            else { error.Text = result.FirstError ?? T("web.admin.homeRows.delete.failed"); error.Visibility = Visibility.Visible; }
            return false;
        });
        await ShowDialogAsync(dialog, owner);
    }

    /// <summary>A delete with nothing left to confirm (every row was already gone): still reads the page again.</summary>
    private async Task RunDeleteAsync(AdminHomeSectionsController controller, AdminHomeSectionsDeletePlan plan, IReadOnlyList<string> ids, Action<int, int>? progress)
    { var result = await controller.DeleteAsync(plan, progress); if (_active) Report(result, ids, ids.Count > 1); }

    private void Report(AdminHomeSectionsDeleteResult result, IReadOnlyList<string> ids, bool many)
    {
        _selection.Clear(); foreach (var id in result.FailedIds) _selection.Add(id);
        if (result.FailedIds.Count == 0) { CloseEditor(); }
        if (!many) { if (result.FailedIds.Count == 0) CloseEditor(); return; }
        var requested = ids.Count; var deleted = result.Deleted; var failed = result.FailedIds.Count;
        if (failed == 0) Toasts?.Success(AdminHomeSectionsText.T("web.admin.common.sections.deleted", AdminHomeSectionsText.Count(deleted)));
        else if (deleted > 0) Toasts?.Warning(AdminHomeSectionsText.T("web.admin.common.sections.partiallyDeleted", AdminHomeSectionsText.Count(requested, ("deleted", deleted))) + " " + result.FirstError);
        else Toasts?.Error(AdminHomeSectionsText.T("web.admin.common.sections.deleteFailed", AdminHomeSectionsText.Count(failed)) + " " + result.FirstError);
    }

    // ── Restore ──────────────────────────────────────────────

    private static string JoinNames(IReadOnlyList<string> names) => string.Join(", ", names);
    private static string ListTitles(IReadOnlyList<string> titles)
    {
        var rest = titles.Count - 3; string At(int index) => index < titles.Count ? titles[index] : "";
        if (rest > 0) return T("web.home.rows.restore.listMore", new { first = At(0), second = At(1), third = At(2), more_count = (long)rest });
        if (titles.Count == 3) return T("web.home.rows.restore.listTriple", new { first = At(0), second = At(1), third = At(2) });
        if (titles.Count == 2) return T("web.home.rows.restore.listPair", new { first = At(0), second = At(1) });
        return At(0);
    }

    private async Task RestoreAsync()
    {
        var controller = _controller; if (controller?.CanWrite != true || _dialog != null) return;
        var revision = ++_dialogRevision;
        // The dialog names what the page's current version holds, and the restore is checked against that same version.
        await controller.ReloadAsync();
        if (!_active || revision != _dialogRevision || !ReferenceEquals(controller, _controller) || !controller.CanWrite || controller.State.Snapshot is not { } first) return;
        var snapshot = first; var owner = controller.State.Owner!; var home = _scope.Kind == "home";
        var others = _libraries.Where(library => library.Id != _scope.LibraryId).ToList(); var otherLabels = others.Select(library => library.Label).ToList();
        var resetSupported = controller.State.Capabilities?.ResetProfiles == true;
        bool all = false, reset = false;
        var content = new StackPanel { Spacing = 14 };
        var conflict = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        var error = U.Text("", 13); error.Visibility = Visibility.Collapsed; AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, PrimaryButtonText = T("web.home.rows.restore.confirm"), CloseButtonText = AdminHomeSectionsText.T("common.cancel"),
            DefaultButton = ContentDialogButton.Close, PrimaryButtonStyle = Resource("DestructiveButtonStyle") };
        string Target() => !(all && others.Count > 0) ? (home ? "home" : "library") : (home ? "homeAll" : "other");
        void Build()
        {
            var target = new { target = Target(), page = PageLabel }; var rows = snapshot.Rows;
            dialog.Title = T("web.home.rows.restore.title", target);
            content.Children.Clear(); content.Children.Add(U.Text(T("web.home.rows.restore.description", target)));
            var bullets = new StackPanel { Spacing = 6 }; AutomationProperties.SetName(bullets, T("web.home.rows.whatChanges"));
            var values = new { target = home ? "home" : "library", page = PageLabel, row_count = (long)rows.Count };
            bullets.Children.Add(U.Text("• " + (rows.Count == 0 ? T("web.home.rows.restore.rowsEmpty", values) : T("web.home.rows.restore.rows", values)), 13));
            var collections = rows.Where(row => row.SectionType is "collection" or "collection_posters").Select(RowTitle).ToList();
            if (collections.Count == 1) bullets.Children.Add(U.Text("• " + T("web.home.rows.restore.collectionOne", new { row = collections[0] }), 13));
            else if (collections.Count > 1) bullets.Children.Add(U.Text("• " + T("web.home.rows.restore.collectionMany", new { rows = ListTitles(collections) }), 13));
            var libraries = JoinNames(otherLabels); string? other;
            if (all && others.Count > 0) other = T(home ? "web.home.rows.restore.allFromHome" : "web.home.rows.restore.allFromLibrary", new { libraries });
            else if (home) other = others.Count > 0 ? T("web.home.rows.restore.librariesUnchanged", new { libraries }) : null;
            else other = others.Count > 0 ? T("web.home.rows.restore.homeAndLibrariesUnchanged", new { libraries }) : T("web.home.rows.restore.homeUnchanged");
            if (other != null) bullets.Children.Add(U.Text("• " + other, 13));
            content.Children.Add(bullets);
            if (others.Count > 0)
                content.Children.Add(Option(T(home ? "web.home.rows.restore.alsoEveryLibrary" : "web.home.rows.restore.alsoOtherLibraries"), T("web.home.rows.restore.alsoHelp", new { libraries }), null,
                    "HomeSectionsRestoreAll", all && others.Count > 0, true, value => { all = value; Build(); }));
            var resetOn = resetSupported && reset;
            content.Children.Add(Option(T("web.home.rows.restore.resetLabel", target), T(resetOn ? "web.home.rows.restore.resetHelpOn" : "web.home.rows.restore.resetHelpOff", target),
                resetSupported ? null : T("web.home.rows.restore.resetUnsupported"), "HomeSectionsRestoreReset", resetOn, resetSupported, value => { reset = value; Build(); }));
            content.Children.Add(conflict); content.Children.Add(error); content.Children.Add(U.Text(T("web.home.rows.cantUndo"), 13, true));
        }
        var reload = Action(T("web.home.rows.reloadRows"), "HomeSectionsRestoreReload");
        conflict.Children.Add(U.Text(T("web.home.rows.restore.conflict"), 13)); conflict.Children.Add(reload);
        reload.Click += async (_, _) =>
        {
            reload.IsEnabled = false;
            try
            {
                await controller.ReloadAsync();
                if (!_active || !controller.CanWrite || controller.State.Snapshot is not { } fresh) { dialog.Hide(); return; }
                snapshot = fresh; conflict.Visibility = Visibility.Collapsed; dialog.IsPrimaryButtonEnabled = true; Build();
            }
            finally { reload.IsEnabled = true; }
        };
        dialog.Content = new ScrollViewer { Content = content, MaxHeight = 560 }; Build();
        KeepOpenWhile(dialog, async _ =>
        {
            error.Visibility = Visibility.Collapsed;
            var targets = all ? others.Select(library => new AdminHomeSectionScope("library", library.Id)).ToArray() : null;
            var result = await controller.RestoreAsync(snapshot, resetSupported && reset, targets);
            if (!_active || !owner.IsCurrent) return true;
            if (result.PrimaryRestored) { ReportRestore(others, all, result); return true; }
            var state = controller.State;
            if (state.Failure == AdminHomeSectionsFailure.Conflict) { conflict.Visibility = Visibility.Visible; dialog.IsPrimaryButtonEnabled = false; return false; }
            if (state.Failure == AdminHomeSectionsFailure.Refused) { error.Text = state.Error ?? T("web.admin.homeRows.restore.failed"); error.Visibility = Visibility.Visible; return false; }
            return true;
        });
        await ShowDialogAsync(dialog, owner);
    }

    private FrameworkElement Option(string label, string help, string? note, string id, bool value, bool enabled, Action<bool> changed)
    {
        var grid = new Grid { ColumnSpacing = 16, Padding = new(12), BorderThickness = new(1), BorderBrush = U.Brush("MutedBrush"), CornerRadius = new(14) };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 4 }; text.Children.Add(U.Text(label, 14)); text.Children.Add(U.Text(help, 13, true)); if (note != null) text.Children.Add(U.Text(note, 13, true));
        var toggle = Toggle(id, value, changed); toggle.IsEnabled = enabled; AutomationProperties.SetName(toggle, label);
        Grid.SetColumn(toggle, 1); grid.Children.Add(text); grid.Children.Add(toggle); return grid;
    }

    private void ReportRestore(IReadOnlyList<AdminHomeSectionChoice> others, bool all, AdminHomeSectionsRestoreResult result)
    {
        var home = _scope.Kind == "home"; var failures = result.Failures; var alsoCount = all ? others.Count : 0;
        var restored = alsoCount - failures.Count;
        var title = restored <= 0 ? T("web.admin.homeRows.restore.done", new { page = PageLabel })
            : T(home ? "web.admin.homeRows.restore.doneWithLibraryPages" : "web.admin.homeRows.restore.doneWithOtherLibraryPages", AdminHomeSectionsText.Count(restored, ("page", PageLabel)));
        if (failures.Count == 0) { Toasts?.Success(title); return; }
        var kept = failures.Count(failure => !failure.Unknown); var unknown = failures.Count - kept;
        var outcomes = new List<string>();
        if (kept > 0) outcomes.Add(T(home ? "web.admin.homeRows.restore.libraryPagesKept" : "web.admin.homeRows.restore.otherLibraryPagesKept", AdminHomeSectionsText.Count(kept)));
        if (unknown > 0) outcomes.Add(T(home ? "web.admin.homeRows.restore.libraryPagesUnknown" : "web.admin.homeRows.restore.otherLibraryPagesUnknown", AdminHomeSectionsText.Count(unknown)));
        var description = string.Join(" ", failures.Select(failure =>
        {
            var label = others.FirstOrDefault(library => library.Id == failure.LibraryId)?.Label ?? T("web.admin.homeRows.restore.libraryFallback", new { id = failure.LibraryId });
            return failure.Unknown ? T("web.admin.homeRows.restore.pageUnknown", new { label })
                : T("web.admin.homeRows.restore.pageFailed", new { label, message = string.IsNullOrWhiteSpace(failure.Message) ? T("web.admin.homeRows.restore.failed") : failure.Message });
        }));
        Toasts?.Warning(string.Join(" ", new[] { title }.Concat(outcomes)) + " " + description);
    }

    // ── Add to other libraries ───────────────────────────────

    private async Task CopyAsync(AdminHomeSection row)
    {
        var controller = _controller; if (controller?.CanWrite != true || _dialog != null || _scope.LibraryId == null) return;
        var revision = ++_dialogRevision;
        AdminHomeSectionSession? read;
        try { read = await controller.OpenEditAsync(row.Id); }
        catch (Exception) { if (_active) Toasts?.Error(T("web.admin.homeRows.page.loadRowsFailed")); return; }
        if (!_active || revision != _dialogRevision || read == null) return;
        var session = read; var title = RowTitle(session.Row); var owner = session.Owner;
        var draft = AdminHomeSectionDraft.From(session.Row);
        var changed = U.Text(T("web.home.rows.addToOtherLibraries.changed", new { row = title }), 13); changed.Visibility = Visibility.Collapsed;
        var error = U.Text("", 13); error.Visibility = Visibility.Collapsed;
        if (AdminHomeSectionsController.Fingerprint(session.Row) != AdminHomeSectionsController.Fingerprint(row)) { Toasts?.Error(T("web.home.rows.addToOtherLibraries.changed", new { row = title })); return; }
        var pages = AdminHomeSectionsRules.CopyTargetPages(draft.SectionType, draft.Config, LibraryPages(), _scope.LibraryId);
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(U.Text(T("web.home.rows.addToOtherLibraries.description"), 13));
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = T("web.home.rows.addToOtherLibraries.title", new { row = title }), CloseButtonText = AdminHomeSectionsText.T("common.cancel"), DefaultButton = ContentDialogButton.Primary };
        void Label() { dialog.PrimaryButtonText = chosen.Count == 0 ? T("web.home.rows.addToOtherLibraries.confirmNone") : T("web.home.rows.addToOtherLibraries.confirm", AdminHomeSectionsText.Count(chosen.Count)); dialog.IsPrimaryButtonEnabled = chosen.Count > 0 && changed.Visibility == Visibility.Collapsed; }
        foreach (var page in pages)
        {
            var current = page.Id == _scope.LibraryId;
            var box = new CheckBox { Content = current ? page.Label + " · " + T("web.home.rows.addToOtherLibraries.alreadyHere") : page.Label, IsEnabled = !current, MinWidth = 0 };
            AutomationProperties.SetAutomationId(box, "HomeSectionsCopyPage_" + page.Id);
            box.Checked += (_, _) => { if (chosen.Count < AdminHomeSectionsRules.BulkLibraryLimit) chosen.Add(page.Id); else box.IsChecked = false; Label(); };
            box.Unchecked += (_, _) => { chosen.Remove(page.Id); Label(); };
            panel.Children.Add(box);
        }
        panel.Children.Add(U.Text(T(session.Row.Featured ? "web.home.rows.addToOtherLibraries.helpHero" : "web.home.rows.addToOtherLibraries.helpRow"), 13, true));
        panel.Children.Add(changed); panel.Children.Add(error); dialog.Content = new ScrollViewer { Content = panel, MaxHeight = 420 }; Label();
        KeepOpenWhile(dialog, async _ =>
        {
            error.Visibility = Visibility.Collapsed;
            if (!await controller.CopyAsync(session, chosen.ToArray()))
            {
                var state = controller.State;
                if (state.Failure == AdminHomeSectionsFailure.Conflict) { changed.Visibility = Visibility.Visible; Label(); return false; }
                if (state.Failure == AdminHomeSectionsFailure.Refused) { error.Text = state.Error ?? T("web.home.rows.addToOtherLibraries.failed"); error.Visibility = Visibility.Visible; return false; }
                return true;
            }
            Toasts?.Success(T("web.home.rows.addToOtherLibraries.added", new { pages = AdminBackupRestoreModel.JoinNames(pages.Where(page => chosen.Contains(page.Id)).Select(page => page.Label).ToArray()) }));
            return true;
        });
        await ShowDialogAsync(dialog, owner);
    }
}
