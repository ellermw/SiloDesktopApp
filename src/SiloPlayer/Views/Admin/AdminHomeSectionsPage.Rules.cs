using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminHomeSectionsPage
{
    private AdminHomeRuleCapabilities? _ruleCapabilities;
    private void RulesEditor(StackPanel panel)
    {
        var host = new StackPanel { Spacing = 12 }; panel.Children.Add(host);
        var draft = _draft!; var revision = _dialogRevision; var pickerRevision = _pickerRevision; var token = _editorReads!.Token;
        void RenderRules()
        {
            host.Children.Clear();
            foreach (var key in _fieldErrors.Keys.Where(key => key.StartsWith("rule:", StringComparison.Ordinal)).ToArray()) _fieldErrors.Remove(key);
            RefreshSave();
            var scope = Value(draft.Config, "media_scope"); if (scope == "") scope = Value(draft.Config, "filter_type");
            Field(host, "Show", Select("HomeSectionRuleScope", [("", "Everything"), ("video", "Movies and shows"), ("movie", "Movies"), ("series", "Shows"), ("episode", "Episodes"), ("audiobook", "Audiobooks"), ("ebook", "Ebooks"), ("manga", "Manga")], scope,
                value => { AdminHomeSectionsRules.NormalizeQueryInPlace(draft.Config); SetConfig("media_scope", value == "" ? null : JsonValue.Create(value)); RenderRules(); }));
            LibrarySelector(host);
            var groups = draft.Config["groups"] as JsonArray ?? new();
            Field(host, "How groups combine", Select("HomeSectionGroupsMatch", [("all", "All groups (and)"), ("any", "Any group (or)")], Value(draft.Config, "match") == "any" ? "any" : "all", value => { AdminHomeSectionsRules.NormalizeQueryInPlace(draft.Config); SetConfig("match", JsonValue.Create(value)); }));
            if (groups.Count == 0) host.Children.Add(U.Text("No rules yet. Add a rule to choose what this row shows.", 13, true));
            void Commit() { if (draft.Config["groups"] == null) draft.Config["groups"] = groups; AdminHomeSectionsRules.NormalizeQueryInPlace(draft.Config); Dirty(); }
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var at = groupIndex;
                if (groups[groupIndex] is not JsonObject group) { host.Children.Add(U.Text("A saved rule group cannot be edited here. It will be preserved.", 13)); continue; }
                var groupPanel = new StackPanel { Spacing = 8 };
                Field(groupPanel, "Match", Select("HomeSectionGroupMatch_" + at, [("all", "All of these rules"), ("any", "Any of these rules")], Value(group, "match") == "any" ? "any" : "all", value => { group["match"] = value; Commit(); }));
                var rules = group["rules"] as JsonArray ?? new();
                for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    var ruleAt = ruleIndex;
                    if (rules[ruleIndex] is not JsonObject rule) { groupPanel.Children.Add(U.Text("Saved rule (read only)", 13)); continue; }
                    var field = Value(rule, "field"); var op = Value(rule, "op");
                    var offered = QueryFieldCatalog.Offered(scope, _ruleCapabilities?.ExtendedQueryRules == true, _ruleCapabilities?.ShownRatingSources ?? new HashSet<string>(), true, _ruleCapabilities?.ShowTitleRules == true).ToArray();
                    var definition = offered.FirstOrDefault(entry => entry.Name == field);
                    var row = new StackPanel { Spacing = 6 };
                    if (definition != null && QueryFieldCatalog.OfferedOperators(field, op, _ruleCapabilities?.ExtendedQueryRules == true).Contains(op) && rule.All(pair => pair.Key is "field" or "op" or "value"))
                    {
                        Field(row, "Field", Select("HomeSectionRuleField_" + at + "_" + ruleAt, offered.Select(entry => (entry.Name, entry.Label)), field, value =>
                        { rule["field"] = value; rule["op"] = QueryFieldCatalog.Get(value)!.Operators[0]; rule["value"] = QueryRuleValues.IsBoolean(value) ? JsonValue.Create(true) : JsonValue.Create(""); Commit(); RenderRules(); }));
                        Field(row, "Condition", Select("HomeSectionRuleOp_" + at + "_" + ruleAt, QueryFieldCatalog.OfferedOperators(field, op, _ruleCapabilities?.ExtendedQueryRules == true).Select(value => (value, OperatorLabel(value))), op, value =>
                        { rule["op"] = value; rule["value"] = JsonSerializer.SerializeToNode(QueryRuleValues.ForOperator(field, value, NodeValue(rule["value"]))); Commit(); RenderRules(); }));
                        var status = U.Text("", 12);
                        if (QueryRuleValues.IsBoolean(field))
                            Field(row, "Value", Select("HomeSectionRuleValue_" + at + "_" + ruleAt, [("true", "Yes"), ("false", "No")], Value(rule, "value"), value => { rule["value"] = value == "true"; Commit(); }));
                        else if (definition.Kind is "person" or "facet" or "language")
                            Field(row, "Value", RuleValuePicker(rule, definition, scope, Commit));
                        else
                        {
                            var valueBox = Input("Value", "HomeSectionRuleValue_" + at + "_" + ruleAt, QueryRuleValues.Format(NodeValue(rule["value"])), value =>
                            {
                                try { rule["value"] = JsonSerializer.SerializeToNode(QueryRuleValues.Parse(field, Value(rule, "op"), value)); Commit(); FieldError("rule:" + at + ":" + ruleAt, status, null); }
                                catch (FormatException ex) { FieldError("rule:" + at + ":" + ruleAt, status, ex.Message); }
                            });
                            Field(row, op == "between" ? "From, to (separated by a comma)" : QueryRuleValues.IsDate(field) ? "Date (YYYY-MM-DD) or relative period (30d)" : "Value", valueBox);
                        }
                        row.Children.Add(status);
                    }
                    else row.Children.Add(U.Text("Saved rule (read only). This server or row uses options outside the available controls. Remove it to replace it.", 13, true));
                    var remove = Action("Remove rule", "HomeSectionRuleRemove_" + at + "_" + ruleAt);
                    remove.Click += (_, _) => { rules.RemoveAt(ruleAt); Commit(); RenderRules(); }; row.Children.Add(remove); groupPanel.Children.Add(row);
                }
                var add = Action("Add rule", "HomeSectionRuleAdd_" + at); add.IsEnabled = rules.Count < 100;
                add.Click += (_, _) => { if (group["rules"] == null) group["rules"] = rules; rules.Add(new JsonObject { ["field"] = "genre", ["op"] = "is", ["value"] = "" }); Commit(); RenderRules(); };
                var removeGroup = Action("Remove group", "HomeSectionGroupRemove_" + at);
                removeGroup.Click += (_, _) => { groups.RemoveAt(at); Commit(); RenderRules(); };
                groupPanel.Children.Add(add); groupPanel.Children.Add(removeGroup); host.Children.Add(new Border { Child = groupPanel, Padding = new(12), BorderThickness = new(1), BorderBrush = U.Brush("MutedBrush"), CornerRadius = new(12) });
            }
            var addGroup = Action("Add another group", "HomeSectionGroupAdd"); addGroup.IsEnabled = groups.Count < 20;
            addGroup.Click += (_, _) => { if (groups.Count <= 1) draft.Config["match"] = "any"; groups.Add(new JsonObject { ["match"] = "all", ["rules"] = new JsonArray(new JsonObject { ["field"] = "genre", ["op"] = "is", ["value"] = "" }) }); Commit(); RenderRules(); }; host.Children.Add(addGroup);
            _afterLibraryChange = RenderRules;
        }
        RenderRules();
        if (_ruleCapabilities == null) _ = LoadAsync();
        async Task LoadAsync()
        {
            try
            {
                var caps = await _api.RuleCapabilitiesAsync(_editorOwner!, token);
                if (EditorCurrent(revision) && pickerRevision == _pickerRevision) { _ruleCapabilities = caps; RenderRules(); }
            }
            catch (Exception) { if (EditorCurrent(revision) && pickerRevision == _pickerRevision) host.Children.Add(U.Text("Extended rule options are unavailable. Existing rules are preserved.", 12, true)); }
        }
    }
    /// <summary>The rule row's order (official RuleSortField), under More options.</summary>
    private void RuleSortField(StackPanel host)
    {
        var draft = _draft!; var scope = Value(draft.Config, "media_scope"); if (scope == "") scope = Value(draft.Config, "filter_type");
        var sort = draft.Config["sort"] as JsonObject;
        var sortFields = new[] { ("added_at", "Recently added"), ("title", "Title"), ("release_date", "Release date"), ("last_air_date", "Latest episode aired"), ("latest_episode_added", "Latest episode added"), ("year", "Year"), ("content_rating", "Content rating"), ("rating_imdb", "IMDb rating"), ("rating_tmdb", "TMDB rating"), ("rating_rt_critic", "RT critic score"), ("rating_rt_audience", "RT audience score"), ("runtime", "Duration"), ("resolution", "Resolution"), ("bitrate", "Bitrate"), ("progress", "Progress"), ("date_viewed", "Date viewed"), ("plays", "Plays"), ("author", "Author"), ("narrator", "Narrator"), ("series", "Series") }
            .Where(option => CatalogRatingSortPolicy.AppliesToScope(option.Item1, scope == "" ? "all" : scope) && CatalogRatingSortPolicy.IsAvailable(option.Item1, _ruleCapabilities?.ShownRatingSources ?? new HashSet<string>(), sort == null ? Value(draft.Config, "sort") : Value(sort, "field"))).ToArray();
        Field(host, "Order", Select("HomeSectionRuleSort", sortFields, sort == null ? Value(draft.Config, "sort") : Value(sort, "field"), value => { AdminHomeSectionsRules.NormalizeQueryInPlace(draft.Config); var node = draft.Config["sort"] as JsonObject ?? new(); if (draft.Config["sort"] == null) draft.Config["sort"] = node; node["field"] = value; Dirty(); }));
        Field(host, "Direction", Select("HomeSectionRuleDirection", [("asc", "Ascending"), ("desc", "Descending")], sort == null ? Value(draft.Config, "order") : Value(sort, "order"), value => { AdminHomeSectionsRules.NormalizeQueryInPlace(draft.Config); ((JsonObject)draft.Config["sort"]!)["order"] = value; Dirty(); }));
    }
    private static object? NodeValue(JsonNode? node) => node == null ? null : JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
    private AutoSuggestBox RuleValuePicker(JsonObject rule, QueryFieldDefinition definition, string scope, Action commit)
    {
        var box = new AutoSuggestBox { Text = Value(rule, "value"), DisplayMemberPath = "Label", PlaceholderText = "Search values" };
        var revision = _dialogRevision; var view = _pickerRevision; var token = _editorReads!.Token; var owner = _editorOwner!;
        CancellationTokenSource? pending = null;
        async Task Search(string text, bool debounce)
        {
            pending?.Cancel(); var request = pending = CancellationTokenSource.CreateLinkedTokenSource(token);
            try
            {
                if (debounce) await Task.Delay(300, request.Token);
                var ids = (_draft!.Config["library_ids"] as JsonArray ?? _draft.Config["filter_library_ids"] as JsonArray)?.Select(node => node!.ToString()).ToArray() ?? (_scope.LibraryId == null ? [] : new[] { _scope.LibraryId });
                var values = await _api.RuleValuesAsync(owner, definition.Name, definition.Kind, text, scope, ids, _ruleCapabilities, request.Token);
                if (EditorCurrent(revision) && view == _pickerRevision && ReferenceEquals(request, pending) && box.IsLoaded) box.ItemsSource = values;
            }
            catch (Exception) { if (EditorCurrent(revision) && view == _pickerRevision && ReferenceEquals(request, pending)) box.ItemsSource = null; }
            finally { if (ReferenceEquals(request, pending)) pending = null; request.Dispose(); }
        }
        box.TextChanged += async (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            rule["value"] = box.Text; commit(); await Search(box.Text, true);
        };
        box.SuggestionChosen += (_, args) => { if (args.SelectedItem is AdminHomeRuleSuggestion suggestion) { rule["value"] = suggestion.Value; box.Text = suggestion.Label; commit(); } };
        box.GotFocus += async (_, _) => await Search("", false);
        box.Unloaded += (_, _) => pending?.Cancel();
        return box;
    }
    private static string OperatorLabel(string op) => op switch
    {
        "is" => "is", "is_not" => "is not", "contains" => "contains", "not_contains" => "does not contain", "begins_with" => "begins with", "ends_with" => "ends with",
        "gte" => "at least", "lte" => "at most", "gt" => "greater than", "lt" => "less than", "between" => "between", "in_last" => "in the last", "not_in_last" => "not in the last", _ => "Saved condition"
    };
}
