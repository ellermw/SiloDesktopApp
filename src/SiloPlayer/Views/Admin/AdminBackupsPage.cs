using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using UiKit = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Configure > Server > Backups (official AdminBackups.tsx) and its two restore routes
/// (official AdminBackupRestore.tsx): <c>/admin/backups</c>, <c>/admin/backups/:id/restore</c>
/// and <c>/admin/backups/uploads/:uploadId/restore</c>. One registered page hosts the list
/// view or the restore view from its route segments. Each view owns its controller, timers
/// and dialogs; departure, suspension or an authority change retires all of them and clears
/// signed links, typed secrets and drafts.
/// </summary>
public sealed partial class AdminBackupsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = new() { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 32 };
    private readonly AdminMonitorAuthority _authority;
    private AdminRouteRequest? _location;
    private AdminBackupRestoreSourceRef? _restoreSource;
    private ContentDialog? _dialog;
    private bool _active, _opening;
    private int _widthClass = -1;

    /// <summary>Fixture seam: chooses a save path for a suggested file name. Production uses the OS save picker.</summary>
    internal static Func<string, Task<string?>>? PickSavePathOverride;
    /// <summary>Fixture seam: chooses a restore set to upload. Production uses the OS open picker.</summary>
    internal static Func<Task<(string Name, Stream Stream)?>>? PickUploadOverride;

    public FrameworkElement ScrollContent => _scrollContent;
    public AdminBackupsController? Controller { get; private set; }
    public AdminBackupRestoreController? RestoreController { get; private set; }
    public bool IsRestoreView => _restoreSource != null;
    public bool RequiresLeaveConfirmation => _leave?.RequiresConfirmation == true;

    public AdminBackupsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        _authority = App.Services.GetRequiredService<AdminMonitorAuthority>();
        AutomationProperties.SetAutomationId(this, "AdminBackupsPage");
        _scrollContent.Children.Add(_body);
        _scroller.Content = _scrollContent;
        _root.Children.Add(_scroller);
        Content = _root;
        // The content is as wide as the part of the scroller the page can use: the viewport (the scroll bar takes its own width) less the gutters
        // the shell gives the content, or its right edge would sit past the edge of the page on a narrow window.
        void FitContent()
        {
            var available = (_scroller.ViewportWidth > 0 ? _scroller.ViewportWidth : _scroller.ActualWidth) - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
            if (available <= 0) return;
            var target = Math.Min(_scrollContent.MaxWidth, available);
            if (double.IsNaN(_scrollContent.Width) || Math.Abs(_scrollContent.Width - target) > .5) _scrollContent.Width = target;
            // The body fills that width (up to its own maximum) rather than shrinking to its widest child: the restore sections take the room the summary leaves.
            var bodyWidth = Math.Min(_body.MaxWidth, target);
            if (double.IsNaN(_body.Width) || Math.Abs(_body.Width - bodyWidth) > .5) _body.Width = bodyWidth;
        }
        _scroller.SizeChanged += (_, _) => FitContent();
        _scroller.ViewChanged += (_, _) => FitContent();
        Loaded += (_, _) => Activate();
        Unloaded += (_, _) => Close();
        SizeChanged += (_, _) => { _saveBar.SetAvailableWidth(ActualWidth); RelayoutIfNeeded(); };
        // The content is resized after the page is, so a row's own width is only known from here.
        _scrollContent.SizeChanged += (_, _) => RelayoutIfNeeded();
    }

    private static string T(string key, object? values = null) => AdminText.T(key, values);

    /// <summary>
    /// The layouts change at 640 (phone header and table), 768 (version column) and 1024 (summary beside the sections) of the window, and at 672 and 832
    /// of a restore row's own width: draw again when a breakpoint is crossed.
    /// </summary>
    private void RelayoutIfNeeded()
    {
        var widthClass = LayoutClass();
        if (widthClass == _widthClass) return;
        var first = _widthClass < 0;
        _widthClass = widthClass;
        if (first || !_active) return;
        if (_restoreSource != null) QueueRestoreRender(); else QueueListRender();
    }

    /// <summary>
    /// The window's width: the official page's media queries (<c>max-sm</c>, <c>md</c>, <c>lg</c>) ask the viewport, not the page, so a window
    /// 1024 px wide gets the summary beside the sections though the sidebar leaves the page less.
    /// </summary>
    private double ViewportWidth => XamlRoot?.Size.Width is > 0 and var width ? width : ActualWidth;

    /// <summary>Which side of each breakpoint the window and a restore row are on, so a resize redraws only when a layout changes.</summary>
    private int LayoutClass() => ViewportWidth <= 0 ? 0 : (ViewportWidth < 640 ? 1 : 0) | (ViewportWidth < 768 ? 2 : 0) | (ViewportWidth < 1024 ? 4 : 0) | (RowsWidth < 672 ? 8 : 0) | (RowsWidth < 832 ? 16 : 0) | 32;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _location = e.Parameter as AdminRouteRequest;
        _restoreSource = SourceOf(_location);
        base.OnNavigatedTo(e);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); base.OnNavigatedFrom(e); }

    /// <summary>The restore source a route names, or null for the list view.</summary>
    internal static AdminBackupRestoreSourceRef? SourceOf(AdminRouteRequest? request)
    {
        if (request is not { Route: AdminRoute.Backups }) return null;
        var segments = request.Segments;
        if (segments.Count == 2 && segments[1] == "restore") return AdminBackupRestoreSourceRef.Backup(segments[0]);
        if (segments.Count == 3 && segments[0] == "uploads" && segments[2] == "restore") return AdminBackupRestoreSourceRef.FromUpload(segments[1]);
        return null;
    }

    private void Activate()
    {
        if (_active) return;
        _active = true;
        _body.Children.Clear();
        if (XamlRoot != null) XamlRoot.Changed += OnXamlRootChanged;
        _authority.AvailabilityChanged += OnAuthorityChanged;
        if (_restoreSource != null) ActivateRestore();
        else ActivateList();
        _ = OpenAsync();
    }

    private async Task OpenAsync()
    {
        if (_opening || !_active || !_authority.IsAvailable) return;
        _opening = true;
        try
        {
            if (_restoreSource is { } source && RestoreController is { } restore)
            {
                // Identifiers outside the official shapes open nothing; the page says the backup could not be opened.
                var valid = source.Upload ? AdminBackupIdentifiers.IsUploadId(source.Id) : AdminBackupIdentifiers.IsBackupId(source.Id);
                if (valid) await restore.OpenAsync(source, _location?.Get("job"));
                else RenderRestoreInvalid();
            }
            else if (Controller is { } controller) await controller.OpenAsync();
        }
        finally
        {
            _opening = false;
            var needsOpen = _restoreSource != null ? RestoreController?.State.Owner == null && RestoreSourceValid : Controller?.State.Owner == null;
            if (_active && _authority.IsAvailable && needsOpen) DispatcherQueue.TryEnqueue(() => { if (_active) _ = OpenAsync(); });
        }
    }

    private bool RestoreSourceValid => _restoreSource is { } source && (source.Upload ? AdminBackupIdentifiers.IsUploadId(source.Id) : AdminBackupIdentifiers.IsBackupId(source.Id));

    private void OnAuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        // Authority changes close dialogs (signed links, typed secrets, uploads) before reopening.
        CloseDialogs();
        if (_restoreSource == null ? Controller?.State.Owner == null : RestoreController?.State.Owner == null) _ = OpenAsync();
    });

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        var visible = sender.IsHostVisible;
        Controller?.SetVisible(visible);
        RestoreController?.SetVisible(visible);
    }

    public Task<bool> CanLeaveAsync()
    {
        if (Controller?.State is { Saving: true, Owner.IsCurrent: true }) return Task.FromResult(false);
        return _leave?.CanLeaveAsync(owner => UiKit.ConfirmDiscardAsync(this, owner, () => _dialog, dialog => _dialog = dialog)) ?? Task.FromResult(true);
    }

    public void SuspendAdminPage() => Close();

    private void Close()
    {
        if (!_active) return;
        _active = false;
        if (XamlRoot != null) XamlRoot.Changed -= OnXamlRootChanged;
        _authority.AvailabilityChanged -= OnAuthorityChanged;
        CloseDialogs();
        _dialog?.Hide(); _dialog = null;
        if (Controller != null)
        {
            Controller.Changed -= QueueListRender; Controller.Notified -= OnNotified; Controller.Saved -= OnSaved;
            Controller.Dispose(); Controller = null;
        }
        if (RestoreController != null)
        {
            RestoreController.Changed -= QueueRestoreRender; RestoreController.Notified -= OnNotified;
            RestoreController.JobChanged -= OnJobChanged; RestoreController.Finished -= OnSaved;
            RestoreController.Dispose(); RestoreController = null;
        }
        _leave = null;
        ClearListUi();
        _body.Children.Clear();
    }

    private void OnNotified(AdminBackupNotice notice) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        var toast = App.Services.GetService<ToastService>();
        var message = notice.Description is { Length: > 0 } description ? notice.Title + " " + description : notice.Title;
        if (notice.Error) toast?.Error(message); else toast?.Success(message);
    });

    private void OnSaved() => DispatcherQueue.TryEnqueue(() =>
    {
        if (_active && App.Services.GetService<NavigationService>()?.Frame?.Content is AdminShellPage shell) shell.RefreshSettingsStatus();
    });

    // ── Shared UI helpers ────────────────────────────────────────────

    private static TextBlock Text(string value, double size = 14, bool muted = false) => UiKit.Text(value, size, muted);

    private static TextBlock Heading(string value, double size = 15, AutomationHeadingLevel level = AutomationHeadingLevel.Level2)
    {
        var heading = Text(value, size);
        heading.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(heading, level);
        return heading;
    }

    /// <summary>Official rich text (b, muted, code, link, label, sr): links call <paramref name="link"/>; sr text is only in the accessible name.</summary>
    private static TextBlock Rich(IReadOnlyList<AdminBackupTextRun> runs, double size = 14, bool muted = false, Action? link = null)
    {
        var block = Text("", size, muted);
        foreach (var run in runs)
        {
            if (run.Tag == "sr") continue;
            Inline inline = new Run { Text = run.Text };
            switch (run.Tag)
            {
                case "b": inline = new Bold { Inlines = { new Run { Text = run.Text } } }; break;
                case "muted": ((Run)inline).Foreground = UiKit.Brush("SecondaryTextBrush"); break;
                case "code": ((Run)inline).FontFamily = new FontFamily("Consolas"); break;
                case "link" when link != null:
                    var hyperlink = new Hyperlink { Inlines = { new Run { Text = run.Text } } };
                    hyperlink.Click += (_, _) => link();
                    inline = hyperlink; break;
            }
            block.Inlines.Add(inline);
        }
        AutomationProperties.SetName(block, string.Concat(runs.Select(run => run.Text)));
        return block;
    }

    private static Button Action(string label, string id, bool primary = false)
    {
        var button = UiKit.Action(label, id, primary);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button;
    }

    /// <summary>Official SettingFieldStatus: warn (amber), ok (green) or muted.</summary>
    private static Grid Status(string text, string tone, string id = "")
    {
        var color = tone switch { "warn" => AdminMonitorUi.Amber400, "ok" => Windows.UI.Color.FromArgb(255, 52, 211, 153), _ => AdminMonitorUi.ColorOf("SecondaryTextColor", Windows.UI.Color.FromArgb(255, 160, 160, 170)) };
        var line = new Grid { ColumnSpacing = 6 };
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var label = Text(text, 13); label.Foreground = AdminMonitorUi.Solid(color);
        if (id.Length > 0) AutomationProperties.SetAutomationId(label, id);
        if (tone == "warn")
        {
            var icon = AdminMonitorUi.Icon("alert-triangle", 14, color);
            icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new(0, 3, 0, 0);
            line.Children.Add(icon);
        }
        Grid.SetColumn(label, 1); line.Children.Add(label);
        return line;
    }

    private static Border Surface(UIElement child) => new()
    {
        Child = child, Padding = new(24), CornerRadius = new(16), BorderThickness = new(1),
        BorderBrush = UiKit.Brush("BorderBrush"), Background = UiKit.Brush("SurfaceBrush"),
    };

    private static Border Alert(string message, Button? action = null)
    {
        var stack = new StackPanel { Spacing = 12 };
        var text = Text(message);
        stack.Children.Add(text);
        if (action != null) stack.Children.Add(action);
        var panel = Surface(stack);
        AutomationProperties.SetLiveSetting(panel, AutomationLiveSetting.Assertive);
        return panel;
    }

    private static Button Disclosure(string label, string id, bool open, Action toggle)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(AdminMonitorUi.Icon(open ? "chevron-down" : "chevron-right", 14, UiKit.Brush("SecondaryTextBrush")));
        content.Children.Add(Text(label, 13, true));
        var button = new Button { Content = content, Padding = new(4, 2, 4, 2), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new(0), MinHeight = 0 };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => toggle();
        return button;
    }

    private static CheckBox Check(string label, string id, bool? value, bool enabled, Action<bool> changed)
    {
        var box = new CheckBox { Content = label, IsChecked = value, IsEnabled = enabled, IsThreeState = false, MinWidth = 0 };
        AutomationProperties.SetName(box, label);
        AutomationProperties.SetAutomationId(box, id);
        // Checked/Unchecked fire for pointer, keyboard and automation toggles alike; values set before this never fire them.
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }

    /// <summary>Re-focuses the control that had focus before a section was rebuilt.</summary>
    private string? FocusedId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : null;

    private void RestoreFocus(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        var target = Find(_body, id);
        // The current restore step is a tab stop only while it holds focus.
        if (target is ContentControl { IsEnabled: true } step && id == "RestoreCurrentStep")
        {
            step.IsTabStop = true;
            // The rebuilt step list is in the tree one layout pass later.
            if (!step.IsLoaded)
            {
                RoutedEventHandler? loaded = null;
                loaded = (_, _) => { step.Loaded -= loaded; if (_active) step.Focus(FocusState.Programmatic); };
                step.Loaded += loaded;
                return;
            }
        }
        if (target is Control control && control.IsEnabled) control.Focus(FocusState.Programmatic);
    }

    private static DependencyObject? Find(DependencyObject root, string id)
    {
        if (AutomationProperties.GetAutomationId(root) == id) return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
            if (Find(VisualTreeHelper.GetChild(root, index), id) is { } found) return found;
        if (root is Panel panel)
            foreach (var child in panel.Children)
                if (Find(child, id) is { } found) return found;
        if (root is Border { Child: { } inner } && Find(inner, id) is { } nested) return nested;
        if (root is ContentControl { Content: DependencyObject content } && Find(content, id) is { } contained) return contained;
        return null;
    }

    /// <summary>A content dialog with owned actions; returns true for the primary action. One dialog at a time.</summary>
    private async Task<bool> ShowDialogAsync(string title, UIElement content, string primary, string close, bool destructive, MonitorAuthorityLease? owner,
        string id, Func<bool>? primaryEnabled = null)
    {
        if (_dialog != null || XamlRoot == null) return false;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = ContentDialogButton.Close,
        };
        if (destructive) dialog.PrimaryButtonStyle = DestructiveStyle();
        if (primaryEnabled != null) dialog.IsPrimaryButtonEnabled = primaryEnabled();
        AutomationProperties.SetAutomationId(dialog, id);
        _dialog = dialog;
        using var retired = owner?.Token.Register(() => DispatcherQueue.TryEnqueue(() => { if (ReferenceEquals(_dialog, dialog)) dialog.Hide(); }));
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        catch (System.Runtime.InteropServices.COMException) { return false; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    private void CloseDialogs()
    {
        _dialog?.Hide(); _dialog = null;
        RetireUpload();
        _downloadCancel?.Cancel();
    }

    private void RetireUpload()
    {
        var session = _upload;
        if (session == null) return;
        _upload = null;
        _ = DiscardUploadAsync(session);
    }

    private async Task DiscardUploadAsync(AdminBackupUploadSession session)
    {
        try { await session.DiscardAsync().ConfigureAwait(false); }
        finally
        {
            session.Dispose();
            DispatcherQueue.TryEnqueue(() => { if (_active) _ = Controller?.RefreshUploadsAsync(); });
        }
    }

    private async Task<string?> PickSavePathAsync(string suggested)
    {
        if (PickSavePathOverride is { } pick) return await pick(suggested);
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = suggested };
        var extension = Path.GetExtension(suggested);
        picker.FileTypeChoices.Add(T("configure.native.backups.fileType"), [extension.Length > 0 ? extension : ".bin"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private static async Task<(string Name, Stream Stream)?> PickUploadAsync()
    {
        if (PickUploadOverride is { } pick) return await pick();
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".tar"); picker.FileTypeFilter.Add(".age");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
        var file = await picker.PickSingleFileAsync();
        if (file == null) return null;
        return (file.Name, new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous));
    }

    private static void Copy(string text, string done, string failed)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            App.Services.GetService<ToastService>()?.Success(done);
        }
        catch (Exception) { App.Services.GetService<ToastService>()?.Error(failed); }
    }
}
