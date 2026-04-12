using ContinuumPlayer.Controls;

namespace ContinuumPlayer.Services;

/// <summary>
/// F2: Application-wide toast notification service. Call
/// <see cref="Success"/> / <see cref="Error"/> / <see cref="Info"/> /
/// <see cref="Warning"/> from any ViewModel or page to surface transient
/// feedback; calls are no-ops until MainWindow wires up a
/// <see cref="ToastContainer"/> via <see cref="Register"/>.
///
/// Replaces the old <c>StatusToast</c> inline border in AdminSettingsDetailPage
/// (B27) and 20+ other ad-hoc status-message sites across the app.
/// </summary>
public class ToastService
{
    private ToastContainer? _container;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    /// <summary>
    /// Called once from <see cref="MainWindow"/> once the container is
    /// loaded. The service keeps a weak-ish reference (nulled on app close)
    /// and dispatches all <c>Show</c> calls to the container's UI thread.
    /// </summary>
    public void Register(ToastContainer container, Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _container = container;
        _dispatcher = dispatcher;
    }

    public void Unregister()
    {
        _container = null;
        _dispatcher = null;
    }

    public void Success(string message) => Show(message, ToastContainer.ToastKind.Success);
    public void Error(string message) => Show(message, ToastContainer.ToastKind.Error);
    public void Info(string message) => Show(message, ToastContainer.ToastKind.Info);
    public void Warning(string message) => Show(message, ToastContainer.ToastKind.Warning);

    private void Show(string message, ToastContainer.ToastKind kind)
    {
        if (_container == null || _dispatcher == null) return;
        _dispatcher.TryEnqueue(() => _container.Show(message, kind));
    }
}
