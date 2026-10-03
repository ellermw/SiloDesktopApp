using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Views;

internal static class MangaStickyNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var page = new ItemDetailPage { Width = 900, Height = 640 };
        try
        {
            var chapters = Enumerable.Range(1, 30).Select(index => new MangaChapter { ContentId = $"fixture-chapter-{index}", Title = $"Chapter {index}", ChapterIndex = index, Volume = "1", Read = index < 21 }).ToList();
            chapters.AddRange(Enumerable.Range(31, 15).Select(index => new MangaChapter { ContentId = $"fixture-chapter-{index}", Title = $"Chapter {index}", ChapterIndex = index, Volume = "2", Read = false }));
            chapters.AddRange(Enumerable.Range(46, 5).Select(index => new MangaChapter { ContentId = $"fixture-chapter-{index}", Title = $"Chapter {index}", ChapterIndex = index, Volume = "3", Read = true }));
            var item = new MediaItemDetail { ContentId = "fixture-manga", Type = "manga", Title = "The Fixture Manga", Manga = new() { Chapters = chapters } };
            page.ViewModel.Item = item; ((TextBlock)page.FindName("TitleText")).Text = item.Title;
            Invoke(page, "ConfigureBookDetail", item); parent.Children.Add(page);
            page.Measure(new Windows.Foundation.Size(900, 640)); page.Arrange(new Windows.Foundation.Rect(0, 0, 900, 640)); page.UpdateLayout(); await Task.Delay(100);
            var volumes = ((System.Collections.IEnumerable)typeof(ItemDetailPage).GetField("_mangaVolumes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!).Cast<ITuple>().Select(tuple => (Expander)tuple[0]!).ToArray();
            if (volumes.Length != 3 || !volumes[0].IsExpanded || !volumes[1].IsExpanded || volumes[2].IsExpanded) throw new InvalidOperationException("Manga read-volume collapse defaults are not retained.");
            var scroll = (ScrollViewer)page.FindName("ContentScroll"); var sticky = (FrameworkElement)page.FindName("MangaStickyHeader");
            double Top(FrameworkElement element) => element.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point()).Y;
            var firstTop = Top(volumes[0]) + scroll.VerticalOffset;
            scroll.ChangeView(null, firstTop + 100, null, true);
            await UntilAsync(() => sticky.Visibility == Visibility.Visible && Top(volumes[0]) < 0);
            page.UpdateLayout(); await Task.Delay(80);
            if (((TranslateTransform)sticky.RenderTransform).Y != 0) throw new InvalidOperationException("Long manga volume does not retain its header at the top of the viewport.");
            await MediaParityNativeFixture.CaptureAsync(page, "media-manga-volume-sticky.png");
            var height = sticky.ActualHeight;
            scroll.ChangeView(null, firstTop + volumes[0].ActualHeight - height / 2, null, true);
            await UntilAsync(() => Math.Abs(Top(volumes[0]) + volumes[0].ActualHeight - height / 2) < 1);
            Invoke(page, "UpdateMangaStickyHeader");
            var bottom = Top(volumes[0]) + volumes[0].ActualHeight;
            var actual = ((TranslateTransform)sticky.RenderTransform).Y;
            var expected = Math.Min(0, bottom - sticky.ActualHeight);
            Program.Log($"TRACE: manga sticky boundary header={sticky.ActualHeight}, volumeBottom={bottom}, transform={actual}, expected={expected}.");
            if (Math.Abs(actual - expected) > 1) throw new InvalidOperationException("Manga sticky header uses a fixed height and overlaps the next volume at its boundary.");
            await MediaParityNativeFixture.CaptureAsync(page, "media-manga-volume-pushout.png");
            var collapse = (Button)page.FindName("MangaStickyHeaderButton");
            ((IInvokeProvider)new ButtonAutomationPeer(collapse).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => !volumes[0].IsExpanded);
            volumes[0].IsExpanded = true; page.UpdateLayout(); scroll.ChangeView(null, 0, null, true); await Task.Delay(100);
            var resume = (FrameworkElement)typeof(ItemDetailPage).GetField("_mangaResumeRow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            Program.Log($"TRACE: manga jump before invoke offset={scroll.VerticalOffset}, viewport={scroll.ViewportHeight}, extent={scroll.ExtentHeight}, rowTop={Top(resume)}, rowHeight={resume.ActualHeight}, volumeHeight={volumes[0].ActualHeight}, loaded={resume.IsLoaded}, expanded={volumes[0].IsExpanded}.");
            ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("MangaJumpButton")).GetPattern(PatternInterface.Invoke)).Invoke();
            try { await UntilAsync(() => Top(resume) >= 0 && Top(resume) + resume.ActualHeight <= scroll.ViewportHeight); }
            catch
            {
                Program.Log($"TRACE: manga jump failed offset={scroll.VerticalOffset}, viewport={scroll.ViewportHeight}, extent={scroll.ExtentHeight}, rowTop={Top(resume)}, rowHeight={resume.ActualHeight}, volumeHeight={volumes[0].ActualHeight}, loaded={resume.IsLoaded}, visibility={resume.Visibility}, expanded={volumes[0].IsExpanded}.");
                await MediaParityNativeFixture.CaptureAsync(page, "media-manga-jump-failed.png");
                throw;
            }
            await MediaParityNativeFixture.CaptureAsync(page, "media-manga-jump-resume.png");
            Program.Log("PASS: MANGA_STICKY_ACCEPTANCE_COMPLETED actual long-volume pinning, measured-height boundary pushout, native collapse invocation, read-volume defaults and jump-to-resume scrolling.");
        }
        finally
        {
            if (typeof(ItemDetailPage).GetField("_rootElement", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page) is FrameworkElement root)
                root.SizeChanged -= (SizeChangedEventHandler)typeof(ItemDetailPage).GetMethod("OnRootSizeChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.CreateDelegate(typeof(SizeChangedEventHandler), page);
            parent.Children.Remove(page); await Task.Delay(100);
        }
    }
    private static object? Invoke(object target, string method, params object?[] arguments) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);
    private static async Task UntilAsync(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Manga sticky fixture did not settle."); }
}
