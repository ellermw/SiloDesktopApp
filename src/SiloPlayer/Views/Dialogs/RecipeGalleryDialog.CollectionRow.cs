using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Converters;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Views.Dialogs;

/// <summary>What the Add row collection pickers read: every collection this profile can offer, and whether any of the reads failed.</summary>
public sealed record HomeRowCollectionLoad(IReadOnlyList<HomeRowCollectionOption> Options, bool Failed);

/// <summary>
/// "A row of collections" in the Add row and Edit row form (official CollectionsRowEditor, collectionChooser, collectionSearch; #4410, #4471): the
/// collections the row shows, in order, then a searchable list to add more from, filtered by type and, when the collections come from more than one
/// library, by library. A chosen collection the list no longer offers stays, marked "No longer available", until it is removed.
/// </summary>
public static partial class RecipeGalleryDialog
{
    /// <summary>The one bounded read the collection pickers share. A test replaces it with synthetic data.</summary>
    public static Func<CancellationToken, Task<HomeRowCollectionLoad>> CollectionOptionsLoader { get; set; } = LoadCollectionOptionsAsync;

    private const int CollectionLibraryReads = 3;

    /// <summary>How long the reads may take together. What has not answered by then counts as a failed read.</summary>
    private static readonly TimeSpan CollectionReadTimeout = TimeSpan.FromSeconds(30);

