using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Views;

internal static class DetailPresentationNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var description = new ExpandableDescription { Width = 500, Text = string.Join(" ", Enumerable.Repeat("A long description should remain readable without pushing the controls out of the hero.", 15)) };
        parent.Children.Add(description);
        await Task.Delay(80); description.UpdateLayout();
        var text = description.Children.OfType<TextBlock>().Single();
        var toggle = description.Children.OfType<Button>().Single();
        if (!text.IsTextTrimmed || toggle.Visibility != Visibility.Visible || text.MaxLines != 3)
            throw new InvalidOperationException("Native detail description doesn't offer More for clipped text.");
        var collapsedHeight = description.ActualHeight;
        typeof(ExpandableDescription).GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(description, null);
        description.UpdateLayout(); await Task.Delay(80);
        if (text.MaxLines != 0 || description.ActualHeight <= collapsedHeight || (string?)toggle.Content != "Less")
            throw new InvalidOperationException("Native detail description didn't expand.");
        description.Text = "Short description."; description.UpdateLayout(); await Task.Delay(80);
        if (toggle.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Short native description offers unnecessary More action.");
        parent.Children.Remove(description);

        var page = new ItemDetailPage { Width = 900, Height = 700 };
        parent.Children.Add(page);
        page.ViewModel.FailureKind = DetailFailureKind.Transient;
        page.ViewModel.ErrorMessage = DetailFailurePolicy.Message(DetailFailureKind.Transient);
        await Task.Delay(80);
        var error = (StackPanel)page.FindName("DetailErrorContent");
        if (error.Children.OfType<TextBlock>().Single().Text != page.ViewModel.ErrorMessage)
            throw new InvalidOperationException("Native detail error hides the real failure behind Item not found.");
        if (!error.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Any(button => (string?)button.Content == "Retry"))
            throw new InvalidOperationException("Native detail failure has no Retry action.");
        page.ViewModel.Item = new SiloPlayer.Core.Models.Catalog.MediaItemDetail
        {
            Type = "series", ContentId = "series", PlayContentId = "episode-97",
            UserData = new() { InProgressCount = 1, WatchedCount = 5 }
        };
        var apply = typeof(ItemDetailPage).GetMethod("ApplyAuthoritativeSeriesAction", BindingFlags.NonPublic | BindingFlags.Instance)!;
        apply.Invoke(page, null);
        var play = (FrameworkElement)page.FindName("SplitPlayButton");
        var label = (TextBlock)page.FindName("PlayButtonText");
        var resolve = typeof(ItemDetailPage).GetMethod("ResolvePlayableContentIdForPlaybackAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        if (play.Visibility != Visibility.Visible || label.Text != "Resume" ||
            await (Task<string?>)resolve.Invoke(page, null)! != "episode-97")
            throw new InvalidOperationException("Native series action ignores the server target or watch rollup.");
        page.ViewModel.Item.PlayContentId = null;
        apply.Invoke(page, null);
        if (play.Visibility != Visibility.Collapsed || label.Text != "Browse Series" ||
            await (Task<string?>)resolve.Invoke(page, null)! != null)
            throw new InvalidOperationException("Unavailable authoritative series target falls back to a stale episode.");
        page.ViewModel.Item = new SiloPlayer.Core.Models.Catalog.MediaItemDetail { Type = "series", ContentId = "unavailable" };
        typeof(ItemDetailPage).GetField("_playableContentId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, "stale-episode");
        apply.Invoke(page, null);
        if (play.Visibility != Visibility.Collapsed || await (Task<string?>)resolve.Invoke(page, null)! != null)
            throw new InvalidOperationException("An omitted server target revives stale episode playback.");
        parent.Children.Remove(page);
        Program.Log("PASS: native overview overflow/More/Less/short text and truthful retryable detail error.");
        Program.Log("PASS: native series action uses the server target/rollup and respects unavailable targets.");
    }
}
