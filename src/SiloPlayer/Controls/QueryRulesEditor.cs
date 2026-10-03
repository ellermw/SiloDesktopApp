using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>Shared native group editor. Loading controls never coerces saved values.</summary>
public sealed class QueryRulesEditor : UserControl
{
    private readonly StackPanel _body = new() { Spacing = 12 };
    private UIElementCollection Children => _body.Children;
    private static readonly string[] Fields = ["genre", "year", "rating_imdb", "type", "content_rating", "original_language", "actor", "director", "writer", "producer", "author", "narrator", "series", "studio", "network", "country", "status", "resolution", "audio_language", "hdr", "dolby_vision", "bitrate", "added_at", "release_date", "watched", "favorited", "in_watchlist", "in_progress"];
    private static string[] Operators(string field) => QueryRuleValues.IsBoolean(field) ? ["is"]
        : QueryRuleValues.IsNumeric(field) ? ["is", "gte", "lte", "gt", "lt", "between"]
        : field is "added_at" or "release_date" ? ["gt", "lt", "between", "in_last"]
        : field == "genre" ? ["is", "is_not", "contains"] : ["is", "is_not"];
    private readonly HashSet<TextBox> _invalid = [];
    public QueryDefinition Query { get; private set; } = new();
    public bool IsValid => _invalid.Count == 0;
    public event Action? Changed;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Options { get; set; } = new Dictionary<string, IReadOnlyList<string>>();
    public Func<string, string, CancellationToken, Task<IReadOnlyList<(string Id, string Name)>>>? PeopleSearch { get; set; }
    public Func<string, string, CancellationToken, Task<IReadOnlyList<string>>>? FacetSearch { get; set; }
    public void ConfigureCatalogScope(string? mediaScope = null, int? libraryId = null)
    {
        PeopleSearch = async (_, text, ct) => (await App.Services.GetRequiredService<PeopleApi>().SearchScopedAsync(text, mediaScope, 20, ct)).Select(person => (person.Id, person.Name)).ToList();
        FacetSearch = (field, text, ct) => App.Services.GetRequiredService<CatalogApi>().SearchFacetAsync(field, text, libraryId, mediaScope, ct);
    }
    private sealed record Option(string Label, string Value) { public override string ToString() => Label; }
    internal static string Label(string value) => value switch
    {
        "rating_imdb" => "IMDb rating", "content_rating" => "Content rating", "original_language" => "Original language",
        "audio_language" => "Audio language", "added_at" => "Date added", "release_date" => "Release date",
        "in_watchlist" => "In watchlist", "in_progress" => "In progress", "hdr" => "HDR", "dolby_vision" => "Dolby Vision",
        "is" => "is", "is_not" => "is not", "gte" => "at least", "lte" => "at most", "gt" => "greater than",
        "lt" => "less than", "in_last" => "in the last", "all" => "All", "any" => "Any", "true" => "Yes", "false" => "No",
        _ => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].Replace('_', ' ')
    };
    private static string Selected(ComboBox choice) => (choice.SelectedItem as Option)?.Value ?? "";

    public QueryRulesEditor() => Content = _body;

    public void Load(QueryDefinition query)
    { if (PeopleSearch == null) ConfigureCatalogScope(); Query = query; Rebuild(); }

    private void Rebuild()
    {
        Children.Clear(); _invalid.Clear();
        var match = Choice(["all", "any"], Query.Match, "Match groups");
        match.Width = 80; match.Height = 28; match.FontSize = 12;
        match.SelectionChanged += (_, _) => { Query.Match = Selected(match); Changed?.Invoke(); };
        Children.Add(MatchLine(match, "of the following groups"));
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
    }

    private FrameworkElement BuildRule(QueryGroup group, QueryRule rule)
    {
        var body = new StackPanel { Spacing = 6 };
        var selectors = new Grid { ColumnSpacing = 8, Height = 32 };
        var fieldColumn = new ColumnDefinition { Width = new GridLength(144) };
        var operatorColumn = new ColumnDefinition { Width = new GridLength(96) };
        selectors.ColumnDefinitions.Add(fieldColumn);
        selectors.ColumnDefinitions.Add(operatorColumn);
        selectors.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        selectors.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        selectors.SizeChanged += (_, args) =>
        {
            var flexibleWidth = Math.Max(0, args.NewSize.Width - 52);
            fieldColumn.Width = new GridLength(Math.Min(144, flexibleWidth * .36));
            operatorColumn.Width = new GridLength(Math.Min(96, flexibleWidth * .24));
        };
        void AddValue(FrameworkElement value)
        {
            value.MinWidth = 0; value.Height = 32; value.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (value is Control control) { control.MinHeight = 0; control.FontSize = 12; }
            Grid.SetColumn(value, 2); selectors.Children.Add(value);
        }
        var field = Choice(Fields, rule.Field, "Rule field");
        field.SelectionChanged += (_, _) => { rule.Field = Selected(field); rule.Op = Operators(rule.Field)[0]; rule.Value = QueryRuleValues.IsBoolean(rule.Field) ? false : ""; Rebuild(); Changed?.Invoke(); };
        selectors.Children.Add(field);
        var op = Choice(Operators(rule.Field), rule.Op, "Rule operator");
        op.SelectionChanged += (_, _) => { rule.Op = Selected(op); Rebuild(); Changed?.Invoke(); };
        Grid.SetColumn(op, 1); selectors.Children.Add(op);
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
            var from = new TextBox { Height = 32, MinHeight = 0, MinWidth = 0, FontSize = 12, Padding = new Thickness(8, 4, 8, 4), Text = parts.ElementAtOrDefault(0) ?? "", PlaceholderText = "From" };
            var to = new TextBox { Height = 32, MinHeight = 0, MinWidth = 0, FontSize = 12, Padding = new Thickness(8, 4, 8, 4), Text = parts.ElementAtOrDefault(1) ?? "", PlaceholderText = "To" };
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(to, 1); row.Children.Add(from); row.Children.Add(to);
            var rangeError = new TextBlock { Foreground = (Brush)Application.Current.Resources["ErrorBrush"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            void Validate()
            {
                try { rule.Value = QueryRuleValues.Parse(rule.Field, rule.Op, from.Text + "," + to.Text); _invalid.Remove(from); rangeError.Visibility = Visibility.Collapsed; }
                catch (FormatException ex) { _invalid.Add(from); rangeError.Text = ex.Message; rangeError.Visibility = Visibility.Visible; }
                Changed?.Invoke();
            }
            from.TextChanged += (_, _) => Validate(); to.TextChanged += (_, _) => Validate();
            AddValue(row); body.Children.Add(rangeError); return body;
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
        if (rule.Field is "author" or "narrator" or "series" && FacetSearch != null)
        {
            var search = new AutoSuggestBox { Text = QueryRuleValues.Format(rule.Value), PlaceholderText = "Search " + Label(rule.Field).ToLowerInvariant() };
            CancellationTokenSource? owner = null;
            search.TextChanged += async (_, args) =>
            {
                if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
                owner?.Cancel(); var current = owner = new CancellationTokenSource();
                try { await Task.Delay(300, current.Token); var values = await FacetSearch(rule.Field, search.Text, current.Token); if (owner == current && !current.IsCancellationRequested) search.ItemsSource = values; }
                catch (OperationCanceledException) { } catch { if (owner == current) search.ItemsSource = null; }
            };
            search.SuggestionChosen += (_, args) => { rule.Value = args.SelectedItem?.ToString() ?? ""; Changed?.Invoke(); };
            search.Unloaded += (_, _) => owner?.Cancel();
            AddValue(search); return body;
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
        content.Children.Add(WebUiIcon.Create("plus", 12)); content.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        return new Button { Content = content, Height = ghost ? 28 : 32, MinHeight = 0, Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)Application.Current.Resources[ghost ? "GhostButtonStyle" : "OutlineButtonStyle"] };
    }

    private sealed record PersonOption(string Id, string Name);

    private static ComboBox Choice(IEnumerable<string> values, string selected, string name)
    {
        var options = values.Concat(string.IsNullOrWhiteSpace(selected) ? [] : new[] { selected }).Distinct().ToArray();
        var items = options.Select(value => new Option(Label(value), value)).ToArray();
        var control = new ComboBox { ItemsSource = items, SelectedItem = items.FirstOrDefault(item => item.Value == selected), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, MinHeight = 0, Height = 32, FontSize = 12 };
        AutomationProperties.SetName(control, name); return control;
    }
}
