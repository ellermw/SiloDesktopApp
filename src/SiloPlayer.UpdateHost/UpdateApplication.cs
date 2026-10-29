using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
using SiloPlayer.Core.Services.Updates;
using Windows.Graphics;

namespace SiloPlayer.UpdateHost;

internal sealed class UpdateApplication : Application, IXamlMetadataProvider
{
    private readonly Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider metadata=new();
    public IXamlType GetXamlType(Type type)=>metadata.GetXamlType(type);
    public IXamlType GetXamlType(string name)=>metadata.GetXamlType(name);
    public XmlnsDefinition[] GetXmlnsDefinitions()=>metadata.GetXmlnsDefinitions();
    private readonly HostOptions options;
    internal UpdateApplication(HostOptions options)
    {
        this.options=options;
        UnhandledException+=(_,e)=>{Console.Error.WriteLine(e.Exception);e.Handled=true;Environment.ExitCode=1;Exit();};
    }
    private Window? window;
    private UpdateProgressWindow? view;
    private readonly CancellationTokenSource lifetime=new();
    private TextBlock status=null!;
    private ProgressBar progress=null!;
    private Button cancel=null!;
    private bool closeAllowed, committed;
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        view=new UpdateProgressWindow(options.Restart,options.Owner);window=view;
        status=view.Status;progress=view.Progress;cancel=view.Cancel;
        cancel.Click+=(_,_)=>{if(closeAllowed)window.Close();else if(!committed)lifetime.Cancel();};
        window.AppWindow.Closing+=(_,e)=>{if(!closeAllowed){e.Cancel=true;if(!committed)lifetime.Cancel();}};
        window.Closed+=(_,_)=>{lifetime.Cancel();Exit();};
        window.Activate();
        _=RunAsync();
    }

    private async Task RunAsync()
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(20));var ct=deadline.Token;
        using var app=new NamedPipeClientStream(".",options.AppPipe,PipeDirection.InOut,PipeOptions.Asynchronous);
        NamedPipeServerStream? worker=null;
        try
        {
            var hostImage=Environment.ProcessPath!;
            var root=UpdateInstallation.FindRegisteredRoot(hostImage)??throw new InvalidOperationException("Use the registered Silo installation to update.");
            var versionDirectory=Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
            var oldImage=Path.Combine(versionDirectory,"SiloPlayer.exe");
            await Task.Run(()=>UpdateInstallation.RequireProtectedMachineInstallation(root,versionDirectory),ct);
            using var mutationMutex=UpdatePipeIdentity.CreateMutationMutex(root);
            await app.ConnectAsync(ct);
            UpdatePipeIdentity.Authenticate(app,false,options.ParentPid,options.ParentStart,oldImage);
            await UpdateHostProtocol.WriteAsync(app,new("visible"),ct);
            worker=UpdatePipeIdentity.CreateServer(options.WorkerPipe,allowElevation:true);
            using var current=Process.GetCurrentProcess();
            var start=new ProcessStartInfo(hostImage){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(hostImage)!};
            foreach(var arg in options.Arguments(true,current.Id,current.StartTime.ToUniversalTime().Ticks))start.ArgumentList.Add(arg);
            using var elevated=Process.Start(start)??throw new IOException("The update helper could not start.");
            await worker.WaitForConnectionAsync(ct);
            UpdatePipeIdentity.Authenticate(worker,true,elevated.Id,UpdatePipeIdentity.InspectProcess(elevated.Id).StartUtcTicks,hostImage);
            await UpdateHostProtocol.WriteAsync(worker,new("visible"),ct);
            UpdateHostMessage message;
            do
            {
                message=await UpdateHostProtocol.ReadAsync(worker,ct);
                Render(message);
                await UpdateHostProtocol.WriteAsync(app,message,ct);
                if(message.Kind=="failed")throw new IOException(message.Text);
            }while(message.Kind!="staged");
            var transaction=message.TransactionId!;
            var close=await UpdateHostProtocol.ReadAsync(app,ct);
            if(close.Kind!="close" || close.TransactionId!=transaction)throw new OperationCanceledException();
            committed=true; cancel.IsEnabled=false;status.Text="Finishing and restarting Silo…";
            await UpdateHostProtocol.WriteAsync(worker,close,ct);
            message=await UpdateHostProtocol.ReadAsync(worker,ct);Render(message);
            if(message.Kind!="activated")throw new IOException(message.Text??"Activation did not finish.");
            if(!options.Restart){closeAllowed=true;window!.Close();return;}
            var readyName="SiloUpdate-"+Guid.NewGuid().ToString("N");
            using var ready=UpdatePipeIdentity.CreateServer(readyName);
            var launch=new ProcessStartInfo(Path.Combine(root,"SiloPlayer.exe")){UseShellExecute=false};
            launch.ArgumentList.Add("--silo-update-ready");launch.ArgumentList.Add(readyName);
            using var launcher=Process.Start(launch)??throw new IOException("Silo could not restart.");
            // The stable launcher records its exact child identity; the pipe validates it independently.
            using var startup=CancellationTokenSource.CreateLinkedTokenSource(ct);startup.CancelAfter(TimeSpan.FromMinutes(2));
            await ready.WaitForConnectionAsync(startup.Token);
            var readyMessage=await UpdateHostProtocol.ReadAsync(ready,startup.Token);
            var pid=UpdatePipeIdentity.PeerProcess(ready,true);
            using var newClient=Process.GetProcessById(pid);
            var ticks=newClient.StartTime.ToUniversalTime().Ticks;
            var image=Path.Combine(root,"versions",message.Text!,"SiloPlayer.exe");
            UpdatePipeIdentity.Authenticate(ready,true,pid,ticks,image);
            if(readyMessage.Kind!="ready" || readyMessage.TransactionId!=transaction)throw new InvalidDataException("Unexpected startup confirmation.");
            await UpdateHostProtocol.WriteAsync(worker,new("ready",transaction,ProcessId:pid,StartUtcTicks:ticks),ct);
            message=await UpdateHostProtocol.ReadAsync(worker,ct);
            if(message.Kind!="confirmed")throw new IOException(message.Text??"Startup could not be confirmed.");
            await UpdateHostProtocol.WriteAsync(ready,message,ct);
            closeAllowed=true;window!.Close();
        }
        catch(Exception ex)
        {
            try{if(app.IsConnected)await UpdateHostProtocol.WriteAsync(app,new("failed",Text:ex is System.ComponentModel.Win32Exception{NativeErrorCode:1223}?"Administrator approval was canceled. Silo remains open.":"The update could not finish. Silo's previous version is preserved."),CancellationToken.None);}catch{}
            status.Text=committed ? "Silo has not confirmed startup yet. Your previous version is preserved; launch Silo again to recover if the new version exited. No running app will be closed." : "The update was canceled or could not finish. You can continue using Silo and try again.";
            progress.IsIndeterminate=false;progress.Value=0;closeAllowed=true;cancel.Content="Close";cancel.IsEnabled=true;
        }
        finally{worker?.Dispose();}
    }
    private void Render(UpdateHostMessage message)
    {
        view!.Apply(message);
    }
    internal static void CenterWindow(Window target,long owner)
    {
        if(IsIconic((IntPtr)owner))return;
        var dpi=GetDpiForWindow((IntPtr)owner);if(dpi==0)dpi=96;
        var width=(int)(500*dpi/96);var height=(int)(360*dpi/96);
        if(GetClientRect((IntPtr)owner,out var rect))
        {
            var origin=new Point{X=rect.Left,Y=rect.Top};if(!ClientToScreen((IntPtr)owner,ref origin))return;
            rect.Right+=origin.X;rect.Bottom+=origin.Y;rect.Left=origin.X;rect.Top=origin.Y;
            var point=UpdateWindowPlacement.Center(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,width,height);
            var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            if(GetMonitorInfo(MonitorFromWindow((IntPtr)owner,2),ref monitor))
            {
                width=Math.Min(width,monitor.Work.Right-monitor.Work.Left);height=Math.Min(height,monitor.Work.Bottom-monitor.Work.Top);
                point=(Math.Clamp(point.X,monitor.Work.Left,monitor.Work.Right-width),Math.Clamp(point.Y,monitor.Work.Top,monitor.Work.Bottom-height));
            }
            target.AppWindow.Resize(new SizeInt32(width,height));target.AppWindow.Move(new PointInt32(point.X,point.Y));
        }
    }
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr hwnd,ref Point point);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr hwnd);
}
