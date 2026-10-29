; installer/SiloInstaller.iss
; Inno Setup script for Silo Desktop Player

#define MyAppName "Silo Desktop Player"
#ifndef MyAppVersion
#define MyAppVersion "1.2.505"
#endif
#define MyAppPublisher "Silo"
#define MyAppExeName "SiloPlayer.exe"
#ifndef MyAppFourPartVersion
#define MyAppFourPartVersion MyAppVersion + ".0"
#endif
#ifndef SiloUninstallKey
#define SiloUninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8E2F4A1-3C5D-4E6F-9A1B-2D3E4F5A6B7C}_is1"
#endif
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
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
DisableProgramGroupPage=yes
CloseApplications=force
RestartApplications=no
; Discard historical broad UninstallDelete directives from legacy releases.
; Current files are logged anew; incremental files have validated inventories.
UninstallLogMode=overwrite
SetupMutex=Global\SiloDesktopInstaller-B8E2F4A1-3C5D-4E6F-9A1B-2D3E4F5A6B7C
#ifdef LocalSigning
SignTool=localtesting
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional options:"; Flags: checkedonce

[Files]
; Root bootstrap plus a private version directory. Never wipe the root or
; previous versions: only logged and validated inventory-owned files are ours.
Source: "{#PublishSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Windows App SDK runtime installer
Source: "deps\windowsappruntimeinstall-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; AfterInstall: InstallWindowsAppRuntime

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Native invitation deep links emitted by the current Silo WebUI.
Root: HKA; Subkey: "Software\Classes\silo"; ValueType: string; ValueData: "URL:Silo invitation"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\silo"; ValueName: "URL Protocol"; ValueType: string; ValueData: ""
Root: HKA; Subkey: "Software\Classes\silo\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\silo\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
; Public Silo native OAuth PKCE callbacks return to the existing single-instance activation pipe.
Root: HKA; Subkey: "Software\Classes\org.siloserver.silo"; ValueType: string; ValueData: "URL:Silo sign-in"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\org.siloserver.silo"; ValueName: "URL Protocol"; ValueType: string; ValueData: ""
Root: HKA; Subkey: "Software\Classes\org.siloserver.silo\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\org.siloserver.silo\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
; Multiple same-name rules retain the old live client's authorization until
; successful activation. Uninstall deletes all entries with this exact name.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Silo Desktop SiloCast (Private LAN)"" dir=in action=allow program=""{app}\versions\{#MyAppFourPartVersion}\{#MyAppExeName}"" enable=yes profile=private remoteip=localsubnet"; Flags: runhidden waituntilterminated
; Make Inno's normal postinstall original-user launch policy explicit.
; Setup started already elevated cannot recover an unelevated caller token.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Run while the trusted bootstrap/runtime is still present. It validates each
; updater-owned inventory and retains foreign, modified or in-use files.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--silo-uninstall-owned"; Flags: runhidden waituntilterminated; RunOnceId: "SiloOwnedVersions"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Silo Desktop SiloCast (Private LAN)"""; Flags: runhidden waituntilterminated; RunOnceId: "SiloCastPrivateLanFirewall"

[Code]
function GuardFileAttributes(const Name: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function HasLinkedInstallPath(Dir: String): Boolean;
var Attributes, ErrorCode: Cardinal; Parent: String;
begin
  Result := False;
  Dir := RemoveBackslashUnlessRoot(ExpandFileName(Dir));
  repeat
    Attributes := GuardFileAttributes(Dir);
    if Attributes = $FFFFFFFF then
    begin
      // Setup makes its own API calls, so kernel32 GetLastError may already be
      // overwritten. DLLGetLastError holds the value captured after this call.
      ErrorCode := DLLGetLastError;
      if (ErrorCode <> 2) and (ErrorCode <> 3) then
      begin Result := True; Exit; end;
    end;
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then
    begin Result := True; Exit; end;
    Parent := ExtractFileDir(Dir);
    if CompareText(Parent, Dir) = 0 then Exit;
    Dir := Parent;
  until Dir = '';
end;

function SameInstallDirectory(const Left, Right: String): Boolean;
begin
  Result := (Left <> '') and (Right <> '') and
    (CompareText(RemoveBackslashUnlessRoot(ExpandFileName(Left)),
      RemoveBackslashUnlessRoot(ExpandFileName(Right))) = 0);
end;

function RegisteredSiloDirectory(const Dir: String): Boolean;
var Location: String;
begin
  Result := False;
  if not FileExists(AddBackslash(Dir) + '{#MyAppExeName}') then Exit;
  if RegQueryStringValue(HKLM64, '{#SiloUninstallKey}', 'InstallLocation', Location) then
    Result := SameInstallDirectory(Dir, Location);
  if not Result then
    if RegQueryStringValue(HKCU64, '{#SiloUninstallKey}', 'InstallLocation', Location) then
      Result := SameInstallDirectory(Dir, Location);
end;

function HasLinkedInstallContent(const Dir: String; Depth: Integer): Boolean;
var FindRec: TFindRec;
begin
  Result := True;
  if Depth > 32 then Exit;
  if not FindFirst(AddBackslash(Dir) + '*', FindRec) then Exit;
  Result := False;
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        if (FindRec.Attributes and $400) <> 0 then Result := True
        else if (FindRec.Attributes and $10) <> 0 then
          Result := HasLinkedInstallContent(AddBackslash(Dir) + FindRec.Name, Depth + 1);
      end;
    until Result or not FindNext(FindRec);
  finally FindClose(FindRec); end;
end;

// A generic uninstall-data or executable filename is not ownership proof.
// Only the registered Silo destination may receive clean-payload upgrades.
function DirectoryHasForeignContent(const Dir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(Dir) then Exit;
  if RegisteredSiloDirectory(Dir) then
  begin Result := HasLinkedInstallContent(Dir, 0); Exit; end;
  if FindFirst(AddBackslash(Dir) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
          Result := True;
      until Result or not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Runs for interactive and silent installs before any file is deleted.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Dir: String;
begin
  Result := '';
  Dir := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}')));
  if SameInstallDirectory(Dir, AddBackslash(ExtractFileDrive(Dir))) or
    SameInstallDirectory(Dir, ExpandConstant('{commonpf64}')) or
    SameInstallDirectory(Dir, ExpandConstant('{commonpf32}')) or
    SameInstallDirectory(Dir, GetEnv('USERPROFILE')) or
    SameInstallDirectory(Dir, ExpandConstant('{userdocs}')) or
    SameInstallDirectory(Dir, ExpandConstant('{userdesktop}')) or
    SameInstallDirectory(Dir, ExpandConstant('{userappdata}')) or
    SameInstallDirectory(Dir, ExpandConstant('{commonappdata}')) or
    SameInstallDirectory(Dir, ExpandConstant('{localappdata}')) or
    FileOrDirExists(AddBackslash(Dir) + '.git') or
    FileOrDirExists(AddBackslash(Dir) + '.codex-tmp') or
    (Pos(Lowercase(AddBackslash(ExpandConstant('{win}'))), Lowercase(AddBackslash(Dir))) = 1) or
    (Pos(Lowercase(AddBackslash(ExpandConstant('{localappdata}\SiloPlayer'))), Lowercase(AddBackslash(Dir))) = 1) or
    HasLinkedInstallPath(Dir) then
  begin Result := 'Choose a dedicated local folder, not a system, account, data or linked directory.'; Exit; end;
  if DirectoryHasForeignContent(Dir) then
    Result := 'The folder "' + Dir + '" is not a verified Silo installation or contains linked files. Choose a new or empty folder. Registered Silo upgrades retain previous versions and unrelated additions.';
end;

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

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  WizardForm.StatusLabel.Caption := 'Verifying the installed Silo version...';
  if not Exec(ExpandConstant('{app}\{#MyAppExeName}'),
              '--silo-initialize', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('The Silo installation verifier could not be started.');
  if ResultCode <> 0 then
    RaiseException('The installed Silo version could not be verified. The previous version has not been selected away. Run the installer again to repair it.');
end;

// Inno removes installed files from its own log. Do not recursively erase
// unrelated files that a user may later place in the application directory.
