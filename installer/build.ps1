# installer/build.ps1
# Builds the Silo Desktop Player installer.
# Prerequisites: dotnet SDK, Inno Setup 6
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

# Find ISCC.exe (check multiple locations)
$IsccPaths = @(
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$IsccPath = $null
foreach ($p in $IsccPaths) {
    if (Test-Path $p) { $IsccPath = $p; break }
}
if (-not $IsccPath) {
    Write-Error "Inno Setup not found. Install with: winget install JRSoftware.InnoSetup"
    exit 1
}
Write-Host "Using ISCC: $IsccPath"

# Check Windows App SDK runtime installer
$WinAppSdkInstaller = "$InstallerDir\deps\windowsappruntimeinstall-x64.exe"
if (-not (Test-Path $WinAppSdkInstaller)) {
    Write-Host "Downloading Windows App SDK runtime installer..."
    $null = New-Item -ItemType Directory -Path "$InstallerDir\deps" -Force
    Invoke-WebRequest -Uri "https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe" -OutFile $WinAppSdkInstaller
}

Write-Host "=== Publishing app ($Configuration, $Runtime) ==="
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }

# Clean obj/{Release,x64/Release} and bin/{Release,x64/Release} for the app project
# to force every .xbf to recompile against the current XamlTypeInfo.g.cs. WinUI 3's
# incremental build does NOT track XamlTypeInfo dependencies — when that file is
# regenerated and type indices shift, stale XBFs reference old indices and crash at
# runtime with cryptic errors like "Failed to assign to property RangeBase.Value".
# Note: the installer publish uses -p:Platform=x64 which puts output under
# obj/x64/Release, not obj/Release, so we must clean BOTH paths.
$AppProjectDir = Split-Path -Parent $ProjectPath
$CleanPaths = @(
    (Join-Path $AppProjectDir "obj\$Configuration"),
    (Join-Path $AppProjectDir "obj\x64\$Configuration"),
    (Join-Path $AppProjectDir "bin\$Configuration"),
    (Join-Path $AppProjectDir "bin\x64\$Configuration")
)
foreach ($p in $CleanPaths) {
    if (Test-Path $p) {
        Write-Host "Cleaning $p"
        Remove-Item -Recurse -Force $p
    }
}

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

# Inno Setup writes a versioned filename (SiloDesktopPlayer-{version}-Setup.exe).
# Pick the most recent one from the output directory.
$SetupExe = Get-ChildItem "$OutputDir\SiloDesktopPlayer-*-Setup.exe" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
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
