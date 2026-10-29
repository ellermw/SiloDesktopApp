using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace SiloPlayer.Core.Services.Updates;

public static class UpdatePipeIdentity
{
    public static Mutex CreateMutationMutex(string root)
    {
        var security=new MutexSecurity();security.SetAccessRuleProtection(true,false);
        security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),MutexRights.FullControl,AccessControlType.Deny));
        security.AddAccessRule(new MutexAccessRule(WindowsIdentity.GetCurrent().User!,MutexRights.FullControl,AccessControlType.Allow));
        security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),MutexRights.FullControl,AccessControlType.Allow));
        security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),MutexRights.FullControl,AccessControlType.Allow));
        return MutexAcl.Create(false,SideBySideInstallation.GetMutexName(root),out _,security);
    }
    public static (long StartUtcTicks,string Image) InspectProcess(int pid)
    {
        using var handle=OpenProcess(0x1000,false,pid);
        if(handle.IsInvalid || !GetProcessTimes(handle,out var created,out _,out _,out _) || !GetExitCodeProcess(handle,out var exitCode) || exitCode!=259)throw new IOException("Cannot inspect a live update process.");
        var length=32768;var image=new System.Text.StringBuilder(length);
        if(!QueryFullProcessImageName(handle,0,image,ref length))throw new IOException("Cannot inspect update image.");
        return(DateTime.FromFileTimeUtc(created).Ticks,Path.GetFullPath(image.ToString()));
    }
    public static NamedPipeServerStream CreateServer(string name,bool allowElevation=false)
    {
        if(!allowElevation) return new(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var security=new PipeSecurity();
        security.SetAccessRuleProtection(true,false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!,PipeAccessRights.FullControl,AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);
    }

    public static bool MatchesProcess(int pid,long startTicks,string image)
    {
        try
        {
            var process=InspectProcess(pid);
            return process.StartUtcTicks==startTicks && process.Image.Equals(Path.GetFullPath(image),StringComparison.OrdinalIgnoreCase);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException){return false;}
    }

    public static int PeerProcess(PipeStream pipe,bool server)
    {
        uint pid;
        var ok=server ? GetNamedPipeClientProcessId(pipe.SafePipeHandle,out pid) : GetNamedPipeServerProcessId(pipe.SafePipeHandle,out pid);
        if(!ok || pid>int.MaxValue)throw new IOException("Cannot authenticate update connection.");
        return (int)pid;
    }

    public static void Authenticate(PipeStream pipe,bool server,int pid,long startTicks,string image)
    {
        if(PeerProcess(pipe,server)!=pid || !MatchesProcess(pid,startTicks,image))
            throw new InvalidDataException("The update connection has an unexpected process identity.");
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle handle,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle handle,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetProcessTimes(SafeProcessHandle handle,out long created,out long exited,out long kernel,out long user);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetExitCodeProcess(SafeProcessHandle handle,out uint code);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle handle,uint flags,System.Text.StringBuilder image,ref int size);
}
