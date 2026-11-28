; installer/SiloInstaller.iss
; Inno Setup script for Silo Desktop Player

#define MyAppName "Silo Desktop Player"
#ifndef MyAppVersion
#define MyAppVersion "1.2.1797-beta.1"
#endif
#define MyAppPublisher "Silo"
#define MyAppExeName "SiloPlayer.exe"
#ifndef MyAppFourPartVersion
#define MyAppFourPartVersion "1.2.1796.1"
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
VersionInfoVersion={#MyAppFourPartVersion}
VersionInfoProductVersion={#MyAppFourPartVersion}
VersionInfoProductTextVersion={#MyAppVersion}
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
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "Silo.DesktopPlayer"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; AppUserModelID: "Silo.DesktopPlayer"; Tasks: desktopicon

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
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Silo Desktop SiloCast (Private LAN)"""; Flags: runhidden waituntilterminated; RunOnceId: "SiloCastPrivateLanFirewall"

[Code]
const
  UninstallCheckBusy = 2; // --silo-check-uninstall exit code in the launcher
var
  RunOwnedUninstall: Boolean;

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

// A system, account, data, repository or linked folder is never installed into or emptied.
function UnsafeInstallDirectory(const Dir: String): Boolean;
begin
  Result := SameInstallDirectory(Dir, AddBackslash(ExtractFileDrive(Dir))) or
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
    HasLinkedInstallPath(Dir);
end;

function DirectoryHasFiles(const Dir: String; Depth: Integer): Boolean;
var FindRec: TFindRec;
begin
  Result := Depth > 32;
  if Result or not FindFirst(AddBackslash(Dir) + '*', FindRec) then Exit;
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        if (FindRec.Attributes and $10) = 0 then Result := True
        else Result := DirectoryHasFiles(AddBackslash(Dir) + FindRec.Name, Depth + 1);
      end;
    until Result or not FindNext(FindRec);
  finally FindClose(FindRec); end;
end;

// What an uninstall, including an older uninstaller, can leave of a Silo folder: the updater's
// private .silo-update control folder (the marker) and versions folder, and empty folders (an
// upgrade's overwritten uninstall log never lists folders that already existed). Files in
// .silo-update or versions count only beside the marker. Any other file, or a link anywhere,
// is not proven to be Silo's.
function SiloLeftoverDirectory(const Dir: String): Boolean;
var FindRec: TFindRec; Foreign, Marker, Reserved: Boolean;
begin
  Result := False;
  if HasLinkedInstallContent(Dir, 0) then Exit;
  Marker := DirExists(AddBackslash(Dir) + '.silo-update');
  Foreign := False;
  if FindFirst(AddBackslash(Dir) + '*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Reserved := Marker and ((CompareText(FindRec.Name, '.silo-update') = 0) or (CompareText(FindRec.Name, 'versions') = 0));
        if (FindRec.Attributes and $10) = 0 then Foreign := True
        else if not Reserved then Foreign := DirectoryHasFiles(AddBackslash(Dir) + FindRec.Name, 0);
      end;
    until Foreign or not FindNext(FindRec);
  finally FindClose(FindRec); end;
  Result := not Foreign;
end;

function PendingOperationsTouchDirectory(const Dir, Operations: String): Boolean;
var Path, Entries: String;
begin
  // MoveFileEx records NT paths in a multi-string. A rename destination can have a leading !.
  Path := '\??\' + Lowercase(Dir);
  Entries := #0 + Lowercase(Operations) + #0;
  Result := (Pos(#0 + Path + #0, Entries) > 0) or (Pos(#0 + Path + '\', Entries) > 0) or
    (Pos(#0 + '!' + Path + #0, Entries) > 0) or (Pos(#0 + '!' + Path + '\', Entries) > 0);
end;

function HasPendingInstallOperations(const Dir: String): Boolean;
var Operations: String;
begin
  Result := RegQueryMultiStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager',
    'PendingFileRenameOperations', Operations) and PendingOperationsTouchDirectory(Dir, Operations);
end;

// Runs for interactive and silent installs before any file is deleted.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Dir: String;
begin
  Result := '';
  Dir := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}')));
  if UnsafeInstallDirectory(Dir) then
  begin Result := 'Choose a dedicated local folder, not a system, account, data or linked directory.'; Exit; end;
  // Do not copy new files to paths Windows is already scheduled to delete at reboot.
  if HasPendingInstallOperations(Dir) then
  begin
    NeedsRestart := True;
    Result := 'Windows has pending file operations in "' + Dir + '". Restart Windows before installing Silo here.';
    Exit;
  end;
  if DirectoryHasForeignContent(Dir) then
  begin
    // Leftovers of an earlier Silo install are Silo's own: remove them and install as into an empty folder.
    if SiloLeftoverDirectory(Dir) then
    begin
      DelTree(AddBackslash(Dir) + 'versions', True, True, True);
      // Keep the ownership marker if the version tree is still locked, so Setup can retry later.
      if not DirExists(AddBackslash(Dir) + 'versions') then
        DelTree(AddBackslash(Dir) + '.silo-update', True, True, True);
      if DirExists(AddBackslash(Dir) + '.silo-update') or DirExists(AddBackslash(Dir) + 'versions') then
        Result := 'Files left by an earlier Silo installation in "' + Dir + '" are still in use. Close Silo or restart Windows, then run Setup again.';
      Exit;
    end;
    Result := 'The folder "' + Dir + '" is not a verified Silo installation or contains linked files. Choose a new or empty folder. Registered Silo upgrades retain previous versions and unrelated additions.';
  end;
end;

// Another program's Inno uninstaller in the same folder: the folder is shared, so keep what is left.
function SharedWithAnotherUninstaller(const Dir: String): Boolean;
var FindRec: TFindRec; Own: String;
begin
  Result := False;
  Own := ChangeFileExt(ExtractFileName(ExpandConstant('{uninstallexe}')), '');
  if FindFirst(AddBackslash(Dir) + 'unins???.*', FindRec) then
  try
    repeat
      Result := CompareText(ChangeFileExt(FindRec.Name, ''), Own) <> 0;
    until Result or not FindNext(FindRec);
  finally FindClose(FindRec); end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Dir, Launcher: String; ResultCode: Integer;
begin
  Dir := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}')));
  if (CurUninstallStep = usUninstall) and RunOwnedUninstall then
  begin
    // The confirmed uninstall has started, but the launcher and registration are still present.
    // A broken launcher must not be retried by an unconditional [UninstallRun] entry.
    Launcher := AddBackslash(Dir) + '{#MyAppExeName}';
    if not UnsafeInstallDirectory(Dir) and not HasLinkedInstallPath(Launcher) then
    begin
      if not Exec(Launcher, '--silo-uninstall-owned', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        Log(Format('Silo uninstall cleanup could not start (Windows error %d).', [ResultCode]))
      else if ResultCode <> 0 then
        Log(Format('Silo uninstall cleanup returned exit code %d.', [ResultCode]));
    end;
  end;
  // Inno has removed its logged files. Sweep only a dedicated folder; DelTree removes links
  // as links and does not follow them.
  if (CurUninstallStep = usPostUninstall) and DirExists(Dir) and
     not UnsafeInstallDirectory(Dir) and not SharedWithAnotherUninstaller(Dir) then
    DelTree(Dir, True, True, True);
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

function InitializeUninstall: Boolean;
var Dir, Launcher: String; ResultCode: Integer;
begin
  Dir := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}')));
  Launcher := AddBackslash(Dir) + '{#MyAppExeName}';
  Result := True;
  if UnsafeInstallDirectory(Dir) or HasLinkedInstallPath(Launcher) then
  begin
    // Do not run an elevated launcher or delete logged paths at an unsafe destination.
    Log('Uninstall blocked: the Silo installation folder or launcher is unsafe, linked, or inaccessible: ' + Dir);
    SuppressibleMsgBox('Silo cannot uninstall safely from "' + Dir + '". The installation folder or launcher is linked, inaccessible, or not in a dedicated application folder.', mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
  // Read-only check before Inno commits to removing anything. Do not force-close the client.
  if not FileExists(Launcher) then
    Log('Silo launcher is missing; continuing uninstall without launcher cleanup.')
  else if not Exec(Launcher, '--silo-check-uninstall', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log(Format('Silo uninstall preflight could not start (Windows error %d); continuing without launcher cleanup.', [ResultCode]))
  else if ResultCode = UninstallCheckBusy then
  begin
    Log('Uninstall blocked: Silo is running or an update is in progress.');
    SuppressibleMsgBox('Close Silo and let any update finish, then run uninstall again.', mbError, MB_OK, IDOK);
    Result := False;
  end
  else if ResultCode = 0 then
    RunOwnedUninstall := True
  else
    Log(Format('Silo uninstall preflight returned exit code %d; continuing without launcher cleanup.', [ResultCode]));
end;

function UninstallNeedRestart: Boolean;
begin
  // The launcher status does not indicate a reboot. Check what Windows actually queued.
  Result := HasPendingInstallOperations(RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}'))));
end;

// Inno removes installed files from its own log. CurUninstallStepChanged removes
// the rest of a dedicated, unshared Silo folder; upgrades keep unrelated additions.
