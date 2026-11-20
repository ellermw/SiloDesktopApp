using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using Presentation = SiloPlayer.Core.Services.Admin.AdminPreparedFilesPresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private const string FilesKey = "web.admin.downloads.preparedFiles";
    private const string None = "—";

    /// <summary>A combo box whose programmatic changes never read as the administrator's choice.</summary>
    private sealed class Choice
    {
        private readonly ComboBox _box;
        private bool _syncing;
        private string _signature = "";
        public Choice(string id, string name, double width, Action<string> changed)
        {
            _box = Select(id, [("", "")], width);
            _box.Height = 32; _box.FontSize = 13;
            AutomationProperties.SetName(_box, name);
            _box.SelectionChanged += (_, _) =>
            {
                if (_syncing || _box.SelectedItem is not ComboBoxItem { Tag: string value }) return;
                changed(value);
            };
        }
        public ComboBox Box => _box;
        public void Show(IReadOnlyList<(string Value, string Label)> options, string selected)
        {
            _syncing = true;
            try
            {
                var signature = string.Join("\n", options.Select(o => o.Value + "\t" + o.Label));
                if (signature != _signature)
                {
                    _box.Items.Clear();
                    foreach (var (value, label) in options) _box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
                    _signature = signature;
                }
                var match = _box.Items.OfType<ComboBoxItem>().FirstOrDefault(x => Equals(x.Tag, selected));
                if (match == null)
                {
                    match = new ComboBoxItem { Content = selected, Tag = selected };
                    _box.Items.Add(match);
                    _signature = "";
                }
                if (!ReferenceEquals(_box.SelectedItem, match)) _box.SelectedItem = match;
            }
            finally { _syncing = false; }
        }
    }

    /// <summary>The Prepared files tab (official <c>PreparedFilesTab.tsx</c>).</summary>
    private sealed class FilesView : TabView
    {
        private readonly StackPanel _root = Stack(16);
        private readonly LogsWrapPanel _filters = new() { Spacing = 8 };
        private readonly SearchField _search;
        private readonly Choice _location, _state, _format, _sort;
        private readonly StackPanel _dynamic = Stack(16);
        private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
        private AdminPreparedFileQuery _selectionQuery = new();

        public FilesView(AdminPreparedFilesPage page) : base(page)
        {
            _search = new SearchField(T(FilesKey + ".search"), "PreparedFilesSearch", 200, value => Change(q => q with { Search = value.Length == 0 ? null : value }));
            _location = new Choice("PreparedFilesLocationFilter", T(FilesKey + ".locationFilter"), 190, value =>
            {
                var next = value.Length == 0 ? null : value;
                P.ReplaceRoute("files", next);
                Change(q => q with { Location = next });
            });
            _state = new Choice("PreparedFilesStateFilter", T(FilesKey + ".stateFilter"), 170, value => Change(q => q with { State = value.Length == 0 ? null : value }));
            _format = new Choice("PreparedFilesFormatFilter", T(FilesKey + ".formatFilter"), 160, value => Change(q => q with { Format = value.Length == 0 ? null : value }));
            _sort = new Choice("PreparedFilesSort", T(FilesKey + ".sortFilter"), 200, value => Change(q => q with { Sort = value }));
            _filters.Children.Add(_search.Root);
            foreach (var choice in new[] { _location, _state, _format, _sort }) _filters.Children.Add(choice.Box);
            _root.Children.Add(_filters); _root.Children.Add(_dynamic);
        }

        public override UIElement Root => _root;

        public override void Reset() { _selected.Clear(); _search.Reset(); _selectionQuery = new(); }

        private void Change(Func<AdminPreparedFileQuery, AdminPreparedFileQuery> edit) =>
            _ = P.Controller.SetFileQueryAsync(edit(P.Controller.State.FileQuery));

        public override void Update(AdminPreparedFilesSnapshot state)
        {
            var query = state.FileQuery;
            // A filter change clears the selection, so files it hid cannot come back selected, and deleted, on return.
            if (_selectionQuery != query) { _selected.Clear(); _selectionQuery = query; }
            var locations = new List<(string, string)> { ("", T(FilesKey + ".allLocations")) };
            locations.AddRange((state.Storage.Value?.Locations ?? []).Select(l => (l.Key, l.Name)));
            _location.Show(locations, query.Location ?? "");
            _state.Show([("", T(FilesKey + ".state.all")), ("in_use", T(FilesKey + ".state.inUse")), ("cached", T(FilesKey + ".state.cached")), ("expired", T(FilesKey + ".state.expired"))], query.State ?? "");
            _format.Show([("", T(FilesKey + ".allFormats")), ("transcode", T(FilesKey + ".formatTranscode")), ("remux", T(FilesKey + ".formatRemux"))], query.Format ?? "");
            _sort.Show([("size", T(FilesKey + ".sort.size")), ("last_used", T(FilesKey + ".sort.lastUsed")), ("created", T(FilesKey + ".sort.created"))], query.Sort);

            _dynamic.Children.Clear();
            var files = state.Files;
            var rows = files.Items;
            var known = rows.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            _selected.RemoveWhere(id => !known.Contains(id));
            var selectedRows = rows.Where(x => _selected.Contains(x.Id)).ToArray();
            if (selectedRows.Length > 0) _dynamic.Children.Add(SelectionBar(selectedRows, state));
            if (files.Error != null && rows.Count > 0) _dynamic.Children.Add(StaleBanner("PreparedFilesFilesReload", () => _ = P.Controller.ReloadFilesAsync()));

            var table = new LogsNativeTable([T(FilesKey + ".column.title"), T(FilesKey + ".column.preparedAs"), T(FilesKey + ".column.location"), T(FilesKey + ".column.onDisk"),
                T(FilesKey + ".column.lastUsed"), T(FilesKey + ".column.usedBy"), T(FilesKey + ".column.state")], [320, 220, 140, 110, 120, 240, 150]);
            table.AlignEnd(3);
            var selectable = rows.Where(r => r.State != "expired").ToArray();
            if (!files.Loaded && files.Error == null || files.Loading && !files.Loaded)
                for (var i = 0; i < 4; i++) table.AddFull(Text("…", 14, true), 40);
            else if (rows.Count == 0)
            {
                var empty = Stack(12);
                var message = Text(files.Error != null ? T(FilesKey + ".loadError") : T(FilesKey + ".empty"), 14, true);
                message.HorizontalAlignment = HorizontalAlignment.Center; message.TextAlignment = TextAlignment.Center;
                empty.Children.Add(message);
                if (files.Error != null)
                {
                    var reload = Button(T("common.tryAgain"), "PreparedFilesFilesReload");
                    reload.HorizontalAlignment = HorizontalAlignment.Center;
                    reload.Click += (_, _) => _ = P.Controller.ReloadFilesAsync();
                    empty.Children.Add(reload);
                }
                table.AddFull(empty, 120);
            }
            else
            {
                foreach (var row in rows) AddRow(table, row, state);
            }
            var selectAll = Check(T(FilesKey + ".selectAll"), "PreparedFilesSelectAll", selectable.Length > 0 && selectable.All(r => _selected.Contains(r.Id)), selectable.Length > 0, on =>
            {
                _selected.Clear();
                if (on) foreach (var row in selectable) _selected.Add(row.Id);
                Update(P.Controller.State);
            });
            AutomationProperties.SetName(selectAll, T(FilesKey + ".selectAll"));
            _dynamic.Children.Add(selectAll);
            _dynamic.Children.Add(Panel(table, 16, 4, true));
            if (files.NextCursor != null) _dynamic.Children.Add(LoadMore(files.Loading, () => _ = P.Controller.LoadMoreFilesAsync()));
            if (files.Truncated) _dynamic.Children.Add(Text(T("configure.native.preparedFiles.listTruncated", new { count = rows.Count }), 12, true));
        }

        private UIElement SelectionBar(IReadOnlyList<AdminPreparedFile> selected, AdminPreparedFilesSnapshot state)
        {
            var inUse = selected.Count(x => x.State == "in_use");
            var row = new LogsWrapPanel { Spacing = 8 };
            row.Children.Add(Text(string.Join(" · ", new[]
            {
                T(FilesKey + ".selectedCount", Presentation.With(Presentation.Count(selected.Count), ("count_text", selected.Count.ToString("N0", AppStrings.FormatCulture)))),
                Presentation.FormatBytes(selected.Sum(x => x.Bytes)),
                inUse > 0 ? T(FilesKey + ".selectedInUse", Presentation.Count(inUse)) : "",
            }.Where(x => !string.IsNullOrEmpty(x))), 12));
            var clear = Button(T(FilesKey + ".clear"), "PreparedFilesClearSelection", "GhostButtonStyle");
            clear.Click += (_, _) => { _selected.Clear(); Update(P.Controller.State); };
            var delete = Button(T(FilesKey + ".delete"), "PreparedFilesDeleteSelected", "DestructiveButtonStyle", "trash-2");
            delete.IsEnabled = !state.ActionPending && !state.DestructiveBlocked;
            var targets = selected.ToArray();
            delete.Click += (_, _) => P.ConfirmDeleteFiles(targets, afterDelete: () => { _selected.Clear(); });
            row.Children.Add(clear); row.Children.Add(delete);
            var panel = Panel(row, 10, 10, true);
            AutomationProperties.SetAutomationId(panel, "PreparedFilesSelection");
            AutomationProperties.SetName(panel, T(FilesKey + ".selectionToolbar"));
            return panel;
        }

        private void AddRow(LogsNativeTable table, AdminPreparedFile file, AdminPreparedFilesSnapshot state)
        {
            var title = Presentation.FileTitle(file);
            var recipe = Presentation.FileRecipe(file);
            var subtitle = Presentation.FileSubtitle(file);
            var select = Check("", "PreparedFilesSelect_" + file.Id, _selected.Contains(file.Id), file.State != "expired" && !state.ActionPending, on =>
            {
                if (on) _selected.Add(file.Id); else _selected.Remove(file.Id);
                Update(P.Controller.State);
            });
            AutomationProperties.SetName(select, T(FilesKey + ".selectFile", new { title }));
            var titleCell = Stack(0);
            var heading = Strong(title, 14); heading.TextWrapping = TextWrapping.Wrap;
            titleCell.Children.Add(heading);
            if (subtitle.Length > 0) titleCell.Children.Add(Text(subtitle, 12, true));
            var recipeCell = Stack(0);
            recipeCell.Children.Add(Text(recipe.Main, 14));
            if (recipe.Detail.Length > 0) recipeCell.Children.Add(Text(recipe.Detail, 12, true));
            var usageCell = Stack(0);
            usageCell.Children.Add(Text(Presentation.FileUsage(file), 14));
            if (file.OldestWaiting is { } stale)
            {
                var device = string.IsNullOrEmpty(stale.DeviceName) ? T(FilesKey + ".aDevice") : stale.DeviceName;
                var seen = Presentation.Relative(stale.LastSeenAt);
                var warning = Text(stale.LastSeenAt != null
                    ? T(FilesKey + ".staleSeen", new { device, when = seen ?? "" })
                    : T(FilesKey + ".staleNever", new { device }), 12);
                warning.Foreground = Brush("WarningBrush");
                usageCell.Children.Add(warning);
            }
            var stateCell = Stack(2);
            stateCell.Children.Add(Pill(file.State switch
            {
                "in_use" => T(FilesKey + ".state.inUse"), "cached" => T(FilesKey + ".state.cached"), _ => T(FilesKey + ".state.expired"),
            }, file.State switch { "in_use" => "active", "cached" => "cached", _ => "muted" }));
            if (file.State == "cached" && file.ExpiresAt != null)
                stateCell.Children.Add(Text(T(FilesKey + ".expires", new { when = Presentation.FormatExpiresIn(file.ExpiresAt) }), 12, true));
            var grid = table.Add([
                Wrap(select, titleCell), recipeCell, Cell(file.LocationName), Cell(file.State == "expired" ? None : Presentation.FormatBytes(file.Bytes)),
                Cell(Presentation.Relative(file.LastUsedAt) ?? None), usageCell, stateCell,
            ]);
            if (_selected.Contains(file.Id)) grid.Background = Tint("AccentBrush", 0.06);
            AutomationProperties.SetAutomationId(grid, "PreparedFilesRow_" + file.Id);
        }

        private static UIElement Wrap(UIElement check, UIElement content)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.Children.Add(check);
            Grid.SetColumn((FrameworkElement)content, 1); row.Children.Add(content);
            return row;
        }
    }

    /// <summary>
    /// The official delete dialog: cached files by default, in-use files only on an explicit opt-in. Exactly the rows it names are
    /// sent, whatever the selection became while it was open.
    /// </summary>
    private async void ConfirmDeleteFiles(IReadOnlyList<AdminPreparedFile> files, Action afterDelete)
    {
        if (files.Count == 0 || Controller.State.ActionPending || Controller.State.DestructiveBlocked) return;
        var cached = files.Where(f => f.State != "in_use").ToArray();
        var inUse = files.Where(f => f.State == "in_use").ToArray();
        var include = Check(T(FilesKey + ".dialog.includeInUse", Presentation.Count(inUse.Length)), "PreparedFilesIncludeInUse", false, true, _ => { });
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        var content = Stack(12);
        content.Children.Add(description);
        if (cached.Length > 0)
        {
            var card = Stack(2);
            card.Children.Add(RichText(AdminPreparedFilesText.Rich(FilesKey + ".dialog.cachedList", Presentation.With(Presentation.Count(cached.Length), ("names", TitleList(cached)))), 14));
            card.Children.Add(Text(T(FilesKey + ".dialog.cachedNote", new { count = cached.Length }), 12, true));
            content.Children.Add(Panel(card, 12, 12, true));
        }
        if (inUse.Length > 0)
        {
            var card = Stack(2);
            card.Children.Add(RichText(AdminPreparedFilesText.Rich(FilesKey + ".dialog.inUseList", Presentation.With(Presentation.Count(inUse.Length), ("names", TitleList(inUse)))), 14));
            card.Children.Add(Text(T(FilesKey + ".dialog.inUseNote", new { count = inUse.Length }), 12));
            var panel = Panel(card, 12, 12, true);
            panel.BorderBrush = Tint("WarningBrush", 0.4); panel.Background = Tint("WarningBrush", 0.08);
            content.Children.Add(panel);
            content.Children.Add(include);
        }
        content.Children.Add(Text(T("web.shell.confirmDialog.irreversible"), 12, true));
        var dialog = NewDialog(T(FilesKey + ".dialog.title", Presentation.Count(files.Count)), content, "", destructive: true);
        void Refresh()
        {
            var includeInUse = include.IsChecked == true;
            var targets = includeInUse ? files.Count : cached.Length;
            var bytes = (includeInUse ? files : cached).Sum(f => f.Bytes);
            description.Text = T(FilesKey + ".dialog.description", new { size = Presentation.FormatBytes(bytes) });
            dialog.PrimaryButtonText = includeInUse || inUse.Length == 0
                ? T(FilesKey + ".dialog.deleteFiles", Presentation.Count(targets)) : T(FilesKey + ".dialog.deleteCached", Presentation.Count(targets));
            dialog.IsPrimaryButtonEnabled = targets > 0;
        }
        include.Checked += (_, _) => Refresh();
        include.Unchecked += (_, _) => Refresh();
        Refresh();
        AutomationProperties.SetAutomationId(dialog, "PreparedFilesDeleteDialog");
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        var chosen = include.IsChecked == true;
        var sent = (chosen ? files : cached).ToArray();
        if (sent.Length == 0) return;
        afterDelete();
        await Controller.DeleteFilesAsync(sent, chosen);
    }

    /// <summary>The first three titles, then how many more.</summary>
    private static string TitleList(IReadOnlyList<AdminPreparedFile> rows)
    {
        var shown = string.Join(", ", rows.Take(3).Select(Presentation.FileTitle));
        if (rows.Count <= 3) return shown;
        return T(FilesKey + ".namesMore", Presentation.With(Presentation.Count(rows.Count - 3), ("names", shown)));
    }
}
