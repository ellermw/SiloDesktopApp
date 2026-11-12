using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Views;
using System.Reflection;

internal static partial class CollectionsPresentationNativeFixture
{
    private sealed partial class Run
    {
        private static async Task VerifyHeroEffectsAsync(CollectionBrowsePage page, CollectionHeroBackdrop backdrop)
        {
            var host = (FrameworkElement)page.FindName("CollectionTitleShadow");
            await Until(() => Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementChildVisual(host) is Microsoft.UI.Composition.SpriteVisual);
            var visual = (Microsoft.UI.Composition.SpriteVisual)Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementChildVisual(host);
            Check(visual.Shadow is Microsoft.UI.Composition.DropShadow { BlurRadius: 50, Opacity: .45f } shadow
                && shadow.Offset.Y == 18 && shadow.Mask != null, "Hero title must use its actual native text mask for the pinned shadow.");
            foreach (var name in new[] { "CollectionEditButton", "CollectionAddToHomeButton", "CollectionMoreButton" })
            {
                var button = (Button)page.FindName(name);
                Check(button.Background is CollectionGlassBrush, "Hero glass must render real native backdrop blur, not a translucent solid fill.");
                if (button.Visibility == Visibility.Visible && new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled)
                {
                    Check(typeof(XamlCompositionBrushBase).GetProperty("CompositionBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button.Background) is Microsoft.UI.Composition.CompositionEffectBrush,
                        "Supported native glass must connect the blur effect to the backdrop source.");
                    foreach (var state in new[] { "PointerOver", "Pressed" })
                    {
                        Check(VisualStateManager.GoToState(button, state, false), "Hero glass must support the native " + state + " state.");
                        button.UpdateLayout();
                        var root = Descendants<Border>(button).Single(border => border.Name == "CollectionGlassRoot");
                        Check(root.Background is CollectionGlassBrush glass && Math.Abs(glass.TintOpacity - .18) < .0001,
                            $"Hover/press must retain backdrop blur with the pinned 18% tint, not replace it with an opaque accent fill. Actual={root.Background?.GetType().Name}/{(root.Background as CollectionGlassBrush)?.TintOpacity:R}.");
                    }
                    VisualStateManager.GoToState(button, "Normal", false);
                }
            }
            var image = (BackdropImage)typeof(CollectionHeroBackdrop).GetField("_image", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backdrop)!;
            var transform = (CompositeTransform)image.FindName("KenBurnsTransform");
            if (new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
            {
                await Task.Delay(200);
                Check(image.UseCollectionPan && Math.Abs(transform.ScaleX - 1.06) < .0001 && Math.Abs(transform.ScaleY - 1.06) < .0001 && transform.TranslateX < 0 && transform.TranslateY < 0,
                    $"Collection hero must pan in both axes at a fixed overscan scale, not zoom/drift. Actual scale={transform.ScaleX}/{transform.ScaleY}, translate={transform.TranslateX}/{transform.TranslateY}, size={image.ActualWidth}/{image.ActualHeight}, enabled={image.EnableKenBurns}, loaded={image.IsLoaded}.");
            }
            var applyMotion = typeof(CollectionHeroBackdrop).GetMethod("ApplyMotionPreference", BindingFlags.Instance | BindingFlags.NonPublic)!;
            applyMotion.Invoke(backdrop, [false]);
            Check(!image.EnableKenBurns && transform.ScaleX == 1 && transform.TranslateX == 0 && transform.TranslateY == 0,
                "Reduced motion must stop the actual native storyboard and reset every transform.");
            var artwork = (Border)typeof(CollectionHeroBackdrop).GetField("_artwork", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backdrop)!;
            Check(artwork.OpacityTransition == null, "Reduced motion must also remove the artwork opacity transition.");
            applyMotion.Invoke(backdrop, [new Windows.UI.ViewManagement.UISettings().AnimationsEnabled]);
        }

        private async Task HeroArtworkOwnershipAsync()
        {
            wire.Reset(own: 0, shared: 0, libraries: 0);
            var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.BackdropGate = held; wire.BackdropGateIgnoresCancellation = true;
            var backdrop = new CollectionHeroBackdrop();
            var host = new Grid { Width = 900, Height = 400 }; host.Children.Add(backdrop); parent.Children.Add(host);
            try
            {
                await Layout(host, 900, 400);
                backdrop.Show("https://collections-presentation.invalid/backdrop/retired-art.png", SampleThumbhash, "retired-art");
                await Until(() => wire.BackdropRequests.Count == 1 && backdrop.ShowsPlaceholder);
                wire.BackdropGate = null;
                backdrop.Show("https://collections-presentation.invalid/backdrop/current-art.png", SampleThumbhash, "current-art");
                await Until(() => backdrop.ShowsArtwork);
                var image = (BackdropImage)typeof(CollectionHeroBackdrop).GetField("_image", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backdrop)!;
                var current = image.Source;
                held.SetResult(true);
                await Task.Delay(200);
                Check(ReferenceEquals(current, image.Source) && backdrop.BackdropUrl!.EndsWith("/current-art.png"), "A retired cancellation-ignoring artwork read must not replace its new owner's art.");
                parent.Children.Remove(host);
                await Task.Delay(100);
                Check(image.Source == null && !image.EnableKenBurns, "Unloading a collection hero must clear artwork and stop native motion.");
                parent.Children.Add(host);
                await Layout(host, 900, 400);
                await Until(() => backdrop.ShowsArtwork);
                Check(backdrop.BackdropUrl!.EndsWith("/current-art.png"), "A genuine reload must resume the current source, never the retired request.");
                backdrop.Clear();
                Check(image.Source == null && !image.EnableKenBurns && backdrop.Visibility == Visibility.Collapsed, "Clearing a hero must release the source and all motion.");
            }
            finally { held.TrySetResult(true); parent.Children.Remove(host); }
            Log("PASS: hero text-mask shadow, native blur8 glass, fixed-scale two-axis pan, reduced-motion stop, stale artwork owner and unload/reload cleanup.");
        }

        internal async Task ViewportAdmissionAsync()
        {
            wire.Reset(own: 60, shared: 0, libraries: 2);
            var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.RestGates[1] = held;
            var (frame, page) = await OpenWall(1280, 700);
            try
            {
                await Until(() => Names(page).Count == 63);
                var scroll = Descendants<ScrollViewer>(page).First(view => view.Content is StackPanel panel && panel.Name == "CollectionsPageShell");
                await Until(() => scroll.ScrollableHeight > 1000);
                await Task.Delay(200);
                Check(wire.TabReads(1) == 0, "A long wall must wait until it nears the actual viewport.");
                scroll.ChangeView(null, scroll.ScrollableHeight - 450, null, true);
                await Until(() => wire.TabReads(1) == 1, () => "Near-bottom ScrollViewer did not admit the first library rest.");
                var more = Descendants<Button>(page).Single(button => Auto.GetName(button) == "Load more server collections");
                more.Focus(FocusState.Keyboard);
                Invoke(more);
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                await Task.Delay(150);
                Check(wire.TabReads(1) == 1, "Held viewport reads must deduplicate.");
                Check(ReferenceEquals(FocusManager.GetFocusedElement(page.XamlRoot), more), "Busy explicit action must retain keyboard focus.");
                await Capture(page, "collections-viewport-loading-focus.png");
                held.SetResult(true);
                await Until(() => Names(page).Count == 64);
                await Capture(page, "collections-viewport-near-bottom.png");
            }
            finally { held.TrySetResult(true); parent.Children.Remove(frame); }
            await ShortWallContinuationAsync();
            await RetiredViewportLoadsAsync();
            await PickedLibraryRetiresAutomaticLoadAsync();
            Log("PASS: actual viewport admission, short-wall continuation, sequential/deduped held reads, explicit action focus and stale retirement.");
        }

        private async Task ShortWallContinuationAsync()
        {
            wire.Reset(own: 0, shared: 0, libraries: 2); wire.BothLibrariesTruncated = true;
            var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.RestGates[1] = first; wire.RestGates[2] = second;
            var (frame, page) = await OpenWall(1280, 900);
            try
            {
                await Until(() => wire.TabReads(1) == 1);
                await Task.Delay(150);
                Check(wire.TabReads(2) == 0, "Short walls must not admit a second library during a held first read.");
                first.SetResult(true);
                await Until(() => wire.TabReads(2) == 1);
                second.SetResult(true);
                await Until(() => Names(page).Count == 5);
                Check(Names(page).Distinct().Count() == 5 && wire.TabReads(1) == 1, "Short-wall continuation must preserve capped ordering without duplicate reads/cards.");
                await Capture(page, "collections-viewport-short-wall.png");
            }
            finally { first.TrySetResult(true); second.TrySetResult(true); parent.Children.Remove(frame); }
            wire.Reset(own: 0, shared: 0, libraries: 2); wire.FailLibraryTab = true;
            var (failedFrame, failedPage) = await OpenWall(1280, 900);
            try
            {
                await Until(() => ServerRows(failedPage).Any(button => Auto.GetName(button) == "Retry Movies"));
                await Task.Delay(350);
                Check(wire.TabReads(1) == 1 && Names(failedPage).Count == 3, "A failed short wall must keep capped cards without retry loops.");
            }
            finally { parent.Children.Remove(failedFrame); }
        }

        private async Task RetiredViewportLoadsAsync()
        {
            foreach (var retirement in new[] { "filter", "unload", "navigation", "account", "access", "library-access" })
            {
                wire.Reset(own: 0, shared: 0, libraries: 2); wire.BothLibrariesTruncated = true;
                var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.RestGates[1] = held;
                var client = (SiloPlayer.Core.Api.SiloApiClient)SiloPlayer.App.Services.GetService(typeof(SiloPlayer.Core.Api.SiloApiClient))!;
                var (frame, page) = await OpenWall(1280, 900);
                try
                {
                    await Until(() => wire.TabReads(1) == 1);
                    if (retirement == "filter") Invoke(Pill(page, "Mine"));
                    else if (retirement == "unload")
                    {
                        parent.Children.Remove(frame);
                        await Until(() => !page.IsLoaded);
                    }
                    else if (retirement == "navigation") frame.Navigate(typeof(Page));
                    else if (retirement == "account") client.SetProfile("retired-profile");
                    else if (retirement == "access") client.InvalidateAccessContext();
                    else
                    {
                        wire.Reset(own: 0, shared: 0, libraries: 0);
                        catalog.InvalidateLibraryCache();
                        await Until(() => page.ViewModel.ServerLibraries.Count == 0);
                    }
                    var state = $"{retirement}: loaded={page.IsLoaded}, busy={page.ViewModel.IsLoadingServerLibraryRest}, active={typeof(CollectionsPage).GetField("_wallActive", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)}";
                    Log("RETIRE " + state);
                    held.SetResult(true);
                    await Task.Delay(300);
                    Check(wire.TabReads(2) == 0 && !page.ViewModel.HasServerLibraryRest(1), $"{retirement} must retire held viewport results and continuation. Before release: {state}; after: rest={page.ViewModel.HasServerLibraryRest(1)}, nextReads={wire.TabReads(2)}");
                }
                finally { held.TrySetResult(true); parent.Children.Remove(frame); client.SetProfile("me"); }
            }
        }

        private async Task PickedLibraryRetiresAutomaticLoadAsync()
        {
            wire.Reset(own: 0, shared: 0, libraries: 2); wire.BothLibrariesTruncated = true;
            var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.RestGates[1] = held;
            var (frame, page) = await OpenWall(1280, 900);
            try
            {
                await Until(() => wire.TabReads(1) == 1);
                Invoke(Pill(page, "Shows"));
                await Until(() => wire.TabReads(2) == 1 && Names(page).SequenceEqual(["Anime", "Serial picks"]));
                held.SetResult(true);
                await Task.Delay(200);
                Check(!page.ViewModel.HasServerLibraryRest(1) && Names(page).SequenceEqual(["Anime", "Serial picks"]), "Picking a library must retire an old automatic read and load the picked rest immediately.");
            }
            finally { held.TrySetResult(true); parent.Children.Remove(frame); }
        }
    }
}
