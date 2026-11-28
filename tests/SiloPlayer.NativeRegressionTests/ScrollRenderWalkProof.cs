using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

/// <summary>
/// Measurement only (SILO_SCROLL_WALKPROOF=1): which still frames count for WinUI's working-set check. Each variant
/// first raises the working set by 70 MB of touched native memory (no managed allocation, so no collection of its
/// own), then runs 60 frames in a page that is not moving, and reports whether a blocking full collection followed.
/// </summary>
internal static class ScrollRenderWalkProof
{
    private const int Frames = 60;
    private const int Bytes = 70 << 20;

    internal static async Task RunAsync(Panel root)
    {
        var held = new List<IntPtr>();
        try
        {
            await VariantAsync(root, held, "control: frames tick (Rendering subscribed), nothing changes", null);
            var hidden = new Border { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var black = new SolidColorBrush(Microsoft.UI.Colors.Black); var white = new SolidColorBrush(Microsoft.UI.Colors.White);
            await VariantAsync(root, held, "invisible (Opacity 0) 1x1 element swaps its brush each frame", hidden, frame => hidden.Background = (frame & 1) == 0 ? black : white);
            var clear = new Border { Width = 1, Height = 1, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var none1 = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)); var none2 = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 255, 255, 255));
            await VariantAsync(root, held, "visible 1x1 element swaps between two fully transparent brushes", clear, frame => clear.Background = (frame & 1) == 0 ? none1 : none2);
            var faint = new Border { Width = 1, Height = 1, IsHitTestVisible = false, Background = black, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Opacity = 0 };
            await VariantAsync(root, held, "1x1 black element alternates Opacity 0 and 0.001", faint, frame => faint.Opacity = (frame & 1) == 0 ? 0 : 0.001);
            // What drawing costs when no collection is due: process CPU over 120 still frames, without and with the brush swap.
            await CostAsync("frames tick, nothing changes", null);
            root.Children.Add(clear);
            await CostAsync("1x1 element swaps between two fully transparent brushes", frame => clear.Background = (frame & 1) == 0 ? none1 : none2);
            root.Children.Remove(clear);
        }
        finally
        {
            foreach (var block in held) Marshal.FreeHGlobal(block);
            await Task.Delay(500);
            ScrollProcessMemory.Mark("render-walk proof: native blocks freed");
        }
    }

    /// <summary>The same cost on a built page (Home): its whole tree is what a render walk could visit.</summary>
    internal static async Task CostOnAsync(Panel root, string page)
    {
        var clear = new Border { Width = 1, Height = 1, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var none1 = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)); var none2 = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 255, 255, 255));
        await CostAsync($"{page}: frames tick, nothing changes", null);
        root.Children.Add(clear);
        await CostAsync($"{page}: 1x1 element swaps between two fully transparent brushes", frame => clear.Background = (frame & 1) == 0 ? none1 : none2);
        root.Children.Remove(clear);
        await CostAsync($"{page}: frames tick, nothing changes (again)", null);
    }

    private static async Task CostAsync(string name, Action<int>? perFrame)
    {
        await Task.Delay(300);
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var frames = 0;
        var done = new TaskCompletionSource();
        void OnRendering(object? sender, object e)
        {
            perFrame?.Invoke(frames);
            if (++frames < 120) return;
            CompositionTarget.Rendering -= OnRendering;
            done.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        await done.Task;
        process.Refresh();
        var used = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        Program.Log($"INFO: scroll render-walk cost, {name}: {frames} frames in {clock.ElapsedMilliseconds} ms, process CPU {used:0} ms ({used / frames:0.00} ms a frame)");
    }

    private static async Task VariantAsync(Panel root, List<IntPtr> held, string name, FrameworkElement? element, Action<int>? perFrame = null)
    {
        var block = Marshal.AllocHGlobal(Bytes);
        held.Add(block);
        for (var offset = 0; offset < Bytes; offset += 4096) Marshal.WriteByte(block, offset, 1);
        if (element != null) root.Children.Add(element);
        await Task.Delay(300);
        var gen2 = GC.CollectionCount(2);
        var blocking = GC.GetGCMemoryInfo(GCKind.FullBlocking).Index;
        ScrollProcessMemory.Mark($"render-walk proof, {name}: before ({Bytes >> 20} MB more touched)");
        var frames = 0;
        var done = new TaskCompletionSource();
        void OnRendering(object? sender, object e)
        {
            perFrame?.Invoke(frames);
            if (++frames < Frames) return;
            CompositionTarget.Rendering -= OnRendering;
            done.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        await done.Task;
        await Task.Delay(200);
        var collected = GC.GetGCMemoryInfo(GCKind.FullBlocking).Index != blocking;
        Program.Log($"INFO: scroll render-walk proof, {name}: {frames} still frames, blocking full collection {(collected ? "yes" : "no")} (gen2 +{GC.CollectionCount(2) - gen2})");
        ScrollProcessMemory.Mark($"render-walk proof, {name}: after");
        if (element != null) root.Children.Remove(element);
    }
}
