using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminLibrariesUi;
using P = SiloPlayer.Core.Services.Admin.AdminLibraryPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Ambiguous roots (with the root override dialog), Skipped roots (local sort and expandable
/// details) and Stale identifiers (with the reused match tool). Each reads its first page
/// only when opened, appends on "Load more" within the panel's page cap, and pages the
/// loaded rows 10 at a time.
/// </summary>
public sealed partial class AdminLibrariesPage
{
    private abstract class AppendView<TItem> : SectionView
    {
        protected readonly AdminAppendList<TItem> List;
        protected readonly StackPanel BodyStack = new() { Spacing = 8 };
        protected readonly ContentControl Rows = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        protected readonly StackPanel Footer = new() { Spacing = 8 };
        protected readonly TextBox Search;
        protected readonly Grid SearchHost;

        protected AppendView(AdminLibrariesPage page, AdminAppendList<TItem> list, string key, string title, string description, string placeholder)
            : base(page, list, key, title, description)
        {
            List = list;
            Search = SearchBox(placeholder, key + "Search", text => _ = List.SetSearchAsync(text));
            SearchHost = WithSearchIcon(Search);
            Section.Body.Child = BodyStack;
        }

        /// <summary>The 10-row slice of the loaded rows and the pager under it.</summary>
        protected IReadOnlyList<TItem> Slice(AdminAppendListState<TItem> state, IReadOnlyList<TItem> items)
        {
            var pages = Math.Max(1, (items.Count + AdminAppendList<TItem>.LocalPageSize - 1) / AdminAppendList<TItem>.LocalPageSize);
            var index = Math.Min(state.PageIndex, pages - 1);
            return items.Skip(index * AdminAppendList<TItem>.LocalPageSize).Take(AdminAppendList<TItem>.LocalPageSize).ToArray();
        }

        protected void AddPager(string key, AdminAppendListState<TItem> state, int count)
        {
            var size = AdminAppendList<TItem>.LocalPageSize;
            var pages = Math.Max(1, (count + size - 1) / size);
            var index = Math.Min(state.PageIndex, pages - 1);
            if (Pager(key, count, count == 0 ? 0 : index * size + 1, Math.Min((index + 1) * size, count), index > 0, index < pages - 1,
                    () => List.SetPage(0), () => List.SetPage(index - 1), () => List.SetPage(index + 1), () => List.SetPage(pages - 1)) is { } pager)
                Footer.Children.Add(pager);
        }

        protected void AddLoadMore(string key, AdminAppendListState<TItem> state, string? showing)
        {
            if (state.HasMore)
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                if (showing != null) row.Children.Add(Text(showing, 12, true));
                var more = LoadMore(key + "LoadMore", state.LoadingMore, () => _ = List.LoadMoreAsync());
                Grid.SetColumn(more, 1); row.Children.Add(more);
                Footer.Children.Add(row);
            }
            if (state.Capped) Footer.Children.Add(Text(T("admin.native.libraries.pageCap", AdminLibrariesText.Count(AdminAppendList<TItem>.MaxPages)), 12, true));
            if (state.Error != null && !state.LoadFailed) Footer.Children.Add(Text(state.Error, 12));
        }

        protected void ShowFailure(string key, AdminAppendListState<TItem> state)
        {
            var failure = new StackPanel { Spacing = 8 };
            failure.Children.Add(CardMessage(state.Error ?? "", true));
            failure.Children.Add(RetryButton(key + "Retry", () => _ = List.ReloadAsync()));
            Rows.Content = failure;
        }
    }

    // ── Ambiguous roots ──────────────────────────────────────────────────

    private sealed class AmbiguousView : AppendView<AdminLibraryRoot>
    {
        private readonly ComboBox _library = new() { Width = 220, Height = 32, FontSize = 12, CornerRadius = new(RadiusMd) };
        private readonly Grid _controls = new() { ColumnSpacing = 8, RowSpacing = 8 };
        private string _libraryKey = "";
        private bool _syncing;
        /// <summary>Until the admin picks a library, the list follows the current first library (WebUI selectedLibraryId ?? libraries[0]).</summary>
        private bool _userSelected;

        internal AmbiguousView(AdminLibrariesPage page, AdminAppendList<AdminLibraryRoot> list)
            : base(page, list, "Ambiguous", T("web.admin.libraries.ambiguousRoots.title"), T("web.admin.libraries.ambiguousRoots.description"), T("web.admin.libraries.ambiguousRoots.filterPlaceholder"))
        {
            _library.PlaceholderText = T("web.admin.libraries.ambiguousRoots.selectLibrary");
            AutomationProperties.SetName(_library, T("web.admin.libraries.ambiguousRoots.selectLibrary"));
            AutomationProperties.SetAutomationId(_library, "AmbiguousLibrary");
            _library.SelectionChanged += (_, _) => { if (!_syncing && _library.SelectedItem is ComboBoxItem { Tag: string id }) { _userSelected = true; _ = List.SetScopeAsync(id); } };
            BodyStack.Children.Add(_controls);
            BodyStack.Children.Add(Rows);
            BodyStack.Children.Add(Footer);
            Render();
        }

        internal override void Render()
        {
            var libraries = Page.Libraries;
            var state = List.State;
            // Embedded in Catalog Health the section stays and says there is nothing to inspect (official: list.empty).
            _controls.Visibility = libraries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (libraries.Count == 0)
            {
                Footer.Children.Clear();
                Rows.Content = CardMessage(T("web.admin.libraries.list.empty"));
                return;
            }
            // The first library is the default selection (WebUI selectedLibraryId ?? libraries[0]).
            var selected = _userSelected && state.Scope != null && libraries.Any(library => library.Id == state.Scope) ? state.Scope : libraries.FirstOrDefault()?.Id;
            if (selected != state.Scope) { _ = List.SetScopeAsync(selected); state = List.State; }
            var countUnknown = state.Total == null && !state.LoadFailed;
            var warning = state.Total > 0;
            if (state.LoadFailed) Section.SetIcon("alert-triangle", Destructive);
            else if (warning) Section.SetIcon("folder-open", Amber);
            else Section.SetMutedIcon("folder-open");
            Section.SetOpen(state.Open);
            Section.SetCount(countUnknown ? null : state.Total, error: state.LoadFailed);
            if (!state.Open) return;
            var key = string.Join("\n", libraries.Select(library => library.Id + "\t" + library.Name));
            if (key != _libraryKey)
            {
                _libraryKey = key;
                _syncing = true;
                try
                {
                    _library.Items.Clear();
                    foreach (var library in libraries) _library.Items.Add(new ComboBoxItem { Content = library.Name, Tag = library.Id });
                }
                finally { _syncing = false; }
            }
            _syncing = true;
            try { _library.SelectedItem = _library.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected); }
            finally { _syncing = false; }
            _controls.Children.Clear(); _controls.ColumnDefinitions.Clear(); _controls.RowDefinitions.Clear();
            Touch(_library); Touch(Search);
            if (Page._phone)
            {
                _library.Width = double.NaN; _library.HorizontalAlignment = HorizontalAlignment.Stretch;
                _controls.RowDefinitions.Add(new() { Height = GridLength.Auto }); _controls.RowDefinitions.Add(new() { Height = GridLength.Auto });
                _controls.Children.Add(_library); Grid.SetRow(SearchHost, 1); _controls.Children.Add(SearchHost);
            }
            else
            {
                _library.Width = 220;
                _controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _controls.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                Grid.SetRow(SearchHost, 0); Grid.SetColumn(SearchHost, 1);
                _controls.Children.Add(_library); _controls.Children.Add(SearchHost);
            }
            KeepFocus(BodyStack, () =>
            {
                Footer.Children.Clear();
                string? message = state.LoadFailed ? T("web.admin.libraries.ambiguousRoots.loadFailed")
                    : countUnknown || (state.Loading && state.Items.Count == 0) ? T("web.admin.libraries.ambiguousRoots.loading")
                    : state.Items.Count == 0 ? T(state.Search.Trim().Length > 0 ? "web.admin.libraries.ambiguousRoots.noMatches" : "web.admin.libraries.ambiguousRoots.empty")
                    : null;
                var rows = Slice(state, state.Items);
                if (Wide)
                {
                    IReadOnlyList<(string, GridLength, bool)> columns =
                    [
                        (T("web.admin.libraries.column.root"), new(1, GridUnitType.Star), false), (T("web.admin.libraries.column.type"), GridLength.Auto, false),
                        (T("web.admin.libraries.column.confidence"), GridLength.Auto, false), (T("web.admin.libraries.column.files"), GridLength.Auto, true),
                        (T("web.admin.libraries.column.actions"), new(120), false),
                    ];
                    var cells = rows.Select(root => (IReadOnlyList<UIElement>)new UIElement[]
                    {
                        RootCell(root), Badge(TypeLabel(root), BadgeTone.Outline), Badge(AdminRootLabels.Confidence(root.TypeConfidence)), Text(root.ObservedFileCount.ToString("N0", Core.Localization.AppStrings.FormatCulture), 12, true),
                        RootActions(root, false),
                    }).ToArray();
                    Rows.Content = TableFrame(GridTable(columns, cells, message, error: state.LoadFailed));
                }
                else if (message != null) Rows.Content = CardMessage(message, state.LoadFailed);
                else
                {
                    var list = new StackPanel { Spacing = 12 };
                    foreach (var root in rows)
                    {
                        var card = new StackPanel { Spacing = 12 };
                        var head = new StackPanel { Spacing = 4 };
                        head.Children.Add(Strong(Label(root)));
                        var path = Mono(root.RootPath); path.TextWrapping = TextWrapping.Wrap; head.Children.Add(path);
                        card.Children.Add(head);
                        card.Children.Add(Fields((T("web.admin.libraries.column.type"), Badge(TypeLabel(root), BadgeTone.Outline)),
                            (T("web.admin.libraries.column.confidence"), Badge(AdminRootLabels.Confidence(root.TypeConfidence))),
                            (T("web.admin.libraries.column.files"), Text(root.ObservedFileCount.ToString("N0", Core.Localization.AppStrings.FormatCulture), 12))));
                        if (P.RootEvidence(root) is { Length: > 0 } evidence) card.Children.Add(Text(evidence, 12, true));
                        card.Children.Add(RootActions(root, true));
                        list.Children.Add(Card(card));
                    }
                    Rows.Content = list;
                }
                AddPager("Ambiguous", state, state.Items.Count);
                AddLoadMore("Ambiguous", state, state.Total is { } total ? T("web.admin.libraries.ambiguousRoots.showing", AdminLibrariesText.Count(total, ("shown", state.Items.Count))) : null);
            });
        }

        private static string Label(AdminLibraryRoot root) => root.Title.Length > 0 ? root.Title : P.PathLeaf(root.RootPath);
        private static string TypeLabel(AdminLibraryRoot root) => AdminRootLabels.RootType(root.InferredType);

        private static StackPanel RootCell(AdminLibraryRoot root)
        {
            var stack = new StackPanel { Spacing = 4, MaxWidth = 448 };
            var label = Strong(Label(root)); label.TextTrimming = TextTrimming.CharacterEllipsis; label.TextWrapping = TextWrapping.NoWrap;
            stack.Children.Add(label);
            var path = Mono(root.RootPath, 11); path.TextTrimming = TextTrimming.CharacterEllipsis; path.TextWrapping = TextWrapping.NoWrap;
            ToolTipService.SetToolTip(path, root.RootPath);
            stack.Children.Add(path);
            if (P.RootEvidence(root) is { } evidence)
            {
                var text = Text(evidence, 11, true); text.TextTrimming = TextTrimming.CharacterEllipsis; text.TextWrapping = TextWrapping.NoWrap;
                stack.Children.Add(text);
            }
            return stack;
        }

        private FrameworkElement RootActions(AdminLibraryRoot root, bool card)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = card ? 8 : 4 };
            var key = Uri.EscapeDataString(root.RootPath);
            var overrideButton = Action(T("web.admin.libraries.ambiguousRoots.override"), "AmbiguousOverride_" + key, height: card ? 44 : 28);
            if (!card) overrideButton.FontSize = 12;
            overrideButton.Click += async (_, _) => { if (await Page.ShowRootOverrideAsync(root, UiOwner)) _ = List.ReloadAsync(); };
            actions.Children.Add(overrideButton);
            if (!string.IsNullOrEmpty(root.ContentId))
            {
                var resolve = Action(T("web.admin.libraries.ambiguousRoots.resolve"), "AmbiguousResolve_" + key, height: card ? 44 : 28);
                if (!card) resolve.FontSize = 12;
                ToolTipService.SetToolTip(resolve, T("web.admin.libraries.ambiguousRoots.resolveTitle"));
                var contentId = root.ContentId;
                resolve.Click += (_, _) => Page.OpenItem(contentId, UiOwner);
                actions.Children.Add(resolve);
            }
            return card ? CardActionRow(actions) : actions;
        }
    }

    /// <summary>
    /// Force the inferred identity of one root (WebUI <c>RootOverrideDialog</c>). Save sends only
    /// the set values; Remove override deletes the active one. Each is one write; the dialog
    /// closes on success and stays (with the server's message) on failure. True after a write.
    /// </summary>
    private async Task<bool> ShowRootOverrideAsync(AdminLibraryRoot root, LibraryUiOwner owner)
    {
        var controller = owner.Controller;
        if (!owner.IsCurrent || _dialog != null) return false;
        MonitorAuthorityLease lease;
        try { lease = controller.CaptureAction(owner.Lease); }
        catch (OperationCanceledException) { return false; }
        using (lease)
        {
            var changed = false;
            var active = root.ActiveOverride;
            var content = new StackPanel { Spacing = 16 };
            var description = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Brush("SecondaryTextBrush") };
            foreach (var (text, tag) in AdminLibrariesText.Segments(T("web.admin.libraries.rootOverride.description", ("path", root.RootPath))))
            {
                var run = new Microsoft.UI.Xaml.Documents.Run { Text = text };
                if (tag == "code") run.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
                description.Inlines.Add(run);
            }
            content.Children.Add(description);
            StackPanel Field(string label, FrameworkElement control) { var field = new StackPanel { Spacing = 6 }; field.Children.Add(Strong(label, 13)); field.Children.Add(control); return field; }
            TextBox Box(string id, string value, string placeholder = "")
            {
                var box = new TextBox { Text = value, PlaceholderText = placeholder, Height = 36, FontSize = 14, CornerRadius = new(RadiusMd) };
                AutomationProperties.SetAutomationId(box, id);
                return box;
            }
            var type = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 36, CornerRadius = new(RadiusMd) };
            foreach (var (value, key) in new[] { ("auto", "web.admin.libraries.rootOverride.auto"), ("movie", "web.admin.libraries.rootOverride.movie"), ("series", "web.admin.libraries.rootOverride.series") })
                type.Items.Add(new ComboBoxItem { Content = T(key), Tag = value });
            var initialType = active?.ForcedType is { Length: > 0 } forced ? forced : root.InferredType;
            type.SelectedIndex = initialType == "movie" ? 1 : initialType == "series" ? 2 : 0;
            AutomationProperties.SetName(type, T("web.admin.libraries.rootOverride.type"));
            AutomationProperties.SetAutomationId(type, "RootOverrideType");
            var year = Box("RootOverrideYear", active?.ForcedYear is > 0 ? active.ForcedYear.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : root.Year > 0 ? root.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) : "", "2024");
            AutomationProperties.SetName(year, T("web.admin.libraries.rootOverride.year"));
            var title = Box("RootOverrideTitle", active?.ForcedTitle is { Length: > 0 } forcedTitle ? forcedTitle : root.Title, T("web.admin.libraries.rootOverride.titlePlaceholder"));
            AutomationProperties.SetName(title, T("web.admin.libraries.rootOverride.titleLabel"));
            var tmdb = Box("RootOverrideTmdb", active?.ForcedTmdbId is { Length: > 0 } t ? t : root.TmdbId ?? "");
            var imdb = Box("RootOverrideImdb", active?.ForcedImdbId is { Length: > 0 } i ? i : root.ImdbId ?? "");
            var tvdb = Box("RootOverrideTvdb", active?.ForcedTvdbId is { Length: > 0 } v ? v : root.TvdbId ?? "");
            AutomationProperties.SetName(tmdb, T("web.admin.libraries.rootOverride.tmdbId"));
            AutomationProperties.SetName(imdb, T("web.admin.libraries.rootOverride.imdbId"));
            AutomationProperties.SetName(tvdb, T("web.admin.libraries.rootOverride.tvdbId"));
            var note = Box("RootOverrideNote", active?.Note ?? "", T("web.admin.libraries.rootOverride.notePlaceholder"));
            AutomationProperties.SetName(note, T("web.admin.libraries.rootOverride.note"));

            var first = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
            var ids = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
            if (_phone)
            {
                first.Children.Add(Field(T("web.admin.libraries.rootOverride.type"), type));
                first.RowDefinitions.Add(new() { Height = GridLength.Auto }); first.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var yearField = Field(T("web.admin.libraries.rootOverride.year"), year); Grid.SetRow(yearField, 1); first.Children.Add(yearField);
                for (var row = 0; row < 3; row++) ids.RowDefinitions.Add(new() { Height = GridLength.Auto });
            }
            else
            {
                first.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); first.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                first.Children.Add(Field(T("web.admin.libraries.rootOverride.type"), type));
                var yearField = Field(T("web.admin.libraries.rootOverride.year"), year); Grid.SetColumn(yearField, 1); first.Children.Add(yearField);
                for (var column = 0; column < 3; column++) ids.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            }
            var idFields = new[] { Field(T("web.admin.libraries.rootOverride.tmdbId"), tmdb), Field(T("web.admin.libraries.rootOverride.imdbId"), imdb), Field(T("web.admin.libraries.rootOverride.tvdbId"), tvdb) };
            for (var index = 0; index < idFields.Length; index++) { if (_phone) Grid.SetRow(idFields[index], index); else Grid.SetColumn(idFields[index], index); ids.Children.Add(idFields[index]); }
            content.Children.Add(first);
            content.Children.Add(Field(T("web.admin.libraries.rootOverride.titleLabel"), title));
            content.Children.Add(ids);
            content.Children.Add(Field(T("web.admin.libraries.rootOverride.note"), note));
            var failure = Text("", 13); failure.Foreground = Solid(Destructive); failure.Visibility = Visibility.Collapsed;
            AutomationProperties.SetLiveSetting(failure, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
            content.Children.Add(failure);
            var footer = new Grid { ColumnSpacing = 8 };
            footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var remove = DialogButton(T("web.admin.libraries.rootOverride.remove"), "RootOverrideRemove", "OutlineButtonStyle");
            remove.IsEnabled = active != null;
            var save = DialogButton(T("web.admin.libraries.rootOverride.save"), "RootOverrideSave", "AccentButtonStyle");
            footer.Children.Add(remove);
            Grid.SetColumn(save, 2); footer.Children.Add(save);
            content.Children.Add(footer);
            var dialog = NewDialog(T("web.admin.libraries.rootOverride.title"), new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 512);
            var busy = false;
            async Task Run(Func<Task> write, string success, string fallback)
            {
                if (busy || !controller.IsActionCurrent(lease)) return;
                busy = true; save.IsEnabled = false; remove.IsEnabled = false; failure.Visibility = Visibility.Collapsed;
                try
                {
                    await write();
                    if (!controller.IsActionCurrent(lease)) return;
                    changed = true;
                    controller.Announce(T(success), false);
                    dialog.Hide();
                }
                catch (Exception error)
                {
                    if (!controller.IsActionCurrent(lease) || AdminFailures.Classify(error, lease) is AdminFailureKind.Retired) return;
                    var unknown = P.IsUnknownOutcome(error);
                    failure.Text = unknown ? T("admin.native.libraries.outcomeUnknown") : P.Describe(error, fallback);
                    failure.Visibility = Visibility.Visible;
                    // An unknown outcome must be reconciled by reloading, never resent from this dialog.
                    if (unknown) { changed = true; return; }
                    busy = false; save.IsEnabled = true; remove.IsEnabled = active != null;
                }
            }
            save.Click += async (_, _) =>
            {
                var forcedType = (type.SelectedItem as ComboBoxItem)?.Tag as string;
                var yearValue = int.TryParse(year.Text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null;
                var write = new AdminRootOverrideWrite(root.LibraryId, root.RootPath)
                {
                    ForcedType = forcedType is "movie" or "series" ? forcedType : null, ForcedTitle = NullIfEmpty(title.Text), ForcedYear = yearValue,
                    ForcedTmdbId = NullIfEmpty(tmdb.Text), ForcedImdbId = NullIfEmpty(imdb.Text), ForcedTvdbId = NullIfEmpty(tvdb.Text), Note = NullIfEmpty(note.Text),
                };
                await Run(() => controller.Api.SetRootOverrideAsync(lease, write, lease.Token), "web.admin.libraries.toast.rootOverrideSaved", "web.admin.libraries.toast.rootOverrideSaveFailed");
            };
            remove.Click += async (_, _) =>
                await Run(() => controller.Api.DeleteRootOverrideAsync(lease, root.LibraryId, root.RootPath, lease.Token), "web.admin.libraries.toast.rootOverrideRemoved",
                    "web.admin.libraries.toast.rootOverrideRemoveFailed");
            dialog.Closing += (_, args) => { if (busy && !changed && lease.IsCurrent) args.Cancel = true; };
            await ShowOwnedAsync(dialog, lease, title);
            return changed;
        }
        static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
    }

    // ── Skipped roots ────────────────────────────────────────────────────

    private sealed class SkippedView : AppendView<AdminSkippedRoot>
    {
        private string _sortField = "last_seen";
        private bool _ascending;
        private string? _expanded;

        internal SkippedView(AdminLibrariesPage page, AdminAppendList<AdminSkippedRoot> list)
            : base(page, list, "Skipped", T("web.admin.libraries.skippedRoots.title"), T("web.admin.libraries.skippedRoots.description"), T("web.admin.libraries.skippedRoots.filterPlaceholder"))
        {
            Section.SetIcon("alert-triangle", Amber);
            BodyStack.Children.Add(SearchHost);
            BodyStack.Children.Add(Rows);
            BodyStack.Children.Add(Footer);
            Render();
        }

        private static IReadOnlyList<(string Field, string Label)> SortColumns =>
        [
            ("root_path", T("web.admin.libraries.column.item")), ("library", T("web.admin.libraries.column.library")), ("reason", T("web.admin.libraries.column.reason")),
            ("first_seen", T("web.admin.libraries.column.firstSeen")), ("last_seen", T("web.admin.libraries.column.lastSeen")),
        ];

        /// <summary>WebUI <c>useSort</c>: the active column flips direction; another column becomes active ascending.</summary>
        private void Toggle(string field)
        {
            if (field == _sortField) _ascending = !_ascending;
            else { _sortField = field; _ascending = true; }
            Render();
        }

        private IReadOnlyList<AdminSkippedRoot> Sorted(IReadOnlyList<AdminSkippedRoot> items)
        {
            Func<AdminSkippedRoot, string> key = _sortField switch
            {
                "root_path" => root => root.RootPath,
                "library" => root => root.LibraryName,
                "reason" => root => root.Reason,
                "first_seen" => root => root.FirstSeenAt.UtcDateTime.ToString("O"),
                _ => root => root.LastSeenAt.UtcDateTime.ToString("O"),
            };
            var comparer = _sortField is "first_seen" or "last_seen" ? StringComparer.Ordinal : StringComparer.CurrentCulture;
            return (_ascending ? items.OrderBy(key, comparer) : items.OrderByDescending(key, comparer)).ToArray();
        }

        internal override void Render()
        {
            var state = List.State;
            Section.SetOpen(state.Open);
            Section.SetCount(state.Total, error: state.LoadFailed);
            if (!state.Open) return;
            Touch(Search);
            KeepFocus(BodyStack, () =>
            {
                Footer.Children.Clear();
                if (state.LoadFailed) { ShowFailure("Skipped", state); return; }
                // Embedded in Catalog Health: a first page still on its way reads "Loading", and a first page with no rows says so (health.skippedEmpty).
                if (state.Items.Count == 0 && (state.Loading || state.Total != null))
                {
                    Rows.Content = CardMessage(state.Loading ? T("web.admin.libraries.skippedRoots.loading") : T("web.admin.libraries.health.skippedEmpty"));
                    return;
                }
                var sorted = Sorted(state.Items);
                var rows = Slice(state, sorted);
                if (Wide)
                {
                    IReadOnlyList<(string, GridLength, bool)> columns =
                    [
                        (SortHeader("root_path"), new(1, GridUnitType.Star), false), (SortHeader("library"), GridLength.Auto, false), (SortHeader("reason"), GridLength.Auto, false),
                        (T("web.admin.libraries.column.files"), GridLength.Auto, true), (SortHeader("first_seen"), GridLength.Auto, false), (SortHeader("last_seen"), GridLength.Auto, false),
                    ];
                    var cells = rows.Select(root => (IReadOnlyList<UIElement>)new UIElement[]
                    {
                        ItemCell(root), Text(root.LibraryName, 14), ReasonBadge(root.Reason), Text(root.FileCount.ToString("N0", Core.Localization.AppStrings.FormatCulture), 12, true),
                        Text(DateTimeText(root.FirstSeenAt), 12, true), Text(DateTimeText(root.LastSeenAt), 12, true),
                    }).ToArray();
                    var grid = GridTable(columns, cells, state.Loading && state.Items.Count == 0 ? T("web.admin.libraries.skippedRoots.loading") : null,
                        expansion: index => (Details(rows[index]), Key(rows[index]) == _expanded),
                        rowClick: index => { var key = Key(rows[index]); _expanded = _expanded == key ? null : key; Render(); });
                    ReplaceSortHeaders(grid);
                    Rows.Content = TableFrame(grid);
                }
                else
                {
                    var list = new StackPanel { Spacing = 12 };
                    list.Children.Add(SortControl());
                    if (state.Loading && state.Items.Count == 0) list.Children.Add(CardMessage(T("web.admin.libraries.skippedRoots.loading")));
                    foreach (var root in rows)
                    {
                        var card = new StackPanel { Spacing = 12 };
                        var head = new StackPanel { Spacing = 6 };
                        head.Children.Add(Strong(P.PathLeaf(root.RootPath)));
                        head.Children.Add(ReasonBadge(root.Reason));
                        card.Children.Add(head);
                        card.Children.Add(Fields((T("web.admin.libraries.column.library"), Text(root.LibraryName, 12)),
                            (T("web.admin.libraries.column.firstSeen"), Text(DateTimeText(root.FirstSeenAt), 12)), (T("web.admin.libraries.column.lastSeen"), Text(DateTimeText(root.LastSeenAt), 12))));
                        card.Children.Add(new Border { Child = Details(root), CornerRadius = new(RadiusXl), Padding = new(12, 8, 12, 8), Background = Tint("MutedBrush", 0.3) });
                        list.Children.Add(Card(card));
                    }
                    Rows.Content = list;
                }
                AddPager("Skipped", state, state.Items.Count);
                if (state.HasMore)
                {
                    var more = LoadMore("SkippedLoadMore", state.LoadingMore, () => _ = List.LoadMoreAsync());
                    if (state.LoadingMore) more.Content = T("web.admin.libraries.skippedRoots.loading");
                    if (!Wide) more.HorizontalAlignment = HorizontalAlignment.Stretch;
                    Footer.Children.Add(more);
                }
                if (state.Capped) Footer.Children.Add(Text(T("admin.native.libraries.pageCap", AdminLibrariesText.Count(AdminAppendList<AdminSkippedRoot>.MaxPages)), 12, true));
                if (state.Error != null && !state.LoadFailed) Footer.Children.Add(Text(state.Error, 12));
            });
        }

        private static string Key(AdminSkippedRoot root) => root.LibraryId + ":" + root.RootPath;

        private string SortHeader(string field) => "\u0001" + field;

        /// <summary>Turns the marked header cells into sort buttons with the active direction's chevron.</summary>
        private void ReplaceSortHeaders(Grid grid)
        {
            foreach (var cell in grid.Children.OfType<Border>().Where(border => Grid.GetRow(border) == 0 && border.Child is TextBlock { Text: var text } && text.StartsWith('\u0001')).ToArray())
            {
                var field = ((TextBlock)cell.Child).Text[1..];
                var label = SortColumns.First(column => column.Field == field).Label;
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                var text = Text(label, 14); text.FontWeight = FontWeights.Medium;
                content.Children.Add(text);
                var icon = Icon(field == _sortField && _ascending ? "chevron-up" : "chevron-down", 12, Brush("SecondaryTextBrush"));
                icon.Opacity = field == _sortField ? 1 : 0;
                content.Children.Add(icon);
                // The ghost style's rounded corners clip the header label: padding with the equal negative margin keeps it where it was.
                var button = new Button { Content = content, Padding = new(6, 2, 6, 2), Margin = new(-6, -2, -6, -2), CornerRadius = new(4), MinHeight = 0, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
                AutomationProperties.SetName(button, label);
                AutomationProperties.SetAutomationId(button, "SkippedSort_" + field);
                AutomationProperties.SetItemStatus(button, field == _sortField ? (_ascending ? "ascending" : "descending") : "");
                button.Click += (_, _) => Toggle(field);
                cell.Child = button;
            }
        }

        /// <summary>The card layout's stand-in for sortable headers: a field picker and a direction toggle.</summary>
        private Grid SortControl()
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var picker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 44, CornerRadius = new(RadiusMd), Header = null };
            foreach (var (field, label) in SortColumns) picker.Items.Add(new ComboBoxItem { Content = Core.Localization.AppStrings.Get("web.catalog.sort.sortBy", fallback: "Sort by") + " " + label, Tag = field, MinHeight = 44 });
            picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == _sortField);
            AutomationProperties.SetName(picker, Core.Localization.AppStrings.Get("web.catalog.sort.sortBy", fallback: "Sort by"));
            AutomationProperties.SetAutomationId(picker, "SkippedSortField");
            picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is ComboBoxItem { Tag: string field } && field != _sortField) { _sortField = field; Render(); } };
            grid.Children.Add(picker);
            var direction = Action(Core.Localization.AppStrings.Get(_ascending ? "web.catalog.sort.ascending" : "web.catalog.sort.descending", fallback: _ascending ? "Ascending" : "Descending"),
                "SkippedSortDirection", _ascending ? "arrow-up" : "arrow-down", height: 44);
            direction.Click += (_, _) => { _ascending = !_ascending; Render(); };
            Grid.SetColumn(direction, 1); grid.Children.Add(direction);
            return grid;
        }

        private UIElement ItemCell(AdminSkippedRoot root)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, MaxWidth = 320 };
            row.Children.Add(Icon(Key(root) == _expanded ? "chevron-down" : "chevron-right", 14, Brush("SecondaryTextBrush")));
            var leaf = Strong(P.PathLeaf(root.RootPath)); leaf.TextTrimming = TextTrimming.CharacterEllipsis; leaf.TextWrapping = TextWrapping.NoWrap;
            row.Children.Add(leaf);
            // The ghost style's rounded corners clip the chevron and name: padding with the equal negative margin keeps them where they were.
            var button = new Button { Content = row, Padding = new(6, 2, 6, 2), Margin = new(-6, -2, -6, -2), CornerRadius = new(4), MinHeight = 0, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
            AutomationProperties.SetName(button, P.PathLeaf(root.RootPath));
            AutomationProperties.SetAutomationId(button, "SkippedRow_" + Uri.EscapeDataString(Key(root)));
            AutomationProperties.SetItemStatus(button, Key(root) == _expanded ? "expanded" : "collapsed");
            button.Click += (_, _) => { var key = Key(root); _expanded = _expanded == key ? null : key; Render(); };
            return button;
        }

        private static Border ReasonBadge(string reason)
        {
            var badge = Badge(reason, BadgeTone.Warning);
            badge.Background = Solid(Warning, 0.05);
            return badge;
        }

        private static Grid Details(AdminSkippedRoot root)
        {
            var grid = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var row = 0;
            void Line(string label, UIElement value)
            {
                grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var caption = Text(label, 12, true); caption.FontWeight = FontWeights.Medium;
                Grid.SetRow(caption, row); grid.Children.Add(caption);
                if (value is FrameworkElement element) { Grid.SetRow(element, row); Grid.SetColumn(element, 1); grid.Children.Add(element); }
                row++;
            }
            TextBlock Selectable(string text) { var block = Mono(text, 12, false); block.TextWrapping = TextWrapping.Wrap; block.IsTextSelectionEnabled = true; return block; }
            Line(T("web.admin.libraries.skippedRoots.rootPath"), Selectable(root.RootPath));
            if (root.SampleFilePath.Length > 0) Line(T("web.admin.libraries.skippedRoots.sampleFile"), Selectable(root.SampleFilePath));
            Line(T("web.admin.libraries.skippedRoots.filesAffected"), Text(root.FileCount.ToString("N0", Core.Localization.AppStrings.FormatCulture), 12));
            return grid;
        }
    }

    // ── Stale identifiers ────────────────────────────────────────────────

    private sealed class StaleView : AppendView<AdminStaleMediaId>
    {
        internal StaleView(AdminLibrariesPage page, AdminAppendList<AdminStaleMediaId> list)
            : base(page, list, "Stale", T("web.admin.libraries.staleIds.title"), T("web.admin.libraries.staleIds.description"), T("web.admin.libraries.staleIds.filterPlaceholder"))
        {
            Section.SetIcon("unlink", Destructive);
            BodyStack.Children.Add(SearchHost);
            BodyStack.Children.Add(Rows);
            BodyStack.Children.Add(Footer);
            Render();
        }

        internal override void Render()
        {
            var state = List.State;
            Section.SetOpen(state.Open);
            Section.SetCount(state.Total, error: state.LoadFailed);
            if (!state.Open) return;
            Touch(Search);
            KeepFocus(BodyStack, () =>
            {
                Footer.Children.Clear();
                if (state.LoadFailed) { ShowFailure("Stale", state); return; }
                // Embedded in Catalog Health: "Loading" while the first page is on its way, health.staleEmpty when it holds no rows.
                if (state.Items.Count == 0 && (state.Loading || state.Total != null))
                {
                    Rows.Content = CardMessage(state.Loading ? T("common.loading") : T("web.admin.libraries.health.staleEmpty"));
                    return;
                }
                var rows = Slice(state, state.Items);
                if (Wide)
                {
                    IReadOnlyList<(string, GridLength, bool)> columns =
                    [
                        (T("web.admin.libraries.column.title"), new(1, GridUnitType.Star), false), (T("web.admin.libraries.column.year"), GridLength.Auto, false),
                        (T("web.admin.libraries.column.library"), GridLength.Auto, false), (T("web.admin.libraries.column.provider"), GridLength.Auto, false),
                        (T("web.admin.libraries.column.providerId"), GridLength.Auto, false), (T("web.admin.libraries.column.firstSeen"), GridLength.Auto, false),
                        (T("web.admin.libraries.column.lastSeen"), GridLength.Auto, false), (T("web.admin.libraries.column.actions"), new(100), false),
                    ];
                    var cells = rows.Select(row => (IReadOnlyList<UIElement>)new UIElement[]
                    {
                        Strong(row.Title), Text(row.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), 14), Text(row.LibraryName, 14), Badge(row.Provider, BadgeTone.Outline),
                        Mono(row.ProviderId, 12, false), Text(DateTimeText(row.FirstSeenAt), 12, true), Text(DateTimeText(row.LastSeenAt), 12, true), MatchButton(row, false),
                    }).ToArray();
                    var grid = GridTable(columns, cells, state.Loading && state.Items.Count == 0 ? T("web.admin.libraries.ambiguousRoots.loading") : null);
                    AutomationProperties.SetName(grid, T("web.admin.libraries.staleIds.tableLabel"));
                    Rows.Content = TableFrame(grid);
                }
                else
                {
                    var list = new StackPanel { Spacing = 12 };
                    if (state.Loading && state.Items.Count == 0) list.Children.Add(CardMessage(T("web.admin.libraries.ambiguousRoots.loading")));
                    foreach (var row in rows)
                    {
                        var card = new StackPanel { Spacing = 12 };
                        card.Children.Add(Strong(row.Title));
                        var id = Mono(row.ProviderId, 12, false); id.TextWrapping = TextWrapping.Wrap;
                        card.Children.Add(Fields((T("web.admin.libraries.column.year"), Text(row.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), 12)),
                            (T("web.admin.libraries.column.library"), Text(row.LibraryName, 12)), (T("web.admin.libraries.column.provider"), Badge(row.Provider, BadgeTone.Outline)),
                            (T("web.admin.libraries.column.providerId"), id), (T("web.admin.libraries.column.firstSeen"), Text(DateTimeText(row.FirstSeenAt), 12)),
                            (T("web.admin.libraries.column.lastSeen"), Text(DateTimeText(row.LastSeenAt), 12))));
                        var match = MatchButton(row, true); match.HorizontalAlignment = HorizontalAlignment.Stretch;
                        card.Children.Add(match);
                        list.Children.Add(Card(card));
                    }
                    Rows.Content = list;
                }
                AddPager("Stale", state, state.Items.Count);
                AddLoadMore("Stale", state, T("web.admin.libraries.staleIds.showing", AdminLibrariesText.Count(state.Items.Count)));
            });
        }

        private Button MatchButton(AdminStaleMediaId row, bool card)
        {
            var button = Action(T("web.admin.libraries.match"), "StaleMatch_" + Uri.EscapeDataString(row.ContentId + ":" + row.Provider), "search", height: card ? 44 : 28, iconSize: 12, gap: 4, fontSize: card ? 14 : 12);
            button.Click += async (_, _) =>
            {
                await Page.ShowMatchAsync(row.ContentId, row.Title, row.Year, row.ContentType, row.LibraryId, UiOwner);
            };
            return button;
        }
    }
}
