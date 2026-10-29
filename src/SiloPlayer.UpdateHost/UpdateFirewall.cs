using System.Diagnostics;

namespace SiloPlayer.UpdateHost;

internal static class UpdateFirewall
{
    internal static void Authorize(string executable)
    {
        Run(executable,add:false,required:false);
        Run(executable,add:true,required:true);
    }
    internal static void Retire(string executable)=>Run(executable,add:false,required:true);
    private static void Run(string executable,bool add,bool required)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"netsh.exe"))
            {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        var args=add ? new[]{"advfirewall","firewall","add","rule","name=Silo Desktop SiloCast (Private LAN)","dir=in","action=allow",
            "program="+executable,"enable=yes","profile=private","remoteip=localsubnet"}
            : new[]{"advfirewall","firewall","delete","rule","name=Silo Desktop SiloCast (Private LAN)","program="+executable};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("The Private LAN receiver rule could not be created.");
        if(!process.WaitForExit(15000) || (required && process.ExitCode!=0))throw new IOException("The Private LAN receiver rule could not be updated.");
        // Retain previous-version authorization until its activation has been confirmed.
    }
}
