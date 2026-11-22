using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

/// <summary>
/// Tells a lost web view browser process that the machine caused from one that the product or the page caused.
///
/// On the development machine the <c>msedgewebview2</c> browser process sometimes exits by itself with a memory-access violation
/// (0xC0000005) inside <c>RTSSHooks64.dll</c>, the RivaTuner Statistics Server / MSI Afterburner overlay hook that is injected into it.
/// Every Crashpad report of such a loss that was read (ten of ten, in the groups <c>admin-plugin-apps</c> and <c>plugin-cutover</c>) names that
/// module at the same offset as the faulting one. The product reacts as designed (it shows its load-failed state with Reload), so the
/// fixture's own page-level checks fail for a reason that is not a regression.
///
/// A loss counts as the environment's fault only when BOTH hold: the overlay hook module was loaded in this web view's browser process
/// when the watch attached, and that same process ended with exit code 0xC0000005 (read from the process handle held since attaching, and from the
/// control's own <c>ProcessFailed</c> notification). Any other failure, any loss without the hook, any other exit code and a process
/// that is still running all stay failures.
/// </summary>
internal sealed class WebViewBrowserLossWatch : IDisposable
{
    private const int AccessViolation = unchecked((int)0xC0000005);
    internal const int MaxStarts = 3;

    private Process? _browser;
    private volatile bool _hooked;
    private volatile bool _violated;

    /// <summary>True when the overlay hook was loaded in the browser process when the watch attached (the necessary half of the fault).</summary>
    internal bool HookLoaded => _hooked;

    /// <summary>
    /// True when the overlay hook was loaded in the browser process and that process died by the access violation. A process that is
    /// still running when the failure is seen may be in the middle of writing its crash report, so this waits, bounded, for that specific
    /// exit; a process that is still running after the wait, or that ended differently, is not this fault.
    /// </summary>
    internal async Task<bool> ConfirmLostToHookAsync()
    {
        if (!_hooked) return false;
        try
        {
            var browser = _browser;
            if (browser == null) return _violated;
            if (!browser.HasExited)
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await browser.WaitForExitAsync(limit.Token);
            }
            return _violated || browser.ExitCode == AccessViolation;
        }
        catch (OperationCanceledException) { return _violated; } // still running after the wait
        catch (Exception) { return false; } // handle not readable: no exemption
    }

    /// <summary>Attaches now when the control already has its browser, otherwise as soon as the control reports it.</summary>
    internal void AttachWhenReady(WebView2 web)
    {
        if (web.CoreWebView2 != null) { Attach(web.CoreWebView2); return; }
        void Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
        {
            sender.CoreWebView2Initialized -= Initialized;
            if (args.Exception == null && sender.CoreWebView2 != null) Attach(sender.CoreWebView2);
        }
        web.CoreWebView2Initialized += Initialized;
    }

    internal void Attach(CoreWebView2 core)
    {
        try
        {
            _browser?.Dispose();
            _browser = Process.GetProcessById((int)core.BrowserProcessId);
            _hooked = _browser.Modules.Cast<ProcessModule>()
                .Any(module => module.ModuleName.StartsWith("RTSSHooks", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception) { _hooked = false; } // process or modules not readable: no exemption
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited &&
                e.Reason == CoreWebView2ProcessFailedReason.Unexpected && e.ExitCode == AccessViolation)
                _violated = true;
        };
    }

    public void Dispose() { _browser?.Dispose(); _browser = null; }

    /// <summary>
    /// Runs <paramref name="start"/> (a part of a fixture that opens a real web view and takes everything it needs from scratch) and starts it again,
    /// at most <see cref="MaxStarts"/> times in all, only when it failed and the failure was this machine's overlay-hook crash of the browser process.
    /// The part receives the watch to attach to its web view and the number of the start (1, 2, 3).
    /// Everything else, including a third loss, is thrown unchanged. A restart is logged as a NOTE.
    /// </summary>
    internal static async Task RunAsync(string what, Func<WebViewBrowserLossWatch, int, Task> start)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var watch = new WebViewBrowserLossWatch();
            try
            {
                await start(watch, attempt);
                return;
            }
            catch (Exception) when (attempt < MaxStarts && watch.HookLoaded)
            {
                // Without the hook, or when the process is still running or ended any other way, this is not the machine's fault: rethrow.
                if (!await watch.ConfirmLostToHookAsync()) throw;
                Program.Log("NOTE: " + what + ": the web view's browser process was lost to the overlay hook (RTSSHooks, access violation 0xC0000005; start " +
                    attempt + " of " + MaxStarts + "); starting this part again on a fresh web view.");
            }
        }
    }
}
