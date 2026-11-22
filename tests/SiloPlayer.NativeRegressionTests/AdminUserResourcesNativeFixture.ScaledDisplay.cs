using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Views.Admin;
using Windows.Foundation;

/// <summary>
/// The shared names row of the Add users dialog on a display scaled to 125% or 150% (this machine runs at 100%). A desired width is rounded to the
/// device pixel grid, so a name's rounded width can be a fraction below what its text needs; a row that measures a cell again at exactly that
/// rounded width trims a name that fits. A probe that needs 100.4 reports a desired width of 100 and records the width it was last offered, so a
/// cell measured at its rounded width shows up as trimmed. The row is laid out in the live window (a popup on its XamlRoot), not in isolation.
/// </summary>
internal static partial class AdminUserResourcesNativeFixture
{
    /// <summary>An element that needs <see cref="Natural"/> to show its text whole and trims below it (like a no-wrap text with an ellipsis).</summary>
    private sealed class NameProbe : Panel
    {
        internal NameProbe(double natural) { Natural = natural; }
        internal double Natural { get; }
        internal double LastWidth { get; private set; } = double.NaN;
        internal bool Trimmed => LastWidth < Natural - 0.001;
        protected override Size MeasureOverride(Size availableSize) { LastWidth = availableSize.Width; return new Size(Math.Min(Natural, availableSize.Width), 10); }
        protected override Size ArrangeOverride(Size finalSize) => finalSize;
        public override string ToString() => $"need {Natural:0.##}: last offered {LastWidth:0.###}, desired {DesiredSize.Width:0.###}";
    }

    private static Rect SlotOf(UIElement element) => LayoutInformation.GetLayoutSlot((FrameworkElement)element);

    private static async Task ScaledDisplayAsync(Context c)
    {
        await ScenarioAsync(c, "scaled display: the shared names row keeps a name that fits whole", async () =>
        {
            var type = typeof(AdminAccessGroupsPage).Assembly.GetType("SiloPlayer.Views.Admin.SharedNamesRow") ?? throw new InvalidOperationException("SharedNamesRow is missing");
            // The rounded desired widths are 100 and 60 (needs 100.4 and 60.4) and the row's gap is 12: they exactly fill 172, and 171 is a pixel short.
            foreach (var width in new[] { 172.0, 171.0 })
            {
                NameProbe first = new(100.4), second = new(60.4);
                var row = (Panel)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [first, second], null)!;
                var host = new Grid { Width = width, HorizontalAlignment = HorizontalAlignment.Left }; host.Children.Add(row);
                var popup = new Popup { XamlRoot = c.Frame.XamlRoot, Child = host, IsOpen = true };
                try
                {
                    host.UpdateLayout(); await Task.Delay(80); host.UpdateLayout();
                    Program.Log($"INFO: shared names row {width}: {first} / {second}");
                    Check(first.DesiredSize.Width < 100.4, "the precondition holds: a child that needs 100.4 reports a rounded desired width below it (" + first.DesiredSize.Width.ToString("0.###") + ")");
                    Check(!first.Trimmed && !second.Trimmed, "a row " + width + " wide whose names fit within a pixel measures neither narrower than it needs (" + first + " / " + second + ")");
                    Check(SlotOf(first).Width >= 99.99 && SlotOf(second).Width >= 59.99, "both names are arranged at their own widths (" + SlotOf(first).Width.ToString("0.##") + ", " + SlotOf(second).Width.ToString("0.##") + ")");
                }
                finally { popup.IsOpen = false; }
            }
            {
                NameProbe first = new(100.4), second = new(60.4);
                var row = (Panel)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [first, second], null)!;
                var host = new Grid { Width = 130, HorizontalAlignment = HorizontalAlignment.Left }; host.Children.Add(row);
                var popup = new Popup { XamlRoot = c.Frame.XamlRoot, Child = host, IsOpen = true };
                try
                {
                    host.UpdateLayout(); await Task.Delay(80); host.UpdateLayout();
                    Check(first.Trimmed && second.Trimmed, "a row that really is too narrow still trims both long names (" + first + " / " + second + ")");
                    Check(SlotOf(first).Width + 12 + SlotOf(second).Width <= 130.01, "a row that really is too narrow keeps both names inside it");
                }
                finally { popup.IsOpen = false; }
            }
        });
    }
}
