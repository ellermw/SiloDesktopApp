using Microsoft.UI.Dispatching;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

/// <summary>Reparents the existing control, including its running timers and action closures.</summary>
internal sealed class PlaybackToastHost : IDisposable
{
    private readonly ToastContainer _container;
    private readonly Panel _shell;
    private readonly int _shellIndex;
    private readonly DispatcherQueue _dispatcher;
    private readonly ToastHostPlacement<ToastContainer> _placement;
    private MpvVideoWindow? _surface;
    private PlaybackToastWindow? _overlay;
    private bool _queued;
    private bool _disposed;

    internal PlaybackToastHost(ToastContainer container, Panel shell, DispatcherQueue dispatcher)
    {
        _container = container;
        _shell = shell;
        _shellIndex = shell.Children.IndexOf(container);
        _dispatcher = dispatcher;
        _placement = new(container, Move);
        container.ToastsChanged += OnToastsChanged;
    }

    internal void SetPlaybackSurface(MpvVideoWindow surface)
    {
        if (_disposed || ReferenceEquals(_surface, surface)) return;
        _placement.Update(false, false, _container.HasToasts);
        if (_surface != null) _surface.SurfaceChanged -= OnSurfaceChanged;
        _surface = surface;
        surface.SurfaceChanged += OnSurfaceChanged;
        Refresh();
    }

    private void OnToastsChanged() => Refresh();

    private void OnSurfaceChanged()
    {
        // Dispose publishes a zero HWND before destroying the owner. Detach the
        // XAML control and close its owned window before that native destruction.
        if (_surface?.Hwnd == IntPtr.Zero && _dispatcher.HasThreadAccess)
        {
            Refresh();
            return;
        }
        // WM_SHOWWINDOW arrives before visibility is committed. Coalesce and
        // inspect the final state after the native callback has returned.
        if (_queued || _disposed) return;
        _queued = true;
        if (!_dispatcher.TryEnqueue(() => { _queued = false; Refresh(); })) _queued = false;
    }

    private void Refresh()
    {
        if (_disposed) return;
        try
        {
            if (_surface?.Hwnd == IntPtr.Zero)
            {
                _surface.SurfaceChanged -= OnSurfaceChanged;
                _surface = null;
            }
            _placement.Update(_surface?.CoversShell == true,
                _surface != null && PlaybackToastWindow.IsSurfaceVisible(_surface.Hwnd), _container.HasToasts);
            _overlay?.Position();
        }
        catch (Exception ex)
        {
            LocalLog.AppendException("errors.txt", ex, "playback_toast_host");
        }
    }

    private void Move(ToastContainer container, bool toPlayback)
    {
        if (toPlayback)
        {
            var overlay = new PlaybackToastWindow(_surface!.Hwnd);
            try
            {
                overlay.Root.RequestedTheme = container.ActualTheme;
                _shell.Children.Remove(container);
                overlay.Root.Children.Add(container);
                overlay.Show();
                _overlay = overlay;
                overlay.Closed += OnOverlayClosed;
            }
            catch
            {
                overlay.Root.Children.Remove(container);
                RestoreShell(container);
                overlay.Dispose();
                throw;
            }
        }
        else
        {
            var overlay = _overlay;
            _overlay = null;
            if (overlay == null) return;
            overlay.Closed -= OnOverlayClosed;
            overlay.Root.Children.Remove(container);
            RestoreShell(container);
            overlay.Dispose();
        }
    }

    private void RestoreShell(ToastContainer container)
        => _shell.Children.Insert(Math.Clamp(_shellIndex, 0, _shell.Children.Count), container);

    private void OnOverlayClosed() => _placement.Update(false, false, _container.HasToasts);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _container.ToastsChanged -= OnToastsChanged;
        if (_surface != null) _surface.SurfaceChanged -= OnSurfaceChanged;
        _surface = null;
        _placement.Update(false, false, _container.HasToasts);
    }
}
