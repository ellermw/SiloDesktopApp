using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using static SiloPlayer.Views.Admin.AdminLibrariesUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The page's modal dialogs: the delete and empty-root cleanup confirmations (WebUI
/// <c>ConfirmDialog</c>), the metadata refresh mode dialog (<c>LibraryRefreshDialog</c>)
/// and the reused item match tool. Only one dialog shows at a time; each is bound to an
/// action lease and closes when that authority retires.
/// </summary>
public sealed partial class AdminLibrariesPage
{
    private ContentDialog? _dialog;

    /// <summary>
    /// A modal dialog as wide as the WebUI dialog (sm:max-w-lg 512 px, sm:max-w-2xl 672 px) within the
    /// window: the content takes the dialog width less its padding and border, so wide rows never clip.
    /// </summary>
    private ContentDialog NewDialog(string title, UIElement content, double maxWidth = 512)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content };
        dialog.Resources["ContentDialogMaxWidth"] = maxWidth;
        var viewport = XamlRoot?.Size.Width ?? maxWidth;
        if (content is FrameworkElement element) element.Width = Math.Max(240, Math.Min(maxWidth, viewport - 32) - 58);
        return dialog;
    }

    private static Button DialogButton(string label, string automationId, string style)
    {
        var button = new Button
        {
            Content = label, Style = (Style)Application.Current.Resources[style], MinWidth = 0, Height = 36, Padding = new(12, 0, 12, 0), CornerRadius = new(10),
            UseSystemFocusVisuals = true,
        };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    /// <summary>Shows a dialog owned by <paramref name="lease"/>; it hides if that authority retires. False when another dialog is open.</summary>
    private async Task<bool> ShowOwnedAsync(ContentDialog dialog, MonitorAuthorityLease lease, Control? initialFocus = null)
    {
        if (_dialog != null || !lease.IsCurrent || XamlRoot == null) return false;
        _dialog = dialog;
        var queue = DispatcherQueue;
        if (initialFocus != null) dialog.Opened += (_, _) => queue.TryEnqueue(() => { if (ReferenceEquals(_dialog, dialog)) initialFocus.Focus(FocusState.Keyboard); });
        using var registration = lease.Token.Register(() => queue.TryEnqueue(() => { if (ReferenceEquals(_dialog, dialog)) dialog.Hide(); }));
        try { await dialog.ShowAsync(); return true; }
        catch (System.Runtime.InteropServices.COMException) { return false; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    /// <summary>A two-button confirmation; Escape and light dismissal never confirm.</summary>
    private async Task<bool> ConfirmAsync(LibraryUiOwner owner, string title, string description, string confirmLabel, string automationId)
    {
        if (!owner.IsCurrent) return false;
        MonitorAuthorityLease lease;
        try { lease = owner.Controller.CaptureAction(owner.Lease); }
        catch (OperationCanceledException) { return false; }
        using (lease)
        {
            var confirmed = false;
            var content = new StackPanel { Spacing = 24 };
            content.Children.Add(Text(description, 14, true));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = DialogButton(Core.Localization.AppStrings.Get("common.cancel", fallback: "Cancel"), automationId + "Cancel", "OutlineButtonStyle");
            var confirm = DialogButton(confirmLabel, automationId + "Confirm", "DestructiveButtonStyle");
            actions.Children.Add(cancel); actions.Children.Add(confirm);
            content.Children.Add(actions);
            var dialog = NewDialog(title, content);
            cancel.Click += (_, _) => dialog.Hide();
            confirm.Click += (_, _) => { if (!owner.IsCurrent || !lease.IsCurrent) return; confirmed = true; dialog.Hide(); };
            if (!await ShowOwnedAsync(dialog, lease, cancel)) return false;
            return confirmed && lease.IsCurrent && owner.IsCurrent && owner.Controller.IsActionCurrent(lease);
        }
    }

    private async Task ConfirmDeleteAsync(AdminLibrary library, LibraryUiOwner owner)
    {
        if (await ConfirmAsync(owner, T("web.admin.libraries.deleteDialog.title"), T("web.admin.libraries.deleteDialog.description", ("name", library.Name)),
                T("web.admin.libraries.deleteDialog.confirm"), "LibrariesDelete") && owner.IsCurrent)
            await owner.Controller.DeleteAsync(library.Id, owner.Lease);
    }

    private async Task ConfirmCleanupAsync(AdminLibrary library, LibraryUiOwner owner)
    {
        if (await ConfirmAsync(owner, T("web.admin.libraries.emptyRootDialog.title"), T("web.admin.libraries.emptyRootDialog.description", ("name", library.Name)),
                T("web.admin.libraries.emptyRootDialog.confirm"), "LibrariesCleanup") && owner.IsCurrent)
            await owner.Controller.ConfirmCleanupAsync(library.Id, owner.Lease);
    }

    /// <summary>
    /// Quick (missing only) or full metadata refresh. The dialog stays up until the queued
    /// request settles; its options and Cancel are disabled meanwhile.
    /// </summary>
    private async Task ShowRefreshDialogAsync(AdminLibrary library, LibraryUiOwner owner)
    {
        var controller = owner.Controller;
        if (!owner.IsCurrent) return;
        MonitorAuthorityLease lease;
        try { lease = controller.CaptureAction(owner.Lease); }
        catch (OperationCanceledException) { return; }
        using (lease)
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(Text(T("web.admin.common.libraryEditor.refresh.description", ("name", library.Name)), 14, true));
            ContentDialog? dialog = null;
            Button? cancelButton = null;
            var options = new List<Button>();
            Button Option(string mode, string icon, string titleKey, string descriptionKey)
            {
                var grid = new Grid { ColumnSpacing = 12 };
                grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                var glyph = new ContentControl { Content = Icon(icon, 20, Brush("SecondaryTextBrush")), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 2, 0, 0) };
                grid.Children.Add(glyph);
                var text = new StackPanel { Spacing = 4 };
                var heading = Text(T(titleKey), 14); heading.FontWeight = FontWeights.SemiBold;
                text.Children.Add(heading);
                text.Children.Add(Text(T(descriptionKey), 14, true));
                Grid.SetColumn(text, 1); grid.Children.Add(text);
                var button = new Button
                {
                    Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(16),
                    CornerRadius = new(12), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Background = Tint("SurfaceBrush", 0.9),
                    Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                };
                AutomationProperties.SetName(button, T(titleKey));
                AutomationProperties.SetAutomationId(button, "LibrariesRefresh" + (mode == "full" ? "All" : "Missing"));
                button.Click += async (_, _) =>
                {
                    if (!owner.IsCurrent || !lease.IsCurrent) return;
                    foreach (var option in options) option.IsEnabled = false;
                    glyph.Content = Spinning("loader", 20, Brush("SecondaryTextBrush"));
                    cancelButton!.IsEnabled = false;
                    var accepted = await controller.RefreshMetadataAsync(library.Id, mode, owner.Lease);
                    _ = accepted;
                    // WebUI onSettled: the dialog closes once the request settles, whatever the outcome.
                    dialog?.Hide();
                };
                options.Add(button);
                return button;
            }
            content.Children.Add(Option("quick", "refresh-cw", "web.admin.common.libraryEditor.refresh.missingTitle", "web.admin.common.libraryEditor.refresh.missingDescription"));
            content.Children.Add(Option("full", "database-backup", "web.admin.common.libraryEditor.refresh.allTitle", "web.admin.common.libraryEditor.refresh.allDescription"));
            cancelButton = DialogButton(Core.Localization.AppStrings.Get("common.cancel", fallback: "Cancel"), "LibrariesRefreshCancel", "OutlineButtonStyle");
            cancelButton.HorizontalAlignment = HorizontalAlignment.Right;
            cancelButton.Margin = new(0, 4, 0, 0);
            content.Children.Add(cancelButton);
            dialog = NewDialog(T("web.admin.common.libraryEditor.refresh.title"), content);
            cancelButton.Click += (_, _) => dialog.Hide();
            // Keep the dialog up while the queued request settles.
            dialog.Closing += (_, args) => { if (controller.State.RefreshStarting == library.Id && lease.IsCurrent) args.Cancel = true; };
            await ShowOwnedAsync(dialog, lease, options[0]);
        }
    }

    /// <summary>
    /// The existing item match tool (MatchItemDialog, admin item match endpoints) bound to an
    /// action lease of this page: it closes and drops candidates when that authority retires.
    /// Returns whether a match was applied.
    /// </summary>
    private async Task<bool> ShowMatchAsync(string contentId, string title, int year, string contentType, string libraryId, LibraryUiOwner owner)
    {
        var controller = owner.Controller;
        if (!owner.IsCurrent || _dialog != null) return false;
        MonitorAuthorityLease lease;
        try { lease = controller.CaptureAction(owner.Lease); }
        catch (OperationCanceledException) { return false; }
        using (lease)
        {
            int? library = int.TryParse(libraryId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            var dialog = new MatchItemDialog(contentId, title, year > 0 ? year : null, contentType, library, null, null) { XamlRoot = XamlRoot };
            dialog.BindNavigation(lease.Token);
            if (!await ShowOwnedAsync(dialog, lease)) return false;
            if (!dialog.HasAppliedMatch || !owner.IsCurrent || !controller.IsActionCurrent(lease)) return false;
            await RefreshMatchDiagnosticsAsync(owner);
            return true;
        }
    }
}
