# Continuum Desktop Player Installer Design Spec

## Goal

Create a single-file `.exe` installer that installs the app, its dependencies, and optionally creates a desktop shortcut. Users download one file and run it.

## Tool

**Inno Setup** -- produces a single `.exe` installer. Lightweight, well-established, handles custom install paths, shortcuts, dependency installation, and uninstaller registration.

## Build Pipeline

1. `dotnet publish` as self-contained x64 with Windows App SDK
2. Bundle `libmpv-2.dll` from `libs/mpv/`
3. Bundle Windows App SDK runtime installer in `installer/deps/`
4. Inno Setup script compiles everything into `ContinuumDesktopPlayer-Setup.exe`

### Publish Command

```
dotnet publish src/ContinuumPlayer/ContinuumPlayer.csproj -c Release -r win-x64 --self-contained -p:Platform=x64
```

This produces a self-contained deployment in `src/ContinuumPlayer/bin/Release/net8.0-windows10.0.22621.0/win-x64/publish/` with the .NET 8 runtime bundled.

## Installer Flow

1. **Welcome page** -- app icon, "Continuum Desktop Player Setup"
2. **Destination picker** -- default: `C:\Program Files (x86)\Continuum Desktop Player`, user can change
3. **Options** -- "Create desktop shortcut?" checkbox (default: checked)
4. **Progress** -- installs files, silently runs Windows App SDK runtime if needed
5. **Done** -- "Launch Continuum Desktop Player" checkbox, Finish button

## What Gets Installed

| Item | Location |
|------|----------|
| Published app files | `{install_dir}\` |
| `libmpv-2.dll` | `{install_dir}\` |
| Desktop shortcut | `{userdesktop}\Continuum Desktop Player.lnk` (optional) |
| Start Menu shortcut | `{group}\Continuum Desktop Player.lnk` |
| Uninstaller | Registered in Add/Remove Programs |

## Dependencies

### .NET 8 Runtime
Bundled via `--self-contained` publish. No separate install needed.

### Windows App SDK Runtime
The `WindowsAppRuntimeInstall.exe` is bundled in the installer. Inno Setup runs it silently (`--quiet`) during install. If already installed, the runtime installer exits immediately with no effect.

Download source: `https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe`

Stored at: `installer/deps/windowsappruntimeinstall-x64.exe`

### libmpv
`libs/mpv/libmpv-2.dll` is copied to the install directory alongside the app exe.

## File Structure

```
installer/
  ContinuumDesktopPlayer.iss    -- Inno Setup script
  deps/
    windowsappruntimeinstall-x64.exe  -- Windows App SDK runtime
  build.ps1                     -- PowerShell script: publish + compile installer
```

## Installer Metadata

| Field | Value |
|-------|-------|
| App Name | Continuum Desktop Player |
| App Version | (read from project) |
| Default Dir | `{commonpf32}\Continuum Desktop Player` |
| Publisher | Continuum |
| Exe Name | `ContinuumPlayer.exe` |
| Setup Exe | `ContinuumDesktopPlayer-Setup.exe` |
| Uninstall Display Icon | `{app}\Assets\app.ico` |
| Architecture | x64 |
| Privileges | admin (for Program Files install) |

## Build Script (build.ps1)

Single PowerShell script that:
1. Runs `dotnet publish` with Release config, self-contained, x64
2. Copies `libmpv-2.dll` into the publish output if not already there
3. Invokes Inno Setup compiler (`iscc.exe`) on the `.iss` script
4. Outputs `ContinuumDesktopPlayer-Setup.exe` to `installer/output/`

## Uninstaller

Inno Setup automatically generates an uninstaller that:
- Removes all installed files
- Removes desktop and Start Menu shortcuts
- Removes the install directory
- Removes the Add/Remove Programs entry
- Does NOT remove user data (`%LocalAppData%\ContinuumPlayer\`)
