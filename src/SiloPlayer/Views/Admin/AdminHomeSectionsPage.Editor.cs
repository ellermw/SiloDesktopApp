using System.Text.Json.Nodes;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Add row (step 1: pick a kind; step 2: preview, variant, name, More options) and Edit row in one in-place modal panel shaped like the official StepDialog (AddRowDialog).</summary>
public sealed partial class AdminHomeSectionsPage
{
    private Grid? _editorOverlay;
    private Border? _editor;
    private StackPanel _editorFields = new() { Spacing = 16 };
    private ScrollViewer _editorScroll = new();
    // Created fresh for every editor, so no control is ever added to a second parent.
    private TextBlock _editorTitle = U.Text("", 20), _editorNotice = U.Text("", 14, true), _editorFooter = U.Text("", 13, true);
    private Button _editorSave = Action(T("web.home.addRow.save"), "HomeSectionSave"), _editorBack = Action(T("web.home.addRow.allRows"), "HomeSectionBack"), _editorDelete = Action(T("web.home.addRow.deleteRow"), "HomeSectionDelete");
    private StackPanel _editorHeaderExtra = new(), _editorFooterStart = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>1 while the kind picker shows, 2 on the form.</summary>
    private int _editorStep = 1;
    private AdminHomeSectionDraft? _draft, _original;
    private AdminHomeSectionSession? _session;
    private MonitorAuthorityLease? _editorOwner;
    private CancellationTokenSource? _editorReads;
    private bool _editorDirty;
    /// <summary>Validation messages of the fields on screen. Cleared whenever the form is rebuilt, so a field that is no longer shown never keeps blocking Save.</summary>
    private readonly Dictionary<string, string> _fieldErrors = new(StringComparer.Ordinal);
    private long _previewRevision, _pickerRevision;
    private readonly Dictionary<string, string> _choiceLabels = new(StringComparer.Ordinal);
    private readonly HashSet<string> _extraCopies = new(StringComparer.Ordinal);
    private bool _pickerOnly;
    /// <summary>More options stays as the admin left it while the form is rebuilt; a new editor starts closed.</summary>
    private bool _moreExpanded;
    /// <summary>The control that gets focus when the form is next built: the name on a new form, else the control whose change rebuilt it.</summary>
    private string? _refocus;

    private bool EditorCurrent(long revision) => _active && _editor != null && revision == _dialogRevision && _editorOwner is { IsCurrent: true } && ReferenceEquals(_editorOwner, _controller?.State.Owner);
    private static string KindLabel(string type) => AdminHomeSectionsKinds.Label(type);
    private AdminHomeSectionCatalog? Catalog => _controller?.State.Catalog;

    private void OpenAdd()
    {
        if (_controller?.CanWrite != true || _editor != null) return;
        _session = null; _draft = null; _original = null; BeginEditor(); RenderPicker();
    }
    private async Task OpenEditAsync(string id)
    {
        var controller = _controller; if (controller?.CanWrite != true || _editor != null || _dialog != null) return;
        var revision = ++_dialogRevision;
        try
        {
            var session = await controller.OpenEditAsync(id);
            if (!_active || revision != _dialogRevision || !ReferenceEquals(controller, _controller) || session == null || !session.Owner.IsCurrent) return;
            _session = session; _draft = AdminHomeSectionsDraftFactory.FromRow(session.Row, Catalog); _original = CloneDraft(_draft); BeginEditor(); RenderForm();
        }
        catch (Exception) { if (_active && revision == _dialogRevision) Toasts?.Error(T("web.admin.homeRows.page.loadRowsFailed")); }
    }
    private static AdminHomeSectionDraft CloneDraft(AdminHomeSectionDraft draft) => new()
    {
        SectionType = draft.SectionType, Title = draft.Title, TitleFollowsVariant = draft.TitleFollowsVariant, Featured = draft.Featured, WebOnly = draft.WebOnly,
        Enabled = draft.Enabled, ItemLimit = draft.ItemLimit, Config = (JsonObject)draft.Config.DeepClone()
    };

