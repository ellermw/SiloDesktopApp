# installer/build.ps1
# Builds the Silo Desktop Player installer.
# Prerequisites: dotnet SDK, Inno Setup 6
# Usage: pwsh -File installer/build.ps1
# PublishDirectory is the fresh installation-layout root, with the client under
# versions/<four-part-version>/ and its updater host under Updater/. Use a fresh
# InstallerOutputDirectory to preserve a prior attempt at the same version.

param(
    [ValidateSet('Debug','Release')][string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishDirectory = "",
    [string]$InstallerOutputDirectory = "",
    [string]$SigningCertificateThumbprint = $env:SILO_SIGNING_CERT_THUMBPRINT,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [switch]$AllowSelfSignedSignatures
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$InstallerDir = $PSScriptRoot
$PublishDir = if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    Join-Path $InstallerDir ('publish-' + [Guid]::NewGuid().ToString('N'))
} elseif ([System.IO.Path]::IsPathRooted($PublishDirectory)) {
    $PublishDirectory
} else {
    Join-Path $RepoRoot $PublishDirectory
}
$OutputDir = "$InstallerDir\output"
$ProjectPath = "$RepoRoot\src\SiloPlayer\SiloPlayer.csproj"
$HostProjectPath = "$RepoRoot\src\SiloPlayer.UpdateHost\SiloPlayer.UpdateHost.csproj"
$LauncherProjectPath = "$RepoRoot\src\SiloPlayer.Launcher\SiloPlayer.Launcher.csproj"

function Assert-NoLinkedPublishPath {
    param([Parameter(Mandatory=$true)][string]$Path)
    $current=[IO.Path]::GetFullPath($Path)
    while($current) {
        try {
            if(([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a linked build path: '$current'."
            }
        } catch [IO.FileNotFoundException] { } catch [IO.DirectoryNotFoundException] { }
        $current=[IO.Path]::GetDirectoryName($current)
    }
}

function Get-DedicatedPublishDirectory {
    param([Parameter(Mandatory=$true)][string]$Path,[Parameter(Mandatory=$true)][string]$RepositoryRoot)
    $fullPath=[IO.Path]::GetFullPath($Path)
    Assert-NoLinkedPublishPath $fullPath
    $trimmed=$fullPath.TrimEnd('\','/')
    $repository=[IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\','/')
    $separator=[IO.Path]::DirectorySeparatorChar
    if([string]::IsNullOrEmpty($trimmed) -or
        $trimmed -eq [IO.Path]::GetPathRoot($fullPath).TrimEnd('\','/') -or
        ($repository+$separator).StartsWith($trimmed+$separator,[StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing a publish directory that is a filesystem root or contains the repository."
    }
    $protected=@([Environment]::GetFolderPath('Windows'),[Environment]::GetFolderPath('ProgramFiles'),
        [Environment]::GetFolderPath('ProgramFilesX86'),(Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SiloPlayer'))
    foreach($location in $protected) {
        if([string]::IsNullOrWhiteSpace($location)){continue}
        $location=[IO.Path]::GetFullPath($location).TrimEnd('\','/')
        if($trimmed.Equals($location,[StringComparison]::OrdinalIgnoreCase) -or
            ($trimmed+$separator).StartsWith($location+$separator,[StringComparison]::OrdinalIgnoreCase)) {
            throw 'System, installed-application and account-data directories cannot be build outputs.'
        }
    }
    if([IO.File]::Exists($fullPath) -or
        ((Test-Path -LiteralPath $fullPath) -and (Get-ChildItem -LiteralPath $fullPath -Force|Select-Object -First 1))) {
        throw "Refusing to overwrite populated output '$fullPath'. Existing files, installed players and frozen publishes are preserved. Choose a new or empty folder."
    }
    return $fullPath
}
$PublishDir=Get-DedicatedPublishDirectory -Path $PublishDir -RepositoryRoot $RepoRoot
$PublishRoot=$PublishDir
if (-not [string]::IsNullOrWhiteSpace($InstallerOutputDirectory)) {
    $OutputDir=Get-DedicatedPublishDirectory -Path $InstallerOutputDirectory -RepositoryRoot $RepoRoot
}

# Reject mismatched stamps before dependency downloads, native loading or builds.
function Get-StampedAppVersion {
    param([Parameter(Mandatory=$true)][string]$Project,[Parameter(Mandatory=$true)][string]$InstallerSource)
    $xml=[xml](Get-Content -LiteralPath $Project -Raw)
    $version=@($xml.Project.PropertyGroup.Version | Where-Object { $_ })
    $fileVersion=@($xml.Project.PropertyGroup.FileVersion | Where-Object { $_ })
    $assemblyVersion=@($xml.Project.PropertyGroup.AssemblyVersion | Where-Object { $_ })
    if ($version.Count -ne 1 -or $version[0] -notmatch '^\d+\.\d+\.\d+$' -or
        $fileVersion.Count -ne 1 -or $assemblyVersion.Count -ne 1 -or
        $fileVersion[0] -ne ($version[0]+'.0') -or $assemblyVersion[0] -ne ($version[0]+'.0')) {
        throw 'Version, FileVersion and AssemblyVersion must be deliberately stamped together.'
    }
    $innoVersion=[regex]::Match([IO.File]::ReadAllText($InstallerSource),'#define MyAppVersion "([^"]+)"').Groups[1].Value
    if ($innoVersion -ne $version[0]) { throw 'The Inno version must match all app and helper versions before building.' }
    return [string]$version[0]
}
$AppVersion=Get-StampedAppVersion -Project $ProjectPath -InstallerSource "$InstallerDir\SiloInstaller.iss"
foreach($helper in @($HostProjectPath,$LauncherProjectPath)) {
    $helperVersion=Get-StampedAppVersion -Project $helper -InstallerSource "$InstallerDir\SiloInstaller.iss"
    if ($helperVersion -ne $AppVersion) { throw 'The client, updater and launcher must have matching version stamps.' }
}
$FourPartVersion=$AppVersion+'.0'
$ExpectedSetup=Join-Path $OutputDir "SiloInstaller-$AppVersion-Setup.exe"
if (Test-Path -LiteralPath $ExpectedSetup) { throw 'Preserve the existing versioned installer. Choose a fresh InstallerOutputDirectory.' }
$UpdateAssetDir=Get-DedicatedPublishDirectory -Path (Join-Path $OutputDir "update-$AppVersion") -RepositoryRoot $RepoRoot
$PublishDir=Join-Path $PublishRoot "versions\$FourPartVersion"

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
# Publishing starts from new/empty output; previous payloads are never deleted.
$BuildMutex=New-Object Threading.Mutex($false,'SiloOct18Build')
$BuildMutexOwned=$false
try {
try { $null=$BuildMutex.WaitOne() } catch [Threading.AbandonedMutexException] { }
$BuildMutexOwned=$true

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
    $clean=[IO.Path]::GetFullPath($p)
    if(!$clean.StartsWith([IO.Path]::GetFullPath($AppProjectDir)+'\',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Generated cleanup path escaped the application project.'
    }
    Assert-NoLinkedPublishPath $clean
    if(Test-Path -LiteralPath $clean) {
        $pending=New-Object 'Collections.Generic.Stack[string]'
        $pending.Push($clean)
        while($pending.Count) {
            foreach($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Generated cleanup contains a linked entry.'}
                if($item.PSIsContainer){$pending.Push($item.FullName)}
            }
        }
    }
}
foreach ($p in $CleanPaths) {
    if (Test-Path -LiteralPath $p) {
        Write-Host "Cleaning $p"
        Remove-Item -LiteralPath $p -Recurse -Force
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

# The host has its own WinUI PRI and self-contained runtime. Keeping it below
# Updater avoids resource-name and assembly collisions with the running client.
dotnet publish $HostProjectPath -c $Configuration -r $Runtime --self-contained `
    -p:Platform=x64 -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false `
    -p:Version=$AppVersion -p:FileVersion=$FourPartVersion -p:AssemblyVersion=$FourPartVersion `
    -o (Join-Path $PublishDir 'Updater')
if ($LASTEXITCODE -ne 0) { throw 'The finite updater host could not be published.' }

# Only the stable launcher/runtime is shared. Protocol1 updates leave these
# bootstrap files immutable; a future bootstrap change needs a full installer.
dotnet publish $LauncherProjectPath -c $Configuration -r $Runtime --self-contained `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false `
    -p:Version=$AppVersion -p:FileVersion=$FourPartVersion -p:AssemblyVersion=$FourPartVersion `
    -o $PublishRoot
if ($LASTEXITCODE -ne 0) { throw 'The stable launcher could not be published.' }
$LauncherAppHost=Join-Path $PublishRoot 'SiloPlayer.Launcher.exe'
if (-not (Test-Path -LiteralPath $LauncherAppHost)) { throw 'The stable launcher apphost is missing.' }
Move-Item -LiteralPath $LauncherAppHost -Destination (Join-Path $PublishRoot 'SiloPlayer.exe')
if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PublishRoot 'SiloPlayer.exe')).FileVersion -ne $FourPartVersion) {
    throw 'The stable launcher does not carry the file version required by the legacy updater.'
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
foreach ($font in Get-ChildItem -LiteralPath "$RepoRoot\src\SiloPlayer\Assets\Fonts" -File -Filter '*.ttf') {
    $publishedFont = Join-Path $PublishDir ("Assets\Fonts\" + $font.Name)
    if (-not (Test-Path -LiteralPath $publishedFont) -or
        (Get-FileHash -LiteralPath $publishedFont -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $font.FullName -Algorithm SHA256).Hash) {
        throw "Published font is missing or differs from its source: $($font.Name). PRI path entries do not embed font bytes."
    }
}
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
$MpvHeader = [byte[]]::new(2)
$MpvStream = [System.IO.File]::OpenRead($MpvDll)
try { $null = $MpvStream.Read($MpvHeader, 0, 2) } finally { $MpvStream.Dispose() }
if ($MpvHeader[0] -ne 0x4D -or $MpvHeader[1] -ne 0x5A) {
    throw "Published libmpv-2.dll is not a Windows PE binary."
}
$SourceMpvHash = (Get-FileHash -LiteralPath "$RepoRoot\libs\mpv\libmpv-2.dll" -Algorithm SHA256).Hash
$EngineManifest = Get-Content -LiteralPath "$RepoRoot\libs\mpv\engine.json" -Raw | ConvertFrom-Json
if ($SourceMpvHash -ne $EngineManifest.librarySha256) {
    throw "The playback engine does not match libs/mpv/engine.json. Verify its provenance and native Dolby Vision tests before updating the pin."
}
$PublishedMpvHash = (Get-FileHash -LiteralPath $MpvDll -Algorithm SHA256).Hash
if ($SourceMpvHash -ne $PublishedMpvHash) {
    throw "Published libmpv-2.dll does not match the verified repository asset."
}

if ($SigningCertificate) {
    Write-Host "=== Signing locally produced and unsigned application binaries ==="
    $PublishBinaries = Get-ChildItem $PublishRoot -Recurse -File -Include *.exe,*.dll
    foreach ($binary in $PublishBinaries) {
        if ((Get-AuthenticodeSignature -LiteralPath $binary.FullName).Status -eq
            [System.Management.Automation.SignatureStatus]::NotSigned) {
            Set-LocalCodeSignature -Path $binary.FullName
            Write-Host "Signed $($binary.Name)"
        }
    }

    $unsignedPublishedBinaries = Get-ChildItem $PublishRoot -Recurse -File -Include *.exe,*.dll |
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
$BootstrapAssets=Join-Path $PublishRoot 'Assets'
$null=New-Item -ItemType Directory -Path $BootstrapAssets -Force
Copy-Item -LiteralPath $IconDest -Destination (Join-Path $BootstrapAssets 'app.ico')

# Build the manifest/pack after signing and all content copies, from the exact
# version-local bytes used by the full installer. Bootstrap is immutable in v1.
dotnet run --project "$RepoRoot\tools\SiloUpdatePack\SiloUpdatePack.csproj" -c $Configuration -- `
    --client $PublishDir --output $UpdateAssetDir --version $FourPartVersion
if ($LASTEXITCODE -ne 0) { throw 'The incremental update assets could not be built.' }
$ControlDirectory=Join-Path $PublishRoot '.silo-update'
$null=New-Item -ItemType Directory -Path $ControlDirectory
Copy-Item -LiteralPath (Join-Path $UpdateAssetDir 'SiloUpdate-Windows-x64.manifest.json') `
    -Destination (Join-Path $ControlDirectory 'initial.manifest.json')

Write-Host "=== Compiling installer ==="
if (-not (Test-Path $OutputDir)) { $null = New-Item -ItemType Directory -Path $OutputDir }

$IsccArguments = @("/DPublishSourceDir=$PublishRoot", "/DMyAppVersion=$AppVersion", "/DMyAppFourPartVersion=$FourPartVersion", "/O$OutputDir")
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
# Read only this invocation's expected file, never an older successful build.
$SetupExe = Get-Item -LiteralPath $ExpectedSetup -ErrorAction SilentlyContinue
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
    $ManifestPath=Join-Path $UpdateAssetDir 'SiloUpdate-Windows-x64.manifest.json'
    $PackPath=Join-Path $UpdateAssetDir 'SiloUpdate-Windows-x64.pack'
    [pscustomobject]@{
        version=$AppVersion;fileVersion=$FourPartVersion;bootstrapProtocol=1
        publishRoot=$PublishRoot;clientDirectory=$PublishDir
        installerPath=$SetupExe.FullName;installerSha256=$VersionedHash;installerBytes=$SetupExe.Length
        manifestPath=$ManifestPath;manifestSha256=(Get-FileHash -LiteralPath $ManifestPath).Hash
        packPath=$PackPath;packSha256=(Get-FileHash -LiteralPath $PackPath).Hash;packBytes=(Get-Item -LiteralPath $PackPath).Length
        launcherSha256=(Get-FileHash -LiteralPath (Join-Path $PublishRoot 'SiloPlayer.exe')).Hash
        clientSha256=(Get-FileHash -LiteralPath (Join-Path $PublishDir 'SiloPlayer.dll')).Hash
        hostSha256=(Get-FileHash -LiteralPath (Join-Path $PublishDir 'Updater\SiloPlayer.UpdateHost.exe')).Hash
        engineSha256=$PublishedMpvHash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDir "SiloInstaller-$AppVersion-build.json")
    Write-Host ""
    Write-Host "=== Installer built successfully ==="
    Write-Host "  Output: $($SetupExe.FullName)"
    Write-Host "  Stable: $StableSetupExe"
    Write-Host "  SHA256: $VersionedHash"
    Write-Host "  Size:   $SizeMB MB"
    Write-Host "  Layout: $PublishRoot"
    Write-Host "  Client: $PublishDir"
    Write-Host "  Assets: $UpdateAssetDir"
} else {
    Write-Error "Installer output not found"
    exit 1
}
} finally {
    if ($BuildMutexOwned) { $BuildMutex.ReleaseMutex() }
    $BuildMutex.Dispose()
}
