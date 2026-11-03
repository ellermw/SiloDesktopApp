using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class SidebarReviewNativeFixture
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> Failures = [];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static object Pins = PinMap("Pin");
    private static string MenuLabel = "Current menu";
    private static TaskCompletionSource<object>? HeldPins, HeldLibraries, HeldMenuRead, HeldMenuWrite;
    private static int PinReads, PinWrites, LibraryReads, MenuReads, MenuWrites;
    private static object PinMap(string label, bool duplicate = false) => new Dictionary<string, object> { ["1"] = duplicate
        ? new[] { new { type = "collection", id = "pin", label }, new { type = "collection", id = "pin", label = "Later duplicate label" } }
        : new[] { new { type = "collection", id = "pin", label } } };
    private static object Menu(string label) => new { items = new object[] { new { type = "builtin", destination = "home" }, new { type = "library", library_id = 1, label } } };
    private static object EffectiveMenu() => new { items = new[] { new { key = "nav.primary_menu", value = Menu(MenuLabel), source = "profile_client" } } };
    private static object Libraries() => new { items = new[] { new { id = 1, name = "One", type = "movies" }, new { id = 2, name = "Two", type = "series" } } };
    private static TaskCompletionSource<object> Hold() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static NavigationViewItem[] Rows(NavigationView nav) => nav.MenuItems.OfType<NavigationViewItem>().ToArray();
    private static NavigationViewItem[] LibraryRows(NavigationView nav) => Rows(nav).Where(n => n.Tag is Library).ToArray();
    private static NavigationViewItem[] PrimaryRows(NavigationView nav) => Rows(nav).Where(n => n.Tag?.GetType().Name == "PrimaryMenuNavTag").ToArray();
    private static NavigationViewItem Home(NavigationView nav) => PrimaryRows(nav).Single(n => Equals(n.Content, "Home"));
    private static Button Unpin(NavigationViewItem row) => ((Grid)((NavigationViewItem)row.MenuItems[0]).Content).Children.OfType<Button>().Single();
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Check(bool value, string description) { Program.Log((value ? "PASS: " : "FAIL: ") + description); if (!value) Failures.Add(description); }
    private static async Task Drain() => await Task.Delay(220);
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 220 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Sidebar review fixture state did not arrive."); }
    private static void Call(MainWindow window, string method, params object[] args) => typeof(MainWindow).GetMethod(method, Instance)!.Invoke(window, args);
    internal static async Task RunAsync(MainWindow window, IServiceProvider provider)
    {
        var mode = Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY")!;
        var nav = (NavigationView)((FrameworkElement)window.Content).FindName("NavView");
        var api = provider.GetRequiredService<SiloApiClient>();
        var navigation = provider.GetRequiredService<NavigationService>();
        var vm = provider.GetRequiredService<MainViewModel>();
        var service = provider.GetRequiredService<UICustomizationService>();
        typeof(MainWindow).GetField("_playerPrewarmed", Instance)!.SetValue(window, true);
        if (mode.EndsWith("initial", StringComparison.Ordinal)) HeldMenuRead = Hold();
        if (mode.EndsWith("duplicates", StringComparison.Ordinal)) Pins = PinMap("Duplicate", duplicate: true);
        if (mode.EndsWith("move", StringComparison.Ordinal)) Pins = new Dictionary<string, object> { ["2"] = new[] { new { type = "section", id = "second-pin", label = "Second pin" } } };
        window.ShowMainNavigation();
        await Until(() => LibraryRows(nav).Length == 2);
        if (HeldMenuRead == null && !mode.EndsWith("duplicates", StringComparison.Ordinal)) await Until(() => PrimaryRows(nav).Length == 2);
        if (mode.EndsWith("save", StringComparison.Ordinal) || mode.EndsWith("reset", StringComparison.Ordinal) || mode.EndsWith("clear", StringComparison.Ordinal))
        {
            navigation.Navigate<SettingsPage>();
            await Until(() => navigation.Frame!.Content is SettingsPage page && page.IsLoaded);
            var page = (SettingsPage)navigation.Frame!.Content;
            page.SelectSettingsSection("Interface"); await Drain();
            var draft = (List<PrimaryMenuItem>)typeof(SettingsPage).GetField("_interfaceMenuDraft", Instance)!.GetValue(page)!;
            draft.Clear(); draft.AddRange([PrimaryMenuItem.Builtin("home"), new() { Type = "library", LibraryId = 1, Label = "Outgoing menu" }]);
            typeof(SettingsPage).GetField("_interfaceMenuDirty", Instance)!.SetValue(page, true);
            typeof(SettingsPage).GetMethod("SyncInterfaceControls", Instance)!.Invoke(page, null);
            HeldMenuWrite = Hold();
            var reset = mode.EndsWith("reset", StringComparison.Ordinal);
            var clear = mode.EndsWith("clear", StringComparison.Ordinal);
            if (reset || clear)
                typeof(SettingsPage).GetMethod(clear ? "ClearInterfaceMenuDeviceOverride_Click" : "ResetInterfaceMenu_Click", Instance)!.Invoke(page, [page, new RoutedEventArgs()]);
            else Invoke((Button)page.FindName("SaveInterfaceMenuButton"));
            await Until(() => MenuWrites > 0);
            navigation.Navigate<Page>();
            MenuLabel = "New profile menu";
            provider.GetRequiredService<AuthService>().SelectProfile("second", profile: new() { Id = "second", Name = "Second" }); window.ShowMainNavigation();
            await Until(() => PrimaryRows(nav).Any(n => Equals(n.Content, MenuLabel))); await Drain();
            var reads = MenuReads;
            HeldMenuWrite.SetResult(new { }); HeldMenuWrite = null; await Drain();
            Check(PrimaryRows(nav).Any(n => Equals(n.Content, "New profile menu")) && !PrimaryRows(nav).Any(n => Equals(n.Content, "Outgoing menu")), "outgoing actual Settings " + (reset ? "reset" : "save") + " completion cannot overwrite new profile custom menu");
            Check(service.PrimaryMenu!.Items.Last().Label == "New profile menu", "singleton customization remains owned by hydrated new profile");
            if (reset || clear) Check(MenuReads == reads, "outgoing reset/clear completion cannot start a new-profile refresh");
            Check(draft.Last().Label == "Outgoing menu", "retired Settings caller cannot replace its draft with another profile's state");
        }
        else if (mode.EndsWith("home-initial", StringComparison.Ordinal))
        {
            await Until(() => MenuReads > 0);
            navigation.Navigate<HomePage>(); await Until(() => navigation.Frame!.Content is HomePage page && page.IsLoaded && !page.ViewModel.IsLoading);
            var library = LibraryRows(nav)[0]; var pin = (NavigationViewItem)library.MenuItems[0];
            var stale = EffectiveMenu(); var original = HeldMenuRead!; HeldMenuRead = Hold();
            var current = HeldMenuRead; MenuLabel = "Current catch-up menu";
            Call(window, "OnReadCatchUpRequested", api.InvalidateAccessContext()); await Drain();
            original.SetResult(stale); await Drain();
            var ready = typeof(MainWindow).GetField("_sidebarCustomizationReady", Instance)!;
            Check(MenuReads == 2, "loaded Home request-only catch-up rearms unfinished customization with one current read");
            Check(!(bool)ready.GetValue(window)! && PrimaryRows(nav).Length == 0,
                "stale initial response cannot mark current shell customization ready while replacement is held");
            current.SetResult(EffectiveMenu()); HeldMenuRead = null; await Drain();
            Check((bool)ready.GetValue(window)! && PrimaryRows(nav).Any(n => Equals(n.Content, "Current catch-up menu")) &&
                !PrimaryRows(nav).Any(n => Equals(n.Content, "Current menu")), "current catch-up response publishes only current primary menu data");
            var reads = MenuReads;
            window.ShowMainNavigation();
            Call(window, "OnReadCatchUpRequested", api.InvalidateAccessContext()); await Drain();
            Check(MenuReads == reads, "same-shell Show and Home catch-up after readiness add no customization reads");
            Check(ReferenceEquals(LibraryRows(nav)[0], library) && ReferenceEquals(library.MenuItems[0], pin) && pin.IsEnabled,
                "initial Home catch-up preserves same-identity library and pin nodes");
            var writes = PinWrites; Invoke(Unpin(library)); await Drain();
            Check(PinWrites == writes + 1, "retained pin callback stays useful after initial and ready Home catch-up");
        }
        else if (mode.EndsWith("initial", StringComparison.Ordinal))
        {
            await Until(() => MenuReads > 0);
            var original = HeldMenuRead!; HeldMenuRead = null;
            Call(window, "OnAccessChanged", api.InvalidateAccessContext()); await Drain();
            original.SetResult(EffectiveMenu()); await Drain();
            Check(MenuReads >= 2 && PrimaryRows(nav).Length == 2, "access during initial customization read restarts current load and readiness publication");
        }
        else if (mode.EndsWith("catchup", StringComparison.Ordinal))
        {
            navigation.Navigate<HomePage>(); await Until(() => navigation.Frame!.Content is HomePage page && page.IsLoaded && !page.ViewModel.IsLoading);
            var library = LibraryRows(nav)[0]; var pin = (NavigationViewItem)library.MenuItems[0];
            HeldPins = Hold(); Invoke(Unpin(library)); await Until(() => PinReads > 1);
            Call(window, "OnReadCatchUpRequested", api.InvalidateAccessContext()); await Drain();
            HeldPins.SetResult(new { value = Pins }); HeldPins = null; await Drain();
            Check(ReferenceEquals(LibraryRows(nav)[0].MenuItems[0], pin) && pin.IsEnabled, "held unpin GET retired by Home-only catch-up restores surviving actual row enablement");
            var writes = PinWrites; Invoke(Unpin(library)); await Drain();
            Check(PinWrites == writes + 1, "surviving row can unpin again after aborted operation");
        }
        else if (mode.EndsWith("duplicates", StringComparison.Ordinal))
        {
            await Drain(); var library = LibraryRows(nav)[0];
            Check(library.MenuItems.Count == 1, "accepted duplicate pin snapshot produces one semantic node");
            Check(((Grid)((NavigationViewItem)library.MenuItems[0]).Content).Children.OfType<TextBlock>().Single().Text == "Duplicate" && PinWrites == 0,
                "duplicate presentation keeps first label without automatic settings writes");
            // Existing duplicated nodes can survive in a running shell from an
            // earlier snapshot. Recovery must not throw on the old vector.
            var old = (NavigationViewItem)library.MenuItems[0];
            library.MenuItems.Add(new NavigationViewItem { Tag = old.Tag, Content = "Old duplicate" });
            Pins = PinMap("Recovered"); await window.RefreshSidebarPinsAsync(); await Drain();
            Check(library.MenuItems.Count == 1 && ((Grid)((NavigationViewItem)library.MenuItems[0]).Content).Children.OfType<TextBlock>().Single().Text == "Recovered", "clean snapshot recovers actual already-duplicated old pin vector");
            var vectors = 0; ((Windows.Foundation.Collections.IObservableVector<object>)library.MenuItems).VectorChanged += (_, _) => vectors++;
            await window.RefreshSidebarPinsAsync(); await Drain(); Check(vectors == 0, "duplicate recovery remains stable on repeated publication");
        }
        else if (mode.EndsWith("move", StringComparison.Ordinal))
        {
            var moved = LibraryRows(nav)[1]; moved.IsExpanded = true; nav.SelectedItem = moved; moved.Focus(FocusState.Keyboard); await Drain();
            Check(moved.FocusState == FocusState.Keyboard && moved.IsExpanded && ReferenceEquals(nav.SelectedItem, moved), "fixture establishes focus selection and expansion on the node that will move");
            var originalIndex = nav.MenuItems.IndexOf(moved); var removed = new List<uint>();
            ((Windows.Foundation.Collections.IObservableVector<object>)nav.MenuItems).VectorChanged += (_, e) => { if (e.CollectionChange == Windows.Foundation.Collections.CollectionChange.ItemRemoved) removed.Add(e.Index); };
            var order = vm.Libraries.Reverse().ToArray(); vm.Libraries.Clear(); foreach (var library in order) vm.Libraries.Add(library);
            window.DispatcherQueue.TryEnqueue(() => LogFocus(nav, "after publication")); await Drain(); LogFocus(nav, "settled");
            Check(removed.Contains((uint)originalIndex) && ReferenceEquals(LibraryRows(nav)[0], moved), "focused library node is actually removed and reinserted during reorder");
            Check(moved.FocusState == FocusState.Keyboard && ReferenceEquals(nav.SelectedItem, moved) && moved.IsExpanded, $"actually moved native node retains keyboard focus selection and expansion ({moved.FocusState}/{ReferenceEquals(nav.SelectedItem, moved)}/{moved.IsExpanded})");
            var nextMoved = LibraryRows(nav)[1]; nextMoved.Focus(FocusState.Keyboard); await Drain();
            order = vm.Libraries.Reverse().ToArray(); vm.Libraries.Clear(); foreach (var library in order) vm.Libraries.Add(library);
            var home = Home(nav); window.DispatcherQueue.TryEnqueue(() => home.Focus(FocusState.Keyboard)); await Drain();
            Check(home.FocusState == FocusState.Keyboard && nextMoved.FocusState == FocusState.Unfocused, "newer explicit focus choice wins over delayed moved-node restoration");
            nextMoved = LibraryRows(nav)[1]; nextMoved.Focus(FocusState.Keyboard); await Drain();
            order = vm.Libraries.Reverse().ToArray(); vm.Libraries.Clear(); foreach (var library in order) vm.Libraries.Add(library);
            window.DispatcherQueue.TryEnqueue(window.HideMainNavigation); await Drain();
            Check(LibraryRows(nav).Length == 0 && nextMoved.FocusState == FocusState.Unfocused, "shell retirement cancels pending native focus restoration");
        }
        else
        {
            var home = Home(nav); home.Focus(FocusState.Keyboard); nav.SelectedItem = home; await Drain();
            var library = LibraryRows(nav)[0]; var outgoing = Unpin(library); var plugin = Rows(nav).Single(n => n.Tag?.GetType().Name == "PluginAppNavTag");
            var expanded = mode.EndsWith("expanded", StringComparison.Ordinal);
            var player = provider.GetRequiredService<PlayerService>();
            if (expanded) typeof(PlayerService).GetProperty(nameof(PlayerService.State))!.SetValue(player, PlayerState.Expanded);
            HeldLibraries = Hold(); var reads = LibraryReads;
            Call(window, "OnAccessChanged", api.InvalidateAccessContext()); await Drain();
            Check(!LibraryRows(nav).Contains(library), "explicit access retires old library/pin rows " + (expanded ? "during expanded playback" : "before held replacement read"));
            Check(Rows(nav).Contains(home) && home.FocusState == FocusState.Keyboard, "access-only retirement preserves valid builtin primary identity and keyboard focus");
            var pinReads = PinReads; var writes = PinWrites; Invoke(outgoing); await Drain();
            Check(PinReads == pinReads && PinWrites == writes, "retired actual unpin button cannot read/write under access replacement authority");
            Check(Rows(nav).Contains(plugin), "access retirement preserves same-viewer plugin route");
            if (expanded) Check(player.State == PlayerState.Expanded && LibraryReads == reads, "expanded access retirement leaves playback state and defers hidden hydration");
            if (expanded) typeof(PlayerService).GetProperty(nameof(PlayerService.State))!.SetValue(player, PlayerState.Idle);
            HeldLibraries.SetResult(Libraries()); HeldLibraries = null;
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Sidebar review failures: " + string.Join("; ", Failures));
    }
    private static void LogFocus(NavigationView nav, string stage)
    {
        var focus = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(nav.XamlRoot) as FrameworkElement;
        Program.Log($"Focus {stage}: {focus?.GetType().Name}/{focus?.Name}/{focus?.Tag}/{focus?.FocusState}");
    }
    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json") };
    internal static async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/v2/auth/me") return Json(new { id = "fixture", username = "Fixture", role = "user" });
        if (path == "/api/v2/user/libraries") { LibraryReads++; return Json(HeldLibraries == null ? Libraries() : await HeldLibraries.Task); }
        if (path.Contains("sidebar_pins"))
        {
            if (request.Method == HttpMethod.Get) { PinReads++; return Json(HeldPins == null ? new { value = Pins } : await HeldPins.Task); }
            PinWrites++; return Json(new { });
        }
        if (path.Contains("nav.primary_menu") && request.Method != HttpMethod.Get)
        { MenuWrites++; return Json(HeldMenuWrite == null ? new { } : await HeldMenuWrite.Task); }
        if (path == "/api/v2/settings/values/effective" && request.RequestUri.Query.Contains("nav.primary_menu"))
        { MenuReads++; return Json(HeldMenuRead == null ? EffectiveMenu() : await HeldMenuRead.Task); }
        if (path == "/api/v2/settings/contract/capabilities") return Json(new { api_version = 1, manifest_revision = 22, client_families = new[] { "desktop", "web" }, supports_batched_effective = true, supports_idempotent_writes = true });
        if (path == "/api/v2/settings/plugins") return Json(new { items = new[] { new { id = 7, plugin_id = "Fixture", routes = new[] { new { path = "/fixture", navigable = true, navigation_kind = "user", navigation_label = "Fixture" } } } } });
        return Json(new { items = Array.Empty<object>(), settings = Array.Empty<object>(), sections = Array.Empty<object>(), devices = Array.Empty<object>(), providers = Array.Empty<object>() });
    }
}
