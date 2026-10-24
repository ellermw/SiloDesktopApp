using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Views;

internal static class DetailActionHoverNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        // Real compiled media-page controls, isolated from navigation, account
        // settings and playback. Exercise WinUI's actual state/template paint.
        var page = new ItemDetailPage();
        var play = (Button)page.FindName("PrimaryPlayButton");
        var pill = (Grid)page.FindName("SplitPlayButton");
        var progress = (Border)page.FindName("PlayProgressBar");
        var playText = (TextBlock)page.FindName("PlayButtonText");
        var actions = (SiloPlayer.Controls.WrapPanel)page.FindName("HeroActionsRow");
        actions.Children.Remove(pill);
        var watched = (Button)page.FindName("WatchedButton");
        var favorite = (Button)page.FindName("FavoriteButton");
        actions.Children.Remove(watched); actions.Children.Remove(favorite);
        var surface = new StackPanel { Width = 600, Padding = new Thickness(24), Spacing = 20,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
        surface.Children.Add(pill);
        surface.Children.Add(watched); surface.Children.Add(favorite);
        parent.Children.Add(surface);
        try
        {
            await Task.Delay(100); surface.UpdateLayout(); play.ApplyTemplate();
            progress.Width = pill.ActualWidth * .35;
            Program.Log($"Play geometry: pill={pill.ActualWidth:0.##}x{pill.ActualHeight:0.##}, hit target={play.ActualWidth:0.##}x{play.ActualHeight:0.##}.");
            VisualStateManager.GoToState(play, "Normal", false);
            var normal = await PixelsAsync(pill);
            await MediaParityNativeFixture.CaptureAsync(surface, "detail-actions-normal.png");
            if (!VisualStateManager.GoToState(play, "PointerOver", false))
                throw new InvalidOperationException("Play has no native PointerOver state.");
            await Task.Delay(220);
            var hovered = await PixelsAsync(pill);
            await MediaParityNativeFixture.CaptureAsync(surface, "detail-actions-hover.png");
            var changed = normal.Pixels.Zip(hovered.Pixels).Count(pair => pair.First != pair.Second);
            Program.Log($"Play hover paint with transform normalized: {changed} changed channels (primary/progress paint must remain unchanged). ");
            if (changed != 0)
                throw new InvalidOperationException("Play hover repaints a partial gray surface over the primary pill/progress instead of preserving its paint.");
            if (Math.Abs(play.ActualHeight - pill.ActualHeight) > .1)
                throw new InvalidOperationException("Play hit target does not cover the full painted pill height.");
            var scale = ElementCompositionPreview.GetElementVisual(pill).Scale;
            var expectedScale = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled ? 1.02f : 1f;
            if (Math.Abs(scale.X - expectedScale) > .001 || Math.Abs(scale.Y - expectedScale) > .001)
                throw new InvalidOperationException($"Play hover scale {scale} does not match the WebUI's motion-safe scale {expectedScale}.");
            foreach (var label in new[] { "Play", "Resume", "Start From Episode 1", "Continue Reading" })
            foreach (var fraction in new[] { 0d, .35, .98 })
            {
                playText.Text = label;
                surface.UpdateLayout();
                progress.Width = pill.ActualWidth * fraction;
                VisualStateManager.GoToState(play, "Normal", false);
                await Task.Delay(180);
                normal = await PixelsAsync(pill);
                foreach (var (state, factor) in new[] { ("PointerOver", 1.02f), ("Pressed", .98f), ("Normal", 1f) })
                {
                    VisualStateManager.GoToState(play, state, false);
                    await Task.Delay(180);
                    var painted = await PixelsAsync(pill);
                    if (!normal.Pixels.SequenceEqual(painted.Pixels))
                        throw new InvalidOperationException($"{label}/{fraction}/{state}: unexpected primary/progress repaint.");
                    CheckScale(pill, factor, $"{label}/{state}");
                }
            }
            foreach (var (state, factor) in new[] { ("PointerOver", 1.02f), ("Pressed", .98f), ("Normal", 1f) })
            {
                watched.ApplyTemplate();
                VisualStateManager.GoToState(watched, state, false);
                await Task.Delay(180);
                CheckScale(watched, factor, $"Watched/{state}");
                favorite.ApplyTemplate(); VisualStateManager.GoToState(favorite, state, false);
                CheckScale(favorite, 1, $"Favorite/{state}");
            }
            if (!play.IsTabStop || !play.UseSystemFocusVisuals || string.IsNullOrWhiteSpace(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(play)))
                throw new InvalidOperationException("Play lost keyboard focus or its accessible name.");
            play.IsEnabled = false;
            await Task.Delay(180); CheckScale(pill, 1, "Disabled");
            play.IsEnabled = true;
            surface.Children.Remove(pill);
            await Task.Delay(60); CheckScale(pill, 1, "Unloaded");
            surface.Children.Insert(0, pill);
            await Task.Delay(60);
            VisualStateManager.GoToState(play, "PointerOver", false);
            await Task.Delay(180); CheckScale(pill, 1.02f, "Reloaded hover");
            await MediaParityNativeFixture.CaptureAsync(surface, "detail-actions-final.png");
            Program.Log("PASS: real media Play hover retains complete primary/progress paint and scales the whole pill.");
            Program.Log("PASS: 12 label/progress combinations; hover/press/normal, watched and static favorite states, keyboard focus/name, disabled and unload/reload lifecycle.");
        }
        finally { parent.Children.Remove(surface); }
    }

    private static void CheckScale(FrameworkElement surface, float expected, string context)
    {
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) expected = 1;
        var visual = ElementCompositionPreview.GetElementVisual(surface);
        if (Math.Abs(visual.Scale.X - expected) > .001 || Math.Abs(visual.Scale.Y - expected) > .001)
            throw new InvalidOperationException($"{context}: scale {visual.Scale}, expected {expected}.");
        if (expected != 1 && (Math.Abs(visual.CenterPoint.X - surface.ActualWidth / 2) > .1 || Math.Abs(visual.CenterPoint.Y - surface.ActualHeight / 2) > .1))
            throw new InvalidOperationException($"{context}: feedback is not centered after resizing.");
    }

    private static async Task<(byte[] Pixels, int Width, int Height)> PixelsAsync(FrameworkElement element)
    {
        // Compare the state paint in common coordinates. RenderTargetBitmap
        // includes compositor scale; counting those edge/text movements as a
        // background repaint would reject the intended hover animation itself.
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var animations = visual.ImplicitAnimations;
        var scale = visual.Scale;
        visual.ImplicitAnimations = null;
        visual.StopAnimation("Scale");
        visual.Scale = System.Numerics.Vector3.One;
        try
        {
            await Task.Delay(40);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(element);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            if (bitmap.PixelWidth == 0 || pixels.Length != bitmap.PixelWidth * bitmap.PixelHeight * 4)
                throw new InvalidOperationException("No realized native hover pixels.");
            return (pixels, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        finally { visual.Scale = scale; visual.ImplicitAnimations = animations; }
    }
}
