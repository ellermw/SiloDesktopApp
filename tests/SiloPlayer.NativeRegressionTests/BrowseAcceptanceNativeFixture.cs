using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class BrowseAcceptanceNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_QUICK_INTERACTIONS") == "1")
        {
            await BrowseQuickInteractionsNativeFixture.RunAsync(parent);
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_WIZARD_RECOVERY") == "1")
        {
            await BrowseWizardRecoveryNativeFixture.RunAsync(parent);
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_CALENDAR_ARTWORK") == "1")
        {
            await BrowseCalendarArtworkNativeFixture.RunAsync(parent);
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_SEARCH_RECOVERY") == "1")
        {
            await BrowseSearchRecoveryNativeFixture.RunAsync(parent);
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_GUIDED_INTERACTIONS") == "1")
        {
            await BrowseGuidedInteractionsNativeFixture.RunAsync(parent);
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_LISTENING_ARTWORK") == "1")
        {
            await BrowseListeningArtworkNativeFixture.RunAsync(parent);
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_PERSON_ACTIONS") == "1")
        {
            await BrowsePersonNativeFixture.RunAsync(parent);
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_MENU_COUPLING") == "1")
        {
            await BrowseMenuCouplingNativeFixture.RunAsync(parent);
            return;
        }
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)serviceField.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = new SiloApiClient(http); client.SetBaseUrl("https://browse-acceptance.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); auth.SelectProfile("acceptance", profile: new() { Id = "acceptance", Name = "Fixture" });
        var catalog = new CatalogApi(client); var collections = new CollectionsApi(client); var settings = new SettingsApi(client); var overlays = new CardOverlayService(settings);
        var navigation = new NavigationService();
        var localSettings = new SettingsService(Path.Combine(Program.ResultDirectory, "browse-acceptance-settings"));
        serviceField.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = () => client, [typeof(AuthService)] = () => auth,
            [typeof(CatalogApi)] = () => catalog, [typeof(CollectionsApi)] = () => collections,
            [typeof(SettingsApi)] = () => settings, [typeof(CardOverlayService)] = () => overlays,
            [typeof(NotificationsViewModel)] = () => new NotificationsViewModel(new NotificationsApi(client)),
            [typeof(NavigationService)] = () => navigation,
            [typeof(CalendarViewModel)] = () => new CalendarViewModel(catalog, localSettings),
            [typeof(CollectionEditorViewModel)] = () => new CollectionEditorViewModel(collections, catalog, settings, auth),
            [typeof(CollectionsViewModel)] = () => new CollectionsViewModel(collections, catalog),
        }));
        try
        {
            await QueryVariantsAsync(parent);
            await CounterModesAsync(parent);
            await MountedCardPreferencesAsync(parent, wire, overlays);
            await ManualOrderAsync(parent, wire);
            await ImportedDraftAsync(parent, wire);
            await TemplateConfigAsync(parent);
            await CalendarTodayAsync(parent, wire);
            await MenuRolesAsync(parent, auth);
            await NowListeningTransportAsync(parent, client, auth, localSettings, wire);
            await NotificationsRecoveryAsync(parent, wire);
            Program.Log("PASS: browse acceptance numeric validation/unknown draft preservation, responsive hero counters, mounted card modes, manual complete-order/ETag/conflict, imported dirty failure/discard/save, template narrow controls, Today navigation, mounted permission menus and real local Now Listening transport.");
        }
        finally { parent.Children.Clear(); serviceField.SetValue(null, previous); }
    }

    private static async Task QueryVariantsAsync(StackPanel parent)
    {
        var query = new QueryDefinition { Match = "any", Groups = [new() { Rules = [new() { Field = "year", Op = "between", Value = new[] { 1980d, 2001d } }, new() { Field = "watched", Op = "is", Value = true }, new() { Field = "vendor_unknown", Op = "vendor_op", Value = "saved value" }] }] };
        var editor = new QueryFilterEditor { Width = 460 }; editor.Load(query, "video"); parent.Children.Add(editor); Call(editor, "ShowMode", true); await Layout(editor, 460, 640);
        var to = Descendants<TextBox>(editor).Single(box => box.PlaceholderText == "To"); to.Text = "not a number"; await Layout(editor, 460, 640);
        if (editor.IsValid || !Descendants<TextBlock>(editor).Any(text => text.Visibility == Visibility.Visible && text.Text.Contains("valid number", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Invalid numeric range input must disable applying and expose native validation.");
        to.Text = "2002"; await Layout(editor, 460, 640);
        if (!editor.IsValid || query.Groups[0].Rules[0].Value is not double[] pair || pair[1] != 2002) throw new InvalidOperationException("Native range correction did not recover the valid typed value.");
        Call(editor, "ShowMode", false); await Layout(editor, 460, 640); Call(editor, "ShowMode", true); await Layout(editor, 460, 640);
        if (query.Groups[0].Rules[2].Value?.ToString() != "saved value" || query.Groups[0].Rules[2].Op != "vendor_op" || query.Match != "any") throw new InvalidOperationException("Guided/Advanced round trip discarded a saved unknown rule or top match.");
        if (!Descendants<ComboBox>(editor).Any(combo => AutomationProperties.GetName(combo) == "Rule value")) throw new InvalidOperationException("Boolean rule must use a native boolean selector.");
        await Capture(editor, "browse-acceptance-range-recovery.png"); parent.Children.Remove(editor);
    }

    private static async Task CounterModesAsync(StackPanel parent)
    {
        var oldWidth = parent.Width; var oldHeight = parent.Height;
        try
        {
            foreach (var tall in new[] { false, true })
            {
                parent.Width = 460; parent.Height = 900; await Layout(parent, 460, 900);
                var hero = new HeroCarousel { Width = 460, IsTall = tall, ItemsSource = new List<MediaItem> { new() { ContentId = "hero-one", Type = "movie", Title = "First featured title" }, new() { ContentId = "hero-two", Type = "movie", Title = "Second featured title" } } };
                parent.Children.Add(hero); await Layout(hero, 460, tall ? 540 : 486); Call(hero, "UpdateHeightFromWindow"); await Task.Delay(100);
                var counter = (FrameworkElement)hero.FindName("SlideControlsPanel"); var counterOrigin = counter.TransformToVisual(hero).TransformPoint(new(0, 0));
                if (Math.Abs(counterOrigin.Y - (tall ? 96 : 16)) > .5 || counter.VerticalAlignment != VerticalAlignment.Top) throw new InvalidOperationException("Narrow hero counter placement differs for home/library mode.");
                if (((FrameworkElement)hero.FindName("ProgressRailContainer")).Visibility != Visibility.Collapsed) throw new InvalidOperationException("Narrow hero progress rail must collapse.");
                await Capture(hero, $"browse-acceptance-hero-{(tall ? "library" : "home")}.png"); parent.Children.Remove(hero);
            }
        }
        finally { parent.Width = oldWidth; parent.Height = oldHeight; }
    }

    private static async Task MountedCardPreferencesAsync(StackPanel parent, Wire wire, CardOverlayService overlays)
    {
        await overlays.RefreshPreferencesAsync();
        var card = new PosterCard { Width = 180, MediaItem = new() { ContentId = "card-one", Type = "movie", Title = "Mounted card", UserState = new() } }; parent.Children.Add(card); await Layout(card, 180, 330);
        foreach (var mode in new[] { "favorites", "watched", "both", "none" })
        {
            wire.QuickMode = mode; wire.QuickEnabled = true; await overlays.RefreshPreferencesAsync(); await Task.Delay(100);
            var favorite = (Button)card.FindName("QuickFavoriteButton"); var watched = (Button)card.FindName("QuickWatchedButton");
            if ((favorite.Visibility == Visibility.Visible) != (mode is "favorites" or "both") || (watched.Visibility == Visibility.Visible) != (mode is "watched" or "both")) throw new InvalidOperationException($"Mounted card did not refresh {mode} actions from settings Changed.");
        }
        wire.QuickEnabled = false; wire.QuickMode = "both"; await overlays.RefreshPreferencesAsync(); await Task.Delay(100);
        if (((Button)card.FindName("QuickFavoriteButton")).Visibility != Visibility.Collapsed || ((Button)card.FindName("QuickWatchedButton")).Visibility != Visibility.Collapsed) throw new InvalidOperationException("Disabled quick actions remained on the mounted card.");
        await Capture(card, "browse-acceptance-card-disabled.png"); parent.Children.Remove(card);
    }

    private static async Task ManualOrderAsync(StackPanel parent, Wire wire)
    {
        var page = new CollectionEditorPage { Width = 460, Height = 800 }; parent.Children.Add(page); Set(page, "_editorActive", true); await (Task)Call(page, "LoadEditorAsync", "manual")!; await Layout(page, 460, 800);
        if (!page.ViewModel.CanReorderManualItems || page.ViewModel.ManualItems.Count != 2) throw new InvalidOperationException("Complete editable manual order did not enable reordering.");
        await page.ViewModel.MoveItemDownCommand.ExecuteAsync(page.ViewModel.ManualItems[0]); await Layout(page, 460, 800);
        if (!wire.Order.SequenceEqual(new[] { "two", "one" }) || wire.LastIfMatch != "\"fixture-v1\"" || page.ViewModel.ManualItems[0].MediaItemId != "two") throw new InvalidOperationException("Manual command did not send complete order with captured ETag before native reorder.");
        wire.FailOrder = true; await page.ViewModel.MoveItemUpCommand.ExecuteAsync(page.ViewModel.ManualItems[1]); await Layout(page, 460, 800);
        if (page.ViewModel.CanReorderManualItems || page.ViewModel.ManualItems[0].MediaItemId != "two" || page.ViewModel.ErrorMessage == null) throw new InvalidOperationException("Order conflict moved native items or left stale order editable.");
        await Capture(page, "browse-acceptance-manual-order-conflict.png"); parent.Children.Remove(page); wire.FailOrder = false;
    }

    private static async Task ImportedDraftAsync(StackPanel parent, Wire wire)
    {
        var page = new CollectionEditorPage { Width = 460, Height = 900 }; parent.Children.Add(page); Set(page, "_editorActive", true); await (Task)Call(page, "LoadEditorAsync", "imported")!; await Layout(page, 460, 900);
        var dock = (FrameworkElement)page.FindName("CollectionDirtyDock"); if (dock.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Unchanged imported draft shows dirty dock.");
        page.ViewModel.Name = "Changed imported name"; await Layout(page, 460, 900);
        if (dock.Visibility != Visibility.Visible || ((TextBlock)page.FindName("CollectionDirtyCount")).Text != "1 change" || ((ScrollViewer)page.FindName("CollectionEditorScroll")).Padding.Bottom < 100) throw new InvalidOperationException("Imported edit did not show its one-change dock with reserved scroll space.");
        wire.FailSave = true; await page.ViewModel.SaveCommand.ExecuteAsync(null); await Layout(page, 460, 900);
        if (page.ViewModel.ErrorMessage == null || dock.Visibility != Visibility.Visible || page.ViewModel.Name != "Changed imported name") throw new InvalidOperationException("Imported save failure discarded draft or hid dirty recovery.");
        Call(page, "Discard_Click", page, new RoutedEventArgs()); await Wait(() => page.ViewModel.Name == "Imported fixture" && !page.ViewModel.IsLoading); await Layout(page, 460, 900);
        if (dock.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Discard failed to restore imported baseline.");
        page.ViewModel.Description = "Saved description"; wire.FailSave = false; await page.ViewModel.SaveCommand.ExecuteAsync(null); await Layout(page, 460, 900);
        if (page.ViewModel.ErrorMessage != null || wire.SavedDescription != "Saved description") throw new InvalidOperationException("Imported retry did not save the current draft.");
        await Capture(page, "browse-acceptance-imported-draft.png"); parent.Children.Remove(page);
    }

    private static async Task TemplateConfigAsync(StackPanel parent)
    {
        var page = new CollectionsPage { Width = 460, Height = 900 }; var template = new CollectionTemplate { Id = "fixture-template", Title = "A native template with a longer wrapped title", Description = "Template fixture description", Source = "tmdb", DefaultLimit = 100, DefaultSyncSchedule = "daily", MediaKind = "movie" };
        var card = (FrameworkElement)Call(page, "BuildTemplateCard", template)!; card.Width = 280; parent.Children.Add(card); await Layout(card, 280, 260);
        var title = Descendants<TextBlock>(card).Single(text => text.Text == template.Title); if (title.FontSize != 20 || title.TextWrapping != TextWrapping.Wrap) throw new InvalidOperationException("Template card title hierarchy/overflow regressed.");
        await Capture(card, "browse-acceptance-template-card.png"); parent.Children.Remove(card);
        var dialog = new ContentDialog { XamlRoot = parent.XamlRoot }; var panel = (FrameworkElement)Call(page, "BuildTemplateConfigPanel", template, dialog)!;
        parent.Children.Add(panel); await Layout(panel, 380, 900);
        if (panel.Width > 380 || panel.MinWidth > 380 || Descendants<FrameworkElement>(panel).Any(element => element.Width is 320 or 560)) throw new InvalidOperationException("Narrow template config retains a fixed width that overflows.");
        if (!Descendants<TextBox>(panel).Any(box => box.PlaceholderText == "No limit" && box.Text == "100")) throw new InvalidOperationException("Template defaults were not rendered.");
        await Capture(panel, "browse-acceptance-template-config.png"); parent.Children.Remove(panel);
    }

    private static async Task CalendarTodayAsync(StackPanel parent, Wire wire)
    {
        var page = new CalendarPage { Width = 460, Height = 900 }; parent.Children.Add(page); await Layout(page, 460, 900);
        page.ViewModel.WeekStart = CalendarViewModel.AddDays(CalendarViewModel.GetWeekStart(DateTime.Today), -14);
        wire.CalendarCalls = 0;
        var today = Descendants<Button>(page).Single(button => button.Content is string text && text == "Today");
        ((IInvokeProvider)new ButtonAutomationPeer(today).GetPattern(PatternInterface.Invoke)).Invoke();
        await Wait(() => !page.ViewModel.IsLoading && wire.CalendarCalls == 1); await Layout(page, 460, 900);
        if (page.ViewModel.WeekStart != CalendarViewModel.GetWeekStart(DateTime.Today) || !page.ViewModel.HasLoaded || page.ViewModel.ErrorMessage != null) throw new InvalidOperationException("Native Today navigation did not load the viewer's current week.");
        ((IInvokeProvider)new ButtonAutomationPeer(today).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(150);
        if (wire.CalendarCalls != 1) throw new InvalidOperationException("Today reloaded a week already selected.");
        await Capture(page, "browse-acceptance-calendar-today.png"); parent.Children.Remove(page);
    }

    private static async Task MenuRolesAsync(StackPanel parent, AuthService auth)
    {
        var anchor = new Button { Content = "Menu fixture", Width = 180 }; parent.Children.Add(anchor); await Layout(anchor, 180, 40);
        foreach (var role in new[] { "viewer", "curator", "admin", "secondary" })
        {
            auth.SetCurrentUser(new() { Id = "fixture", Role = role is "admin" or "secondary" ? "admin" : "user", Permissions = role == "curator" ? [AuthorizationPolicy.MetadataCuration] : [] });
            auth.SelectProfile("acceptance", profile: new() { Id = "acceptance", Name = "Fixture", IsPrimary = role == "admin" });
            var menu = MediaItemMenu.Build(new MediaItem { ContentId = "menu-one", Type = "movie", Title = "Menu movie", PositionSeconds = 10, ProgressUpdatedAt = "2026-10-01T00:00:00Z", UserState = new() }, MediaItemMenu.Surface.ContinueWatching);
            var names = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray();
            if (names.Contains("Edit Metadata") != (role is "curator" or "admin") || names.Contains("Play History") != (role == "admin") || names.Contains("Refresh Metadata") != (role is "curator" or "admin") || !names.Contains("Remove from Continue Watching")) throw new InvalidOperationException($"Native {role} menu exposed incorrect maintenance/dismissal actions.");
            menu.ShowAt(anchor); await Task.Delay(180);
            var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot)
                .SelectMany(popup => new DependencyObject[] { popup.Child }.Concat(Descendants<DependencyObject>(popup.Child)))
                .OfType<MenuFlyoutPresenter>().Single();
            if (!presenter.IsLoaded || presenter.ActualWidth <= 0 || presenter.ActualHeight <= 0) throw new InvalidOperationException($"Native {role} menu was not rendered.");
            var rowHeights = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.ActualHeight).ToArray();
            Program.Log($"Native {role} menu row heights: {string.Join(",", rowHeights)}.");
            if (rowHeights.Any(height => Math.Abs(height - 32) > .5)) throw new InvalidOperationException($"Native {role} media menu rows differ from official32px items: {string.Join(",", rowHeights)}.");
            foreach (var row in menu.Items.OfType<MenuFlyoutItem>())
            {
                var caption = Descendants<TextBlock>(row).Single(text => text.Text == row.Text);
                var icon = Descendants<Viewbox>(row).Single(part => part.Name == "IconRoot" && part.Visibility == Visibility.Visible);
                var captionOrigin = caption.TransformToVisual(row).TransformPoint(new(0, 0));
                var iconOrigin = icon.TransformToVisual(row).TransformPoint(new(0, 0));
                var offset = captionOrigin.X - iconOrigin.X;
                Program.Log($"Native {role} menu action '{row.Text}': icon={iconOrigin} {icon.ActualWidth}x{icon.ActualHeight}, caption={captionOrigin}, text/icon offset={offset}.");
                if (Math.Abs(icon.ActualWidth - 16) > .5 || Math.Abs(offset - 24) > .5)
                    throw new InvalidOperationException($"Native media menu icon16px +gap8px requires24px text offset; actual={offset}.");
            }
            var separatorHeights = menu.Items.OfType<MenuFlyoutSeparator>().Select(separator => separator.ActualHeight).ToArray();
            Program.Log($"Native {role} menu separator heights: {string.Join(",", separatorHeights)}.");
            if (separatorHeights.Any(height => Math.Abs(height - 9) > .5)) throw new InvalidOperationException($"Native {role} media menu separator spacing differs from official9px block: {string.Join(",", separatorHeights)}.");
            var visibleNames = Descendants<TextBlock>(presenter).Select(text => text.Text).ToArray();
            if (!names.All(visibleNames.Contains)) throw new InvalidOperationException($"Native {role} menu lost action captions while mounted.");
            Program.Log($"PASS: mounted {role} menu {presenter.ActualWidth}x{presenter.ActualHeight}: {string.Join(", ", names)}.");
            menu.Hide(); await Task.Delay(60);
        }
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); auth.SelectProfile("acceptance", profile: new() { Id = "acceptance", Name = "Fixture" }); parent.Children.Remove(anchor);
    }

    private static nint _nativeMpv;
    private static async Task NowListeningTransportAsync(StackPanel parent, SiloApiClient client, AuthService auth, SettingsService settings, Wire wire)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        using var player = new PlayerService(new PlaybackApi(client), new CatalogApi(client), auth, client, settings, new SettingsApi(client));
        servicesField.SetValue(null, new Services(previous, new() { [typeof(PlayerService)] = () => player }));
        object? mpv = null; NowListeningHero? hero = null;
        try
        {
            var wave = Path.Combine(Program.ResultDirectory, "browse-hero-silence.wav");
            using (var writer = new BinaryWriter(File.Create(wave)))
            {
                const int bytes = 8000 * 60 * 2;
                writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
            }
            if (_nativeMpv == 0)
            {
                _nativeMpv = NativeLibrary.Load(Path.Combine(Program.AppDirectory, "libmpv-2.dll"));
                NativeLibrary.SetDllImportResolver(typeof(PlayerService).Assembly, (name, _, _) => name.Equals("libmpv-2.dll", StringComparison.OrdinalIgnoreCase) ? _nativeMpv : 0);
            }
            mpv = Activator.CreateInstance(typeof(PlayerService).Assembly.GetType("SiloPlayer.Player.MpvPlayer", true)!)!;
            void Public(string name, params object?[] args) => mpv.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance)!.Invoke(mpv, args);
            void Property(string name, object value) => player.GetType().GetProperty(name)!.SetValue(player, value);
            void Raise(string name, object value) => (player.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player) as Delegate)?.DynamicInvoke(value);
            Public("Initialize", 16, 16); Public("SetProperty", "ao", "null"); Public("SetProperty", "video", "no"); Set(player, "_mpv", mpv);
            Property("IsAudiobook", true); Property("ContentId", "active-book"); Property("State", PlayerState.Minimized); Property("Duration", 60d);
            Property("Versions", new List<FileVersion> { new() { FileId = 1, Duration = 60, Chapters = [new() { Title = "Opening", StartSeconds = 0, EndSeconds = 1 }, new() { Title = "The next chapter", StartSeconds = 1, EndSeconds = 60 }] } });
            hero = new NowListeningHero { Width = 460 }; parent.Children.Add(hero); hero.Bind(new() { ContentId = "active-book", Type = "audiobook", Title = "Active native audiobook", DurationSeconds = 60 }); await Layout(hero, 460, 520);
            Action<bool> pause = paused => { Property("IsPaused", paused); Raise("PauseChanged", paused); };
            Action<double> position = seconds => { Property("Position", seconds); Raise("PositionChanged", seconds); };
            mpv.GetType().GetEvent("PauseChanged")!.AddEventHandler(mpv, pause); mpv.GetType().GetEvent("PositionChanged")!.AddEventHandler(mpv, position);
            Public("LoadFile", wave, null, 0d); Public("Play");
            await Wait(() => player.Position >= 1.1);
            // This fixture attaches mpv directly instead of PlaybackManager's
            // startup path. Synchronize its initial observed pause state even
            // when Play leaves mpv's already-unpaused value unchanged.
            pause((bool)mpv.GetType().GetProperty("IsPaused")!.GetValue(mpv)!);
            await Wait(() => ((TextBlock)hero.FindName("ResumeText")).Text == "Pause");
            if (!((TextBlock)hero.FindName("PositionText")).Text.Contains("Chapter 2 of 2") || ((Border)hero.FindName("ProgressFill")).Width <= 0 || string.IsNullOrWhiteSpace(((TextBlock)hero.FindName("TimeLeftText")).Text)) throw new InvalidOperationException("Now Listening did not render live native chapter/progress/time remaining.");
            var transport = Descendants<Button>(hero).Single(button => Descendants<TextBlock>(button).Any(text => text.Name == "ResumeText"));
            ((IInvokeProvider)new ButtonAutomationPeer(transport).GetPattern(PatternInterface.Invoke)).Invoke();
            await Wait(() => (bool)mpv.GetType().GetProperty("IsPaused")!.GetValue(mpv)! && ((TextBlock)hero.FindName("ResumeText")).Text == "Resume");
            await Capture(hero, "browse-acceptance-now-listening-paused.png");
            ((IInvokeProvider)new ButtonAutomationPeer(transport).GetPattern(PatternInterface.Invoke)).Invoke();
            await Wait(() => !(bool)mpv.GetType().GetProperty("IsPaused")!.GetValue(mpv)! && ((TextBlock)hero.FindName("ResumeText")).Text == "Pause");
            if (wire.PlaybackStarts != 0) throw new InvalidOperationException("Active hero transport started a new remote playback session.");
            mpv.GetType().GetEvent("PauseChanged")!.RemoveEventHandler(mpv, pause); mpv.GetType().GetEvent("PositionChanged")!.RemoveEventHandler(mpv, position);
            Program.Log("PASS: actual Now Listening native local mpv chapter2/progress/Pause/Resume without playback/start.");
        }
        finally
        {
            if (hero != null) parent.Children.Remove(hero);
            Set(player, "_mpv", null!); (mpv as IDisposable)?.Dispose(); servicesField.SetValue(null, previous);
        }
    }

    private static async Task NotificationsRecoveryAsync(StackPanel parent, Wire wire)
    {
        var page = new NotificationsPage { Width = 500, Height = 800 }; parent.Children.Add(page);
        Flyout? flyout = null;
        try
        {
            await Wait(() => page.IsLoaded && page.ViewModel.Notifications.Count == 1 && !page.ViewModel.IsLoading && page.ViewModel.HasLoadedPreferences);
            await Layout(page, 500, 800);
            var list = (ListView)page.FindName("NotificationsList");
            var row = (ListViewItem)list.ContainerFromItem(page.ViewModel.Notifications[0]);
            var mark = Descendants<Button>(row).Single(button => button.Name == "InlineMarkReadButton");
            wire.FailNotificationRead = true;
            mark.Focus(FocusState.Programmatic);
            ((IInvokeProvider)new ButtonAutomationPeer(mark).GetPattern(PatternInterface.Invoke)).Invoke();
            await Wait(() => !string.IsNullOrEmpty(page.ViewModel.ErrorMessage));
            if (!page.ViewModel.Notifications[0].IsUnread || ((Button)page.FindName("ReloadButton")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("Native failed mark-read hid its retained unread row or recovery action.");
            var reads = wire.NotificationReads;
            ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("ReloadButton")).GetPattern(PatternInterface.Invoke)).Invoke();
            await Wait(() => wire.NotificationReads > reads && string.IsNullOrEmpty(page.ViewModel.ErrorMessage));
            wire.FailNotificationRead = false;
            wire.HoldReadAll = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var all = (Button)page.FindName("MarkAllButton");
            ((IInvokeProvider)new ButtonAutomationPeer(all).GetPattern(PatternInterface.Invoke)).Invoke();
            await Wait(() => page.ViewModel.IsMarkingAllRead);
            if (all.IsEnabled || wire.NotificationCutoff != "native-cutoff") throw new InvalidOperationException("Native mark-all did not disable while pending or omitted the actual server cutoff.");
            wire.HoldReadAll.SetResult();
            await Wait(() => !page.ViewModel.IsMarkingAllRead);
            var preferences = (Button)page.FindName("PreferencesButton");
            if (preferences.Flyout is not Flyout preferencesFlyout) throw new InvalidOperationException("Native preferences flyout is missing.");
            flyout = preferencesFlyout; flyout.ShowAt(preferences);
            await Layout(page, 500, 800); await Task.Delay(120);
            var content = (FrameworkElement)flyout.Content;
            var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(popup => new DependencyObject[] { popup.Child }.Concat(Descendants<DependencyObject>(popup.Child))).OfType<FlyoutPresenter>().Single();
            Program.Log($"Native notification preferences: outer={presenter.ActualWidth}, content={content.ActualWidth}.");
            if (Math.Abs(presenter.ActualWidth - 320) > 1 || Math.Abs(content.ActualWidth - 288) > 1) throw new InvalidOperationException("Native notification preferences differ from320px outer/288px content.");
            wire.FailNotificationPreference = true;
            var favorites = (ToggleSwitch)page.FindName("FavoritesToggle");
            ((IToggleProvider)new ToggleSwitchAutomationPeer(favorites).GetPattern(PatternInterface.Toggle)).Toggle();
            await Wait(() => ((TextBlock)page.FindName("PreferenceErrorText")).Visibility == Visibility.Visible);
            if (!favorites.IsOn) throw new InvalidOperationException("Failed native preference write did not reconcile the authoritative enabled value.");
            await Capture(page, "browse-notification-recovery.png");
            Program.Log("PASS: actual native unread failure/Reload, cutoff/pending Mark all, preference320/288 popup and failed-toggle reconciliation.");
        }
        finally { wire.FailNotificationRead = wire.FailNotificationPreference = false; wire.HoldReadAll?.TrySetResult(); wire.HoldReadAll = null; flyout?.Hide(); page.ViewModel.CancelPendingLoad(); parent.Children.Remove(page); }
    }

    private static async Task Wait(Func<bool> ready) { for (var count = 0; count < 200 && !ready(); count++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Native acceptance operation did not settle."); }
    private static async Task Layout(FrameworkElement element, double width, double height) { element.Measure(new(width, height)); element.Arrange(new(0, 0, width, height)); element.UpdateLayout(); await Task.Delay(120); }
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static async Task Capture(FrameworkElement element, string name)
    {
        var panel = element switch { Page page => page.Content as Panel, UserControl control => control.Content as Panel, _ => null };
        var background = panel?.Background;
        var bitmap = new RenderTargetBitmap();
        try
        {
            if (panel != null && background == null) panel.Background = element is Page page ? page.Background : (Brush)Application.Current.Resources["AppBackgroundBrush"];
            await bitmap.RenderAsync(element);
        }
        finally { if (panel != null) panel.Background = background; }
        if (bitmap.PixelWidth == 0) throw new InvalidOperationException("Acceptance control was not rendered.");
        var pixels = await bitmap.GetPixelsAsync(); var folder = await StorageFolder.GetFolderFromPathAsync(Program.ResultDirectory); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, Func<object>> factories) : IServiceProvider { public object? GetService(Type type) => factories.TryGetValue(type, out var factory) ? factory() : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal string QuickMode = "both"; internal bool QuickEnabled = true; internal bool FailOrder; internal bool FailSave; internal string? LastIfMatch; internal string SavedDescription = ""; internal string[] Order = ["one", "two"]; internal int CalendarCalls; internal int PlaybackStarts;
        internal bool FailNotificationRead, FailNotificationPreference; internal int NotificationReads; internal string? NotificationCutoff; internal TaskCompletionSource? HoldReadAll;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "browse-acceptance.invalid") throw new InvalidOperationException("Browse acceptance attempted external network.");
            var path = request.RequestUri.AbsolutePath; object body = new { items = Array.Empty<object>(), page = new { has_more = false } }; var status = HttpStatusCode.OK;
            if (path.EndsWith("/capabilities")) body = new { manifest_revision = 15, item_reorder = true };
            if (path.EndsWith("/overlay-config")) body = new { enabled = true };
            if (path == "/api/v2/calendar") { CalendarCalls++; body = new { events = Array.Empty<object>() }; }
            if (path == "/api/v2/notifications") { NotificationReads++; body = new { items = new[] { new { id = "native-notification", type = "episode.available", series_id = "native-series", episode_id = "native-episode", series_title = "Native series", episode_title = "Native episode", created_at = "2026-10-01T12:00:00Z" } }, read_cutoff = "native-cutoff" }; }
            if (path == "/api/v2/notifications/unread-count") body = new { count = 1 };
            if (path == "/api/v2/notifications/preferences")
            {
                if (request.Method == HttpMethod.Put && FailNotificationPreference) { status = HttpStatusCode.ServiceUnavailable; body = new { message = "fixture preference rejected" }; }
                else body = new { enabled = true, notify_favorites = true, notify_watchlist = true, notify_continue_watching = true, notify_next_up = true };
            }
            if (path == "/api/v2/notifications/native-notification/read" && FailNotificationRead) { status = HttpStatusCode.ServiceUnavailable; body = new { message = "fixture read rejected" }; }
            if (path == "/api/v2/notifications/read-all")
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); NotificationCutoff = json.RootElement.GetProperty("through").GetString();
                if (HoldReadAll != null) await HoldReadAll.Task;
            }
            if (path == "/api/v2/catalog/items/active-book") body = new
            {
                content_id = "active-book", type = "audiobook", title = "Active native audiobook",
                audiobook = new { total_duration_seconds = 60, authors = new[] { new { name = "Fixture author" } }, narrators = new[] { new { name = "Fixture narrator" } } },
                versions = new[] { new { file_id = 1, duration = 60, chapters = new[] { new { title = "Opening", start_seconds = 0, end_seconds = 1 }, new { title = "The next chapter", start_seconds = 1, end_seconds = 60 } } } }
            };
            if (path.Contains("/playback/")) { PlaybackStarts++; throw new InvalidOperationException("Browse acceptance attempted remote playback."); }
            if (path.EndsWith("/values/effective")) body = new { items = new object[] { new { key = "ui.card_quick_actions_enabled", value = (object)QuickEnabled }, new { key = "ui.card_quick_actions", value = (object)QuickMode }, new { key = "ui.card_overlays_enabled", value = (object)true } } };
            if (path.EndsWith("/items/order"))
            {
                if (request.Method == HttpMethod.Put) { LastIfMatch = request.Headers.IfMatch.Single().ToString(); if (FailOrder) { status = HttpStatusCode.PreconditionFailed; body = new { message = "fixture order changed" }; } else { using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Order = json.RootElement.GetProperty("ordered_ids").EnumerateArray().Select(value => value.GetString()!).ToArray(); } }
                if (status == HttpStatusCode.OK) body = new { ordered_ids = Order, has_more = false };
            }
            else if (path == "/api/v2/collections/manual/items") body = new { items = Order.Select((id, index) => new { media_item_id = id, content_id = id, title = "Manual " + id, type = "movie", position = index }).ToArray(), page = new { has_more = false } };
            else if (path is "/api/v2/collections/manual" or "/api/v2/collections/imported")
            {
                if (request.Method == HttpMethod.Patch) { if (FailSave) { status = HttpStatusCode.ServiceUnavailable; body = new { message = "fixture save rejected" }; } else { using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); if (json.RootElement.TryGetProperty("description", out var description)) SavedDescription = description.GetString() ?? ""; } }
                if (status == HttpStatusCode.OK) body = new { id = path.EndsWith("/manual") ? "manual" : "imported", name = path.EndsWith("/manual") ? "Manual fixture" : "Imported fixture", description = SavedDescription, collection_type = path.EndsWith("/manual") ? "manual" : "mdblist", creator_profile_id = "acceptance", source_url = "https://mdblist.com/lists/fixture/native/", source_config = new { max_items = 100 }, sync_schedule = "daily", updated_at = "2026-10-01T00:00:00Z" };
            }
            var reply = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body)) }; reply.Headers.ETag = new("\"fixture-v1\""); return reply;
        }
    }
}

