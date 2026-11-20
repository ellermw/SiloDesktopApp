using System.Text.Json.Nodes;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminHomeSectionsPage
{
    private Grid? _editorOverlay;
    private Border? _editor;
    private StackPanel _editorFields = new() { Spacing = 16 };
    private readonly TextBlock _editorTitle = U.Text("", 24);
    private readonly TextBlock _editorNotice = U.Text("", 13);
    private readonly Button _editorSave = Action("Save", "HomeSectionSave");
    private readonly Button _editorBack = Action("All rows", "HomeSectionBack");
    private readonly Button _editorDelete = Action("Delete row…", "HomeSectionDelete");
    private AdminHomeSectionDraft? _draft;
    private AdminHomeSectionSession? _session;
    private MonitorAuthorityLease? _editorOwner;
    private CancellationTokenSource? _editorReads;
    private bool _editorDirty, _editorInvalid;
    private long _previewRevision, _pickerRevision;
    private readonly Dictionary<string, string> _choiceLabels = new(StringComparer.Ordinal);
    private readonly HashSet<string> _extraCopies = new(StringComparer.Ordinal);

    private static readonly string[] GroupOrder = ["keep", "new", "popular", "picked", "moods", "collections"];
    private static readonly Dictionary<string, (string Group, string Key, string Name)> Kinds = new()
    {
        ["continue_watching"] = ("keep", "continueWatching", "Continue watching"), ["next_up"] = ("keep", "nextUp", "Next up"),
        ["next_in_series"] = ("keep", "nextInSeries", "Next in series"), ["watchlist"] = ("keep", "watchlist", "Watchlist"), ["favorites"] = ("keep", "favorites", "Favorites"),
        ["recently_added"] = ("new", "recentlyAdded", "Recently added"), ["recently_released"] = ("new", "recentlyReleased", "New releases"),
        ["new_to_library"] = ("new", "newToLibrary", "New this month"), ["returning_shows"] = ("new", "returningShows", "Returning shows"),
        ["trending_on_server"] = ("popular", "trendingOnServer", "Trending on this server"), ["most_watched"] = ("popular", "mostWatched", "Most watched"),
        ["trending_discover"] = ("popular", "trendingDiscover", "Trending worldwide"), ["profile_activity_feed"] = ("popular", "profileActivityFeed", "What others just watched"),
        ["recommended_for_you"] = ("picked", "recommendedForYou", "Recommended for you"), ["because_you_watched"] = ("picked", "becauseYouWatched", "Because you watched"),
        ["similar_users_liked"] = ("picked", "similarUsersLiked", "Profiles like you enjoyed"), ["taste_match"] = ("picked", "tasteMatch", "Top picks today"),
        ["mood_collection"] = ("moods", "moodCollection", "Mood picks"), ["seasonal_themed"] = ("moods", "seasonalThemed", "Seasonal picks"),
        ["editorial_spotlight"] = ("moods", "editorialSpotlight", "Spotlight"), ["format_showcase"] = ("moods", "formatShowcase", "4K & HDR showcase"),
        ["hidden_gems"] = ("moods", "hiddenGems", "Hidden gems"), ["critically_acclaimed"] = ("moods", "criticallyAcclaimed", "Critically acclaimed"),
        ["forgotten_favorites"] = ("moods", "forgottenFavorites", "Forgotten favorites"), ["genre_roulette"] = ("moods", "genreRoulette", "Genre roulette"),
        ["random"] = ("moods", "random", "Surprise me"), ["short_watches"] = ("moods", "shortWatches", "Short & sweet"), ["anniversaries"] = ("moods", "anniversaries", "Anniversaries"),
        ["collection"] = ("collections", "collection", "A collection"), ["collection_posters"] = ("collections", "collectionPosters", "Collections"),
        ["custom_filter"] = ("collections", "customFilter", "Titles matching rules"), ["admin_curated_list"] = ("collections", "adminCuratedList", "Editor's picks"),
        ["genre"] = ("collections", "genre", "Genre (no longer offered)"), ["award_winners"] = ("moods", "awardWinners", "Award winners (no longer offered)")
    };
    private static string KindLabel(string type) => Kinds.TryGetValue(type, out var kind) ? T("web.home.rows.kinds." + kind.Key) : "Row";
    private static bool NotOffered(string type) => type is "next_up" or "admin_curated_list" or "genre" or "award_winners";
    private bool EditorCurrent(long revision) => _active && _editor != null && revision == _dialogRevision && _editorOwner is { IsCurrent: true } && ReferenceEquals(_editorOwner, _controller?.State.Owner);

    private void OpenAdd()
    {
        if (_controller?.CanWrite != true || _controller.State.Catalog == null || _editor != null) return;
        _session = null; _draft = null; BeginEditor(); RenderPicker();
    }
    private async Task OpenEditAsync(string id)
    {
        var controller = _controller; if (controller?.CanWrite != true || _editor != null || _dialog != null) return;
        var revision = ++_dialogRevision;
        try
        {
            var session = await controller.OpenEditAsync(id);
            if (!_active || revision != _dialogRevision || !ReferenceEquals(controller, _controller) || session == null || !session.Owner.IsCurrent) return;
            _session = session; _draft = AdminHomeSectionDraft.From(session.Row); BeginEditor(); RenderForm();
        }
        catch (Exception) { if (_active && revision == _dialogRevision) _status.Text = "Couldn't load this row. Reload and try again."; }
    }
    private void BeginEditor()
    {
        _dialogRevision++; _editorOwner = _controller!.State.Owner;
        _editorReads = CancellationTokenSource.CreateLinkedTokenSource(_editorOwner!.Token);
        _editorDirty = _editorInvalid = false; _choiceLabels.Clear(); _extraCopies.Clear();
        foreach (var pair in _collectionLabels) _choiceLabels[pair.Key] = pair.Value;
        _editorOverlay = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(185, 0, 0, 0)), TabFocusNavigation = KeyboardNavigationMode.Cycle };
        _editorOverlay.KeyDown += async (_, args) =>
        {
            if (!args.Handled && args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; if (await CanLeaveAsync()) CloseEditor(); }
        };
        _editor = new Border { Background = U.Brush("AppBackgroundBrush"), CornerRadius = new(20), Padding = new(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(_editor, "HomeSectionEditor"); AutomationProperties.SetName(_editor, _session == null ? "Add row" : "Edit row");
        var layout = new Grid { RowSpacing = 16, TabFocusNavigation = KeyboardNavigationMode.Cycle };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Spacing = 8 }; header.Children.Add(_editorTitle); header.Children.Add(_editorNotice); layout.Children.Add(header);
        _editorFields = new() { Spacing = 16 };
        var scroller = new ScrollViewer { Content = _editorFields, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroller, 1); layout.Children.Add(scroller);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var cancel = Action("Cancel", "HomeSectionCancel"); cancel.Click += async (_, _) => { if (await CanLeaveAsync()) CloseEditor(); };
        _editorSave.Click -= EditorSaveClick; _editorSave.Click += EditorSaveClick;
        _editorBack.Click -= EditorBackClick; _editorBack.Click += EditorBackClick;
        _editorDelete.Click -= EditorDeleteClick; _editorDelete.Click += EditorDeleteClick;
        footer.Children.Add(_editorBack); footer.Children.Add(_editorDelete); footer.Children.Add(cancel); footer.Children.Add(_editorSave);
        Grid.SetRow(footer, 2); layout.Children.Add(footer); _editor.Child = layout; _editorOverlay.Children.Add(_editor); _root.Children.Add(_editorOverlay);
        _body.IsEnabled = false; Fit(); Render();
    }
    private async void EditorBackClick(object sender, RoutedEventArgs args) { if (CanReturn) await ReturnToCollectionAsync(); else if (_editorBack.Content?.ToString() == "Back to row") RenderForm(); else RenderPicker(); }
    private async void EditorDeleteClick(object sender, RoutedEventArgs args) { if (_session != null) await DeleteAsync([_session.Row.Id]); }
    private async void EditorSaveClick(object sender, RoutedEventArgs args)
    {
        if (_draft == null || _controller?.CanWrite != true || _editorInvalid || _editorOwner?.IsCurrent != true) return;
        if (string.IsNullOrWhiteSpace(_draft.Title))
        {
            _draft.Title = _draft.SectionType == "collection" && _choiceLabels.TryGetValue(Value(_draft.Config, "library_collection_id"), out var collectionName)
                ? collectionName : Kinds.GetValueOrDefault(_draft.SectionType).Name ?? "Row";
        }
        try { _draft.Validate(); }
        catch (ArgumentException ex) { _editorNotice.Text = ex.Message; return; }
        var revision = _dialogRevision; var controller = _controller;
        if (await controller.SaveAsync(_session, _draft, _extraCopies.ToArray()) && EditorCurrent(revision))
        { var back = CanReturn ? _returnPath : null; CloseEditor(); if (back != null) AdminShellPage.TryOpenHref(back); }
        else if (EditorCurrent(revision))
        {
            _editorNotice.Text = controller.State.Error ?? "Couldn't save this row.";
            if (_session != null && !controller.State.OutcomeUnknown)
            {
                var reload = Action(T("web.home.addRow.reloadRow"), "HomeSectionReloadRow");
                reload.Click += async (_, _) =>
                {
                    if (!EditorCurrent(revision) || !await ConfirmAsync("Reload row?", "Replace your draft with the current server row and review it before saving.", "Reload row", _editorOwner!)) return;
                    await controller.ReloadAsync();
                    if (!EditorCurrent(revision) || !controller.CanWrite) return;
                    try
                    {
                        var fresh = await controller.OpenEditAsync(_session!.Row.Id);
                        if (!EditorCurrent(revision) || fresh == null) return;
                        _session = fresh; _draft = AdminHomeSectionDraft.From(fresh.Row); _editorDirty = false; RenderForm();
                    }
                    catch (Exception) { if (EditorCurrent(revision)) _editorNotice.Text = "Couldn't reload this row. Try again."; }
                };
                _editorFields.Children.Insert(0, reload);
            }
            else
            {
                var review = Action("Reload rows and review", "HomeSectionReviewUnknown");
                review.Click += async (_, _) =>
                {
                    if (!EditorCurrent(revision) || !await CanLeaveAsync()) return;
                    CloseEditor(); await controller.ReloadAsync();
                    if (_active && ReferenceEquals(controller, _controller)) { await LoadPagesAsync(); await LoadDefaultsAsync(); }
                };
                _editorFields.Children.Insert(0, review);
            }
        }
    }
    private void CloseEditor()
    {
        _dialogRevision++; _previewRevision++; _pickerRevision++;
        _editorReads?.Cancel(); _editorReads?.Dispose(); _editorReads = null;
        if (_editorOverlay != null) _root.Children.Remove(_editorOverlay);
        // These controls are reused in the next editor; detach them from the old visual tree.
        if (_editorSave.Parent is Panel footer) footer.Children.Clear();
        if (_editorTitle.Parent is Panel header) header.Children.Clear();
        _editorFields.Children.Clear(); _editor = null; _editorOverlay = null; _draft = null; _session = null; _editorOwner = null;
        _editorDirty = _editorInvalid = false; _body.IsEnabled = true; _choiceLabels.Clear(); _ruleCapabilities = null; _seasonalCatalog = null;
        _linkedCollection = false;
        if (_active) { _add.IsEnabled = _controller?.CanWrite == true && _controller.State.Catalog != null; _more.IsEnabled = _controller?.CanWrite == true; _pages.IsEnabled = _controller?.State is { Busy: false, Owner.IsCurrent: true }; }
        if (_active && _add.IsLoaded && _add.IsEnabled) _add.Focus(FocusState.Programmatic);
    }
    private void RenderPicker()
    {
        if (_editor == null) return;
        _pickerRevision++; _previewRevision++; _editorFields.Children.Clear();
        _editorTitle.Text = _session == null ? "Add row to " + PageLabel : T("web.home.addRow.changeTitle"); _editorNotice.Text = T("web.home.addRow.pickDescription");
        _editorSave.Visibility = Visibility.Collapsed; _editorDelete.Visibility = Visibility.Collapsed; _editorBack.Visibility = _draft == null ? Visibility.Collapsed : Visibility.Visible;
        _editorBack.Content = "Back to row";
        var catalog = _controller?.State.Catalog;
        if (catalog == null)
        {
            _editorFields.Children.Add(U.Text(T("web.home.addRow.catalogFailed")));
            var retry = Action("Retry", "HomeSectionCatalogRetry"); retry.Click += async (_, _) => { await _controller!.ReloadAsync(); if (_editor != null) RenderPicker(); }; _editorFields.Children.Add(retry); return;
        }
        var search = Input("Search rows", "HomeSectionKindSearch", "", _ => { });
        search.Loaded += (_, _) => search.Focus(FocusState.Programmatic);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var cards = new StackPanel { Spacing = 10 }; string? chosen = null;
        void Refresh()
        {
            cards.Children.Clear(); var query = search.Text.Trim();
            var recipes = catalog.Categories.SelectMany(group => group.Recipes).Where(recipe => !NotOffered(recipe.Type) && (recipe.Presets.Count > 0 || recipe.Type == "custom_filter"));
            foreach (var group in GroupOrder)
            {
                if (chosen != null && group != chosen) continue;
                var entries = recipes.Where(recipe => (Kinds.TryGetValue(recipe.Type, out var known) ? known.Group : "moods") == group &&
                    (KindLabel(recipe.Type).Contains(query, StringComparison.CurrentCultureIgnoreCase) || recipe.Presets.Any(preset => preset.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)))).ToArray();
                if (entries.Length == 0) continue;
                cards.Children.Add(U.Text(T("web.home.rows.groups." + group), 16));
                foreach (var recipe in entries)
                {
                    var card = Action(KindLabel(recipe.Type), "HomeSectionKind_" + recipe.Type);
                    card.HorizontalAlignment = HorizontalAlignment.Stretch; card.HorizontalContentAlignment = HorizontalAlignment.Left;
                    card.Click += (_, _) => PickRecipe(recipe, recipe.Presets.FirstOrDefault()); cards.Children.Add(card);
                    if (recipe.Presets.FirstOrDefault() is { } preset) cards.Children.Add(U.Text(preset.DescriptionShort, 12, true));
                }
            }
            if (cards.Children.Count == 0) cards.Children.Add(U.Text("No rows match your search.", 13, true));
        }
        var all = Action("All", "HomeSectionCategoryAll"); all.Click += (_, _) => { chosen = null; Refresh(); }; tabs.Children.Add(all);
        foreach (var group in GroupOrder)
        {
            var tab = Action(T("web.home.rows.groups." + group), "HomeSectionCategory_" + group);
            tab.Click += (_, _) => { chosen = group; Refresh(); }; tabs.Children.Add(tab);
        }
        search.TextChanged += (_, _) => Refresh();
        _editorFields.Children.Add(search); _editorFields.Children.Add(new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _editorFields.Children.Add(cards); Refresh(); search.Focus(FocusState.Programmatic);
    }
    private void PickRecipe(AdminHomeRecipe recipe, AdminHomePreset? preset)
    {
        var previous = _draft;
        _draft = new()
        {
            SectionType = recipe.Type, Title = previous?.Title ?? Kinds.GetValueOrDefault(recipe.Type).Name ?? preset?.DisplayName ?? "Row",
            ItemLimit = previous?.ItemLimit ?? 20, Featured = previous?.Featured == true && recipe.Type != "collection_posters", WebOnly = previous?.WebOnly == true,
            Enabled = previous?.Enabled ?? true, Config = preset?.DefaultParams.DeepClone().AsObject() ?? new()
        };
        if (previous == null && preset != null && recipe.Type is not ("collection" or "collection_posters" or "custom_filter")) _draft.Title = preset.DisplayName;
        if (preset != null && recipe.Type == "seasonal_themed") _draft.ApplyVariant(preset);
        _editorDirty = true; RenderForm();
    }
    private void RenderForm()
    {
        var draft = _draft; if (draft == null || _editor == null) return;
        _pickerRevision++; _previewRevision++; _editorFields.Children.Clear(); _editorInvalid = false;
        _editorTitle.Text = _session == null ? KindLabel(draft.SectionType) : T("web.home.addRow.editRow");
        _editorNotice.Text = _session == null ? "Step 2 of 2 · Add to " + PageLabel : T("web.home.addRow.editDescription", new { page = PageLabel });
        _editorSave.Visibility = Visibility.Visible; _editorSave.Content = _session == null ? T("web.home.addRow.addRow") : T("web.home.addRow.save");
        _editorSave.IsEnabled = _controller?.CanWrite == true; _editorDelete.Visibility = _session == null ? Visibility.Collapsed : Visibility.Visible;
        var kindLocked = draft.SectionType == "continue_watching" && Value(draft.Config, "continue_type") == "reading";
        _editorBack.Visibility = _session != null && kindLocked ? Visibility.Collapsed : Visibility.Visible; _editorBack.Content = _session == null ? T("web.home.addRow.allRows") : "Change";
        if (CanReturn) _editorBack.Content = "Back to collection";
        Field(_editorFields, "Shows", U.Text(KindLabel(draft.SectionType), 14));
        var recipe = _controller?.State.Catalog?.Categories.SelectMany(group => group.Recipes).FirstOrDefault(entry => entry.Type == draft.SectionType);
        if (recipe?.Presets.Count > 0 && !NotOffered(draft.SectionType) && !kindLocked && AdminHomeSectionDraft.VariantKeys(draft.SectionType).Count > 0)
        {
            var variants = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var preset in recipe.Presets) variants.Items.Add(new ComboBoxItem { Content = preset.DisplayName, Tag = preset });
            variants.SelectedIndex = recipe.Presets.FindIndex(preset => preset.DefaultParams.All(pair => JsonNode.DeepEquals(draft.Config[pair.Key], pair.Value)));
            AutomationProperties.SetAutomationId(variants, "HomeSectionVariant");
            variants.SelectionChanged += (_, _) =>
            {
                if (variants.SelectedItem is not ComboBoxItem { Tag: AdminHomePreset preset }) return;
                if (recipe.Presets.Any(entry => entry.DisplayName == draft.Title)) draft.Title = preset.DisplayName;
                draft.ApplyVariant(preset); _editorDirty = true; RenderForm();
            };
            Field(_editorFields, "Variant", variants);
        }
        var name = Input("Row name", "HomeSectionTitle", draft.Title, value => { draft.Title = value; Dirty(); }, 500);
        name.Loaded += (_, _) => name.Focus(FocusState.Programmatic);
        Field(_editorFields, T("web.home.addRow.form.rowName"), name);
        if (draft.SectionType == "collection") CollectionSelector(_editorFields, multiple: false);
        else if (draft.SectionType == "collection_posters") CollectionSelector(_editorFields, multiple: true);
        else if (draft.SectionType is "custom_filter" or "genre") RulesEditor(_editorFields);
        else if (draft.SectionType == "admin_curated_list") CuratedEditor(_editorFields);
        else if (draft.SectionType == "seasonal_themed") SeasonalEditor(_editorFields);
        if (_scope.Kind == "home" && draft.SectionType is "recently_added" or "recently_released" or "watchlist" or "favorites") LibrarySelector(_editorFields, "filter_library_ids");
        if (draft.SectionType is "trending_discover" or "watchlist" or "favorites")
            ConfigSelect(_editorFields, "Show", "filter_type", [("", "Everything"), ("movie", "Movies"), ("series", "Shows"), ("audiobook", "Audiobooks")]);
        var more = new StackPanel { Spacing = 14 };
        if (draft.SectionType is "watchlist" or "favorites")
        {
            var orders = new[] { ("", "List order"), ("added_at:desc", "Newest added first"), ("added_at:asc", "Oldest added first"), ("title:asc", "Title A to Z"), ("title:desc", "Title Z to A"), ("release_date:desc", "Newest released first"), ("release_date:asc", "Oldest released first"), ("rating_imdb:desc", "Highest IMDb rating first") };
            var current = Value(draft.Config, "sort"); if (current != "") current += ":" + (Value(draft.Config, "order") is { Length: > 0 } order ? order : current == "title" ? "asc" : "desc");
            Field(more, "Order", Select("HomeSectionListOrder", orders, current, value =>
            { var parts = value.Split(':'); SetConfig("sort", parts[0] == "" ? null : JsonValue.Create(parts[0])); SetConfig("order", parts.Length > 1 ? JsonValue.Create(parts[1]) : null); }));
        }
        if (draft.SectionType == "returning_shows") ConfigNumber(more, "Look back (days)", "lookback_days", 30);
        if (draft.SectionType == "short_watches") ConfigNumber(more, "Maximum minutes", "max_minutes", 95);
        if (draft.SectionType == "anniversaries") ConfigNumber(more, "Milestone years", "milestone_years", 5);
        if (draft.SectionType == "taste_match") ConfigText(more, "Genre", "genre");
        if (draft.SectionType == "editorial_spotlight")
        {
            var rotating = draft.Config["auto_rotate"] is JsonValue rotate && rotate.TryGetValue<bool>(out var savedRotate) ? savedRotate : string.IsNullOrEmpty(Value(draft.Config, "subject"));
            Field(more, "Rotate automatically", Toggle("HomeSectionAutoRotate", rotating, value => { SetConfig("auto_rotate", JsonValue.Create(value)); RenderForm(); }));
            if (rotating) ConfigSelect(more, "How often", "rotation_cadence", [("daily", "Every day"), ("weekly", "Every week"), ("monthly", "Every month")]);
            else ConfigText(more, "Subject", "subject");
        }
        var limit = new NumberBox { Minimum = 1, Maximum = Math.Max(100, draft.ItemLimit), SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Value = draft.ItemLimit };
        AutomationProperties.SetAutomationId(limit, "HomeSectionItemLimit");
        limit.ValueChanged += (_, args) => { if (double.IsFinite(args.NewValue) && args.NewValue == Math.Truncate(args.NewValue)) { draft.ItemLimit = (int)args.NewValue; Dirty(); } };
        Field(more, T("web.home.addRow.more.itemLimit"), limit);
        if (draft.SectionType != "collection_posters")
        {
            Field(more, T("web.home.addRow.more.heroBanner"), Toggle("HomeSectionHero", draft.Featured, value => { draft.Featured = value; if (!value) draft.WebOnly = false; Dirty(); RenderForm(); }));
            if (draft.Featured && _controller?.State.Capabilities?.WebOnly == true)
                Field(more, T("web.home.addRow.more.showInApps"), Toggle("HomeSectionShowInApps", !draft.WebOnly, value => { draft.WebOnly = !value; Dirty(); }));
        }
        var expander = new Expander { Header = T("web.home.addRow.more.title"), Content = more, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        _editorFields.Children.Add(expander);
        if (_session == null && _scope.Kind == "library")
        {
            var targets = draft.Featured ? Array.Empty<AdminHomeSectionChoice>() : CopyTargets(draft).ToArray();
            _extraCopies.IntersectWith(targets.Select(library => library.Id));
            if (targets.Length > 0)
            {
                var copies = new ListView { ItemsSource = targets, DisplayMemberPath = "Label", SelectionMode = ListViewSelectionMode.Multiple, IsMultiSelectCheckBoxEnabled = true, MaxHeight = 180 };
                AutomationProperties.SetAutomationId(copies, "HomeSectionExtraCopies");
                foreach (var library in targets.Where(library => _extraCopies.Contains(library.Id))) copies.SelectedItems.Add(library);
                copies.SelectionChanged += (_, args) =>
                {
                    foreach (var library in args.RemovedItems.OfType<AdminHomeSectionChoice>()) _extraCopies.Remove(library.Id);
                    foreach (var library in args.AddedItems.OfType<AdminHomeSectionChoice>()) if (_extraCopies.Count < 99) _extraCopies.Add(library.Id); else copies.SelectedItems.Remove(library);
                    Dirty();
                };
                Field(_editorFields, "Also add to these library pages (up to 99)", copies);
            }
        }
        var preview = new StackPanel { Spacing = 10 }; var refresh = Action("Preview", "HomeSectionPreview");
        refresh.IsEnabled = _controller?.State.Capabilities?.Preview == true;
        refresh.Click += async (_, _) => await PreviewDraftAsync(preview); _editorFields.Children.Add(refresh); _editorFields.Children.Add(preview);
        if (!refresh.IsEnabled) preview.Children.Add(U.Text(T("web.home.addRow.previewUnavailable"), 13, true));
        else preview.Children.Add(U.Text("Preview this draft before saving. Each profile may see different titles.", 13, true));
        RefreshSave();
    }
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
    private void RefreshSave() => _editorSave.IsEnabled = _controller?.CanWrite == true && !_editorInvalid && DraftCanSave();
    private void Dirty() { _editorDirty = true; _previewRevision++; RefreshSave(); }
    private void SetConfig(string key, JsonNode? value) { if (_draft == null) return; if (value == null) _draft.Config.Remove(key); else _draft.Config[key] = value; Dirty(); }
    private static string Value(JsonObject config, string key) => config[key]?.ToString() ?? "";
    private void ConfigText(StackPanel panel, string label, string key) => Field(panel, label, Input(label, "HomeSection_" + key, Value(_draft!.Config, key), value => SetConfig(key, value == "" ? null : JsonValue.Create(value))));
    private void ConfigNumber(StackPanel panel, string label, string key, int placeholder)
    {
        var box = Input(label, "HomeSection_" + key, Value(_draft!.Config, key), value =>
        { if (value == "") { SetConfig(key, null); _editorInvalid = false; } else if (int.TryParse(value, out var number) && number > 0) { SetConfig(key, JsonValue.Create(number)); _editorInvalid = false; } else { _editorInvalid = true; _editorNotice.Text = "Enter a positive whole number."; } RefreshSave(); });
        box.PlaceholderText = placeholder.ToString(); Field(panel, label, box);
    }
    private void ConfigSelect(StackPanel panel, string label, string key, (string Value, string Label)[] options) =>
        Field(panel, label, Select("HomeSection_" + key, options, Value(_draft!.Config, key), value => SetConfig(key, value == "" ? null : JsonValue.Create(value))));
    private static TextBox Input(string label, string id, string value, Action<string> changed, int max = 500)
    {
        var input = U.Input(label, id); input.MaxLength = max; input.Text = value;
        input.TextChanged += (_, _) => changed(input.Text); return input;
    }
    private static ToggleSwitch Toggle(string id, bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsOn = value, OffContent = "", OnContent = "" }; AutomationProperties.SetAutomationId(toggle, id);
        toggle.Toggled += (_, _) => changed(toggle.IsOn); return toggle;
    }
    private static ComboBox Select(string id, IEnumerable<(string Value, string Label)> options, string selected, Action<string> changed)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(combo, id);
        foreach (var (value, label) in options) { var item = new ComboBoxItem { Tag = value, Content = label }; combo.Items.Add(item); if (value == selected) combo.SelectedItem = item; }
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string value }) changed(value); }; return combo;
    }
    private static void Field(StackPanel panel, string label, FrameworkElement input)
    { var stack = new StackPanel { Spacing = 6 }; stack.Children.Add(U.Text(label, 13)); AutomationProperties.SetName(input, label); stack.Children.Add(input); panel.Children.Add(stack); }
    private async Task PreviewDraftAsync(StackPanel panel)
    {
        if (_draft == null || _editorOwner == null || _editorReads == null) return;
        var revision = _dialogRevision; var previewRevision = ++_previewRevision; var owner = _editorOwner;
        panel.Children.Clear(); panel.Children.Add(U.Text("Loading preview…", 13, true));
        try
        {
            var result = await _api.PreviewAsync(owner, _draft, _editorReads.Token, _scope);
            if (!EditorCurrent(revision) || previewRevision != _previewRevision) return;
            panel.Children.Clear(); panel.Children.Add(PreviewPanel(result));
        }
        catch (Exception ex) { if (EditorCurrent(revision) && previewRevision == _previewRevision) { panel.Children.Clear(); panel.Children.Add(U.Text(ex is ArgumentException ? ex.Message : "Couldn't load the preview. Try again.", 13, true)); } }
    }
    private readonly SemaphoreSlim _posterSlots = new(2, 2);
    private StackPanel PreviewPanel(AdminHomeSectionPreview result)
    {
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(U.Text($"{result.TotalCount} titles", 13, true));
        var cards = result.Collections.Count > 0 ? result.Collections : result.Items;
        if (cards.Count == 0) panel.Children.Add(U.Text("No titles match this row yet.", 13, true));
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
