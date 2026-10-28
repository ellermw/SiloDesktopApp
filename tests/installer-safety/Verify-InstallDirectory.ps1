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
begin
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
[InstallDelete]
Type: filesandordirs; Name: "{app}\*"
[Files]
Source: "$result\dummy-payload.bin"; DestDir: "{app}"; DestName: "SiloPlayer.exe"
UNINSTALL_PLACEHOLDER
[Code]
$fullGuard
"@
foreach($variant in @('Legacy','Current')){
 $delete=if($variant -eq 'Legacy'){"[UninstallDelete]`nType: filesandordirs; Name: `"{app}`""}else{''}
 $script=$fullTemplate.Replace('FullFixturePLACEHOLDER','FullFixture'+$variant).Replace('UNINSTALL_PLACEHOLDER',$delete)
 [IO.File]::WriteAllText("$result\FullFixture$variant.iss",$script)
 & $iscc "$result\FullFixture$variant.iss" *> "$result\full-$variant-compile.log"
 if($LASTEXITCODE -ne 0){throw "Isolated $variant fixture did not compile"}
}
foreach($variant in @('Legacy','Current')){
 if($variant -eq 'Current'){[IO.File]::WriteAllText("$fullTarget\obsolete-runtime.dll",'old synthetic runtime')}
 $process=Start-Process -FilePath "$result\FullFixture$variant.exe" -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/DIR="'+$fullTarget+'"'),('/LOG="'+$result+'\full-'+$variant+'.log"')) -PassThru
 if(!$process.WaitForExit(30000) -or $process.ExitCode -ne 0){throw "Isolated $variant installation failed"}
 if(!(Test-Path -LiteralPath "$fullTarget\SiloPlayer.exe")){throw 'Isolated fixture payload missing'}
}
if(Test-Path -LiteralPath "$fullTarget\obsolete-runtime.dll"){throw 'Registered clean-payload upgrade left an obsolete runtime'}
[IO.File]::WriteAllText("$fullTarget\keep-on-uninstall.txt",'preserve synthetic personal file')
$process=Start-Process -FilePath "$fullTarget\unins000.exe" -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$result+'\full-uninstall.log"')) -PassThru
if(!$process.WaitForExit(30000) -or $process.ExitCode -ne 0){throw 'Isolated uninstall failed'}
if(!(Test-Path -LiteralPath "$fullTarget\keep-on-uninstall.txt") -or (Test-Path -LiteralPath "$fullTarget\SiloPlayer.exe")){
 throw 'Uninstall did not preserve an unrelated addition while removing installed files'
}
[pscustomobject]@{appId=$fullId;setupPrivileges='lowest';sourceSha256=(Get-FileHash -LiteralPath $sourcePath).Hash;freshInstall=$true;registeredUpgrade=$true;obsoletePayloadRemoved=$true;foreignFilePreservedOnUninstall=$true;productionAppChanged=$false}|ConvertTo-Json|Set-Content "$result\install-upgrade-uninstall.json"
Write-Output 'PASS: isolated fresh install, registered clean upgrade and uninstall preserve the unrelated addition.'
