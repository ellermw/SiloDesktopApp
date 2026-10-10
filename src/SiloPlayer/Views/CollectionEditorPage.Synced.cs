using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private bool _newSynced;
    private string _syncedSource = "mdblist";
    private readonly Border _syncedCreatePanel = new();
    private readonly StackPanel _syncedBody = new() { Spacing = 16 };
    private readonly StackPanel _syncedChoices = new() { Spacing = 0 };
    private readonly TextBox _syncedLink = new();
    private readonly TextBlock _syncedError = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly StackPanel _syncedChart = new() { Spacing = 8 };
    private readonly ComboBox _chartPreset = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _chartMedia = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _chartWindow = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private CollectionsViewModel? _syncedImports;
    private CollectionTemplate? _syncedPick;
    private int _syncedLoadGeneration;
    private bool _sourceChoiceLoading;
    private readonly TextBox _listSearch = new() { PlaceholderText = "Search MDBList and popular picks", MinHeight = 44 };
    private readonly StackPanel _listSearchPanel = new() { Spacing = 8 };
    private readonly StackPanel _syncedLibraryChoices = new() { Spacing = 4 };
    private readonly TextBox _titleLimit = new() { PlaceholderText = "No limit", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _newSchedule = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
    private CancellationTokenSource? _listSearchDebounce;
    private CollectionCapabilitiesResponse? _creationCapabilities;
    private bool _pickingTemplate;
    private readonly Dictionary<string, string> _sourceLinks = new();

    private async Task ConfigureSyncedCreationAsync()
    {
        _newSynced = true;
        ViewModel.CollectionType = "synced";
        _syncedCreatePanel.Child = _syncedBody; PanelStyle(_syncedCreatePanel, ActualWidth < 640 ? 20 : 24);
        EditorPrimaryColumn.Children.Insert(1, _syncedCreatePanel);
        _syncedBody.Spacing = 20;
        _syncedBody.Children.Add(new StackPanel { Spacing = 4, Children = {
            new TextBlock { Text = "The list it follows", FontSize = 17, LineHeight = 25.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold },
            new TextBlock { Text = "Titles come from this list and update on its schedule.", FontSize = 13.5, LineHeight = 20.25, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = CurrentBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap }
        } });
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var context = client.CaptureContext();
        try { _creationCapabilities = await App.Services.GetRequiredService<CollectionsApi>().GetCollectionCapabilitiesAsync(); }
        catch { _syncedError.Text = "Couldn't check whether Synced lists are on. Return to collections and try again."; }
        if (!_editorActive || !client.IsCurrentContext(context)) return;
        var offeredSources = new[] { "mdblist", "tmdb", "tmdb_list" }.Where(source => _creationCapabilities?.ImportSources?.Contains(source) == true).ToArray();
        _syncedSource = offeredSources.FirstOrDefault() ?? "";
        var tabs = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 4 };
        foreach (var (source, label) in new[] { ("mdblist", "MDBList"), ("tmdb", "TMDB chart"), ("tmdb_list", "TMDB list") })
        {
            if (!offeredSources.Contains(source)) continue;
            var button = SyncedSourceTab(label, source == _syncedSource);
            button.Checked += (_, _) => { _sourceLinks[_syncedSource] = _syncedLink.Text; _syncedSource = source; _syncedPick = null; _syncedLink.Text = _sourceLinks.GetValueOrDefault(source, ""); RenderSyncedSource(); }; tabs.Children.Add(button);
        }
        _syncedBody.Children.Add(new Border { Child = tabs, Padding = new(4), CornerRadius = new(12), Background = CurrentBrush("SurfaceBrush"), HorizontalAlignment = HorizontalAlignment.Left });
        _listSearch.IsEnabled = _creationCapabilities?.MdblistSearch == true;
        AutomationProperties.SetName(_listSearch, "Search lists");
        _listSearch.TextChanged += async (_, _) => await SearchSyncedListsAsync();
        _listSearchPanel.Spacing = 12;
        _listSearch.Height = 42; _listSearch.MinHeight = 0; _listSearch.FontSize = 14; _listSearch.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); _listSearch.BorderThickness = new(0); _listSearch.Padding = new(0, 8, 0, 8);
        var searchLine = new Grid { ColumnSpacing = 8 }; searchLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); searchLine.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var searchIcon = WebUiIcon.Create("search", 16, CurrentBrush("SecondaryTextBrush")); searchIcon.VerticalAlignment = VerticalAlignment.Center; searchLine.Children.Add(searchIcon); Grid.SetColumn(_listSearch, 1); searchLine.Children.Add(_listSearch);
        _listSearchPanel.Children.Add(new Border { Child = searchLine, Height = 44, Padding = new(12, 0, 12, 0), CornerRadius = new(12), Background = CurrentBrush("AppBackgroundBrush"), BorderBrush = CurrentBrush("BorderBrush"), BorderThickness = new(1) });
        if (!_listSearch.IsEnabled) _listSearchPanel.Children.Add(new TextBlock { Text = "Search is off for this profile. Popular picks and pasted links still work.", FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = CurrentBrush("SecondaryTextBrush") });
        _listSearchPanel.Children.Add(_syncedThemes);
        _syncedChoiceFrame.Child = new ScrollViewer { Content = _syncedChoices, MaxHeight = 420, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _syncedBody.Children.Add(_listSearchPanel); _syncedBody.Children.Add(_syncedChoiceFrame);
        _syncedLink.Style = (Style)Application.Current.Resources["DarkTextBoxStyle"];
        _syncedLink.TextChanged += (_, _) =>
        {
            ViewModel.SourceUrl = _syncedLink.Text;
            var pickedLink = _syncedPick?.Mdblist?.Url ?? _syncedPick?.TmdbList?.Url;
            // WinUI can deliver TextChanged after the pick handler has ended.
            // A link filled by that pick still belongs to the same selection.
            if (_syncedSource != "tmdb" && !_pickingTemplate && !string.Equals(_syncedLink.Text, pickedLink, StringComparison.Ordinal))
            { _syncedPick = null; ApplySyncedPickFields("", "", null, ""); }
        };
        _syncedLink.MinHeight = 0; _syncedLink.Height = 44; _syncedLink.FontSize = 14;
        _syncedLinkField = Field("Or paste any MDBList link", _syncedLink); _syncedBody.Children.Add(_syncedLinkField);
        foreach (var (key, label) in new[] { ("trending", "Trending"), ("popular", "Popular"), ("top_rated", "Top rated"), ("now_playing", "Now playing"), ("upcoming", "Upcoming"), ("airing_today", "Airing today"), ("on_the_air", "On the air") })
            _chartPreset.Items.Add(new ComboBoxItem { Content = label, Tag = key });
        _chartWindow.Items.Add(new ComboBoxItem { Content = "Today", Tag = "day" }); _chartWindow.Items.Add(new ComboBoxItem { Content = "This week", Tag = "week" }); _chartWindow.SelectedIndex = 0;
        _chartPreset.SelectionChanged += (_, _) => NormalizeChart();
        _chartMedia.SelectionChanged += (_, _) => { if (_normalizingChart) return; RefreshSyncedLibraries(); ApplySyncedChartFields(); RebuildSyncedChart(); UpdateDirtyDock(); };
        _chartWindow.SelectionChanged += (_, _) => { ApplySyncedChartFields(); RebuildSyncedChart(); UpdateDirtyDock(); };
        ConfigureSyncedChart();
        _syncedBody.Children.Add(_syncedChart);
        _syncedMatchButton.Flyout = new Flyout { Content = new ScrollViewer { Content = _syncedLibraryChoices, MaxHeight = 320, MinWidth = 200, HorizontalScrollMode = ScrollMode.Disabled } };
        _syncedBody.Children.Add(SyncedDivider(new StackPanel { Spacing = 8, Children = { _syncedMatchButton } }));
        RefreshSyncedLibraries();
        Detach(ImportedDefaultSortCombo);
        _titleLimit.Text = ViewModel.MaxItemsText ?? "";
        _titleLimit.LostFocus += (_, _) => CommitSyncedLimit();
        _titleLimit.KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; CommitSyncedLimit(); } };
        _syncedBody.Children.Add(BuildSyncedOrder());
        foreach (var (value, label) in new[] { ("", "Manual only"), ("daily", "Daily"), ("weekly", "Weekly"), ("monthly", "Monthly") }) _newSchedule.Items.Add(new ComboBoxItem { Tag = value, Content = label });
        _newSchedule.SelectedIndex = 0; ViewModel.SyncSchedule = "";
        _newSchedule.SelectionChanged += (_, _) => ViewModel.SyncSchedule = (_newSchedule.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        _newSchedule.MinHeight = 0; _newSchedule.Height = 36; _newSchedule.FontSize = 14; _newSchedule.Width = 280; _newSchedule.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(_newSchedule, "Sync schedule");
        _syncedBody.Children.Add(SyncedDivider(Field("Sync", _newSchedule)));
        _syncedError.Foreground = CurrentBrush("ErrorBrush"); _syncedBody.Children.Add(_syncedError);
        _syncedImports = new(App.Services.GetRequiredService<CollectionsApi>(), App.Services.GetRequiredService<CatalogApi>(), requestClient: client);
        RenderSyncedSource(); UpdateCurrentEditor(); await LoadSyncedPicksAsync();
    }
    private void RefreshSyncedLibraries()
    {
        _syncedLibraryChoices.Children.Clear();
        var allLibraries = new Button { Content = "All my libraries", IsEnabled = ViewModel.SelectedLibraryIds.Count > 0, Style = (Style)Application.Current.Resources["GhostButtonStyle"], HorizontalAlignment = HorizontalAlignment.Stretch };
        allLibraries.Click += (_, _) => { ViewModel.SelectedLibraryIds.Clear(); RefreshSyncedLibraries(); };
        _syncedLibraryChoices.Children.Add(allLibraries);
        var media = _newSynced && _syncedSource == "tmdb" ? (_chartMedia.SelectedItem as ComboBoxItem)?.Tag?.ToString() : _syncedPick?.MediaKind;
        foreach (var library in ViewModel.AvailableLibraries)
        {
            if (media is "movie" or "tv" && !string.IsNullOrEmpty(library.Type) && library.Type != "mixed" && library.Type != (media == "tv" ? "series" : "movies")) { ViewModel.SelectedLibraryIds.Remove(library.Id); continue; }
            var box = new CheckBox { Content = library.Name, IsChecked = ViewModel.SelectedLibraryIds.Contains(library.Id) };
            box.Checked += (_, _) => { if (!ViewModel.SelectedLibraryIds.Contains(library.Id)) ViewModel.SelectedLibraryIds.Add(library.Id); UpdateSyncedMatchLabel(); };
            box.Unchecked += (_, _) => { ViewModel.SelectedLibraryIds.Remove(library.Id); UpdateSyncedMatchLabel(); }; _syncedLibraryChoices.Children.Add(box);
        }
        UpdateSyncedMatchLabel();
    }
    private async Task LoadSyncedPicksAsync()
    {
        if (_syncedImports == null || _sourceChoiceLoading) return;
        var generation = ++_syncedLoadGeneration; var client = App.Services.GetRequiredService<SiloApiClient>(); var context = client.CaptureContext();
        _sourceChoiceLoading = true; _syncedError.Text = ""; RenderSyncedSource();
        try
        {
            await _syncedImports.LoadTemplateFlowAsync();
            if (!_editorActive || generation != _syncedLoadGeneration || !client.IsCurrentContext(context)) return;
            _syncedError.Text = _syncedImports.TemplateErrorMessage ?? "";
        }
        finally
        {
            if (generation == _syncedLoadGeneration) { _sourceChoiceLoading = false; if (_editorActive && client.IsCurrentContext(context)) RenderSyncedSource(); }
        }
    }
    private void RenderSyncedSource()
    {
        _syncedChoices.Children.Clear(); _syncedChart.Visibility = _syncedSource == "tmdb" ? Visibility.Visible : Visibility.Collapsed;
        _listSearchPanel.Visibility = _syncedSource == "mdblist" ? Visibility.Visible : Visibility.Collapsed;
        BuildSyncedThemes();
        RefreshSyncedLibraries();
        if (_syncedLinkField != null) _syncedLinkField.Visibility = _syncedSource == "tmdb" ? Visibility.Collapsed : Visibility.Visible;
        _syncedLink.PlaceholderText = _syncedSource == "mdblist" ? "https://mdblist.com/lists/…" : "Paste a public TMDB list link or list ID";
        if (_syncedLinkField?.Children.FirstOrDefault() is TextBlock linkLabel) linkLabel.Text = _syncedSource == "mdblist" ? "Or paste any MDBList link" : "Public TMDB list link or list ID";
        AutomationProperties.SetName(_syncedLink, _syncedSource == "mdblist" ? "MDBList link" : "TMDB list link");
        if (_sourceChoiceLoading) { _syncedChoices.Children.Add(new SkeletonText { Width = 160, Height = 20, HorizontalAlignment = HorizontalAlignment.Left }); return; }
        if (_syncedImports?.TemplateErrorMessage != null && _syncedImports.TemplateGroups.Count == 0)
        {
            var retry = new Button { Content = "Retry lists", HorizontalAlignment = HorizontalAlignment.Left }; retry.Click += async (_, _) => await LoadSyncedPicksAsync(); _syncedChoices.Children.Add(retry); return;
        }
        var picks = (_syncedImports?.TemplateGroups.Where(group => _syncedSource != "mdblist" || _syncedTheme == "all" || group.Category == _syncedTheme).SelectMany(group => group.Templates).Where(pick => pick.Source == _syncedSource && !string.IsNullOrWhiteSpace(pick.Mdblist?.Url ?? pick.TmdbList?.Url ?? (pick.Source == "tmdb" ? pick.Id : null)) && (_syncedSource != "mdblist" || string.IsNullOrWhiteSpace(_listSearch.Text) || (pick.Title + " " + pick.Description + " " + string.Join(" ", pick.Tags)).Contains(_listSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase))) ?? []).ToArray();
        if (_syncedSource != "mdblist") picks = [];
        _syncedChoiceFrame.Visibility = _syncedSource == "mdblist" ? Visibility.Visible : Visibility.Collapsed;
        if (picks.Length > 0) _syncedChoices.Children.Add(SyncedGroupHeading("Popular picks", "Ready-made lists that work well here"));
        foreach (var pick in picks)
        {
            var row = CollectionPickRow.Create(pick, ReferenceEquals(pick, _syncedPick), "personal-list-" + GetHashCode());
            row.Checked += (_, _) =>
            {
                _syncedPick = pick;
                _pickingTemplate = true;
                ApplySyncedTemplateFields(pick);
                if (pick.Source == "tmdb" && pick.Tmdb != null)
                {
                    _chartPreset.SelectedItem = _chartPreset.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, pick.Tmdb.Preset));
                    _chartMedia.SelectedItem = _chartMedia.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, pick.Tmdb.MediaType));
                    _chartWindow.SelectedIndex = pick.Tmdb.TimeWindow == "week" ? 1 : 0;
                }
                else _syncedLink.Text = pick.Mdblist?.Url ?? pick.TmdbList?.Url ?? "";
                _syncedPick = pick; _pickingTemplate = false; RefreshSyncedLibraries(); UpdateCurrentEditor();
            };
            _syncedChoices.Children.Add(row);
        }
        if (_syncedSource == "mdblist" && _syncedImports != null && !string.IsNullOrWhiteSpace(_listSearch.Text))
        {
            _syncedChoices.Children.Add(SyncedGroupHeading("From MDBList", "Public lists matching your search"));
            foreach (var list in _syncedImports.MdblistResults)
            {
                var url = (list.Url ?? $"https://mdblist.com/lists/{list.UserName}/{list.Slug}").TrimEnd('/');
                if (!url.EndsWith("/json", StringComparison.OrdinalIgnoreCase)) url += "/json";
                var row = new RadioButton { GroupName = "personal-list-" + GetHashCode(), Content = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = list.Name, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = $"by {list.UserName} · {list.Items:N0} titles", FontSize = 12.5, Foreground = CurrentBrush("SecondaryTextBrush") } } } };
                row.Checked += (_, _) => { _pickingTemplate = true; _syncedLink.Text = url; _syncedPick = new() { Source = "mdblist", MediaKind = list.Mediatype == "show" ? "tv" : list.Mediatype, Mdblist = new() { Url = url } }; ApplySyncedPickFields(list.Name, list.Description, null, ""); _pickingTemplate = false; RefreshSyncedLibraries(); UpdateCurrentEditor(); };
                _syncedChoices.Children.Add(row);
            }
        }
        if (_syncedChoices.Children.Count == 0 && _syncedSource == "mdblist") _syncedChoices.Children.Add(new TextBlock { Text = "No popular picks here. Search MDBList or paste a link.", FontSize = 13, Margin = new(14, 12, 14, 12), TextWrapping = TextWrapping.Wrap, Foreground = CurrentBrush("SecondaryTextBrush") });
    }
    private void NormalizeChart()
    {
        var preset = (_chartPreset.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var previous = (_chartMedia.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var allowed = preset switch { "trending" => new[] { "movie", "tv", "all" }, "airing_today" or "on_the_air" => ["tv"], "now_playing" or "upcoming" => ["movie"], _ => ["movie", "tv"] };
        _normalizingChart = true;
        try
        {
            _chartMedia.Items.Clear();
            foreach (var value in allowed) _chartMedia.Items.Add(new ComboBoxItem { Tag = value, Content = value switch { "movie" => "Movies", "tv" => "TV shows", _ => "Both" } });
            _chartMedia.SelectedItem = _chartMedia.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, previous)) ?? _chartMedia.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, previous == null && preset == "trending" ? "all" : allowed[0]));
        }
        finally { _normalizingChart = false; }
        RefreshSyncedLibraries(); ApplySyncedChartFields(); RebuildSyncedChart(); UpdateDirtyDock();
    }
    private static string? SyncedNamedSchedule(string? cron)
    {
        if (cron is "daily" or "weekly" or "monthly") return cron;
        var fields = cron?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields?.Length == 5 ? fields[2] == "1" && fields[3] == "*" ? "monthly" : fields[2] == "*" && fields[3] == "*" && fields[4] != "*" ? "weekly" : "daily" : null;
    }
    private async Task SearchSyncedListsAsync()
    {
        _listSearchDebounce?.Cancel();
        _listSearchDebounce?.Dispose();
        var debounce = _listSearchDebounce = new CancellationTokenSource();
        var query = _listSearch.Text.Trim();
        RenderSyncedSource();
        if (query.Length == 0 || !_listSearch.IsEnabled || _syncedImports == null) return;
        var client = App.Services.GetRequiredService<SiloApiClient>(); var context = client.CaptureContext();
        try
        {
            await Task.Delay(300, debounce.Token);
            await _syncedImports.SearchMDBListAsync(query);
            if (debounce.IsCancellationRequested || !_editorActive || !client.IsCurrentContext(context)) return;
            _syncedError.Text = _syncedImports.TemplateErrorMessage ?? "";
            RenderSyncedSource();
        }
        catch (OperationCanceledException) { }
    }
    private async Task SaveSyncedCreationAsync()
    {
        if (ViewModel.IsSaving || _syncedImports == null) return;
        CommitSyncedLimit();
        var client = App.Services.GetRequiredService<SiloApiClient>(); var context = client.CaptureContext();
        if (_creationCapabilities?.ImportSources?.Contains(_syncedSource) != true) { _syncedError.Text = "Synced lists are off on this server."; return; }
        var pick = _syncedSource == "tmdb" ? new CollectionTemplate { Source = "tmdb", Tmdb = new() { Preset = (_chartPreset.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "", MediaType = (_chartMedia.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "", TimeWindow = (_chartPreset.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "trending" ? (_chartWindow.SelectedItem as ComboBoxItem)?.Tag?.ToString() : null } } : _syncedPick ?? new CollectionTemplate { Source = _syncedSource };
        if (pick.Source == "tmdb" && string.IsNullOrEmpty(pick.Tmdb?.Preset)) { _syncedError.Text = "Pick a chart first."; return; }
        if (!string.IsNullOrWhiteSpace(ViewModel.MaxItemsText) && (!int.TryParse(ViewModel.MaxItemsText, out var limit) || limit < 1)) { _syncedError.Text = "Title limit must be a positive whole number."; return; }
        var poster = ViewModel.PosterFileBytes; var fileName = ViewModel.PosterFileName; var contentType = ViewModel.PosterContentType; var visible = ViewModel.IncludeInServerCollections;
        ViewModel.IsSaving = true;
        try
        {
            var created = await _syncedImports.ImportTemplateAsync(new()
            {
                Template = pick, Title = ViewModel.Name, Description = ViewModel.Description, MaxItems = int.TryParse(ViewModel.MaxItemsText, out var max) ? max : null,
                LibraryIds = ViewModel.SelectedLibraryIds.ToList(), IsShared = ViewModel.IsShared, MDBListUrl = _syncedLink.Text, TMDBListUrl = _syncedLink.Text,
                SyncSchedule = ViewModel.SyncSchedule, PosterUrl = poster is { Length: > 0 } || ViewModel.RemovePosterOnSave ? null : !string.IsNullOrWhiteSpace(ViewModel.PosterSourceUrl) ? ViewModel.PosterSourceUrl : _syncedPickedPosterUrl, DisplayQueryDefinition = ViewModel.CaptureDisplayQuery()
                , SortConfig = ViewModel.CaptureSortConfig()
            });
            if (!_editorActive || !client.IsCurrentContext(context)) return;
            if (created == null) { _syncedError.Text = _syncedImports.TemplateErrorMessage ?? "Couldn't create the list."; return; }
            // The import is durable: retain its ID before artwork or preference
            // requests. Their failure must never repeat the creation POST.
            _newSynced = false; _syncedCreatePanel.Visibility = Visibility.Collapsed;
            Detach(ImportedDefaultSortCombo);
            Body(ImportedSourceSection).Children.Add(Field("Order", ImportedDefaultSortCombo));
            ViewModel.CollectionId = created.Id; ViewModel.IsEditing = true; ViewModel.CollectionType = created.CollectionType;
            _editingCollectionId = created.Id;
            await LoadEditorAsync(created.Id);
            ViewModel.IncludeInServerCollections = visible;
            if (poster is { Length: > 0 } && fileName != null) ViewModel.SetPosterFile(fileName, poster, contentType);
            if (visible || poster is { Length: > 0 }) { ViewModel.IsSaving = false; await ViewModel.SaveCommand.ExecuteAsync(null); }
        }
        finally { ViewModel.IsSaving = false; UpdateCurrentEditor(); }
    }
}