    private void BeginEditor()
    {
        // One overlay at a time: an editor still open is taken off the page, so none is left covering it.
        if (_editorOverlay != null) { _root.Children.Remove(_editorOverlay); _editorReads?.Cancel(); _editorReads?.Dispose(); _editorReads = null; }
        _dialogRevision++; _editorOwner = _controller!.State.Owner;
        _editorTitle = U.Text("", 20); _editorTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; _editorTitle.CharacterSpacing = -20; _editorTitle.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetHeadingLevel(_editorTitle, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        _editorNotice = U.Text("", 14, true); _editorFooter = U.Text("", 13, true); _editorFooter.TextTrimming = TextTrimming.CharacterEllipsis; _editorFooter.TextWrapping = TextWrapping.NoWrap;
        _editorSave = U.Action(T("web.home.addRow.save"), "HomeSectionSave", primary: true); _editorBack = Action(T("web.home.addRow.allRows"), "HomeSectionBack"); _editorDelete = Action(T("web.home.addRow.deleteRow"), "HomeSectionDelete");
        _editorBack.BorderThickness = new(0); _editorBack.Height = 28; _editorBack.HorizontalAlignment = HorizontalAlignment.Left; _editorBack.Padding = new(8, 0, 8, 0); _editorBack.Margin = new(-10, 0, 0, 0);
        _editorDelete.BorderThickness = new(0); _editorDelete.Content = AdminHomeSectionsUi.ActionContent(T("web.home.addRow.deleteRow"), "trash-2", 14, AdminHomeSectionsUi.Solid(AdminMonitorUi.Destructive)); _editorDelete.Margin = new(-12, 0, 0, 0);
        _editorHeaderExtra = new StackPanel(); _editorFooterStart = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        _editorReads = CancellationTokenSource.CreateLinkedTokenSource(_editorOwner!.Token);
        _editorDirty = false; _moreExpanded = false; _fieldErrors.Clear(); _choiceLabels.Clear(); _extraCopies.Clear(); _collectionOptions = null; _collectionOptionsFailed = false; _collectionOptionsTruncated = false; _editorStep = 1; _kindQuery = "";
        foreach (var pair in _collectionNames) if (pair.Value.Title != null) _choiceLabels[pair.Key] = pair.Value.Title;
        _editorOverlay = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(185, 0, 0, 0)), TabFocusNavigation = KeyboardNavigationMode.Cycle };
        _editorOverlay.KeyDown += async (_, args) =>
        {
            if (!args.Handled && args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; if (await CanLeaveAsync()) CloseEditor(); }
        };
        // StepDialog: rounded-[20px], a header (back link, title, description, search), the body, and a footer on a tinted band.
        _editor = new Border { Background = U.Brush("AppBackgroundBrush"), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .9), BorderThickness = new(1), CornerRadius = new(20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(_editor, "HomeSectionEditor"); AutomationProperties.SetName(_editor, T(_session == null ? "web.home.addRow.addRow" : "web.home.addRow.editRow"));
        var layout = new Grid { TabFocusNavigation = KeyboardNavigationMode.Cycle };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Spacing = 6, Padding = new(28, 24, 56, 16) };
        header.Children.Add(_editorBack); header.Children.Add(_editorTitle); header.Children.Add(_editorNotice); header.Children.Add(_editorHeaderExtra); layout.Children.Add(header);
        _editorFields = new() { Spacing = 16, Padding = new(28, 0, 28, 24) };
        var scroller = _editorScroll = new ScrollViewer { Content = _editorFields, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(scroller, 1); layout.Children.Add(scroller);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var cancel = Action(AdminHomeSectionsText.T("common.cancel"), "HomeSectionCancel"); cancel.Click += async (_, _) => { if (await CanLeaveAsync()) CloseEditor(); };
        _editorSave.Click += EditorSaveClick; _editorBack.Click += EditorBackClick; _editorDelete.Click += EditorDeleteClick;
        buttons.Children.Add(cancel); buttons.Children.Add(_editorSave);
        var footerLine = new Grid { ColumnSpacing = 12 }; footerLine.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footerLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footerLine.Children.Add(_editorFooterStart); Grid.SetColumn(buttons, 1); footerLine.Children.Add(buttons);
        var footer = new Border { Child = footerLine, Padding = new(28, 16, 28, 16), BorderThickness = new(0, 1, 0, 0), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .9), Background = AdminMonitorUi.Solid(AdminMonitorUi.Surface, .55), CornerRadius = new(0, 0, 19, 19) };
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        var close = AdminHomeSectionsUi.GhostIcon("x", T("web.ui.close"), "HomeSectionClose", 34); close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top; close.Margin = new(0, 18, 18, 0);
        close.Click += async (_, _) => { if (await CanLeaveAsync()) CloseEditor(); };
        var frame = new Grid(); frame.Children.Add(layout); frame.Children.Add(close);
        _editor.Child = frame; _editorOverlay.Children.Add(_editor); _root.Children.Add(_editorOverlay);
        _scroll.IsEnabled = false; Fit(); Render();
    }

    /// <summary>The dialog's shape: centered at 1000 (picker) or 880 (form) px from 1024px up; below that a bottom sheet, and the form takes the whole window.</summary>
    private void FitEditor()
    {
        if (_editor == null) return;
        var width = ActualWidth; var height = ActualHeight; var picker = _editorStep == 1;
        _paneHeight = Math.Max(200, Math.Min(picker ? 860 : height, height - (_narrow ? 40 : 64)) - 210);
        if (_narrow)
        {
            _editor.HorizontalAlignment = HorizontalAlignment.Stretch; _editor.Width = double.NaN;
            if (picker) { _editor.VerticalAlignment = VerticalAlignment.Bottom; _editor.MaxHeight = Math.Max(240, height - 40); _editor.Height = Math.Max(240, height - 40); _editor.CornerRadius = new(20, 20, 0, 0); }
            else { _editor.VerticalAlignment = VerticalAlignment.Stretch; _editor.MaxHeight = double.PositiveInfinity; _editor.Height = double.NaN; _editor.CornerRadius = new(0); }
        }
        else
        {
            _editor.HorizontalAlignment = HorizontalAlignment.Center; _editor.VerticalAlignment = VerticalAlignment.Center; _editor.CornerRadius = new(20);
            _editor.Width = Math.Max(280, Math.Min(picker ? 1000 : 880, width - 48)); _editor.MaxHeight = Math.Max(240, height - 64);
            _editor.Height = picker ? Math.Max(240, Math.Min(860, height - 64)) : double.NaN;
        }
        if (_kindPane != null) _kindPane.Height = _paneHeight;
    }
    private double _paneHeight = 480;
    private string _kindQuery = "";
    private ScrollViewer? _kindPane;

    private void SetBack(string label) { _editorBack.Content = AdminHomeSectionsUi.ActionContent(label, "chevron-left", 13, U.Brush("SecondaryTextBrush")); AutomationProperties.SetName(_editorBack, label); }
    private void SetSave(string label, bool plus)
    {
        if (AutomationProperties.GetName(_editorSave) == label) return;
        _editorSave.Content = AdminHomeSectionsUi.ActionContent(label, plus ? "plus" : null, 14, AdminHomeSectionsUi.PrimaryInk); AutomationProperties.SetName(_editorSave, label);
    }
    /// <summary>The footer's left side (StepCount): two bars and "Step N of 2 · note" while adding; Delete row… while editing the form.</summary>
    private void SetFooterStart(int step, bool editing)
    {
        _editorFooterStart.Children.Clear();
        if (editing) { if (step == 2) _editorFooterStart.Children.Add(_editorDelete); return; }
        Border Bar(bool on) => new() { Width = 18, Height = 4, CornerRadius = new(2), Background = on ? U.Brush("PrimaryTextBrush") : AdminMonitorUi.Solid(AdminMonitorUi.Border), VerticalAlignment = VerticalAlignment.Center };
        _editorFooterStart.Children.Add(Bar(true)); _editorFooterStart.Children.Add(Bar(step == 2));
        var count = U.Text(T("web.ui.stepDialog.stepCount", new { step = (long)step, total = 2L }), 13, true); count.TextWrapping = TextWrapping.NoWrap; _editorFooterStart.Children.Add(count);
        if (!_phone && _editorFooter.Text.Length > 0) { _editorFooterStart.Children.Add(U.Text("·", 13, true)); _editorFooterStart.Children.Add(_editorFooter); }
    }

    private async void EditorBackClick(object sender, RoutedEventArgs args)
    {
        if (CanReturn) await ReturnToCollectionAsync();
        else if (_draft != null && _pickerOnly) { _pickerOnly = false; RenderForm(); }
        else RenderPicker();
    }
    private async void EditorDeleteClick(object sender, RoutedEventArgs args) { if (_session != null) await DeleteAsync([_session.Row.Id]); }

    private async void EditorSaveClick(object sender, RoutedEventArgs args)
    {
        if (_draft == null || _controller?.CanWrite != true || _fieldErrors.Count > 0 || _editorOwner?.IsCurrent != true) return;
        var controller = _controller; var revision = _dialogRevision; var draft = _draft;
        var fallback = draft.SectionType == "collection" && _choiceLabels.TryGetValue(AdminHomeSectionsDraftFactory.CollectionIdOf(draft.Config), out var collectionName) ? collectionName : null;
        // The name a save sends: typed, the collection's, the variant's or the kind's plain name.
        draft.Title = AdminHomeSectionsDraftFactory.SavedTitle(draft, Catalog, fallback);
        var copies = AdminHomeSectionsRules.LibraryCopyIds(draft, _scope, LibraryPages(), _extraCopies);
        try { draft.Validate(); }
        catch (ArgumentException ex) { _editorNotice.Text = ex.Message; return; }
        var creating = _session == null; var before = controller.State.Snapshot?.Rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        if (await controller.SaveAsync(_session, draft, copies) && EditorCurrent(revision))
        {
            var added = controller.State.NewIds.FirstOrDefault(); var linked = _linkedCollection;
            // CloseEditor forgets the link, so the way back to the collection is read first.
            var back = CanReturn ? _returnPath : null;
            CloseEditor();
            if (creating && linked) AfterLinkedAdd(added, back);
            else if (creating && added != null) ScrollToRow(added);
        }
        else if (EditorCurrent(revision)) ShowSaveFailure(controller, revision, creating);
    }

    private void ScrollToRow(string id)
    {
        var row = _rows.FirstOrDefault(entry => entry.Row.Id == id);
        if (row != null) _list.ScrollIntoView(row);
    }

    private void ShowSaveFailure(AdminHomeSectionsController controller, long revision, bool creating)
    {
        var state = controller.State;
        _editorNotice.Text = state.Failure == AdminHomeSectionsFailure.Conflict && !creating ? T("web.home.addRow.conflict") : state.Error ?? T(creating ? "web.home.addRow.addFailed" : "web.home.addRow.saveFailed");
        if (state.Failure == AdminHomeSectionsFailure.Refused) return;
        if (state.Failure == AdminHomeSectionsFailure.Conflict && !creating)
        {
            var reload = Action(T("web.home.addRow.reloadRow"), "HomeSectionReloadRow");
            reload.Click += async (_, _) => await ReloadRowAsync(revision);
            ShowRecovery(reload); return;
        }
        var review = Action(T("web.home.rows.reloadRows"), "HomeSectionReviewUnknown");
        review.Click += async (_, _) =>
        {
            if (!EditorCurrent(revision) || !await CanLeaveAsync()) return;
            CloseEditor(); await ReloadAllAsync();
        };
        ShowRecovery(review);
    }

    /// <summary>The way out of a stale or unknown save sits under the notice, outside the scrolling form: the notice and Save's blocked state stay in view, so the button does too however far the form was scrolled.</summary>
    private void ShowRecovery(Button button)
    { _editorHeaderExtra.Children.Clear(); button.HorizontalAlignment = HorizontalAlignment.Left; _editorHeaderExtra.Children.Add(button); }

    /// <summary>After a stale save: every field the admin changed keeps their value, every other field takes the row's new value; what changed elsewhere is named.</summary>
    private async Task ReloadRowAsync(long revision)
    {
        var controller = _controller; var session = _session; var draft = _draft; var original = _original;
        if (controller == null || session == null || draft == null || original == null || !EditorCurrent(revision)) return;
        try
        {
            await controller.ReloadAsync();
            if (!EditorCurrent(revision) || !controller.CanWrite) return;
            var fresh = await controller.OpenEditAsync(session.Row.Id);
            if (!EditorCurrent(revision) || fresh == null) return;
            var upstream = AdminHomeSectionsDraftFactory.FromRow(fresh.Row, Catalog);
            var changed = AdminHomeSectionsDraftFactory.MergeReloaded(original, draft, upstream);
            _session = fresh; _original = CloneDraft(upstream); _draft = draft; _editorDirty = true; RenderForm();
            var labels = changed.Select(field => T(field switch { "title" => "web.home.addRow.form.rowName", "shows" => "web.home.rows.draft.whatItShows", "itemLimit" => "web.home.addRow.more.itemLimit", _ => "web.home.addRow.more.heroBanner" }));
            if (changed.Count > 0) _editorNotice.Text = T("web.home.addRow.changedElsewhere", new { fields = string.Join(", ", labels) });
        }
        catch (Exception) { if (EditorCurrent(revision)) _editorNotice.Text = T("web.home.addRow.reloadFailed"); }
    }

    private void CloseEditor()
    {
        _dialogRevision++; _previewRevision++; _pickerRevision++;
        _editorReads?.Cancel(); _editorReads?.Dispose(); _editorReads = null;
        if (_editorOverlay != null) _root.Children.Remove(_editorOverlay);
        _editorFields.Children.Clear(); _editor = null; _editorOverlay = null; _draft = null; _original = null; _session = null; _editorOwner = null;
        _editorDirty = false; _pickerOnly = false; _fieldErrors.Clear(); _scroll.IsEnabled = true; _choiceLabels.Clear(); _collectionOptions = null; _ruleCapabilities = null; _seasonalCatalog = null;
        _linkedCollection = false; _afterLibraryChange = null; _libraryFocus = null;
        if (_active) { _add.IsEnabled = _controller?.CanWrite == true; _more.IsEnabled = _controller?.CanWrite == true; _pages.PillsEnabled = _controller?.State is { Busy: false, Owner.IsCurrent: true }; }
        if (_active && _add.IsLoaded && _add.IsEnabled) _add.Focus(FocusState.Programmatic);
    }

    // ── Step 1: pick a kind ──────────────────────────────────

    private void RenderPicker()
    {
        if (_editor == null) return;
        _pickerRevision++; _previewRevision++; _editorFields.Children.Clear(); _editorHeaderExtra.Children.Clear(); _fieldErrors.Clear(); _editorNotice.Text = ""; _editorStep = 1; _kindPane = null;
        var changing = _session != null;
        _editorTitle.Text = changing ? T("web.home.addRow.changeTitle") : T("web.home.addRow.addTitle", new { page = PageLabel });
        _editorNotice.Text = T(changing ? "web.home.addRow.changeDescription" : "web.home.addRow.pickDescription");
        _editorFooter.Text = changing ? "" : T("web.home.addRow.footerPickStep", new { page = PageLabel });
        _editorSave.Visibility = Visibility.Collapsed; _editorDelete.Visibility = Visibility.Collapsed;
        _pickerOnly = _draft != null; _editorBack.Visibility = _draft == null ? Visibility.Collapsed : Visibility.Visible;
        SetBack(changing ? T("web.home.addRow.editRow") : T("web.home.addRow.allRows"));
        SetFooterStart(1, changing); FitEditor();
        var catalog = Catalog;
        if (catalog == null)
        {
            var state = _controller?.State;
            _editorFields.Children.Add(U.Text(T(state?.CatalogFailed == true ? "web.home.addRow.catalogFailed" : "web.home.addRow.catalogLoading")));
            var retry = Action(T("web.home.rows.reloadRows"), "HomeSectionCatalogRetry");
            retry.Click += async (_, _) => { retry.IsEnabled = false; await _controller!.ReloadCatalogAsync(); if (_editor != null) RenderPicker(); };
            _editorFields.Children.Add(retry); return;
        }
        var rows = _controller?.State.Snapshot?.Rows ?? [];
        var typesOnPage = rows.Select(row => row.SectionType).ToHashSet(StringComparer.Ordinal);
        var search = Input(T("web.home.addRow.searchRows"), "HomeSectionKindSearch", "", _ => { });
        search.PlaceholderText = T(_phone ? "web.home.addRow.searchRows" : "web.home.addRow.searchRowsPlaceholder"); search.Width = double.NaN; search.HorizontalAlignment = HorizontalAlignment.Stretch; search.Height = 44; search.MinHeight = 44; search.Margin = new(0, 6, 0, 0);
        _editorHeaderExtra.Children.Add(search);
        // The body: a rail of the groups beside the cards (chips above them on a narrow window), each side scrolling on its own.
        var rail = new StackPanel { Spacing = 2, Padding = new(12, 16, 12, 16) }; var railChips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new(0, 0, 0, 4) };
        var cards = new StackPanel { Spacing = 24, Padding = _narrow ? new(0, 12, 0, 24) : new(24, 18, 24, 24) };
        var pane = _kindPane = new ScrollViewer { Content = cards, Height = _paneHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(pane, "HomeSectionKindPane");
        var sections = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal); string? active = null, refocusGroup = null;
        FrameworkElement Card(AdminHomeRecipe recipe)
        {
            var type = recipe.Type; var group = AdminHomeSectionsKinds.Group(type); var label = KindLabel(type); var sentence = AdminHomeSectionsKinds.Sentence(type);
            var host = new Grid(); host.RowDefinitions.Add(new() { Height = GridLength.Auto }); host.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var button = U.Action(label, "HomeSectionKind_" + type); button.Height = double.NaN; button.MinHeight = 0; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.VerticalAlignment = VerticalAlignment.Stretch;
            button.CornerRadius = new(14); button.BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .9); button.Background = AdminMonitorUi.Solid(AdminMonitorUi.Surface, .6); button.Content = null;
            AutomationProperties.SetName(button, label + ". " + sentence); Grid.SetRowSpan(button, 2);
            button.Click += (_, _) => PickRecipe(recipe, recipe.Presets.FirstOrDefault()); host.Children.Add(button);
            var onPage = typesOnPage.Contains(type);
            var content = new Grid { ColumnSpacing = 14, Padding = new(14, 14, 16, 14), IsHitTestVisible = false }; content.ColumnDefinitions.Add(new() { Width = new(40) }); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            content.Children.Add(AdminHomeSectionsUi.KindTile(type, group));
            var texts = new StackPanel { Spacing = 2, Margin = new(0, 0, onPage ? 96 : 0, 0) };
            var title = U.Text(label, 14.5); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; texts.Children.Add(title);
            texts.Children.Add(U.Text(sentence, 13, true)); Grid.SetColumn(texts, 1); content.Children.Add(texts);
            if (onPage)
            {
                var tag = AdminHomeSectionsUi.Tag(T("web.home.addRow.onPage", new { page = PageLabel }), AdminMonitorUi.Muted, "check"); tag.HorizontalAlignment = HorizontalAlignment.Right; tag.VerticalAlignment = VerticalAlignment.Top; tag.Margin = new(0, -2, -4, 0);
                Grid.SetColumn(tag, 1); content.Children.Add(tag);
            }
            host.Children.Add(content);
            var options = AdminHomeSectionsKinds.Family(type)?.Options ?? [];
            if (options.Count >= 2 && !_narrow)
            {
                // The card's variants as chips; each opens step 2 with that variant picked.
                var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(68, -4, 16, 14) }; Grid.SetRow(chips, 1);
                foreach (var option in options.Take(3))
                {
                    var chip = U.Action(T(option.ChipKey), "HomeSectionKindVariant_" + type + "_" + option.PresetKey); chip.Height = 26; chip.MinHeight = 0; chip.CornerRadius = new(13); chip.FontSize = 13; chip.Padding = new(10, 0, 10, 0);
                    AutomationProperties.SetName(chip, T("web.home.addRow.chips.label", new { kind = label, variant = T(option.ChipKey) }));
                    var chosen = option; chip.Click += (_, _) => PickRecipe(recipe, recipe.Presets.FirstOrDefault(preset => preset.Key == chosen.PresetKey) ?? recipe.Presets.FirstOrDefault()); chips.Children.Add(chip);
                }
                if (options.Count > 3) chips.Children.Add(U.Text(T("web.home.addRow.chips.more", new { count = (long)(options.Count - 3) }), 13, true));
                host.Children.Add(chips);
            }
            return host;
        }
        void Refresh()
        {
            cards.Children.Clear(); rail.Children.Clear(); railChips.Children.Clear(); sections.Clear(); var query = search.Text.Trim();
            var groups = AdminHomeSectionsKinds.PickerGroups(catalog); var shown = new List<string>();
            foreach (var (group, entries) in groups)
            {
                var matches = entries.Where(recipe => query == "" || KindLabel(recipe.Type).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || AdminHomeSectionsKinds.Sentence(recipe.Type).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || recipe.Presets.Any(preset => preset.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
                if (matches.Length == 0) continue;
                shown.Add(group);
                var section = new StackPanel { Spacing = 12 }; sections[group] = section;
                var heading = new TextBlock { FontSize = 13 };
                heading.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = AdminHomeSectionsKinds.GroupLabel(group), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = U.Brush("PrimaryTextBrush") });
                heading.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "  " + AdminHomeSectionsKinds.GroupBlurb(group), Foreground = U.Brush("SecondaryTextBrush") });
                AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3); section.Children.Add(heading);
                var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 }; var columns = _narrow ? 1 : 2;
                for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                for (var i = 0; i < matches.Length; i++)
                {
                    if (i % columns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    var card = Card(matches[i]); Grid.SetRow(card, i / columns); Grid.SetColumn(card, i % columns); grid.Children.Add(card);
                }
                section.Children.Add(grid); cards.Children.Add(section);
            }
            if (shown.Count == 0)
            {
                var none = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Padding = new(0, 48, 0, 48) };
                none.Children.Add(U.Text(T("web.home.addRow.picker.noMatch", new { query }), 14, true));
                var clear = Action(T("web.home.addRow.picker.clearSearch"), "HomeSectionKindClear"); clear.HorizontalAlignment = HorizontalAlignment.Center; clear.Click += (_, _) => { search.Text = ""; search.Focus(FocusState.Programmatic); };
                none.Children.Add(clear); cards.Children.Add(none);
            }
            if (active == null || !shown.Contains(active)) active = shown.FirstOrDefault();
            foreach (var group in shown)
            {
                var chosenGroup = group; var isActive = group == active; var tint = AdminHomeSectionsUi.GroupTint(group);
                var count = groups.First(entry => entry.Group == group).Cards.Count;
                var entry = U.Action(AdminHomeSectionsKinds.GroupLabel(group), "HomeSectionCategory_" + group); entry.BorderThickness = new(isActive && !_narrow ? 1 : _narrow ? 1 : 0);
                // Choosing a group rebuilds the rail, so the entry that was chosen is a new one: it takes focus back once it is loaded.
                if (refocusGroup == group) { refocusGroup = null; entry.Loaded += (_, _) => entry.Focus(FocusState.Programmatic); }
                void Go() { active = chosenGroup; refocusGroup = chosenGroup; Refresh(); if (sections.TryGetValue(chosenGroup, out var target)) target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 }); }
                entry.Click += (_, _) => Go();
                if (_narrow)
                {
                    entry.Height = 40; entry.CornerRadius = new(20); entry.Padding = new(16, 0, 16, 0); entry.Background = isActive ? U.Brush("PrimaryTextBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    entry.Content = new TextBlock { Text = AdminHomeSectionsKinds.GroupLabel(group), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = isActive ? U.Brush("AppBackgroundBrush") : U.Brush("PrimaryTextBrush") };
                    railChips.Children.Add(entry);
                }
                else
                {
                    var line = new Grid { ColumnSpacing = 10 }; line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    var tile = new Border { Width = 22, Height = 22, CornerRadius = new(7), BorderThickness = new(1), BorderBrush = AdminHomeSectionsUi.Solid(tint, .25), Background = AdminHomeSectionsUi.Solid(tint, .15) };
                    var glyph = AdminHomeSectionsUi.Icon(AdminHomeSectionsUi.GroupIconName(group), 13, AdminHomeSectionsUi.Solid(tint)); glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center; tile.Child = glyph;
                    line.Children.Add(tile);
                    var name = U.Text(AdminHomeSectionsKinds.GroupLabel(group), 14, !isActive); Grid.SetColumn(name, 1); line.Children.Add(name);
                    var number = U.Text(count.ToString("N0", SiloPlayer.Core.Localization.AppStrings.FormatCulture), 12, true); Grid.SetColumn(number, 2); line.Children.Add(number);
                    line.Width = 184; entry.Height = 40; entry.CornerRadius = new(10); entry.Padding = new(12, 0, 12, 0); entry.HorizontalAlignment = HorizontalAlignment.Stretch; entry.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    entry.Background = isActive ? U.Brush("AccentBackgroundBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent); entry.BorderBrush = U.Brush("BorderBrush"); entry.Content = line;
                    AutomationProperties.SetName(entry, AdminHomeSectionsKinds.GroupLabel(group)); rail.Children.Add(entry);
                }
            }
        }
        search.Text = _kindQuery; search.TextChanged += (_, _) => { _kindQuery = search.Text; Refresh(); };
        Refresh();
        if (_narrow)
        {
            _editorFields.Padding = new(28, 0, 28, 0);
            _editorFields.Children.Add(new ScrollViewer { Content = railChips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Disabled });
            _editorFields.Children.Add(pane);
            if (rows.Count > 0) _editorFields.Children.Add(U.Text(T("web.home.addRow.picker.onPageNote", new { page = PageLabel }), 12, true));
        }
        else
        {
            _editorFields.Padding = new(0);
            var split = new Grid { BorderThickness = new(0, 1, 0, 0), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .9) }; split.ColumnDefinitions.Add(new() { Width = new(232) }); split.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var left = new StackPanel { BorderThickness = new(0, 0, 1, 0), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .9), Height = _paneHeight }; var leftScroll = new ScrollViewer { Content = rail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            left.Children.Add(leftScroll);
            if (rows.Count > 0) { var note = U.Text(T("web.home.addRow.picker.onPageNote", new { page = PageLabel }), 13, true); note.Margin = new(20, 4, 12, 12); rail.Children.Add(note); }
            split.Children.Add(left); Grid.SetColumn(pane, 1); split.Children.Add(pane); _editorFields.Children.Add(split);
        }
        search.Focus(FocusState.Programmatic);
    }

    private void PickRecipe(AdminHomeRecipe recipe, AdminHomePreset? preset)
    {
        _draft = AdminHomeSectionsDraftFactory.ForPick(recipe, preset, _draft, _session != null); _editorDirty = true; _pickerOnly = false; RenderForm();
    }

    // ── Step 2: the form ─────────────────────────────────────

    private void RenderForm(string? focus = "HomeSectionTitle")
    {
        var draft = _draft; if (draft == null || _editor == null) return;
        _pickerRevision++; _previewRevision++; _editorFields.Children.Clear(); _fieldErrors.Clear(); _pickerOnly = false; _refocus = focus; _afterLibraryChange = null;
        var editing = _session != null; var onLibraryPage = _scope.Kind == "library";
        _editorTitle.Text = editing ? T("web.home.addRow.editRow") : KindLabel(draft.SectionType);
        _editorNotice.Text = editing ? T("web.home.addRow.editDescription", new { page = PageLabel })
            : draft.SectionType == "custom_filter" ? T("web.home.addRow.customFilterDescription") : AdminHomeSectionsKinds.Sentence(draft.SectionType);
        var copies = !editing && onLibraryPage && !draft.Featured && draft.CanCopy && CopyTargets(draft).Count > 0;
        _editorFooter.Text = editing ? "" : T(copies ? "web.home.addRow.footerFormStepCopies" : "web.home.addRow.footerFormStep", new { page = PageLabel });
        _editorSave.Visibility = Visibility.Visible; _editorStep = 2; _editorHeaderExtra.Children.Clear(); _editorFields.Padding = new(28, 0, 28, 24);
        _editorSave.IsEnabled = _controller?.CanWrite == true; _editorDelete.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        SetFooterStart(2, editing); FitEditor();
        var kindLocked = AdminHomeSectionsKinds.VariantLocked(draft.SectionType, draft.Config);
        // Editing has no back link: what the row shows is changed from its Shows line.
        _editorBack.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        SetBack(editing ? T("web.home.addRow.changeTitle") : T("web.home.addRow.allRows"));
        if (CanReturn) { SetBack(T("web.home.rows.links.back", new { collection = _choiceLabels.GetValueOrDefault(AdminHomeSectionsDraftFactory.CollectionIdOf(draft.Config), KindLabel("collection")) })); _editorBack.Visibility = Visibility.Visible; }
        var rules = draft.SectionType is "custom_filter" or "genre";
        var control = draft.SectionType is "collection" or "collection_posters" or "admin_curated_list" || rules;
        // Collection, rule and Editor's picks rows put their control above the preview; the others show the preview first.
        var previewHost = new StackPanel { Spacing = 10 };
        if (editing) ShowsSummary(draft, kindLocked);
        if (control) BuildKindControl(draft, rules);
        _editorFields.Children.Add(previewHost);
        BuildVariant(draft, kindLocked);
        BuildPrimaryParams(draft, onLibraryPage);
        BuildName(draft);
        BuildCopies(draft, editing, onLibraryPage);
        if (!AdminHomeSectionsKinds.HoldsCollections(draft.SectionType)) BuildMore(draft, rules);
        BuildPreview(previewHost, draft);
        RefreshSave(); _refocus = null;
    }

    /// <summary>Gives focus to <paramref name="control"/> once it is on screen, when it is the one this build of the form asked for.</summary>
    private void Refocus(Control control, string id)
    { if (_refocus != id) return; _refocus = null; control.Loaded += (_, _) => control.Focus(FocusState.Programmatic); }

    /// <summary>Edit row's Shows line: the kind's tile, its name and sentence, and Change when the kind may change.</summary>
    private void ShowsSummary(AdminHomeSectionDraft draft, bool kindLocked)
    {
        var box = new StackPanel { Spacing = 8 };
        var label = U.Text(T("web.home.addRow.shows.label"), 14); label.FontWeight = Microsoft.UI.Text.FontWeights.Medium; box.Children.Add(label);
        var line = new Grid { ColumnSpacing = 12 }; line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var tile = AdminHomeSectionsUi.KindTile(draft.SectionType, AdminHomeSectionsKinds.Group(draft.SectionType)); tile.Width = tile.Height = 36; tile.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(tile);
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var name = U.Text(AdminHomeSectionsKinds.ShowsLabel(draft.SectionType, draft.Config), 14); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; texts.Children.Add(name);
        texts.Children.Add(U.Text(AdminHomeSectionsKinds.Sentence(draft.SectionType).TrimEnd('.'), 13, true)); Grid.SetColumn(texts, 1); line.Children.Add(texts);
        if (!kindLocked)
        {
            var change = Action(T("web.home.addRow.shows.change"), "HomeSectionChange"); AutomationProperties.SetName(change, T("web.home.addRow.shows.changeLabel", new { kind = AdminHomeSectionsKinds.Label(draft.SectionType) }));
            change.Click += (_, _) => RenderPicker(); change.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(change, 2); line.Children.Add(change);
        }
        box.Children.Add(new Border { Child = line, BorderThickness = new(1), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .9), CornerRadius = new(16), Padding = new(10, 10, 12, 10) });
        _editorFields.Children.Add(box);
    }

    private void BuildVariant(AdminHomeSectionDraft draft, bool kindLocked)
    {
        var family = AdminHomeSectionsKinds.Family(draft.SectionType);
        if (family == null || kindLocked || AdminHomeSectionsKinds.NotOfferedForNewRows.Contains(draft.SectionType)) return;
        var selected = AdminHomeSectionsRules.VariantOf(draft.SectionType, draft.Config);
        var variants = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(variants, "HomeSectionVariant"); Refocus(variants, "HomeSectionVariant");
        foreach (var option in family.Options) { var item = new ComboBoxItem { Content = T(option.LabelKey), Tag = option.PresetKey }; variants.Items.Add(item); if (option.PresetKey == selected) variants.SelectedItem = item; }
        var hint = U.Text(family.Options.FirstOrDefault(option => option.PresetKey == selected)?.HintKey is { } key ? T(key) : "", 12, true);
        variants.SelectionChanged += (_, _) =>
        {
            if (variants.SelectedItem is not ComboBoxItem { Tag: string key }) return;
            AdminHomeSectionsDraftFactory.WithVariant(draft, Catalog, key); _editorDirty = true; RenderForm("HomeSectionVariant");
        };
        var box = new StackPanel { Spacing = 6 }; box.Children.Add(variants); box.Children.Add(hint);
        Field(_editorFields, T(family.ControlKey), box);
    }

    private void BuildName(AdminHomeSectionDraft draft)
    {
        var fallback = draft.SectionType == "collection" ? _choiceLabels.GetValueOrDefault(AdminHomeSectionsDraftFactory.CollectionIdOf(draft.Config)) : null;
        var name = Input(T("web.home.addRow.form.rowName"), "HomeSectionTitle", draft.Title, value => { AdminHomeSectionsDraftFactory.WithTitle(draft, value); Dirty(); }, 500);
        name.PlaceholderText = AdminHomeSectionsDraftFactory.SavedTitle(new AdminHomeSectionDraft { SectionType = draft.SectionType, Config = draft.Config }, Catalog, fallback);
        Refocus(name, "HomeSectionTitle");
        var box = new StackPanel { Spacing = 6 }; box.Children.Add(name);
        var family = AdminHomeSectionsKinds.Family(draft.SectionType);
        if (draft.SectionType == "collection" && draft.TitleFollowsVariant) box.Children.Add(U.Text(T("web.home.addRow.form.nameFromCollection"), 12, true));
        else if (family != null && draft.TitleFollowsVariant) box.Children.Add(U.Text(T("web.home.addRow.form.nameFollows", new { variant = T(family.FollowsKey) }), 12, true));
        Field(_editorFields, T("web.home.addRow.form.rowName"), box);
    }

    private void BuildCopies(AdminHomeSectionDraft draft, bool editing, bool onLibraryPage)
    {
        if (editing || !onLibraryPage) return;
        IReadOnlyList<AdminHomeSectionChoice> targets = draft.Featured ? Array.Empty<AdminHomeSectionChoice>() : CopyTargets(draft);
        _extraCopies.IntersectWith(targets.Select(library => library.Id));
        if (targets.Count == 0 && !draft.Featured) return;
        var panel = new StackPanel { Spacing = 6 };
        if (draft.Featured) { panel.Children.Add(U.Text(T("web.home.addRow.pages.helpHero"), 12, true)); Field(_editorFields, T("web.home.addRow.pages.label"), panel); return; }
        var copies = new ListView { ItemsSource = targets, DisplayMemberPath = "Label", SelectionMode = ListViewSelectionMode.Multiple, IsMultiSelectCheckBoxEnabled = true, MaxHeight = 180 };
        copies.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
        AutomationProperties.SetAutomationId(copies, "HomeSectionExtraCopies");
        foreach (var library in targets.Where(library => _extraCopies.Contains(library.Id))) copies.SelectedItems.Add(library);
        copies.SelectionChanged += (_, args) =>
        {
            foreach (var library in args.RemovedItems.OfType<AdminHomeSectionChoice>()) _extraCopies.Remove(library.Id);
            foreach (var library in args.AddedItems.OfType<AdminHomeSectionChoice>())
                if (_extraCopies.Count < AdminHomeSectionsRules.BulkLibraryLimit - 1) _extraCopies.Add(library.Id); else copies.SelectedItems.Remove(library);
            Dirty();
        };
        panel.Children.Add(copies); panel.Children.Add(U.Text(T("web.home.addRow.pages.help"), 12, true));
        Field(_editorFields, T("web.home.addRow.pages.label"), panel);
    }

    private void BuildMore(AdminHomeSectionDraft draft, bool rules)
    {
        var more = new StackPanel { Spacing = 14 };
        if (rules) RuleSortField(more);
        BuildMoreParams(more, draft);
        var limit = new NumberBox { Minimum = Math.Min(1, draft.ItemLimit), Maximum = Math.Max(AdminHomeSectionsDraftFactory.MaxFormItemLimit, draft.ItemLimit), SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Value = draft.ItemLimit, MinWidth = 120 };
        AutomationProperties.SetAutomationId(limit, "HomeSectionItemLimit");
        var limitHint = U.Text(T("web.home.addRow.more.itemLimitHint"), 12, true);
        // Only a whole number in range reaches the draft; a blank or partial entry leaves the saved value in place.
        limit.ValueChanged += (_, args) => { if (double.IsFinite(args.NewValue) && args.NewValue == Math.Truncate(args.NewValue) && args.NewValue is >= 1 and <= 500) { draft.ItemLimit = (int)args.NewValue; Dirty(); } };
        var limitBox = new StackPanel { Spacing = 6 }; limitBox.Children.Add(limit); limitBox.Children.Add(limitHint);
        Field(more, T("web.home.addRow.more.itemLimit"), limitBox);
        var webOnly = _controller?.State.Capabilities?.WebOnly == true;
        var hero = Toggle("HomeSectionHero", draft.Featured, value => { draft.Featured = value; if (!value) draft.WebOnly = false; if (value) _extraCopies.Clear(); Dirty(); RenderForm("HomeSectionHero"); });
        Refocus(hero, "HomeSectionHero");
        var heroBox = new StackPanel { Spacing = 6 }; heroBox.Children.Add(hero);
        heroBox.Children.Add(U.Text(T(webOnly ? "web.home.addRow.more.heroHintWeb" : "web.home.addRow.more.heroHint"), 12, true));
        Field(more, T("web.home.addRow.more.heroBanner"), heroBox);
        if (draft.Featured && webOnly)
        {
            var apps = Toggle("HomeSectionShowInApps", !draft.WebOnly, value => { draft.WebOnly = !value; Dirty(); RenderForm("HomeSectionShowInApps"); });
            Refocus(apps, "HomeSectionShowInApps");
            var appsBox = new StackPanel { Spacing = 6 }; appsBox.Children.Add(apps);
            appsBox.Children.Add(U.Text(T(draft.WebOnly ? "web.home.addRow.more.webOnlyHint" : "web.home.addRow.more.appsHint"), 12, true));
            Field(more, T("web.home.addRow.more.showInApps"), appsBox);
        }
        var summary = (rules ? AdminHomeSectionsDescription.RuleSortSummary(draft.Config) + " · " : "") + AdminHomeSectionsDescription.TitleCount(draft.ItemLimit)
            + " · " + T(draft.Featured ? (draft.WebOnly && webOnly ? "web.home.addRow.more.heroWebOnly" : "web.home.addRow.more.hero") : "web.home.addRow.more.notHero");
        var expander = new Expander { Header = T("web.home.addRow.more.title") + " — " + summary, Content = more, IsExpanded = _moreExpanded, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        expander.Expanding += (_, _) => _moreExpanded = true; expander.Collapsed += (_, _) => _moreExpanded = false;
        AutomationProperties.SetAutomationId(expander, "HomeSectionMoreOptions");
        _editorFields.Children.Add(expander);
    }

    private void BuildPrimaryParams(AdminHomeSectionDraft draft, bool onLibraryPage)
    {
        // Recently added and Recently released filter by library on Home only; a library page is already one library.
        if (AdminHomeSectionsRules.UsesLibraryFilter(draft.SectionType) && !onLibraryPage) LibrarySelector(_editorFields);
        else if (draft.SectionType == "trending_discover")
            Field(_editorFields, T("web.home.addRow.params.trendingShow"), Select("HomeSection_filter_type",
                [("", T("web.collections.tmdbSources.mediaBoth")), ("movie", T("web.collections.tmdbSources.mediaMovies")), ("series", T("web.collections.tmdbSources.mediaTv"))],
                Value(draft.Config, "filter_type") is "movie" or "series" ? Value(draft.Config, "filter_type") : "", value => SetConfig("filter_type", value == "" ? null : JsonValue.Create(value))));
        else if (draft.SectionType == "seasonal_themed") SeasonalEditor(_editorFields);
    }

    private void BuildMoreParams(StackPanel more, AdminHomeSectionDraft draft)
    {
        var type = draft.SectionType;
        if (type is "watchlist" or "favorites")
        {
            Field(more, T("web.home.addRow.params.mediaType"), Select("HomeSection_filter_type",
                [("", T("web.home.addRow.params.mediaEverything")), ("movie", T("web.home.addRow.params.mediaMovies")), ("series", T("web.home.addRow.params.mediaShows")), ("audiobook", T("web.home.addRow.params.mediaAudiobooks"))],
                Value(draft.Config, "filter_type"), value => SetConfig("filter_type", value == "" ? null : JsonValue.Create(value))));
            LibrarySelector(more);
            var orders = new[] { ("", T("web.home.addRow.params.orderList")), ("added_at:desc", T("web.home.addRow.params.orderAddedDesc")), ("added_at:asc", T("web.home.addRow.params.orderAddedAsc")),
                ("title:asc", T("web.home.addRow.params.orderTitleAsc")), ("title:desc", T("web.home.addRow.params.orderTitleDesc")), ("release_date:desc", T("web.home.addRow.params.orderReleaseDesc")),
                ("release_date:asc", T("web.home.addRow.params.orderReleaseAsc")), ("rating_imdb:desc", T("web.home.addRow.params.orderImdbDesc")) };
            var sort = Value(draft.Config, "sort"); var current = "";
            if (sort != "") { var order = Value(draft.Config, "order") is { Length: > 0 } saved ? saved : sort == "title" ? "asc" : "desc"; current = sort + ":" + order; if (!orders.Any(option => option.Item1 == current)) current = ""; }
            Field(more, T("web.home.addRow.order"), Select("HomeSectionListOrder", orders, current, value =>
            { var parts = value.Split(':'); SetConfig("sort", parts[0] == "" ? null : JsonValue.Create(parts[0])); SetConfig("order", parts.Length > 1 ? JsonValue.Create(parts[1]) : null); }));
        }
        if (type == "returning_shows") ConfigNumber(more, T("web.home.addRow.params.lookBack"), "lookback_days", 30, T("web.home.addRow.params.lookBackHint"));
        if (type == "short_watches") ConfigNumber(more, T("web.home.addRow.params.maxMinutes"), "max_minutes", 95, T("web.home.addRow.params.maxMinutesHint"));
        if (type == "anniversaries") ConfigNumber(more, T("web.home.addRow.params.milestone"), "milestone_years", 5, T("web.home.addRow.params.milestoneHint"));
        if (type == "taste_match")
        {
            var genre = Input(T("web.home.addRow.params.genre"), "HomeSection_genre", Value(draft.Config, "genre"), value => SetConfig("genre", JsonValue.Create(value)));
            var box = new StackPanel { Spacing = 6 }; box.Children.Add(genre); box.Children.Add(U.Text(T("web.home.addRow.params.genreHint"), 12, true));
            Field(more, T("web.home.addRow.params.genre"), box);
        }
        if (type == "editorial_spotlight")
        {
            // A preset with a pinned subject and no auto_rotate is pinned, as the server reads it.
            var rotating = draft.Config["auto_rotate"] is JsonValue rotate && rotate.TryGetValue<bool>(out var savedRotate) ? savedRotate : string.IsNullOrEmpty(Value(draft.Config, "subject"));
            var autoRotate = Toggle("HomeSectionAutoRotate", rotating, value => { SetConfig("auto_rotate", JsonValue.Create(value)); RenderForm("HomeSectionAutoRotate"); });
            Refocus(autoRotate, "HomeSectionAutoRotate"); Field(more, T("web.home.addRow.params.autoRotate"), autoRotate);
            if (rotating) Field(more, T("web.home.addRow.params.howOften"), Select("HomeSection_rotation_cadence",
                [("daily", T("web.home.addRow.params.everyDay")), ("weekly", T("web.home.addRow.params.everyWeek")), ("monthly", T("web.home.addRow.params.everyMonth"))],
                Value(draft.Config, "rotation_cadence") is { Length: > 0 } cadence ? cadence : "weekly", value => SetConfig("rotation_cadence", JsonValue.Create(value))));
            else
            {
                var subject = Input(T("web.home.addRow.params.subject"), "HomeSection_subject", Value(draft.Config, "subject"), value => SetConfig("subject", JsonValue.Create(value)));
                subject.PlaceholderText = T(Value(draft.Config, "subject_type") == "era" ? "web.home.addRow.params.subjectEra" : "web.home.addRow.params.subjectPerson");
                Field(more, T("web.home.addRow.params.subject"), subject);
            }
        }
    }

    private void BuildKindControl(AdminHomeSectionDraft draft, bool rules)
    {
        if (draft.SectionType == "collection") CollectionSelector(_editorFields, multiple: false);
        else if (draft.SectionType == "collection_posters") CollectionSelector(_editorFields, multiple: true);
        else if (rules) RulesEditor(_editorFields);
        else if (draft.SectionType == "admin_curated_list") CuratedEditor(_editorFields);
    }

    // ── Validation, dirty state and field helpers ────────────

    private bool DraftCanSave()
    {
        if (_draft == null) return false;
        try
        {
            new AdminHomeSectionDraft { SectionType = _draft.SectionType, Title = string.IsNullOrWhiteSpace(_draft.Title) ? "Row" : _draft.Title,
                ItemLimit = _draft.ItemLimit, Featured = _draft.Featured, Config = _draft.Config }.Validate(); return true;
        }
        catch (ArgumentException) { return false; }
    }
    private void RefreshSave()
    {
        _editorSave.IsEnabled = _controller?.CanWrite == true && _fieldErrors.Count == 0 && DraftCanSave();
        // Add row names the pages it will reach once more than this one is picked; Save keeps its own name.
        if (_editorStep == 2 && _draft != null)
            SetSave(_session != null ? T("web.home.addRow.save") : _extraCopies.Count > 0 ? T("web.home.addRow.addToPages", AdminHomeSectionsText.Count(_extraCopies.Count + 1)) : T("web.home.addRow.addRow"), _session == null);
    }
    private long _draftVersion;
    private void Dirty() { _editorDirty = true; _draftVersion++; RefreshSave(); }
    /// <summary>Sets or clears the error of one on-screen field; the message shows beside it and Save waits until every shown field is valid.</summary>
    private void FieldError(string key, TextBlock? slot, string? message)
    {
        if (message == null) _fieldErrors.Remove(key); else _fieldErrors[key] = message;
        if (slot != null) slot.Text = message ?? "";
        if (message != null) _editorNotice.Text = message;
        RefreshSave();
    }
    private void SetConfig(string key, JsonNode? value) { if (_draft == null) return; if (value == null) _draft.Config.Remove(key); else _draft.Config[key] = value; Dirty(); }
    private static string Value(JsonObject config, string key) => config[key]?.ToString() ?? "";

    /// <summary>One optional whole-number setting; clearing it lets the server default apply. A stored value stays in the box until the admin edits it.</summary>
    private void ConfigNumber(StackPanel panel, string label, string key, int placeholder, string hint)
    {
        var error = U.Text("", 12);
        var box = Input(label, "HomeSection_" + key, Value(_draft!.Config, key), value =>
        {
            var text = value.Trim();
            if (text == "") { SetConfig(key, null); FieldError(key, error, null); }
            else if (int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0) { SetConfig(key, JsonValue.Create(number)); FieldError(key, error, null); }
            else FieldError(key, error, T("native.homeSections.enterAWholeNumberOf1"));
        }, 9);
        box.PlaceholderText = placeholder.ToString(); box.Width = 128; box.HorizontalAlignment = HorizontalAlignment.Left;
        var stack = new StackPanel { Spacing = 6 }; stack.Children.Add(box); stack.Children.Add(U.Text(hint, 12, true)); stack.Children.Add(error);
        Field(panel, label, stack);
    }

    private static TextBox Input(string label, string id, string value, Action<string> changed, int max = 500)
    {
        var input = U.Input(label, id); input.MaxLength = max; input.Text = value; input.Width = double.NaN; input.HorizontalAlignment = HorizontalAlignment.Stretch;
        input.TextChanged += (_, _) => changed(input.Text); return input;
    }
    private static ToggleSwitch Toggle(string id, bool value, Action<bool> changed)
    {
        // A switch re-reports its starting state once it is realized; only a different state is the admin's change.
        var toggle = new ToggleSwitch { IsOn = value, OffContent = "", OnContent = "" }; AutomationProperties.SetAutomationId(toggle, id); var last = value;
        toggle.Toggled += (_, _) => { if (toggle.IsOn == last) return; last = toggle.IsOn; changed(last); }; return toggle;
    }
    private static ComboBox Select(string id, IEnumerable<(string Value, string Label)> options, string selected, Action<string> changed)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(combo, id);
        foreach (var (value, label) in options) { var item = new ComboBoxItem { Tag = value, Content = label }; combo.Items.Add(item); if (value == selected) combo.SelectedItem = item; }
        // The initial selection is reported once more after realization; only a different value is the admin's change.
        var last = selected;
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string value } && value != last) { last = value; changed(value); } }; return combo;
    }
    private static void Field(StackPanel panel, string label, FrameworkElement input)
    { var stack = new StackPanel { Spacing = 6 }; stack.Children.Add(U.Text(label, 13)); AutomationProperties.SetName(input, label); stack.Children.Add(input); panel.Children.Add(stack); }

    // ── Preview ──────────────────────────────────────────────

    private void BuildPreview(StackPanel host, AdminHomeSectionDraft draft)
    {
        host.Children.Clear();
        var title = draft.Title.Trim() is { Length: > 0 } typed ? typed : AdminHomeSectionsDraftFactory.SavedTitle(draft, Catalog, draft.SectionType == "collection" ? _choiceLabels.GetValueOrDefault(AdminHomeSectionsDraftFactory.CollectionIdOf(draft.Config)) : null);
        var preview = new StackPanel { Spacing = 10 }; AutomationProperties.SetName(preview, T("web.home.addRow.preview.label", new { title }));
        var refresh = Action(T("web.home.addRow.livePreview"), "HomeSectionPreview");
        var wait = AdminHomeSectionsDraftFactory.PreviewWaitText(draft);
        refresh.IsEnabled = _controller?.State.Capabilities?.Preview == true && wait == null;
        refresh.Click += async (_, _) => await PreviewDraftAsync(preview);
        host.Children.Add(refresh); host.Children.Add(preview);
        if (_controller?.State.Capabilities?.Preview != true) preview.Children.Add(U.Text(T("web.home.addRow.previewUnavailable"), 13, true));
        else if (wait != null) preview.Children.Add(U.Text(wait, 13, true));
        else { preview.Children.Add(U.Text(T(_session == null ? "web.home.addRow.livePreviewAdd" : "web.home.addRow.livePreview"), 13, true)); _ = PreviewDraftAsync(preview); }
    }

    private async Task PreviewDraftAsync(StackPanel panel)
    {
        if (_draft == null || _editorOwner == null || _editorReads == null) return;
        var revision = _dialogRevision; var previewRevision = ++_previewRevision; var owner = _editorOwner; var version = _draftVersion;
        panel.Children.Clear(); panel.Children.Add(U.Text(T("web.home.addRow.catalogLoading"), 13, true));
        try
        {
            var draft = new AdminHomeSectionDraft { SectionType = _draft.SectionType, Title = "Preview", ItemLimit = Math.Max(1, _draft.ItemLimit), Config = _draft.Config };
            var result = await _api.PreviewAsync(owner, draft, _editorReads.Token, _scope);
            if (!EditorCurrent(revision) || previewRevision != _previewRevision) return;
            panel.Children.Clear(); panel.Children.Add(PreviewPanel(result));
            // Native adaptation: the WebUI refreshes the preview as the draft changes; here it refreshes on request.
            if (version != _draftVersion) panel.Children.Add(U.Text(T("native.homeSections.theDraftChangedAfterThisPreview"), 12, true));
        }
        catch (Exception) { if (EditorCurrent(revision) && previewRevision == _previewRevision) { panel.Children.Clear(); panel.Children.Add(U.Text(T("web.home.addRow.preview.failed"), 13, true)); } }
    }
    private readonly SemaphoreSlim _posterSlots = new(2, 2);
    private StackPanel PreviewPanel(AdminHomeSectionPreview result)
    {
        var panel = new StackPanel { Spacing = 8 };
        var cards = result.Collections.Count > 0 ? result.Collections : result.Items; var shown = Math.Min(12, cards.Count);
        panel.Children.Add(U.Text(cards.Count == 0 ? T("web.home.addRow.preview.empty")
            : System.Text.RegularExpressions.Regex.Replace(T("web.home.addRow.preview.count", new { refreshing = "no", count = (long)result.TotalCount, total = result.TotalCount.ToString("N0"), shown = shown.ToString() }), @"\s*·\s*", " · "), 13, true));
        var rail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var owner = _editorOwner ?? _controller?.State.Owner;
        var token = _editorReads?.Token ?? owner?.Token ?? CancellationToken.None;
        var revision = _dialogRevision; var previewRevision = _previewRevision;
        foreach (var card in cards.Take(12))
        {
            var tile = new StackPanel { Spacing = 6, Width = 96 };
            var image = new Image { Width = 96, Height = 144, Stretch = Stretch.UniformToFill };
            tile.Children.Add(new Border { Child = image, Width = 96, Height = 144, Background = U.Brush("MutedBrush"), CornerRadius = new(8) });
            tile.Children.Add(U.Text(card.Label, 12)); rail.Children.Add(tile);
            var requested = false;
            image.Loaded += async (_, _) =>
            {
                if (requested || string.IsNullOrEmpty(card.PosterUrl) || owner?.IsCurrent != true) return;
                requested = true; var entered = false;
                try
                {
                    await _posterSlots.WaitAsync(token); entered = true;
                    var bytes = await _api.PosterAsync(owner, card.PosterUrl, token);
                    if (!_active || !owner.IsCurrent || revision != _dialogRevision || previewRevision != _previewRevision || !image.IsLoaded) return;
                    using var stream = new MemoryStream(bytes); using var source = stream.AsRandomAccessStream();
                    var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(); await bitmap.SetSourceAsync(source);
                    if (_active && owner.IsCurrent && revision == _dialogRevision && previewRevision == _previewRevision && image.IsLoaded) image.Source = bitmap;
                }
                catch (Exception) { /* Keep the named placeholder; never expose a signed URL or decode from a network URI. */ }
                finally { if (entered) _posterSlots.Release(); }
            };
        }
        panel.Children.Add(new ScrollViewer { Content = rail, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return panel;
    }
}
