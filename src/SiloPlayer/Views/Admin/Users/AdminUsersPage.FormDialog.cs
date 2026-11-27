using Microsoft.UI.Xaml.Automation.Peers;
using SiloPlayer.Controls;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    /// <summary>What one Users form or confirmation dialog needs from the page: its content, how to build the write, and what to say when the server answers otherwise.</summary>
    private sealed class FormDialogSpec
    {
        internal required string Title { get; init; }
        /// <summary>The form (or the confirmation text), shown under the error line.</summary>
        internal required FrameworkElement Body { get; init; }
        internal string Primary { get; init; } = AppStrings.Get("common.save", fallback: "Save");
        internal string Saving { get; init; } = T("web.admin.users.detail.access.card.saving");
        internal string Cancel { get; init; } = AppStrings.Get("common.cancel", fallback: "Cancel");
        /// <summary>Red primary button: the action cannot be undone (delete, revoke, sign out).</summary>
        internal bool Destructive { get; init; }
        /// <summary>The account read when the window opened; a conflict replaces it after "Reload and review".</summary>
        internal required AdminUserReview Review { get; set; }
        /// <summary>Checks the form (showing each problem at its field) and builds the write; null means there is nothing to send yet.</summary>
        internal required Func<AdminUserReview, AdminUsersWrite?> Build { get; init; }
        /// <summary>False keeps Save off: nothing changed, or a row has nothing valid to replace a saved value.</summary>
        internal Func<bool>? CanSave { get; init; }
        /// <summary>Called with a callback to run whenever the form changes.</summary>
        internal Action<Action>? OnChanged { get; init; }
        /// <summary>The line under the form ("2 unsaved changes · Role, Can sign in"); empty hides it.</summary>
        internal Func<string>? Footnote { get; init; }
        /// <summary>After "Reload and review": reads the account (and any record the write checks) again; the form keeps what the admin changed.</summary>
        internal Func<Task<AdminUserReview>>? Reload { get; init; }
        internal Action<AdminUserReview>? Rebase { get; init; }
        /// <summary>Wording of the unconfirmed-write message: an account that may or may not exist yet, or a change that may or may not be saved.</summary>
        internal bool Created { get; init; }
        /// <summary>Puts a refusal at its field (true), or leaves it for the error line (false).</summary>
        internal Func<ApiException, Task<bool>>? PlaceFieldError { get; init; }
        /// <summary>The widest the dialog may be (the official <c>sm:max-w-lg</c> is 512, <c>sm:max-w-2xl</c> 672).</summary>
        internal double MaxWidth { get; init; } = 512;
        internal string? AutomationId { get; init; }
        internal string FailureText { get; init; } = T("web.admin.users.detail.access.card.saveFailed");
        /// <summary>Runs once the dialog is on screen (focus, a first measure).</summary>
        internal Action? Opened { get; init; }
        /// <summary>A Save label that follows the form (Create link / Send invite) and the matching "working" label.</summary>
        internal Func<string>? PrimaryLabel { get; init; }
        internal Func<string>? SavingLabel { get; init; }
        /// <summary>What the window says when the outcome cannot be confirmed; the account wording by default.</summary>
        internal string? UnconfirmedText { get; init; }
        /// <summary>After a successful write: the content that replaces the form (the invitation link), shown under Done; null closes the window as usual.</summary>
        internal Func<AdminUsersDocument, FrameworkElement?>? ShowResult { get; init; }
    }

    /// <summary>
    /// The official form dialog: a title, the form, Cancel and Save (Save is red for a destructive action and reads "Saving…" while it runs). A refused write can be
    /// corrected and sent again; one whose outcome is unknown cannot be sent a second time from this window, and one the account changed under (another admin) keeps
    /// the admin's edits and offers "Reload and review" before it can be saved. Returns the server's answer, or null when nothing was saved.
    /// </summary>
    private async Task<AdminUsersDocument?> FormDialogAsync(FormDialogSpec spec, long generation)
    {
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return null;
        var error = AlertText();
        var conflict = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        var conflictText = Text(T("web.admin.users.detail.access.card.conflict"), 13);
        var reloadButton = ActionButton(T("web.admin.users.detail.access.card.reloadAndReview"), height: 28);
        conflict.Children.Add(conflictText); conflict.Children.Add(reloadButton);
        var conflictBox = new Border
        {
            Child = conflict, Padding = new(12, 8, 12, 8), CornerRadius = new(8), BorderThickness = new(1), Visibility = Visibility.Collapsed,
            Background = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, 0.05), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, 0.3),
        };
        AutomationProperties.SetLiveSetting(conflictBox, AutomationLiveSetting.Assertive);
        var footnote = Text("", 12, true); footnote.Visibility = Visibility.Collapsed;
        var content = new StackPanel { Spacing = 12 };
        // The scroll area is 14 px narrower than the dialog (its own padding and border): a form as wide as the dialog would be cut off at the right.
        double ContentWidth() => Math.Max(280, Math.Min(spec.MaxWidth - 48, (XamlRoot?.Size.Width ?? 800) - 96) - 14);
        content.MinWidth = ContentWidth();
        var root = XamlRoot;
        void Resized(XamlRoot sender, XamlRootChangedEventArgs args) => content.MinWidth = ContentWidth();
        if (root != null) root.Changed += Resized;
        content.Children.Add(error); content.Children.Add(conflictBox); content.Children.Add(spec.Body); content.Children.Add(footnote);
        if (spec.AutomationId != null) AutomationProperties.SetAutomationId(content, spec.AutomationId);
        var dialog = NewFormDialog(spec.Title, content, spec.Primary, spec.Cancel, spec.MaxWidth);
        if (spec.Destructive) dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"];
        dialog.DefaultButton = spec.Destructive ? ContentDialogButton.Close : ContentDialogButton.Primary;
        _dialog = dialog; _dialogDirty = true;
        var busy = false;
        var dialogCompleted = false;
        var blocked = false;
        var unknown = false;
        var needsReload = false;
        var resultShown = false;
        void SyncSave()
        {
            dialog.IsPrimaryButtonEnabled = !busy && !blocked && (spec.CanSave?.Invoke() ?? true);
            if (!busy && !resultShown && spec.PrimaryLabel != null) dialog.PrimaryButtonText = spec.PrimaryLabel();
            var note = spec.Footnote?.Invoke() ?? "";
            footnote.Text = note; footnote.Visibility = note.Length == 0 || blocked ? Visibility.Collapsed : Visibility.Visible;
        }
        spec.OnChanged?.Invoke(SyncSave);
        SyncSave();
        dialog.Closing += (_, args) => { if (busy) args.Cancel = true; };
        AdminUsersDocument? result = null;
        reloadButton.Click += async (_, _) =>
        {
            if (busy || spec.Reload == null) return;
            busy = true; reloadButton.IsEnabled = false;
            try
            {
                var fresh = await spec.Reload();
                if (dialogCompleted || !_workspace.IsCurrent(generation)) return;
                _workspace.AcceptReviewedReadback(generation);
                spec.Review = fresh; spec.Rebase?.Invoke(fresh);
                blocked = false; conflictBox.Visibility = Visibility.Collapsed; Show(error, null);
            }
            catch (OperationCanceledException) { if (!_workspace.IsCurrent(generation)) dialog.Hide(); else Show(error, T("web.admin.users.detail.access.card.reloadFailed")); }
            catch (Exception) { if (_workspace.IsCurrent(generation)) Show(error, T("web.admin.users.detail.access.card.reloadFailed")); }
            finally { busy = false; reloadButton.IsEnabled = true; if (!dialogCompleted) SyncSave(); }
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (resultShown) return;
            args.Cancel = true;
            if (busy || blocked) return;
            Show(error, null);
            AdminUsersWrite? write;
            try { write = spec.Build(spec.Review); }
            catch (ArgumentException exception) { Show(error, exception.Message); return; }
            if (write == null) return;
            var deferral = args.GetDeferral();
            busy = true; SyncSave();
            dialog.PrimaryButtonText = spec.SavingLabel?.Invoke() ?? spec.Saving;
            spec.Body.IsHitTestVisible = false;
            try
            {
                if (!_workspace.IsCurrent(generation)) return;
                result = await _workspace.WriteAsync(generation, _api, spec.Review, write);
                if (dialogCompleted || !_workspace.IsCurrent(generation)) { await result.DiscardViewAsAsync(); result = null; return; }
                busy = false;
                if (spec.ShowResult?.Invoke(result) is { } view)
                {
                    content.Children.Clear(); content.Children.Add(view);
                    resultShown = true; dialog.PrimaryButtonText = AppStrings.Get("common.done", fallback: "Done"); dialog.CloseButtonText = ""; dialog.IsPrimaryButtonEnabled = true;
                    return;
                }
                dialog.Hide();
            }
            catch (OperationCanceledException)
            {
                if (!_workspace.IsCurrent(generation)) { busy = false; dialog.Hide(); }
                else Unconfirmed(_workspace.OutcomeUnknown, "The request didn't finish. Check your connection and try again.");
            }
            catch (AdminUsersReviewRequiredException) { Unconfirmed(_workspace.OutcomeUnknown); }
            catch (Exception exception)
            {
                if (_workspace.IsCurrent(generation))
                {
                    if (exception is ApiException refused && spec.PlaceFieldError != null && !dialogCompleted && await spec.PlaceFieldError(refused)) blocked = _workspace.NeedsReview;
                    else if (_workspace.OutcomeUnknown || _workspace.NeedsReview) Unconfirmed(_workspace.OutcomeUnknown);
                    else Show(error, RefusalText(exception, spec.FailureText));
                }
            }
            finally
            {
                spec.Body.IsHitTestVisible = true;
                busy = false;
                if (!dialogCompleted && !resultShown) { dialog.PrimaryButtonText = spec.PrimaryLabel?.Invoke() ?? spec.Primary; SyncSave(); }
                deferral.Complete();
            }

            // An outcome nobody can confirm blocks Save for good; an account another admin changed first keeps the edits and waits for "Reload and review".
            void Unconfirmed(bool outcomeUnknown, string? plain = null)
            {
                if (outcomeUnknown || _workspace.OutcomeUnknown || spec.Reload == null)
                {
                    blocked = true; unknown = true; needsReload = true;
                    Show(error, spec.UnconfirmedText ?? UnconfirmedWriteText(spec.Created));
                }
                else if (_workspace.NeedsReview) { blocked = true; needsReload = true; conflictBox.Visibility = Visibility.Visible; }
                else Show(error, plain ?? T("web.admin.users.detail.access.card.saveFailed"));
            }
        };
        var handedOff = false;
        try
        {
            spec.Opened?.Invoke();
            await dialog.ShowAsync();
            if (!_workspace.IsCurrent(generation)) return null;
            handedOff = true;
            // A window closed after a conflict or an unknown outcome reads the page again, which lifts the block on the next change.
            if (result == null && (needsReload || unknown)) { RequireGeneration(generation); await LoadAsync(_location); }
            return result;
        }
        finally
        {
            dialogCompleted = true;
            if (root != null) root.Changed -= Resized;
            if (ReferenceEquals(_dialog, dialog)) { _dialog = null; _dialogDirty = false; }
            if (!handedOff && result != null) await result.DiscardViewAsAsync();
        }
    }
}
