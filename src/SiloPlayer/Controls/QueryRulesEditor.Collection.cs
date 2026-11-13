using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Helpers;

namespace SiloPlayer.Controls;

public sealed partial class QueryRulesEditor
{
    private Grid? _collectionLimitOwner;
    public void ReleaseCollectionLimitInput()
    {
        if (_collectionLimitOwner != null && CollectionLimitInput != null) _collectionLimitOwner.Children.Remove(CollectionLimitInput);
        _collectionLimitOwner = null;
    }
    private static Brush CollectionBrush(string key) => (Brush)Application.Current.Resources[key];

    private void BuildCollectionPresentation()
    {
        ReleaseCollectionLimitInput();
        _body.Spacing = 12;
        if (Query.Groups.Count > 0)
        {
            var match = CollectionMatchChoice(Query.Groups[0].Match, CollectionText.Get("web.home.addRow.rules.howRulesCombine", "How the rules combine"));
            match.Width = 80; match.Height = 36; match.FontSize = 14;
            match.SelectionChanged += (_, _) => { Query.Groups[0].Match = Selected(match); Changed?.Invoke(); };
            var sentence = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            sentence.Children.Add(new TextBlock { Text = "that match", FontSize = 15, VerticalAlignment = VerticalAlignment.Center }); sentence.Children.Add(match);
            sentence.Children.Add(new TextBlock { Text = "of these:", FontSize = 15, VerticalAlignment = VerticalAlignment.Center }); Children.Add(sentence);
        }
        var body = new StackPanel { Spacing = 10 };
        if (Query.Groups.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = "No rules yet, so the collection holds every title from these libraries.", FontSize = 14, LineHeight = 21, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap, Foreground = CollectionBrush("SecondaryTextBrush") });
            var add = CollectionAddButton(CollectionText.Get("web.home.addRow.rules.addRule", "Add rule"));
            add.Click += (_, _) => { Query.Groups.Add(new() { Rules = [new() { Field = "genre", Op = "is", Value = "" }] }); Rebuild(); Changed?.Invoke(); };
            body.Children.Add(add);
        }
        for (var index = 0; index < Query.Groups.Count; index++)
        {
            var group = Query.Groups[index];
            var groupBody = new StackPanel { Spacing = 8 };
            if (index > 0)
            {
                var join = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
                var combine = new ComboBox { MinWidth = 0, MinHeight = 0, Height = 36, FontSize = 14 };
                combine.Items.Add(new Option(CollectionText.Get("web.home.addRow.rules.joinAnd", "and"), "all")); combine.Items.Add(new Option(CollectionText.Get("web.home.addRow.rules.joinOr", "or"), "any"));
                var savedMatch = combine.Items.Cast<Option>().FirstOrDefault(o => o.Value == Query.Match);
                // Keep unknown saved matches visible without coercing the query on load.
                if (savedMatch == null) combine.Items.Add(savedMatch = new Option($"Saved match ({Query.Match})", Query.Match));
                combine.SelectedItem = savedMatch;
                AutomationProperties.SetName(combine, CollectionText.Get("web.home.addRow.rules.howGroupsCombine", "How the groups combine")); combine.SelectionChanged += (_, _) => { Query.Match = Selected(combine); Changed?.Invoke(); };
                join.Children.Add(combine); join.Children.Add(new TextBlock { Text = "titles that match", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                var match = CollectionMatchChoice(group.Match, CollectionText.Get("web.home.addRow.rules.groupMatchLabel", $"How group {index + 1}'s rules combine", ("number", CollectionText.Number(index + 1)))); match.Height = 36; match.FontSize = 14;
                match.SelectionChanged += (_, _) => { group.Match = Selected(match); Changed?.Invoke(); }; join.Children.Add(match);
                join.Children.Add(new TextBlock { Text = "of these:", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                var remove = RemoveButton(CollectionText.Get("web.home.addRow.rules.removeGroup", $"Remove group {index + 1}", ("number", CollectionText.Number(index + 1)))); remove.Click += (_, _) => { Query.Groups.Remove(group); Rebuild(); Changed?.Invoke(); }; join.Children.Add(remove);
                groupBody.Children.Add(new Border { BorderThickness = new(0, 1, 0, 0), BorderBrush = CollectionBrush("BorderBrush"), Padding = new(0, 12, 0, 0), Child = join });
            }
            foreach (var rule in group.Rules) groupBody.Children.Add(BuildRule(group, rule));
            var actions = new WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 4 };
            var add = CollectionAddButton(CollectionText.Get("web.home.addRow.rules.addRule", "Add rule"));
            add.Click += (_, _) => { group.Rules.Add(new() { Field = "genre", Op = "is", Value = "" }); Rebuild(); Changed?.Invoke(); }; actions.Children.Add(add);
            if (index == Query.Groups.Count - 1)
            {
                var more = CollectionAddButton(Query.Groups.Count == 1 || Query.Match == "any" ? CollectionText.Get("web.home.addRow.rules.addOrGroup", "Add an “or” group") : CollectionText.Get("web.home.addRow.rules.addAndGroup", "Add an “and” group"));
                more.Click += (_, _) => { if (Query.Groups.Count == 1) Query.Match = "any"; Query.Groups.Add(new() { Rules = [new() { Field = "genre", Op = "is", Value = "" }] }); Rebuild(); Changed?.Invoke(); }; actions.Children.Add(more);
            }
            groupBody.Children.Add(actions); body.Children.Add(groupBody);
        }
        var panel = new Border { Child = body, Padding = new(16), BorderBrush = CollectionBrush("BorderBrush"), BorderThickness = new(1), CornerRadius = new(16) }; Children.Add(panel);
        panel.SizeChanged += (_, _) => panel.Padding = new((XamlRoot?.Size.Width ?? 1280) < 640 ? 12 : 16);
        Children.Add(new TextBlock { Text = CollectionText.Get("web.collections.copy.personalRulesNote", "“All my libraries” follows the libraries this profile can see. Rules about you, like Watched, are offered only here."), FontSize = 12.5, Foreground = CollectionBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        Children.Add(BuildCollectionOrder());
    }

    private FrameworkElement BuildCollectionOrder()
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = CollectionText.Get("web.collections.copy.fieldOrder", "Order"), FontSize = 14.5, LineHeight = 21.75, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold });
        var fields = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        foreach (var fraction in new[] { 1.2, 1.0, 1.0 }) fields.ColumnDefinitions.Add(new() { Width = new(fraction, GridUnitType.Star) });
        foreach (var unused in Enumerable.Range(0, 3)) fields.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var sort = _sortChoice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top, MinWidth = 0, Height = 36, MinHeight = 0, FontSize = 14 };
        PopulateSortChoice(sort); AutomationProperties.SetName(sort, CollectionText.Get("web.collections.fields.sortBy", "Sort by"));
        var direction = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top, Height = 36, MinHeight = 0, MinWidth = 0, FontSize = 14 };
        AutomationProperties.SetName(direction, CollectionText.Get("web.collections.fields.directionLabel", "Direction")); var updating = false;
        void Directions()
        {
            updating = true;
            var field = Query.Sort?.Field ?? "added_at";
            var alphabetical = field is "title" or "author" or "narrator" or "series" or "content_rating";
            var date = field is "added_at" or "release_date" or "last_air_date" or "latest_episode_added" or "year" or "date_viewed";
            var order = Query.Sort?.Order ?? "desc";
            var options = new List<Option>
            {
                new(CollectionText.Get("web.collections.fields.direction", alphabetical ? "A–Z" : date ? "Oldest first" : "Lowest first", ("direction", alphabetical ? "az" : date ? "oldest" : "lowest")), "asc"),
                new(CollectionText.Get("web.collections.fields.direction", alphabetical ? "Z–A" : date ? "Newest first" : "Highest first", ("direction", alphabetical ? "za" : date ? "newest" : "highest")), "desc"),
            };
            // Keep legacy/future orders visible without rewriting an untouched
            // saved query; asc/desc remain available for an explicit edit.
            if (order is not ("asc" or "desc")) options.Add(new($"Saved order ({order})", order));
            direction.ItemsSource = options;
            direction.SelectedItem = options.FirstOrDefault(o => o.Value == order); updating = false;
        }
        Directions();
        sort.SelectionChanged += (_, _) => { if (_refreshingChoices) return; var field = Selected(sort); Query.Sort = new() { Field = field, Order = field is "title" or "author" or "narrator" or "series" or "content_rating" ? "asc" : "desc" }; Directions(); SortChanged?.Invoke(); Changed?.Invoke(); };
        direction.SelectionChanged += (_, _) => { if (updating) return; Query.Sort ??= new() { Field = Selected(sort) }; Query.Sort.Order = Selected(direction); SortChanged?.Invoke(); Changed?.Invoke(); };
        fields.Children.Add(sort); Grid.SetColumn(direction, 1); fields.Children.Add(direction);
        var limit = CollectionLimitInput ?? new TextBox { Text = Query.Limit?.ToString() ?? "" };
        limit.PlaceholderText = CollectionText.Get("web.collections.copy.noLimit", "No limit");
        if (limit.Parent is Panel previous) previous.Children.Remove(limit);
        limit.Height = 42; limit.MinHeight = 0; limit.MinWidth = 0; limit.Padding = new(12, 8, 0, 8); limit.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); limit.BorderThickness = new(0); AutomationProperties.SetName(limit, CollectionText.Get("web.collections.copy.fieldLimit", "Max titles"));
        var limitRow = new Grid(); limitRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); limitRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); limitRow.Children.Add(limit);
        _collectionLimitOwner = limitRow;
        var unit = new Border { BorderBrush = CollectionBrush("BorderBrush"), BorderThickness = new(1, 0, 0, 0), Padding = new(12, 0, 12, 0), Child = new TextBlock { Text = CollectionText.Get("web.collections.fields.maxTitlesSuffix", "max titles"), FontSize = 13, Foreground = CollectionBrush("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center } }; Grid.SetColumn(unit, 1); limitRow.Children.Add(unit);
        var limitField = new Border { Child = limitRow, Height = 44, CornerRadius = new(6), BorderThickness = new(1), BorderBrush = CollectionBrush("BorderBrush") }; Grid.SetColumn(limitField, 2); fields.Children.Add(limitField);
        void Reflow()
        {
            var phone = (XamlRoot?.Size.Width ?? 1280) < 640;
            fields.RowSpacing = phone ? 10 : 0;
            for (var i = 0; i < fields.Children.Count; i++) { var control = (FrameworkElement)fields.Children[i]; Grid.SetColumn(control, phone ? 0 : i); Grid.SetColumnSpan(control, phone ? 3 : 1); Grid.SetRow(control, phone ? i : 0); }
        }
        fields.SizeChanged += (_, _) => Reflow(); Reflow(); body.Children.Add(fields);
        body.Children.Add(new TextBlock { Text = CollectionText.Get("web.collections.copy.orderHelp", "A profile that picks its own sort while browsing keeps that choice."), FontSize = 13, Foreground = CollectionBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        return new Border { Child = body, Padding = new(0, 16, 0, 0), BorderThickness = new(0, 1, 0, 0), BorderBrush = CollectionBrush("BorderBrush") };
    }

    private static Button CollectionAddButton(string label) => new()
    {
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { WebUiIcon.Create("plus", 16), new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Medium } } },
        Height = 32, MinHeight = 0, Padding = new(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left,
        Style = (Style)Application.Current.Resources["GhostButtonStyle"]
    };
}
