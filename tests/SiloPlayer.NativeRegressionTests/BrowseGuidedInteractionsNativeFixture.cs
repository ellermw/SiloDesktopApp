using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;

internal static class BrowseGuidedInteractionsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://guided-interaction.invalid");
        using var services = new ServiceCollection().AddSingleton(new CatalogApi(client)).AddSingleton(new PeopleApi(client)).BuildServiceProvider();
        var editor = new QueryFilterEditor { Width = 416, Height = 900 };
        var unknown = new QueryRule { Field = "fixture_unknown", Op = "fixture_op", Value = "retained advanced value" };
        var preserved = new QueryRule { Field = "country", Op = "is", Value = "GB" };
        var query = new QueryDefinition { Groups = [new() { Rules = [preserved] }] };
        var filters = new CatalogFiltersResponse { Genres = ["Crime", "Drama"], OriginalLanguages = ["en", "fr"] };
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_FILTER_CAPABILITIES") == "1")
        {
            field.SetValue(null, services); parent.Children.Add(editor);
            try
            {
                editor.Load(query, "series", 22, filters);
                var advanced = (QueryRulesEditor)typeof(QueryFilterEditor).GetField("_advanced", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
                for (var attempt = 0; attempt < 80 && !advanced.ExtendedRules; attempt++) await Task.Delay(25);
                if (!advanced.ExtendedRules || advanced.ShownRatingSources?.Contains("rt_critic") != true)
                    throw new InvalidOperationException("Mounted filter editor must receive current server extended-rule and shown-rating capabilities.");
                Program.Log("PASS: FILTER_CAPABILITIES_COMPLETED actual mounted editor receives extended-query/ratings capabilities from its isolated API.");
            }
            finally { parent.Children.Remove(editor); field.SetValue(null, previous); }
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_FILTER_REPRESENTATION") == "1")
        {
            field.SetValue(null, services); parent.Children.Add(editor);
            try
            {
                var differences = new List<string>();
                foreach (var (name, candidate, guidedExpected) in new (string, QueryDefinition, bool)[]
                {
                    ("empty", new(), true),
                    ("ordinary", new() { Groups = [new() { Rules = [new() { Field = "genre", Op = "contains", Value = "Crime" }, new() { Field = "year", Op = "gte", Value = 2000 }] }] }, true),
                    ("unknown", new() { Groups = [new() { Rules = [unknown] }] }, false),
                    ("negated", new() { Groups = [new() { Rules = [new() { Field = "genre", Op = "is_not", Value = "Crime" }] }] }, false),
                    ("strict-year", new() { Groups = [new() { Rules = [new() { Field = "year", Op = "gt", Value = 2000 }] }] }, false),
                    ("repeated-people", new() { Groups = [new() { Rules = [new() { Field = "actor", Op = "is", Value = "Person One" }, new() { Field = "actor", Op = "is", Value = "Person Two" }] }] }, false),
                    ("non-4k-resolution", new() { Groups = [new() { Rules = [new() { Field = "resolution", Op = "is", Value = "1080p" }] }] }, false),
                    ("ebook-narrator", new() { MediaScope = "ebook", Groups = [new() { Rules = [new() { Field = "narrator", Op = "is", Value = "Narrator" }] }] }, false),
                    ("or-genres", new() { Groups = [new() { Match = "any", Rules = [new() { Field = "genre", Op = "is", Value = "Crime" }, new() { Field = "genre", Op = "is", Value = "Drama" }] }] }, false),
                    ("or-languages", new() { Groups = [new() { Match = "any", Rules = [new() { Field = "original_language", Op = "is", Value = "en" }, new() { Field = "original_language", Op = "is", Value = "fr" }] }] }, true),
                })
                {
                    var before = JsonSerializer.Serialize(candidate);
                    editor.Load(candidate, candidate.MediaScope ?? "video", 22, filters); await Task.Delay(60);
                    var guided = (Button)typeof(QueryFilterEditor).GetField("_guidedButton", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
                    var advanced = (QueryRulesEditor)typeof(QueryFilterEditor).GetField("_advanced", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
                    if (guided.IsEnabled != guidedExpected || !guidedExpected && advanced.Visibility != Visibility.Visible)
                        differences.Add($"{name}: current native editor does not disable Guided and show Advanced for rules Guided cannot fully represent.");
                    if (before != JsonSerializer.Serialize(candidate)) differences.Add($"{name}: loading a mode changed its rules.");
                }
                if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
                Program.Log("PASS: FILTER_REPRESENTATION_COMPLETED actual Guided availability/Advanced fallback and lossless loaded rules.");
            }
            finally { parent.Children.Remove(editor); field.SetValue(null, previous); }
            return;
        }
        editor.Load(query, "video", 22, filters);
        field.SetValue(null, services); parent.Children.Add(editor);
        try
        {
            await Task.Delay(120);
            if (wire.FacetRequests != 0) throw new InvalidOperationException("Hidden Advanced controls performed facet lookups while Guided was displayed.");
            AutoSuggestBox Input(string caption) => Descendants<AutoSuggestBox>(editor).Single(box => box.Header is TextBlock header && header.Text == caption);
            var initialQuery=JsonSerializer.Serialize(query);
            var focusedGenres=Input("Genres");
            focusedGenres.Focus(FocusState.Programmatic);
            await Task.Delay(80);
            if (!focusedGenres.IsSuggestionListOpen || !Values(focusedGenres).SequenceEqual(filters.Genres))
                throw new InvalidOperationException("Opening Guided Genres must offer available choices before typing.");
            if (initialQuery!=JsonSerializer.Serialize(query))
                throw new InvalidOperationException("Opening Guided choices must not modify the draft.");
            focusedGenres.IsSuggestionListOpen=false;
            Program.Log("PASS: Guided focus opens bounded available choices without changing the query.");
            var advancedToggle=Descendants<Button>(editor).Single(button=>button.Content?.ToString()=="Advanced");
            ((IInvokeProvider)new ButtonAutomationPeer(advancedToggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(60);
            var rulesEditor=Descendants<QueryRulesEditor>(editor).Single();
            if (!Descendants<TextBlock>(rulesEditor).Any(text=>text.Text=="Rule Groups"))
                throw new InvalidOperationException("Advanced filter editor is missing the current Rule Groups heading.");
            var guidedToggle=Descendants<Button>(editor).Single(button=>button.Content?.ToString()=="Guided");
            ((IInvokeProvider)new ButtonAutomationPeer(guidedToggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(60);
            foreach (var input in Descendants<AutoSuggestBox>(editor))
            {
                var name = (input.Header as TextBlock)?.Text;
                if (name is not ("Genres" or "Original Language")) continue;
                input.SuggestionChosen += (_, args) => Program.Log($"{name} real SuggestionChosen selected={args.SelectedItem}, text={input.Text}, afterHandler={JsonSerializer.Serialize(editor.Query)}.");
                input.QuerySubmitted += (_, args) => Program.Log($"{name} real QuerySubmitted chosen={args.ChosenSuggestion ?? "null"}, query={args.QueryText}, text={input.Text}, afterHandler={JsonSerializer.Serialize(editor.Query)}.");
            }
            async Task Type(AutoSuggestBox input, string text)
            {
                if (!input.Focus(FocusState.Programmatic)) throw new InvalidOperationException("Scoped suggestion textbox could not receive fixture focus.");
                var textbox = Descendants<TextBox>(input).Single();
                textbox.Text = ""; textbox.SelectionStart = 0;
                // WinUI gives its content island keyboard focus. Target only
                // this process's focused HWND; no global input injection.
                var focused = GetFocus(); GetWindowThreadProcessId(focused, out var process);
                if (focused == IntPtr.Zero || process != (uint)Environment.ProcessId)
                    throw new InvalidOperationException("Scoped suggestion fixture has no owned focused content-island HWND.");
                var userInput = false;
                Windows.Foundation.TypedEventHandler<AutoSuggestBox, AutoSuggestBoxTextChangedEventArgs> observe = (_, args) => userInput |= args.Reason == AutoSuggestionBoxTextChangeReason.UserInput;
                input.TextChanged += observe;
                foreach (var character in text) if (!PostMessage(focused, 0x0102, (nuint)character, 1)) throw new InvalidOperationException("Could not deliver a character to the owned fixture content island.");
                await Task.Delay(360);
                input.TextChanged -= observe;
                Program.Log($"Owned suggestion character input: inner={textbox.Text}, outer={input.Text}, UserInput={userInput}.");
                if (!userInput || input.Text != text) throw new InvalidOperationException("Targeted fixture characters did not reach the actual AutoSuggestBox UserInput contract.");
            }
            async Task Select(AutoSuggestBox input, string text)
            {
                await Type(input, text);
                for (var attempt = 0; attempt < 80 && !Values(input).SequenceEqual(new[] { text }); attempt++) await Task.Delay(25);
                Program.Log($"Mounted {text} suggestions: type={input.ItemsSource?.GetType().FullName ?? "null"}, values={string.Join(",", Values(input))}.");
                if (!Values(input).SequenceEqual(new[] { text })) throw new InvalidOperationException($"Mounted {text} suggestion did not populate through its actual TextBox edit event.");
                input.IsSuggestionListOpen = true; await Task.Delay(100);
                var list = VisualTreeHelper.GetOpenPopupsForXamlRoot(editor.XamlRoot).SelectMany(popup => new[] { popup.Child }.Concat(Descendants<DependencyObject>(popup.Child))).OfType<ListView>().Single(view => view.Items.Contains(text));
                if (list.Items.Count != 1 || list.Items[0]?.ToString() != text) throw new InvalidOperationException("Scoped suggestion popup did not contain its one matching option.");
                input.Focus(FocusState.Programmatic); SendKey(0x28); await Task.Delay(40); SendKey(0x0D);
                await Until(() => query.Groups.SelectMany(group => group.Rules).Any(rule => QueryRuleValues.Format(rule.Value) == text));
                input.IsSuggestionListOpen = false;
            }
            await Select(Input("Genres"), "Crime"); await Select(Input("Genres"), "Drama");
            await Select(Input("Original Language"), "en"); await Select(Input("Original Language"), "fr");
            Program.Log("Guided export before HDR: " + JsonSerializer.Serialize(editor.Query) + "; original=" + ReferenceEquals(query, editor.Query) + "; preserved=" + query.Groups.SelectMany(group => group.Rules).Contains(preserved));
            var hdr = Descendants<ToggleButton>(editor).Single(button => button.Content?.ToString() == "HDR");
            hdr.StartBringIntoView(); var focusedHdr = hdr.Focus(FocusState.Programmatic); var toggle = new ToggleButtonAutomationPeer(hdr).GetPattern(PatternInterface.Toggle) as IToggleProvider; Program.Log($"HDR actual peer: checked={hdr.IsChecked}, focus={focusedHdr}, state={hdr.FocusState}, toggle={toggle != null}.");
            if (toggle == null) throw new InvalidOperationException("Actual HDR Toggle provider is unavailable; keyboard-only delivery did not activate it.");
            toggle.Toggle(); await Until(() => hdr.IsChecked == true);
            Program.Log("Guided export after HDR: " + JsonSerializer.Serialize(editor.Query) + "; checked=" + hdr.IsChecked + "; focus=" + hdr.FocusState + "; preserved=" + query.Groups.SelectMany(group => group.Rules).Contains(preserved));
            if (!query.Groups.Any(group => group.Match == "all" && group.Rules.Count(rule => rule.Field == "genre") == 2) || !query.Groups.Any(group => group.Match == "any" && group.Rules.Count(rule => rule.Field == "original_language") == 2) || !query.Groups.SelectMany(group => group.Rules).Any(rule => rule.Field == "hdr" && QueryRuleValues.Format(rule.Value) == "true") || !query.Groups.SelectMany(group => group.Rules).Contains(preserved))
                throw new InvalidOperationException("Actual Guided genre/language/HDR interactions must retain ALL genres, ANY languages and the unrelated advanced rule.");
            Program.Log("PASS: actual rendered Guided two genres ALL/two languages ANY/HDR with unrelated advanced-rule preservation.");
            var manualGenres = Input("Genres");
            await Type(manualGenres, "Comedy, Action"); SendKey(0x0D);
            await Until(() => query.Groups.SelectMany(group => group.Rules).Where(rule => rule.Field == "genre").Select(rule => QueryRuleValues.Format(rule.Value)).Order().SequenceEqual(new[] { "Action", "Comedy" }));
            async Task ClearGenres()
            {
                var clearTextbox = Descendants<TextBox>(manualGenres).Single(); var clearFocus = clearTextbox.Focus(FocusState.Programmatic); clearTextbox.SelectAll();
                Program.Log($"Manual clear before: focus={clearFocus}/{clearTextbox.FocusState}, inner={clearTextbox.Text}, outer={manualGenres.Text}, selection={clearTextbox.SelectionStart}/{clearTextbox.SelectionLength}.");
                var clearWindow = GetFocus(); GetWindowThreadProcessId(clearWindow, out var clearProcess);
                if (clearWindow == IntPtr.Zero || clearProcess != (uint)Environment.ProcessId) throw new InvalidOperationException("Manual clear has no owned focused HWND.");
                var clearChanges = 0; var clearReasons = new List<string>();
                Windows.Foundation.TypedEventHandler<AutoSuggestBox, AutoSuggestBoxTextChangedEventArgs> observeClear = (_, args) => { clearChanges++; clearReasons.Add(args.Reason.ToString()); };
                manualGenres.TextChanged += observeClear;
                SendKey(0x08);
                if (!PostMessage(clearWindow, 0x0102, 8, 1)) throw new InvalidOperationException("Could not deliver actual manual Backspace to the owned textbox.");
                await Until(() => manualGenres.Text == "" && clearTextbox.Text == "" && clearChanges > 0); manualGenres.TextChanged -= observeClear;
                Program.Log($"Manual clear after: inner={clearTextbox.Text}, outer={manualGenres.Text}, changes={clearChanges}, reasons={string.Join(",", clearReasons)}.");
                if (clearChanges == 0) throw new InvalidOperationException("Owned Backspace did not emit an actual native TextChanged event.");
                SendKey(0x0D); await Until(() => !query.Groups.SelectMany(group => group.Rules).Any(rule => rule.Field == "genre"));
            }
            await ClearGenres();
            await Select(manualGenres, "Crime"); await ClearGenres();
            if (!query.Groups.Any(group => group.Match == "any" && group.Rules.Count(rule => rule.Field == "original_language") == 2) || !query.Groups.SelectMany(group => group.Rules).Contains(preserved)) throw new InvalidOperationException("Manual genre submit/clear must preserve selected languages and the unrelated country rule.");
            Program.Log("PASS: actual manual comma-separated genres replace selected values; owned Backspace emits native TextChanged and empty submission clears manual values and a directly chosen suggestion, preserving unrelated rules.");
            var actor = Input("Actor"); wire.DelayPerson = true; await Type(actor, "Deferred Person");
            await Until(() => wire.PersonRequests == 1);
            editor.Load(new(), "audiobook", 11, filters); await Task.Delay(100);
            wire.PersonGate.SetResult(new[] { new { id = "late-person", name = "Deferred Person" } });
            await Task.Delay(120);
            if (actor.ItemsSource != null) throw new InvalidOperationException("A late canceled people response published into the old scope.");
            var author = Input("Author"); wire.FailFacet = true; await Type(author, "Failure");
            await Until(() => wire.FacetRequests == 1); await Task.Delay(80);
            if (author.ItemsSource != null) throw new InvalidOperationException("Failed scoped suggestions retained invalid options.");
            wire.FailFacet = false; await Type(author, "Recovered Author");
            await Until(() => Values(author).Contains("Recovered Author"));
            if (!wire.FacetQuery.Contains("library_ids=11") || !wire.FacetQuery.Contains("type=audiobook")) throw new InvalidOperationException("Recovered author suggestion lost audiobook library scope.");
            Program.Log("PASS: mounted suggestion scope switch rejects ignored-cancellation people response; failed audiobook/library11 author lookup recovers.");
        }
        finally { foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(editor.XamlRoot)) popup.IsOpen = false; parent.Children.Remove(editor); field.SetValue(null, previous); }
    }
    private static async Task Until(Func<bool> ready) { for (var attempt = 0; attempt < 80 && !ready(); attempt++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Mounted Guided interaction did not settle."); }
    // WinRT may project ItemsSource as an inspectable object collection rather
    // than preserving the managed IEnumerable<string> implementation.
    private static string[] Values(AutoSuggestBox input) => input.ItemsSource is System.Collections.IEnumerable values ? values.Cast<object>().Select(value => value.ToString() ?? "").ToArray() : [];
    private static void SendKey(nuint key)
    {
        var focused = GetFocus(); GetWindowThreadProcessId(focused, out var process);
        if (focused == IntPtr.Zero || process != (uint)Environment.ProcessId) throw new InvalidOperationException("Fixture key target is not an owned focused HWND.");
        if (!PostMessage(focused, 0x0100, key, 1) || !PostMessage(focused, 0x0101, key, unchecked((nint)0xC0000001u)))
            throw new InvalidOperationException("Could not deliver a key to the owned fixture HWND.");
    }
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window, uint message, nuint wParam, nint lParam);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T typed) yield return typed; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool DelayPerson, FailFacet; internal int PersonRequests, FacetRequests; internal string FacetQuery = "";
        internal TaskCompletionSource<object> PersonGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "guided-interaction.invalid") throw new InvalidOperationException("Guided fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath; object body = new { items = Array.Empty<string>() }; var status = HttpStatusCode.OK;
            if (path == "/api/v2/catalog/search/capabilities") body = new { people_media_scope = true, extended_query_rules = true, facet_value_search = true };
            if (path == "/api/v2/capabilities/ratings") body = new { state = "available", sources = new[] { new { source = "rt_critic" } } };
            if (path == "/api/v2/catalog/people") { PersonRequests++; body = new { items = DelayPerson ? await PersonGate.Task : Array.Empty<object>() }; }
            if (path == "/api/v2/catalog/filters/search") { FacetRequests++; FacetQuery = request.RequestUri.Query; status = FailFacet ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK; body = new { values = new[] { new { value = "Recovered Author", count = 1 } }, values_has_more = false }; }
            return new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
    }
}
