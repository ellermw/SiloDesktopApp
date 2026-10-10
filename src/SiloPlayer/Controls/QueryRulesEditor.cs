using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>Shared native group editor. Loading controls never coerces saved values.</summary>
public sealed partial class QueryRulesEditor : UserControl
{
    private readonly StackPanel _body = new() { Spacing = 16 };
    private UIElementCollection Children => _body.Children;
    public bool ExtendedRules { get; set; }
    public bool CollectionPresentation { get; set; }
    public TextBox? CollectionLimitInput { get; set; }
    public IReadOnlySet<string>? ShownRatingSources { get; set; }
    public bool AllowPersonalizedFilters { get; set; } = true;
    public bool AllowPersonalizedSorts { get; set; }
    public string? OnlyPersonalizedSort { get; set; }
    public event Action? SortChanged;
    private ComboBox? _sortChoice;
    private string? _mediaScope;
    public Func<CancellationToken, Task<QueryEditorCapabilities>>? CapabilitiesLoader { get; set; }
    private CancellationTokenSource? _capabilityOwner;
    private bool _refreshingChoices;
    private readonly List<(ComboBox Choice, string Field)> _fieldChoices = [];
    private readonly List<(ComboBox Choice, string Field, string Operator)> _operatorChoices = [];
    private readonly HashSet<FrameworkElement> _invalid = [];
    public QueryDefinition Query { get; private set; } = new();
    public bool IsValid => _invalid.Count == 0;
    public event Action? Changed;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Options { get; set; } = new Dictionary<string, IReadOnlyList<string>>();
    public Func<string, string, CancellationToken, Task<IReadOnlyList<(string Id, string Name)>>>? PeopleSearch { get; set; }
    public Func<string, string, CancellationToken, Task<IReadOnlyList<string>>>? FacetSearch { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<string>>>? LanguageLoader { get; set; }
    public void ConfigureCatalogScope(string? mediaScope = null, int? libraryId = null)
    {
        _mediaScope = mediaScope;
        PeopleSearch = async (_, text, ct) => (await App.Services.GetRequiredService<PeopleApi>().SearchScopedAsync(text, mediaScope, 20, ct)).Select(person => (person.Id, person.Name)).ToList();
        FacetSearch = (field, text, ct) => App.Services.GetRequiredService<CatalogApi>().SearchFacetAsync(field, text, libraryId, mediaScope, ct);
        LanguageLoader = async (field, ct) =>
        {
            var values = await App.Services.GetRequiredService<CatalogApi>().GetRuleLanguagesAsync(libraryId, mediaScope, field != "original_language", ct);
            return field switch { "original_language" => values.OriginalLanguages, "audio_language" => values.AudioLanguages, _ => values.SubtitleLanguages };
        };
        _capabilityOwner?.Cancel(); _capabilityOwner = null;
        if (IsLoaded) _ = RefreshCapabilitiesAsync();
    }
    private sealed record Option(string Label, string Value) { public override string ToString() => Label; }
    internal static string Label(string value) => value switch
    {
        "rating_imdb" => "IMDb rating", "content_rating" => "Content rating", "original_language" => "Original language",
        "audio_language" => "Audio language", "added_at" => "Date added", "release_date" => "Release date",
        "in_watchlist" => "In watchlist", "in_progress" => "In progress", "hdr" => "HDR", "dolby_vision" => "Dolby Vision",
        "is" => "is", "is_not" => "is not", "gte" => "at least", "lte" => "at most", "gt" => "greater than",
        "lt" => "less than", "in_last" => "in the last", "all" => "All", "any" => "Any", "true" => "Yes", "false" => "No",
        "not_in_last" => "is not in the last", "not_contains" => "does not contain", "begins_with" => "begins with", "ends_with" => "ends with",
        "d" => "days", "w" => "weeks", "m" => "months", "y" => "years", "h" => "hours",
        "asc" => "Ascending", "desc" => "Descending",
        _ => QueryFieldCatalog.Get(value)?.Label ?? (string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].Replace('_', ' '))
    };
    private static string Selected(ComboBox choice) => (choice.SelectedItem as Option)?.Value ?? (choice.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    public QueryRulesEditor()
    {
        Content = _body;
        CapabilitiesLoader = ct => App.Services.GetRequiredService<CatalogApi>().GetQueryEditorCapabilitiesAsync(ct);
        Loaded += (_, _) => { if (_capabilityOwner == null) _ = RefreshCapabilitiesAsync(); };
        Unloaded += (_, _) => { _capabilityOwner?.Cancel(); _capabilityOwner = null; };
    }

    private async Task RefreshCapabilitiesAsync()
    {
        if (CapabilitiesLoader == null) return;
        var owner = new CancellationTokenSource(); _capabilityOwner = owner;
        try
        {
            var capability = await CapabilitiesLoader(owner.Token);
            if (_capabilityOwner != owner || owner.IsCancellationRequested || !IsLoaded) return;
            ExtendedRules = capability.ExtendedRules; ShownRatingSources = capability.ShownRatingSources;
            // Updating option lists must not rebuild value inputs or discard an in-progress edit.
            _refreshingChoices = true;
            try
            {
                foreach (var (choice, field) in _fieldChoices) PopulateFieldChoice(choice, field);
                foreach (var (choice, field, op) in _operatorChoices) PopulateChoices(choice, QueryFieldCatalog.OfferedOperators(field, op, ExtendedRules), op);
                if (_sortChoice != null) PopulateSortChoice(_sortChoice);
            }
            finally { _refreshingChoices = false; }
        }
        catch (OperationCanceledException) { }
        catch { /* Older/unavailable capabilities leave new fields hidden; saved rules remain. */ }
        finally { if (_capabilityOwner == owner) _capabilityOwner = null; owner.Dispose(); }
    }

    public void Load(QueryDefinition query)
    { if (PeopleSearch == null) ConfigureCatalogScope(); Query = query; Rebuild(); }

    private void Rebuild()
    {
        Children.Clear(); _invalid.Clear(); _fieldChoices.Clear(); _operatorChoices.Clear();
        if (CollectionPresentation) { BuildCollectionPresentation(); return; }
        var match = Choice(["all", "any"], Query.Match, "Match groups");
        match.Width = 80; match.Height = 36; match.FontSize = 14;
        match.SelectionChanged += (_, _) => { Query.Match = Selected(match); Changed?.Invoke(); };
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(new TextBlock { Text = "Rule Groups", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
        heading.Children.Add(MatchLine(match, "of the following groups"));
        Children.Add(heading);
        foreach (var group in Query.Groups)
        {
            var body = new StackPanel { Spacing = 8 };
            var header = new Grid { ColumnSpacing = 8 };
            header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var groupMatch = Choice(["all", "any"], group.Match, "Match rules in this group");
            groupMatch.Width = 80; groupMatch.Height = 28; groupMatch.FontSize = 12;
            groupMatch.SelectionChanged += (_, _) => { group.Match = Selected(groupMatch); Changed?.Invoke(); };
            header.Children.Add(MatchLine(groupMatch, "rules"));
            var removeGroup = RemoveButton("Remove group");
            removeGroup.Click += (_, _) => { Query.Groups.Remove(group); Rebuild(); Changed?.Invoke(); };
            Grid.SetColumn(removeGroup, 1); header.Children.Add(removeGroup); body.Children.Add(header);
            foreach (var rule in group.Rules) body.Children.Add(BuildRule(group, rule));
            var add = AddButton("Add Rule", true);
            add.Click += (_, _) => { group.Rules.Add(new() { Field = "genre", Op = "is", Value = "" }); Rebuild(); Changed?.Invoke(); };
            body.Children.Add(add);
            Children.Add(new Border { Child = body, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = (Brush)Application.Current.Resources["BorderBrush"] });
        }
        var addGroup = AddButton("Add Group", false);
        addGroup.Click += (_, _) => { Query.Groups.Add(new() { Rules = [new() { Field = "genre", Op = "is", Value = "" }] }); Rebuild(); Changed?.Invoke(); };
        Children.Add(addGroup);
        Children.Add(BuildSortControls());
    }

    private static readonly (string Field, string Label)[] SortFields = [
        ("title", "Title"), ("added_at", "Date Added"), ("release_date", "Release Date"),
        ("last_air_date", "Latest Episode Air Date"), ("latest_episode_added", "Latest Episode Added"),
        ("year", "Year"), ("content_rating", "Content Rating"), ("runtime", "Duration"),
        ("rating_imdb", "IMDb Rating"), ("rating_tmdb", "TMDB Rating"), ("rating_rt_critic", "RT Critic Rating"),
        ("rating_rt_audience", "RT Audience Rating"), ("resolution", "Resolution"), ("bitrate", "Bitrate"),
        ("progress", "Progress"), ("date_viewed", "Date Viewed"), ("plays", "Plays"),
        ("author", "Author"), ("narrator", "Narrator"), ("series", "Series") ];

    private void PopulateSortChoice(ComboBox choice)
    {
        var selected = Query.Sort?.Field ?? (CollectionPresentation ? "added_at" : "title");
        var options = SortFields.Where(sort => CatalogRatingSortPolicy.AppliesToScope(sort.Field, _mediaScope ?? "all")
            && (ShownRatingSources == null || CatalogRatingSortPolicy.IsAvailable(sort.Field, ShownRatingSources, selected))
            && (sort.Field is not ("progress" or "date_viewed" or "plays") || AllowPersonalizedSorts || sort.Field == OnlyPersonalizedSort))
            .Select(sort => new Option(_mediaScope is "ebook" or "manga" ? sort.Field switch { "date_viewed" => "Date Read", "plays" => "Reads", _ => sort.Label } : sort.Label, sort.Field)).ToList();
        // Rendering a saved sort must not silently rewrite it, including future fields.
        if (!options.Any(option => option.Value == selected)) options.Add(new(Label(selected), selected));
        choice.ItemsSource = options; choice.SelectedItem = options.First(option => option.Value == selected);
    }

    private FrameworkElement BuildSortControls()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = "Sort by", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        var choice = _sortChoice = new ComboBox { Width = 128, Height = 36, MinHeight = 0, MinWidth = 0, FontSize = 12 };
        PopulateSortChoice(choice); AutomationProperties.SetName(choice, "Sort by");
        var direction = Choice(["desc", "asc"], Query.Sort?.Order ?? "desc", "Direction"); direction.Width = 112; direction.Height = 36;
        var updating = false;
        choice.SelectionChanged += (_, _) =>
        {
            if (_refreshingChoices || updating) return;
            var field = Selected(choice);
            Query.Sort = field.Length == 0 ? null : new() { Field = field, Order = field is "title" or "content_rating" or "author" or "narrator" or "series" ? "asc" : "desc" };
            updating = true; try { PopulateChoices(direction, ["desc", "asc"], Query.Sort?.Order ?? "desc"); } finally { updating = false; }
            direction.Visibility = Query.Sort == null ? Visibility.Collapsed : Visibility.Visible;
            SortChanged?.Invoke(); Changed?.Invoke();
        };
        direction.SelectionChanged += (_, _) => { if (updating || _refreshingChoices) return; Query.Sort ??= new() { Field = Selected(choice) }; Query.Sort.Order = Selected(direction); SortChanged?.Invoke(); Changed?.Invoke(); };
        row.Children.Add(choice); row.Children.Add(direction);
        return new Border { Child = row, BorderThickness = new(0, 1, 0, 0), BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], Padding = new(0, 8, 0, 0) };
    }

    private FrameworkElement BuildRule(QueryGroup group, QueryRule rule)
    {
        var isArray = rule.Value is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array }
            || rule.Value is System.Collections.IEnumerable and not string;
        var rangeLength = rule.Value switch
        {
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } json => json.GetArrayLength(),
            System.Collections.IEnumerable values when rule.Value is not string => values.Cast<object?>().Count(),
            _ => 0
        };
        var booleanValue = rule.Value is bool || rule.Value is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False };
        var definition = QueryFieldCatalog.Get(rule.Field);
        if (definition == null || !definition.Operators.Contains(rule.Op) || !AllowPersonalizedFilters && definition.Group == "You"
            || rule.Op == "between" && (!isArray || rangeLength != 2)
            || QueryRuleValues.IsBoolean(rule.Field) && !booleanValue)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"Not editable here  {rule.Field} {rule.Op} {System.Text.Json.JsonSerializer.Serialize(rule.Value)}", FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var removeOpaque = RemoveButton("Remove rule");
            removeOpaque.Click += (_, _) => { group.Rules.Remove(rule); Rebuild(); Changed?.Invoke(); };
            Grid.SetColumn(removeOpaque, 1); row.Children.Add(removeOpaque);
            var readonlyRule = new Border { Child = row, Padding = new(8, 4, 8, 4), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = (Brush)Application.Current.Resources["BorderBrush"] };
            AutomationProperties.SetName(readonlyRule, "Rule not editable here");
            return readonlyRule;
        }
        var body = new StackPanel { Spacing = 6 };
        var selectors = new Grid { ColumnSpacing = 8, Height = CollectionPresentation ? double.NaN : 32 };
        if (CollectionPresentation) for (var n = 0; n < 3; n++) selectors.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var fieldColumn = new ColumnDefinition { Width = new GridLength(144) };
        var operatorColumn = new ColumnDefinition { Width = new GridLength(96) };
        selectors.ColumnDefinitions.Add(fieldColumn);
        selectors.ColumnDefinitions.Add(operatorColumn);
        selectors.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        selectors.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        selectors.SizeChanged += (_, args) =>
        {
            if (CollectionPresentation)
            {
                var wide = args.NewSize.Width >= 532;
                var pair = args.NewSize.Width >= 344;
                selectors.RowSpacing = wide ? 0 : 8;
                fieldColumn.Width = new(wide || pair ? 192 : 1, wide || pair ? GridUnitType.Pixel : GridUnitType.Star);
                operatorColumn.Width = new(wide || pair ? 144 : 0);
                foreach (var child in selectors.Children.OfType<FrameworkElement>())
                {
                    var column = Grid.GetColumn(child);
                    // Keep each control's original semantic column while rows wrap.
                    if (child.Tag is not int original) { original = column; child.Tag = original; }
                    Grid.SetColumn(child, !wide && (original == 2 || !pair && original < 2) ? 0 : original);
                    Grid.SetColumnSpan(child, !wide && (!pair && original < 2 || original == 2) ? 3 : 1);
                    Grid.SetRow(child, wide ? 0 : pair ? (original < 2 ? 0 : 1) : original == 0 ? 0 : original == 1 ? 1 : 2);
                }
                return;
            }
            var flexibleWidth = Math.Max(0, args.NewSize.Width - 52);
            fieldColumn.Width = new GridLength(Math.Min(144, flexibleWidth * .36));
            operatorColumn.Width = new GridLength(Math.Min(96, flexibleWidth * .24));
        };
        void AddValue(FrameworkElement value)
        {
            if (definition.Unit is {} unit)
            {
                var withUnit = new Grid { ColumnSpacing = 8 };
                withUnit.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                withUnit.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                withUnit.Children.Add(value);
                var label = new TextBlock { Text = unit, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
                Grid.SetColumn(label, 1); withUnit.Children.Add(label); value = withUnit;
            }
            value.MinWidth = 0; value.Height = CollectionPresentation ? 36 : 32; value.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (value is Control control) { control.MinHeight = 0; control.FontSize = CollectionPresentation ? 14 : 12; }
            Grid.SetColumn(value, 2); selectors.Children.Add(value);
        }
        var field = FieldChoice(rule.Field);
        if (CollectionPresentation) { field.Height = 36; field.FontSize = 14; }
        field.SelectionChanged += (_, _) => { if (_refreshingChoices) return; rule.Field = Selected(field); rule.Op = QueryFieldCatalog.Get(rule.Field)!.Operators[0]; rule.Value = QueryRuleValues.IsBoolean(rule.Field) ? false : QueryRuleValues.IsNumeric(rule.Field) ? 0 : ""; Rebuild(); Changed?.Invoke(); };
        selectors.Children.Add(field);
        var op = Choice(QueryFieldCatalog.OfferedOperators(rule.Field, rule.Op, ExtendedRules), rule.Op, "Rule operator");
        _operatorChoices.Add((op, rule.Field, rule.Op));
        op.SelectionChanged += (_, _) => { if (_refreshingChoices) return; rule.Op = Selected(op); rule.Value = QueryRuleValues.ForOperator(rule.Field, rule.Op, rule.Value); Rebuild(); Changed?.Invoke(); };
        Grid.SetColumn(op, 1); selectors.Children.Add(op);
        if (CollectionPresentation) { op.Height = 36; op.FontSize = 14; }
        var remove = RemoveButton("Remove rule");
        remove.Click += (_, _) => { group.Rules.Remove(rule); Rebuild(); Changed?.Invoke(); };
        Grid.SetColumn(remove, 3); selectors.Children.Add(remove); body.Children.Add(selectors);
        if (rule.Op is "exists" or "not_exists") return body;
        if (QueryRuleValues.IsBoolean(rule.Field) && rule.Op is not ("between" or "in" or "not_in"))
        {
            var boolean = Choice(["true", "false"], QueryRuleValues.Format(rule.Value), "Rule value");
            boolean.SelectionChanged += (_, _) => { rule.Value = Selected(boolean) == "true"; Changed?.Invoke(); };
            AddValue(boolean); return body;
        }
        if (rule.Op == "between")
        {
            var parts = QueryRuleValues.Format(rule.Value).Split(',', StringSplitOptions.TrimEntries);
            if (QueryRuleValues.IsDate(rule.Field))
            {
                var range = new Grid { ColumnSpacing = 8 };
                range.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); range.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                var dates = new[] { parts.ElementAtOrDefault(0) ?? "", parts.ElementAtOrDefault(1) ?? "" };
                void ValidateDates() { if (dates.Any(string.IsNullOrWhiteSpace)) _invalid.Add(range); else _invalid.Remove(range); }
                for (var index = 0; index < 2; index++)
                {
                    var endpoint = index;
                    var date = DateInput(dates[index], index == 0 ? "From" : "To", text => { dates[endpoint] = text; rule.Value = dates.ToArray(); ValidateDates(); Changed?.Invoke(); });
                    Grid.SetColumn(date, index); range.Children.Add(date);
                }
                ValidateDates(); AddValue(range); return body;
            }
            var from = new TextBox { Height = 32, MinHeight = 0, MinWidth = 0, FontSize = 12, Padding = new Thickness(8, 4, 8, 4), Text = parts.ElementAtOrDefault(0) ?? "", PlaceholderText = "From" };
            var to = new TextBox { Height = 32, MinHeight = 0, MinWidth = 0, FontSize = 12, Padding = new Thickness(8, 4, 8, 4), Text = parts.ElementAtOrDefault(1) ?? "", PlaceholderText = "To" };
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(to, 1); row.Children.Add(from); row.Children.Add(to);
            var rangeError = new TextBlock { Foreground = (Brush)Application.Current.Resources["ErrorBrush"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            void Validate(bool save)
            {
                try { var parsed = QueryRuleValues.Parse(rule.Field, rule.Op, from.Text + "," + to.Text); if (save) rule.Value = parsed; _invalid.Remove(from); rangeError.Visibility = Visibility.Collapsed; }
                catch (FormatException ex) { _invalid.Add(from); rangeError.Text = ex.Message; rangeError.Visibility = Visibility.Visible; }
                if (save) Changed?.Invoke();
            }
            from.TextChanged += (_, _) => Validate(true); to.TextChanged += (_, _) => Validate(true);
            Validate(false);
            AddValue(row); body.Children.Add(rangeError); return body;
        }
        if (QueryRuleValues.IsDate(rule.Field))
        {
            var text = QueryRuleValues.Format(rule.Value);
            if (rule.Op is "in_last" or "not_in_last")
            {
                var parsed = System.Text.RegularExpressions.Regex.Match(text, @"^\s*(\d+)\s*([hdwmy])\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (text.Trim().Length == 0 || parsed.Success)
                {
                    var row = new Grid { ColumnSpacing = 8 };
                    row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(96) });
                    var amount = new TextBox { Text = parsed.Success ? parsed.Groups[1].Value : "", PlaceholderText = "Amount", MinWidth = 0, Height = 32, MinHeight = 0, FontSize = 12 };
                    AutomationProperties.SetName(amount, "Relative date amount");
                    var currentUnit = parsed.Success ? parsed.Groups[2].Value.ToLowerInvariant() : "d";
                    var units = Choice(currentUnit == "h" ? ["h", "d", "w", "m", "y"] : ["d", "w", "m", "y"], currentUnit, "Relative date unit");
                    void WriteSpan() { rule.Value = long.TryParse(amount.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) && count >= 1 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) + Selected(units) : ""; Changed?.Invoke(); }
                    amount.TextChanged += (_, _) => WriteSpan(); units.SelectionChanged += (_, _) => WriteSpan();
                    Grid.SetColumn(units, 1); row.Children.Add(amount); row.Children.Add(units); AddValue(row); return body;
                }
                // Nonstandard saved spans remain editable text without coercion.
            }
            else { AddValue(DateInput(text, "Rule value", value => { rule.Value = value; Changed?.Invoke(); })); return body; }
        }
        if (definition.Kind == "select")
        {
            var values = rule.Field switch
            {
                "type" => new[] { "movie", "series" }, "status" => ["pending", "matched", "unmatched"],
                "resolution" => ["480p", "720p", "1080p", "2160p", "4320p"],
                "decade" => Enumerable.Range(190, DateTime.Today.Year / 10 - 189).Reverse().Select(decade => (decade * 10).ToString()).ToArray(),
                _ => Array.Empty<string>()
            };
            var choice = Choice(values, QueryRuleValues.Format(rule.Value), "Rule value");
            choice.SelectionChanged += (_, _) => { rule.Value = QueryRuleValues.IsNumeric(rule.Field) ? QueryRuleValues.Parse(rule.Field, rule.Op, Selected(choice)) : Selected(choice); Changed?.Invoke(); };
            AddValue(choice); return body;
        }
        if (rule.Field is "actor" or "director" or "writer" or "producer" && PeopleSearch != null)
        {
            var search = new AutoSuggestBox { Text = QueryRuleValues.Format(rule.Value), PlaceholderText = "Search people", DisplayMemberPath = "Name" };
            CancellationTokenSource? owner = null;
            search.TextChanged += async (_, args) =>
            {
                if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
                owner?.Cancel(); var current = owner = new CancellationTokenSource();
                try { await Task.Delay(300, current.Token); var people = await PeopleSearch(rule.Field, search.Text, current.Token); if (owner == current && !current.IsCancellationRequested) search.ItemsSource = people.Select(person => new PersonOption(person.Id, person.Name)).ToList(); }
                catch (OperationCanceledException) { } catch { if (owner == current) search.ItemsSource = null; }
            };
            search.SuggestionChosen += (_, args) => { if (args.SelectedItem is PersonOption person) { rule.Value = person.Name; search.Text = person.Name; Changed?.Invoke(); } };
            search.Unloaded += (_, _) => owner?.Cancel();
            AddValue(search); return body;
        }
        if (definition.Kind == "language")
        {
            var selected = QueryRuleValues.Format(rule.Value);
            var choice = new ComboBox { Height = 32, MinHeight = 0, MinWidth = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Pick a language" };
            AutomationProperties.SetName(choice, "Rule language");
            var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], Visibility = Visibility.Collapsed };
            var populating = false;
            void Populate(IEnumerable<string> codes)
            {
                populating = true;
                try
                {
                    var options = codes.Concat(string.IsNullOrEmpty(selected) ? [] : new[] { selected }).Distinct()
                        .Select(code => new Option(LanguageName(code), code)).OrderBy(option => option.Label).ToArray();
                    choice.ItemsSource = options; choice.SelectedItem = options.FirstOrDefault(option => option.Value == selected);
                    status.Text = options.Length == 0 ? "No languages available" : ""; status.Visibility = status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                }
                finally { populating = false; }
            }
            Populate(Options.TryGetValue(rule.Field, out var existing) ? existing : []);
            choice.SelectionChanged += (_, _) => { if (!populating) { selected = Selected(choice); rule.Value = selected; Changed?.Invoke(); } };
            CancellationTokenSource? owner = null;
            async Task Refresh()
            {
                if (LanguageLoader == null || owner != null) return;
                var current = owner = new CancellationTokenSource(); choice.PlaceholderText = "Loading languages";
                try { var codes = await LanguageLoader(rule.Field, current.Token); if (owner == current && !current.IsCancellationRequested) { Populate(codes); choice.PlaceholderText = "Pick a language"; } }
                catch (OperationCanceledException) { }
                catch { if (owner == current) { status.Text = choice.Items.Count > 0 ? "Couldn’t refresh languages" : "Couldn’t load languages"; status.Visibility = Visibility.Visible; choice.PlaceholderText = "Couldn’t load languages"; } }
                finally { if (owner == current) owner = null; current.Dispose(); }
            }
            WhenVisible(choice, Refresh); choice.DropDownOpened += async (_, _) => await Refresh();
            choice.Unloaded += (_, _) => { owner?.Cancel(); owner = null; };
            AddValue(choice); body.Children.Add(status); return body;
        }
        if (definition.Kind == "facet" && FacetSearch != null)
        {
            var search = new AutoSuggestBox { Text = QueryRuleValues.Format(rule.Value), PlaceholderText = "Search " + Label(rule.Field).ToLowerInvariant() };
            var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], Visibility = Visibility.Collapsed };
            CancellationTokenSource? owner = null;
            async Task Refresh(string text, bool debounce)
            {
                owner?.Cancel(); var current = owner = new CancellationTokenSource();
                try { if (debounce) await Task.Delay(300, current.Token); var values = await FacetSearch(rule.Field, text, current.Token); if (owner == current && !current.IsCancellationRequested) { search.ItemsSource = values; status.Visibility = Visibility.Collapsed; } }
                catch (OperationCanceledException) { }
                catch { if (owner == current) { status.Text = "Couldn’t load values. Try again."; status.Visibility = Visibility.Visible; } }
                finally { if (owner == current) owner = null; current.Dispose(); }
            }
            search.TextChanged += async (_, args) =>
            {
                if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
                await Refresh(search.Text, true);
            };
            WhenVisible(search, () => Refresh("", false));
            search.GotFocus += async (_, _) => await Refresh("", false);
            search.SuggestionChosen += (_, args) => { rule.Value = args.SelectedItem?.ToString() ?? ""; search.Text = rule.Value.ToString(); Changed?.Invoke(); };
            search.Unloaded += (_, _) => { owner?.Cancel(); owner = null; };
            AddValue(search); body.Children.Add(status); return body;
        }
        if (rule.Field == "type" || Options.ContainsKey(rule.Field))
        {
            var values = rule.Field == "type" ? new[] { "movie", "series", "episode", "audiobook", "ebook", "manga" } : Options[rule.Field];
            var search = new AutoSuggestBox { Text = QueryRuleValues.Format(rule.Value), PlaceholderText = "Search " + Label(rule.Field).ToLowerInvariant() };
            search.TextChanged += (_, args) => { if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) search.ItemsSource = values.Where(value => value.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).Take(30).ToArray(); };
            search.SuggestionChosen += (_, args) => { rule.Value = args.SelectedItem?.ToString() ?? ""; Changed?.Invoke(); };
            search.QuerySubmitted += (_, args) => { if (values.Contains(args.QueryText)) { rule.Value = args.QueryText; Changed?.Invoke(); } };
            AddValue(search); return body;
        }
        var input = new TextBox { Text = QueryRuleValues.Format(rule.Value), PlaceholderText = rule.Op is "between" or "in" or "not_in" ? "Values separated by commas" : "Value" };
        AutomationProperties.SetName(input, "Rule value");
        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["ErrorBrush"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        input.TextChanged += (_, _) =>
        {
            try { rule.Value = QueryRuleValues.Parse(rule.Field, rule.Op, input.Text); _invalid.Remove(input); error.Visibility = Visibility.Collapsed; }
            catch (FormatException ex) { _invalid.Add(input); error.Text = ex.Message; error.Visibility = Visibility.Visible; }
            Changed?.Invoke();
        };
        AddValue(input); body.Children.Add(error); return body;
    }

    private static StackPanel MatchLine(ComboBox selector, string suffix)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(new TextBlock { Text = "Match", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        line.Children.Add(selector);
        line.Children.Add(new TextBlock { Text = suffix, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        return line;
    }
    private static Button RemoveButton(string name)
    {
        var icon = (Viewbox)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Viewbox xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='14' Height='14'><Canvas Width='24' Height='24'><Path Data='M3 6h18 M19 6v14a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6 M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2 M10 10v7 M14 10v7' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/></Canvas></Viewbox>");
        ((Microsoft.UI.Xaml.Shapes.Path)((Canvas)icon.Child).Children[0]).Stroke = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        var button = new Button { Content = icon, Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        AutomationProperties.SetName(button, name); return button;
    }
    private static Button AddButton(string label, bool ghost)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(WebUiIcon.Create("plus", ghost ? 12 : 14)); content.Children.Add(new TextBlock { Text = label, FontSize = ghost ? 12 : 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium });
        return new Button { Content = content, Height = ghost ? 28 : 32, MinHeight = 0, Padding = new Thickness(ghost ? 8 : 12, 4, ghost ? 8 : 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)Application.Current.Resources[ghost ? "GhostButtonStyle" : "OutlineButtonStyle"] };
    }

    private sealed record PersonOption(string Id, string Name);

    private static string LanguageName(string code)
    {
        try { return System.Globalization.CultureInfo.GetCultureInfo(code).EnglishName; }
        catch (System.Globalization.CultureNotFoundException) { return code; }
    }

    private void WhenVisible(FrameworkElement input, Func<Task> load)
    {
        long? token = null;
        async Task Refresh() { if (Visibility == Visibility.Visible && IsLoaded && input.IsLoaded) await load(); }
        input.Loaded += async (_, _) =>
        {
            token ??= RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => { if (Visibility == Visibility.Visible) _ = Refresh(); });
            await Refresh();
        };
        input.Unloaded += (_, _) => { if (token is {} registration) UnregisterPropertyChangedCallback(VisibilityProperty, registration); token = null; };
    }

    private static FrameworkElement DateInput(string value, string name, Action<string> changed)
    {
        if (value.Length == 0 || DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
        {
            var date = new CalendarDatePicker { MinWidth = 0, Height = 32, MinHeight = 0, FontSize = 12, PlaceholderText = name };
            if (value.Length > 0) date.Date = new DateTimeOffset(DateTime.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            AutomationProperties.SetName(date, name);
            date.DateChanged += (_, _) => changed(date.Date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "");
            return date;
        }
        var input = new TextBox { Text = value, MinWidth = 0, Height = 32, MinHeight = 0, FontSize = 12 };
        AutomationProperties.SetName(input, name); input.TextChanged += (_, _) => changed(input.Text); return input;
    }

    private ComboBox FieldChoice(string selected)
    {
        var choice = new ComboBox { MinWidth = 0, MinHeight = 0, Height = 32, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        PopulateFieldChoice(choice, selected); _fieldChoices.Add((choice, selected));
        AutomationProperties.SetName(choice, "Rule field"); return choice;
    }

    private void PopulateFieldChoice(ComboBox choice, string selected)
    {
        choice.Items.Clear();
        var definition = QueryFieldCatalog.Get(selected)!;
        var fields = QueryFieldCatalog.Offered(_mediaScope, ExtendedRules, ShownRatingSources, AllowPersonalizedFilters).ToList();
        if (!fields.Any(field => field.Name == definition.Name)) fields.Add(definition);
        foreach (var group in new[] { "Title", "People", "File", "You", "Library" })
        {
            var members = fields.Where(field => field.Group == group).ToList(); if (members.Count == 0) continue;
            choice.Items.Add(new ComboBoxItem { Content = group, IsEnabled = false, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
            foreach (var field in members)
            {
                var label = _mediaScope is "ebook" or "manga" ? field.Name switch { "watched" => "Read", "last_watched" => "Last read", _ => field.Label } : field.Label;
                var item = new ComboBoxItem { Content = label, Tag = field.Name == definition.Name ? selected : field.Name };
                choice.Items.Add(item); if (field.Name == definition.Name) choice.SelectedItem = item;
            }
        }
    }

    private static ComboBox Choice(IEnumerable<string> values, string selected, string name)
    {
        var control = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, MinHeight = 0, Height = 32, FontSize = 12 };
        PopulateChoices(control, values, selected);
        AutomationProperties.SetName(control, name); return control;
    }

    private static void PopulateChoices(ComboBox control, IEnumerable<string> values, string selected)
    {
        var options = values.Concat(string.IsNullOrWhiteSpace(selected) ? [] : new[] { selected }).Distinct().ToArray();
        var items = options.Select(value => new Option(Label(value), value)).ToArray();
        control.ItemsSource = items; control.SelectedItem = items.FirstOrDefault(item => item.Value == selected);
    }
}
