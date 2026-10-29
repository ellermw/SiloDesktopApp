using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Services;

public sealed partial class WindowsAppUpdateInstaller
{
    private NamedPipeServerStream? stagedPipe;
    private string? stagedTransaction;
    private bool stagedCloseArmed;
    public bool HasPreparedHandoff=>stagedPipe?.IsConnected==true && stagedTransaction is not null;
    public DateTimeOffset? AuthorityCooldownUntil {get;private set;}
    public async Task<bool> PrepareAsync(AppRelease release,PreparedUpdatePackage package,bool restart,long ownerWindow,CancellationToken ct)
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
                "--parent-start",current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),"--package",package.Directory,"--tag",release.Tag,
                "--restart",restart?"yes":"no","--owner",ownerWindow.ToString(CultureInfo.InvariantCulture)})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new IOException("The update window could not start.");
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromMinutes(20));
            await pipe.WaitForConnectionAsync(deadline.Token);
            UpdatePipeIdentity.Authenticate(pipe,true,process.Id,process.StartTime.ToUniversalTime().Ticks,host);
            var visible=false;
            while(true)
            {
                var message=await UpdateHostProtocol.ReadAsync(pipe,deadline.Token);
                if(message.Kind=="visible")visible=true;
                else if(message.Kind=="failed"){AuthorityCooldownUntil=message.CooldownUntil;throw new IOException(message.Text);}
                else if(message.Kind=="staged")
                {
                    if(!visible || message.Text!=package.Manifest.Version || !Guid.TryParseExact(message.TransactionId,"N",out _))throw new InvalidDataException("Invalid update handoff.");
                    stagedPipe?.Dispose();stagedPipe=pipe;pipe=null;stagedTransaction=message.TransactionId;stagedCloseArmed=false;return true;
                }
            }
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){return false;}
        catch(Exception ex){LocalLog.AppendLine("updates.txt","staged_handoff_failed kind="+ex.GetType().Name);return false;}
        finally{pipe?.Dispose();}
    }
    private void ConfirmStagedClose()
    {
        var pipe=Interlocked.Exchange(ref stagedPipe,null);
        if(pipe is null)return;
        using(pipe)
        using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            if(stagedCloseArmed)UpdateHostProtocol.WriteAsync(pipe,new("close",stagedTransaction),deadline.Token).GetAwaiter().GetResult();
        stagedTransaction=null;
        stagedCloseArmed=false;
    }
    public void ArmNormalClose(){if(!HasPreparedHandoff)throw new InvalidOperationException("The update handoff is unavailable.");stagedCloseArmed=true;}
    public void CancelPreparedHandoff(){Interlocked.Exchange(ref stagedPipe,null)?.Dispose();stagedTransaction=null;stagedCloseArmed=false;}
}
