using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Services;

public sealed partial class WindowsAppUpdateInstaller : IWindowedAppUpdateInstaller
{
    private static readonly TimeSpan DownloadFrameInterval = TimeSpan.FromMilliseconds(250);
    private NamedPipeServerStream? stagedPipe;
    private string? stagedTransaction;
    private bool stagedCloseArmed;
    public bool HasPreparedHandoff=>stagedPipe?.IsConnected==true && stagedTransaction is not null;
    public DateTimeOffset? AuthorityCooldownUntil {get;private set;}

    public Task<bool> PrepareAsync(AppRelease release,PreparedUpdatePackage package,bool restart,long ownerWindow,CancellationToken ct)
        =>HandoffAsync(release,package.Directory,restart,ownerWindow,null,package,ct);

    public Task<bool> PrepareWithDownloadAsync(AppRelease release,string packageDirectory,bool restart,long ownerWindow,
        Func<Action<IncrementalUpdateProgress>,CancellationToken,Task<WindowedUpdateDownload>> download,CancellationToken ct)
        =>HandoffAsync(release,packageDirectory,restart,ownerWindow,download,null,ct);

    private async Task<bool> HandoffAsync(AppRelease release,string packageDirectory,bool restart,long ownerWindow,
        Func<Action<IncrementalUpdateProgress>,CancellationToken,Task<WindowedUpdateDownload>>? download,PreparedUpdatePackage? package,CancellationToken ct)
    {
        NamedPipeServerStream? pipe=null;
        AuthorityCooldownUntil=null;
        try
        {
            var root=UpdateInstallation.FindRegisteredRoot(Environment.ProcessPath!)??throw new InvalidOperationException("The installed application was not found.");
            var version=Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            await Task.Run(()=>UpdateInstallation.RequireProtectedMachineInstallation(root,AppContext.BaseDirectory),ct);
            var host=await Task.Run(()=>SideBySideInstallation.VerifyInstalledClientFile(root,version,"Updater/SiloPlayer.UpdateHost.exe"),ct);
            var pipeName="SiloUpdate-"+Guid.NewGuid().ToString("N");
            pipe=UpdatePipeIdentity.CreateServer(pipeName);
            var workerPipe="SiloUpdate-"+Guid.NewGuid().ToString("N");
            using var current=Process.GetCurrentProcess();
            var start=new ProcessStartInfo(host){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(host)!};
            foreach(var arg in new[]{"--worker","no","--app-pipe",pipeName,"--worker-pipe",workerPipe,"--parent",current.Id.ToString(CultureInfo.InvariantCulture),
                "--parent-start",current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),"--package",packageDirectory,"--tag",release.Tag,
                "--restart",restart?"yes":"no","--owner",ownerWindow.ToString(CultureInfo.InvariantCulture),"--await-download",download is null?"no":"yes"})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new IOException("The update window could not start.");
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
            // The download has its own 15-minute limit before the usual 20 minutes for installation.
            deadline.CancelAfter(TimeSpan.FromMinutes(download is null ? 20 : 40));
            var hostStart=process.StartTime.ToUniversalTime().Ticks;
            using(var connectionLifetime=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
            {
                var connection=pipe.WaitForConnectionAsync(connectionLifetime.Token);
                var exited=process.WaitForExitAsync(connectionLifetime.Token);
                if(await Task.WhenAny(connection,exited)==exited && !connection.IsCompletedSuccessfully)
                {
                    connectionLifetime.Cancel();
                    try { await connection; } catch(OperationCanceledException) { }
                    deadline.Token.ThrowIfCancellationRequested();
                    LocalLog.AppendLine("updates.txt","staged_handoff_host_exited code="+process.ExitCode);
                    throw new IOException("The update window closed before connecting. Open update details to retry.");
                }
                await connection;
                connectionLifetime.Cancel();
                try { await exited; } catch(OperationCanceledException) { }
            }
            UpdatePipeIdentity.Authenticate(pipe,true,process.Id,hostStart,host);
            var visible=false;
            Task<UpdateHostMessage>? pending=null;
            if(download is not null)
            {
                var first=await UpdateHostProtocol.ReadAsync(pipe,deadline.Token);
                if(first.Kind=="failed"){AuthorityCooldownUntil=first.CooldownUntil;throw new IOException(first.Text);}
                if(first.Kind!="visible")throw new InvalidDataException("Invalid update handoff.");
                visible=true;
                (package,pending)=await DownloadIntoWindowAsync(pipe,download,deadline.Token);
                if(package is null)return false;
                if(!Path.GetFullPath(package.Directory).Equals(Path.GetFullPath(packageDirectory),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded package is not the one the update window installs.");
                await UpdateHostProtocol.WriteAsync(pipe,new("downloaded"),deadline.Token);
            }
            while(true)
            {
                var message=pending is not null ? await pending : await UpdateHostProtocol.ReadAsync(pipe,deadline.Token);
                pending=null;
                if(message.Kind=="visible")visible=true;
                else if(message.Kind=="failed"){AuthorityCooldownUntil=message.CooldownUntil;throw new IOException(message.Text);}
                else if(message.Kind=="staged")
                {
                    if(!visible || message.Text!=package!.Manifest.Version || !Guid.TryParseExact(message.TransactionId,"N",out _))throw new InvalidDataException("Invalid update handoff.");
                    stagedPipe?.Dispose();stagedPipe=pipe;pipe=null;stagedTransaction=message.TransactionId;stagedCloseArmed=false;return true;
                }
            }
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){return false;}
        catch(Exception ex){LocalLog.AppendLine("updates.txt","staged_handoff_failed kind="+ex.GetType().Name);return false;}
        finally{pipe?.Dispose();}
    }

    // Runs the client's download while the window shows it. The window only writes during this
    // phase to report a failure or cancellation, which stops the download. Returns the ready
    // package (or null after telling the window why) and the window's pending next frame.
    private static async Task<(PreparedUpdatePackage? Package,Task<UpdateHostMessage> Pending)> DownloadIntoWindowAsync(PipeStream pipe,
        Func<Action<IncrementalUpdateProgress>,CancellationToken,Task<WindowedUpdateDownload>> download,CancellationToken ct)
    {
        var hostFrame=UpdateHostProtocol.ReadAsync(pipe,ct);
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        IncrementalUpdateProgress? latest=null,sent=null;
        var downloading=download(progress=>Volatile.Write(ref latest,progress),stop.Token);
        while(!downloading.IsCompleted)
        {
            var finished=await Task.WhenAny(downloading,hostFrame,Task.Delay(DownloadFrameInterval,ct));
            if(finished==hostFrame)
            {
                stop.Cancel();
                try{await downloading;}catch(Exception ex){LocalLog.AppendLine("updates.txt","windowed_download_stopped kind="+ex.GetType().Name);}
                var frame=await hostFrame;
                throw new IOException(frame.Kind=="failed" ? frame.Text : "The update window stopped the download.");
            }
            ct.ThrowIfCancellationRequested();
            sent=await SendProgressAsync(pipe,Volatile.Read(ref latest),sent,ct);
        }
        var result=await downloading;
        await SendProgressAsync(pipe,Volatile.Read(ref latest),sent,ct);
        if(result.Package is null)
        {
            // Neither ready nor failed: another update path is needed; the window closes quietly.
            await UpdateHostProtocol.WriteAsync(pipe,result.Error is null ? new("cancel") : new("failed",Text:result.Error),ct);
            return (null,hostFrame);
        }
        return (result.Package,hostFrame);
    }

    private static async Task<IncrementalUpdateProgress?> SendProgressAsync(PipeStream pipe,IncrementalUpdateProgress? progress,IncrementalUpdateProgress? sent,CancellationToken ct)
    {
        if(progress is null || ReferenceEquals(progress,sent))return sent;
        await UpdateHostProtocol.WriteAsync(pipe,new("download",Completed:progress.CompletedBytes,Total:progress.TotalBytes,
            Files:progress.DownloadedFiles,FileCount:progress.DownloadFiles,FileBytes:progress.CurrentFileBytes,FileTotal:progress.CurrentFileTotal),ct);
        return progress;
    }

    private void ConfirmStagedClose()
    {
        var pipe=Interlocked.Exchange(ref stagedPipe,null);
        if(pipe is null)return;
        try
        {
            using(pipe)
            using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                if(stagedCloseArmed && pipe.IsConnected)UpdateHostProtocol.WriteAsync(pipe,new("close",stagedTransaction),deadline.Token).GetAwaiter().GetResult();
        }
        catch(Exception ex){LocalLog.AppendLine("updates.txt","staged_close_confirmation_failed kind="+ex.GetType().Name);}
        finally{CancelPreparedHandoff();}
    }
    public void ArmNormalClose(){if(!HasPreparedHandoff)throw new InvalidOperationException("The update handoff is unavailable.");stagedCloseArmed=true;}
    public void CancelPreparedHandoff(){Interlocked.Exchange(ref stagedPipe,null)?.Dispose();stagedTransaction=null;stagedCloseArmed=false;}
}
