using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>Guided and grouped editors share the same draft; switching modes never rebuilds its query.</summary>
public sealed class QueryFilterEditor : UserControl
{
    private readonly StackPanel _body = new() { Spacing = 12 };
    private readonly StackPanel _guided = new() { Spacing = 16 };
    private readonly QueryRulesEditor _advanced = new();
    private readonly List<(Grid Row, int DesktopColumns)> _guidedRows = [];
    private readonly List<NumberBox> _guidedNumbers = [];
    private XamlRoot? _observedRoot;
    private readonly DataTemplate _guidedLabelTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding}' FontSize='14' FontWeight='Medium' LineHeight='14' LineStackingStrategy='BlockLineHeight' /></DataTemplate>");
    private string? _scope;
    private int? _libraryId;
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _options = new Dictionary<string, IReadOnlyList<string>>();
    public QueryDefinition Query { get; private set; } = new();
    public bool IsValid => _advanced.IsValid;
    public event Action? Changed;
    public event Action? SortChanged;
    public void ConfigureSort(bool personalized = true, string? onlyPersonalized = null)
    {
        _advanced.AllowPersonalizedSorts = personalized; _advanced.OnlyPersonalizedSort = onlyPersonalized;
    }
    private bool _advancedMode;
    private readonly StackPanel _modes = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Button _guidedButton = new() { Content = "Guided", Height = 24, MinHeight = 0, MinWidth = 0, FontSize = 12, Padding = new(8, 0, 8, 0) };
    private readonly Button _advancedButton = new() { Content = "Advanced", Height = 24, MinHeight = 0, MinWidth = 0, FontSize = 12, Padding = new(8, 0, 8, 0) };
    private readonly TextBlock _guidedUnavailable = new() { Text = GuidedQuerySupport.UnavailableMessage, FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

    public FrameworkElement DetachModeSelector()
    {
        _body.Children.Remove(_modes);
        return _modes;
    }

    public QueryFilterEditor()
    {
        Content = _body;
        _guidedButton.Click += (_, _) => ShowMode(false); _advancedButton.Click += (_, _) => ShowMode(true);
        _modes.Children.Add(_guidedButton); _modes.Children.Add(_advancedButton); _body.Children.Add(_modes);
        _guidedUnavailable.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        _body.Children.Add(_guidedUnavailable); _body.Children.Add(_guided); _body.Children.Add(_advanced);
        _advanced.Changed += () => { UpdateGuidedAvailability(); Changed?.Invoke(); };
        _advanced.SortChanged += () => SortChanged?.Invoke();
        // These overrides belong only to Guided's source bg-background inputs.
        var background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundFocused", "TextControlBackgroundPointerOver", "ComboBoxBackground", "ComboBoxBackgroundFocused", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed" })
            _guided.Resources[key] = background;
        Loaded += (_, _) => ObserveViewport();
        Unloaded += (_, _) => { if (_observedRoot != null) _observedRoot.Changed -= ViewportChanged; _observedRoot = null; };
    }

    public void Load(QueryDefinition query, string? scope = null, int? libraryId = null, CatalogFiltersResponse? filters = null)
    {
        if (_scope != scope || _libraryId != libraryId) _options = new Dictionary<string, IReadOnlyList<string>>();
        Query = query; _scope = scope; _libraryId = libraryId;
        if (filters != null) SetOptions(filters);
        _advanced.ConfigureCatalogScope(scope, libraryId); _advanced.Options = _options; _advanced.Load(query);
        UpdateGuidedAvailability(); BuildGuided(); ShowMode(_advancedMode);
    }

    private void SetOptions(CatalogFiltersResponse filters)
    {
        _options = new Dictionary<string, IReadOnlyList<string>>
        {
            ["genre"] = filters.Genres, ["content_rating"] = filters.ContentRatings, ["country"] = filters.Countries,
            ["studio"] = filters.Studios, ["network"] = filters.Networks, ["resolution"] = filters.Resolutions,
            ["audio_language"] = filters.AudioLanguages, ["original_language"] = filters.OriginalLanguages,
            ["subtitle_language"] = filters.SubtitleLanguages,
        };
    }

    public void UpdateOptions(CatalogFiltersResponse filters)
    {
        SetOptions(filters);
        _advanced.Options = _options;
        foreach (var input in Descendants<AutoSuggestBox>(_guided))
        {
            if (input.Tag is not string field || !_options.TryGetValue(field, out var values) ||
                input.FocusState == FocusState.Unfocused && !input.IsSuggestionListOpen) continue;
            input.ItemsSource = values.Where(value => value.Contains(input.Text, StringComparison.OrdinalIgnoreCase)).ToList();
            input.IsSuggestionListOpen = values.Count > 0;
        }
    }

    private void ShowMode(bool advanced)
    {
        if (!advanced && !_guidedButton.IsEnabled) return;
        _advancedMode = advanced;
        _guidedButton.Style = (Style)Application.Current.Resources[advanced ? "OutlineButtonStyle" : "AccentButtonStyle"];
        _advancedButton.Style = (Style)Application.Current.Resources[advanced ? "AccentButtonStyle" : "OutlineButtonStyle"];
        if (!advanced) BuildGuided();
        _guided.Visibility = advanced ? Visibility.Collapsed : Visibility.Visible;
        _advanced.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateGuidedAvailability()
    {
        var available = GuidedQuerySupport.CanEdit(Query, _scope);
        _guidedButton.IsEnabled = available;
        _guidedUnavailable.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        if (!available) ShowMode(true);
    }

    private void BuildGuided()
    {
        _guidedRows.Clear();
        _guidedNumbers.Clear();
        _guided.Children.Clear();
        if (!GuidedQuerySupport.CanEdit(Query, _scope))
        {
            _guided.Children.Add(new TextBlock { Text = "This query uses rule groups. Open Advanced to edit them; switching modes keeps every rule.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
            return;
        }
        var book = _scope is "audiobook" or "audiobooks" or "ebook" or "ebooks" or "manga";
        var audiobook = _scope is "audiobook" or "audiobooks";
        AddTextField("Genres", "genre", "Select genres...");
        var genreInput = _guided.Children[_guided.Children.Count - 1];
        _guided.Children.RemoveAt(_guided.Children.Count - 1);
        var genres = new StackPanel { Spacing = 8 };
        genres.Children.Add(genreInput);
        genres.Children.Add(new TextBlock { Text = "Items must match all selected genres.", FontSize = 12, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        _guided.Children.Add(genres);
        var decades = new[] { "Custom" }.Concat(Enumerable.Range(190, 14).Reverse().Select(value => $"{value * 10}s")).ToArray();
        var from = ReadYear("gte"); var to = ReadYear("lte");
        var matchingDecade = int.TryParse(from, out var firstYear) && int.TryParse(to, out var lastYear) && firstYear % 10 == 0 && lastYear == firstYear + 9 ? Array.IndexOf(decades, $"{firstYear}s") : 0;
        var decade = new ComboBox { Header = "Decade", HeaderTemplate = _guidedLabelTemplate, Height = 58, MinHeight = 0, ItemsSource = decades, SelectedIndex = Math.Max(0, matchingDecade), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 0 };
        StyleGuidedControl(decade);
        decade.SelectionChanged += (_, _) =>
        {
            if (decade.SelectedIndex <= 0) { SetRule("year", "gte", null); SetRule("year", "lte", null); }
            else { var year = int.Parse(decade.SelectedItem.ToString()![..4]); SetRule("year", "gte", year); SetRule("year", "lte", year + 9); }
            BuildGuided();
        };
        _guided.Children.Add(decade);
        AddNumber("Year From", "year", "gte", "e.g. 2000"); AddNumber("Year To", "year", "lte", "e.g. 2025");
        GroupLastFields(3, 3);
        if (!book) { AddNumber("Minimum IMDb Rating", "rating_imdb", "gte", "e.g. 7.0"); AddTextField("Content Rating", "content_rating", "Select rating..."); GroupLastFields(2, 2); }
        AddTextField("Original Language", "original_language", "Select languages...");
        if (!book)
        {
            foreach (var pair in new[] { new[] { "actor", "director" }, new[] { "writer", "producer" } })
            {
                foreach (var field in pair) AddTextField(QueryRulesEditor.Label(field), field, "Search people...");
                GroupLastFields(2, 2);
            }
            AddTextField("Studio", "studio", "Select studio..."); AddTextField("Network", "network", "Select network...");
            GroupLastFields(2, 2);
        }
        else
        {
            AddTextField("Author", "author", "Search authors...");
            if (audiobook) AddTextField("Narrator", "narrator", "Search narrators...");
            GroupLastFields(audiobook ? 2 : 1, 2);
            AddTextField("Series", "series", "Search series...");
            GroupLastFields(1, 2);
        }
        AddTextField("Country", "country", "Select country...");
        GroupLastFields(1, 2);
        AddChoice("Match Status", new[] { "Any", "Matched", "Unmatched", "Pending" }, Current("status", "is"), value => SetRule("status", "is", value == "Any" ? null : value.ToLowerInvariant()));
        var progressLabel = book ? audiobook ? "Listening Status" : "Read Status" : "Watch Status";
        var completed = book ? audiobook ? "Listened" : "Read" : "Watched";
        var unstarted = book ? audiobook ? "Unlistened" : "Unread" : "Unwatched";
        var progress = Current("in_progress", "is") == "true" ? "In Progress" : Current("watched", "is") is var watched && watched == "true" ? completed : watched == "false" ? unstarted : "Any";
        AddChoice(progressLabel, new[] { "Any", completed, unstarted, "In Progress" }, progress, value =>
        {
            SetRule("watched", "is", value == completed ? true : value == unstarted ? false : null);
            SetRule("in_progress", "is", value == "In Progress" ? true : value == unstarted ? false : null);
        });
        GroupLastFields(2, 2);
        if (!book)
        {
            var quality = new StackPanel { Spacing = 8 };
            quality.Children.Add(new TextBlock { Text = "Video Quality", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; quality.Children.Add(buttons);
            foreach (var (label, field, value) in new (string, string, object)[] { ("4K", "resolution", "4k"), ("HDR", "hdr", true), ("DOVI", "dolby_vision", true) })
            {
                var button = new ToggleButton { Content = label, Height = 32, MinHeight = 0, Padding = new(12, 0, 12, 0), IsChecked = field == "resolution" ? Current(field, "is") is "4k" or "2160p" : Current(field, "is") == "true" };
                button.Click += (_, _) => SetRule(field, "is", button.IsChecked == true ? field == "resolution" ? "2160p" : value : null); buttons.Children.Add(button);
            }
            _guided.Children.Add(quality);
        }
        foreach (var (label, field, placeholder) in new[] { ("Added in the Last", "added_at", "e.g. 30d, 2w, 6m"), ("Released in the Last", "release_date", "e.g. 90d, 1y") })
        {
            var input = new TextBox { Header = label, HeaderTemplate = _guidedLabelTemplate, Height = 58, MinHeight = 0, PlaceholderText = placeholder, Text = Current(field, "in_last") };
            StyleGuidedControl(input);
            input.TextChanged += (_, _) => SetRule(field, "in_last", string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim()); _guided.Children.Add(input);
        }
        GroupLastFields(2, 2);
        UpdateGuidedRows();
    }

    private void GroupLastFields(int count, int desktopColumns)
    {
        var first = _guided.Children.Count - count;
        var fields = _guided.Children.Skip(first).ToArray();
        foreach (var field in fields) _guided.Children.Remove(field);
        var row = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        foreach (var field in fields) row.Children.Add(field);
        _guidedRows.Add((row, desktopColumns));
        _guided.Children.Add(row);
    }

    private void ObserveViewport()
    {
        if (_observedRoot != XamlRoot)
        {
            if (_observedRoot != null) _observedRoot.Changed -= ViewportChanged;
            _observedRoot = XamlRoot;
            if (_observedRoot != null) _observedRoot.Changed += ViewportChanged;
        }
        UpdateGuidedRows();
    }

    private void ViewportChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateGuidedRows();

    private void UpdateGuidedRows()
    {
        // CSS md applies to the viewport, even when the filter sheet is narrow.
        var desktop = (XamlRoot?.Size.Width ?? ActualWidth) >= 768;
        foreach (var number in _guidedNumbers)
        {
            number.FontSize = desktop ? 14 : 16;
            foreach (var text in Descendants<TextBox>(number)) text.FontSize = number.FontSize;
        }
        foreach (var (row, desktopColumns) in _guidedRows)
        {
            var columns = desktop ? desktopColumns : 1;
            if (row.ColumnDefinitions.Count == columns) continue;
            row.ColumnDefinitions.Clear(); row.RowDefinitions.Clear();
            for (var column = 0; column < columns; column++) row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            for (var index = 0; index < row.Children.Count; index++)
            {
                if (index % columns == 0) row.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetColumn((FrameworkElement)row.Children[index], index % columns);
                Grid.SetRow((FrameworkElement)row.Children[index], index / columns);
            }
        }
    }

    private string Current(string field, string op) => QueryRuleValues.Format(Query.Groups.SelectMany(group => group.Rules).FirstOrDefault(rule => rule.Field == field && rule.Op == op)?.Value);

    private string ReadYear(string op)
    {
        var direct = Current("year", op); if (direct.Length > 0) return direct;
        var equal = Current("year", "is"); if (equal.Length > 0) return equal;
        var between = Current("year", "between").Split(',', StringSplitOptions.TrimEntries);
        return between.Length == 2 ? between[op == "gte" ? 0 : 1] : "";
    }

    private void AddChoice(string label, string[] values, string selected, Action<string> update)
    {
        var index = Array.FindIndex(values, value => value.Equals(selected, StringComparison.OrdinalIgnoreCase));
        var input = new ComboBox { Header = label, HeaderTemplate = _guidedLabelTemplate, Height = 58, MinHeight = 0, MinWidth = 0, ItemsSource = values, SelectedIndex = Math.Max(0, index), HorizontalAlignment = HorizontalAlignment.Left };
        StyleGuidedControl(input);
        input.SelectionChanged += (_, _) => update(input.SelectedItem?.ToString() ?? values[0]); _guided.Children.Add(input);
    }

    private void AddTextField(string label, string field, string placeholder)
    {
        // WinUI includes the 14px header and its 8px bottom margin in the
        // control's height. Allocate the remaining 36px to the input body.
        var input = new AutoSuggestBox { Tag = field, Height = 58, MinHeight = 0, Header = new TextBlock { Text = label, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight }, PlaceholderText = placeholder, Text = string.Join(", ", Query.Groups.SelectMany(group => group.Rules).Where(rule => rule.Field == field).Select(rule => QueryRuleValues.Format(rule.Value))) };
        StyleGuidedControl(input);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, label);
        var scope = _scope; var libraryId = _libraryId;
        CancellationTokenSource? owner = null;
        string? selectedSuggestionText = null;
        void OpenAvailableChoices()
        {
            if (_scope != scope || _libraryId != libraryId || !_options.TryGetValue(field, out var values)) return;
            owner?.Cancel();
            // Bind strings to WinUI's virtualized suggestion list. Do not build
            // thousands of option controls or commit a rule just to open it.
            input.ItemsSource = values;
            input.IsSuggestionListOpen = values.Count > 0;
        }
        input.GotFocus += (_, args) =>
        {
            if (args.OriginalSource is TextBox && !input.IsSuggestionListOpen) OpenAvailableChoices();
        };
        input.TextChanged += async (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            selectedSuggestionText = null;
            owner?.Cancel(); var active = owner = new CancellationTokenSource();
            try
            {
                await Task.Delay(300, active.Token);
                IReadOnlyList<string> matches;
                if (field is "actor" or "director" or "writer" or "producer") matches = (await App.Services.GetRequiredService<PeopleApi>().SearchScopedAsync(input.Text, scope, 20, active.Token)).Select(person => person.Name).ToList();
                else if (field is "author" or "narrator" or "series") matches = await App.Services.GetRequiredService<CatalogApi>().SearchFacetAsync(field, input.Text, libraryId, scope, active.Token);
                else matches = _options.TryGetValue(field, out var values) ? values.Where(value => value.Contains(input.Text, StringComparison.OrdinalIgnoreCase)).Take(30).ToList() : [];
                if (active == owner && !active.IsCancellationRequested && _scope == scope && _libraryId == libraryId) input.ItemsSource = matches;
            }
            catch (OperationCanceledException) { } catch { if (active == owner) input.ItemsSource = null; }
            finally { if (active == owner) owner = null; active.Dispose(); }
        };
        input.SuggestionChosen += (_, args) =>
        {
            var value = args.SelectedItem?.ToString() ?? "";
            if (field is "genre" or "original_language")
            {
                var selected = Query.Groups.SelectMany(group => group.Rules).Where(rule => rule.Field == field).Select(rule => QueryRuleValues.Format(rule.Value)).ToList();
                if (!selected.Contains(value)) selected.Add(value); SetMulti(field, selected); input.Text = string.Join(", ", selected);
            }
            else SetRule(field, "is", value);
            selectedSuggestionText = input.Text;
        };
        input.QuerySubmitted += (_, args) =>
        {
            // Updating the selected-list text inside SuggestionChosen can make
            // WinUI lose ChosenSuggestion/QueryText for the same Enter press.
            // A subsequent real edit resets this guard, including manual clear.
            var completingSelection = selectedSuggestionText != null && input.Text == selectedSuggestionText && string.IsNullOrWhiteSpace(args.QueryText);
            selectedSuggestionText = null;
            if (args.ChosenSuggestion != null || completingSelection) return;
            if (field is "genre" or "original_language") SetMulti(field, args.QueryText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            else SetRule(field, "is", string.IsNullOrWhiteSpace(args.QueryText) ? null : args.QueryText.Trim());
        };
        input.Unloaded += (_, _) => owner?.Cancel();
        var fieldHost = new Grid();
        fieldHost.Children.Add(input);
        var openChoices = new Button { Width = 32, Height = 36, MinWidth = 0, MinHeight = 0, Margin = new(0, 22, 0, 0), Padding = new(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), Content = new FontIcon { Glyph = "\uE70D", FontSize = 10, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(openChoices, $"Show {label} choices");
        openChoices.Click += (_, _) => { input.Focus(FocusState.Programmatic); OpenAvailableChoices(); };
        fieldHost.Children.Add(openChoices);
        _guided.Children.Add(fieldHost);
    }
    private void AddNumber(string label, string field, string op, string placeholder)
    {
        var value = field == "year" ? ReadYear(op) : Current(field, op);
        var input = new NumberBox { Header = label, HeaderTemplate = _guidedLabelTemplate, PlaceholderText = placeholder, Value = double.TryParse(QueryRuleValues.Format(value), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, SmallChange = field == "rating_imdb" ? .1 : 1, Minimum = 0, Maximum = field == "rating_imdb" ? 10 : 9999 };
        _guidedNumbers.Add(input);
        StyleGuidedControl(input);
        input.ValueChanged += (_, args) => SetRule(field, op, double.IsNaN(args.NewValue) ? null : args.NewValue);
        _guided.Children.Add(input);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private void StyleGuidedControl(Control input)
    {
        input.CornerRadius = new CornerRadius(10);
        input.Background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
        input.Loaded += (_, _) =>
        {
            foreach (var text in Descendants<TextBox>(input))
            {
                text.CornerRadius = input.CornerRadius;
                text.Background = input.Background;
                if (input is NumberBox) text.FontSize = input.FontSize;
            }
            UpdateGuidedRows();
        };
    }
    private void SetMulti(string field, IReadOnlyList<string> values)
    {
        foreach (var group in Query.Groups) group.Rules.RemoveAll(rule => rule.Field == field);
        if (Query.Groups.Count == 0) Query.Groups.Add(new());
        if (!Query.Groups.Any(group => group.Match == "all")) Query.Groups.Insert(0, new());
        var target = field == "original_language" && values.Count > 1 ? new QueryGroup { Match = "any" } : Query.Groups.First(group => group.Match == "all");
        foreach (var value in values) target.Rules.Add(new() { Field = field, Op = "is", Value = value });
        if (!Query.Groups.Contains(target)) Query.Groups.Add(target);
        Query.Groups.RemoveAll(group => group.Rules.Count == 0);
        _advanced.Load(Query); Changed?.Invoke();
    }
    private void SetRule(string field, string op, object? value)
    {
        if (Query.Groups.Count == 0) Query.Groups.Add(new());
        if (!Query.Groups.Any(group => group.Match == "all")) Query.Groups.Insert(0, new());
        var rules = Query.Groups.First(group => group.Match == "all").Rules;
        if (field == "year")
        {
            var from = ReadYear("gte"); var to = ReadYear("lte");
            rules.RemoveAll(rule => rule.Field == "year" && rule.Op is "between" or "is");
            if (!rules.Any(rule => rule.Field == "year" && rule.Op == "gte") && from.Length > 0) rules.Add(new() { Field = "year", Op = "gte", Value = double.Parse(from, System.Globalization.CultureInfo.InvariantCulture) });
            if (!rules.Any(rule => rule.Field == "year" && rule.Op == "lte") && to.Length > 0) rules.Add(new() { Field = "year", Op = "lte", Value = double.Parse(to, System.Globalization.CultureInfo.InvariantCulture) });
        }
        rules.RemoveAll(rule => rule.Field == field && (field != "year" || rule.Op == op));
        if (value != null) rules.Add(new() { Field = field, Op = op, Value = value });
        Query.Groups.RemoveAll(group => group.Rules.Count == 0);
        _advanced.Load(Query); Changed?.Invoke();
    }
}
