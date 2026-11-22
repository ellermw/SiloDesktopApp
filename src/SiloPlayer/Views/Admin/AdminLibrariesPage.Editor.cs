using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminLibrariesUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Hosting for the library editor panel (over the whole Admin shell, as the WebUI dialog
/// covers the layout) and the server folder browser dialog (WebUI <c>FolderBrowser</c>).
/// </summary>
public sealed partial class AdminLibrariesPage
{
    private AdminLibraryEditorView? _editor;
    private Panel? _editorHost;

    /// <summary>The open editor's session (tests and fixtures); null when no editor is open.</summary>
    public AdminLibraryEditorSession? EditorSession => _editor?.Session;

    private void OpenEditor(AdminLibrary? library, LibraryUiOwner owner, AdminLibraryEditorSection section = AdminLibraryEditorSection.General)
    {
        var controller = owner.Controller;
        if (!owner.IsCurrent || _editor != null || _dialog != null) return;
        AdminLibraryEditorSession session;
        try { session = AdminLibraryEditorSession.Begin(controller, library, owner.Lease); }
        catch (OperationCanceledException) { return; }
        var opener = Focused(XamlRoot) as Control;
        var view = new AdminLibraryEditorView(this, controller, session);
        view.OpenOnSection(section);
        view.CloseRequested += () => { if (ReferenceEquals(_editor, view)) CloseEditor(force: false); };
        _editor = view;
        _editorHost = OverlayHost();
        Grid.SetRowSpan(view, Math.Max(1, (_editorHost as Grid)?.RowDefinitions.Count ?? 1));
        Grid.SetColumnSpan(view, Math.Max(1, (_editorHost as Grid)?.ColumnDefinitions.Count ?? 1));
        Canvas.SetZIndex(view, 1000);
        _editorHost.Children.Add(view);
        _scroll.IsEnabled = false;
        view.Show(opener);
        _ = session.LoadAsync();
    }

    /// <summary>The shell's overlay root when the page is hosted in the Admin shell, else the page itself.</summary>
    private Panel OverlayHost()
    {
        for (DependencyObject? ancestor = this; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is AdminShellPage shell) return shell.MonitorOverlayRoot;
        return _root;
    }

    /// <summary>Closes the editor. A save in flight keeps it open unless forced (retirement or departure).</summary>
    private void CloseEditor(bool force)
    {
        var editor = _editor;
        if (editor == null) return;
        if (!force && editor.Session.State.Saving && editor.Session.IsCurrent) return;
        _editor = null;
        _editorHost?.Children.Remove(editor);
        _editorHost = null;
        _scroll.IsEnabled = true;
        editor.Detach();
        // A sent save settles first; its outcome still reaches the list and toast.
        editor.Session.Dispose();
    }

    private void DiscardEditor()
    {
        _editor?.Session.Discard();
        CloseEditor(force: true);
    }

    /// <summary>
    /// Browse the server's folders and pick one or more (WebUI <c>FolderBrowser</c>): read-only,
    /// one bounded page per folder with Load more. Returns the paths to merge (empty when cancelled).
    /// </summary>
    internal async Task<IReadOnlyList<string>> BrowseFoldersAsync(MonitorAuthorityLease editorLease, IReadOnlyList<string> existing)
    {
        var controller = _controller;
        if (controller == null || _dialog != null || !controller.IsActionCurrent(editorLease)) return [];
        MonitorAuthorityLease lease;
        var renderedOwner = controller.State.Owner;
        try { lease = controller.CaptureAction(renderedOwner); }
        catch (OperationCanceledException) { return []; }
        if (!controller.IsActionCurrent(editorLease)) { lease.Dispose(); return []; }
        using (lease)
        using (var session = new AdminFolderBrowserSession(controller, lease, existing))
        {
            IReadOnlyList<string> result = [];
            var content = new StackPanel { Spacing = 12 };
            var draft = new TextBox { Text = AdminFolderBrowserSession.RootPath, PlaceholderText = "/mnt/media", Height = 36, FontSize = 14, CornerRadius = new(RadiusMd), FlowDirection = FlowDirection.LeftToRight };
            AutomationProperties.SetName(draft, T("web.admin.common.folderBrowser.title"));
            AutomationProperties.SetAutomationId(draft, "FolderBrowserPath");
            var browse = DialogButton(T("web.admin.common.folderBrowser.browse"), "FolderBrowserBrowse", "OutlineButtonStyle");
            var top = new Grid { ColumnSpacing = 8 };
            top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            top.Children.Add(draft); Grid.SetColumn(browse, 1); top.Children.Add(browse);
            content.Children.Add(top);
            var validation = Text("", 14); validation.Foreground = Solid(Destructive);
            AutomationProperties.SetLiveSetting(validation, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
            AutomationProperties.SetAutomationId(validation, "FolderBrowserError");
            content.Children.Add(validation);
            var already = Text(T("web.admin.common.folderBrowser.alreadyListed"), 14, true);
            content.Children.Add(already);

            var head = new Grid { ColumnSpacing = 8, Padding = new(12, 8, 12, 8), BorderBrush = Tint("BorderBrush", 0.6), BorderThickness = new(0, 0, 0, 1) };
            head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var current = new StackPanel();
            current.Children.Add(Strong(T("web.admin.common.folderBrowser.currentFolder"), 14));
            var currentPath = Mono("/", 12); currentPath.TextWrapping = TextWrapping.NoWrap; AutomationProperties.SetAutomationId(currentPath, "FolderBrowserCurrent");
            current.Children.Add(new ScrollViewer { Content = currentPath, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled });
            head.Children.Add(current);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            var up = Action(T("web.admin.common.folderBrowser.up"), "FolderBrowserUp", "arrow-up", "GhostButtonStyle", gap: 10);
            var refresh = IconButton("refresh-cw", T("web.admin.activity.page.refresh"), "FolderBrowserRefresh", 36, 14);
            tools.Children.Add(up); tools.Children.Add(refresh);
            Grid.SetColumn(tools, 1); head.Children.Add(tools);
            var entries = new StackPanel { Padding = new(8), Spacing = 0 };
            var list = new ScrollViewer { Content = entries, Height = 288, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled };
            var box = new StackPanel();
            box.Children.Add(head); box.Children.Add(list);
            content.Children.Add(new Border { Child = box, CornerRadius = new(RadiusMd), BorderThickness = new(1), BorderBrush = Tint("BorderBrush", 0.6) });
            var more = DialogButton(T("web.admin.common.folderBrowser.loadMore"), "FolderBrowserLoadMore", "OutlineButtonStyle");
            var restart = DialogButton(T("web.admin.common.folderBrowser.restart"), "FolderBrowserRestart", "OutlineButtonStyle");
            var cap = Text("", 12, true);
            content.Children.Add(more); content.Children.Add(restart); content.Children.Add(cap);
            var footer = new Grid { ColumnSpacing = 8 };
            footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var hint = Text("", 12, true); AutomationProperties.SetAutomationId(hint, "FolderBrowserHint");
            footer.Children.Add(hint);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var cancel = DialogButton(Core.Localization.AppStrings.Get("common.cancel", fallback: "Cancel"), "FolderBrowserCancel", "OutlineButtonStyle");
            var accept = DialogButton("", "FolderBrowserAccept", "AccentButtonStyle");
            buttons.Children.Add(cancel); buttons.Children.Add(accept);
            Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
            content.Children.Add(footer);
            var dialog = NewDialog(T("web.admin.common.folderBrowser.title"), content, 672);

            var queue = DispatcherQueue;
            void Render()
            {
                var state = session.State;
                currentPath.Text = state.ResolvedPath;
                validation.Text = state.ValidationError ?? (state.Error ?? "");
                validation.Visibility = validation.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                already.Visibility = state.AlreadyAdded ? Visibility.Visible : Visibility.Collapsed;
                up.IsEnabled = state.CanGoUp;
                refresh.Content = state.Fetching ? Spinning("refresh-cw", 14) : Icon("refresh-cw", 14);
                entries.Children.Clear();
                if (state.Loading)
                {
                    for (var index = 0; index < 6; index++) entries.Children.Add(new Border { Height = 40, CornerRadius = new(RadiusMd), Margin = new(0, 0, 0, 8), Background = Tint("MutedBrush", 0.7) });
                }
                else if (state.Entries.Count == 0 && state.Error == null)
                {
                    var empty = Text(T("web.admin.common.folderBrowser.empty"), 14, true); empty.Margin = new(8, 24, 8, 24);
                    entries.Children.Add(empty);
                }
                else
                {
                    foreach (var entry in state.Entries)
                    {
                        var selected = state.Selected.Contains(entry.Path);
                        var existingPath = state.ExistingPaths.Contains(entry.Path);
                        var row = new Grid { CornerRadius = new(RadiusMd), Background = selected ? Solid(Primary, 0.10) : null, BorderBrush = selected ? Solid(Primary, 0.3) : null, BorderThickness = new(selected ? 1 : 0) };
                        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                        var mark = new Border
                        {
                            Width = 16, Height = 16, CornerRadius = new(4), BorderThickness = new(1),
                            BorderBrush = selected ? Solid(Primary) : existingPath ? Tint("SecondaryTextBrush", 0.3) : Tint("SecondaryTextBrush", 0.4),
                            Background = selected ? Solid(Primary) : existingPath ? Tint("MutedBrush", 0.5) : null,
                            Child = selected || existingPath ? Icon("check", 12, selected ? Brush("AccentForegroundBrush") : Brush("SecondaryTextBrush")) : null,
                        };
                        var check = new Button { Content = mark, Padding = new(8), MinHeight = 0, IsEnabled = !existingPath, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
                        var checkLabel = T(existingPath ? "web.admin.common.folderBrowser.alreadyAdded" : selected ? "web.admin.common.folderBrowser.deselect" : "web.admin.common.folderBrowser.select");
                        AutomationProperties.SetName(check, checkLabel + " " + entry.Name);
                        AutomationProperties.SetAutomationId(check, "FolderBrowserSelect_" + entry.Name);
                        ToolTipService.SetToolTip(check, checkLabel);
                        var path = entry.Path;
                        check.Click += (_, _) => session.ToggleSelected(path);
                        row.Children.Add(check);
                        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        label.Children.Add(Icon("folder-open", 16, Brush("SecondaryTextBrush")));
                        var name = Text(entry.Name, 14); name.TextTrimming = TextTrimming.CharacterEllipsis; name.TextWrapping = TextWrapping.NoWrap;
                        label.Children.Add(name);
                        var open = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(4, 8, 8, 8), Margin = new(-4, 0, 0, 0), CornerRadius = new(4), MinHeight = 0, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
                        // The ghost style's rounded corners clip the folder icon: left padding with the equal negative margin keeps the label where it was.
                        AutomationProperties.SetName(open, entry.Name);
                        AutomationProperties.SetAutomationId(open, "FolderBrowserEntry_" + entry.Name);
                        open.Click += (_, _) => { draft.Text = path; _ = session.NavigateAsync(path); };
                        Grid.SetColumn(open, 1); row.Children.Add(open);
                        entries.Children.Add(row);
                    }
                }
                more.Visibility = state.HasMore ? Visibility.Visible : Visibility.Collapsed;
                more.IsEnabled = !state.LoadingMore;
                restart.Visibility = state.Error != null ? Visibility.Visible : Visibility.Collapsed;
                cap.Text = state.Capped ? T("admin.native.libraries.browseCap", AdminLibrariesText.Count(state.Entries.Count)) : "";
                cap.Visibility = state.Capped ? Visibility.Visible : Visibility.Collapsed;
                hint.Text = state.Selected.Count > 0 ? T("web.admin.common.folderBrowser.selectedCount", AdminLibrariesText.Count(state.Selected.Count)) : T("web.admin.common.folderBrowser.selectHint");
                accept.Content = state.AddableSelected > 0 ? T("web.admin.common.folderBrowser.addFolders", AdminLibrariesText.Count(state.AddableSelected)) : T("web.admin.common.folderBrowser.useCurrent");
                AutomationProperties.SetName(accept, (string)accept.Content);
                accept.IsEnabled = state.AddableSelected > 0 || !state.AlreadyAdded;
            }
            session.Changed += () => queue.TryEnqueue(Render);
            var follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            var syncing = false;
            follow.Tick += async (_, _) =>
            {
                follow.Stop();
                var resolved = await session.TryFollowDraftAsync(draft.Text);
                _ = resolved;
            };
            draft.TextChanged += (_, _) => { if (syncing) return; session.DraftChanged(); follow.Stop(); follow.Start(); };
            async Task Submit()
            {
                follow.Stop();
                var resolved = await session.SubmitDraftAsync(draft.Text);
                if (resolved != null) { syncing = true; draft.Text = resolved; syncing = false; }
            }
            draft.KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; _ = Submit(); } };
            browse.Click += (_, _) => _ = Submit();
            up.Click += (_, _) => { var parent = session.State.Parent; syncing = true; draft.Text = parent; syncing = false; _ = session.NavigateAsync(parent); };
            refresh.Click += (_, _) => _ = session.RefreshAsync();
            more.Click += (_, _) => _ = session.LoadMoreAsync();
            restart.Click += (_, _) => _ = session.RefreshAsync();
            cancel.Click += (_, _) => dialog.Hide();
            accept.Click += (_, _) => { result = session.Selection(); dialog.Hide(); };
            Render();
            _ = session.OpenAsync();
            await ShowOwnedAsync(dialog, lease, draft);
            follow.Stop();
            return controller.IsActionCurrent(editorLease) ? result : [];
        }
    }
}
