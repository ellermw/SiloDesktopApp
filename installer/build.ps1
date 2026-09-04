# installer/build.ps1
# Builds the Silo Desktop Player installer.
# Prerequisites: dotnet SDK, Inno Setup 6
# Usage: pwsh -File installer/build.ps1

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishDirectory = "",
    [string]$SigningCertificateThumbprint = $env:SILO_SIGNING_CERT_THUMBPRINT,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [switch]$AllowSelfSignedSignatures
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$InstallerDir = $PSScriptRoot
$PublishDir = if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    "$InstallerDir\publish"
} elseif ([System.IO.Path]::IsPathRooted($PublishDirectory)) {
    $PublishDirectory
} else {
    Join-Path $RepoRoot $PublishDirectory
}
$OutputDir = "$InstallerDir\output"
$ProjectPath = "$RepoRoot\src\SiloPlayer\SiloPlayer.csproj"

$SigningCertificate = $null
if (-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
    $normalizedThumbprint = $SigningCertificateThumbprint.Replace(" ", "").ToUpperInvariant()
    $SigningCertificate = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Thumbprint -eq $normalizedThumbprint -and $_.HasPrivateKey } |
        Select-Object -First 1
    if (-not $SigningCertificate) {
        throw "The requested code-signing certificate was not found with a private key in Cert:\CurrentUser\My."
    }
    if ($SigningCertificate.NotAfter -le (Get-Date)) {
        throw "The requested code-signing certificate has expired."
    }
    $isSelfSigned = $SigningCertificate.Subject -eq $SigningCertificate.Issuer
    if ($isSelfSigned -and -not $AllowSelfSignedSignatures) {
        Write-Warning "Ignoring self-signed certificate $($SigningCertificate.Subject). Smart App Control evaluates these QA signatures below its required signing level and blocks files that launch successfully when unsigned. Pass -AllowSelfSignedSignatures only for a machine whose policy explicitly trusts that signer."
        $SigningCertificate = $null
    } else {
        Write-Host "Code signing enabled: $($SigningCertificate.Subject) [$($SigningCertificate.Thumbprint)]"
    }
}

function Set-LocalCodeSignature {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not $SigningCertificate) { return }
    $signature = Set-AuthenticodeSignature `
        -LiteralPath $Path `
        -Certificate $SigningCertificate `
        -HashAlgorithm SHA256 `
        -TimestampServer $TimestampServer
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        -not $signature.TimeStamperCertificate) {
        throw "Timestamp signing failed for '$Path': $($signature.Status) - $($signature.StatusMessage)"
    }
}

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
$WinAppSdkInstallerUri = "https://aka.ms/windowsappsdk/1.8/1.8.260317003/windowsappruntimeinstall-x64.exe"
$WinAppSdkInstallerSha256 = "8E21F22BF1191D7E347F6718BA8251D30B1E1C01775B9943C8E6239B7AF95EA7"
if (-not (Test-Path $WinAppSdkInstaller)) {
    Write-Host "Downloading pinned Windows App SDK 1.8.6 runtime installer..."
    $null = New-Item -ItemType Directory -Path "$InstallerDir\deps" -Force
    Invoke-WebRequest -Uri $WinAppSdkInstallerUri -OutFile $WinAppSdkInstaller
}
$WinAppSdkActualSha256 = (Get-FileHash -LiteralPath $WinAppSdkInstaller -Algorithm SHA256).Hash
if ($WinAppSdkActualSha256 -ne $WinAppSdkInstallerSha256) {
    throw "Windows App SDK installer hash mismatch. Expected $WinAppSdkInstallerSha256 but found $WinAppSdkActualSha256."
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
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed"
    exit 1
}

