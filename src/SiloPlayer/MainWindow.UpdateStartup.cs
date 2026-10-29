using System.IO.Pipes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;
using SiloPlayer.Services;

namespace SiloPlayer;

public sealed partial class MainWindow
{
    private async Task NotifySideBySideStartupAsync()
    {
        if(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") is not null)return;
        try
        {
            var root=UpdateInstallation.FindRegisteredRoot(Environment.ProcessPath!);
            if(root is null || SideBySideInstallation.ReadState(root) is not {TransactionId:{ } transaction,PendingVersion:{ } version})return;
            // Local UI readiness only. Server/network state is intentionally not part of startup success.
            SideBySideInstallation.RecordReady(root);
            var args=Environment.GetCommandLineArgs();
            var index=Array.IndexOf(args,"--silo-update-ready");
            if(index>=0 && index+1<args.Length && Regex.IsMatch(args[index+1],@"^SiloUpdate-[a-f0-9]{32}$"))
            {
                using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var pipe=new NamedPipeClientStream(".",args[index+1],PipeDirection.InOut,PipeOptions.Asynchronous);
                await pipe.ConnectAsync(deadline.Token);
                var broker=UpdatePipeIdentity.PeerProcess(pipe,false);
                using var process=System.Diagnostics.Process.GetProcessById(broker);
                // The broker remains the original user's old version-local, inventory-verified updater.
                var image=process.MainModule!.FileName;
                var brokerRoot=UpdateInstallation.FindRegisteredRoot(image);
                if(!string.Equals(root,brokerRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unexpected updater.");
                var brokerVersion=Path.GetFileName(Directory.GetParent(Path.GetDirectoryName(image)!)!.FullName);
                var expected=await Task.Run(()=>SideBySideInstallation.VerifyInstalledClientFile(root,brokerVersion,"Updater/SiloPlayer.UpdateHost.exe"));
                UpdatePipeIdentity.Authenticate(pipe,false,broker,process.StartTime.ToUniversalTime().Ticks,expected);
                await UpdateHostProtocol.WriteAsync(pipe,new("ready",transaction),deadline.Token);
                var confirmed=await UpdateHostProtocol.ReadAsync(pipe,deadline.Token);
                if(confirmed.Kind!="confirmed" || confirmed.TransactionId!=transaction)return;
            }
            var receiptDirectory=SideBySideInstallation.GetBootReceiptDirectory(root);
            Directory.CreateDirectory(receiptDirectory);
            var shown=Path.Combine(receiptDirectory,"success-"+transaction+".shown");
            UpdateManifestValidation.EnsureOrdinaryPath(shown);
            try {using var once=new FileStream(shown,FileMode.CreateNew,FileAccess.Write,FileShare.None);once.Flush(true);}
            catch(IOException)when(File.Exists(shown)){return;}
            App.Services.GetRequiredService<ToastService>().Success("Silo was updated successfully to "+Version.Parse(version).ToString(3)+".");
        }
        catch(Exception ex){LocalLog.AppendLine("updates.txt","startup_confirmation_deferred kind="+ex.GetType().Name);}
    }
}
