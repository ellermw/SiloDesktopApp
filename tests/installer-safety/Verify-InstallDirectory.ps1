param([Parameter(Mandatory=$true)][string]$ResultDirectory,[string]$InstallerSourcePath)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$result=[IO.Path]::GetFullPath($ResultDirectory)
if(Test-Path -LiteralPath $result){throw 'Use a fresh fixture evidence directory'}
New-Item -ItemType Directory -Path $result|Out-Null
$iscc=Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if(!(Test-Path -LiteralPath $iscc)){throw 'Inno Setup6 is required for this Windows runtime fixture'}
$sourcePath=if($InstallerSourcePath){$InstallerSourcePath}else{Join-Path $repo 'installer\SiloInstaller.iss'}
$source=[IO.File]::ReadAllText($sourcePath)
$productionIdentity=[regex]::Match($source,'AppId=\{(\{[A-F0-9-]+\})').Groups[1].Value
$productionKey=[regex]::Match($source,'#define SiloUninstallKey "([^"]+)"').Groups[1].Value
if($productionKey){
 $updater=[IO.File]::ReadAllText((Join-Path $repo 'src\SiloPlayer\Services\WindowsAppUpdateInstaller.cs'))
 if(!$productionIdentity -or !$productionKey.EndsWith($productionIdentity+'_is1',[StringComparison]::OrdinalIgnoreCase) -or !$updater.Contains($productionIdentity+'_is1')){
  throw 'Production installer, guard and updater registration identities disagree'
 }
}
$begin=$source.IndexOf('[Code]')+6
$end=$source.IndexOf('procedure InstallWindowsAppRuntime;',$begin)
if($begin -lt 6 -or $end -lt $begin){throw 'Installer guard boundary missing'}
$guard=$source.Substring($begin,$end-$begin).Replace('function PrepareToInstall(','function GuardPrepareToInstall(')
$key='Software\SiloInstallerSafetyTests\'+[Guid]::NewGuid().ToString('N')
$keyLiteral=$key.Replace('"','""')
$quoted=$result.Replace("'","''")
$fixture=@"
#define MyAppExeName "SiloPlayer.exe"
#define MyAppName "Silo Desktop Player"
#define SiloUninstallKey "$keyLiteral"
[Setup]
AppId=SiloInstallerSafetyRuntimeProbe
AppName=Silo Installer Safety Runtime Probe
AppVersion=1.0
DefaultDirName=$result\target
OutputDir=$result
OutputBaseFilename=GuardProbe
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=no
CreateAppDir=yes
DisableProgramGroupPage=yes
CloseApplications=no
RestartApplications=no
[Code]
$guard
function PrepareToInstall(var NeedsRestart: Boolean): String;
var GuardResult, TestDir: String;
    Attributes, FileError, PathError: Cardinal;
