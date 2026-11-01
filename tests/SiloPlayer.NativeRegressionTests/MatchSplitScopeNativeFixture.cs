using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Views.Dialogs;
using SiloPlayer.Views;
using SiloPlayer.ViewModels;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Services;

internal static class MatchSplitScopeNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, bool reviewOnly = false)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://identity-scope-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); Admin(auth);
        slot.SetValue(null, new Services(original, client, auth));
        var window = new Window { Content = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] } };
        var owner = (Grid)window.Content;
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1000, 900)); window.AppWindow.Show(false); await Task.Delay(120);
            if (reviewOnly)
            {
                var failures = new List<Exception>();
                async Task Case(string name, Func<Task> run) { try { await run(); Program.Log("PASS review: " + name); } catch (Exception error) { Program.Log("FAIL review: " + name + " — " + error); failures.Add(error); } }
                await Case("unmatched split without search scope", () => UnmatchedWithoutChoiceAsync(owner, wire));
                await Case("only effective picker choices", () => EffectivePickerAsync(owner, wire));
                await Case("initial split inventory retry", () => InitialSplitRetryAsync(owner, wire));
                await Case("card read navigation departure", () => CardDepartureAsync(owner, wire, apply: false));
                await Case("card apply navigation departure", () => CardDepartureAsync(owner, wire, apply: true));
                await Case("fresh detail mutation reload", () => MutationReloadAsync(owner));
                if (failures.Count > 0) throw new AggregateException(failures);
                return;
            }
            await MatchChoicesAsync(owner, wire);
            await MatchInventoryAsync(owner, wire);
            await MatchBlockedScopesAsync(owner, wire);
            await CuratorAsync(owner, wire, auth);
            await SplitAsync(owner, wire);
            await SplitRetirementAsync(owner, wire);
            await DepartureAsync(owner, wire, client, auth);
            Program.Log("PASS: actual Match and Split controls preserve library scope across search, choice, preview and commit; retire stale inventory/selection/results; enforce curator/admin and account/profile ownership.");
        }
        finally { wire.SearchGate?.TrySetResult(); wire.PreviewGate?.TrySetResult(); wire.ApplyGate?.TrySetResult(); slot.SetValue(null, original); window.Close(); }
    }
    private static async Task UnmatchedWithoutChoiceAsync(FrameworkElement owner, Wire wire)
    {
        wire.Files = [("1", 2), ("2", 7), ("3", 2)]; var dialog = Split(owner); var showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            await Until(() => Libraries(dialog).Items.Count == 2); File(dialog, "1").IsChecked = true; File(dialog, "2").IsChecked = true;
            Check(!Search(dialog).IsEnabled, "Ambiguous split search must still require a library");
            All<CheckBox>(dialog).Single(box => box.Content as string == "Detach as unmatched (identify later)").IsChecked = true;
            await Until(() => dialog.IsPrimaryButtonEnabled);
            Check(wire.Last("split").Body.GetProperty("target").GetProperty("unmatched").GetBoolean(), "Unmatched preview lost target");
            Invoke(All<Button>(dialog).Single(button => button.Name == "PrimaryButton")); await Until(() => dialog.HasSaved); await showing;
            Check(wire.Last("split").Body.GetProperty("target").GetProperty("unmatched").GetBoolean(), "Unmatched commit requires a search target");
        }
        finally { dialog.Hide(); await showing; }
    }
    private static async Task EffectivePickerAsync(FrameworkElement owner, Wire wire)
    {
        wire.Files = [("1", 2), ("2", 7), ("3", 2)]; var match = Match(owner, 2); var showing = match.ShowAsync(); await Task.Delay(140);
        try { await Until(() => Search(match).IsEnabled); Check(Libraries(match).Visibility == Visibility.Collapsed, "Scoped Match offers ineffective library choices"); }
        finally { match.Hide(); await showing; }
        var split = Split(owner); var splitting = split.ShowAsync(); await Task.Delay(140);
        try
        {
            await Until(() => Libraries(split).Items.Count == 2); File(split, "1").IsChecked = true;
            Check(Libraries(split).Visibility == Visibility.Collapsed, "Shared selected-file library offers ineffective choices");
            File(split, "2").IsChecked = true; Check(Libraries(split).Visibility == Visibility.Visible, "Ambiguous selected files lost actual choices");
            Pick(split, 7); Check(Libraries(split).Visibility == Visibility.Visible && Search(split).IsEnabled, "Valid explicit choice disappears after picking");
        }
        finally { split.Hide(); await splitting; }
    }
    private static async Task InitialSplitRetryAsync(FrameworkElement owner, Wire wire)
    {
        wire.Files = [("1", 2), ("2", 7), ("3", 2)]; wire.DenyInventory = true;
        var dialog = Split(owner); var showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            Check(Refresh(dialog).IsEnabled, "Initial inventory failure disables its advertised retry");
            wire.DenyInventory = false; Invoke(Refresh(dialog)); await Until(() => Libraries(dialog).Items.Count == 2);
            await Until(() => All<CheckBox>(dialog).Any(box => AutomationProperties.GetName(box) == "Select /fixture/1.mkv"));
            File(dialog, "1").IsChecked = true;
            Check(File(dialog, "1").IsEnabled && Search(dialog).IsEnabled, "Successful retry does not restore editing/readiness");
            All<CheckBox>(dialog).Single(box => box.Content as string == "Detach as unmatched (identify later)").IsChecked = true;
            await Until(() => dialog.IsPrimaryButtonEnabled);
        }
        finally { wire.DenyInventory = false; dialog.Hide(); await showing; }
    }
    private static MatchItemDialog? PopupMatch(FrameworkElement owner) => VisualTreeHelper.GetOpenPopupsForXamlRoot(owner.XamlRoot).SelectMany(popup => All<MatchItemDialog>(popup.Child)).FirstOrDefault();
    private static async Task CardDepartureAsync(Grid root, Wire wire, bool apply)
    {
        wire.Files = [("1", 2), ("2", 2)]; var surface = new Grid(); root.Children.Add(surface); await Task.Delay(80);
        var method = typeof(MediaItemMenu).GetMethod("ShowMatchItemDialogAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var changes = 0; if (!apply) wire.DetailGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = wire.DetailReads;
        MatchItemDialog? active = null;
        object?[] args = [new MediaItem { ContentId = "fixture", Type = "movie", Title = "Fixture" }, new CatalogApi(WireClient()), SiloPlayer.App.Services.GetService(typeof(ToastService)), (Action)(() => changes++), root.XamlRoot];
        if (method.GetParameters().Length == 6) args = [.. args, surface];
        var action = (Task)method.Invoke(null, args)!;
        try
        {
            await Until(() => wire.DetailReads > reads);
            if (apply)
            {
                await Until(() => PopupMatch(root) != null); var dialog = active = PopupMatch(root)!; await Until(() => Search(dialog).IsEnabled); await SearchAndChooseAsync(dialog);
                wire.ApplyGate = new(TaskCreationOptions.RunContinuationsAsynchronously); var writes = wire.Count("apply"); Invoke(All<Button>(dialog).Single(button => button.Name == "ApplyMatchButton")); await Until(() => wire.Count("apply") > writes);
            }
            root.Children.Remove(surface); await Task.Delay(80); root.Children.Add(surface); await Task.Delay(80);
            wire.DetailGate?.TrySetResult(); wire.ApplyGate?.TrySetResult(); wire.DetailGate = wire.ApplyGate = null; await Task.Delay(160);
            Check(PopupMatch(root) == null, "Departed card read opened or retained an old Match dialog");
            Check(active?.HasAppliedMatch != true, "Departed card Apply publishes success using window authority");
            Check(changes == 0, "Departed card Apply invoked its old callback"); await action;
        }
        finally { wire.DetailGate?.TrySetResult(); wire.ApplyGate?.TrySetResult(); wire.DetailGate = wire.ApplyGate = null; PopupMatch(root)?.Hide(); await action; root.Children.Remove(surface); }
        static SiloApiClient WireClient() => (SiloApiClient)SiloPlayer.App.Services.GetService(typeof(SiloApiClient))!;
    }
    private static async Task MutationReloadAsync(Grid root)
    {
        var reads = 0; var cache = new ItemDetailPrefetchCache((id, _) => Task.FromResult(new MediaItemDetail { ContentId = id, Type = "movie", Title = "Fixture", UserState = new(), Versions = Enumerable.Range(1, ++reads == 1 ? 3 : 2).Select(n => new SiloPlayer.Core.Models.Playback.FileVersion { FileId = n }).ToList() }));
        var client = (SiloApiClient)SiloPlayer.App.Services.GetService(typeof(SiloApiClient))!;
        var vm = new ItemDetailViewModel(new CatalogApi(client), cache); await vm.LoadInLibraryAsync("fixture", null); var before = vm.Item!;
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!; var original = (IServiceProvider)slot.GetValue(null)!;
        slot.SetValue(null, new ViewModelServices(original, vm)); var page = new ItemDetailPage(); root.Children.Add(page); using var lifetime = new CancellationTokenSource();
        typeof(ItemDetailPage).GetField("_navigationCts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, lifetime);
        typeof(ItemDetailPage).GetField("_currentContentId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, "fixture"); await Task.Delay(80);
        var dialog = new SavedMutationDialog();
        var action = (Task)typeof(ItemDetailPage).GetMethod("ShowItemActionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, [before, dialog])!;
        try { await Task.Delay(140); Invoke(All<Button>(dialog).Single(button => button.Name == "PrimaryButton")); await action; Check(reads == 2 && vm.Item!.Versions.Count == 2, "Mutation completion reuses pre-split detail cache"); }
        finally { dialog.Hide(); root.Children.Remove(page); lifetime.Cancel(); CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(vm); slot.SetValue(null, original); }
    }
    private sealed class SavedMutationDialog : ItemScopedDialog
    {
        public SavedMutationDialog() : base("fixture", "Synthetic mutation", "", 400, "Save", AuthorizationPolicy.IsActingAdmin) { }
        protected override Task LoadAsync() => Task.CompletedTask;
        protected override Task SubmitAsync() { HasSaved = true; DispatcherQueue.TryEnqueue(Hide); return Task.CompletedTask; }
    }
    private sealed class ViewModelServices(IServiceProvider original, ItemDetailViewModel vm) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(ItemDetailViewModel) ? vm : original.GetService(type); }
    private static MatchItemDialog Match(FrameworkElement owner, int? library = null) => new("fixture", "Fixture", 2026, "movie", library, null, null) { XamlRoot = owner.XamlRoot };
    private static SplitVersionsDialog Split(FrameworkElement owner, int? library = null) => new(new MediaItemDetail { ContentId = "fixture", Title = "Fixture", Type = "movie" }, library) { XamlRoot = owner.XamlRoot };
    private static void Admin(AuthService auth) => auth.SetCurrentUser(new() { Id = "fixture", Role = "admin" });
    private static ComboBox Libraries(DependencyObject dialog) => All<ComboBox>(dialog).Single(box => AutomationProperties.GetName(box) == "Identity search library");
    private static Button Search(DependencyObject dialog) => All<Button>(dialog).Single(button => button.Name == "SearchButton" || button.Content as string == "Search");
    private static Button Candidate(DependencyObject dialog) => All<Button>(dialog).Single(button => AutomationProperties.GetName(button) == "Scoped candidate");
    private static Button Refresh(DependencyObject dialog) => All<Button>(dialog).Single(button => AutomationProperties.GetName(button) == "Refresh item libraries");
    private static void Pick(DependencyObject dialog, int library) => Libraries(dialog).SelectedItem = Libraries(dialog).Items.OfType<ComboBoxItem>().Single(item => (int)item.Tag == library);
    private static async Task SearchAndChooseAsync(DependencyObject dialog)
    {
        Invoke(Search(dialog)); await Until(() => All<Button>(dialog).Any(button => AutomationProperties.GetName(button) == "Scoped candidate"));
        Invoke(Candidate(dialog)); await Task.Delay(80);
    }
    private static async Task MatchChoicesAsync(FrameworkElement owner, Wire wire)
    {
        wire.Files = [("1", 2), ("2", 7), ("3", 2)]; var dialog = Match(owner); var showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            await Until(() => Libraries(dialog).Items.Count == 2);
            Check(!Search(dialog).IsEnabled, "Unscoped multi-library Match guesses a library");
            Pick(dialog, 7); await SearchAndChooseAsync(dialog);
            Check(wire.Last("search").Body.GetProperty("library_id").GetString() == "7", "Match search loses its selected library");
            var retired = Candidate(dialog); Pick(dialog, 2);
            Check(dialog.SelectedCandidate == null && !All<Button>(dialog).Any(button => AutomationProperties.GetName(button) == "Scoped candidate"), "Library choice retains candidates");
            Invoke(retired); Check(dialog.SelectedCandidate == null, "Retired Match candidate callback still selects");
            wire.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously); var count = wire.Count("search"); Invoke(Search(dialog)); await Until(() => wire.Count("search") > count);
            Pick(dialog, 7); wire.SearchGate.SetResult(); wire.SearchGate = null; await Until(() => Search(dialog).IsEnabled);
            Check(!All<Button>(dialog).Any(button => AutomationProperties.GetName(button) == "Scoped candidate"), "Delayed old-library Match results reappear");
            await SearchAndChooseAsync(dialog); Invoke(All<Button>(dialog).Single(button => button.Name == "ApplyMatchButton"));
            await Until(() => dialog.HasAppliedMatch); await showing;
            Check(wire.Last("apply").Body.GetProperty("library_id").GetString() == "7", "Match Apply uses another library than Search");
        }
        finally { wire.SearchGate?.TrySetResult(); wire.SearchGate = null; dialog.Hide(); await showing; }
    }
    private static async Task MatchInventoryAsync(FrameworkElement owner, Wire wire)
    {
        wire.Files = [("1", 2), ("2", 2)]; var dialog = Match(owner); var showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            await Until(() => Search(dialog).IsEnabled); await SearchAndChooseAsync(dialog);
            Check(wire.Last("search").Body.GetProperty("library_id").GetString() == "2", "Single-library Match is not inferred");
            wire.Files = [("1", 7), ("2", 7)]; var writes = wire.Count("apply"); Invoke(All<Button>(dialog).Single(button => button.Name == "ApplyMatchButton"));
            await Until(() => dialog.SelectedCandidate == null); Check(wire.Count("apply") == writes, "Changed inventory is applied before scope revalidation");
            await SearchAndChooseAsync(dialog); Check(wire.Last("search").Body.GetProperty("library_id").GetString() == "7", "New inventory searches old library");
        }
        finally { dialog.Hide(); await showing; }
        wire.Files = []; dialog = Match(owner); showing = dialog.ShowAsync(); await Task.Delay(140);
        try { await Until(() => Search(dialog).IsEnabled); await SearchAndChooseAsync(dialog); Check(!wire.Last("search").Body.TryGetProperty("library_id", out _), "Empty successful inventory invents a Match library"); }
        finally { dialog.Hide(); await showing; }
    }
    private static async Task MatchBlockedScopesAsync(FrameworkElement owner, Wire wire)
    {
        wire.Files = [("1", 2), ("2", 7)]; var dialog = Match(owner, 2); var showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            await Until(() => Search(dialog).IsEnabled); await SearchAndChooseAsync(dialog);
            wire.Files = [("1", 7), ("2", 9)]; Invoke(Refresh(dialog)); await Until(() => dialog.SelectedCandidate == null);
            Check(!Search(dialog).IsEnabled, "Explicit departed page scope falls back to another item library");
        }
        finally { dialog.Hide(); await showing; }
        wire.DenyInventory = true; dialog = Match(owner); showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            Check(!Search(dialog).IsEnabled, "Failed inventory read is treated as successful empty inventory");
            wire.DenyInventory = false; Invoke(Refresh(dialog)); await Until(() => Libraries(dialog).Items.Count == 2);
            Check(!Search(dialog).IsEnabled, "Recovered multi-library inventory skips choice"); Pick(dialog, 9); await Until(() => Search(dialog).IsEnabled);
        }
        finally { wire.DenyInventory = false; dialog.Hide(); await showing; }
    }
    private static async Task CuratorAsync(FrameworkElement owner, Wire wire, AuthService auth)
    {
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user", Permissions = [AuthorizationPolicy.MetadataCuration] });
        var reads = wire.FileReads; var dialog = Match(owner, 7); var showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            await Until(() => Search(dialog).IsEnabled); await SearchAndChooseAsync(dialog);
            Invoke(All<Button>(dialog).Single(button => button.Name == "ApplyMatchButton")); await Until(() => dialog.HasAppliedMatch); await showing;
            Check(wire.FileReads == reads, "Curator Match fetches administrator inventory"); Check(wire.Last("apply").Body.GetProperty("library_id").GetString() == "7", "Scoped curator Match loses scope");
        }
        finally { dialog.Hide(); await showing; }
        dialog = Match(owner); showing = dialog.ShowAsync(); await Task.Delay(140);
        try { await Until(() => Search(dialog).IsEnabled); await SearchAndChooseAsync(dialog); Check(!wire.Last("search").Body.TryGetProperty("library_id", out _), "Unscoped curator Match invents scope"); }
        finally { dialog.Hide(); await showing; }
        var split = Split(owner); var splitShowing = split.ShowAsync(); await Task.Delay(140);
        try { await Task.Delay(100); Check(wire.FileReads == reads && !split.IsPrimaryButtonEnabled, "Curator can load administrator-only Split inventory"); }
        finally { split.Hide(); await splitShowing; Admin(auth); }
    }
    private static CheckBox File(DependencyObject dialog, string id) => All<CheckBox>(dialog).Single(check => AutomationProperties.GetName(check) == $"Select /fixture/{id}.mkv");
    private static async Task SplitAsync(FrameworkElement owner, Wire wire)
    {
        wire.Files = [("1", 2), ("2", 7), ("3", 7)]; var dialog = Split(owner, 2); var showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            await Until(() => All<CheckBox>(dialog).Any(check => AutomationProperties.GetName(check) == "Select /fixture/2.mkv"));
            File(dialog, "2").IsChecked = true; await SearchAndChooseAsync(dialog);
            Check(wire.Last("search").Body.GetProperty("library_id").GetString() == "7", "Split ignores selected-files library");
            await Until(() => dialog.IsPrimaryButtonEnabled);
            Check(wire.Last("split").Body.GetProperty("dry_run").GetBoolean(), "Split enables commit without a preview");
            var retired = Candidate(dialog);
            File(dialog, "1").IsChecked = true;
            Check(!dialog.IsPrimaryButtonEnabled, "Split selection retains an accepted preview");
            Invoke(retired); await Task.Delay(60); Check(!dialog.IsPrimaryButtonEnabled, "Retired Split candidate callback accepts a different selection");
            await SearchAndChooseAsync(dialog); Check(wire.Last("search").Body.GetProperty("library_id").GetString() == "2", "Split spanning libraries ignores page scope");
            await Until(() => dialog.IsPrimaryButtonEnabled); Invoke(All<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
            await Until(() => dialog.HasSaved); await showing;
            var commit = wire.Last("split").Body; Check(!commit.GetProperty("dry_run").GetBoolean() && commit.GetProperty("file_ids").GetArrayLength() == 2 && !commit.TryGetProperty("library_id", out _), "Split commit changes its previewed request contract");
        }
        finally { dialog.Hide(); await showing; }
    }
    private static async Task SplitRetirementAsync(FrameworkElement owner, Wire wire)
    {
        wire.Files = [("1", 2), ("2", 7), ("3", 2)]; var dialog = Split(owner); var showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            await Until(() => Libraries(dialog).Items.Count == 2); File(dialog, "1").IsChecked = true; File(dialog, "2").IsChecked = true;
            Check(!Search(dialog).IsEnabled, "Cross-library Split without page scope skips choice"); Pick(dialog, 7);
            wire.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously); var searches = wire.Count("search"); Invoke(Search(dialog)); await Until(() => wire.Count("search") > searches);
            File(dialog, "2").IsChecked = false; wire.SearchGate.SetResult(); wire.SearchGate = null; await Until(() => Search(dialog).IsEnabled);
            Check(!All<Button>(dialog).Any(button => AutomationProperties.GetName(button) == "Scoped candidate"), "Delayed Split search survives selected-files change");
            wire.PreviewGate = new(TaskCreationOptions.RunContinuationsAsynchronously); await SearchAndChooseAsync(dialog); var previews = wire.Count("split"); await Until(() => wire.Count("split") > previews);
            wire.Files = [("1", 7), ("2", 7), ("3", 2)]; Invoke(Refresh(dialog)); await Until(() => !All<Button>(dialog).Any(button => AutomationProperties.GetName(button) == "Scoped candidate"));
            wire.PreviewGate.SetResult(); wire.PreviewGate = null; await Task.Delay(120); Check(!dialog.IsPrimaryButtonEnabled, "Delayed Split preview survives inventory departure");
            await SearchAndChooseAsync(dialog); await Until(() => dialog.IsPrimaryButtonEnabled);
            var commits = wire.Commits; wire.Files = [("1", 2), ("2", 7), ("3", 2)]; Invoke(All<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
            await Until(() => !dialog.IsPrimaryButtonEnabled); await Task.Delay(100); Check(wire.Commits == commits && !dialog.HasSaved, "Split commits a changed-inventory preview");
        }
        finally { wire.SearchGate?.TrySetResult(); wire.SearchGate = null; wire.PreviewGate?.TrySetResult(); wire.PreviewGate = null; dialog.Hide(); await showing; }
    }
    private static async Task DepartureAsync(FrameworkElement owner, Wire wire, SiloApiClient client, AuthService auth)
    {
        foreach (var departure in new[] { "profile", "account", "permission", "permission-return", "navigation" })
        {
            Admin(auth); client.ClearProfile(); wire.Files = [("1", 2), ("2", 2)]; var dialog = Match(owner); using var navigation = new CancellationTokenSource(); dialog.BindNavigation(navigation.Token); var showing = dialog.ShowAsync(); await Task.Delay(140);
            try
            {
                await Until(() => Search(dialog).IsEnabled); await SearchAndChooseAsync(dialog);
                wire.ApplyGate = new(TaskCreationOptions.RunContinuationsAsynchronously); var writes = wire.Count("apply"); Invoke(All<Button>(dialog).Single(button => button.Name == "ApplyMatchButton")); await Until(() => wire.Count("apply") > writes);
                if (departure == "profile") client.SetProfile("other");
                else if (departure == "account") auth.SetCurrentUser(new() { Id = "other", Role = "admin" });
                else if (departure is "permission" or "permission-return") { auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); if (departure == "permission-return") Admin(auth); }
                else navigation.Cancel();
                wire.ApplyGate.SetResult(); wire.ApplyGate = null; await Task.Delay(140);
                Check(!dialog.HasAppliedMatch && dialog.AppliedContentId == null, "Delayed Match Apply publishes success after " + departure + " departure");
            }
            finally { wire.ApplyGate?.TrySetResult(); wire.ApplyGate = null; dialog.Hide(); await showing; }
        }
        Admin(auth); client.ClearProfile();
        wire.Files = [("1", 2), ("2", 2)]; var split = Split(owner); var splitShowing = split.ShowAsync(); await Task.Delay(140);
        try
        {
            await Until(() => Search(split).IsEnabled); File(split, "1").IsChecked = true; await SearchAndChooseAsync(split); await Until(() => split.IsPrimaryButtonEnabled);
            var writes = wire.Commits; auth.SetCurrentUser(new() { Id = "fixture", Role = "user", Permissions = [AuthorizationPolicy.MetadataCuration] }); await Task.Delay(60);
            Check(!split.IsPrimaryButtonEnabled, "Split retains administrator authority after demotion");
            try { Invoke(All<Button>(split).Single(button => button.Name == "PrimaryButton")); }
            catch (System.Runtime.InteropServices.COMException error) when ((uint)error.HResult == 0x80040200) { /* Native UI Automation refuses a disabled command. */ }
            await Task.Delay(60); Check(wire.Commits == writes, "Demoted curator commits Split");
        }
        finally { split.Hide(); await splitShowing; Admin(auth); }
        foreach (var departure in new[] { "profile", "account", "permission", "navigation" })
        {
            Admin(auth); client.ClearProfile(); using var navigation = new CancellationTokenSource(); split = Split(owner); split.BindNavigation(navigation.Token); splitShowing = split.ShowAsync(); await Task.Delay(140);
            try
            {
                await Until(() => Search(split).IsEnabled); File(split, "1").IsChecked = true; await SearchAndChooseAsync(split); await Until(() => split.IsPrimaryButtonEnabled);
                wire.CommitGate = new(TaskCreationOptions.RunContinuationsAsynchronously); var commits = wire.Commits;
                Invoke(All<Button>(split).Single(button => button.Name == "PrimaryButton")); await Until(() => wire.Commits > commits);
                if (departure == "profile") client.SetProfile("other");
                else if (departure == "account") auth.SetCurrentUser(new() { Id = "other", Role = "admin" });
                else if (departure == "permission") auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
                else navigation.Cancel();
                wire.CommitGate.SetResult(); wire.CommitGate = null; await Task.Delay(140);
                Check(!split.HasSaved, "Delayed Split commit publishes success after " + departure + " departure");
            }
            finally { wire.CommitGate?.TrySetResult(); wire.CommitGate = null; split.Hide(); await splitShowing; }
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 250; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Identity scope controls did not settle"); }
    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject { if (node is T value) yield return value; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(node, i))) yield return child; }
    private sealed class Services(IServiceProvider original, SiloApiClient client, AuthService auth) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(AuthService) ? auth : type == typeof(MediaMaintenanceApi) ? new MediaMaintenanceApi(client) : original.GetService(type); }
    private sealed record Call(string Path, JsonElement Body);
    private sealed class Wire : HttpMessageHandler
    {
        public (string Id, int Library)[] Files = []; public List<Call> Calls = []; public int FileReads, DetailReads; public bool DenyInventory; public TaskCompletionSource? SearchGate, PreviewGate, ApplyGate, CommitGate, DetailGate;
        public int Count(string suffix) => Calls.Count(call => call.Path.EndsWith("/" + suffix));
        public Call Last(string suffix) => Calls.Last(call => call.Path.EndsWith("/" + suffix));
        public int Commits => Calls.Count(call => call.Path.EndsWith("/split") && !call.Body.GetProperty("dry_run").GetBoolean());
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; string json;
            if (path == "/api/v2/catalog/items/fixture") { DetailReads++; if (DetailGate != null) await DetailGate.Task; return new(HttpStatusCode.OK) { Content = new StringContent("{\"content_id\":\"fixture\",\"title\":\"Fixture\",\"type\":\"movie\",\"user_state\":{}}") }; }
            if (path.EndsWith("/files") && DenyInventory) { FileReads++; return new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"error\":\"forbidden\"}") }; }
            if (path.EndsWith("/files")) { FileReads++; json = JsonSerializer.Serialize(new { items = Files.Select(file => new { id = file.Id, library_id = file.Library.ToString(), observed_root_path = "/fixture", file_path = $"/fixture/{file.Id}.mkv" }), page = new { has_more = false } }); }
            else
            {
                var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)); Calls.Add(new(path, body));
                if (path.EndsWith("/search")) { if (SearchGate != null) await SearchGate.Task; json = "{\"candidates\":[{\"title\":\"Scoped candidate\",\"year\":2026,\"provider_ids\":{\"tmdb\":\"42\"}}]}"; }
                else if (path.EndsWith("/apply")) { if (ApplyGate != null) await ApplyGate.Task; json = "{\"content_id\":\"fixture-target\",\"updated\":true}"; }
                else { if (body.GetProperty("dry_run").GetBoolean() && PreviewGate != null) await PreviewGate.Task; if (!body.GetProperty("dry_run").GetBoolean() && CommitGate != null) await CommitGate.Task; json = JsonSerializer.Serialize(new { dry_run = body.GetProperty("dry_run").GetBoolean(), files_moved = body.GetProperty("file_ids").GetArrayLength(), target_content_id = "fixture-target", target_created = true, reattribution = new { progress_moved = 1, history_moved = 1, history_ambiguous = 0, downloads = 0 } }); }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
