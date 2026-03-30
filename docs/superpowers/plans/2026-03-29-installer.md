# Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a single-file `.exe` installer for Continuum Desktop Player using Inno Setup, with bundled .NET runtime and Windows App SDK.

**Architecture:** `dotnet publish` produces a self-contained x64 build, `build.ps1` copies dependencies and invokes Inno Setup to compile everything into one setup exe. The installer handles destination selection, desktop shortcut, and silent Windows App SDK runtime install.

**Tech Stack:** Inno Setup 6, PowerShell, dotnet publish (self-contained x64)

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `installer/ContinuumDesktopPlayer.iss` | Create | Inno Setup script defining installer behavior |
| `installer/build.ps1` | Create | Build automation: publish, copy deps, compile installer |
| `installer/deps/` | Create dir | Holds Windows App SDK runtime installer |
| `.gitignore` | Modify | Ignore installer output and deps |

---

### Task 1: Install Inno Setup and Download Windows App SDK Runtime

**Files:**
- Create: `installer/deps/` (directory)
- Modify: `.gitignore`

- [ ] **Step 1: Install Inno Setup via winget**

Run:
```bash
winget install JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
```

Expected: Inno Setup 6 installed. Verify with:
```bash
ls "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" 2>/dev/null && echo "ISCC found"
```

- [ ] **Step 2: Create installer/deps directory**

```bash
mkdir -p F:/ContinuumPlayer/installer/deps
```

- [ ] **Step 3: Download Windows App SDK runtime installer**

```bash
curl -L -o F:/ContinuumPlayer/installer/deps/windowsappruntimeinstall-x64.exe "https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe"
```

Verify it downloaded:
```bash
ls -la F:/ContinuumPlayer/installer/deps/windowsappruntimeinstall-x64.exe
```

Expected: File exists, ~5-60MB.

- [ ] **Step 4: Update .gitignore**

Add to the end of `.gitignore`:

```
# Installer
installer/deps/
installer/output/
```

- [ ] **Step 5: Commit**

```bash
git add .gitignore
git commit -m "chore: add installer gitignore rules, create deps directory"
```

Note: `installer/deps/` contains a large binary and is gitignored. Do NOT commit it.

---

### Task 2: Create the Inno Setup Script

The complete `.iss` file that defines the installer: app metadata, files, shortcuts, dependency install, and uninstaller.

**Files:**
- Create: `installer/ContinuumDesktopPlayer.iss`

- [ ] **Step 1: Create the Inno Setup script**

```iss
; installer/ContinuumDesktopPlayer.iss
; Inno Setup script for Continuum Desktop Player

#define MyAppName "Continuum Desktop Player"
#define MyAppVersion "0.0.2"
#define MyAppPublisher "Continuum"
#define MyAppExeName "ContinuumPlayer.exe"

[Setup]
AppId={{B8E2F4A1-3C5D-4E6F-9A1B-2D3E4F5A6B7C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={commonpf32}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=output
OutputBaseFilename=ContinuumDesktopPlayer-Setup
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
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Install Windows App SDK runtime silently before launching
Filename: "{tmp}\windowsappruntimeinstall-x64.exe"; Parameters: "--quiet"; StatusMsg: "Installing Windows App SDK runtime..."; Flags: waituntilterminated runhidden

; Launch app after install
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
```

- [ ] **Step 2: Commit**

```bash
git add installer/ContinuumDesktopPlayer.iss
git commit -m "feat: add Inno Setup script for installer"
```

---

### Task 3: Create the Build Script

PowerShell script that publishes the app, copies dependencies, and compiles the installer.

**Files:**
- Create: `installer/build.ps1`

- [ ] **Step 1: Create build.ps1**

