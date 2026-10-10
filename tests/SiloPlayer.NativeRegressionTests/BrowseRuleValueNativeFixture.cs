using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;

internal static class BrowseRuleValueNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var editor = new QueryRulesEditor { Width = 416 };
        editor.CapabilitiesLoader = null;
        parent.Children.Add(editor);
        var failures = new List<string>();
        try
        {
            async Task Load(QueryRule rule)
            {
                var query = new QueryDefinition { Groups = [new() { Rules = [rule] }] };
                var before = JsonSerializer.Serialize(query);
                editor.Load(query); await Task.Delay(50);
                if (before != JsonSerializer.Serialize(query)) failures.Add("Loading a rule rewrote its saved value.");
            }
            async Task Choose(string op)
            {
                var selector = Descendants<ComboBox>(editor).Single(box => AutomationProperties.GetName(box) == "Rule operator");
                selector.SelectedItem = selector.Items.Cast<object>().Single(item => item.GetType().GetProperty("Value")!.GetValue(item)?.ToString() == op);
                await Task.Delay(50);
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_FILTER_FIELDS") == "1")
            {
                void SetOption(string name, object value)
                {
                    var property = typeof(QueryRulesEditor).GetProperty(name);
                    if (property == null) failures.Add("Missing current field capability: " + name);
                    else property.SetValue(editor, value);
                }
                string[] Choices(string name) => Descendants<ComboBox>(editor).SingleOrDefault(box => AutomationProperties.GetName(box) == name)?.Items.Cast<object>().Select(item => item is ComboBoxItem option ? option.Tag?.ToString() ?? "" : item.GetType().GetProperty("Value")!.GetValue(item)?.ToString() ?? "").Where(value => value.Length > 0).ToArray() ?? [];
                SetOption("ExtendedRules", false); SetOption("ShownRatingSources", new HashSet<string>());
                editor.ConfigureCatalogScope("movie"); await Load(new() { Field = "genre", Op = "is", Value = "Drama" });
                if (Choices("Rule field").Any(value => value is "title" or "runtime" or "rating_tmdb" or "status" or "last_air_date")) failures.Add("Unsupported extended/hidden/show-only fields are offered to new movie rules.");
                if (Choices("Rule operator").Contains("contains")) failures.Add("Legacy genre contains must stay hidden except on a saved contains rule.");
                SetOption("ExtendedRules", true); editor.ConfigureCatalogScope("series"); await Load(new() { Field = "genre", Op = "is", Value = "Drama" });
                if (!new[] { "title", "runtime", "rating_tmdb", "subtitle_language", "last_air_date", "latest_episode_added", "last_watched" }.All(Choices("Rule field").Contains)) failures.Add("Current title/file/viewer fields are missing from the extended series editor.");
                if (Choices("Rule field").Any(value => value is "rating_rt_critic" or "rating_rt_audience")) failures.Add("Hidden ratings must not be offered to new rules.");
                await Load(new() { Field = "rating_rt_critic", Op = "gte", Value = 80 });
                if (!Choices("Rule field").Contains("rating_rt_critic")) failures.Add("A saved hidden rating must remain available on its existing rule.");
                await Load(new() { Field = "year", Op = "is_not", Value = 2020 });
                if (!Choices("Rule operator").Contains("is_not")) failures.Add("Year is-not is missing.");
                await Load(new() { Field = "runtime", Op = "gte", Value = 90 });
                if (!Descendants<TextBlock>(editor).Any(text => text.Text == "min")) failures.Add("Duration must expose its server unit in minutes.");
                await Load(new() { Field = "bitrate", Op = "gte", Value = 8000 });
                if (!Descendants<TextBlock>(editor).Any(text => text.Text == "kbps")) failures.Add("Bitrate must expose its server unit in kbps.");
                await Load(new() { Field = "added_at", Op = "in_last", Value = "30d" });
                var amount = Descendants<TextBox>(editor).SingleOrDefault(box => AutomationProperties.GetName(box) == "Relative date amount");
                var unit = Descendants<ComboBox>(editor).SingleOrDefault(box => AutomationProperties.GetName(box) == "Relative date unit");
                if (amount?.Text != "30" || unit == null) failures.Add("Relative dates need separate amount/unit controls.");
                else { unit.SelectedItem = unit.Items.Cast<object>().Single(item => item.GetType().GetProperty("Value")!.GetValue(item)?.ToString() == "w"); await Task.Delay(30); if (QueryRuleValues.Format(editor.Query.Groups[0].Rules[0].Value) != "30w") failures.Add("Changing the relative date unit did not write the server's span format."); }
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
                Program.Log("PASS: FILTER_FIELDS_COMPLETED mounted scoped/capability-gated fields, preserved hidden rules, typed numeric units and relative dates.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_FILTER_SORT") == "1")
            {
                await Load(new() { Field = "genre", Op = "is", Value = "Comedy" });
                var sort = Descendants<ComboBox>(editor).SingleOrDefault(box => AutomationProperties.GetName(box) == "Sort by");
                var direction = Descendants<ComboBox>(editor).SingleOrDefault(box => AutomationProperties.GetName(box) == "Direction");
                if (sort == null || direction == null) throw new InvalidOperationException("Advanced filters are missing their Sort by/Direction controls.");
                sort.SelectedItem = sort.Items.Cast<object>().Single(item => item.GetType().GetProperty("Value")?.GetValue(item)?.ToString() == "title");
                sort.SelectedItem = sort.Items.Cast<object>().Single(item => item.GetType().GetProperty("Value")?.GetValue(item)?.ToString() == "year");
                await Task.Delay(30);
                if (editor.Query.Sort?.Field != "year" || editor.Query.Sort.Order != "desc") failures.Add("Choosing Year must submit the new sort with its default descending direction.");
                direction.SelectedItem = direction.Items.Cast<object>().Single(item => item.GetType().GetProperty("Value")?.GetValue(item)?.ToString() == "asc");
                if (editor.Query.Sort?.Field != "year" || editor.Query.Sort.Order != "asc") failures.Add("Direction changes must retain the selected sort field.");
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
                Program.Log("PASS: FILTER_SORT_COMPLETED actual Advanced sort/direction controls update their shared query.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_FILTER_PICKERS") == "1")
            {
                editor.Options = new Dictionary<string, IReadOnlyList<string>> { ["original_language"] = new[] { "en", "fr" } };
                typeof(QueryRulesEditor).GetProperty("LanguageLoader")?.SetValue(editor, (Func<string, CancellationToken, Task<IReadOnlyList<string>>>)((_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "en", "fr" })));
                await Load(new() { Field = "original_language", Op = "is", Value = "zz" });
                var language = Descendants<ComboBox>(editor).SingleOrDefault(box => AutomationProperties.GetName(box) == "Rule language");
                if (language == null) failures.Add("Language rules require named choices and must retain an unlisted saved language.");
                else
                {
                    if (!language.Items.Cast<object>().Any(item => item.ToString() == "English") || !language.Items.Cast<object>().Any(item => item.ToString() == "French") || language.SelectedItem?.ToString() != "zz") failures.Add("Language choices must use readable names without dropping an unknown saved code.");
                    language.SelectedItem = language.Items.Cast<object>().Single(item => item.ToString() == "French");
                    if (QueryRuleValues.Format(editor.Query.Groups[0].Rules[0].Value) != "fr") failures.Add("Language selection must save the language code, not its display name.");
                }
                editor.FacetSearch = (_, text, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "Ranked Studio" });
                await Load(new() { Field = "studio", Op = "is", Value = "Saved Studio" });
                var picker = Descendants<AutoSuggestBox>(editor).SingleOrDefault();
                for (var wait = 0; wait < 20 && picker != null && picker.ItemsSource == null; wait++) await Task.Delay(30);
                if (picker?.ItemsSource is not IEnumerable<string> suggestions || !suggestions.Contains("Ranked Studio")) failures.Add("A scoped facet must offer ranked server choices when mounted, not only old preloaded names.");
                if (QueryRuleValues.Format(editor.Query.Groups[0].Rules[0].Value) != "Saved Studio") failures.Add("Loading suggestions changed the saved facet value.");
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
                Program.Log("PASS: FILTER_PICKERS_COMPLETED named/code-preserving languages and scoped server facet suggestions.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_FILTER_OPAQUE") == "1")
            {
                foreach (var rule in new QueryRule[]
                {
                    new() { Field = "future_field", Op = "future_op", Value = JsonSerializer.Deserialize<JsonElement>("{\"nested\":[false,7.5]}") },
                    new() { Field = "year", Op = "future_op", Value = 2026 },
                    new() { Field = "hdr", Op = "is", Value = "false" },
                    new() { Field = "year", Op = "between", Value = new[] { 1990, 2000, 2010 } },
                })
                {
                    await Load(rule);
                    var readonlyRow = Descendants<Border>(editor).SingleOrDefault(row => AutomationProperties.GetName(row) == "Rule not editable here");
                    if (readonlyRow == null || Descendants<ComboBox>(readonlyRow).Any() || Descendants<TextBox>(readonlyRow).Any())
                        failures.Add($"{rule.Field}/{rule.Op}: a rule these controls cannot represent must be read-only.");
                    if (!Descendants<TextBlock>(editor).Any(text => text.Text.Contains("Not editable here")))
                        failures.Add($"{rule.Field}/{rule.Op}: read-only explanation is missing.");
                    var remove = Descendants<Button>(editor).Single(button => AutomationProperties.GetName(button) == "Remove rule");
                    var invoke = new ButtonAutomationPeer(remove).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                    if (invoke == null) throw new InvalidOperationException("Actual remove provider is missing.");
                    invoke.Invoke(); await Task.Delay(50);
                    if (editor.Query.Groups.Single().Rules.Count != 0) failures.Add("Read-only rules must remain explicitly removable.");
                }
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
                Program.Log("PASS: FILTER_OPAQUE_COMPLETED mounted unknown/operator/type rules remain lossless, read-only and explicitly removable.");
                return;
            }
            var range = new QueryRule { Field = "year", Op = "between", Value = JsonSerializer.Deserialize<JsonElement>("[1990,2000]") };
            await Load(range); await Choose("gte");
            if (JsonSerializer.Serialize(range.Value) != "1990") failures.Add("Leaving a range must retain only its first scalar value, including saved JSON arrays.");
            await Choose("between");
            if (JsonSerializer.Serialize(range.Value) != "[\"\",\"\"]") failures.Add("Entering a range must initialize two empty endpoints rather than retaining a scalar.");
            if (editor.IsValid) failures.Add("A newly selected incomplete range must block submission before any typing.");
            var inputs = Descendants<TextBox>(editor).Where(box => box.PlaceholderText is "From" or "To").ToArray();
            inputs.Single(box => box.PlaceholderText == "From").Text = "1990";
            inputs.Single(box => box.PlaceholderText == "To").Text = "2000";
            await Task.Delay(50);
            if (!editor.IsValid || QueryRuleValues.Format(range.Value) != "1990, 2000") failures.Add("Completing both actual range textboxes must restore submission and a numeric range.");
            var date = new QueryRule { Field = "added_at", Op = "in_last", Value = "30d" };
            await Load(date); await Choose("gt");
            if (QueryRuleValues.Format(date.Value) != "") failures.Add("A relative date must clear when changed to an absolute date condition.");
            date.Value = "2026-10-09"; editor.Load(new() { Groups = [new() { Rules = [date] }] });
            await Choose("in_last");
            if (QueryRuleValues.Format(date.Value) != "") failures.Add("An absolute date must clear when changed to a relative date condition.");
            var incomplete = new QueryRule { Field = "rating_imdb", Op = "between", Value = new[] { "7.5", "" } };
            await Load(incomplete);
            if (editor.IsValid) failures.Add("Loading an incomplete saved range must block submission while preserving its original value.");
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
            Program.Log("PASS: FILTER_VALUES_COMPLETED mounted condition transitions, lossless loading and incomplete-range submission/recovery.");
        }
        finally { parent.Children.Remove(editor); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
