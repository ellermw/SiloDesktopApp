; installer/SiloInstaller.iss
; Inno Setup script for Silo Desktop Player

#define MyAppName "Silo Desktop Player"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.102"
#endif
#define MyAppPublisher "Silo"
#define MyAppExeName "SiloPlayer.exe"
#ifndef PublishSourceDir
#define PublishSourceDir "publish"
#endif

[Setup]
AppId={{B8E2F4A1-3C5D-4E6F-9A1B-2D3E4F5A6B7C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={commonpf64}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=output
OutputBaseFilename=SiloInstaller-{#MyAppVersion}-Setup
SetupIconFile=..\src\SiloPlayer\Assets\app.ico
UninstallDisplayIcon={app}\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
DisableProgramGroupPage=yes
CloseApplications=force
RestartApplications=no
#ifdef LocalSigning
SignTool=localtesting
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional options:"; Flags: checkedonce

[InstallDelete]
; Upgrades must start from a clean application payload. Earlier QA builds
; changed publish modes and left stale DLL/deps/runtime files beside the new
; EXE, which can break playback and trigger Smart App Control on obsolete files.
; User data lives in %LOCALAPPDATA%\SiloPlayer, not under {app}.
Type: filesandordirs; Name: "{app}\*"

[Files]
; Main application files from publish output
Source: "{#PublishSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Windows App SDK runtime installer
Source: "deps\windowsappruntimeinstall-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; AfterInstall: InstallWindowsAppRuntime

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon

[Registry]
; Native invitation deep links emitted by the current Silo WebUI.
Root: HKA; Subkey: "Software\Classes\silo"; ValueType: string; ValueData: "URL:Silo invitation"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\silo"; ValueName: "URL Protocol"; ValueType: string; ValueData: ""
Root: HKA; Subkey: "Software\Classes\silo\DefaultIcon"; ValueType: string; ValueData: "{app}\Assets\app.ico,0"
Root: HKA; Subkey: "Software\Classes\silo\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
; Launch app after install
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure InstallWindowsAppRuntime;
var
  ResultCode: Integer;
begin
  WizardForm.StatusLabel.Caption := 'Installing Windows App SDK runtime (this may take a moment)...';
  if not Exec(ExpandConstant('{tmp}\windowsappruntimeinstall-x64.exe'),
              '--quiet --force', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('The Windows App SDK runtime installer could not be started.');
  if ResultCode <> 0 then
    RaiseException(Format(
      'The Windows App SDK runtime installer failed with code %d. {#MyAppName} cannot start without it.', [ResultCode]));
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