```powershell
# installer/build.ps1
# Builds the Continuum Desktop Player installer.
# Prerequisites: dotnet SDK, Inno Setup 6 installed via winget
# Usage: pwsh -File installer/build.ps1

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$InstallerDir = $PSScriptRoot
$PublishDir = "$InstallerDir\publish"
$OutputDir = "$InstallerDir\output"
$ProjectPath = "$RepoRoot\src\ContinuumPlayer\ContinuumPlayer.csproj"

# Find ISCC.exe
$IsccPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $IsccPath)) {
    $IsccPath = "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $IsccPath)) {
    Write-Error "Inno Setup not found. Install with: winget install JRSoftware.InnoSetup"
    exit 1
}

# Check Windows App SDK runtime installer
$WinAppSdkInstaller = "$InstallerDir\deps\windowsappruntimeinstall-x64.exe"
if (-not (Test-Path $WinAppSdkInstaller)) {
    Write-Host "Downloading Windows App SDK runtime installer..."
    $null = New-Item -ItemType Directory -Path "$InstallerDir\deps" -Force
    Invoke-WebRequest -Uri "https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe" -OutFile $WinAppSdkInstaller
}

Write-Host "=== Publishing app ($Configuration, $Runtime) ==="
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }

dotnet publish $ProjectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained `
    -p:Platform=x64 `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed"
    exit 1
}

# Copy libmpv if not already in publish output
$MpvDll = "$PublishDir\libmpv-2.dll"
if (-not (Test-Path $MpvDll)) {
    Write-Host "Copying libmpv-2.dll..."
    Copy-Item "$RepoRoot\libs\mpv\libmpv-2.dll" $MpvDll
}

# Ensure Assets\app.ico is in publish output
$IconDest = "$PublishDir\Assets\app.ico"
if (-not (Test-Path $IconDest)) {
    $null = New-Item -ItemType Directory -Path "$PublishDir\Assets" -Force
    Copy-Item "$RepoRoot\src\ContinuumPlayer\Assets\app.ico" $IconDest
}

Write-Host "=== Compiling installer ==="
if (-not (Test-Path $OutputDir)) { $null = New-Item -ItemType Directory -Path $OutputDir }

& $IsccPath "$InstallerDir\ContinuumDesktopPlayer.iss"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed"
    exit 1
}

$SetupExe = Get-ChildItem "$OutputDir\ContinuumDesktopPlayer-Setup.exe" -ErrorAction SilentlyContinue
if ($SetupExe) {
    $SizeMB = [math]::Round($SetupExe.Length / 1MB, 1)
    Write-Host ""
    Write-Host "=== Installer built successfully ==="
    Write-Host "  Output: $($SetupExe.FullName)"
    Write-Host "  Size:   $SizeMB MB"
} else {
    Write-Error "Installer output not found"
    exit 1
}
```

- [ ] **Step 2: Commit**

```bash
git add installer/build.ps1
git commit -m "feat: add build script for publishing and compiling installer"
```

---

### Task 4: Build the Installer and Verify

Run the full pipeline and verify the output.

**Files:** None (verification only)

- [ ] **Step 1: Run the build script**

```bash
cd F:/ContinuumPlayer && pwsh -File installer/build.ps1
```

Expected output:
```
=== Publishing app (Release, win-x64) ===
...
=== Compiling installer ===
...
=== Installer built successfully ===
  Output: F:\ContinuumPlayer\installer\output\ContinuumDesktopPlayer-Setup.exe
  Size:   XX.X MB
```

- [ ] **Step 2: Verify the installer exists**

```bash
ls -la F:/ContinuumPlayer/installer/output/ContinuumDesktopPlayer-Setup.exe
```

Expected: File exists, likely 80-150MB (self-contained .NET + app + mpv + WinAppSDK runtime).

- [ ] **Step 3: Test the installer**

Run the setup exe manually:
1. Double-click `ContinuumDesktopPlayer-Setup.exe`
2. Verify: Welcome page shows "Continuum Desktop Player" with app icon
3. Verify: Default install path is `C:\Program Files (x86)\Continuum Desktop Player`
4. Verify: "Create desktop shortcut" checkbox is present and checked
5. Click Install, verify progress completes
6. Verify: Windows App SDK runtime installs silently
7. Verify: "Launch Continuum Desktop Player" option on final page
8. Launch and verify the app works from the installed location

- [ ] **Step 4: Test uninstall**

1. Open Settings > Apps > Installed Apps
2. Find "Continuum Desktop Player"
3. Uninstall
4. Verify the install directory is removed
5. Verify desktop shortcut is removed

- [ ] **Step 5: Commit and push**

```bash
git add -A
git commit -m "feat: complete installer pipeline — publish, bundle, and Inno Setup compilation"
git push
```

---

## Summary

| Task | What it does |
|------|-------------|
| 1 | Install Inno Setup, download WinAppSDK runtime, update gitignore |
| 2 | Inno Setup script — app metadata, files, shortcuts, dependency install |
| 3 | PowerShell build script — publish + copy deps + compile installer |
| 4 | Build, verify, test install/uninstall |
