using Microsoft.Win32;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SiloPlayer.Core.Services.Updates;

public static class UpdateInstallation
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B8E2F4A1-3C5D-4E6F-9A1B-2D3E4F5A6B7C}_is1";
    private static readonly HashSet<string> PrivilegedOwners = ["S-1-5-18","S-1-5-32-544","S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"];

    public static bool IsPrivilegedAcl(FileSystemSecurity acl,bool ancestor=false)
    {
        if(acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !PrivilegedOwners.Contains(owner.Value))return false;
        var dangerous=FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
        if(!ancestor)dangerous|=FileSystemRights.Write;
        var rules=acl.GetAccessRules(true,true,typeof(SecurityIdentifier));
        if(rules.Count==0)return false; // A null DACL is unrestricted.
        foreach(FileSystemAccessRule rule in rules)
            if(rule.AccessControlType==AccessControlType.Allow && (rule.PropagationFlags&PropagationFlags.InheritOnly)==0
                && !PrivilegedOwners.Contains(((SecurityIdentifier)rule.IdentityReference).Value) && (rule.FileSystemRights&dangerous)!=0)return false;
        return true;
    }

    /// <summary>Inventory hashes in a user-writable installation cannot authorize elevation.</summary>
    public static void RequireProtectedMachineInstallation(string root,string versionDirectory)
    {
        var canonical=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if(!string.Equals(FindRegisteredRoot(Path.Combine(canonical,"SiloPlayer.exe"),machineOnly:true),canonical,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("A protected machine installation is required. Use the full installer.");
        var version=Path.GetFullPath(versionDirectory);
        if(!version.StartsWith(canonical+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The updater escaped the installation.");
        for(var directory=new DirectoryInfo(canonical);directory is not null;directory=directory.Parent)
        {
            UpdateManifestValidation.EnsureOrdinaryPath(directory.FullName);
            if(!IsPrivilegedAcl(directory.GetAccessControl(),directory.FullName!=canonical))throw new InvalidDataException("This installation permits unprivileged changes. Use the full installer to repair its permissions.");
        }
        var stack=new Stack<DirectoryInfo>();stack.Push(new DirectoryInfo(canonical));var count=0;
        while(stack.Count>0)
        {
            var directory=stack.Pop();
            if(++count>16384)throw new InvalidDataException("Installation inventory exceeds the supported bounds.");
            UpdateManifestValidation.EnsureOrdinaryPath(directory.FullName);
            if(!IsPrivilegedAcl(directory.GetAccessControl()))throw new InvalidDataException("The installation directory is writable without administrator approval.");
            foreach(var entry in directory.EnumerateFileSystemInfos())
            {
                UpdateManifestValidation.EnsureOrdinaryPath(entry.FullName);
                if(entry is DirectoryInfo child)stack.Push(child);
                else if(!IsPrivilegedAcl(((FileInfo)entry).GetAccessControl()))throw new InvalidDataException("An installed file is writable without administrator approval.");
            }
        }
    }

    public static string? FindRegisteredRoot(string executable, bool machineOnly = false)
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach(var hive in machineOnly ? new[]{RegistryHive.LocalMachine} : new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
        {
            try
            {
            using var registry=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64);
            using var key=registry.OpenSubKey(UninstallKey);
            if(key?.GetValue("InstallLocation") is string location && MatchRegisteredRoot(executable,new[]{location}) is { } root)
                return root;
            }
            catch(Exception ex)when(ex is UnauthorizedAccessException or IOException or ArgumentException or System.Security.SecurityException){ }
        }
        return null;
    }

    public static string? MatchRegisteredRoot(string executable,IEnumerable<string> registeredLocations)
    {
        if(executable.Replace('\\','/').Split('/').Any(p=>p is "." or "..")) return null;
        var full=Path.GetFullPath(executable);
        if(!new[]{"SiloPlayer.exe","SiloPlayer.UpdateHost.exe"}.Contains(Path.GetFileName(full),StringComparer.OrdinalIgnoreCase)) return null;
        foreach(var location in registeredLocations)
        {
            var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
            var relative=Path.GetRelativePath(root,full).Replace('\\','/').Split('/');
            if(relative.Length==1 && relative[0].Equals("SiloPlayer.exe",StringComparison.OrdinalIgnoreCase)) return root;
            if((relative.Length==3 || (relative.Length==4 && relative[2]=="Updater" && relative[3].Equals("SiloPlayer.UpdateHost.exe",StringComparison.OrdinalIgnoreCase))) && relative[0].Equals("versions",StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(relative[1],out var version) && version.Revision>=0 && version.ToString(4)==relative[1]) return root;
        }
        return null;
    }
}
