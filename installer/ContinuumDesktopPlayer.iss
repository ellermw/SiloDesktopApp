; installer/ContinuumDesktopPlayer.iss
; Inno Setup script for Continuum Desktop Player

#define MyAppName "Continuum Desktop Player"
#define MyAppVersion "1.0.162"
#define MyAppPublisher "Continuum"
#define MyAppExeName "ContinuumPlayer.exe"

[Setup]
AppId={{B8E2F4A1-3C5D-4E6F-9A1B-2D3E4F5A6B7C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={commonpf64}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=output
OutputBaseFilename=ContinuumDesktopPlayer-{#MyAppVersion}-Setup
SetupIconFile=..\src\ContinuumPlayer\Assets\app.ico
UninstallDisplayIcon={app}\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional options:"; Flags: checkedonce

[Files]
; Main application files from publish output
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Windows App SDK runtime installer
Source: "deps\windowsappruntimeinstall-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon

[Run]
; Install Windows App SDK runtime (--quiet suppresses UI, --force skips if already installed)
Filename: "{tmp}\windowsappruntimeinstall-x64.exe"; Parameters: "--quiet --force"; StatusMsg: "Installing Windows App SDK runtime (this may take a moment)..."; Flags: waituntilterminated

; Launch app after install
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
