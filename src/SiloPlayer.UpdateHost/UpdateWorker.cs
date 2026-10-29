using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.UpdateHost;

internal static class UpdateWorker
{
    // Keep synchronous: Windows mutex ownership belongs to the entry thread.
    public static int Run(HostOptions options)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(20));
        using var pipe=new NamedPipeClientStream(".",options.WorkerPipe,PipeDirection.InOut,PipeOptions.Asynchronous);
        pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
        UpdatePipeIdentity.Authenticate(pipe,false,options.BrokerPid,options.BrokerStart,Environment.ProcessPath!);
        var root=UpdateInstallation.FindRegisteredRoot(Environment.ProcessPath!,machineOnly:true)??throw new InvalidOperationException("Use the registered machine installation.");
        var directory=Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        using var environment=new WindowsTransactionEnvironment(options,root,directory,pipe);
        var result=UpdateTransactionRunner.Run(new(root,Path.GetFileName(directory),options.Tag,options.Restart),environment,timeout.Token);
        return result.Succeeded ? 0 : 1;
    }
    private sealed class WindowsTransactionEnvironment(HostOptions options,string root,string directory,PipeStream pipe) : IUpdateTransactionEnvironment,IDisposable
    {
        private Process? parent;
        public void ValidateCallerAndParent()
        {
            using var identity=WindowsIdentity.GetCurrent();
            if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new InvalidOperationException("Administrator approval is required.");
            UpdateInstallation.RequireProtectedMachineInstallation(root,directory);
            if(!UpdatePipeIdentity.MatchesProcess(options.ParentPid,options.ParentStart,Path.Combine(directory,"SiloPlayer.exe")))throw new InvalidDataException("The original Silo process changed.");
            parent=Process.GetProcessById(options.ParentPid);
            if(parent.StartTime.ToUniversalTime().Ticks!=options.ParentStart)throw new InvalidDataException("The original Silo process changed.");
            _=parent.SafeHandle;
        }
        public IDisposable AcquireInstallationLock(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var mutex=MutexAcl.OpenExisting(SideBySideInstallation.GetMutexName(root),MutexRights.Synchronize|MutexRights.Modify);
            var held=false;
            try
            {
                try{held=mutex.WaitOne(TimeSpan.FromSeconds(10));}catch(AbandonedMutexException){held=true;}
                if(!held)throw new IOException("Another Silo update owns the installation.");
                ct.ThrowIfCancellationRequested();return new MutationLease(mutex);
            }
            catch{if(held)mutex.ReleaseMutex();mutex.Dispose();throw;}
        }
        public PreparedUpdatePackage FetchAuthoritativePackage(string tag,CancellationToken ct)
        {
            using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=System.Net.DecompressionMethods.None}){Timeout=TimeSpan.FromSeconds(30)};
            var target=Path.Combine(root,SideBySideInstallation.MetadataDirectoryName,"authority",Guid.NewGuid().ToString("N"));
            var authority=new IncrementalReleaseAuthority(http).FetchAsync(tag,target,ct).GetAwaiter().GetResult();
            return new(options.PackageDirectory,authority.ManifestPath,authority.Release.Incremental!.ManifestSha256,authority.Manifest);
        }
        public void AuthorizeNewClient(string executable)=>UpdateFirewall.Authorize(executable);
        public void Send(UpdateHostMessage message,CancellationToken ct)
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
            UpdateHostProtocol.WriteAsync(pipe,message,deadline.Token).GetAwaiter().GetResult();
        }
        public UpdateHostMessage Receive(CancellationToken ct)=>UpdateHostProtocol.ReadAsync(pipe,ct).GetAwaiter().GetResult();
        public void WaitForParentExit(CancellationToken ct)=>parent!.WaitForExitAsync(ct).GetAwaiter().GetResult();
        public bool HasParentExited=>parent?.HasExited==true;
        public void VerifyNoOtherClient()
        {
            foreach(var process in Process.GetProcessesByName("SiloPlayer"))using(process)
            {
                if(process.HasExited)continue;
                var image=UpdatePipeIdentity.InspectProcess(process.Id).Image;
                if(UpdateInstallation.MatchRegisteredRoot(image,new[]{root}) is not null)throw new IOException("Close the other Silo window before updating.");
            }
        }
        public bool MatchesReadyProcess(UpdateHostMessage message,string executable)=>UpdatePipeIdentity.MatchesProcess(message.ProcessId,message.StartUtcTicks,executable);
        public bool IsVersionInUse(string versionDirectory)
        {
            try
            {
                foreach(var name in new[]{"SiloPlayer","SiloPlayer.UpdateHost"})foreach(var process in Process.GetProcessesByName(name))using(process)
                {
                    if(process.HasExited)continue;
                    var image=UpdatePipeIdentity.InspectProcess(process.Id).Image;
                    if(image.StartsWith(Path.TrimEndingDirectorySeparator(versionDirectory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return true;
                }
                return false;
            }
            catch{return true;}
        }
        public void RetireUnusedAuthorization(string versionDirectory)=>UpdateFirewall.Retire(Path.Combine(versionDirectory,"SiloPlayer.exe"));
        public void Dispose()=>parent?.Dispose();
    }
    private sealed class MutationLease(Mutex mutex) : IDisposable
    {
        private bool held=true;
        public void Dispose(){if(!held)return;held=false;mutex.ReleaseMutex();mutex.Dispose();}
    }
}