    private static async Task<HomeRowCollectionLoad> LoadCollectionOptionsAsync(CancellationToken ct)
    {
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var context = client.CaptureContext();
        var catalogApi = App.Services.GetRequiredService<CatalogApi>();
        var collectionsApi = App.Services.GetRequiredService<CollectionsApi>();
        var failed = false;
        // Only the editor's own token ends the load. A read that timed out, or that the client cancelled, is a failed read like any other.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CollectionReadTimeout);
        var reading = timeout.Token;
        var userTask = ReadAsync(async () => (await collectionsApi.GetCollectionsAsync(reading)).Collections);
        List<Library> libraries = [];
        try { libraries = await catalogApi.GetLibrariesAsync(reading); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { failed = true; }
        // At most three library tabs are read at once; each library is read once.
        using var gate = new SemaphoreSlim(CollectionLibraryReads);
        var tabs = libraries.Select(async library =>
        {
            try
            {
                await gate.WaitAsync(reading);
                try { return (IReadOnlyList<LibraryCollection>?)(await catalogApi.GetLibraryCollectionsAsync(library.Id, reading)).Collections; }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { failed = true; return null; }
        }).ToArray();
        var perLibrary = await Task.WhenAll(tabs);
        var user = await userTask;
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("The selected profile changed.");
        return new(HomeRowCollections.BuildOptions(libraries, user, perLibrary), failed);

        async Task<List<Collection>?> ReadAsync(Func<Task<List<Collection>>> read)
        {
            try { return await read(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { failed = true; return null; }
        }
    }

    /// <summary>A row of collections cannot be saved until it lists at least one collection (the WebUI's canSaveCollectionRefs).</summary>
    private sealed class CollectionsRowState(Dictionary<string, object> config, Func<bool> isCurrent)
    {
        /// <summary>The row's config, which the editor writes in place.</summary>
        public Dictionary<string, object> Config => config;
        public string? Problem()
        {
            if (!isCurrent()) return HomeRowText.T("web.home.rows.draft.addCollection");
            var refs = HomeRowCollections.RefsOf(config);
            return HomeRowCollections.CanSave(refs) ? null : HomeRowText.T("web.home.rows.draft.addCollection");
        }
    }

    private static string? CollectionsEditorProblem(DependencyObject root)
    {
        if (root is FrameworkElement { Tag: CollectionsRowState state } && state.Problem() is { } error) return error;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (CollectionsEditorProblem(VisualTreeHelper.GetChild(root, i)) is { } childError) return childError;
        return null;
    }

    /// <summary>One line the chosen list shows.</summary>
    private sealed record ChosenLine(string Title, HomeRowCollectionOption? Option, string Meta, bool Missing);

    private static Task AddCollectionsRowFieldsAsync(Dictionary<string, object> config, StackPanel host)
    {
        Brush Resource(string key) => ResourceBrush(key);
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var context = client.CaptureContext();
        var viewing = App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>().SelectedProfileId;
        var state = new CollectionsRowState(config, () => client.IsCurrentContext(context));
        var editor = new StackPanel { Spacing = 16, Tag = state, Name = "HomeRowCollectionsEditor" };
        host.Children.Add(editor);

        IReadOnlyList<HomeRowCollectionOption> options = [];
        var loading = true; var failed = false;
        var query = ""; var kind = HomeRowCollections.AllKinds; int? pickedLibrary = null;
        var generation = 0; var loadVersion = 0;
        CancellationTokenSource? cancellation = null;

        // ── Chosen collections ──
        var chosenLabel = new TextBlock { FontSize = 14, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        var chosenCount = new TextBlock { FontSize = 13, Foreground = Resource("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Right };
        var chosenHeader = new Grid { ColumnSpacing = 12 };
        chosenHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); chosenHeader.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        chosenHeader.Children.Add(chosenLabel); Grid.SetColumn(chosenCount, 1); chosenHeader.Children.Add(chosenCount);
        var chosenEmpty = new Border { BorderBrush = Resource("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12),
            Child = new TextBlock { Name = "HomeRowCollectionsEmpty", FontSize = 13, Foreground = Resource("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap } };
        var chosenList = new StackPanel { Name = "HomeRowCollectionsChosen" };
        var chosenFrame = new Border { BorderBrush = Resource("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = chosenList };
        var limit = new TextBlock { FontSize = 13, Foreground = Resource("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap };
        var chosenSection = new StackPanel { Spacing = 8, Children = { chosenHeader, chosenEmpty, chosenFrame, limit } };
        editor.Children.Add(chosenSection);

        // ── Chooser ──
        var addLabel = new TextBlock { FontSize = 14, FontWeight = FontWeights.Medium };
        var search = new TextBox { Name = "HomeRowCollectionsSearch", MinWidth = 140, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        var kindBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        var kindFrame = new Border { Background = Resource("SurfaceBrush"), CornerRadius = new CornerRadius(10), Child = kindBar, VerticalAlignment = VerticalAlignment.Center };
        var libraryButton = new DropDownButton { Name = "HomeRowCollectionsLibrary", MinHeight = 32, Padding = new Thickness(10, 0, 10, 0), FontSize = 13, MaxWidth = 192, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        var bar = new Grid { Padding = new Thickness(8), ColumnSpacing = 8, RowSpacing = 8 };
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.Children.Add(search); Grid.SetColumn(kindFrame, 1); bar.Children.Add(kindFrame); Grid.SetColumn(libraryButton, 2); bar.Children.Add(libraryButton);
        var barRule = new Border { Height = 1, Background = Resource("BorderBrush") };
        var results = new StackPanel { Name = "HomeRowCollectionsResults", Spacing = 2, Padding = new Thickness(6) };
        var scroller = new ScrollViewer { Content = results, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var status = new TextBlock { Name = "HomeRowCollectionsStatus", FontSize = 14, Foreground = Resource("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 24, 16, 24) };
        var chooserBody = new StackPanel { Children = { bar, barRule, scroller, status } };
        var chooserFrame = new Border { BorderBrush = Resource("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Child = chooserBody };
        editor.Children.Add(new StackPanel { Spacing = 8, Children = { addLabel, chooserFrame } });
        // Narrow dialogs stack the bar; a wide one keeps the search, type filter and library filter on one line.
        bar.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 560;
            bar.RowDefinitions.Clear();
            if (narrow) { bar.RowDefinitions.Add(new() { Height = GridLength.Auto }); bar.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            Grid.SetColumnSpan(search, narrow ? 3 : 1); Grid.SetRow(kindFrame, narrow ? 1 : 0); Grid.SetColumn(kindFrame, narrow ? 0 : 1);
            Grid.SetRow(libraryButton, narrow ? 1 : 0); Grid.SetColumn(libraryButton, narrow ? 1 : 2);
        };

        string OwnerName(string profileId) => HomeRowOwnerNames.NameOf(profileId);
        string? OwnerLine(HomeRowCollectionOption option) => HomeRowCollections.OwnerLine(option, viewing, OwnerName);
        Dictionary<string, HomeRowCollectionOption> ByKey() => options.GroupBy(option => option.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        List<HomeRowCollectionRef> Refs() => HomeRowCollections.RefsOf(config);
        void SetRefs(IEnumerable<HomeRowCollectionRef> refs) { var next = HomeRowCollections.WithRefs(config, refs); config.Clear(); foreach (var pair in next) config[pair.Key] = pair.Value; Render(); }

        Border Poster(string? url, bool missing)
        {
            var frame = new Border { Width = 36, Height = 54, CornerRadius = new CornerRadius(6), Background = Resource("SurfaceBrush"), BorderBrush = Resource("BorderBrush"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            if (missing)
            {
                frame.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 245, 158, 11));
                frame.Child = new FontIcon { Glyph = "", FontSize = 16, Foreground = Resource("WarningBrush") };
            }
            else if (!string.IsNullOrEmpty(url))
                frame.Child = new Image { Stretch = Stretch.UniformToFill, Source = (ImageSource)new UrlToImageSourceConverter().Convert(url, typeof(ImageSource), 36d, "") };
            return frame;
        }
        Button IconButton(string glyph, string name, bool enabled, Action click)
        {
            var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, Style = ResourceStyle("GhostButtonStyle"), Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), IsEnabled = enabled };
            AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
            button.Click += (_, _) => click();
            return button;
        }

        // The type filter is a set of toggles of which exactly one is on: Checked picks the type, and pressing the one that is on keeps it on.
        var kindToggles = new List<(string Name, ToggleButton Toggle)>(); var syncingKinds = false;
        foreach (var (name, key, english) in new[] { (HomeRowCollections.AllKinds, "web.home.addRow.collection.filterAll", "All"), ("manual", "web.home.addRow.collection.filterManual", "Manual"),
                     ("smart", "web.home.addRow.collection.filterSmart", "Smart"), ("synced", "web.home.addRow.collection.filterSynced", "Synced list") })
        {
            var toggle = new ToggleButton { Name = "HomeRowCollectionsKind_" + name, MinHeight = 28, Height = 28, Padding = new Thickness(10, 0, 10, 0), FontSize = 13, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0) };
            // The chosen type reads as a raised pill on the filter's well, not as an accent button.
            foreach (var visual in new[] { "", "PointerOver", "Pressed" })
            {
                toggle.Resources["ToggleButtonBackgroundChecked" + visual] = Resource("AppBackgroundBrush");
                toggle.Resources["ToggleButtonForegroundChecked" + visual] = Resource("PrimaryTextBrush");
                toggle.Resources["ToggleButtonBorderBrushChecked" + visual] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            var chosenKind = name; var labelKey = key; var labelEnglish = english;
            toggle.Tag = (labelKey, labelEnglish);
            toggle.Checked += (_, _) => { if (syncingKinds) return; kind = chosenKind; RenderChooser(); };
            toggle.Unchecked += (_, _) => { if (!syncingKinds && kind == chosenKind) toggle.IsChecked = true; };
            kindToggles.Add((name, toggle)); kindBar.Children.Add(toggle);
        }
        void SyncKinds()
        {
            syncingKinds = true;
            try
            {
                foreach (var (name, toggle) in kindToggles)
                {
                    var (labelKey, labelEnglish) = ((string, string))toggle.Tag;
                    var label = CollectionText.Get(labelKey, labelEnglish);
                    toggle.Content = label; AutomationProperties.SetName(toggle, label);
                    toggle.IsChecked = kind == name;
                    toggle.Background = kind == name ? Resource("AppBackgroundBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            }
            finally { syncingKinds = false; }
        }

        ChosenLine Describe(HomeRowCollectionRef entry, IReadOnlyDictionary<string, HomeRowCollectionOption> known)
        {
            if (known.TryGetValue(entry.Key, out var option))
                return new(option.Title, option, HomeRowCollections.MetaWithOwner(option, OwnerLine(option)), false);
            var settled = !loading && !failed;
            return new(settled ? HomeRowText.T("web.home.addRow.collections.unavailable") : "", null, "", settled);
        }

        void RenderChosen()
        {
            var refs = Refs(); var known = ByKey();
            chosenLabel.Text = HomeRowText.T("web.home.addRow.collections.chosenLabel");
            chosenCount.Text = refs.Count > 0 ? HomeRowCollections.ChosenCountText(refs.Count) : "";
            chosenCount.Visibility = refs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            chosenEmpty.Visibility = refs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)chosenEmpty.Child).Text = HomeRowText.T("web.home.addRow.collections.empty");
            chosenFrame.Visibility = refs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            limit.Text = HomeRowCollections.IsFull(refs.Count) ? HomeRowCollections.LimitText() : "";
            limit.Visibility = limit.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            chosenList.Children.Clear();
            for (var index = 0; index < refs.Count; index++)
            {
                var at = index; var entry = refs[index]; var line = Describe(entry, known);
                var shownTitle = line.Title.Length > 0 ? line.Title : HomeRowText.T("web.home.addRow.collections.unavailable");
                var row = new Grid { ColumnSpacing = 6, Padding = new Thickness(6, 6, 6, 6), Background = Resource("AppBackgroundBrush"), AllowDrop = true };
                foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
                    row.ColumnDefinitions.Add(new() { Width = width });
                var grip = new Button { Content = WebUiIcon.Create("grip-vertical", 16, Resource("SecondaryTextBrush")), Style = ResourceStyle("GhostButtonStyle"), Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CanDrag = true };
                AutomationProperties.SetName(grip, HomeRowText.T("web.home.addRow.collections.dragHandle", new { collection = shownTitle }));
                AutomationProperties.SetHelpText(grip, HomeRowText.T("web.home.addRow.collections.drag.instructions"));
                grip.DragStarting += (_, args) => { args.Data.SetText("collection-row:" + entry.Key); args.Data.RequestedOperation = DataPackageOperation.Move; };
                row.Children.Add(grip);
                var poster = Poster(line.Option?.PosterUrl, line.Missing); Grid.SetColumn(poster, 1); row.Children.Add(poster);
                var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
                if (line.Title.Length > 0)
                    text.Children.Add(new TextBlock { Text = line.Title, FontSize = 14, FontWeight = line.Missing ? FontWeights.Medium : FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = line.Missing ? Resource("WarningBrush") : Resource("PrimaryTextBrush") });
                else
                    text.Children.Add(new Border { Height = 16, Width = 128, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(6), Background = Resource("SurfaceBrush") });
                if (line.Meta.Length > 0)
                    text.Children.Add(new TextBlock { Text = line.Meta, FontSize = 13, Foreground = Resource("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(text, 2); row.Children.Add(text);
                var up = IconButton("", HomeRowText.T("web.home.addRow.collections.moveUp", new { collection = shownTitle }), at > 0, () => SetRefs(HomeRowCollections.Move(refs, at, at - 1)));
                var down = IconButton("", HomeRowText.T("web.home.addRow.collections.moveDown", new { collection = shownTitle }), at < refs.Count - 1, () => SetRefs(HomeRowCollections.Move(refs, at, at + 1)));
                var remove = IconButton("", HomeRowText.T("web.home.addRow.collections.remove", new { collection = shownTitle }), true, () => SetRefs(refs.Where((_, other) => other != at)));
                Grid.SetColumn(up, 3); Grid.SetColumn(down, 4); Grid.SetColumn(remove, 5);
                row.Children.Add(up); row.Children.Add(down); row.Children.Add(remove);
                row.DragOver += (_, args) =>
                {
                    if (!args.DataView.Contains(StandardDataFormats.Text)) return;
                    args.AcceptedOperation = DataPackageOperation.Move;
                };
                row.Drop += async (_, args) =>
                {
                    if (!args.DataView.Contains(StandardDataFormats.Text)) return;
                    var deferral = args.GetDeferral();
                    try
                    {
                        var dropped = await args.DataView.GetTextAsync();
                        if (!dropped.StartsWith("collection-row:", StringComparison.Ordinal)) return;
                        var current = Refs();
                        var from = current.FindIndex(other => other.Key == dropped["collection-row:".Length..]);
                        var to = current.FindIndex(other => other.Key == entry.Key);
                        if (from >= 0 && to >= 0 && from != to) SetRefs(HomeRowCollections.Move(current, from, to));
                    }
                    finally { deferral.Complete(); }
                };
                chosenList.Children.Add(row);
                if (index < refs.Count - 1) chosenList.Children.Add(new Border { Height = 1, Background = Resource("BorderBrush") });
            }
        }

        void RenderChooser()
        {
            var refs = Refs(); var chosen = refs.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            var libraries = HomeRowCollections.LibrariesOf(options);
            var library = HomeRowCollections.EffectiveLibrary(libraries, pickedLibrary);
            pickedLibrary = library;
            var shown = HomeRowCollections.Filter(options, query, kind, library);
            var full = HomeRowCollections.IsFull(refs.Count);
            addLabel.Text = HomeRowText.T("web.home.addRow.collections.addLabel");
            AutomationProperties.SetName(search, HomeRowText.T("web.home.addRow.collection.search"));
            search.PlaceholderText = HomeRowText.T("web.home.addRow.collection.searchPlaceholder", AdminHomeSectionsTextCount(options.Count));
            AutomationProperties.SetName(kindBar, HomeRowText.T("web.home.addRow.collection.type"));
            SyncKinds();
            // The library filter is offered for two or more libraries; with fewer, a pick made earlier must not keep hiding collections.
            libraryButton.Visibility = libraries.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (libraries.Count > 1)
            {
                var pickedName = library is { } id ? libraries.First(entry => entry.Id == id).Name : HomeRowText.T("web.home.addRow.params.allLibraries");
                libraryButton.Content = pickedName;
                AutomationProperties.SetName(libraryButton, HomeRowText.T("web.home.addRow.collection.library") + ": " + pickedName);
                var menu = new MenuFlyout();
                void AddEntry(string label, int? target)
                {
                    var item = new MenuFlyoutItem { Text = label, Icon = library == target ? new FontIcon { Glyph = "" } : null };
                    item.Click += (_, _) => { pickedLibrary = target; RenderChooser(); };
                    menu.Items.Add(item);
                }
                AddEntry(HomeRowText.T("web.home.addRow.params.allLibraries"), null);
                foreach (var entry in libraries) AddEntry(entry.Name, entry.Id);
                libraryButton.Flyout = menu;
            }
            results.Children.Clear();
            foreach (var option in shown)
            {
                var added = chosen.Contains(option.Key);
                var meta = HomeRowCollections.MetaWithOwner(option, OwnerLine(option));
                var item = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(12),
                    Style = ResourceStyle("GhostButtonStyle"), Background = added ? Resource("SurfaceHoverBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent), IsEnabled = !full || added };
                var grid = new Grid { ColumnSpacing = 12 };
                grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                grid.Children.Add(Poster(option.PosterUrl, false));
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = option.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                if (meta.Length > 0) text.Children.Add(new TextBlock { Text = meta, FontSize = 13, Foreground = Resource("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(text, 1); grid.Children.Add(text);
                var pillText = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                pillText.Children.Add(WebUiIcon.Create(added ? "check" : "plus", 14, added ? Resource("SuccessBrush") : Resource("SecondaryTextBrush")));
                pillText.Children.Add(new TextBlock { Text = HomeRowText.T(added ? "web.home.addRow.collections.added" : "web.home.addRow.collections.add"), FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = added ? Resource("PrimaryTextBrush") : Resource("SecondaryTextBrush") });
                var pill = new Border { Height = 28, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), BorderBrush = Resource("BorderBrush"), Child = pillText, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(pill, 2); grid.Children.Add(pill);
                item.Content = grid;
                AutomationProperties.SetName(item, option.Title);
                AutomationProperties.SetHelpText(item, meta + (added ? " · " + HomeRowText.T("web.home.addRow.collections.added") : ""));
                var captured = option;
                item.Click += (_, _) => SetRefs(HomeRowCollections.Toggle(Refs(), captured.Source, captured.Id));
                results.Children.Add(item);
            }
            scroller.Visibility = shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            status.Text = shown.Count > 0 ? "" : HomeRowCollections.EmptyText(loading, failed, options.Count, query, library != null);
            status.Visibility = shown.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        void Render() { RenderChosen(); RenderChooser(); }
        search.TextChanged += (_, _) => { query = search.Text; RenderChooser(); };
        Render();
        // A language change while the editor is open redraws its text; the search, filters and chosen list stay.
        var registration = SiloPlayer.Services.AppLocalization.Register(editor, Render);
        editor.Unloaded += (_, _) => { ++generation; cancellation?.Cancel(); registration.Dispose(); };

        async Task LoadAsync()
        {
            var mine = ++loadVersion; var token = generation;
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = new CancellationTokenSource();
            var ct = cancellation.Token;
            try
            {
                var loaded = await CollectionOptionsLoader(ct);
                if (mine != loadVersion || token != generation || ct.IsCancellationRequested) return;
                options = loaded.Options; failed = loaded.Failed; loading = false;
            }
            // Only this editor's own cancellation leaves the list as it is. A cancellation from anywhere else (a read that timed out, a
            // profile that changed) is a failed load, so the chooser stops saying it is loading.
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch { if (mine != loadVersion || token != generation) return; failed = true; loading = false; options = []; }
            Render();
            // Another profile's shared collection is named by its owner: the profile list is read once, and only when one is shown.
            if (options.Any(option => option.Source == "user" && !string.IsNullOrEmpty(option.CreatorProfileId) && option.CreatorProfileId != viewing))
            {
                try { await HomeRowOwnerNames.EnsureAsync(); if (mine == loadVersion && token == generation) Render(); }
                catch (OperationCanceledException) { }
                catch { /* The "by Name" lines keep the neutral name. */ }
            }
        }
        _ = LoadAsync();
        return Task.CompletedTask;
    }

    private static Dictionary<string, object?> AdminHomeSectionsTextCount(int count)
        => SiloPlayer.Core.Services.Admin.AdminHomeSectionsText.Count(count);
}
