using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Converters;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Messaging;
using CommunityToolkit.Mvvm.Messaging;
using System.Text.Json;

namespace SiloPlayer.Views.Dialogs;

public sealed class EditMetadataDialog : ContentDialog
{
    private MediaItemDetail _item;
    private readonly MetadataEditState _state;
    private readonly Grid _root = new();
    private readonly Dictionary<string, FrameworkElement> _fieldGroups = [];
    private readonly List<(Grid Grid, string[][] Rows)> _fieldLayouts = [];
    private readonly Button _reset;
    private readonly StackPanel _navigation = new();
    private readonly StackPanel _body = new() { Spacing = 24 };
    private readonly ScrollViewer _scroll;
    private readonly ScrollViewer _navigationScroll;
    private readonly Dictionary<string, StackPanel> _sections = [];
    private readonly Dictionary<string, TextBox> _textInputs = [];
    private readonly Dictionary<string, NumberBox> _numberInputs = [];
    private readonly Dictionary<string, ComboBox> _choiceInputs = [];
    private readonly Dictionary<string, MetadataTagsInput> _tagInputs = [];
    private readonly List<(string Field, Button Button)> _lockButtons = [];
    private readonly Grid _header = new() { ColumnSpacing = 10 };
    private readonly TextBlock _lockedCount = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly MediaMaintenanceApi _api;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _pending, _imagePending;
    private Action? _resizeImages;
    public bool HasSaved { get; private set; }
    private Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private Brush TranslucentBrush(string key, double opacity)
    {
        var brush = (SolidColorBrush)Brush(key);
        return new SolidColorBrush(brush.Color) { Opacity = opacity };
    }
    public EditMetadataDialog(MediaItemDetail item)
    {
        _item = item; _state = new(item); _api = App.Services.GetRequiredService<MediaMaintenanceApi>();
        _header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _header.Padding = new Thickness(20, 16, 16, 16);
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock { Text = "Edit Metadata", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        heading.Children.Add(new Border { Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(4), Background = Brush("SurfaceRaisedBrush"),
            Child = new TextBlock { Text = item.Type.Length == 0 ? "Item" : char.ToUpperInvariant(item.Type[0]) + item.Type[1..], FontSize = 11, Foreground = Brush("SecondaryTextBrush") } });
        _header.Children.Add(heading); _lockedCount.Foreground = Brush("SecondaryTextBrush");
        Grid.SetColumn(_lockedCount, 1); _header.Children.Add(_lockedCount);
        var cornerClose = EditorDialogPresentation.CornerClose(this); Grid.SetColumn(cornerClose, 2); _header.Children.Add(cornerClose);
        Title = _header; PrimaryButtonText = "Save Changes"; CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary; Background = Brush("AppBackgroundBrush");
        EditorDialogPresentation.Configure(this, 1024, new Thickness(0));
        Resources["ContentDialogTitleMargin"] = new Thickness(0);
        _root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new() { Width = new GridLength(160) });
        _root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _navigationScroll = new() { Content = _navigation, Padding = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = .1 }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _root.Children.Add(_navigationScroll); Grid.SetRow(_navigationScroll, 1);
        _scroll = new() { Content = _body, Padding = new Thickness(24, 20, 24, 20), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(_scroll, 1); Grid.SetRow(_scroll, 1); _root.Children.Add(_scroll);
        var general = Section("general", "General");
        foreach (var (key, label) in new[] { ("title","Title"),("sort_title","Sort Title"),("original_title","Original Title"),("overview","Overview"),("tagline","Tagline"),("content_rating","Content Rating") }) Text(general, key, label);
        if (item.Type is "movie" or "episode") Number(general, "runtime", "Runtime (min)", 0, double.PositiveInfinity);
        if (item.Type == "series") Choice(general, "status", "Status", new[] { "", "Continuing", "Ended" });
        if (item.Type is "season" or "episode") Number(general, "season_number", "Season Number", 0, double.PositiveInfinity, item.Type == "episode");
        if (item.Type == "episode") Number(general, "episode_number", "Episode Number", 0, double.PositiveInfinity);
        var dates = Section("dates", "Dates & Ratings");
        if (item.Type is "movie" or "series") Number(dates, "year", "Year", 0, double.PositiveInfinity);
        if (item.Type == "movie") Text(dates, "release_date", "Release Date");
        if (item.Type == "series")
        { Text(dates, "first_air_date", "First Air Date"); Text(dates, "last_air_date", "Last Air Date"); Text(dates, "air_time", "Air Time"); Text(dates, "air_timezone", "Air Timezone"); }
        if (item.Type is "season" or "episode") Text(dates, "air_date", "Air Date");
        if (item.Type is "movie" or "series")
        {
            Number(dates, "rating_imdb", "IMDb Rating", 0, 10); Number(dates, "rating_tmdb", "TMDB Rating", 0, 10);
            Number(dates, "rating_rt_critic", "RT Critic Score", 0, 100); Number(dates, "rating_rt_audience", "RT Audience Score", 0, 100);
            var tags = Section("tags", "Tags & Genres");
            foreach (var key in item.Type == "series" ? new[] { "genres", "studios", "networks", "countries" } : new[] { "genres", "studios", "countries" })
            {
                var input = new MetadataTagsInput((string[])_state.Fields[key]!);
                _tagInputs[key] = input;
                input.Changed += (_, _) => { _state.Set(key, input.Values); RefreshLocks(); };
                var group = new StackPanel { Spacing = 6 };
                group.Children.Add(Label(key, char.ToUpper(key[0]) + key[1..])); group.Children.Add(input); tags.Children.Add(group);
            }
        }
        var ids = Section("ids", "External IDs");
        Text(ids, "imdb_id", "IMDb ID"); Text(ids, "tmdb_id", "TMDB ID"); Text(ids, "tvdb_id", "TVDB ID");
        if (item.Type != "episode" && AuthorizationPolicy.IsActingAdmin(App.Services.GetRequiredService<AuthService>()))
            BuildImages(Section("images", "Images"));
        var generalRows = new List<string[]> { new[] { "title" }, new[] { "sort_title", "original_title" }, new[] { "overview" }, new[] { "tagline" },
            item.Type switch
            {
                "movie" => new[] { "content_rating", "runtime" },
                "series" => new[] { "content_rating", "status" },
                "season" or "episode" => new[] { "content_rating", "season_number" },
                _ => new[] { "content_rating" },
            } };
        if (item.Type == "episode") generalRows.Add(["episode_number", "runtime"]);
        BuildFieldLayout(general, generalRows.ToArray());
        BuildFieldLayout(dates, [["year", "release_date", "first_air_date"], ["last_air_date", "air_time", "air_timezone"], ["air_date"], ["rating_imdb", "rating_tmdb"], ["rating_rt_critic", "rating_rt_audience"]]);
        BuildFieldLayout(ids, [["imdb_id"], ["tmdb_id"], ["tvdb_id"]]);
        // An unselected section has not acquired a visual Parent yet. Its stored
        // layout still owns the rating row and is the authority for insertion.
        if (_fieldGroups.TryGetValue("rating_imdb", out var rating) && _fieldLayouts.Select(layout => layout.Grid).FirstOrDefault(grid => grid.Children.Contains(rating)) is Grid ratingRow)
            dates.Children.Insert(dates.Children.IndexOf(ratingRow), new TextBlock { Text = "RATINGS", FontSize = 11,
                FontWeight = FontWeights.SemiBold, CharacterSpacing = 100, Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("SecondaryTextBrush") });
        _reset = new Button { Content = "Reset to Provider", FontSize = 13, Height = 32, MinHeight = 0,
            Padding = new Thickness(12, 4, 12, 4), Foreground = Brush("SecondaryTextBrush"),
            Style = (Style)Application.Current.Resources["GhostButtonStyle"], HorizontalAlignment = HorizontalAlignment.Left };
        _reset.Visibility = item.Type is "movie" or "series" ? Visibility.Visible : Visibility.Collapsed;
        var reset = _reset;
        var confirmation = new StackPanel { Spacing = 12, MaxWidth = 340 };
        confirmation.Children.Add(new TextBlock { Text = "Reset to Provider Metadata?", FontWeight = FontWeights.SemiBold });
        confirmation.Children.Add(new TextBlock { Text = "This will re-fetch metadata from the provider. Your manual edits may be replaced.", TextWrapping = TextWrapping.Wrap });
        var confirm = new Button { Content = "Reset", Style = (Style)Application.Current.Resources["DestructiveButtonStyle"] };
        var flyout = new Flyout { Content = confirmation }; reset.Flyout = flyout; confirmation.Children.Add(confirm);
        confirm.Click += async (_, _) => { if (_pending) return; _pending = true; confirm.IsEnabled = false;
            try { await _api.RefreshMetadataAsync(_item.ContentId); HasSaved = true; Changed(); flyout.Hide(); Hide(); }
            catch (Exception ex) { Toast().Error(ex.Message); } finally { _pending = false; confirm.IsEnabled = true; } };
        Content = _root;
        ShowSection("general"); PrimaryButtonClick += Save;
        Opened += async (_, _) => { Reflow(); await BuildTranslation(); };
        SizeChanged += (_, _) => Reflow(); Closed += (_, _) => _lifetime.Cancel();
    }
    private ToastService Toast() => App.Services.GetRequiredService<ToastService>();
    private void Changed() => WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.ItemMetadataRefreshed, _item.ContentId, _item.SeriesId));
    private StackPanel Section(string key, string title)
    {
        var panel = new StackPanel { Spacing = 16 }; _sections[key] = panel; _body.Children.Add(panel);
        var button = new Button { Content = title, Tag = key, FontSize = 13, FontWeight = FontWeights.Medium, Padding = new Thickness(16, 8, 16, 8), Height = 36, MinHeight = 36, CornerRadius = new CornerRadius(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        button.Click += (_, _) => ShowSection(key); _navigation.Children.Add(button); return panel;
    }
    private void ShowSection(string key)
    {
        foreach (var section in _sections) section.Value.Visibility = section.Key == key ? Visibility.Visible : Visibility.Collapsed;
        RefreshNavigation(); _scroll.ChangeView(null, 0, null); if (XamlRoot is not null) Reflow();
    }
    private void RefreshNavigation()
    {
        var narrow = (XamlRoot?.Size.Width ?? 1100) < 640;
        foreach (var button in _navigation.Children.OfType<Button>())
        {
            var selected = _sections[(string)button.Tag].Visibility == Visibility.Visible;
            button.Foreground = Brush(selected ? "PrimaryTextBrush" : "SecondaryTextBrush");
            button.Background = selected ? TranslucentBrush("AccentBrush", .08) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.BorderBrush = Brush("AccentBrush");
            button.BorderThickness = selected ? narrow ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 2, 0) : new Thickness(0);
        }
    }
    private void BuildFieldLayout(StackPanel section, string[][] rows)
    {
        var rowIndex = 0;
        foreach (var row in rows)
        {
            var keys = row.Where(_fieldGroups.ContainsKey).ToArray(); if (keys.Length == 0) continue;
            var grid = new Grid { RowSpacing = 16, ColumnSpacing = 16 };
            foreach (var key in keys) { var group = _fieldGroups[key]; section.Children.Remove(group); grid.Children.Add(group); }
            section.Children.Insert(rowIndex++, grid); _fieldLayouts.Add((grid, [keys]));
        }
    }

    private void Reflow()
    {
        var width = XamlRoot?.Size.Width ?? 1100; var narrow = width < 640;
        _root.Width = Math.Max(260, Math.Min(1022, width - (narrow ? 2 : 34)));
        _header.Width = _root.Width;
        _scroll.Padding = narrow ? new Thickness(16) : new Thickness(24, 20, 24, 20);
        foreach (var (grid, rows) in _fieldLayouts)
        {
            grid.RowDefinitions.Clear(); grid.ColumnDefinitions.Clear();
            var columns = narrow ? 1 : rows.Max(row => row.Length);
            for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var rowIndex = 0;
            foreach (var keys in rows)
            {
                if (narrow)
                {
                    foreach (var key in keys) { grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetRow(_fieldGroups[key], rowIndex++); Grid.SetColumn(_fieldGroups[key], 0); Grid.SetColumnSpan(_fieldGroups[key], 1); }
                }
                else
                {
                    grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    for (var column = 0; column < keys.Length; column++) { var group = _fieldGroups[keys[column]]; Grid.SetRow(group, rowIndex); Grid.SetColumn(group, column); Grid.SetColumnSpan(group, keys.Length == 1 ? columns : 1); }
                    rowIndex++;
                }
            }
        }
        // Match the reference flex body's natural active-form height, with its viewport cap.
        // Reflow the fields first so measurement uses the current column layout.
        _resizeImages?.Invoke();
        _body.Measure(new Windows.Foundation.Size(Math.Max(1, _root.Width - (narrow ? 0 : 160) - _scroll.Padding.Left - _scroll.Padding.Right), double.PositiveInfinity));
        var naturalBody = _body.DesiredSize.Height + _scroll.Padding.Top + _scroll.Padding.Bottom;
        EditorDialogPresentation.ReflowCommands(this, narrow, narrow ? new Thickness(16, 12, 16, 12) : new Thickness(20, 14, 20, 14), _reset);
        _header.Measure(new Windows.Foundation.Size(_root.Width, double.PositiveInfinity));
        var commands = EditorDialogPresentation.Descendants<Grid>(this).FirstOrDefault(grid => grid.Name == "CommandSpace");
        commands?.Measure(new Windows.Foundation.Size(_root.Width, double.PositiveInfinity));
        // The WebUI flex body uses its natural form height. Reserve the actual header,
        // footer and32px viewport margins rather than imposing an extra580px scroll cap.
        var bodyLimit = Math.Max(80, (XamlRoot?.Size.Height ?? 900) - 64 - _header.DesiredSize.Height - (commands?.DesiredSize.Height ?? (narrow ? 96 : 60)) - 2);
        _root.Height = Math.Min(bodyLimit, naturalBody + (narrow ? 44 : 0));
        foreach (var input in _numberInputs.Values)
            foreach (var text in EditorDialogPresentation.Descendants<TextBox>(input)) text.Style = (Style)Application.Current.Resources["DarkTextBoxStyle"];
        _navigation.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
        _root.ColumnDefinitions[0].Width = narrow ? new GridLength(0) : new GridLength(160);
        Grid.SetRow(_navigationScroll, narrow ? 0 : 1); Grid.SetColumnSpan(_navigationScroll, narrow ? 2 : 1);
        _navigationScroll.HorizontalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        _navigationScroll.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Grid.SetColumn(_scroll, narrow ? 0 : 1); Grid.SetColumnSpan(_scroll, narrow ? 2 : 1);
        // Horizontal navigation scrolls on small windows instead of clipping.
        _navigation.Spacing = 0;
        RefreshNavigation();
    }
    private Grid Label(string key, string text)
    {
        var row = new Grid(); row.Children.Add(new TextBlock { Text = text, FontSize = 12, Height = 12, LineHeight = 12, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("SecondaryTextBrush"), FontWeight = FontWeights.Medium });
        if (_item.Type is "movie" or "series" && MetadataEditState.LockMap.TryGetValue(key, out var field))
        {
            var button = new Button { Padding = new Thickness(4, 0, 4, 0), Height = 18, MinHeight = 0, MinWidth = 0, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
            button.Click += (_, _) => { _state.SetLock(field, !_state.EffectiveLocks(_item.LockedFields).Contains(field)); RefreshLocks(); };
            _lockButtons.Add((key, button)); row.Children.Add(button); RefreshLocks();
        }
        return row;
    }
    private void RefreshLocks()
    {
        var locks = _state.EffectiveLocks(_item.LockedFields);
        _lockedCount.Text = $"{locks.Count} locked";
        _lockedCount.Visibility = _item.Type is "movie" or "series" && locks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (field, button) in _lockButtons)
        {
            var locked = locks.Contains(MetadataEditState.LockMap[field]);
            button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children =
                { WebUiIcon.Create(locked ? "lock-keyhole" : "lock-keyhole-open", 12, Brush(locked ? "WarningBrush" : "SecondaryTextBrush")),
                  new TextBlock { Text = locked ? "locked" : "", FontSize = 10, Foreground = Brush(locked ? "WarningBrush" : "SecondaryTextBrush") } } };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, (locked ? "Unlock " : "Lock ") + field);
        }
    }
    private void Text(StackPanel panel, string key, string label)
    {
        var group = new StackPanel { Spacing = 6 }; _fieldGroups[key] = group; group.Children.Add(Label(key, label));
        var input = new TextBox { Text = _state.Fields[key]?.ToString() ?? "", FontSize = 14, Height = 36, MinHeight = 36, Style = (Style)Application.Current.Resources["DarkTextBoxStyle"], CornerRadius = new CornerRadius(10) };
        _textInputs[key] = input;
        if (key == "overview") { input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; input.Height = input.MinHeight = 100; input.Padding = new Thickness(12, 10, 12, 10); input.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent); }
        if (key.EndsWith("_date")) input.PlaceholderText = "YYYY-MM-DD";
        input.TextChanged += (_, _) => { _state.Set(key, input.Text); RefreshLocks(); };
        group.Children.Add(input); panel.Children.Add(group);
    }
    private void Number(StackPanel panel, string key, string label, double min, double max, bool disabled = false)
    {
        var group = new StackPanel { Spacing = 6 }; _fieldGroups[key] = group; group.Children.Add(Label(key, label));
        var input = new NumberBox { Value = _state.Fields[key] is double value ? value : double.NaN, Minimum = min, Maximum = max, IsEnabled = !disabled, Height = 36, MinHeight = 36, CornerRadius = new CornerRadius(10) };
        if (!disabled) _numberInputs[key] = input;
        input.ValueChanged += (_, _) => { _state.Set(key, double.IsNaN(input.Value) ? null : input.Value); RefreshLocks(); };
        group.Children.Add(input); panel.Children.Add(group);
    }
    private void Choice(StackPanel panel, string key, string label, string[] options)
    {
        var input = new ComboBox { Header = label, ItemsSource = options, SelectedItem = _state.Fields[key], HorizontalAlignment = HorizontalAlignment.Stretch };
        _choiceInputs[key] = input;
        input.SelectionChanged += (_, _) => _state.Set(key, input.SelectedItem as string ?? ""); _fieldGroups[key] = input; panel.Children.Add(input);
    }
    private async void Save(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; if (_pending || _imagePending || !AuthorizationPolicy.CanCurateMetadata(App.Services.GetRequiredService<AuthService>())) return;
        // WinUI change notifications can still be queued when Save is invoked.
        // Snapshot the visible form so the submitted draft is the user's latest input.
        foreach (var (key, input) in _textInputs) CaptureVisibleValue(key, input.Text);
        foreach (var (key, input) in _numberInputs) CaptureVisibleValue(key, double.IsNaN(input.Value) ? null : input.Value);
        foreach (var (key, input) in _choiceInputs) CaptureVisibleValue(key, input.SelectedItem as string ?? "");
        foreach (var (key, input) in _tagInputs) { input.CommitPending(); CaptureVisibleValue(key, input.Values); }
        RefreshLocks();
        var changes = _state.Changes(_item.LockedFields);
        foreach (var (key, value) in changes)
            if (key.EndsWith("_date") && value is string date && date.Length > 0 && !DateOnly.TryParseExact(date, "yyyy-MM-dd", out _)) { Toast().Error("Use YYYY-MM-DD for dates."); return; }
        if (changes.Count == 0) { Hide(); return; }
        _pending = true; IsPrimaryButtonEnabled = false; PrimaryButtonText = "Saving...";
        try { await _api.UpdateMetadataAsync(_item.ContentId, changes); HasSaved = true; Changed(); Toast().Success("Metadata saved"); Hide(); }
        catch (Exception ex) { Toast().Error(ex.Message); }
        finally { _pending = false; IsPrimaryButtonEnabled = !_imagePending; PrimaryButtonText = "Save Changes"; }
    }
    private void CaptureVisibleValue(string key, object? value)
    {
        if (JsonSerializer.Serialize(_state.Fields[key]) != JsonSerializer.Serialize(value)) _state.Set(key, value);
    }
    private static string String(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private async Task BuildTranslation()
    {
        try
        {
            var capability = await _api.GetMetadataAiCapabilityAsync(_lifetime.Token);
            if (String(capability, "state") != "available") return;
            var panel = new StackPanel { Spacing = 12 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            heading.Children.Add(WebUiIcon.Create("languages", 16, Brush("SecondaryTextBrush")));
            heading.Children.Add(new TextBlock { Text = "Translate with AI", FontSize = 14, FontWeight = FontWeights.Medium });
            panel.Children.Add(heading);
            panel.Children.Add(new TextBlock { Text = (_item.Type == "series" ? "Translates the overview and tagline plus all season and episode overviews" : _item.Type == "movie" ? "Translates the overview and tagline" : "Translates the overview") + " into the chosen language. Translations are served to libraries using that metadata language; provider data replaces them when it becomes available.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("SecondaryTextBrush") });
            var languages = new ComboBox { Header = "Language", PlaceholderText = "Select…", MinWidth = 150,
                FontSize = 14, Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Bottom };
            foreach (var language in MediaLanguageCatalog.All) languages.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
            var force = new ToggleSwitch { MinHeight = 36, Height = 36, VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(force, "Re-translate existing");
            var forceGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Height = 36, VerticalAlignment = VerticalAlignment.Bottom };
            forceGroup.Children.Add(force);
            forceGroup.Children.Add(new TextBlock { Text = "Re-translate existing", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = "Translate", Height = 32, MinHeight = 32, Padding = new Thickness(12, 0, 12, 0),
                Style = (Style)Application.Current.Resources["AccentButtonStyle"], VerticalAlignment = VerticalAlignment.Bottom };
            var controls = new WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 12 };
            controls.Children.Add(languages); controls.Children.Add(forceGroup); controls.Children.Add(button);
            var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Foreground = Brush("SecondaryTextBrush") };
            panel.Children.Add(controls); panel.Children.Add(status);
            var general = _sections["general"];
            var ratingRow = (UIElement)_fieldGroups["content_rating"].Parent;
            general.Children.Insert(general.Children.IndexOf(ratingRow), new Border { Child = panel, Padding = new Thickness(12), BorderThickness = new Thickness(1), BorderBrush = Brush("BorderBrush"), CornerRadius = new CornerRadius(10), Background = TranslucentBrush("MutedBrush", .3) });
            Reflow();
            button.Click += async (_, _) =>
            {
                if (languages.SelectedItem is not ComboBoxItem selected) { Toast().Error("Pick a target language first."); return; }
                button.IsEnabled = false; languages.IsEnabled = false; force.IsEnabled = false; button.Content = "Translating…";
                try
                {
                    var started = await _api.TranslateMetadataAsync(_item.ContentId, (string)selected.Tag, _item.Type == "series", force.IsOn, _lifetime.Token);
                    var jobId = String(started, "id");
                    while (!_lifetime.IsCancellationRequested)
                    {
                        var response = await _api.GetTranslationJobsAsync(_item.ContentId, _lifetime.Token);
                        var jobs = response.GetProperty("jobs").EnumerateArray().ToArray();
                        var job = jobId.Length > 0 ? jobs.FirstOrDefault(value => String(value, "id") == jobId) : jobs.FirstOrDefault();
                        var state = String(job, "status"); status.Text = String(job, "progress_message"); status.Visibility = Visibility.Visible;
                        if (job.ValueKind == JsonValueKind.Object && job.TryGetProperty("fields_total", out var total) && total.GetInt32() > 0) status.Text += $"… {job.GetProperty("fields_done")}/{total} fields";
                        if (state == "completed")
                        {
                            var fieldsTotal = job.TryGetProperty("fields_total", out var completedTotal) ? completedTotal.GetInt32() : 0;
                            var fieldsDone = job.TryGetProperty("fields_done", out var completedDone) ? completedDone.GetInt32() : 0;
                            HasSaved = true; Changed(); status.Text = ""; status.Visibility = Visibility.Collapsed;
                            Toast().Success(fieldsTotal == 0 ? "Nothing to translate — all descriptions are already localized."
                                : $"Translated {fieldsDone} description{(fieldsDone == 1 ? "" : "s")}.");
                            break;
                        }
                        if (state == "failed") throw new InvalidOperationException(String(job, "error_message"));
                        await Task.Delay(1500, _lifetime.Token);
                    }
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                catch (Exception ex) { Toast().Error(ex.Message); }
                finally { button.IsEnabled = true; languages.IsEnabled = true; force.IsEnabled = true; button.Content = "Translate"; }
            };
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Toast().Error(ex.Message); }
    }
    private void BuildImages(StackPanel panel)
    {
        panel.Spacing = 12;
        var notice = new TextBlock { Text = "Image changes apply immediately and are not affected by Cancel.", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        notice.Foreground = Brush("SecondaryTextBrush");
        panel.Children.Add(new Border { Child = notice, Padding = new Thickness(12, 8, 12, 8), Background = TranslucentBrush("MutedBrush", .5), CornerRadius = new CornerRadius(12) });
        if (_item.Type == "season")
        {
            var hint = new Grid { ColumnSpacing = 8 };
            hint.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hint.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hint.Children.Add(WebUiIcon.Create("circle-alert", 14, Brush("WarningBrush")));
            var copy = new TextBlock { Text = "Only seeing one poster? Check for plugin updates and update TMDB and TVDB to load full season artwork galleries.",
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brush("WarningBrush") };
            Grid.SetColumn(copy, 1); hint.Children.Add(copy);
            panel.Children.Add(new Border { Child = hint, Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), BorderBrush = TranslucentBrush("WarningBrush", .2), Background = TranslucentBrush("WarningBrush", .05) });
        }
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var textless = new ToggleButton { Content = "Textless", FontSize = 11, FontWeight = FontWeights.Medium, Height = 30, MinHeight = 0,
            Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = Brush("SecondaryTextBrush"), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Right };
        var tabRow = new Grid(); tabRow.Children.Add(tabs); tabRow.Children.Add(textless); panel.Children.Add(tabRow);
        var warnings = new TextBlock { Foreground = Brush("WarningBrush"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed }; panel.Children.Add(warnings);
        var grid = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8, Margin = new Thickness(0, 0, 12, 4) }; panel.Children.Add(grid);
        var apply = new Button { Content = "Apply Poster", IsEnabled = false, Visibility = Visibility.Collapsed, Height = 32, MinHeight = 32,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(apply);
        List<JsonElement>? images = null; JsonElement current = default, selection = default; var type = "poster"; var applied = new Dictionary<string, string>();
        var cards = new List<(Button Button, Image Picture, Border Selection)>();
        void ResizeImages()
        {
            var narrow = (XamlRoot?.Size.Width ?? 1100) < 640;
            var columns = type == "poster" ? narrow ? 3 : 4 : narrow ? 2 : 3;
            var available = _root.Width - (narrow ? 0 : 160) - _scroll.Padding.Left - _scroll.Padding.Right - 12;
            // WrapPanel receives rounded DesiredSize values. Round the card budget
            // down in physical pixels so the fourth card cannot exceed that row.
            var scale = XamlRoot?.RasterizationScale ?? 1;
            var cardWidth = Math.Max(24, Math.Floor((available - (columns - 1) * 8) / columns * scale) / scale);
            foreach (var (card, picture, _) in cards)
            {
                card.Width = cardWidth;
                picture.Width = cardWidth - 2; picture.Height = picture.Width * (type == "poster" ? 1.5 : 9d / 16);
            }
        }
        _resizeImages = ResizeImages;
        void SelectionPresentation()
        {
            foreach (var (card, _, badge) in cards)
            {
                var original = (string)card.Tag;
                var selected = selection.ValueKind == JsonValueKind.Object && String(selection, "original_url") == original;
                var isCurrent = original == (applied.GetValueOrDefault(type) ?? String(current, type + "_url"));
                card.BorderBrush = selected ? Brush("AccentBrush") : isCurrent ? TranslucentBrush("AccentBrush", .5) : Brush("BorderBrush");
                badge.Visibility = selected && !isCurrent ? Visibility.Visible : Visibility.Collapsed;
            }
            apply.Visibility = selection.ValueKind == JsonValueKind.Object ? Visibility.Visible : Visibility.Collapsed;
            apply.IsEnabled = !_imagePending && selection.ValueKind == JsonValueKind.Object;
            apply.Content = _imagePending ? "Applying..." : "Apply " + char.ToUpperInvariant(type[0]) + type[1..];
        }
        void Render()
        {
            grid.Children.Clear(); cards.Clear(); selection = default; apply.IsEnabled = false;
            var choices = images?.Where(image => String(image, "type") == type && (textless.IsChecked != true || type == "logo" || String(image, "language").Length == 0)).ToArray() ?? [];
            foreach (var tab in tabs.Children.OfType<Button>()) tab.Style = (Style)Application.Current.Resources[Equals(tab.Tag, type) ? "AccentButtonStyle" : "GhostButtonStyle"];
            if (choices.Length == 0) grid.Children.Add(new TextBlock { Text = textless.IsChecked == true ? "No textless images available." : "No images available.", FontSize = 14 });
            foreach (var image in choices)
            {
                var picture = new Image { Stretch = Stretch.UniformToFill };
                if (String(image, "url") is { Length: > 0 } url) picture.Source = (ImageSource)new UrlToImageSourceConverter().Convert(url, typeof(ImageSource), null!, "");
                var body = new Grid(); body.Children.Add(picture);
                var original = String(image, "original_url"); var isCurrent = original == (applied.GetValueOrDefault(type) ?? String(current, type + "_url"));
                if (isCurrent) body.Children.Add(new Border { Margin = new Thickness(4), Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4),
                    Background = TranslucentBrush("AccentBrush", .9), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = "Current", FontSize = 9, Foreground = Brush("AccentForegroundBrush"), FontWeight = FontWeights.SemiBold } });
                var selectionBadge = new Border { Tag = "selection", Width = 20, Height = 20, Margin = new Thickness(4), CornerRadius = new CornerRadius(10),
                    Background = Brush("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed,
                    Child = WebUiIcon.Create("check", 12, Brush("AccentForegroundBrush")) };
                body.Children.Add(selectionBadge);
                body.Children.Add(new Border { Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(4), CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = .6 }, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Child = new TextBlock { Text = String(image, "provider_id").ToUpperInvariant(), FontSize = 9, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) } });
                var button = new Button { Content = body, Tag = original, Padding = new Thickness(0), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
                cards.Add((button, picture, selectionBadge));
                button.Click += (_, _) => { if (_imagePending) return; selection = selection.ValueKind == JsonValueKind.Object && String(selection, "original_url") == original ? default : image;
                    SelectionPresentation(); Reflow(); };
                grid.Children.Add(button);
            }
            ResizeImages(); SelectionPresentation(); if (XamlRoot is not null) Reflow();
        }
        foreach (var imageType in _item.Type == "season" ? new[] { "poster" } : new[] { "poster", "backdrop", "logo" })
        {
            var button = new Button { Content = imageType switch { "poster" => "Posters", "backdrop" => "Backdrops", _ => "Logos" }, Tag = imageType, FontSize = 12, FontWeight = FontWeights.Medium,
                Height = 30, MinHeight = 0, Padding = new Thickness(12, 6, 12, 6), Style = (Style)Application.Current.Resources[imageType == "poster" ? "AccentButtonStyle" : "GhostButtonStyle"] };
            button.Click += (_, _) => { type = imageType; textless.IsChecked = false; textless.Visibility = type == "logo" ? Visibility.Collapsed : Visibility.Visible; Render(); }; tabs.Children.Add(button);
        }
        textless.Click += (_, _) => Render();
        apply.Click += async (_, _) =>
        {
            if (_imagePending || selection.ValueKind != JsonValueKind.Object || !AuthorizationPolicy.IsActingAdmin(App.Services.GetRequiredService<AuthService>())) return;
            var selected = selection; var selectedType = String(selected, "type"); _imagePending = true; apply.IsEnabled = false; IsPrimaryButtonEnabled = false; apply.Content = "Applying...";
            try { await _api.ApplyImageAsync(_item.ContentId, String(selected, "original_url"), selectedType, String(selected, "provider_id"));
                applied[selectedType] = String(selected, "original_url"); _state.SetLock(10, true); HasSaved = true; Changed();
                _item = await App.Services.GetRequiredService<CatalogApi>().GetItemDetailAsync(_item.ContentId); RefreshLocks(); Render(); Toast().Success("Image applied successfully"); }
            catch (Exception ex) { Toast().Error(ex.Message); }
            finally { _imagePending = false; IsPrimaryButtonEnabled = !_pending; SelectionPresentation(); }
        };
        Opened += async (_, _) =>
        {
            grid.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24 });
            try { var result = await _api.GetImagesAsync(_item.ContentId, _lifetime.Token); images = result.Items; current = result.Current; warnings.Text = result.Errors.Count > 0 ? "Could not load from " + string.Join(", ", result.Errors.Keys.Select(key => key.ToUpperInvariant())) : ""; warnings.Visibility = result.Errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed; Render(); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch { grid.Children.Clear(); grid.Children.Add(new TextBlock { Text = "Failed to load images.", FontSize = 14 }); }
        };
    }
}
