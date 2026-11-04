using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
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
    private static bool FailSidebarReads;
    private static string PosterSize = "standard";
    private static Profile FirstProfile = new() { Id = "first", Name = "First", IsPrimary = true };
    private static readonly Profile SecondProfile = new() { Id = "second", Name = "Second", IsChild = true, LibraryRestrictionsEnabled = true, AllowedLibraryIds = [1] };
    private static object? ProfileLibraries;
    private static bool IsSettingsProfileRoute => Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") is "sidebar-review-profile-use" or "sidebar-review-profile-edit";
    private static object PinMap(string label, bool duplicate = false) => new Dictionary<string, object> { ["1"] = duplicate
        ? new[] { new { type = "collection", id = "pin", label }, new { type = "collection", id = "pin", label = "Later duplicate label" } }
        : new[] { new { type = "collection", id = "pin", label } } };
    private static object Menu(string label) => new { items = new object[] { new { type = "builtin", destination = "home" }, new { type = "library", library_id = 1, label } } };
    private static object EffectiveMenu() => new { items = new object[] {
        new { key = "nav.primary_menu", value = Menu(MenuLabel), source = "profile_client" },
        new { key = "ui.card_presentation", value = new { poster_size = PosterSize, caption = "title_metadata" }, source = "profile_client" } } };
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
        if (mode is "sidebar-review-home-library-load" or "sidebar-review-home-pin-load")
        {
            await CheckHeldHomeNavigationAsync(window, provider, nav, mode.EndsWith("pin-load", StringComparison.Ordinal));
            if (Failures.Count > 0) throw new InvalidOperationException(string.Join("; ", Failures));
            return;
        }
        if (mode.EndsWith("initial", StringComparison.Ordinal)) HeldMenuRead = Hold();
        if (mode.EndsWith("duplicates", StringComparison.Ordinal)) Pins = PinMap("Duplicate", duplicate: true);
        if (mode.EndsWith("move", StringComparison.Ordinal)) Pins = new Dictionary<string, object> { ["2"] = new[] { new { type = "section", id = "second-pin", label = "Second pin" } } };
        window.ShowMainNavigation();
        await Until(() => LibraryRows(nav).Length == 2);
        if (HeldMenuRead == null && !mode.EndsWith("duplicates", StringComparison.Ordinal)) await Until(() => PrimaryRows(nav).Length == 2);
        if (IsSettingsProfileRoute)
        {
            await CheckSettingsProfileChangeAsync(window, provider, nav, mode.EndsWith("edit", StringComparison.Ordinal));
        }
        else if (mode is "sidebar-review-card-save" or "sidebar-review-card-reset")
        {
            navigation.Navigate<SettingsPage>();
            await Until(() => navigation.Frame!.Content is SettingsPage page && page.IsLoaded && !page.ViewModel.IsLoading);
            var page = (SettingsPage)navigation.Frame!.Content;
            page.SelectSettingsSection("Interface"); await Drain();
            HeldMenuWrite = Hold();
            var reset = mode.EndsWith("reset", StringComparison.Ordinal);
            Task? save = null;
            if (reset)
                typeof(SettingsPage).GetMethod("ResetDesktopCardLayout_Click", Instance)!.Invoke(page, [page, new RoutedEventArgs()]);
            else
                save = (Task)typeof(SettingsPage).GetMethod("SaveInterfaceCardPresentationAsync", Instance)!.Invoke(page, [new CardPresentation { PosterSize = "compact" }])!;
            await Until(() => MenuWrites > 0);
            navigation.Navigate<Page>();
            PosterSize = "large";
            provider.GetRequiredService<AuthService>().SelectProfile("second", profile: new() { Id = "second", Name = "Second" });
            await Until(() => service.CardPresentation.PosterSize == "large"); await Drain();
            var reads = MenuReads;
            var changes = 0;
            service.Changed += (_, _) => changes++;
            HeldMenuWrite.SetResult(new { }); HeldMenuWrite = null;
            if (save != null) await save;
            await Drain();
            await service.EnsureLoadedAsync();
            Check(service.CardPresentation.PosterSize == "large" && service.CardPresentationSource == "profile_client",
                "retired actual Settings card save/reset cannot overwrite replacement profile presentation");
            Check(MenuReads == reads && changes == 0, "retired card completion neither refreshes the new owner nor publishes singleton changes");
            Check(((ComboBox)page.FindName("InterfacePosterSizeCombo")).SelectedItem is ComboBoxItem { Tag: "standard" },
                "retired card caller does not sync replacement profile controls");
        }
        else if (mode == "sidebar-review-interface-load")
        {
            navigation.Navigate<SettingsPage>();
            await Until(() => navigation.Frame!.Content is SettingsPage page && page.IsLoaded && !page.ViewModel.IsLoading);
            var page = (SettingsPage)navigation.Frame!.Content;
            var reads = MenuReads;
            HeldMenuRead = Hold();
            page.SelectSettingsSection("Interface");
            await Until(() => MenuReads > reads);
            api.InvalidateAccessContext();
            HeldMenuRead.SetResult(EffectiveMenu()); HeldMenuRead = null;
            await Until(() => ((TextBlock)page.FindName("InterfaceStatusTitle")).Text == "Customization changed");
            Check(((TextBlock)page.FindName("InterfaceStatusText")).Text.Contains("Reopen Interface") &&
                !((FrameworkElement)page.FindName("InterfaceControls")).IsHitTestVisible,
                "stale Interface read ends loading with an explicit safe unavailable state and recovery instruction");
            await (Task)typeof(SettingsPage).GetMethod("LoadInterfaceCustomizationAsync", Instance)!.Invoke(page, null)!;
            Check(((FrameworkElement)page.FindName("InterfaceControls")).IsHitTestVisible &&
                ((FrameworkElement)page.FindName("InterfaceStatusCard")).Visibility == Visibility.Collapsed,
                "Interface can load and enable current settings after a stale read");
        }
        else if (mode == "sidebar-review-picker-back")
        {
            await Until(() => LibraryRows(nav)[0].MenuItems.Count == 1 && Rows(nav).Any(n => n.Tag?.GetType().Name == "PluginAppNavTag"));
            var profile = provider.GetRequiredService<AuthService>().SelectedProfileId;
            Call(window, "SwitchProfile_Click", window, new RoutedEventArgs());
            await Until(() => navigation.Frame!.Content is ProfileSelectPage);
            navigation.GoBack();
            await Until(() => navigation.Frame!.Content is not ProfileSelectPage && LibraryRows(nav).Length == 2 &&
                LibraryRows(nav)[0].MenuItems.Count == 1 && PrimaryRows(nav).Length == 2 &&
                Rows(nav).Any(n => n.Tag?.GetType().Name == "PluginAppNavTag"));
            Check(provider.GetRequiredService<AuthService>().SelectedProfileId == profile && vm.HasCurrentLibrarySnapshot,
                "Back from Switch Profile restores libraries, pins, custom shortcuts and Apps without selecting a profile");
            var writes = PinWrites;
            Invoke(Unpin(LibraryRows(nav)[0]));
            await Until(() => PinWrites == writes + 1 && LibraryRows(nav)[0].MenuItems.Count == 0);
            Check(LibraryRows(nav)[0].MenuItems.Count == 0, "restored picker shell owns usable unpin callbacks");
        }
        else if (mode == "sidebar-review-nonhome-catchup")
        {
            navigation.Navigate<LibraryPage>(vm.Libraries[0]);
            await Until(() => navigation.Frame!.Content is LibraryPage page && page.IsLoaded && !page.ViewModel.IsLoading);
            var library = LibraryRows(nav)[0];
            await Until(() => library.MenuItems.Count == 1);
            var pin = library.MenuItems[0]; var primary = PrimaryRows(nav).Last();
            library.IsExpanded = true; nav.SelectedItem = library; library.Focus(FocusState.Keyboard); await Drain();
            var reads = LibraryReads; var pinReads = PinReads;
            FailSidebarReads = true;
            Call(window, "OnReadCatchUpRequested", api.InvalidateAccessContext());
            await Until(() => LibraryReads > reads && PinReads > pinReads); await Drain();
            Check(ReferenceEquals(LibraryRows(nav)[0], library) && ReferenceEquals(library.MenuItems[0], pin) &&
                PrimaryRows(nav).Contains(primary) && vm.HasCurrentLibrarySnapshot && library.IsExpanded &&
                ReferenceEquals(nav.SelectedItem, library) && library.FocusState == FocusState.Keyboard,
                "failed non-Home transport replacement preserves accepted native identity, data, selection, focus and expansion");
            FailSidebarReads = false;
            Call(window, "OnReadCatchUpRequested", api.InvalidateAccessContext()); await Drain();
            Check(ReferenceEquals(LibraryRows(nav)[0], library) && ReferenceEquals(library.MenuItems[0], pin),
                "successful non-Home catch-up reconciles retained nodes in place");
            var writes = PinWrites; Invoke(Unpin(library)); await Until(() => PinWrites == writes + 1);
            Check(PinWrites == writes + 1, "transport catch-up retains useful pin callback ownership");
        }
        else if (mode.EndsWith("save", StringComparison.Ordinal) || mode.EndsWith("reset", StringComparison.Ordinal) || mode.EndsWith("clear", StringComparison.Ordinal))
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
            vm.Libraries.Add(new Library { Id = 1, Name = "Later duplicate", Type = "series" }); await Drain();
            Check(LibraryRows(nav).Length == 2 && ReferenceEquals(LibraryRows(nav)[0], library) && Equals(library.Content, "One"),
                "duplicate library IDs keep the first snapshot occurrence and retained native node");
            nav.MenuItems.Add(new NavigationViewItem { Tag = vm.Libraries[0], Content = "Old duplicate library" });
            window.UpdateLibraryNavItems(); await Drain();
            Check(LibraryRows(nav).Length == 2 && ReferenceEquals(LibraryRows(nav)[0], library), "publication recovers already-duplicated old library vectors");
            vm.Libraries[0] = new Library { Id = 1, Name = "Current first", Type = "movies" }; await Drain();
            Check(Equals(library.Content, "Current first"), "later publications still update after duplicate library recovery");
            var pin = (NavigationViewItem)library.MenuItems[0];
            pin.Icon = SiloPlayer.Controls.WebUiIcon.Navigation("layout-grid", 14);
            Pins = new Dictionary<string, object> { ["1"] = new[] { new { type = "Collection", id = "pin", label = "Mixed case" } } };
            await window.RefreshSidebarPinsAsync(); await Drain();
            Check(ReferenceEquals(library.MenuItems[0], pin) && Equals(pin.Icon.Tag, "folder-open") && pin.Icon.Opacity == .6,
                "retained pin refreshes its collection glyph case-insensitively");
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
    private static async Task CheckHeldHomeNavigationAsync(MainWindow window, IServiceProvider provider, NavigationView nav, bool holdPins)
    {
        var navigation = provider.GetRequiredService<NavigationService>();
        var api = provider.GetRequiredService<SiloApiClient>();
        var vm = provider.GetRequiredService<MainViewModel>();
        var original = Hold();
        if (holdPins) HeldPins = original;
        else HeldLibraries = original;
        navigation.Navigate<HomePage>();
        window.ShowMainNavigation();
        await Until(() => navigation.Frame!.Content is HomePage home && home.IsLoaded && !home.ViewModel.IsLoading &&
            (holdPins ? PinReads > 0 : LibraryReads > 0));
        var retainedLibrary = LibraryRows(nav).FirstOrDefault();
        var replacement = Hold();
        var reads = holdPins ? PinReads : LibraryReads;
        if (holdPins) HeldPins = replacement;
        else HeldLibraries = replacement;
        Call(window, "OnReadCatchUpRequested", api.InvalidateAccessContext());
        await Until(() => (holdPins ? PinReads : LibraryReads) > reads);
        original.SetResult(holdPins ? new { value = Pins } : Libraries()); await Drain();
        Check(holdPins ? window.GetSidebarPins(1, "collection").Count == 0 : !vm.HasCurrentLibrarySnapshot,
            "stale held initial " + (holdPins ? "pin" : "library") + " response cannot publish after Home catch-up");
        replacement.SetResult(holdPins ? new { value = Pins } : Libraries());
        if (holdPins) HeldPins = null;
        else HeldLibraries = null;
        await Until(() => vm.HasCurrentLibrarySnapshot && LibraryRows(nav).Length == 2 && LibraryRows(nav)[0].MenuItems.Count == 1);
        Check(!holdPins || ReferenceEquals(retainedLibrary, LibraryRows(nav)[0]), "automatic Home catch-up accepts replacement reads without retiring surviving libraries");
        Check(window.GetSidebarPins(1, "collection").Count == 1, "automatic Home catch-up restores pins after an invalidated initial read");
        var writes = PinWrites; Invoke(Unpin(LibraryRows(nav)[0])); await Until(() => PinWrites == writes + 1);
        Check(PinWrites == writes + 1, "automatically recovered Home hydration owns a usable pin action");
    }
    private static async Task CheckSettingsProfileChangeAsync(MainWindow window, IServiceProvider provider, NavigationView nav, bool edit)
    {
        var root = (FrameworkElement)window.Content;
        var navigation = provider.GetRequiredService<NavigationService>();
        var auth = provider.GetRequiredService<AuthService>();
        var vm = provider.GetRequiredService<MainViewModel>();
        await Until(() => LibraryRows(nav)[0].MenuItems.Count == 1);
        var oldLibrary = LibraryRows(nav)[0]; var oldUnpin = Unpin(oldLibrary);
        var oldPrimary = PrimaryRows(nav).Last();
        navigation.Navigate<SettingsPage>();
        await Until(() => navigation.Frame!.Content is SettingsPage page && page.IsLoaded && !page.ViewModel.IsLoading);
        var page = (SettingsPage)navigation.Frame!.Content;
        page.SelectSettingsSection("Profiles");
        var cards = (StackPanel)page.FindName("ProfileCardsPanel");
        var detached = typeof(MainWindow).GetField("_appSidebarItems", Instance)!;
        await Until(() => cards.Children.Count == 2 && detached.GetValue(window) is object[]);
        var card = cards.Children.OfType<Border>().Single(row => All<TextBlock>(row).Any(text => text.Text == (edit ? "First" : "Second")));
        var action = All<Button>(card).Single(button => All<TextBlock>(button).Any(text => text.Text == (edit ? "Edit" : "Use")));
        var authorityEvents = 0; var retiredSynchronously = false;
        void ObserveAuthority()
        {
            authorityEvents++;
            var saved = (object[]?)detached.GetValue(window) ?? nav.MenuItems.ToArray();
            retiredSynchronously = vm.Libraries.Count == 0 && window.GetSidebarPins(1, "collection").Count == 0 &&
                !saved.OfType<NavigationViewItem>().Any(row => row.Tag is Library || row.Tag?.GetType().Name == "PrimaryMenuNavTag");
        }
        auth.ProfileChanged += ObserveAuthority;
        try
        {
            MenuLabel = "Current profile shortcut";
            Pins = PinMap("Current profile pin");
            if (edit)
            {
                Invoke(action);
                await Until(() => Dialogs(root).Any(dialog => All<TextBox>(dialog).Any(box => box.PlaceholderText == "Alex")));
                var dialog = Dialogs(root).Single();
                All<TextBox>(dialog).Single(box => box.PlaceholderText == "Alex").Text = "Edited first";
                foreach (var toggle in All<ToggleSwitch>(dialog))
                {
                    var labels = All<TextBlock>(VisualTreeHelper.GetParent(toggle)).Select(text => text.Text).ToArray();
                    if (labels.Contains("Restrict libraries") || labels.Contains("One")) toggle.IsOn = true;
                    else if (labels.Contains("Two")) toggle.IsOn = false;
                }
                HeldLibraries = Hold();
                Invoke(All<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
            }
            else
            {
                HeldLibraries = Hold();
                Invoke(action);
            }
            await Until(() => authorityEvents == 1);
            Check(retiredSynchronously, "real Settings " + (edit ? "Edit" : "Use") + " synchronously retires detached library, pin and custom primary ownership");
            Check(auth.SelectedProfileId == (edit ? "first" : "second") && auth.SelectedProfile?.LibraryRestrictionsEnabled == true,
                "Settings flow selects the saved restricted profile without a fixture shell refresh");
            if (edit) Check(ReferenceEquals(navigation.Frame!.Content, page) && auth.SelectedProfile?.Name == "Edited first", "active-profile Edit stays on Settings with current metadata");
            else await Until(() => navigation.Frame!.Content is HomePage home && home.IsLoaded);
            var reads = PinReads; var writes = PinWrites;
            Invoke(oldUnpin); await Drain();
            Check(PinReads == reads && PinWrites == writes, "outgoing Settings profile's retained unpin cannot read or write replacement pins");
            Check(vm.Libraries.Count == 0, "outgoing libraries stay retired while replacement hydration is held");

            ProfileLibraries = new { items = new[] { new Library { Id = 1, Name = "Current profile library", Type = "movies" } } };
            HeldLibraries.SetResult(ProfileLibraries);
            HeldLibraries = null;
            await Until(() => vm.HasCurrentLibrarySnapshot && vm.Libraries.Count == 1 && window.GetSidebarPins(1, "collection").Count == 1);
            if (edit) navigation.Navigate<Page>();
            await Until(() => LibraryRows(nav).Length == 1 && LibraryRows(nav)[0].MenuItems.Count == 1 && PrimaryRows(nav).Any(row => Equals(row.Content, MenuLabel)));
            var current = LibraryRows(nav)[0];
            Check(!ReferenceEquals(current, oldLibrary) && !PrimaryRows(nav).Contains(oldPrimary) && Equals(current.Content, "Current profile library"),
                "Settings return publishes fresh native nodes and current profile data");
            Check(window.GetSidebarPins(1, "collection").Single().Label == "Current profile pin", "Settings profile hydration accepts only the current pin data");
            writes = PinWrites; Invoke(Unpin(current));
            await Until(() => PinWrites == writes + 1 && current.MenuItems.Count == 0);
            Check(current.MenuItems.Count == 0, "fresh Settings profile's actual unpin publishes successfully");
            Check(await window.ToggleSidebarPinAsync(1, "collection", "new-pin", "New pin"), "pin toggle remains usable after Settings profile change");
            await window.RefreshSidebarPinsAsync(); await Drain();
            Check(current.MenuItems.Count == 1 && window.GetSidebarPins(1, "collection").Any(pin => pin.Id == "new-pin"), "current profile pin refresh can publish after Settings profile change");

            navigation.Navigate<SettingsPage>();
            await Until(() => navigation.Frame!.Content is SettingsPage settingsPage && settingsPage.IsLoaded && !settingsPage.ViewModel.IsLoading && settingsPage.ViewModel.LibraryCards.Count == 1);
            var librariesPage = (SettingsPage)navigation.Frame!.Content;
            librariesPage.SelectSettingsSection("Libraries"); await Drain();
            var visibility = All<ToggleSwitch>((DependencyObject)librariesPage.FindName("LibraryCardsContainer")).Single();
            visibility.IsOn = false;
            await Until(() => provider.GetRequiredService<SettingsService>().Load().HiddenLibraryIds.Contains(1));
            navigation.Navigate<Page>(); await Drain();
            Check(LibraryRows(nav).Length == 0, "real Settings hide-library save still publishes after profile change");
        }
        finally
        {
            auth.ProfileChanged -= ObserveAuthority;
            HeldLibraries?.TrySetResult(Libraries()); HeldLibraries = null;
            foreach (var dialog in Dialogs(root).ToArray()) dialog.Hide();
        }
    }
    private static IEnumerable<ContentDialog> Dialogs(FrameworkElement owner) => VisualTreeHelper.GetOpenPopupsForXamlRoot(owner.XamlRoot).SelectMany(popup => All<ContentDialog>(popup.Child));
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
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
        if (IsSettingsProfileRoute && path == "/api/v2/profiles") return Json(new { items = new[] { FirstProfile, SecondProfile } });
        if (IsSettingsProfileRoute && path == "/api/v2/profiles/first" && request.Method == HttpMethod.Patch)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            FirstProfile = new() { Id = "first", Name = body.RootElement.GetProperty("name").GetString()!, IsPrimary = true,
                LibraryRestrictionsEnabled = body.RootElement.GetProperty("library_restrictions_enabled").GetBoolean(),
                AllowedLibraryIds = body.RootElement.GetProperty("allowed_library_ids").EnumerateArray().Select(id => int.Parse(id.GetString()!)).ToList() };
            return Json(FirstProfile);
        }
        if (path == "/api/v2/user/libraries")
        {
            LibraryReads++;
            if (FailSidebarReads) return new(HttpStatusCode.ServiceUnavailable);
            return Json(HeldLibraries == null ? ProfileLibraries ?? Libraries() : await HeldLibraries.Task);
        }
        if (path.Contains("sidebar_pins"))
        {
            if (request.Method == HttpMethod.Get)
            {
                PinReads++;
                if (FailSidebarReads) return new(HttpStatusCode.ServiceUnavailable);
                return Json(HeldPins == null ? new { value = Pins } : await HeldPins.Task);
            }
            PinWrites++;
            if (IsSettingsProfileRoute || Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "sidebar-review-picker-back")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Pins = body.RootElement.GetProperty("value").Clone();
            }
            return Json(new { });
        }
        if ((path.Contains("nav.primary_menu") || path.Contains("ui.card_presentation")) && request.Method != HttpMethod.Get)
        { MenuWrites++; return Json(HeldMenuWrite == null ? new { } : await HeldMenuWrite.Task); }
        if (path == "/api/v2/settings/values/effective" && request.RequestUri.Query.Contains("nav.primary_menu"))
        { MenuReads++; return Json(HeldMenuRead == null ? EffectiveMenu() : await HeldMenuRead.Task); }
        if (path == "/api/v2/settings/contract/capabilities") return Json(new { api_version = 1, manifest_revision = 22, client_families = new[] { "desktop", "web" }, supports_batched_effective = true, supports_idempotent_writes = true });
        if (path == "/api/v2/settings/plugins") return Json(new { items = new[] { new { id = 7, plugin_id = "Fixture", routes = new[] { new { path = "/fixture", navigable = true, navigation_kind = "user", navigation_label = "Fixture" } } } } });
        return Json(new { items = Array.Empty<object>(), settings = Array.Empty<object>(), sections = Array.Empty<object>(), devices = Array.Empty<object>(), providers = Array.Empty<object>() });
    }
}
