param([Parameter(Mandatory)][string]$HostExecutable,[Parameter(Mandatory)][string]$ResultDirectory)
$ErrorActionPreference='Stop'
Add-Type -Namespace SiloSmoke -Name Native -MemberDefinition @'
[DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeoutW")]
public static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint msg,IntPtr wParam,System.Text.StringBuilder lParam,uint flags,uint timeout,out IntPtr result);
'@
# The panel is frameless (no WS_CAPTION), and GetWindowText (Process.MainWindowTitle) may return nothing
# for another process's caption-less window, so ask the window itself with WM_GETTEXT.
function Get-WindowTitle([IntPtr]$hwnd){
    $text=[Text.StringBuilder]::new(256);$copied=[IntPtr]::Zero
    if([SiloSmoke.Native]::SendMessageTimeout($hwnd,0x000D,[IntPtr]256,$text,0x0002,2000,[ref]$copied) -eq [IntPtr]::Zero){return $null} # WM_GETTEXT, SMTO_ABORTIFHUNG
    $text.ToString()
}
$image=[IO.Path]::GetFullPath($HostExecutable)
if(!(Test-Path -LiteralPath $image -PathType Leaf) -or [IO.Path]::GetFileName($image) -ne 'SiloPlayer.UpdateHost.exe'){throw 'Expected a built updater host.'}
foreach($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryHive]::CurrentUser)) {
    $registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,[Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key=$registry.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B8E2F4A1-3C5D-4E6F-9A1B-2D3E4F5A6B7C}_is1')
        if($key) {try {$location=$key.GetValue('InstallLocation');if($location -and $image.StartsWith([IO.Path]::GetFullPath($location).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Never run this smoke test against an installed player.'}}finally{$key.Dispose()}}
    }finally{$registry.Dispose()}
}
if(Test-Path -LiteralPath $ResultDirectory){throw 'Use a fresh smoke evidence directory.'}
$null=New-Item -ItemType Directory -Path $ResultDirectory
$start=[Diagnostics.ProcessStartInfo]::new($image)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.RedirectStandardError=$true;$start.WorkingDirectory=Split-Path $image
foreach($arg in @('--worker','no','--app-pipe',('SiloUpdate-'+[guid]::NewGuid().ToString('N')),'--worker-pipe',('SiloUpdate-'+[guid]::NewGuid().ToString('N')),
    '--parent',"$PID",'--parent-start','1','--package',(Join-Path $ResultDirectory 'unused-synthetic'),'--tag','v1.2.484','--restart','yes','--owner','0')){$start.ArgumentList.Add($arg)}
# Unregistered host fails its root check before any pipe, network, elevation or installation.
# Its own WinUI Application must still construct/render the visible recovery window.
$process=[Diagnostics.Process]::Start($start);$errorTask=$process.StandardError.ReadToEndAsync()
try {
    $end=[DateTime]::UtcNow.AddSeconds(10)
    while(!$process.HasExited -and [DateTime]::UtcNow -lt $end){Start-Sleep -Milliseconds 100;$process.Refresh();if($process.MainWindowHandle -ne 0){break}}
    if($process.HasExited -or $process.MainWindowHandle -eq 0 -or (Get-WindowTitle $process.MainWindowHandle) -ne 'Updating Silo Desktop Player'){throw 'The updater Application did not construct its own window with the Silo Desktop Player title.'}
    Start-Sleep -Milliseconds 500;$process.Refresh();if($process.HasExited){throw 'Updater startup crashed after window creation.'}
    $receipt=[ordered]@{image=$image;sha256=(Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash;version=[Diagnostics.FileVersionInfo]::GetVersionInfo($image).FileVersion;
        ownWindowCreated=$true;unregisteredRoot=$true;noElevationOrNetwork=$true;pid=$process.Id;utc=[DateTimeOffset]::UtcNow}
    $receipt|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $ResultDirectory 'receipt.json')
}finally {
    if(!$process.HasExited){$process.Kill();$process.WaitForExit()}
    $errorTask.GetAwaiter().GetResult()|Set-Content -LiteralPath (Join-Path $ResultDirectory 'stderr.txt')
    $process.Dispose()
}
if((Get-Item -LiteralPath (Join-Path $ResultDirectory 'stderr.txt')).Length -gt 3){throw 'Updater startup reported an error.'}