begin
  TestDir := '$quoted\pending';
  if not PendingOperationsTouchDirectory(TestDir, '\??\' + Uppercase(TestDir) + '\versions\SiloPlayer.exe' + #0 + #0) then
    RaiseException('A queued delete inside the install directory was not detected');
  if not PendingOperationsTouchDirectory(TestDir, '\??\' + TestDir + '-old\file' + #0 + '!\??\' + TestDir + #0) then
    RaiseException('A queued rename to the install directory was not detected');
  if PendingOperationsTouchDirectory(TestDir, '\??\' + TestDir + '-old\versions\SiloPlayer.exe' + #0 + #0) then
    RaiseException('A queued delete in a neighbouring directory was mistaken for a Silo file');
  Attributes := GuardFileAttributes('$quoted\native-missing.txt');
  FileError := DLLGetLastError;
  if Attributes <> `$FFFFFFFF then RaiseException('Missing fixture file unexpectedly exists');
  Attributes := GuardFileAttributes('$quoted\native-missing-parent\file.txt');
  PathError := DLLGetLastError;
  if Attributes <> `$FFFFFFFF then RaiseException('Missing fixture parent unexpectedly exists');
  SaveStringToFile('$quoted\native-errors.txt', IntToStr(FileError) + ',' + IntToStr(PathError), False);
  GuardResult := GuardPrepareToInstall(NeedsRestart);
  if GuardResult = '' then SaveStringToFile('$quoted\verdict.txt', 'allowed', False)
  else SaveStringToFile('$quoted\verdict.txt', 'refused', False);
  Result := 'Isolated validation is complete. This fixture never installs files.';
end;
"@
[IO.File]::WriteAllText("$result\GuardProbe.iss",$fixture)
& $iscc "$result\GuardProbe.iss" *> "$result\compile.log"
if($LASTEXITCODE -ne 0){Get-Content "$result\compile.log" -Tail 15;throw 'Guard fixture compile failed'}
$cases=@(
 @{name='new';expected='allowed'},
 @{name='empty';expected='allowed'},
 @{name='foreign';expected='refused'},
 @{name='generic-uninstall';expected='refused'},
 @{name='copied-executable';expected='refused'},
 @{name='wrong-registration';expected='refused'},
 @{name='registered-link';expected='refused'},
 @{name='registered';expected='allowed'},
 # What an uninstall left in the owner's folder (2026-11-26): only the updater's private layout.
 @{name='silo-leftover';expected='allowed'},
 @{name='silo-leftover-foreign';expected='refused'},
 @{name='silo-leftover-linked';expected='refused'},
 # An upgrade's overwritten uninstall log never lists folders that already existed: only empty ones remain.
 @{name='empty-folders';expected='allowed'},
 @{name='versions-without-marker';expected='refused'}
)
$receipts=@();$links=@()
try {
 foreach($case in $cases){
  $target=Join-Path $result $case.name
  if(!$target.StartsWith($result+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture target escaped its dedicated root'}
  if($case.name -ne 'new'){New-Item -ItemType Directory -Path $target|Out-Null}
  if($case.name -notin @('new','empty','silo-leftover','silo-leftover-linked','empty-folders','versions-without-marker')){[IO.File]::WriteAllText("$target\unrelated.txt",'preserve this synthetic file')}
  if($case.name -eq 'empty-folders'){New-Item -ItemType Directory -Path "$target\Assets","$target\versions\1.2.1660.0"|Out-Null}
  if($case.name -eq 'versions-without-marker'){
   New-Item -ItemType Directory -Path "$target\versions\1.2.1759.0"|Out-Null
   [IO.File]::WriteAllText("$target\versions\1.2.1759.0\unrelated.txt",'preserve this synthetic file')
  }
  if($case.name -like 'silo-leftover*'){
   $authority=Join-Path $target ('.silo-update\authority\'+[Guid]::NewGuid().ToString('N'))
   New-Item -ItemType Directory -Path $authority|Out-Null
   [IO.File]::WriteAllText((Join-Path $authority (('A'*64)+'.manifest.json')),'{}')
   New-Item -ItemType Directory -Path (Join-Path $target ('.silo-update\staging\'+[Guid]::NewGuid().ToString('N')+'\bootstrap'))|Out-Null
   New-Item -ItemType Directory -Path "$target\versions\1.2.1759.0" -Force|Out-Null
   [IO.File]::WriteAllText("$target\versions\1.2.1759.0\SiloPlayer.dll",'synthetic leftover; never executed')
   New-Item -ItemType Directory -Path "$target\Assets"|Out-Null
  }
  if($case.name -eq 'silo-leftover-linked'){
   $outside=Join-Path $result 'leftover-link-target'
   New-Item -ItemType Directory -Path $outside -Force|Out-Null
   [IO.File]::WriteAllText("$outside\unrelated.txt",'preserve this synthetic file')
   $link="$target\versions\linked";$links+=@($link)
   New-Item -ItemType Junction -Path $link -Value $outside|Out-Null
  }
  if($case.name -eq 'generic-uninstall'){[IO.File]::WriteAllText("$target\unins000.dat",'another application')}
  if($case.name -in @('copied-executable','wrong-registration','registered-link','registered')){[IO.File]::WriteAllText("$target\SiloPlayer.exe",'synthetic; never executed')}
  if($case.name -in @('wrong-registration','registered-link','registered')){
   $registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
   $location=if($case.name -eq 'wrong-registration'){Join-Path $result 'other-location'}else{$target}
   try {$entry=$registry.CreateSubKey($key);try{$entry.SetValue('InstallLocation',$location)}finally{$entry.Dispose()}}finally{$registry.Dispose()}
  }
  if($case.name -eq 'registered-link'){
   $link=Join-Path $target 'linked-data';$links+=@($link)
   New-Item -ItemType Junction -Path $link -Value (Join-Path $result 'foreign')|Out-Null
  }
  $verdict="$result\verdict.txt"
  if(Test-Path -LiteralPath $verdict){Remove-Item -LiteralPath $verdict}
  $process=Start-Process -FilePath "$result\GuardProbe.exe" -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/DIR="'+$target+'"'),('/LOG="'+$result+'\'+$case.name+'.log"')) -PassThru
  if(!$process.WaitForExit(30000)){throw 'Isolated guard probe did not finish'}
  if(!(Test-Path -LiteralPath $verdict)){throw "No native guard verdict for $($case.name); exit=$($process.ExitCode)"}
  $actual=[IO.File]::ReadAllText($verdict)
  if([IO.File]::ReadAllText("$result\native-errors.txt") -ne '2,3'){throw 'Inno did not capture ERROR_FILE_NOT_FOUND/ERROR_PATH_NOT_FOUND immediately after the native calls'}
  $preserved=if($case.name -eq 'silo-leftover'){
   # Allowed only after Setup removed the proven leftovers; nothing else is required to go.
   !(Test-Path -LiteralPath "$target\.silo-update") -and !(Test-Path -LiteralPath "$target\versions")
  }elseif($case.name -eq 'silo-leftover-linked'){
   ([IO.File]::ReadAllText("$result\leftover-link-target\unrelated.txt") -eq 'preserve this synthetic file') -and (Test-Path -LiteralPath "$target\.silo-update")
  }elseif($case.name -eq 'empty-folders'){
   Test-Path -LiteralPath "$target\Assets"
  }elseif($case.name -eq 'versions-without-marker'){
   [IO.File]::ReadAllText("$target\versions\1.2.1759.0\unrelated.txt") -eq 'preserve this synthetic file'
  }else{
   $case.name -in @('new','empty') -or ([IO.File]::ReadAllText("$target\unrelated.txt") -eq 'preserve this synthetic file')
  }
  $receipts += [pscustomobject]@{name=$case.name;expected=$case.expected;actual=$actual;preserved=$preserved;passed=($actual -eq $case.expected -and $preserved);exitCode=$process.ExitCode}
 }
}finally{
 foreach($link in $links){if((Test-Path -LiteralPath $link) -and ((Get-Item -LiteralPath $link -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){[IO.Directory]::Delete($link)}}
 $registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
 try{$registry.DeleteSubKeyTree($key,$false)}finally{$registry.Dispose()}
}
$receipts|ConvertTo-Json -Depth 5|Set-Content "$result\receipt.json"
$receipts|ForEach-Object{Write-Output (($_.name)+': '+($_.actual)+' expected='+($_.expected))}
if(@($receipts|Where-Object{!$_.passed}).Count){throw 'Installer directory guard regression'}

# Exercise install/upgrade/uninstall order with an unrelated fixture AppId,
# dummy files and lowest privileges. No app, SDK, firewall, protocol or shortcut
# actions from the production installer are included.
$fullId='SiloInstallerSafety-'+[Guid]::NewGuid().ToString('N')
$fullKey='Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$fullId+'_is1'
$fullTarget=Join-Path $result 'full-install'
[IO.File]::WriteAllText("$result\dummy-payload.bin",'synthetic payload; never executed')
$fullGuard=$guard.Replace('function GuardPrepareToInstall(','function PrepareToInstall(')
$installDelete=[regex]::Match($source,'(?ms)^\[InstallDelete\]\s*\r?\n(.*?)(?=^\[|\z)').Value
$uninstallLogMode=[regex]::Match($source,'(?m)^UninstallLogMode=.*$').Value
$preflightStart=$source.IndexOf('function InitializeUninstall: Boolean;')
$preflightEnd=$source.IndexOf('function UninstallNeedRestart: Boolean;')
if($preflightStart -lt 0 -or $preflightEnd -le $preflightStart){throw 'Uninstall preflight boundary missing'}
$preflight=$source.Substring($preflightStart,$preflightEnd-$preflightStart)
$fullTemplate=@"
#define MyAppExeName "SiloPlayer.exe"
#define MyAppName "Silo Desktop Player"
#define SiloUninstallKey "$fullKey"
[Setup]
AppId=$fullId
AppName=Silo Isolated Safety Fixture
AppVersion=1.0
DefaultDirName=$fullTarget
OutputDir=$result
OutputBaseFilename=FullFixturePLACEHOLDER
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CreateAppDir=yes
DisableProgramGroupPage=yes
CloseApplications=no
RestartApplications=no
UNINSTALL_LOG_MODE_PLACEHOLDER
INSTALL_DELETE_PLACEHOLDER
[Files]
Source: "$result\dummy-payload.bin"; DestDir: "{app}"; DestName: "SiloPlayer.exe"
VERSION_PLACEHOLDER
UNINSTALL_PLACEHOLDER
[Code]
$fullGuard
"@
foreach($variant in @('Legacy','Current')){
 $delete=if($variant -eq 'Legacy'){"[UninstallDelete]`nType: filesandordirs; Name: `"{app}`""}else{''}
 $installDeleteSection=if($variant -eq 'Legacy'){"[InstallDelete]`nType: filesandordirs; Name: `"{app}\*`""}else{$installDelete}
 $versionFile=if($variant -eq 'Current'){"Source: `"$result\dummy-payload.bin`"; DestDir: `"{app}\versions\1.2.999.0`"; DestName: `"SiloPlayer.exe`""}else{''}
 $logMode=if($variant -eq 'Current'){$uninstallLogMode}else{''}
 $script=$fullTemplate.Replace('FullFixturePLACEHOLDER','FullFixture'+$variant).Replace('UNINSTALL_PLACEHOLDER',$delete).Replace('INSTALL_DELETE_PLACEHOLDER',$installDeleteSection).Replace('VERSION_PLACEHOLDER',$versionFile).Replace('UNINSTALL_LOG_MODE_PLACEHOLDER',$logMode)
 if($variant -eq 'Current'){$script+="`n$preflight"}
 [IO.File]::WriteAllText("$result\FullFixture$variant.iss",$script)
 & $iscc "$result\FullFixture$variant.iss" *> "$result\full-$variant-compile.log"
 if($LASTEXITCODE -ne 0){throw "Isolated $variant fixture did not compile"}
}
foreach($variant in @('Legacy','Current')){
 if($variant -eq 'Current'){
  [IO.File]::WriteAllText("$fullTarget\unknown-old-runtime.dll",'unknown legacy file; preserve')
  [IO.File]::WriteAllText("$fullTarget\keep-through-upgrade.txt",'preserve unknown addition through migration')
 }
 $process=Start-Process -FilePath "$result\FullFixture$variant.exe" -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/DIR="'+$fullTarget+'"'),('/LOG="'+$result+'\full-'+$variant+'.log"')) -PassThru
 if(!$process.WaitForExit(30000) -or $process.ExitCode -ne 0){throw "Isolated $variant installation failed"}
 if(!(Test-Path -LiteralPath "$fullTarget\SiloPlayer.exe")){throw 'Isolated fixture payload missing'}
}
if(!(Test-Path -LiteralPath "$fullTarget\unknown-old-runtime.dll") -or !(Test-Path -LiteralPath "$fullTarget\keep-through-upgrade.txt")){
 throw 'Side-by-side migration erased files that are not in its owned inventory'
}
if(!(Test-Path -LiteralPath "$fullTarget\versions\1.2.999.0\SiloPlayer.exe")){throw 'Versioned dummy client was not installed'}
function Invoke-FixtureUninstall([string]$LogName) {
 $process=Start-Process -FilePath "$fullTarget\unins000.exe" -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$result+'\'+$LogName+'"')) -PassThru
 if(!$process.WaitForExit(30000) -or $process.ExitCode -ne 0){throw 'Isolated uninstall failed'}
 # Inno's installed stub may return while its temporary child still owns the
 # uninstall log. Wait for actual successful completion and the closed handle;
 # otherwise the xUnit caller races that child while removing its evidence root.
 $logFinished=$false
 $logDeadline=[DateTime]::UtcNow.AddSeconds(5)
 do {
  try {
   $stream=[IO.File]::Open("$result\$LogName",[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
   try {$reader=New-Object IO.StreamReader($stream);try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$stream.Dispose()}
   $logFinished=$text.Contains('Uninstallation process succeeded.') -and $text.Contains('Log closed.')
  } catch [IO.IOException] { }
  if(!$logFinished){[Threading.Thread]::Sleep(20)}
 } until($logFinished -or [DateTime]::UtcNow -ge $logDeadline)
 if(!$logFinished){throw 'The isolated uninstall child did not finish and close its log'}
 # The child removes its own uninstaller copy last; allow it a moment.
 $folderDeadline=[DateTime]::UtcNow.AddSeconds(10)
 while((Test-Path -LiteralPath $fullTarget) -and [DateTime]::UtcNow -lt $folderDeadline){[Threading.Thread]::Sleep(50)}
}
function Get-FixtureRemainder {
 if(!(Test-Path -LiteralPath $fullTarget)){return 'nothing'}
 return ((Get-ChildItem -LiteralPath $fullTarget -Recurse -Force | ForEach-Object {$_.FullName.Substring($fullTarget.Length+1)}) -join ', ')
}
# Uninstall removes everything (owner decision, 2026-11-26): what the app or updater created in its
# version and control folders, and an unrelated file in the dedicated folder. A junction inside the
# folder is removed as a link; the folder it points to keeps its content.
$linkTarget=Join-Path $result 'uninstall-link-target'
New-Item -ItemType Directory -Path $linkTarget|Out-Null
[IO.File]::WriteAllText("$linkTarget\outside.txt",'outside the installation; preserve')
New-Item -ItemType Junction -Path "$fullTarget\linked-out" -Value $linkTarget|Out-Null
[IO.File]::WriteAllText("$fullTarget\versions\1.2.999.0\app-created.txt",'created by the app at runtime')
New-Item -ItemType Directory -Path ("$fullTarget\.silo-update\authority\"+[Guid]::NewGuid().ToString('N'))|Out-Null
New-Item -ItemType Directory -Path ("$fullTarget\.silo-update\staging\"+[Guid]::NewGuid().ToString('N')+'\bootstrap')|Out-Null
[IO.File]::WriteAllText("$fullTarget\foreign-on-uninstall.txt",'unrelated file in the dedicated folder')
Invoke-FixtureUninstall 'full-uninstall.log'
$uninstallLog=[IO.File]::ReadAllText("$result\full-uninstall.log")
if(!$uninstallLog.Contains('Silo uninstall preflight could not start') -or $uninstallLog.Contains('Silo uninstall cleanup')){
 throw 'Uninstall did not fall back to Inno cleanup after the non-executable launcher failed preflight'
}
if(Test-Path -LiteralPath $fullTarget){throw ('Uninstall left the installation folder: '+(Get-FixtureRemainder))}
if([IO.File]::ReadAllText("$linkTarget\outside.txt") -ne 'outside the installation; preserve'){throw 'Uninstall deleted through a junction'}
# A later install over leftovers of a newer version (what an older uninstaller leaves) proceeds,
# removes those leftovers and installs; the next uninstall again removes the folder.
New-Item -ItemType Directory -Path ("$fullTarget\.silo-update\staging\"+[Guid]::NewGuid().ToString('N')+'\bootstrap')|Out-Null
New-Item -ItemType Directory -Path "$fullTarget\versions\1.2.1759.0"|Out-Null
[IO.File]::WriteAllText("$fullTarget\versions\1.2.1759.0\SiloPlayer.dll",'newer leftover; never executed')
$process=Start-Process -FilePath "$result\FullFixtureCurrent.exe" -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/DIR="'+$fullTarget+'"'),('/LOG="'+$result+'\full-over-leftovers.log"')) -PassThru
if(!$process.WaitForExit(30000) -or $process.ExitCode -ne 0){throw 'Install over leftovers failed'}
if(!(Test-Path -LiteralPath "$fullTarget\versions\1.2.999.0\SiloPlayer.exe") -or (Test-Path -LiteralPath "$fullTarget\versions\1.2.1759.0") -or
 (Test-Path -LiteralPath "$fullTarget\.silo-update\staging")){throw ('Install over leftovers did not replace them: '+(Get-FixtureRemainder))}
Invoke-FixtureUninstall 'full-uninstall-after-leftovers.log'
if(Test-Path -LiteralPath $fullTarget){throw ('Second uninstall left the installation folder: '+(Get-FixtureRemainder))}
$hash=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($sourcePath)
try{$sourceHash=[BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$hash.Dispose()}
[pscustomobject]@{appId=$fullId;setupPrivileges='lowest';sourceSha256=$sourceHash;productionIdentityMatchesUpdater=$true;freshInstall=$true;registeredUpgrade=$true;foreignFilePreservedOnUpgrade=$true;versionedPayloadRemoved=$true;installFolderRemovedOnUninstall=$true;linkTargetPreservedOnUninstall=$true;installedOverLeftovers=$true;uninstallLogClosedBeforeCleanup=$true;productionAppChanged=$false}|ConvertTo-Json|Set-Content "$result\install-upgrade-uninstall.json"
Write-Output 'PASS: isolated fresh install and registered migration keep unrelated additions; uninstall removes the dedicated folder without following links; a reinstall over leftovers proceeds.'
