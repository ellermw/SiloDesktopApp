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
    private Button cancel=null!;
    private bool closeAllowed, committed;
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        view=new UpdateProgressWindow(options.Restart,options.Owner);window=view;
        cancel=view.Cancel;
        cancel.Click+=(_,_)=>{if(closeAllowed)window.Close();else if(!committed)lifetime.Cancel();};
        window.AppWindow.Closing+=(_,e)=>{if(!closeAllowed){e.Cancel=true;if(!committed)lifetime.Cancel();}};
        window.Closed+=(_,_)=>{lifetime.Cancel();Exit();};
        // The panel shows itself without taking focus, so scrolling or playback in Silo continues.
        _=RunAsync();
    }

    private async Task RunAsync()
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        // The client's own download has a 15-minute limit; installation keeps its 20 minutes after it.
        deadline.CancelAfter(TimeSpan.FromMinutes(options.AwaitDownload ? 40 : 20));var ct=deadline.Token;
        using var app=new NamedPipeClientStream(".",options.AppPipe,PipeDirection.InOut,PipeOptions.Asynchronous);
        NamedPipeServerStream? worker=null;
        Task<UpdateHostMessage>? appFrame=null;
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
            if(options.AwaitDownload)
            {
                // Show the client's download here, then request elevation only for a complete package.
                while(true)
                {
                    var frame=await UpdateHostProtocol.ReadAsync(app,ct);
                    if(frame.Kind=="download"){Render(frame);continue;}
                    if(frame.Kind=="downloaded")break;
                    if(frame.Kind=="cancel"){closeAllowed=true;window!.Close();return;}
                    throw new UpdateClientFailure(frame.Kind=="failed" ? frame.Text : null);
                }
                if(options.ConfirmRestart)
                {
                    // Waiting for an answer has no deadline: during playback the panel stays hidden
                    // until the user is back. Silo writes nothing until "close", so a completed read
                    // here means Silo closed or abandoned the update.
                    deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                    appFrame=UpdateHostProtocol.ReadAsync(app,ct);
                    var choice=view!.AskRestartAsync(ct);
                    if(await Task.WhenAny(choice,appFrame)==appFrame){_=appFrame.Exception;closeAllowed=true;window!.Close();return;}
                    if(!await choice)
                    {
                        await UpdateHostProtocol.WriteAsync(app,new("later"),ct);
                        // Silo keeps the download and finishes the update on its next start. If it
                        // could not record Later it says so, and the panel shows that instead of closing.
                        closeAllowed=true;
                        await Task.WhenAny(appFrame,Task.Delay(TimeSpan.FromSeconds(10),lifetime.Token));
                        if(appFrame.IsCompletedSuccessfully && appFrame.Result.Kind=="failed"){view!.ShowFailure(appFrame.Result.Text??"Silo could not save Later. Update again from Silo when ready.");return;}
                        _=appFrame.Exception;window!.Close();return;
                    }
                    await UpdateHostProtocol.WriteAsync(app,new("restart"),ct);
                }
                deadline.CancelAfter(TimeSpan.FromMinutes(20));
                view!.Apply(new("progress",Text:"Preparing your update…"));
            }
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
            var close=await (appFrame??UpdateHostProtocol.ReadAsync(app,ct));
            if(close.Kind!="close" || close.TransactionId!=transaction)throw new OperationCanceledException();
            committed=true;view!.ShowFinishing(options.Restart ? "Finishing and restarting Silo…" : "Finishing the update…");
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
            view?.ShowFailure(committed ? "Silo has not confirmed startup yet. Your previous version is preserved; launch Silo again to recover if the new version exited. No running app will be closed."
                : ex is UpdateClientFailure{Message.Length:>0} client ? client.Message
                : "The update was canceled or could not finish. You can continue using Silo and try again.");
            closeAllowed=true;
        }
        finally{worker?.Dispose();_=appFrame?.Exception;}
    }
    private void Render(UpdateHostMessage message)
    {
        view!.Apply(message);
    }
    // The client reported that its download failed; show its text instead of the generic failure.
    private sealed class UpdateClientFailure(string? text) : IOException(text ?? "");
}
