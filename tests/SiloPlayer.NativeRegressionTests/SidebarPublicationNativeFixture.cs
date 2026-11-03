using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class SidebarPublicationNativeFixture
{
    private static readonly List<string> Failures = [];
    private static List<Library> Libraries = [Lib(1, "Movies"), Lib(2, "Series", "series"), Lib(3, "Hidden")];
    private static object Pins = new Dictionary<string, object> { ["1"] = new[] { new { type = "collection", id = "pin-a", label = "First pin" } } };
    private static TaskCompletionSource<HttpResponseMessage>? HeldLibraries, HeldPins;
    private static TaskCompletionSource<HttpResponseMessage>? HeldWrite;
    private static TaskCompletionSource<HttpResponseMessage>? HeldPlugins;
    private static bool LibraryRead, PinRead;
    private static int LibraryReadCount;
    private static int PinReadCount;
    private static string? LastWrite;
    private static object User = new { id = "fixture", username = "Fixture", role = "user" };
    private static Library Lib(int id, string name, string type = "movies") => new() { Id = id, Name = name, Type = type };
    private static NavigationViewItem[] Nodes(NavigationView nav) => nav.MenuItems.OfType<NavigationViewItem>().ToArray();
    private static NavigationViewItem[] LibraryNodes(NavigationView nav) => Nodes(nav).Where(n => n.Tag is Library).ToArray();
    private static NavigationViewItem[] PrimaryNodes(NavigationView nav) => Nodes(nav).Where(n => n.Tag?.GetType().Name == "PrimaryMenuNavTag").ToArray();
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    internal static async Task RunAsync(MainWindow window, IServiceProvider provider)
    {
        var nav = (NavigationView)((FrameworkElement)window.Content).FindName("NavView");
        var vm = provider.GetRequiredService<MainViewModel>();
        var catalog = provider.GetRequiredService<CatalogApi>();
        var auth = provider.GetRequiredService<AuthService>();
        var settings = provider.GetRequiredService<SettingsService>();
        var preferences = settings.Load(); preferences.HiddenLibraryIds.Add(3); settings.Save(preferences);
        preferences.TasteSeedDismissedProfileIds.AddRange(["third", "fourth", "fifth"]); settings.Save(preferences);
        // Keep this shell-only fixture from creating a player host.
        typeof(MainWindow).GetField("_playerPrewarmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.ShowMainNavigation();
        await Until(() => LibraryNodes(nav).Length == 2 && LibraryNodes(nav)[0].MenuItems.Count == 1);
        var custom = provider.GetRequiredService<UICustomizationService>();
        await custom.EnsureLoadedAsync();
        await custom.SavePrimaryMenuAsync([PrimaryMenuItem.Builtin("home"), new() { Type = "library", LibraryId = 1, Label = "Shortcut" }]);
        await Drain();
        var first = LibraryNodes(nav)[0]; var second = LibraryNodes(nav)[1];
        var pin = (NavigationViewItem)first.MenuItems[0];
        var primary = PrimaryNodes(nav).Last();
        var plugin = Nodes(nav).Single(n => n.Tag?.GetType().Name == "PluginAppNavTag");
        var requests = Nodes(nav).Single(n => Equals(n.Tag, "Requests"));
        Check(!LibraryNodes(nav).Any(n => ((Library)n.Tag).Id == 3), "local hidden library remains filtered");
        var fixedNodes = Nodes(nav).Where(n => n.Tag is string).ToArray();
        first.IsExpanded = true; nav.SelectedItem = first; first.Focus(FocusState.Keyboard);
        await Drain();
        Check(first.FocusState != FocusState.Unfocused, "fixture establishes native keyboard focus");
        var changes = 0; var childChanges = 0;
        ((Windows.Foundation.Collections.IObservableVector<object>)nav.MenuItems).VectorChanged += (_, _) => changes++;
        ((Windows.Foundation.Collections.IObservableVector<object>)first.MenuItems).VectorChanged += (_, _) => childChanges++;
        HeldPins = NewHeld(); PinRead = false;
        catalog.InvalidateLibraryCache();
        await vm.ReloadLibrariesAsync();
        var pinLoad = window.RefreshSidebarPinsAsync();
        await Until(() => PinRead); await Drain();
        Check(changes == 0 && childChanges == 0, $"unchanged Clear/Add with held pins causes zero vector changes ({changes}/{childChanges})");
        Check(ReferenceEquals(LibraryNodes(nav)[0], first) && ReferenceEquals(first.MenuItems[0], pin), "unchanged refresh retains actual library and pin nodes");
        Check(ReferenceEquals(PrimaryNodes(nav).Last(), primary), "unchanged refresh retains primary shortcut node");
        Check(first.IsExpanded && ReferenceEquals(nav.SelectedItem, first) && first.FocusState != FocusState.Unfocused, "unchanged refresh retains expansion selection and keyboard focus");
        HeldPins.SetResult(Json(new { value = Pins })); HeldPins = null; await pinLoad; await Drain();
        Check(changes == 0 && childChanges == 0, "unchanged pin arrival does not churn either vector");

        Libraries = [Lib(2, "Renamed series", "audiobooks"), Lib(1, "Renamed movies", "series"), Lib(4, "Added")];
        Libraries[1].MetadataLanguage = "fr";
        Pins = new Dictionary<string, object> { ["1"] = new[] { new { type = "collection", id = "pin-a", label = "Renamed pin" }, new { type = "section", id = "pin-b", label = "New pin" } } };
        catalog.InvalidateLibraryCache(); await vm.ReloadLibrariesAsync(); await window.RefreshSidebarPinsAsync(); await Drain();
        var current = LibraryNodes(nav); var currentFirst = current.Single(n => ((Library)n.Tag).Id == 1);
        Check(current.Select(n => ((Library)n.Tag).Id).SequenceEqual([2, 1, 4]), "add and reorder follow latest library order");
        Check(ReferenceEquals(current[0], second) && ReferenceEquals(currentFirst, first), "reorder and rename retain surviving library nodes");
        Check((string)currentFirst.Content == "Renamed movies" && ((Library)currentFirst.Tag).Type == "series" && ((Library)current[0].Tag).Type == "audiobooks", "rename/type replace current labels and metadata");
        Check(ReferenceEquals(currentFirst.Tag, vm.Libraries.Single(library => library.Id == 1)) && ((Library)currentFirst.Tag).MetadataLanguage == "fr", "library tag references latest complete API object");
        Check(ReferenceEquals(currentFirst.MenuItems[0], pin) && ((Grid)((NavigationViewItem)currentFirst.MenuItems[0]).Content).Children.OfType<TextBlock>().Single().Text == "Renamed pin", "pin rename preserves actual node and updates content");
        Check(ReferenceEquals(PrimaryNodes(nav).Last(), primary), "library metadata refresh retains custom primary node");
        Check(fixedNodes.All(n => Nodes(nav).Contains(n)), "fixed navigation identities survive reconciliation");
        Check(Nodes(nav).Contains(plugin) && requests.Visibility == Visibility.Collapsed, "plugin Apps node and disabled capability visibility survive library publication");
        await custom.SavePrimaryMenuAsync([PrimaryMenuItem.Builtin("home"), new() { Type = "library", LibraryId = 1, Label = "Latest shortcut" }]);
        await Drain();
        var primaryTag = PrimaryNodes(nav).Last().Tag;
        var primarySource = (PrimaryMenuItem)primaryTag.GetType().GetProperty("Item")!.GetValue(primaryTag)!;
        Check(ReferenceEquals(PrimaryNodes(nav).Last(), primary) && (string)primary.Content == "Latest shortcut" && primarySource.Label == "Latest shortcut", "custom primary label and cloned navigation tag update in place");
        Check(IconName(PrimaryNodes(nav).Last()) == "tv" && IconName(currentFirst) == "tv", "library and primary icons follow latest media type");
        // Focus a real descendant, then rename its callback-owned data.
        first.IsExpanded = true; await Drain();
        var pinButton = ((Grid)currentPinContent(currentFirst).Content).Children.OfType<Button>().Single();
        pinButton.Focus(FocusState.Keyboard); nav.SelectedItem = currentFirst.MenuItems[0]; await Drain();
        Pins = new Dictionary<string, object> { ["1"] = new[] { new { type = "collection", id = "pin-a", label = "Newest pin" }, new { type = "section", id = "pin-b", label = "New pin" } } };
        await window.RefreshSidebarPinsAsync(); await Drain();
        Check(pinButton.FocusState != FocusState.Unfocused && ReferenceEquals(nav.SelectedItem, currentFirst.MenuItems[0]) && first.IsExpanded, "pin label update retains focused unpin descendant selection and expansion");
        var currentPin = (NavigationViewItem)currentFirst.MenuItems[0];
        var unpin = ((Grid)currentPin.Content).Children.OfType<Button>().Single();
        Check(AutomationProperties.GetName(unpin) == "Unpin Newest pin", "unpin accessibility follows latest pin label");
        ((IInvokeProvider)new ButtonAutomationPeer(unpin).GetPattern(PatternInterface.Invoke)).Invoke();
        await Until(() => LastWrite != null); await Drain();
        Check(!LastWrite!.Contains("pin-a") && LastWrite.Contains("pin-b"), "reused pin unpin callback removes latest correct key");
        var sectionPin = (NavigationViewItem)first.MenuItems[0];
        Pins = new Dictionary<string, object> { ["1"] = new[] { new { type = "collection", id = "pin-b", label = "Changed pin kind" } } };
        await window.RefreshSidebarPinsAsync(); await Drain();
        var changedKindPin = (NavigationViewItem)first.MenuItems[0];
        var changedKindTag = changedKindPin.Tag;
        Check(!ReferenceEquals(sectionPin, changedKindPin) && IconName(changedKindPin) == "folder-open" &&
            (string)changedKindTag.GetType().GetProperty("PinType")!.GetValue(changedKindTag)! == "collection", "same pin ID with changed type replaces only its keyed node and navigation metadata");
        LastWrite = null;
        ((IInvokeProvider)new ButtonAutomationPeer(((Grid)changedKindPin.Content).Children.OfType<Button>().Single()).GetPattern(PatternInterface.Invoke)).Invoke();
        await Until(() => LastWrite != null); await Drain();
        Check(!LastWrite!.Contains("pin-b"), "changed pin type unpin action targets current typed identity");
        Pins = new Dictionary<string, object> { ["1"] = new[] { new { type = "section", id = "pin-b", label = "Restored pin" } } };
        await window.RefreshSidebarPinsAsync(); await Drain();
        Libraries = [Lib(1, "Renamed movies", "series"), Lib(4, "Added")];
        catalog.InvalidateLibraryCache(); await vm.ReloadLibrariesAsync(); await Drain();
        Check(!LibraryNodes(nav).Contains(second) && ReferenceEquals(LibraryNodes(nav)[0], first), "removing a library retains other actual nodes");
        typeof(MainWindow).GetMethod("RememberDesktopSidebarState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false]);
        nav.IsPaneOpen = false; await Drain();
        window.UpdateLibraryNavItems(); await Drain();
        Check(!nav.IsPaneOpen && LibraryNodes(nav).All(n => n.Visibility == Visibility.Visible), "collapsed pane remains collapsed with usable library icons");

        // Hold both old reads, then enter a new viewer with the same IDs.
        HeldLibraries = NewHeld(); HeldPins = NewHeld(); LibraryRead = PinRead = false;
        catalog.InvalidateLibraryCache(); var oldLibraries = vm.ReloadLibrariesAsync(); var oldPins = window.RefreshSidebarPinsAsync();
        await Until(() => LibraryRead && PinRead);
        var retiredLibraries = HeldLibraries; var retiredPins = HeldPins;
        HeldLibraries = NewHeld(); HeldPins = NewHeld();
        HeldPlugins = NewHeld();
        auth.SelectProfile("second", profile: new() { Id = "second", Name = "Second" });
        window.ShowMainNavigation();
        Check(LibraryNodes(nav).Length == 0 && PrimaryNodes(nav).Length == 0, "viewer departure synchronously retires old authorized nodes before held new read");
        Check(!Nodes(nav).Any(n => n.Tag?.GetType().Name == "PluginAppNavTag"), "viewer departure synchronously retires actual old plugin routes while new plugin read is held");
        var oldFirst = first;
        retiredLibraries.SetResult(Json(new { items = new[] { Lib(1, "Retired viewer") } }));
        retiredPins.SetResult(Json(new { value = Pins }));
        await Task.WhenAll(oldLibraries, oldPins); await Drain();
        Check(LibraryNodes(nav).Length == 0, "late old reads cannot repopulate while new viewer read is held");
        Check(!Nodes(nav).Any(n => n.Tag?.GetType().Name == "PluginAppNavTag"), "old plugin routes remain retired while replacement read is held");
        HeldLibraries.SetResult(Json(new { items = new[] { Lib(1, "New viewer") } })); HeldLibraries = null;
        await Until(() => LibraryNodes(nav).Length > 0);
        HeldPins.SetResult(Json(new { value = Pins })); HeldPins = null; await Drain();
        HeldPlugins.SetResult(Json(PluginData("New viewer App"))); HeldPlugins = null; await Drain();
        Check(Nodes(nav).Any(n => n.Tag?.GetType().Name == "PluginAppNavTag" && Equals(n.Content, "New viewer App")) && !Nodes(nav).Contains(plugin), "new viewer plugin read publishes fresh actual route ownership");
        Check(!LibraryNodes(nav).Contains(oldFirst), "same-ID new viewer cannot reuse old viewer node");
        var sameProfileNode = LibraryNodes(nav)[0];
        var retiredPinButton = ((Grid)((NavigationViewItem)sameProfileNode.MenuItems[0]).Content).Children.OfType<Button>().Single();
        HeldLibraries = NewHeld(); LibraryRead = false; catalog.InvalidateLibraryCache();
        var sameProfileOldRead = vm.ReloadLibrariesAsync(); await Until(() => LibraryRead);
        var sameProfileRetired = HeldLibraries; HeldLibraries = NewHeld();
        auth.SelectProfile("second", profile: new() { Id = "second", Name = "Second" });
        window.ShowMainNavigation();
        Check(LibraryNodes(nav).Length == 0, "same-ID profile authority reset retires old nodes synchronously");
        sameProfileRetired.SetResult(Json(new { items = new[] { Lib(1, "Stale same profile") } }));
        await sameProfileOldRead; await Drain();
        Check(LibraryNodes(nav).Length == 0, "same-ID retired read cannot publish while replacement is held");
        HeldLibraries.SetResult(Json(new { items = new[] { Lib(1, "Current same profile") } })); HeldLibraries = null;
        await Until(() => LibraryNodes(nav).Length > 0); await Drain();
        Check(!LibraryNodes(nav).Contains(sameProfileNode), "same-ID authority reset creates new native ownership");
        var readsBeforeRetiredClick = PinReadCount; var writeBeforeRetiredClick = LastWrite;
        ((IInvokeProvider)new ButtonAutomationPeer(retiredPinButton).GetPattern(PatternInterface.Invoke)).Invoke(); await Drain();
        Check(PinReadCount == readsBeforeRetiredClick && LastWrite == writeBeforeRetiredClick, "retained outgoing actual unpin button cannot start reads or writes under same-ID replacement identity");
        HeldLibraries = NewHeld(); LibraryRead = false;
        var accessApp = Nodes(nav).First(n => n.Tag?.GetType().Name == "PluginAppNavTag");
        var beforeAccessReads = LibraryReadCount;
        var context = provider.GetRequiredService<SiloApiClient>().InvalidateAccessContext();
        typeof(MainWindow).GetMethod("OnAccessChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [context]);
        await Until(() => LibraryRead); await Drain();
        Check(LibraryReadCount == beforeAccessReads + 1, "access authority refresh starts exactly one library hydration");
        HeldLibraries.SetResult(Json(new { items = new[] { Lib(1, "After access refresh") } })); HeldLibraries = null;
        await Until(() => LibraryNodes(nav).Length > 0); await Drain();
        Check(Nodes(nav).Contains(accessApp), "same-identity access refresh retains plugin route without requiring a plugin reload");
        // A queued unpin belongs to the viewer that clicked it, including the
        // read/write boundary. A new profile's pins must never be overwritten.
        var outgoingLibrary = LibraryNodes(nav)[0];
        await Until(() => outgoingLibrary.MenuItems.Count > 0);
        var outgoingButton = ((Grid)((NavigationViewItem)outgoingLibrary.MenuItems[0]).Content).Children.OfType<Button>().Single();
        HeldPins = NewHeld(); PinRead = false; LastWrite = null;
        ((IInvokeProvider)new ButtonAutomationPeer(outgoingButton).GetPattern(PatternInterface.Invoke)).Invoke();
        await Until(() => PinRead);
        var outgoingPins = HeldPins; HeldPins = null; HeldLibraries = NewHeld();
        auth.SelectProfile("third", profile: new() { Id = "third", Name = "Third" }); window.ShowMainNavigation();
        outgoingPins.SetResult(Json(new { value = Pins })); await Drain();
        Check(LastWrite == null, "retired unpin read cannot write another profile's pins");
        HeldLibraries.SetResult(Json(new { items = new[] { Lib(1, "Third viewer") } })); HeldLibraries = null;
        await Until(() => LibraryNodes(nav).Length > 0); await Drain();
        HeldPins = NewHeld(); PinRead = false; LastWrite = null;
        var outgoingToggle = window.ToggleSidebarPinAsync(1, "collection", "new-pin", "Outgoing pin");
        await Until(() => PinRead); outgoingPins = HeldPins; HeldPins = null; HeldLibraries = NewHeld();
        auth.SelectProfile("fourth", profile: new() { Id = "fourth", Name = "Fourth" }); window.ShowMainNavigation();
        outgoingPins.SetResult(Json(new { value = Pins })); await outgoingToggle; await Drain();
        Check(LastWrite == null, "retired toggle read cannot write another profile's pins");
        HeldLibraries.SetResult(Json(new { items = new[] { Lib(1, "Fourth viewer") } })); HeldLibraries = null;
        await Until(() => LibraryNodes(nav).Length > 0); await Drain();
        HeldWrite = NewHeld(); LastWrite = null;
        var acceptedToggle = window.ToggleSidebarPinAsync(1, "collection", "accepted-pin", "Accepted outgoing pin");
        await Until(() => LastWrite != null);
        HeldLibraries = NewHeld();
        auth.SelectProfile("fifth", profile: new() { Id = "fifth", Name = "Fifth" }); window.ShowMainNavigation();
        Pins = new Dictionary<string, object> { ["1"] = new[] { new { type = "section", id = "new-viewer-pin", label = "New viewer pin" } } };
        HeldWrite.SetResult(Json(new { })); HeldWrite = null; await acceptedToggle; await Drain();
        Check(window.GetSidebarPins(1, "collection").Count == 0, "accepted retired write completion cannot republish outgoing pin map");
        HeldLibraries.SetResult(Json(new { items = new[] { Lib(1, "Fifth viewer") } })); HeldLibraries = null;
        await Until(() => LibraryNodes(nav).Length > 0 && window.GetSidebarPins(1, "section").Count > 0); await Drain();
        var navigation = provider.GetRequiredService<NavigationService>();
        navigation.Navigate<HomePage>();
        await Until(() => navigation.Frame!.Content is HomePage home && home.IsLoaded && !home.ViewModel.IsLoading);
        var catchUpNode = LibraryNodes(nav)[0];
        context = provider.GetRequiredService<SiloApiClient>().InvalidateAccessContext();
        typeof(MainWindow).GetMethod("OnReadCatchUpRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [context]);
        await Drain(); Libraries = [Lib(1, "After Home catch-up")];
        await vm.ReloadLibrariesAsync(); await window.RefreshSidebarPinsAsync(); await Drain();
        Check(ReferenceEquals(LibraryNodes(nav)[0], catchUpNode) && (string)catchUpNode.Content == "After Home catch-up", "Home-only request catch-up permits next same-identity sidebar publication");
        var catchUpPin = (NavigationViewItem)catchUpNode.MenuItems[0]; LastWrite = null;
        ((IInvokeProvider)new ButtonAutomationPeer(((Grid)catchUpPin.Content).Children.OfType<Button>().Single()).GetPattern(PatternInterface.Invoke)).Invoke();
        await Until(() => LastWrite != null); await Drain();
        Check(window.GetSidebarPins(1, "section").Count == 0, "retained unpin callback remains usable after request-only catch-up");
        HeldLibraries = NewHeld(); LibraryRead = false; catalog.InvalidateLibraryCache();
        User = new { id = "new-owner", username = "New owner", role = "user" };
        auth.SetCurrentUser(new() { Id = "new-owner", Username = "New owner", Role = "user" });
        Check(LibraryNodes(nav).Length == 0, "account authority callback synchronously retires outgoing nodes");
        await Until(() => LibraryRead);
        HeldLibraries.SetResult(Json(new { items = new[] { Lib(1, "New account") } })); HeldLibraries = null;
        await Until(() => LibraryNodes(nav).Length > 0); await Drain();
        window.HideMainNavigation();
        Check(LibraryNodes(nav).Length == 0 && PrimaryNodes(nav).Length == 0, "hide retires library and custom primary nodes");
        await Drain(); Check(LibraryNodes(nav).Length == 0 && PrimaryNodes(nav).Length == 0, "queued old publication cannot repopulate hidden shell");
        if (Failures.Count > 0) throw new InvalidOperationException("Sidebar publication failures: " + string.Join("; ", Failures));
    }
    internal static async Task RunDetachedAsync(MainWindow window, IServiceProvider provider)
    {
        var nav = (NavigationView)((FrameworkElement)window.Content).FindName("NavView");
        typeof(MainWindow).GetField("_playerPrewarmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.ShowMainNavigation();
        await Until(() => Nodes(nav).Any(n => n.Tag?.GetType().Name == "PluginAppNavTag"));
        var navigation = provider.GetRequiredService<NavigationService>();
        navigation.Navigate<SettingsPage>();
        var savedSidebar = typeof(MainWindow).GetField("_appSidebarItems", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Until(() => savedSidebar.GetValue(window) is object[]);
        Check(((object[])savedSidebar.GetValue(window)!).OfType<NavigationViewItem>().Any(n => n.Tag?.GetType().Name == "PluginAppNavTag"), "fixture establishes actual plugin in detached Settings app tree");
        HeldLibraries = NewHeld(); HeldPlugins = NewHeld();
        provider.GetRequiredService<AuthService>().SelectProfile("second", profile: new() { Id = "second", Name = "Second" }); window.ShowMainNavigation();
        var saved = (object[])savedSidebar.GetValue(window)!;
        Check(!saved.OfType<NavigationViewItem>().Any(n => n.Tag?.GetType().Name == "PluginAppNavTag"), "identity reset retires plugin route from detached Settings app tree");
        navigation.Navigate<Page>(); await Drain();
        Check(!Nodes(nav).Any(n => n.Tag?.GetType().Name == "PluginAppNavTag"), "Settings return cannot restore outgoing plugin routes while new read is held");
        HeldLibraries.SetResult(Json(new { items = new[] { Lib(1, "Second viewer") } })); HeldLibraries = null;
        HeldPlugins.SetResult(Json(PluginData("Second viewer App"))); HeldPlugins = null;
        await Until(() => Nodes(nav).Any(n => Equals(n.Content, "Second viewer App"))); await Drain();
        Check(Nodes(nav).Any(n => Equals(n.Content, "Second viewer App")), "Settings return publishes current plugin route after replacement read");
        if (Failures.Count > 0) throw new InvalidOperationException("Detached sidebar failures: " + string.Join("; ", Failures));
    }
    private static NavigationViewItem currentPinContent(NavigationViewItem library) => (NavigationViewItem)library.MenuItems[0];
    private static object PluginData(string label) => new { items = new[] { new { id = 7, plugin_id = label, routes = new[] { new { path = "/fixture", navigable = true, navigation_kind = "user", navigation_label = label } } } } };
    private static string IconName(NavigationViewItem item) => Path.GetFileNameWithoutExtension(((Microsoft.UI.Xaml.Media.Imaging.SvgImageSource)((ImageIcon)item.Icon).Source).UriSource.AbsolutePath);
    private static TaskCompletionSource<HttpResponseMessage> NewHeld() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task<HttpResponseMessage> CloneHeldAsync(Task<HttpResponseMessage> held)
    {
        var response = await held;
        return new(response.StatusCode) { Content = new StringContent(await response.Content.ReadAsStringAsync(), System.Text.Encoding.UTF8, "application/json") };
    }
    private static async Task Drain() { await Task.Delay(200); }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Sidebar fixture state did not arrive."); }
    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), System.Text.Encoding.UTF8, "application/json") };
    internal static async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/v2/auth/me") return Json(User);
        if (path == "/api/v2/user/libraries")
        { LibraryRead = true; LibraryReadCount++; return HeldLibraries != null ? await HeldLibraries.Task : Json(new { items = Libraries }); }
        if (path.Contains("sidebar_pins"))
        {
            if (request.Method == HttpMethod.Get)
            { PinRead = true; PinReadCount++; return HeldPins != null ? await HeldPins.Task : Json(new { value = Pins }); }
            LastWrite = await request.Content!.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(LastWrite);
            Pins = doc.RootElement.GetProperty("value").Clone();
            return HeldWrite != null ? await HeldWrite.Task : Json(new { });
        }
        if (path == "/api/v2/settings/contract/capabilities")
            return Json(new { api_version = 1, manifest_revision = 22, client_families = new[] { "desktop", "web" }, supports_batched_effective = true, supports_idempotent_writes = true });
        if (path == "/api/v2/settings/plugins")
            return HeldPlugins != null ? await CloneHeldAsync(HeldPlugins.Task) : Json(PluginData("Fixture App"));
        return Json(new { items = Array.Empty<object>(), settings = Array.Empty<object>(), sections = Array.Empty<object>(), devices = Array.Empty<object>(), providers = Array.Empty<object>() });
    }
}
