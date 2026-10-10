using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
using SiloPlayer.Converters;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.MediaMaintenance;
using SiloPlayer.Core.Services;
using System.Text.Json;

namespace SiloPlayer.Views.Dialogs;

public sealed class SplitVersionsDialog : ItemScopedDialog
{
    private readonly MediaSplitApi _api;
    private readonly int? _libraryId;
    private readonly ItemsRepeater _files = new() { Layout = new StackLayout { Spacing = 4 }, VerticalCacheLength = 1 };
    private readonly StackPanel _candidates = new() { Spacing = 4 };
    private sealed record FileRow(string Id, string Path, string Caption, List<string>? GroupIds = null);
    private List<FileRow> _rows = [];
    private bool _editing;
    private readonly TextBox _title = Input("Title", "Search title"), _year = Input("Year", "Search year"), _tmdb = Input("TMDB ID", "TMDB ID"), _imdb = Input("IMDb ID (tt…)", "IMDb ID"), _tvdb = Input("TVDB ID", "TVDB ID");
    private readonly CheckBox _unmatched = new() { Content = "Detach as unmatched (identify later)", FontSize = 14 };
    private readonly ComboBox _history = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 36, MinHeight = 0 };
    private readonly Button _search = new() { Content = "Search", HorizontalAlignment = HorizontalAlignment.Stretch, Height = 36, MinHeight = 0 };
    private readonly TextBlock _warning = Text("", 12), _previewText = Text("", 14, true);
    private readonly Border _previewBox;
    private readonly Dictionary<string, CheckBox> _checks = [];
    private readonly List<(CheckBox Root, List<string> Ids)> _groups = [];
    private readonly HashSet<string> _selected = [];
    private List<JsonElement> _allFiles = [];
    private MatchCandidate? _candidate;
    private bool _changing, _searching, _previewing;
    private int _planRevision, _searchRevision;
    private int _acceptedRevision = -1;
    private CancellationTokenSource? _previewLifetime;

    public SplitVersionsDialog(MediaItemDetail item, int? libraryId) : base(item.ContentId, "Split Versions", "", 672, "Split", AuthorizationPolicy.CanCurateMetadata, maxHeightRatio: .85)
    {
        _api = new(Client); _libraryId = libraryId; Body.Children.RemoveAt(0);
        Body.Children.Insert(0, new Border { Background = Brush("SurfaceBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Child = Text(item.Title + (item.Year > 0 ? $" ({item.Year})" : "") + "   " + item.Type) });
        _files.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><ContentControl HorizontalContentAlignment=\"Stretch\" IsTabStop=\"False\"/></DataTemplate>");
        _files.ElementPrepared += PrepareFile;
        _files.ElementClearing += (_, args) =>
        {
            if (args.Element is ContentControl { Content: CheckBox check } holder && check.Tag is FileRow row)
            {
                if (row.GroupIds != null) _groups.RemoveAll(group => group.Root == check);
                else _checks.Remove(row.Id);
                holder.Content = null;
            }
        };
        var fileScroll = new ScrollViewer { Content = _files, MaxHeight = 256, Padding = new Thickness(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled };
        AutomationProperties.SetName(fileScroll, "Files to move");
        Body.Children.Add(Text("Files to move")); Body.Children.Add(new Border { Child = fileScroll, CornerRadius = new CornerRadius(8), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1) }); _warning.Foreground = Brush("ErrorBrush"); Body.Children.Add(_warning);
        Body.Children.Add(Text("Correct identity for the moved files"));
        var fields = new Grid { RowSpacing = 12, ColumnSpacing = 12 }; fields.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); fields.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 3; row++) fields.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetColumnSpan(_title, 2); fields.Children.Add(_title);
        foreach (var (field, row, column) in new[] { (_year, 1, 0), (_tmdb, 1, 1), (_imdb, 2, 0), (_tvdb, 2, 1) }) { Grid.SetRow(field, row); Grid.SetColumn(field, column); fields.Children.Add(field); }
        Body.Children.Add(fields); Body.Children.Add(_search); Body.Children.Add(new ScrollViewer { Content = _candidates, MaxHeight = 224, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Body.Children.Add(_unmatched);
        Body.Children.Add(Text("Watch history handling"));
        foreach (var (mode, label) in new[] { ("evidence", "Follow play evidence (recommended)"), ("keep", "Keep all history on this item"), ("move_all", "Move everything to the new item") }) _history.Items.Add(new ComboBoxItem { Content = label, Tag = mode });
        _history.SelectedIndex = 0; AutomationProperties.SetName(_history, "Watch history handling"); Body.Children.Add(_history);
        Body.Children.Add(Text("Resume points and downloads tied to the moved files always follow them. This controls history rows without per-file evidence.", 12, true));
        _previewBox = new Border { Child = _previewText, CornerRadius = new CornerRadius(8), Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), Visibility = Visibility.Collapsed }; Body.Children.Add(_previewBox);
        _unmatched.Checked += (_, _) => { _candidate = null; PlanChanged(); }; _unmatched.Unchecked += (_, _) => PlanChanged(); _history.SelectionChanged += (_, _) => PlanChanged();
        _search.Click += async (_, _) => await SearchAsync(); Closed += (_, _) => { _previewLifetime?.Cancel(); _previewLifetime?.Dispose(); };
    }
    private static TextBox Input(string placeholder, string name)
    { var input = new TextBox { PlaceholderText = placeholder, FontSize = 14, Height = 36, MinHeight = 0, MinWidth = 0, Padding = new Thickness(12, 6, 12, 6) }; AutomationProperties.SetName(input, name); return input; }
    private static string Value(JsonElement value, string key) => value.TryGetProperty(key, out var property) ? property.ToString() : "";
    private bool PlanValid => _selected.Count > 0 && _selected.Count < _allFiles.Count && (_unmatched.IsChecked == true || _candidate != null);
    protected override async Task LoadAsync()
    {
        _allFiles = await _api.GetFilesAsync(Context, ItemId, Lifetime.Token); RequireAuthority();
        if (_allFiles.Count < 2) { Body.Children.Insert(2, Text("This item has only one file; splitting needs at least two.", 14, true)); return; }
        foreach (var grouping in _allFiles.GroupBy(file => Value(file, "observed_root_path")).OrderBy(group => group.Key, StringComparer.CurrentCulture))
        {
            var ids = grouping.Select(file => Value(file, "id")).ToList();
            _rows.Add(new("", grouping.Key, grouping.Key, ids));
            foreach (var file in grouping)
            {
                var id = Value(file, "id"); var path = Value(file, "file_path"); var caption = path.Split('/').Last(); var season = Value(file, "season_number"); var episode = Value(file, "episode_number"); if (int.TryParse(season, out var seasonNumber) && seasonNumber > 0 && int.TryParse(episode, out var episodeNumber) && episodeNumber > 0) caption += $"   S{season}E{episode}";
                _rows.Add(new(id, path, caption));
            }
        }
        _files.ItemsSource = _rows;
    }
    private void PrepareFile(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        var row = _rows[args.Index]; var isGroup = row.GroupIds != null;
        var check = new CheckBox { Tag = row, Content = new TextBlock { Text = row.Caption, FontSize = 12, FontFamily = new FontFamily("Consolas"), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush(isGroup ? "PrimaryTextBrush" : "SecondaryTextBrush") }, Margin = new Thickness(isGroup ? 0 : 24, 0, 0, 0), MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = _editing, IsChecked = isGroup ? row.GroupIds!.All(_selected.Contains) : _selected.Contains(row.Id) };
        AutomationProperties.SetName(check, (isGroup ? "Select all files in " : "Select ") + row.Path); ToolTipService.SetToolTip(check, row.Path);
        if (isGroup) _groups.Add((check, row.GroupIds!)); else _checks.Add(row.Id, check);
        void Changed(object sender, RoutedEventArgs args)
        {
            if (_changing || !Ready || Busy || !CanAct) return;
            var select = check.IsChecked == true; _changing = true;
            foreach (var id in row.GroupIds ?? [row.Id])
            {
                if (select) _selected.Add(id); else _selected.Remove(id);
                if (_checks.TryGetValue(id, out var realized)) realized.IsChecked = select;
            }
            _changing = false; PlanChanged();
        }
        check.Checked += Changed; check.Unchecked += Changed;
        ((ContentControl)args.Element).Content = check;
    }
    private async Task SearchAsync()
    {
        if (_searching || Busy || !Ready || !CanAct) return;
        var revision = ++_searchRevision; _searching = true; _candidate = null; _unmatched.IsChecked = false; _candidates.Children.Clear(); PlanChanged(); UpdateCommands();
        try
        {
            var response = await _api.SearchAsync(Context, ItemId, new ItemMatchSearchRequest { Title = string.IsNullOrWhiteSpace(_title.Text) ? null : _title.Text, Year = int.TryParse(_year.Text, out var year) ? year : null, TmdbId = string.IsNullOrWhiteSpace(_tmdb.Text) ? null : _tmdb.Text, ImdbId = string.IsNullOrWhiteSpace(_imdb.Text) ? null : _imdb.Text, TvdbId = string.IsNullOrWhiteSpace(_tvdb.Text) ? null : _tvdb.Text, LibraryId = _libraryId }, Lifetime.Token); RequireAuthority();
            if (revision != _searchRevision) return;
            if (response.Candidates.Count == 0) _candidates.Children.Add(Text("No candidates found.", 14, true));
            foreach (var candidate in response.Candidates)
            {
                var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(40) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new Border { Width = 40, Height = 56, CornerRadius = new CornerRadius(4), Background = Brush("SurfaceBrush"), Child = new Image { Width = 40, Height = 56, Stretch = Stretch.UniformToFill, Source = string.IsNullOrEmpty(candidate.ImageUrl) ? null : (ImageSource)new UrlToImageSourceConverter().Convert(candidate.ImageUrl, typeof(ImageSource), null!, "") } });
                var copy = new StackPanel(); copy.Children.Add(new TextBlock { Text = candidate.Title, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis }); copy.Children.Add(Text(candidate.Year > 0 ? candidate.Year.ToString() : "", 12, true)); Grid.SetColumn(copy, 1); row.Children.Add(copy);
                var choice = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), BorderBrush = Brush("BorderBrush") }; AutomationProperties.SetName(choice, candidate.Title);
                choice.Click += (_, _) => { if (Busy || !CanAct) return; _candidate = candidate; _changing = true; _unmatched.IsChecked = false; _changing = false; foreach (var option in _candidates.Children.OfType<Button>()) option.BorderBrush = Brush(option == choice ? "AccentBrush" : "BorderBrush"); PlanChanged(); }; _candidates.Children.Add(choice);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Status.Text = error.Message; }
        finally { _searching = false; UpdateCommands(); }
    }
    private object BuildRequest(bool dryRun) => new
    {
        file_ids = _selected.ToArray(), target = _unmatched.IsChecked == true ? (object)new { unmatched = true } : new Dictionary<string, object?> { ["provider_ids"] = _candidate!.ProviderIds, ["title"] = _candidate.Title, ["year"] = _candidate.Year > 0 ? _candidate.Year : null },
        history_mode = (_history.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "evidence", dry_run = dryRun
    };
    private void PlanChanged()
    {
        if (_changing) return;
        var revision = ++_planRevision; _acceptedRevision = -1; _previewing = false; _previewLifetime?.Cancel(); _previewLifetime?.Dispose(); _previewLifetime = null;
        _changing = true; foreach (var group in _groups) group.Root.IsChecked = group.Ids.All(_selected.Contains); _changing = false;
        _warning.Text = _selected.Count > 0 && _selected.Count == _allFiles.Count ? "All files are selected — that is a re-match, not a split. Use “Match Item” instead, or deselect the files that are correct." : "";
        _previewBox.Visibility = PlanValid ? Visibility.Visible : Visibility.Collapsed; _previewText.Text = "Previewing…"; Status.Text = ""; UpdateCommands();
        if (!PlanValid || !Ready || !CanAct || Busy) return;
        var lifetime = _previewLifetime = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token); var request = BuildRequest(true); _ = PreviewAsync(revision, request, lifetime.Token);
    }
    private async Task PreviewAsync(int revision, object request, CancellationToken ct)
    {
        try
        {
            await Task.Delay(400, ct); RequireAuthority(); _previewing = true; UpdateCommands(); var response = await _api.SplitAsync(Context, ItemId, request, ct); RequireAuthority();
            if (revision != _planRevision || ct.IsCancellationRequested) return;
            if (!response.TryGetProperty("dry_run", out var dryRun) || dryRun.ValueKind != JsonValueKind.True) throw new InvalidOperationException("The server did not return a split preview. Close and reopen to try again.");
            var report = response.GetProperty("reattribution"); var count = Value(response, "files_moved");
            _previewText.Text = $"Preview — {count} {(count == "1" ? "file" : "files")} → {Value(response, "target_content_id")}" + (Value(response, "target_created") == "True" ? "   new item" : "") + $"\n• {Value(report, "progress_moved")} resume points move\n• {Value(report, "history_moved")} history entries move, {Value(report, "history_ambiguous")} stay for lack of evidence\n• {Value(report, "downloads")} downloads move";
            if (response.TryGetProperty("episode_pairs", out var pairs) && pairs.TryGetInt32(out var episodes) && episodes > 0) _previewText.Text += $"\n• {episodes} episodes re-anchored";
            int Count(string key) => response.TryGetProperty(key, out var values) && values.ValueKind == JsonValueKind.Array ? values.GetArrayLength() : 0;
            if (Count("root_overrides") + Count("file_overrides") > 0) _previewText.Text += $"\n• {Count("root_overrides")} folder / {Count("file_overrides")} file identity override(s) pinned for future scans";
            _acceptedRevision = revision;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (revision == _planRevision && CanAct) { Status.Text = error.Message; _previewText.Text = "Preview could not be loaded. Change the selection or target to try again."; } }
        finally { if (revision == _planRevision) { _previewing = false; UpdateCommands(); } }
    }
    protected override void UpdateCommands() { base.UpdateCommands(); IsPrimaryButtonEnabled &= PlanValid && _acceptedRevision == _planRevision && !_previewing && !_searching; _search.IsEnabled = Ready && CanAct && !Busy && !_searching; }
    protected override void SetEditing(bool enabled) { _editing = enabled; foreach (var control in new Control[] { _title, _year, _tmdb, _imdb, _tvdb, _unmatched, _history }) control.IsEnabled = enabled; foreach (var check in _checks.Values) check.IsEnabled = enabled; foreach (var group in _groups) group.Root.IsEnabled = enabled; foreach (var choice in _candidates.Children.OfType<Button>()) choice.IsEnabled = enabled; }
    protected override async Task SubmitAsync()
    {
        if (!PlanValid || _acceptedRevision != _planRevision || _previewing || _searching) return;
        await _api.SplitAsync(Context, ItemId, BuildRequest(false), Lifetime.Token); RequireAuthority(); HasSaved = true; DispatcherQueue.TryEnqueue(Hide);
    }
}
