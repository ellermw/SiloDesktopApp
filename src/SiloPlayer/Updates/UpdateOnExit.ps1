param([int]$ParentPid, [long]$ParentStartTicks, [string]$Version, [string]$Sha256,
    [long]$ExpectedSize, [string]$AppPath, [switch]$Restart, [switch]$VerifyOnly, [string]$ValidationRoot, [string]$HandoffToken, [switch]$ValidateExitGate)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2
$root=$null; $gate=$null; $gateOwned=$false; $packageStream=$null; $failureState='failed'

function Assert-OrdinaryPath([string]$Path) {
    $current=[IO.Path]::GetFullPath($Path)
    while ($current) {
        try {
            if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Update paths cannot contain links or junctions.'
            }
        } catch [IO.FileNotFoundException] { } catch [IO.DirectoryNotFoundException] { }
        $current=[IO.Path]::GetDirectoryName($current)
    }
}
function Write-Result([string]$State,[string]$Message,[string]$GateName='') {
    $result=@{state=$State;message=$Message;version=$Version;utc=[DateTime]::UtcNow.ToString('o');gate=$GateName}
    $json=$result|ConvertTo-Json -Compress
    if (-not $VerifyOnly -and $root -and [IO.Directory]::Exists($root)) {
        $temporary=Join-Path $root ('result-'+[Guid]::NewGuid().ToString('N')+'.tmp')
        [IO.File]::WriteAllText($temporary,$json)
        $target=Join-Path $root 'last-result.json'
        Assert-OrdinaryPath $target
        if ([IO.File]::Exists($target)) {[IO.File]::Replace($temporary,$target,$null)} else {[IO.File]::Move($temporary,$target)}
        $logPath=Join-Path $root 'install.log'
        Assert-OrdinaryPath $logPath
        [IO.File]::AppendAllText($logPath,$json+[Environment]::NewLine)
    }
    Write-Output $json
}
function Wait-UpdateSetup([Diagnostics.Process]$Process,[int]$WarningAfterMilliseconds=900000,[scriptblock]$OnLongRunning) {
    if($Process.WaitForExit($WarningAfterMilliseconds)){return $false}
    # Keep the package lock and install gate for as long as setup is alive.
    # A slow setup is never killed and cannot race a newly launched player.
    try {& $OnLongRunning} finally {$Process.WaitForExit()}
    return $true
}
try {
    $parsed=$null
    if (-not [version]::TryParse($Version,[ref]$parsed) -or $parsed.Build -lt 0 -or $parsed.Revision -lt 0 -or
        $parsed.ToString() -ne $Version -or $Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $ExpectedSize -le 0 -or $ExpectedSize -gt 536870912) {
        throw 'Invalid update identity.'
    }
    if ($ValidationRoot -and -not $VerifyOnly) {throw 'A validation directory is only supported with VerifyOnly.'}
    if ($ValidateExitGate -and -not $VerifyOnly) {throw 'Exit-gate validation requires VerifyOnly.'}
    $root=if($VerifyOnly -and $ValidationRoot){[IO.Path]::GetFullPath($ValidationRoot)}else{
        Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SiloPlayer\updates'
    }
    $app=[IO.Path]::GetFullPath($AppPath)
    if ([IO.Path]::GetFileName($app) -ine 'SiloPlayer.exe') {throw 'Unexpected application executable.'}
    $appDirectory=[IO.Path]::GetDirectoryName($app).TrimEnd([char[]]'\/')
    $hash=[Security.Cryptography.SHA256]::Create()
    try {$gateName='Global\SiloDesktopUpdate-'+([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($appDirectory.ToUpperInvariant()))).Replace('-','').Substring(0,16))}finally{$hash.Dispose()}
    $package=Join-Path (Join-Path $root $Version) 'SiloInstaller-Windows-x64.exe'
    Assert-OrdinaryPath $package
    $packageStream=[IO.File]::Open($package,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    if ($packageStream.Length -ne $ExpectedSize) {throw 'Installer size verification failed.'}
    $hash=[Security.Cryptography.SHA256]::Create()
    try {$actual=[BitConverter]::ToString($hash.ComputeHash($packageStream)).Replace('-','')}finally{$hash.Dispose()}
    if ($actual -ine $Sha256) {throw 'Installer checksum verification failed.'}
    if ($VerifyOnly -and -not $ValidateExitGate) {Write-Result 'verified' 'Package verified; no installation was launched.' $gateName;exit 0}
    if (-not $VerifyOnly) {
    if ($HandoffToken -notmatch '^[a-f0-9]{32}$') {throw 'Missing normal-close confirmation identity.'}

    $registered=$false
    foreach($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryHive]::CurrentUser)) {
        $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,[Microsoft.Win32.RegistryView]::Registry64)
        try {
            $key=$base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B8E2F4A1-3C5D-4E6F-9A1B-2D3E4F5A6B7C}_is1')
            if($key){try{$location=$key.GetValue('InstallLocation','');if($location -and [IO.Path]::GetFullPath($location).TrimEnd([char[]]'\/') -ieq $appDirectory){$registered=$true}}finally{$key.Dispose()}}
        } finally {$base.Dispose()}
    }
    if(-not $registered){throw 'Updates can only be installed into the registered Silo installation.'}
    Write-Result 'waiting' 'Waiting for Silo to finish closing.' $gateName
    if($ParentPid -le 0 -or $ParentStartTicks -le 0){throw 'Missing parent process identity.'}
    $parent=$null
    try {$parent=[Diagnostics.Process]::GetProcessById($ParentPid)} catch [ArgumentException] { }
    if($parent){try {
        if($parent.StartTime.ToUniversalTime().Ticks -ne $ParentStartTicks){throw 'The original application process identity changed.'}
        if(-not $parent.WaitForExit(120000)){$failureState='deferred';throw 'Silo is still closing. Update postponed.'}
    }finally{$parent.Dispose()}}
    $confirmation=Join-Path (Join-Path $root $Version) ('close-'+$HandoffToken+'.confirmed')
    Assert-OrdinaryPath $confirmation
    if(-not [IO.File]::Exists($confirmation) -or [IO.File]::ReadAllText($confirmation) -cne $HandoffToken){$failureState='deferred';throw 'Silo did not complete a normal close. Update postponed.'}
    [IO.File]::Delete($confirmation)
    }

    # Share only a named coordination object, never a privileged service.
    $security=New-Object Security.AccessControl.MutexSecurity
    $sid=New-Object Security.Principal.SecurityIdentifier('S-1-1-0')
    $rule=New-Object Security.AccessControl.MutexAccessRule($sid,[Security.AccessControl.MutexRights]::FullControl,[Security.AccessControl.AccessControlType]::Allow)
    $security.AddAccessRule($rule)
    $created=$false
    $gate=[Threading.Mutex]::new($false,$gateName,[ref]$created,$security)
    try {$gateOwned=$gate.WaitOne(0)}catch [Threading.AbandonedMutexException]{$gateOwned=$true}
    if(-not $gateOwned){$failureState='deferred';throw 'Another update is running. Update postponed.'}
    foreach($running in [Diagnostics.Process]::GetProcessesByName('SiloPlayer')) {
        try {
            try {$runningPath=$running.MainModule.FileName}
            catch {if($running.HasExited){continue};$failureState='deferred';throw 'Another Silo process cannot be checked. Update postponed.'}
            if($runningPath -ieq $app){$failureState='deferred';throw 'Another Silo instance is open. Update postponed.'}
        }finally{$running.Dispose()}
    }
    if ($VerifyOnly) {Write-Result 'verified-gate' 'Package and exit gate verified; no installation was launched.' $gateName;exit 0}
    Write-Result 'installing' 'Installing the verified update.' $gateName
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$package; $start.UseShellExecute=$true; $start.Verb='runas'; $start.WindowStyle='Hidden'
    $installerLog=Join-Path $root ('installer-'+[Guid]::NewGuid().ToString('N')+'.log')
    Assert-OrdinaryPath $installerLog
    $start.Arguments='/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NOCLOSEAPPLICATIONS /NOFORCECLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /DIR="'+$appDirectory+'" /LOG="'+$installerLog+'"'
    $setup=[Diagnostics.Process]::Start($start)
    try {
        $longRunning=Wait-UpdateSetup -Process $setup -OnLongRunning {Write-Result 'installing' 'The installer is still running. Waiting safely without restarting Silo.' $gateName | Out-Null}
        if($longRunning){$Restart=$false}
        if($setup.ExitCode -eq 3010){Write-Result 'restart-required' 'Windows needs a restart to complete this update.' $gateName;exit 0}
        if($setup.ExitCode -ne 0){throw ('The installer exited with code '+$setup.ExitCode+'. Retry the staged installer.')}
    }finally{$setup.Dispose()}
    $installed=$null
    if(-not [version]::TryParse([Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion,[ref]$installed) -or $installed -ne $parsed){throw 'The installed version could not be verified. Retry the staged installer.'}
    Write-Result 'installed' 'Update installed successfully.' $gateName
    $gate.ReleaseMutex();$gateOwned=$false
    if($Restart){$null=[Diagnostics.Process]::Start((New-Object Diagnostics.ProcessStartInfo -Property @{FileName=$app;UseShellExecute=$true}))}
    exit 0
} catch {
    $errorObject=$_.Exception
    while($errorObject.InnerException){$errorObject=$errorObject.InnerException}
    if($errorObject -is [ComponentModel.Win32Exception] -and $errorObject.NativeErrorCode -eq 1223){$failureState='canceled'}
    Write-Result $failureState $errorObject.Message
    exit 1
} finally {
    if($packageStream){$packageStream.Dispose()}
    if($gate){if($gateOwned){$gate.ReleaseMutex()};$gate.Dispose()}
}
