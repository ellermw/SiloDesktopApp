# Standalone version-detection regression fixture; never runs the installer build.
# Usage: pwsh -File tests/installer-safety/Verify-InnoSetupVersion.ps1
$ErrorActionPreference = 'Stop'
$buildScript = Join-Path $PSScriptRoot '../../installer/build.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($buildScript, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw "build.ps1 did not parse: $parseErrors" }
$functionNames = 'ConvertTo-InnoSetupVersion', 'Select-InnoSetupVersion', 'Get-InnoSetupVersion'
$definitions = $ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in $functionNames
}, $false)
if ($definitions.Count -ne $functionNames.Count) { throw 'Version-detection functions missing' }
foreach ($definition in $definitions) {
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-Version($Actual, [version]$Expected, [string]$Case) {
    if ($Actual -isnot [version] -or $Actual -ne $Expected) {
        throw "${Case}: expected $Expected, got '$Actual'"
    }
}
function Assert-Conflict([scriptblock]$Action) {
    $threw = $false
    try { & $Action | Out-Null } catch {
        if ($_.Exception.Message -notlike 'Conflicting Inno Setup versions*') { throw }
        $threw = $true
    }
    if (-not $threw) { throw 'Disagreeing fallback versions must fail explicitly' }
}

Assert-Version (ConvertTo-InnoSetupVersion '6.7.2') '6.7.2.0' 'three-part version'
Assert-Version (ConvertTo-InnoSetupVersion '6.7') '6.7.0.0' 'two-part version'
Assert-Version (ConvertTo-InnoSetupVersion '6.7.2.1 (release)') '6.7.2.1' 'four-part version'
Assert-Version (ConvertTo-InnoSetupVersion 'Inno Setup 6.7.2') '6.7.2.0' 'named ProductVersion'
Assert-Version (ConvertTo-InnoSetupVersion 'v6.7.2') '6.7.2.0' 'prefixed ProductVersion'
Assert-Version (ConvertTo-InnoSetupVersion 'Inno Setup Setup Data (6.4.2)') '6.4.2.0' 'descriptive ProductVersion'
Assert-Version (ConvertTo-InnoSetupVersion 'Inno Setup version 6.7.2') '6.7.2.0' 'descriptive DisplayVersion'
foreach ($text in @('', 'unknown', '0.0.0.0', '6.7.2.1.9', '999999999999.7.2', 'Inno Setup 6 Command-Line Compiler', 'Inno Setup Setup Data (6.4.2.1.9)', 'Inno Setup Setup Data (999999999999.4.2)')) {
    if ($null -ne (ConvertTo-InnoSetupVersion $text)) { throw "Invalid/absent version accepted: '$text'" }
}
Assert-Version (Select-InnoSetupVersion -RegistryVersions '6.7.2' -UninstallerVersions '0.0.0.0') '6.7.2.0' 'zero-first fallback'
Assert-Version (Select-InnoSetupVersion -RegistryVersions '6.7.2' -UninstallerVersions '6.2.0') '6.7.2.0' 'stale-first fallback'
Assert-Version (Select-InnoSetupVersion -UninstallerVersions @('0.0.0.0', '6.7.2')) '6.7.2.0' 'skip zero candidate'
Assert-Version (Select-InnoSetupVersion -RegistryVersions '0.0.0.0' -UninstallerVersions '6.7.2') '6.7.2.0' 'absent registry version'
Assert-Version (Select-InnoSetupVersion -RegistryVersions @('6.7.2', '6.7.2.0')) '6.7.2.0' 'equivalent registry versions'
Assert-Version (Select-InnoSetupVersion -RegistryVersions '6.2.0' -UninstallerVersions '6.7.2') '6.2.0.0' 'registry precedence is not highest-version selection'
Assert-Conflict { Select-InnoSetupVersion -RegistryVersions @('6.2.0', '6.7.2') }
Assert-Conflict { Select-InnoSetupVersion -UninstallerVersions @('6.2.0', '6.7.2') }
Assert-Version (Select-InnoSetupVersion) '0.0.0.0' 'no usable candidates'

# Exercise the build's actual minimum-version gate without running the build.
$minimumVersionCheck = $ast.Find({
    param($node)
    $node -is [Management.Automation.Language.IfStatementAst] -and
        $node.Extent.Text.StartsWith('if ($IsccVersion -lt ')
}, $false)
if (-not $minimumVersionCheck) { throw 'Minimum compiler version gate missing' }
$checkMinimumVersion = [scriptblock]::Create($minimumVersionCheck.Extent.Text)
$IsccPath = 'fixture compiler'
foreach ($text in @('6.2.99.99', '6.3.0.0', '6.7.2.0')) {
    $IsccVersion = [version]$text
    $message = $null
    try { & $checkMinimumVersion } catch { $message = $_.Exception.Message }
    if ($IsccVersion -lt [version]'6.3.0.0') {
        if ($message -notlike 'Inno Setup 6.3.0 or newer is required*') {
            throw "Old compiler must fail the minimum-version gate: '$message'"
        }
    } elseif ($message) {
        throw "Supported compiler rejected: $message"
    }
}

# Model file version resources, not invented output from ISCC.exe /?. Unmocked
# files still use FileVersionInfo through PowerShell's real VersionInfo property.
$script:fileVersionInfos = @{}
function Get-Item {
    [CmdletBinding()]
    param([string]$LiteralPath)
    if ($script:fileVersionInfos.ContainsKey($LiteralPath)) {
        return [pscustomobject]@{ VersionInfo = $script:fileVersionInfos[$LiteralPath] }
    }
    return Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -ErrorAction Stop
}

# Isolate registry reads without changing the machine's uninstall registration.
$script:registryEntries = @{}
function Get-ItemProperty {
    [CmdletBinding()]
    param([string]$LiteralPath)
    return $script:registryEntries[$LiteralPath]
}
# Require a literal file probe, including when the fixture directory has brackets.
function Test-Path {
    [CmdletBinding()]
    param([Parameter(Position=0)][string]$Path, [string]$LiteralPath, [string]$PathType)
    if ($Path -or -not $LiteralPath -or $PathType -ne 'Leaf') {
        throw 'Version sources must be probed with -LiteralPath and -PathType Leaf'
    }
    return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType Leaf
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('silo-inno-version-[' + [Guid]::NewGuid().ToString('N') + ']')
$null = [IO.Directory]::CreateDirectory($fixture)
try {
    # A script has no PE version resource. Version discovery must not run it.
    $compiler = Join-Path $fixture 'ISCC.ps1'
    $probeMarker = Join-Path $fixture 'compiler-was-executed'
    [IO.File]::WriteAllText($compiler, '[IO.File]::WriteAllText((Join-Path $PSScriptRoot ''compiler-was-executed''), ''unexpected invocation'')')
    Assert-Version (Get-InnoSetupVersion $compiler) '0.0.0.0' 'missing engine and installation metadata'

    $engine = Join-Path $fixture 'ISCmplr.dll'
    [IO.File]::WriteAllText($engine, '')
    $engineInfo = [pscustomobject]@{
        FileMajorPart = 6; FileMinorPart = 7; FileBuildPart = 2; FilePrivatePart = 0
        ProductVersion = '6.7.2'
    }
    $script:fileVersionInfos[$engine] = $engineInfo
    Assert-Version (Get-InnoSetupVersion $compiler) '6.7.2.0' 'portable zero-resource compiler uses adjacent engine'

    $script:registryEntries['HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'] =
        [pscustomobject]@{ InstallLocation = $fixture; DisplayVersion = '6.2.0' }
    Assert-Version (Get-InnoSetupVersion $compiler) '6.7.2.0' 'engine precedes stale installation metadata'
    $script:fileVersionInfos[$compiler] = [pscustomobject]@{
        FileMajorPart = 6; FileMinorPart = 5; FileBuildPart = 3; FilePrivatePart = 0
        ProductVersion = '6.5.3'
    }
    Assert-Version (Get-InnoSetupVersion $compiler) '6.5.3.0' 'compiler resource still takes precedence'
    $script:fileVersionInfos.Remove($compiler)

    $engineInfo.FileMajorPart = 0
    $engineInfo.FileMinorPart = 0
    $engineInfo.FileBuildPart = 0
    $engineInfo.ProductVersion = 'Inno Setup 6.7.2'
    Assert-Version (Get-InnoSetupVersion $compiler) '6.7.2.0' 'engine ProductVersion fallback'
    $engineInfo.ProductVersion = '6.2.0'
    Assert-Version (Get-InnoSetupVersion $compiler) '6.2.0.0' 'old engine remains subject to minimum-version gate'
    $script:registryEntries.Clear()
    $engineInfo.ProductVersion = ''
    Assert-Version (Get-InnoSetupVersion $compiler) '0.0.0.0' 'unreadable version is not an old version'

    $uninstaller = Join-Path $fixture 'unins000.exe'
    [IO.File]::WriteAllText($uninstaller, '')
    $uninstallerInfo = [pscustomobject]@{
        FileMajorPart = 6; FileMinorPart = 4; FileBuildPart = 2; FilePrivatePart = 0
        ProductVersion = 'Inno Setup Setup Data (6.4.2)'
    }
    $script:fileVersionInfos[$uninstaller] = $uninstallerInfo
    Assert-Version (Get-InnoSetupVersion $compiler) '6.4.2.0' 'uninstaller numeric resource with descriptive ProductVersion'
    $uninstallerInfo.ProductVersion = $null
    Assert-Version (Get-InnoSetupVersion $compiler) '6.4.2.0' 'uninstaller numeric resource without ProductVersion'
    $script:registryEntries['HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'] =
        [pscustomobject]@{ InstallLocation = $fixture; DisplayVersion = '6.2.0' }
    Assert-Version (Get-InnoSetupVersion $compiler) '6.2.0.0' 'matching registry precedes numeric uninstaller resource'
    $script:registryEntries.Clear()
    $uninstallerInfo.FileMajorPart = 0
    $uninstallerInfo.FileMinorPart = 0
    $uninstallerInfo.FileBuildPart = 0
    $uninstallerInfo.ProductVersion = 'Inno Setup Setup Data (6.4.2)'
    Assert-Version (Get-InnoSetupVersion $compiler) '6.4.2.0' 'uninstaller descriptive ProductVersion with zero numeric resource'
    $script:registryEntries['HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'] =
        [pscustomobject]@{ InstallLocation = $fixture; DisplayVersion = 'Inno Setup version 6.7.2' }
    Assert-Version (Get-InnoSetupVersion $compiler) '6.7.2.0' 'matching registry descriptive DisplayVersion'
    $script:registryEntries.Clear()
    $uninstallerInfo.ProductVersion = '6.5.3'
    Assert-Version (Get-InnoSetupVersion $compiler) '6.5.3.0' 'uninstaller ProductVersion fallback'

    $script:registryEntries['HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'] =
        [pscustomobject]@{ InstallLocation = $fixture + [char]0; DisplayVersion = '6.2.0' }
    $script:registryEntries['HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'] =
        [pscustomobject]@{ InstallLocation = $fixture; DisplayVersion = '6.7.2' }
    $script:registryEntries['HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'] =
        [pscustomobject]@{ InstallLocation = (Join-Path $fixture 'other'); DisplayVersion = '99.0.0' }
    Assert-Version (Get-InnoSetupVersion $compiler) '6.7.2.0' 'malformed entry skipped; matching entry used'
    if ([IO.File]::Exists($probeMarker)) { throw 'Version discovery must not invoke compiler help' }
} finally {
    [IO.Directory]::Delete($fixture, $true)
}
Write-Output 'Inno Setup version-detection regressions passed.'