# A user-only desktop build must never package a removed administration,
# setup-wizard, impersonation, or writable marker-editor resource.
$ForbiddenPublishedResourcePatterns = @(
    'Admin',
    'SetupWizard',
    'Impersonation',
    'MarkerEditor'
)
$ForbiddenPublishedResources = Get-ChildItem -LiteralPath $PublishDir -Recurse -File |
    Where-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($PublishDir, $_.FullName)
        $ForbiddenPublishedResourcePatterns.Where({
            $relativePath -match [regex]::Escape($_)
        }).Count -gt 0
    }
if ($ForbiddenPublishedResources) {
    $names = ($ForbiddenPublishedResources | Select-Object -ExpandProperty FullName) -join "`n"
    throw "Forbidden user-only resources were found in the publish output:`n$names"
}

# Copy libmpv if not already in publish output
$MpvDll = "$PublishDir\libmpv-2.dll"
if (-not (Test-Path $MpvDll)) {
    Write-Host "Copying libmpv-2.dll..."
    Copy-Item "$RepoRoot\libs\mpv\libmpv-2.dll" $MpvDll
}

# A Git LFS pointer is a small text file that still satisfies MSBuild's content
# copy step. Publishing one produces a successful build whose player cannot start.
$MpvInfo = Get-Item -LiteralPath $MpvDll
if ($MpvInfo.Length -lt 10MB) {
    throw "Published libmpv-2.dll is only $($MpvInfo.Length) bytes. Run 'git lfs checkout -- libs/mpv/libmpv-2.dll' before packaging."
}
$MpvHeader = [System.IO.File]::ReadAllBytes($MpvDll)[0..1]
if ($MpvHeader[0] -ne 0x4D -or $MpvHeader[1] -ne 0x5A) {
    throw "Published libmpv-2.dll is not a Windows PE binary."
}
$SourceMpvHash = (Get-FileHash -LiteralPath "$RepoRoot\libs\mpv\libmpv-2.dll" -Algorithm SHA256).Hash
$PublishedMpvHash = (Get-FileHash -LiteralPath $MpvDll -Algorithm SHA256).Hash
if ($SourceMpvHash -ne $PublishedMpvHash) {
    throw "Published libmpv-2.dll does not match the verified repository asset."
}

if ($SigningCertificate) {
    Write-Host "=== Signing locally produced and unsigned application binaries ==="
    $PublishBinaries = Get-ChildItem $PublishDir -Recurse -File -Include *.exe,*.dll
    foreach ($binary in $PublishBinaries) {
        if ((Get-AuthenticodeSignature -LiteralPath $binary.FullName).Status -eq
            [System.Management.Automation.SignatureStatus]::NotSigned) {
            Set-LocalCodeSignature -Path $binary.FullName
            Write-Host "Signed $($binary.Name)"
        }
    }

    $unsignedPublishedBinaries = Get-ChildItem $PublishDir -Recurse -File -Include *.exe,*.dll |
        Where-Object {
            (Get-AuthenticodeSignature -LiteralPath $_.FullName).Status -ne
                [System.Management.Automation.SignatureStatus]::Valid
        }
    if ($unsignedPublishedBinaries) {
        $names = ($unsignedPublishedBinaries | Select-Object -ExpandProperty FullName) -join "`n"
        throw "One or more packaged application binaries are not validly signed:`n$names"
    }
    Write-Host "Verified all packaged application EXE/DLL files are signed."
}

