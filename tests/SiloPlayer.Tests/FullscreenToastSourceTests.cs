using System.Runtime.CompilerServices;

namespace SiloPlayer.Tests;

public sealed class FullscreenToastSourceTests
{
    [Fact]
    public void OverlayIsOwnedNonactivatingAndClippedToCards()
    {
        var source = Read("Services", "PlaybackToastWindow.cs");
        Assert.Contains("SetWindowLongPtrW(_hwnd, GwlpHwndParent, videoHwnd)", source);
        Assert.Contains("WsExNoActivate = 0x08000000", source);
        Assert.Contains("WsExToolWindow = 0x00000080", source);
        Assert.Contains("_window.AppWindow.Show(false)", source);
        Assert.Contains("SwpNoActivate |", source);
        Assert.Contains("WM_MOUSEACTIVATE", source);
        Assert.Contains("new IntPtr(3)", source);
        Assert.Contains("ApplyRegion([])", source);
        Assert.Contains("SetWindowRgn(_hwnd, region, true)", source);
        Assert.Contains("RGN_OR", source);
        Assert.Contains("DeleteObject(card)", source);
        Assert.DoesNotContain("SetForegroundWindow", source);
        Assert.DoesNotContain(".Activate()", source);
        Assert.DoesNotContain("HWND_TOPMOST", source);
    }

    [Fact]
    public void HostMovesTheSameContainerAndDisposesOnEveryRetirementPath()
    {
        var host = Read("Services", "PlaybackToastHost.cs");
        Assert.Contains("_shell.Children.Remove(container)", host);
        Assert.Contains("overlay.Root.Children.Add(container)", host);
        Assert.Contains("overlay.Root.Children.Remove(container)", host);
        Assert.Contains("RestoreShell(container)", host);
        Assert.Contains("overlay.Dispose()", host);
        Assert.Contains("_surface?.Hwnd == IntPtr.Zero", host);
        Assert.Contains("SurfaceChanged -= OnSurfaceChanged", host);
        Assert.Contains("ToastsChanged -= OnToastsChanged", host);
        Assert.DoesNotContain("new ToastContainer", host);
        Assert.DoesNotContain("DispatcherTimer", host);
        var service = Read("Services", "ToastService.cs");
        Assert.Contains("_playbackHost?.Dispose()", service);
        Assert.Contains("SetPlaybackSurface", Read("Services", "PlayerService.cs"));
        Assert.Contains("ToastService>().Unregister()", Read("MainWindow.xaml.cs"));
        var window = Read("Services", "PlaybackToastWindow.cs");
        Assert.Contains("Root.LayoutUpdated -= OnLayoutUpdated", window);
        Assert.Contains("RemoveWindowSubclass(_hwnd, _subclass, 1)", window);
        Assert.Contains("if (!_closed) _window.Close()", window);
    }

    [Fact]
    public void ToastExpiryAndActionGuardAreIndependentOfPresentationChanges()
    {
        var container = Read("Controls", "ToastContainer.xaml.cs");
        Assert.Contains("if (!button.IsEnabled) return;", container);
        Assert.Contains("button.IsEnabled = false;", container);
        Assert.Contains("AllowFocusOnInteraction = false", container);
        Assert.Contains("_dismissing.Add(toast)", container);
        Assert.Contains("previous.Stop()", container);
        Assert.Contains("retirement.Start()", container);
        Assert.Contains("toast.Opacity = 1;", container);
        Assert.DoesNotContain("sb.Completed +=", container);
        var video = Read("Services", "MpvVideoWindow.cs");
        Assert.Contains("CoversShell => !_isMiniBar && !_isPictureInPicture", video);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([SourceRoot(), "src", "SiloPlayer", .. parts]));

    // Bind source assertions to this compilation's checkout even when the lead
    // runs the test DLL from shared, external build outputs.
    private static string SourceRoot([CallerFilePath] string file = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
