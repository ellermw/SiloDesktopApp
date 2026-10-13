using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using System.Runtime.CompilerServices;

namespace SiloPlayer.Services;

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
    public void Error(string message, [CallerMemberName] string member = "",
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        using var scope = LocalLog.BeginOperation(LocalLog.CurrentOperationId ?? Guid.NewGuid().ToString("N"));
        LocalLog.AppendLine("errors.txt", $"utc={DateTime.UtcNow:O} version={typeof(App).Assembly.GetName().Version} action={LocalLog.CurrentOperationId} toast caller={Path.GetFileName(file)}:{line}:{member} message={LocalLog.RedactDiagnosticText(message)}");
        Show(message, ToastContainer.ToastKind.Error);
    }

    public void Error(Exception exception, string? message = null,
        [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        using var scope = LocalLog.BeginOperation(LocalLog.CurrentOperationId ?? Guid.NewGuid().ToString("N"));
        LocalLog.AppendLine("errors.txt", $"action={LocalLog.CurrentOperationId} displayedMessage={LocalLog.RedactDiagnosticText(message ?? exception.Message)}");
        LocalLog.AppendException("errors.txt", exception,
            $"toast version={typeof(App).Assembly.GetName().Version} caller={Path.GetFileName(file)}:{line}:{member}");
        LocalLog.AppendLines("errors.txt", App.GetUiDiagnosticSnapshot().Select(entry => $"action={LocalLog.CurrentOperationId} ui={entry}"));
        Show(message ?? exception.Message, ToastContainer.ToastKind.Error);
    }
    public void Info(string message) => Show(message, ToastContainer.ToastKind.Info);
    public void Warning(string message) => Show(message, ToastContainer.ToastKind.Warning);

    private void Show(string message, ToastContainer.ToastKind kind)
    {
        var container = _container;
        var dispatcher = _dispatcher;
        var actionId = LocalLog.CurrentOperationId;
        if (container == null || dispatcher == null) return;
        dispatcher.TryEnqueue(() =>
        {
            // A queued callback must not dereference a service that was
            // unregistered or replaced after it was queued.
            if (!ReferenceEquals(_container, container)) return;
            try { container.Show(message, kind); }
            catch (Exception ex) { LocalLog.AppendException("errors.txt", ex, "ToastService.Show", actionId); }
        });
    }
}
