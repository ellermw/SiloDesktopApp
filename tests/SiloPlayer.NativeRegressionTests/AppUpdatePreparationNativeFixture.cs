using System.Reflection;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;
using SiloPlayer.Services;

internal static partial class AppUpdateNativeFixture
{
    private sealed class DelayedWindowedInstaller : IWindowedAppUpdateInstaller
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Transferred = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Transfer;
        public long HelperHwnd;
        public Action? BeforeSwap, AfterSwap;
        // Retain the original signature so the same regression fixture can run against frozen Beta bytes.
        public Task<WindowedUpdateOutcome> PrepareWithDownloadAsync(AppRelease release, string packageDirectory, bool restart, bool confirmRestart, long ownerWindow,
            Func<Action<IncrementalUpdateProgress>, CancellationToken, Task<WindowedUpdateDownload>> download, CancellationToken ct)
            => PrepareWithDownloadAsync(release, packageDirectory, restart, confirmRestart, ownerWindow, download, ct, null);

        public async Task<WindowedUpdateOutcome> PrepareWithDownloadAsync(AppRelease release, string packageDirectory, bool restart, bool confirmRestart, long ownerWindow,
            Func<Action<IncrementalUpdateProgress>, CancellationToken, Task<WindowedUpdateDownload>> download, CancellationToken ct, Action<long>? presentTrustedHelper = null)
        {
            Entered.TrySetResult();
            try { await Release.Task.WaitAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return WindowedUpdateOutcome.NotStarted; }
            if (Transfer)
            {
                var pipeName="silo-test-"+Guid.NewGuid().ToString("N");
                using var pipe=UpdatePipeIdentity.CreateServer(pipeName);
                var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true};
                start.Environment["SILO_NATIVE_OTHER_WINDOW"]="panel";
                var peerResults=Path.Combine(Program.ResultDirectory,"presentation-peer"); Directory.CreateDirectory(peerResults);
                start.Environment["SILO_NATIVE_TEST_RESULT_DIRECTORY"]=peerResults;
                start.Environment["SILO_NATIVE_PRESENTATION_PIPE"]=pipeName;
                start.Environment["SILO_NATIVE_PRESENTATION_OWNER"]=ownerWindow.ToString(System.Globalization.CultureInfo.InvariantCulture);
                using var peer=System.Diagnostics.Process.Start(start)!;
                try
                {
                    await pipe.WaitForConnectionAsync(ct);
                    UpdatePipeIdentity.Authenticate(pipe,true,peer.Id,peer.StartTime.ToUniversalTime().Ticks,Environment.ProcessPath!);
                    await PresentationAcceptAsync(pipe,handle=>
                    {
                        var validate=typeof(WindowsAppUpdateInstaller).GetMethod("ValidatePresentationWindow",BindingFlags.Static|BindingFlags.NonPublic)!;
                        try {validate.Invoke(null,[ownerWindow,peer.Id]);throw new InvalidOperationException("A foreign-process HWND passed the production helper check.");}
                        catch(TargetInvocationException ex) when(ex.InnerException is InvalidDataException) { }
                        validate.Invoke(null,[handle,peer.Id]);
                        HelperHwnd=handle; BeforeSwap?.Invoke(); presentTrustedHelper!(handle); AfterSwap?.Invoke();
                    },ct);
                    await UpdateHostProtocol.WriteAsync(pipe,new("staged"),ct);
                    if((await UpdateHostProtocol.ReadAsync(pipe,ct)).Kind!="visible")throw new InvalidOperationException("No staged presentation acknowledgement.");
                    Transferred.TrySetResult();
                    await Finish.Task.WaitAsync(ct);
                    await UpdateHostProtocol.WriteAsync(pipe,new("cancel"),ct);
                    await peer.WaitForExitAsync(ct);
                    if(peer.ExitCode!=0)throw new InvalidOperationException("The presentation peer failed; see its isolated receipt.");
                }
                finally {if(!peer.HasExited)peer.Kill();}
                return WindowedUpdateOutcome.Deferred;
            }
            return WindowedUpdateOutcome.NotStarted;
        }
    }

    private static async Task CheckPreparationAsync(MainWindow owner, AppUpdateService updates, string scenario)
    {
        var installer = new DelayedWindowedInstaller {Transfer=scenario=="preparation-transfer"};
        using var stop = new CancellationTokenSource();
        var release = new AppRelease(new Version(1, 2, 999), "v1.2.999", "", DateTimeOffset.UtcNow,
            new Uri("https://fixture.invalid/unused"), new string('a', 64), 1)
        {Incremental=new(new Uri("https://fixture.invalid/manifest"),new string('b',64),1,new Uri("https://fixture.invalid/pack"),new string('c',64),1)};
        var run = (Task)typeof(MainWindow).GetMethod("RunWindowedUpdateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(owner, [null, installer, release, true, stop.Token])!;
        try
        {
            await installer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Preparation is deliberately blocked. The real MainWindow must already have shown its safe view.
            var panel = typeof(MainWindow).GetField("_updatePreparationWindow", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner) as Window;
            if (panel is null || !IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(panel)))
                throw new InvalidOperationException("Update now left a blank corner while deterministic preparation was blocked.");
            var type = panel.GetType();
            var status = (TextBlock)type.GetProperty("Status")!.GetValue(panel)!;
            var bar = (ProgressBar)type.GetProperty("Progress")!.GetValue(panel)!;
            await Task.Delay(350);
            if (status.Text != "Preparing…" || !bar.IsIndeterminate || bar.ActualHeight <= 0)
                throw new InvalidOperationException("Early feedback must render Preparing… with an indeterminate native progress bar.");
            Program.Log("PASS: actual MainWindow shows Preparing… and animated indeterminate progress while deterministic preparation remains blocked.");
            if(installer.Transfer)
            {
                var early=WinRT.Interop.WindowNative.GetWindowHandle(panel);
                var foreground=GetForegroundWindow();
                installer.BeforeSwap=()=>
                {
                    if(!IsWindowVisible(early) || IsWindowVisible((IntPtr)installer.HelperHwnd))
                        throw new InvalidOperationException("The helper showed before the safe transfer.");
                };
                installer.AfterSwap=()=>
                {
                    if(IsWindowVisible(early) || !IsWindowVisible((IntPtr)installer.HelperHwnd))
                        throw new InvalidOperationException("Atomic transfer left duplicate panels or a blank corner.");
                    if(GetForegroundWindow()!=foreground)throw new InvalidOperationException("Presentation transfer stole focus.");
                };
                installer.Release.TrySetResult();
                await installer.Transferred.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await Task.Delay(400);
                if(IsWindowVisible(early) || !IsWindowVisible((IntPtr)installer.HelperHwnd))throw new InvalidOperationException("A timer resurrected the old panel or hid its replacement.");
                var helper=(IntPtr)installer.HelperHwnd;
                owner.Activate(); await Task.Delay(400);
                if(!IsWindowVisible(helper) || GetWindowLongPtrW(helper,-8)!=IntPtr.Zero)throw new InvalidOperationException("Staged failure panel must remain visible without native Silo ownership.");
                GetWindowRect(helper,out var stagedBounds);
                var stagedPoint=new NativePoint {X=stagedBounds.Left+30,Y=stagedBounds.Top+30};
                if(GetAncestor(WindowFromPoint(stagedPoint),2)!=helper)throw new InvalidOperationException("Reactivating Silo covered its staged failure panel.");
                var presenter=(Microsoft.UI.Windowing.OverlappedPresenter)owner.AppWindow.Presenter;
                presenter.Minimize(); await Task.Delay(350);
                if(IsWindowVisible(helper))throw new InvalidOperationException("Cross-process staged failure panel remained visible while Silo minimized.");
                presenter.Restore(); owner.Activate(); await Task.Delay(450);
                if(!IsWindowVisible(helper))throw new InvalidOperationException("Cross-process staged failure panel did not return with Silo.");
                Program.Log("PASS: production HWND validation rejects a foreign PID; separate-process staged failure remains unowned, above reactivated Silo and follows minimize/restore.");
                installer.Finish.TrySetResult(); await run;
                Program.Log("PASS: authenticated separate-process shared helper view atomically replaces preparation without duplicate/ghost HWNDs, blank corner or focus stealing.");
            }
            else if(scenario=="preparation-cancel")
            {
                Click((Button)type.GetProperty("Cancel")!.GetValue(panel)!);
                await run.WaitAsync(TimeSpan.FromSeconds(5));
                Program.Log("PASS: real preparation Cancel cancels the delayed operation and removes its panel.");
            }
            else if(scenario=="preparation-failure")
            {
                installer.Release.TrySetResult(); await run.WaitAsync(TimeSpan.FromSeconds(5));
                Program.Log("PASS: failed/not-started preparation removes its early panel and returns to Silo.");
            }
        }
        finally { stop.Cancel(); installer.Release.TrySetResult(); installer.Finish.TrySetResult(); await run; }
        if (typeof(MainWindow).GetField("_updatePreparationWindow", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner) is not null)
            throw new InvalidOperationException("Canceled preparation left a ghost panel.");
    }

    private static async Task CheckPanelFocusAsync(MainWindow owner)
    {
        owner.AppWindow.Move(new(80, 70)); owner.AppWindow.ResizeClient(new(900, 720));
        var type = Assembly.LoadFrom(System.IO.Path.Combine(Program.AppDirectory, "Updater", "SiloPlayer.UpdateHost.dll"))
            .GetType("SiloPlayer.UpdateHost.UpdateProgressWindow", true)!;
        var ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        var panel = (Window)Activator.CreateInstance(type, true, ownerHwnd.ToInt64())!;
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.Environment["SILO_NATIVE_OTHER_WINDOW"] = "1";
        using var other = System.Diagnostics.Process.Start(start)!;
        IntPtr otherHwnd = IntPtr.Zero;
        await Until(() => { other.Refresh(); otherHwnd = other.MainWindowHandle; return otherHwnd != IntPtr.Zero; });
        var panelHwnd = WinRT.Interop.WindowNative.GetWindowHandle(panel);
        try
        {
            SetWindowPos(otherHwnd, IntPtr.Zero, 100, 100, 240, 200, 0x0040);
            FocusFixtureWindow(otherHwnd);
            await Task.Delay(650);
            if (GetForegroundWindow() != otherHwnd) throw new InvalidOperationException($"Fixture did not establish another foreground HWND: expected={otherHwnd}, actual={GetForegroundWindow()}, visible={IsWindowVisible(otherHwnd)}.");
            if (!IsWindowVisible(panelHwnd)) throw new InvalidOperationException("Corner panel disappeared when a non-overlapping other window took focus.");
            if ((GetWindowLongPtrW(panelHwnd, -20).ToInt64() & 8) != 0) throw new InvalidOperationException("Corner panel is globally topmost.");
            if(GetWindowLongPtrW(panelHwnd,-8)!=IntPtr.Zero)throw new InvalidOperationException("Panel lifetime is tied to Silo's native HWND.");
            GetWindowRect(panelHwnd, out var rect);
            SetWindowPos(otherHwnd, IntPtr.Zero, rect.Left - 20, rect.Top - 20, rect.Right - rect.Left + 40, rect.Bottom - rect.Top + 40, 0x0040);
            FocusFixtureWindow(otherHwnd); await Task.Delay(650);
            var point = new NativePoint { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
            if (GetAncestor(WindowFromPoint(point),2) != otherHwnd) throw new InvalidOperationException("An overlapping normal window did not cover the update panel.");
            owner.Activate(); FocusFixtureWindow(ownerHwnd); await Task.Delay(500);
            if (GetAncestor(WindowFromPoint(point),2) != panelHwnd) throw new InvalidOperationException("Panel is not above Silo content on reactivation.");
            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)owner.AppWindow.Presenter;
            presenter.Minimize(); await Task.Delay(500);
            if (IsWindowVisible(panelHwnd)) throw new InvalidOperationException("Minimizing Silo did not hide its update panel.");
            presenter.Restore(); owner.Activate(); await Task.Delay(650);
            if (!IsWindowVisible(panelHwnd)) throw new InvalidOperationException("Restoring Silo did not restore its panel.");
            var ask = (Task<bool>)type.GetMethod("AskRestartAsync")!.Invoke(panel, [CancellationToken.None])!;
            SetWindowPos(otherHwnd, IntPtr.Zero, 100, 100, 240, 200, 0x0040); FocusFixtureWindow(otherHwnd); await Task.Delay(500);
            if (!IsWindowVisible(panelHwnd) || ask.IsCompleted) throw new InvalidOperationException("Ready choice disappeared or answered itself on focus loss.");
            Click((Button)type.GetProperty("Later")!.GetValue(panel)!);
            if (await ask) throw new InvalidOperationException("Later changed into Restart.");
            Program.Log("PASS: actual HWND focus loss retains panel; normal overlapping HWND covers it; Silo reactivation, minimize/restore and ready Later preserve visibility/choice without topmost.");
            panel.Close();
            panel=(Window)Activator.CreateInstance(type,true,ownerHwnd.ToInt64())!;
            panelHwnd=WinRT.Interop.WindowNative.GetWindowHandle(panel);
            var restart=(Task<bool>)type.GetMethod("AskRestartAsync")!.Invoke(panel,[CancellationToken.None])!;
            FocusFixtureWindow(otherHwnd); await Task.Delay(500);
            if(!IsWindowVisible(panelHwnd) || restart.IsCompleted)throw new InvalidOperationException("Ready Restart choice disappeared on focus loss.");
            Click((Button)type.GetProperty("RestartNow")!.GetValue(panel)!);
            if(!await restart || !((ProgressBar)type.GetProperty("Progress")!.GetValue(panel)!).IsIndeterminate)
                throw new InvalidOperationException("Restart now did not continue indeterminate preparation.");
            Program.Log("PASS: ready Restart now remains until explicitly selected and resumes preparation without running installation or restart.");

            // Closing Silo's HWND must not close/cancel the helper before it can forward
            // the staged close frame and paint its final status. Use a disposable owner
            // so the fixture's real MainWindow can continue collecting results.
            panel.Close();
            var closingOwner=new Window {Content=new Grid()};
            var ownerClosed=false;
            closingOwner.Closed+=(_,_)=>ownerClosed=true;
            closingOwner.Activate();
            panel=(Window)Activator.CreateInstance(type,true,WinRT.Interop.WindowNative.GetWindowHandle(closingOwner).ToInt64())!;
            panelHwnd=WinRT.Interop.WindowNative.GetWindowHandle(panel);
            var panelClosed=false;
            panel.Closed+=(_,_)=>panelClosed=true;
            try
            {
                await Task.Delay(350);
                if(!IsWindowVisible(panelHwnd))throw new InvalidOperationException("The staged panel was not visible before its owner closed.");
                closingOwner.Close();
                await Task.Delay(500);
                if(panelClosed || !IsWindowVisible(panelHwnd))throw new InvalidOperationException("Closing Silo destroyed or hid its independent update panel.");
                type.GetMethod("ShowFinishing")!.Invoke(panel,["Finishing and restarting Silo…"]);
                await Task.Delay(350);
                if(panelClosed || !IsWindowVisible(panelHwnd) || ((TextBlock)type.GetProperty("Status")!.GetValue(panel)!).Text!="Finishing and restarting Silo…")
                    throw new InvalidOperationException("The surviving helper could not present its finishing status.");
                Program.Log("PASS: closing the tracked Silo HWND leaves the actual helper panel alive and able to show finishing status.");
            }
            finally {if(!ownerClosed)closingOwner.Close();}

        }
        finally { if (!other.HasExited) other.Kill(); panel.Close(); }
    }

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint source,uint target,bool attach);

    private static void FocusFixtureWindow(IntPtr hwnd)
    {
        var current=GetCurrentThreadId();
        var foreground=GetWindowThreadProcessId(GetForegroundWindow(),out _);
        var target=GetWindowThreadProcessId(hwnd,out _);
        var attachedForeground=foreground!=0 && foreground!=current && AttachThreadInput(current,foreground,true);
        var attachedTarget=target!=0 && target!=current && target!=foreground && AttachThreadInput(current,target,true);
        try {SetForegroundWindow(hwnd);}
        finally
        {
            if(attachedTarget)AttachThreadInput(current,target,false);
            if(attachedForeground)AttachThreadInput(current,foreground,false);
        }
    }

    private static Task PresentationAcceptAsync(Stream pipe,Action<long> swap,CancellationToken ct)=>(Task)typeof(App).Assembly
        .GetType("SiloPlayer.Core.Services.Updates.UpdatePresentationHandoff",true)!.GetMethod("AcceptAsync")!.Invoke(null,[pipe,swap,ct])!;

    internal static async Task RunPresentationPeerAsync()
    {
        Application.Current.Resources.MergedDictionaries.Add(new XamlControlsResources());
        var type=Assembly.LoadFrom(System.IO.Path.Combine(Program.AppDirectory,"Updater","SiloPlayer.UpdateHost.dll")).GetType("SiloPlayer.UpdateHost.UpdateProgressWindow",true)!;
        var owner=long.Parse(Environment.GetEnvironmentVariable("SILO_NATIVE_PRESENTATION_OWNER")!,System.Globalization.CultureInfo.InvariantCulture);
        var panel=(Window)Activator.CreateInstance(type,true,owner,true)!;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var pipe=new NamedPipeClientStream(".",Environment.GetEnvironmentVariable("SILO_NATIVE_PRESENTATION_PIPE")!,PipeDirection.InOut,PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeout.Token);
            Action start=()=>type.GetMethod("StartTracking")!.Invoke(panel,null);
            await (Task)typeof(App).Assembly.GetType("SiloPlayer.Core.Services.Updates.UpdatePresentationHandoff",true)!.GetMethod("OfferAsync")!
                .Invoke(null,[pipe,WinRT.Interop.WindowNative.GetWindowHandle(panel).ToInt64(),start,timeout.Token])!;
            if((await UpdateHostProtocol.ReadAsync(pipe,timeout.Token)).Kind!="staged")throw new InvalidOperationException("Unexpected synthetic stage command.");
            var apply=type.GetMethod("Apply")!;
            // The published view resolves its own Core assembly; create its exact wire type.
            var staged=System.Text.Json.JsonSerializer.Deserialize("{\"Kind\":\"staged\"}",apply.GetParameters()[0].ParameterType);
            apply.Invoke(panel,[staged]);
            type.GetMethod("ShowFailure")!.Invoke(panel,["Silo could not save playback. Close this panel and retry."]);
            await UpdateHostProtocol.WriteAsync(pipe,new("visible"),timeout.Token);
            if((await UpdateHostProtocol.ReadAsync(pipe,timeout.Token)).Kind!="cancel")throw new InvalidOperationException("Unexpected synthetic peer command.");
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(Program.ResultDirectory,"peer-error.txt"),ex.ToString()); throw; }
        finally {panel.Close();}
    }
}
