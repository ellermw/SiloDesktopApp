using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

internal static class GroupedLibraryCardsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        Type? target = null; object? argument = null;
        var navigation = new NavigationService { Frame = new Frame(), NavigationRequestHandler = (type, args) => { target = type; argument = args; return true; } };
        field.SetValue(null, new Services(previous, navigation));
        var card = new LibraryGridCard(); parent.Children.Add(card);
        try
        {
            var summary = new BoxSetSummary { Id = "9007199254740993", LibraryId = "12", Title = "A long grouped library collection with missing artwork", MovieCount = 4, PlayedCount = 2, FirstYear = 2001, LastYear = 2018 };
            card.Bind(new MediaItem { Type = "box_set", ContentId = "box_set:12:9007199254740993", Title = summary.Title, BoxSet = summary }, "title");
            await Task.Delay(100); card.UpdateLayout();
            var aggregate = Descendants(card).OfType<BoxSetCard>().SingleOrDefault();
            Check(aggregate != null, "virtual Library card must use aggregate renderer");
            foreach (var width in new[] { 170d, 120d, 240d })
            {
                card.SetLayout(width, width * 1.5, width * 1.5 + 64); card.UpdateLayout(); await Task.Delay(50);
                Check(aggregate!.ActualWidth <= card.ActualWidth && aggregate.ActualHeight <= card.ActualHeight, "aggregate bounds within virtual slot");
                await CaptureAsync(card, $"grouped-card-{width}.png");
                Check(aggregate.Focus(FocusState.Keyboard), "aggregate keyboard focus");
                Check(Descendants(card).OfType<Button>().All(button => button == aggregate || !button.IsHitTestVisible || button.Visibility == Visibility.Collapsed), "no ordinary menu or watched controls");
                ((IInvokeProvider)new ButtonAutomationPeer(aggregate).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(20);
                Check(target == typeof(BoxSetPage) && argument is BoxSetNavigationArgs { Id: "9007199254740993", LibraryId: "12" }, "aggregate uses returned library destination");
            }
            Check(Descendants(aggregate!).OfType<DefaultArtwork>().Any(), "missing artwork fallback");
            var ordinary = new MediaItem { Type = "movie", ContentId = "ordinary", Title = "Ordinary film", UserState = new() };
            card.Tag = new ItemDetailNavigationArgs("ordinary", 9);
            card.Bind(ordinary, "title"); await Task.Delay(50);
            Check(!Descendants(card).OfType<BoxSetCard>().Any(), "recycle restores ordinary card");
            typeof(LibraryGridCard).GetMethod("OnTapped", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(card, new object?[] { card, null });
            Check(target == typeof(ItemDetailPage) && argument is ItemDetailNavigationArgs { ContentId: "ordinary", LibraryId: 9 }, "ordinary navigation retained");
            Check(MediaItemMenu.Build(ordinary, owner: card).Items.Count > 0, "ordinary action menu retained");
            card.Bind(new MediaItem { Type = "box_set", ContentId = "box_set:other", BoxSet = summary }, null);
            card.BindPlaceholder(); Check(!Descendants(card).OfType<BoxSetCard>().Any(), "placeholder retires aggregate");
            Program.Log("PASS: actual virtual aggregate renderer, missing art, keyboard/destination, responsive slot bounds, ordinary recycling/actions and placeholder ownership.");
        }
        finally { parent.Children.Remove(card); await Task.Delay(100); field.SetValue(null, previous); }
    }
    internal static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var path = Path.Combine(Program.ResultDirectory, name); File.WriteAllBytes(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels); await encoder.FlushAsync();
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Grouped cards: " + message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private sealed class Services(IServiceProvider previous, NavigationService navigation) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(NavigationService) ? navigation : previous.GetService(type); }
}
