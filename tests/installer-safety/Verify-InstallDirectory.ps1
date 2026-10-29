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
var GuardResult: String;
    Attributes, FileError, PathError: Cardinal;
begin
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
 @{name='registered';expected='allowed'}
)
$receipts=@();$links=@()
try {
 foreach($case in $cases){
  $target=Join-Path $result $case.name
  if(!$target.StartsWith($result+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture target escaped its dedicated root'}
  if($case.name -ne 'new'){New-Item -ItemType Directory -Path $target|Out-Null}
  if($case.name -notin @('new','empty')){[IO.File]::WriteAllText("$target\unrelated.txt",'preserve this synthetic file')}
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
  $preserved=$case.name -in @('new','empty') -or ([IO.File]::ReadAllText("$target\unrelated.txt") -eq 'preserve this synthetic file')
  $receipts += [pscustomobject]@{name=$case.name;expected=$case.expected;actual=$actual;preserved=$preserved;passed=($actual -eq $case.expected -and $preserved);exitCode=$process.ExitCode}
 }
}finally{
 foreach($link in $links){if((Get-Item -LiteralPath $link -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){[IO.Directory]::Delete($link)}}
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
[IO.File]::WriteAllText("$fullTarget\versions\1.2.999.0\keep-with-version.txt",'preserve foreign version addition')
[IO.File]::WriteAllText("$fullTarget\keep-on-uninstall.txt",'preserve synthetic personal file')
$process=Start-Process -FilePath "$fullTarget\unins000.exe" -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$result+'\full-uninstall.log"')) -PassThru
if(!$process.WaitForExit(30000) -or $process.ExitCode -ne 0){throw 'Isolated uninstall failed'}
# Inno's installed stub may return while its temporary child still owns the
# uninstall log. Wait for actual successful completion and the closed handle;
# otherwise the xUnit caller races that child while removing its evidence root.
$logFinished=$false
$logDeadline=[DateTime]::UtcNow.AddSeconds(5)
do {
 try {
  $stream=[IO.File]::Open("$result\full-uninstall.log",[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
  try {$reader=New-Object IO.StreamReader($stream);try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$stream.Dispose()}
  $logFinished=$text.Contains('Uninstallation process succeeded.') -and $text.Contains('Log closed.')
 } catch [IO.IOException] { }
 if(!$logFinished){[Threading.Thread]::Sleep(20)}
} until($logFinished -or [DateTime]::UtcNow -ge $logDeadline)
if(!$logFinished){throw 'The isolated uninstall child did not finish and close its log'}
if(!(Test-Path -LiteralPath "$fullTarget\keep-on-uninstall.txt") -or
 !(Test-Path -LiteralPath "$fullTarget\versions\1.2.999.0\keep-with-version.txt") -or
 (Test-Path -LiteralPath "$fullTarget\versions\1.2.999.0\SiloPlayer.exe") -or
 (Test-Path -LiteralPath "$fullTarget\SiloPlayer.exe")){
 throw 'Uninstall did not preserve an unrelated addition while removing installed files'
}
$hash=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($sourcePath)
try{$sourceHash=[BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$hash.Dispose()}
[pscustomobject]@{appId=$fullId;setupPrivileges='lowest';sourceSha256=$sourceHash;productionIdentityMatchesUpdater=$true;freshInstall=$true;registeredUpgrade=$true;foreignFilePreservedOnUpgrade=$true;versionedPayloadRemoved=$true;foreignFilePreservedOnUninstall=$true;uninstallLogClosedBeforeCleanup=$true;productionAppChanged=$false}|ConvertTo-Json|Set-Content "$result\install-upgrade-uninstall.json"
Write-Output 'PASS: isolated fresh install, registered migration and logged-file uninstall preserve unrelated additions.'
