using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Messaging;

internal static class HomeDismissalNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-dismissal.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("fixture-profile");
        using var events = new EventChannelClient(client, auth);
        var home = new HomeApi(client);
        var vm = new HomeViewModel(home, auth);
        var toast = new ToastService();
        var services = new Services(previous, new Dictionary<Type, object>
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth,
            [typeof(HomeApi)] = home, [typeof(HomeViewModel)] = vm,
            [typeof(CatalogApi)] = new CatalogApi(client), [typeof(EventChannelClient)] = events,
            [typeof(ToastService)] = toast,
        });
        field.SetValue(null, services);
        var failures = new List<string>();
        var injectedFailure = Environment.GetEnvironmentVariable("SILO_NATIVE_DISMISS_FAULT") == "1";
        var faultyRecipient = new FaultyRecipient();
        if (injectedFailure) WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(faultyRecipient);
        EventHandler<FirstChanceExceptionEventArgs> trace = (_, args) =>
        {
            if (args.Exception is NullReferenceException)
            {
                failures.Add(args.Exception.ToString());
                Program.Log("Home dismissal exception: " + args.Exception);
            }
        };
        AppDomain.CurrentDomain.FirstChanceException += trace;
        var toasts = new ToastContainer();
        var page = new HomePage { Width = 900, Height = 750 };
        try
        {
            parent.Children.Add(toasts);
            toast.Register(toasts, parent.DispatcherQueue);
            parent.Children.Add(page);
            await Until(() => vm.Sections.Count == 2 && vm.Sections.All(section => section.LoadCompleted));
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_DISMISS_SCROLLED") == "1")
            {
                await Until(() => Descendants(page).OfType<SectionRow>().Any(row => row.Section?.SectionType == "next_up"));
                var row = Descendants(page).OfType<SectionRow>().First(row => row.Section?.SectionType == "next_up");
                ((ScrollViewer)row.FindName("CardsScrollViewer")).ChangeView(17 * 331d, null, null, true);
                await Task.Delay(80);
            }
            await Until(() => Descendants(page).OfType<LandscapeCard>().Any(card => card.MediaItem?.ContentId == "next-episode"));
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_DISMISS_CATCHUP") == "1")
            {
                var mountedSection = vm.Sections[0];
                var mountedCard = Descendants(page).OfType<LandscapeCard>().First(card => card.MediaItem?.ContentId == "next-episode");
                for (var recurrence = 0; recurrence < 10; recurrence++)
                {
                    var staleRead = client.CaptureContext();
                    client.InvalidateAccessContext();
                    var context = client.CaptureContext();
                    var method = typeof(HomePage).GetMethod("CatchUpAfterReconnectAsync");
                    if (method == null) await vm.LoadCommand.ExecuteAsync(null);
                    else await (Task)method.Invoke(page, [context])!;
                    await Until(() => vm.Sections.Count == 2 && vm.Sections.All(section => section.LoadCompleted));
                    await Task.Delay(100);
                    var currentCard = Descendants(page).OfType<LandscapeCard>().First(card => card.MediaItem?.ContentId == "next-episode");
                    if (!ReferenceEquals(mountedSection, vm.Sections[0]) || !ReferenceEquals(mountedCard, currentCard) || client.IsCurrentContext(staleRead))
                        throw new InvalidOperationException("Notification catch-up must retire old reads while preserving mounted Home rows and poster cards.");
                }
                Program.Log("PASS: ten notification catch-ups preserve mounted Home rows/cards while retiring stale reads.");
                client.InvalidateAccessContext();
                await vm.LoadCommand.ExecuteAsync(null); // Explicit access changes still retire the surface.
                await Until(() => vm.Sections.Count == 2 && vm.Sections.All(section => section.LoadCompleted)); await Task.Delay(100);
                if (ReferenceEquals(mountedSection, vm.Sections[0]) || Descendants(page).Contains(mountedCard))
                    throw new InvalidOperationException("Explicit permission refresh must retire the old Home surface.");
                Program.Log("PASS: explicit access changes still retire mounted Home content.");
                mountedSection = vm.Sections[0];
                auth.SelectProfile("other-fixture-profile");
                await vm.CatchUpAfterReconnectAsync(client.CaptureContext());
                await Until(() => vm.Sections.Count == 2 && vm.Sections.All(section => section.LoadCompleted));
                if (ReferenceEquals(mountedSection, vm.Sections[0])) throw new InvalidOperationException("Catch-up reused the previous profile's Home content.");
                Program.Log("PASS: catch-up never retains another profile's Home surface.");
                return;
            }
            var cycles = Environment.GetEnvironmentVariable("SILO_NATIVE_DISMISS_STRESS") == "1" ? 30 : 1;
            for (var cycle = 0; cycle < cycles; cycle++)
            {
                await Until(() => Descendants(page).OfType<LandscapeCard>().Any(card => card.MediaItem?.ContentId == "next-episode"));
                var card = Descendants(page).OfType<LandscapeCard>().First(card => card.MediaItem?.ContentId == "next-episode");
                var menu = (MenuFlyout)card.ContextFlyout;
                var more = (Button)card.FindName("DismissButton");
                ((IInvokeProvider)new ButtonAutomationPeer(more).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(40);
                var drop = menu.Items.OfType<MenuFlyoutItem>().SingleOrDefault(item => item.Text == "Remove from Next Up")
                    ?? throw new InvalidOperationException("Next Up menu must use the current WebUI label Remove from Next Up.");
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(drop).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => wire.DismissCalls == cycle + 1 && drop.IsEnabled);
                await Task.Delay(100);
                if (injectedFailure)
                {
                    var log = Path.Combine(Program.ResultDirectory, "home_actions.txt");
                    if (!File.Exists(log)) throw new InvalidOperationException("Caught dismissal failure discarded its diagnostic stack.");
                    var detail = File.ReadAllText(log);
                    if (!detail.Contains("stage=surface_update") || !detail.Contains(nameof(FaultyRecipient.Receive)) || !detail.Contains("NullReferenceException"))
                        throw new InvalidOperationException("Caught dismissal diagnostic must identify the failing stage and recipient stack.");
                    var toastLog = File.ReadAllText(Path.Combine(Program.ResultDirectory, "errors.txt"));
                    var httpLog = File.ReadAllText(Path.Combine(Program.ResultDirectory, "api_diagnostics.txt"));
                    if (!toastLog.Contains("caller=MediaItemMenu.cs") || !toastLog.Contains("NullReferenceException") || !httpLog.Contains("status=204"))
                        throw new InvalidOperationException("Error diagnostic must retain the caller and HTTP success independently of the UI failure.");
                    if (vm.Sections.Any(section => section.Items.Any(item => item.SeriesId == "show")))
                        throw new InvalidOperationException("The injected post-removal failure must occur after the show was removed.");
                    Program.Log("PASS: injected post-removal exception records its dismissal stage and complete recipient stack.");
                    return;
                }
                if (wire.DismissBody != "{\"series_id\":\"show\"}") throw new InvalidOperationException("Next Up dismissal did not send the show identity: " + wire.DismissBody);
                if (failures.Count > 0) throw new InvalidOperationException("Removal must not throw a NullReferenceException.\n" + string.Join("\n", failures));
                if (Descendants(toasts).OfType<TextBlock>().Any(text => text.Text.Contains("Object reference", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Removal displayed the reported Object reference error toast.");
                if (!vm.ShowUndoBanner || vm.Sections.Any(section => section.Items.Any(item => item.SeriesId == "show")))
                    throw new InvalidOperationException("Successful removal must remove both progress rows and expose Undo.");
                if (vm.UndoMessage != "Removed from Continue Watching and Next Up" ||
                    !Descendants(toasts).OfType<TextBlock>().Any(text => text.Text == vm.UndoMessage))
                    throw new InvalidOperationException("Removal confirmation must match the current WebUI.");
                await vm.UndoDismissalCommand.ExecuteAsync(null);
                if (wire.UndoCalls != cycle + 1 || vm.Sections.Count(section => section.Items.Any(item => item.SeriesId == "show")) != 2 || vm.ShowUndoBanner)
                    throw new InvalidOperationException("Undo must restore both removed progress cards without an error.");
                await Task.Delay(50);
            }
            Program.Log("PASS: real Home Remove from Next Up matches the current label/confirmation, sends the correct request, removes both show cards, shows no error toast and restores both with Undo.");
        }
        finally
        {
            toast.Unregister(); parent.Children.Remove(page); parent.Children.Remove(toasts);
            await Task.Delay(100);
            vm.SetActive(false); WeakReferenceMessenger.Default.UnregisterAll(vm);
            WeakReferenceMessenger.Default.UnregisterAll(faultyRecipient);
            AppDomain.CurrentDomain.FirstChanceException -= trace;
            field.SetValue(null, previous);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static async Task Until(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 150; attempt++) { if (ready()) return; await Task.Delay(20); }
        throw new InvalidOperationException("Home dismissal did not reach its expected state.");
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> overrides) : IServiceProvider
    {
        public object? GetService(Type type) => overrides.TryGetValue(type, out var service) ? service : fallback.GetService(type);
    }
    internal sealed class FaultyRecipient : IRecipient<MediaSurfaceChanged>
    {
        public void Receive(MediaSurfaceChanged message)
        {
            if (message.Kind == MediaSurfaceChangeKind.HomeDismissed)
                throw new NullReferenceException("Injected diagnostic-only recipient failure.");
        }
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal int DismissCalls, UndoCalls;
        internal string? DismissBody;
        private bool _dropped;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "home-dismissal.invalid") throw new InvalidOperationException("Unexpected external network.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/home/dismissals/next_up/next-episode")
            {
                if (request.Method == HttpMethod.Put)
                {
                    DismissBody = await request.Content!.ReadAsStringAsync(ct); DismissCalls++; _dropped = true;
                    // A real network mutation yields while the menu closes.
                    // A synchronously completed fake response misses that lifecycle.
                    await Task.Delay(100, ct);
                }
                else if (request.Method == HttpMethod.Delete) { UndoCalls++; _dropped = false; }
                else throw new InvalidOperationException("Wrong dismissal method.");
                return new(HttpStatusCode.NoContent);
            }
            string body;
            if (path.EndsWith("/home/layout")) body = """{"sections":[{"id":"next","section_type":"next_up","title":"Next Up"},{"id":"continue","section_type":"continue_watching","title":"Continue Watching"}]}""";
            else if (path.Contains("/home/sections/"))
            {
                var isNext = path.Contains("/next/");
                var count = isNext && Environment.GetEnvironmentVariable("SILO_NATIVE_DISMISS_SCROLLED") == "1" ? 24 : 1;
                var target = count == 24 ? 17 : 0;
                var items = JsonSerializer.Serialize(Enumerable.Range(0, count).Where(index => !_dropped || index != target).Select(index => new
                {
                    content_id = index == target ? isNext ? "next-episode" : "current-episode" : "other-" + index,
                    series_id = index == target ? "show" : "other-show-" + index,
                    type = "episode", title = "Fixture episode", series_title = "Fixture show",
                    item_source = isNext ? "next_up" : "continue_watching",
                    progress_updated_at = "2026-10-12T12:00:00Z", user_state = new { played = false },
                }));
                body = $$"""{"id":"{{(isNext ? "next" : "continue")}}","section_type":"{{(isNext ? "next_up" : "continue_watching")}}","title":"{{(isNext ? "Next Up" : "Continue Watching")}}","items":{{items}}} """;
            }
            else throw new InvalidOperationException("Unexpected isolated request: " + path);
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
