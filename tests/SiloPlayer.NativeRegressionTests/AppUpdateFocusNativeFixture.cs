using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using Microsoft.UI.Xaml;
using SiloPlayer;
using SiloPlayer.Core.Services.Updates;

internal static partial class AppUpdateNativeFixture
{
    // A settled-after-500ms check misses the owner covering the panel during
    // activation. Probe immediately, before the helper's polling/event repair.
    private static async Task CheckFocusReturnAsync(MainWindow owner)
    {
        owner.AppWindow.Move(new(80,70)); owner.AppWindow.ResizeClient(new(900,720));
        var ownerHwnd=WinRT.Interop.WindowNative.GetWindowHandle(owner);
        var type=Assembly.LoadFrom(Path.Combine(Program.AppDirectory,"Updater","SiloPlayer.UpdateHost.dll"))
            .GetType("SiloPlayer.UpdateHost.UpdateProgressWindow",true)!;
        var preparation=(Window)Activator.CreateInstance(type,true,ownerHwnd.ToInt64())!;
        var pipeName="silo-test-"+Guid.NewGuid().ToString("N");
        using var pipe=UpdatePipeIdentity.CreateServer(pipeName);
        using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var peerStart=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true};
        peerStart.Environment["SILO_NATIVE_OTHER_WINDOW"]="panel";
        peerStart.Environment["SILO_NATIVE_PRESENTATION_PIPE"]=pipeName;
        peerStart.Environment["SILO_NATIVE_PRESENTATION_OWNER"]=ownerHwnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var peerResults=Path.Combine(Program.ResultDirectory,"focus-peer");Directory.CreateDirectory(peerResults);
        peerStart.Environment["SILO_NATIVE_TEST_RESULT_DIRECTORY"]=peerResults;
        using var peer=Process.Start(peerStart)!;
        var otherStart=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true};
        otherStart.Environment["SILO_NATIVE_OTHER_WINDOW"]="1";
        using var other=Process.Start(otherStart)!;
        try
        {
            await pipe.WaitForConnectionAsync(limit.Token);
            UpdatePipeIdentity.Authenticate(pipe,true,peer.Id,peer.StartTime.ToUniversalTime().Ticks,Environment.ProcessPath!);
            var helper=IntPtr.Zero;
            await PresentationAcceptAsync(pipe,handle=>{helper=(IntPtr)handle;type.GetMethod("TransferTo")!.Invoke(preparation,[handle]);},limit.Token);
            preparation.Close();
            await Until(()=>{other.Refresh();return other.MainWindowHandle!=IntPtr.Zero;});
            await Task.Delay(350);
            GetWindowRect(helper,out var rect);
            var point=new NativePoint {X=(rect.Left+rect.Right)/2,Y=(rect.Top+rect.Bottom)/2};
            SetWindowPos(other.MainWindowHandle,IntPtr.Zero,rect.Left-20,rect.Top-20,rect.Right-rect.Left+40,rect.Bottom-rect.Top+40,0x0040);
            var covered=0;var settled=0;
            for(var i=0;i<12;i++)
            {
                FocusFixtureWindow(other.MainWindowHandle);await Task.Delay(90);
                if(GetAncestor(WindowFromPoint(point),2)!=other.MainWindowHandle)
                    throw new InvalidOperationException("A normal external fixture window could not cover the panel.");
                FocusFixtureWindow(ownerHwnd);
                if(GetForegroundWindow()!=ownerHwnd)throw new InvalidOperationException("Fixture could not reactivate its own Silo window.");
                if(GetAncestor(WindowFromPoint(point),2)!=helper)covered++;
                await Task.Delay(140);
                if(GetAncestor(WindowFromPoint(point),2)!=helper)settled++;
                if(!IsWindowVisible(helper) || (GetWindowLongPtrW(helper,-20).ToInt64()&8)!=0)
                    throw new InvalidOperationException("Focus return hid the panel or made it globally topmost.");
            }
            Program.Log($"Focus return: immediate covered samples={covered}/12; settled covered samples={settled}/12; helper PID={peer.Id}; native owner={GetWindowLongPtrW(helper,-8)}.");
            await UpdateHostProtocol.WriteAsync(pipe,new("staged"),limit.Token);
            if((await UpdateHostProtocol.ReadAsync(pipe,limit.Token)).Kind!="visible")throw new InvalidOperationException("Focus peer did not acknowledge its final state.");
            await UpdateHostProtocol.WriteAsync(pipe,new("cancel"),limit.Token);
            await peer.WaitForExitAsync(limit.Token);
            if(peer.ExitCode!=0)throw new InvalidOperationException("Focus peer failed.");
            if(covered!=0 || settled!=0)throw new InvalidOperationException("Silo momentarily covers its helper on activation, producing a visible hide/reappear flash.");
            Program.Log("PASS: twelve cross-process external-app/Silo focus returns keep the panel continuously in front of Silo, without global topmost or delayed z-order repair.");
        }
        finally
        {
            if(!peer.HasExited)peer.Kill();if(!other.HasExited)other.Kill();
            try{preparation.Close();}catch{}
        }
    }
}