# Prove the native loader can resolve the bundled library and its dependencies.
$EscapedMpvPath = $MpvDll.Replace('\', '\\')
$MpvSmokeSource = @"
using System;
using System.Runtime.InteropServices;
public static class SiloPlayerMpvPublishSmoke
{
    [DllImport("$EscapedMpvPath", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();
    [DllImport("$EscapedMpvPath", CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_destroy(IntPtr context);
}
"@
Add-Type -TypeDefinition $MpvSmokeSource
$MpvContext = [SiloPlayerMpvPublishSmoke]::mpv_create()
if ($MpvContext -eq [IntPtr]::Zero) {
    throw "Published libmpv loaded, but mpv_create returned a null context."
}
[SiloPlayerMpvPublishSmoke]::mpv_destroy($MpvContext)
Write-Host "Verified libmpv binary, hash, and mpv_create smoke test."

# Ensure Assets\app.ico is in publish output
$IconDest = "$PublishDir\Assets\app.ico"
if (-not (Test-Path $IconDest)) {
    $null = New-Item -ItemType Directory -Path "$PublishDir\Assets" -Force
    Copy-Item "$RepoRoot\src\SiloPlayer\Assets\app.ico" $IconDest
}

Write-Host "=== Compiling installer ==="
if (-not (Test-Path $OutputDir)) { $null = New-Item -ItemType Directory -Path $OutputDir }

$IsccArguments = @("/DPublishSourceDir=$PublishDir")
if ($SigningCertificate) {
    $InnoSignScript = Join-Path $InstallerDir "sign-file.ps1"
    # Reuse the exact PowerShell host that successfully signed the payload.
    # This also avoids Inno's 32-bit process resolving a different PowerShell
    # installation with an incompatible module search path.
    $InnoPowerShell = (Get-Process -Id $PID).Path
    # ISCC parses the /S value before invoking the sign tool. Inno's $q token
    # survives that parsing and becomes a literal quote for paths/values that
    # may contain spaces. Inno expands $f to the file being signed.
    $InnoSignCommand = "`$q$InnoPowerShell`$q -NoProfile -ExecutionPolicy Bypass -File `$q$InnoSignScript`$q -Path `$f -Thumbprint $($SigningCertificate.Thumbprint) -TimestampServer `$q$TimestampServer`$q"
    $IsccArguments += "/DLocalSigning=1"
    $IsccArguments += "/Slocaltesting=$InnoSignCommand"
}
$IsccArguments += "$InstallerDir\SiloInstaller.iss"

& $IsccPath $IsccArguments

if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed"
    exit 1
}

# Inno Setup writes a versioned filename (SiloInstaller-{version}-Setup.exe).
# Pick the most recent one from the output directory.
$SetupExe = Get-ChildItem "$OutputDir\SiloInstaller-*-Setup.exe" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($SetupExe) {
    if ($SigningCertificate) {
        $setupSignature = Get-AuthenticodeSignature -LiteralPath $SetupExe.FullName
        if ($setupSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
            -not $setupSignature.TimeStamperCertificate) {
            Set-LocalCodeSignature -Path $SetupExe.FullName
            $setupSignature = Get-AuthenticodeSignature -LiteralPath $SetupExe.FullName
        }
        if ($setupSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
            -not $setupSignature.TimeStamperCertificate) {
            throw "Installer signature validation failed: a valid timestamped signature was not present."
        }
        Write-Host "Verified timestamp-signed installer with $($SigningCertificate.Subject)."
    }

    # GitHub publishes a stable asset name. Always refresh it from the installer
    # produced by this invocation so a release can never upload an older build.
    $StableSetupExe = Join-Path $OutputDir "SiloInstaller-Windows-x64.exe"
    Copy-Item -LiteralPath $SetupExe.FullName -Destination $StableSetupExe -Force
    $VersionedHash = (Get-FileHash -LiteralPath $SetupExe.FullName -Algorithm SHA256).Hash
    $StableHash = (Get-FileHash -LiteralPath $StableSetupExe -Algorithm SHA256).Hash
    if ($VersionedHash -ne $StableHash) {
        Write-Error "Stable installer alias does not match the versioned build"
        exit 1
    }

    $SizeMB = [math]::Round($SetupExe.Length / 1MB, 1)
    Write-Host ""
    Write-Host "=== Installer built successfully ==="
    Write-Host "  Output: $($SetupExe.FullName)"
    Write-Host "  Stable: $StableSetupExe"
    Write-Host "  SHA256: $VersionedHash"
    Write-Host "  Size:   $SizeMB MB"
} else {
    Write-Error "Installer output not found"
    exit 1
}
