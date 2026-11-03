using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Views;

internal static partial class EpisodeQuickPlayNativeFixture
{
    internal static async Task VerifyRailNavigationAsync()
    {
        using var fixture = new Fixture();
        await fixture.Spoilers.EnsureLoadedAsync();
        var failures = new List<string>();
        var page = new ItemDetailPage { Width = 1200, Height = 720 };
        page.ViewModel.Item = new() { ContentId = "rail-4", Type = "episode", SeriesId = "rail-series", SeasonNumber = 1 };
        using var navigation = new CancellationTokenSource();
        typeof(ItemDetailPage).GetField("_navigationCts", Private)!.SetValue(page, navigation);
        typeof(ItemDetailPage).GetField("_currentContentId", Private)!.SetValue(page, "rail-4");
        var episodes = (List<Episode>)typeof(ItemDetailPage).GetField("_siblingEpisodes", Private)!.GetValue(page)!;
        episodes.AddRange(Enumerable.Range(0, 30).Select(i => new Episode { ContentId = "rail-" + i, Title = "Episode " + i,
            SeasonNumber = 1, EpisodeNumber = i + 1, Files = [new() { FileId = 42, Unreadable = false }] }));
        var window = new Window { Content = page };
        window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.Resize(new(1400, 900)); window.AppWindow.Show(false);
        await Task.Delay(200); page.UpdateLayout();
        var section = RailElement<StackPanel>(page, "SiblingEpisodesSection");
        (RailElement<Panel>(page, "DetailContentPanel")).Children.Remove(section);
        if (typeof(ItemDetailPage).GetField("_tvNavigationItems", Private)!.GetValue(page) is Panel tv) tv.Children.Remove(section);
        section.Visibility = Visibility.Visible;
        var rail = RailElement<FrameworkElement>(page, "SiblingEpisodesScrollViewer");
        rail.Visibility = Visibility.Visible;
        var outer = RailElement<ScrollViewer>(page, "ContentScroll");
        var flow = new StackPanel();
        flow.Children.Add(new Border { Height = 80 }); flow.Children.Add(section); flow.Children.Add(new Border { Height = 3000 });
        outer.Content = flow;
        var index = (int)typeof(ItemDetailPage).GetMethod("BuildSiblingEpisodeCards", Private)!.Invoke(page, null)!;
        var cards = RailElement<StackPanel>(page, "SiblingEpisodesPanel");
        try
        {
            await Task.Delay(200); page.UpdateLayout();
            void Require(bool condition, string message) { if (!condition) failures.Add(message); }
            async Task Reset(double horizontal = 137)
            {
                outer.ChangeView(null, 0, null, true); RailChange(rail, horizontal); await Task.Delay(150); page.UpdateLayout();
            }
            // Assert the native policy on the realized presenter, not XAML text
            // or just the page's routed handler. Posted WM mouse messages do
            // not enter modern WinUI pointer input; diagnostics remain external.
            if (rail is ScrollView view)
            {
                view.ApplyTemplate(); page.UpdateLayout();
                var presenter = Descendants<ScrollPresenter>(view).Single();
                Require((presenter.IgnoredInputKinds & ScrollingInputKinds.MouseWheel) != 0,
                    "Realized sibling presenter still redirects native mouse-wheel input.");
                Require((presenter.IgnoredInputKinds & ScrollingInputKinds.Keyboard) != 0,
                    "Realized sibling presenter still competes with card-aligned keyboard navigation.");
                Require(presenter.HorizontalScrollMode == ScrollingScrollMode.Enabled && presenter.VerticalScrollMode == ScrollingScrollMode.Disabled,
                    "Sibling presenter lost horizontal manipulation or allows vertical manipulation.");
            }
            else failures.Add("Sibling rail cannot exclude native wheel input independently of manipulation modes.");
            foreach (var (width, expectedPage) in new[] { (420d, 344d), (1200d, 1008d) })
            {
                page.Width = width; page.UpdateLayout(); await Task.Delay(150);
                await Reset();
                var stride = ((FrameworkElement)cards.Children[0]).ActualWidth + cards.Spacing;
                var next = RailElement<Button>(page, "SiblingEpisodesNextButton");
                Invoke(next); await RailSettle(rail);
                var after = RailOffset(rail);
                Require(Math.Abs(after - expectedPage) < 1, $"Arrow at width {width} stopped at {after}; expected whole-card page {expectedPage}.");
                RailChange(rail, RailMaximum(rail) - 15); await Task.Delay(100); Invoke(next); await RailSettle(rail);
                Require(Math.Abs(RailOffset(rail) - RailMaximum(rail)) < 1 && !next.IsEnabled,
                    $"Next arrow failed final clamp/disabled state at width {width}.");
                var previous = RailElement<Button>(page, "SiblingEpisodesPrevButton");
                Invoke(previous); await RailSettle(rail);
                Require(IsBoundary(RailOffset(rail), stride, RailMaximum(rail)), $"Previous arrow left partial card at width {width}.");
                RailChange(rail, 15); await Task.Delay(100); Invoke(previous); await RailSettle(rail);
                Require(RailOffset(rail) < 1 && !previous.IsEnabled, $"Previous arrow failed first clamp/disabled state at width {width}.");
                Program.Log($"paging/{width}: stride={stride}; afterPartial={after}; maximum={RailMaximum(rail)}");
            }
            page.Width = 1200; page.UpdateLayout(); await Task.Delay(100);
            await Reset();
            var keys = new List<Windows.System.VirtualKey>();
            rail.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
            { keys.Add(args.Key); Program.Log($"Rail native KeyDown: {args.Key}; handled={args.Handled}"); }), true);
            ((Control)rail).Focus(FocusState.Keyboard); await Task.Delay(80);
            RailKey(0x27); await RailSettle(rail);
            Program.Log($"Rail after Right: {RailOffset(rail)}");
            Require(Math.Abs(RailOffset(rail) - 1008) < 1,
                "Owned Right key did not advance the actual rail to whole cards.");
            RailKey(0x25); await RailSettle(rail);
            Program.Log($"Rail after Left: {RailOffset(rail)}");
            Require(RailOffset(rail) < 1, "Owned Left key did not return to the first page.");
            Require(keys.SequenceEqual(new[] { Windows.System.VirtualKey.Right, Windows.System.VirtualKey.Left }),
                "Owned key messages did not enter the actual rail's native KeyDown boundary.");
            Program.Log($"Rail native keys: {string.Join(",", keys)}");
            typeof(ItemDetailPage).GetMethod("AlignSiblingEpisodeToStart", Private)!.Invoke(page, [index]);
            await Task.Delay(150);
            Require(index == 4 && Math.Abs(RailOffset(rail) - 1008) < 1, "Current episode alignment was lost.");
            var current = (FrameworkElement)cards.Children[index];
            var play = Descendants<Button>(current).Single(button => AutomationProperties.GetName(button).StartsWith("Play "));
            play.Focus(FocusState.Keyboard); await Task.Delay(80);
            typeof(ItemDetailPage).GetMethod("ScrollSiblingEpisodes", Private)!.Invoke(page, [1]); await RailSettle(rail);
            Require(ReferenceEquals(FocusManager.GetFocusedElement(page.XamlRoot), play), "Arrow paging changed the focused episode action.");
            Require(cards.Children[index] == current && fixture.Wire.Watches.Count == 0, "Navigation replaced current card or started quick play.");
            episodes.RemoveRange(2, episodes.Count - 2);
            typeof(ItemDetailPage).GetMethod("BuildSiblingEpisodeCards", Private)!.Invoke(page, null);
            page.UpdateLayout(); await Task.Delay(150);
            Require(RailMaximum(rail) < 1 && RailElement<Panel>(page, "SiblingEpisodesArrowPanel").Visibility == Visibility.Collapsed,
                "A short season retained overflow arrows after its extent changed.");

            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            Program.Log("PASS: More Episodes realized native input policy, partial-card paging, narrow/wide clamps, owned keyboard, current alignment, focused quick-play preservation and short-season extent changes. Physical wheel and pointer drag are not injected.");
        }
        finally { window.Close(); }
    }

    private static T RailElement<T>(ItemDetailPage page, string name) where T : class => (T)typeof(ItemDetailPage).GetField(name, Private)!.GetValue(page)!;
    private static bool IsBoundary(double offset, double stride, double maximum) => offset < 1 || Math.Abs(offset - maximum) < 1 || Math.Abs(offset / stride - Math.Round(offset / stride)) < .005;
    private static double RailOffset(FrameworkElement rail) => rail is ScrollViewer old ? old.HorizontalOffset : ((ScrollView)rail).HorizontalOffset;
    private static double RailMaximum(FrameworkElement rail) => rail is ScrollViewer old ? old.ScrollableWidth : ((ScrollView)rail).ScrollableWidth;
    private static void RailChange(FrameworkElement rail, double offset)
    {
        if (rail is ScrollViewer old) old.ChangeView(offset, null, null, true);
        else ((ScrollView)rail).ScrollTo(offset, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
    }

    private static async Task RailSettle(FrameworkElement rail)
    {
        await Task.Delay(100); // The native request is queued before State changes.
        if (rail is ScrollView view) await Until(() => view.State == ScrollingInteractionState.Idle);
        else await Task.Delay(700);
    }

    private static void RailKey(nuint key)
    {
        var target = RailGetFocus(); RailGetWindowThreadProcessId(target, out var process);
        Check(target != 0 && process == (uint)Environment.ProcessId, "Rail key target is not a focused owned HWND.");
        Check(RailPostMessage(target, 0x0100, key, 1) && RailPostMessage(target, 0x0101, key, unchecked((nint)0xC0000001u)),
            "Could not deliver a key to the owned rail fixture.");
    }
    [DllImport("user32.dll", EntryPoint = "GetFocus")] private static extern nint RailGetFocus();
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint RailGetWindowThreadProcessId(nint target, out uint process);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool RailPostMessage(nint target, uint message, nuint parameter, nint position);
}
