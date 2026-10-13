using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Services;

internal static class ToastDiagnosticsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var service = new ToastService();
        var container = new ToastContainer();
        parent.Children.Add(container);
        service.Register(container, parent.DispatcherQueue);
        // Reflection permits running this fixture against the frozen 1.2.243
        // signature as well as the new caller-information overload.
        var error = typeof(ToastService).GetMethods().Single(method => method.Name == "Error" && method.GetParameters()[0].ParameterType == typeof(string));
        var args = error.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();
        args[0] = "Queued diagnostic fixture error";
        error.Invoke(service, args);
        service.Unregister();
        await Task.Delay(100);
        var logPath = Path.Combine(Program.ResultDirectory, "errors.txt");
        if (!File.Exists(logPath) || !File.ReadAllText(logPath).Contains("Queued diagnostic fixture error"))
            throw new InvalidOperationException("An error toast must be logged even when its container is unregistered before rendering.");
        service.Register(container, parent.DispatcherQueue);
        Exception failure;
        try { throw new NullReferenceException("Diagnostic fixture exception"); }
        catch (Exception ex) { failure = ex; }
        var exceptionError = typeof(ToastService).GetMethods().Single(method => method.Name == "Error" && method.GetParameters()[0].ParameterType == typeof(Exception));
        args = exceptionError.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();
        args[0] = failure;
        exceptionError.Invoke(service, args);
        await Task.Delay(100);
        var log = File.ReadAllText(logPath);
        if (!log.Contains("NullReferenceException") || !log.Contains(nameof(RunAsync)) ||
            !log.Contains($"version={typeof(ToastService).Assembly.GetName().Version}"))
            throw new InvalidOperationException("Error toast must record exception type, source stack and installed version.");
        service.Unregister();
        parent.Children.Remove(container);
        Program.Log("PASS: error toasts retain their message and exception stack; unregistering between enqueue and rendering does not crash.");
    }
}
