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
foreach ($text in @('', 'unknown', '0.0.0.0', '6.7.2.1.9', '999999999999.7.2')) {
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
        throw 'The uninstaller must be probed with -LiteralPath and -PathType Leaf'
    }
    return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType Leaf
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('silo-inno-version-[' + [Guid]::NewGuid().ToString('N') + ']')
$null = [IO.Directory]::CreateDirectory($fixture)
try {
    # A script has no PE version resource. Its canned help output stands in for
    # a portable compiler with neither an uninstaller nor a registry entry.
    $compiler = Join-Path $fixture 'ISCC.ps1'
    [IO.File]::WriteAllText($compiler, "'Compiler engine version: Inno Setup 6.7.2'")
    Assert-Version (Get-InnoSetupVersion $compiler) '6.7.2.0' 'portable zero-resource compiler'
    [IO.File]::WriteAllText($compiler, "'Inno Setup 6 Command-Line Compiler'")
    Assert-Version (Get-InnoSetupVersion $compiler) '0.0.0.0' 'unreadable version is not an old version'

    $script:registryEntries['HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'] =
        [pscustomobject]@{ InstallLocation = $fixture + [char]0; DisplayVersion = '6.2.0' }
    $script:registryEntries['HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'] =
        [pscustomobject]@{ InstallLocation = $fixture; DisplayVersion = '6.7.2' }
    $script:registryEntries['HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'] =
        [pscustomobject]@{ InstallLocation = (Join-Path $fixture 'other'); DisplayVersion = '99.0.0' }
    Assert-Version (Get-InnoSetupVersion $compiler) '6.7.2.0' 'malformed entry skipped; matching entry used'
} finally {
    [IO.Directory]::Delete($fixture, $true)
}
Write-Output 'Inno Setup version-detection regressions passed.'
