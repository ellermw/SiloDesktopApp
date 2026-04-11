# scripts/audit-bump.ps1
# Mark an area as re-audited against the current continuum-server HEAD and bump
# the top-level baseline SHA. Run after finishing a desktop rebuild.
#
# Usage:
#   pwsh scripts/audit-bump.ps1 -Area settings/history-import
#   pwsh scripts/audit-bump.ps1 -Area settings/history-import -Parity full
#   pwsh scripts/audit-bump.ps1 -All   # bump every area to current HEAD without changing parity

param(
    [string]$Area,
    [ValidateSet('full', 'partial', 'rebuilding', 'missing', 'desktop-only')]
    [string]$Parity,
    [switch]$All,
    [string]$ServerPath = "F:\continuum-server",
    [string]$BaselinePath = (Join-Path (Join-Path $PSScriptRoot "..") "docs\audit-baseline.json")
)

$ErrorActionPreference = "Stop"

if (-not $Area -and -not $All) {
    Write-Error "Specify -Area <name> (e.g., settings/history-import) or -All to bump the root SHA without changing any area parity."
    exit 1
}

if (-not (Test-Path $ServerPath)) {
    Write-Error "Server clone not found at '$ServerPath'."
    exit 1
}
if (-not (Test-Path $BaselinePath)) {
    Write-Error "Baseline not found at '$BaselinePath'."
    exit 1
}

Push-Location $ServerPath
try {
    $headSha = (git rev-parse HEAD).Trim()
    $headShort = (git rev-parse --short HEAD).Trim()
    $headMsg = (git log -1 --format="%s" HEAD).Trim()
}
finally {
    Pop-Location
}

$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json

# Update root SHA + timestamp.
$baseline.continuum_server_sha = $headSha
$baseline.continuum_server_short_sha = $headShort
$baseline.continuum_server_commit_message = $headMsg
$baseline.audited_at = (Get-Date -Format "yyyy-MM-ddTHH:mm:sszzz")

if ($Area) {
    if (-not $baseline.areas.PSObject.Properties[$Area]) {
        Write-Error "Area '$Area' not found in audit-baseline.json. Known areas: $($baseline.areas.PSObject.Properties.Name -join ', ')"
        exit 1
    }

    $areaObj = $baseline.areas.$Area

    # Refresh line counts for every web_source / server_source.
    $allSources = @($areaObj.web_sources) + @($areaObj.server_sources)
    foreach ($src in $allSources) {
        if (-not $src) { continue }
        $full = Join-Path $ServerPath ($src -replace '/', '\')
        if (Test-Path $full) {
            $lineCount = (Get-Content $full | Measure-Object -Line).Lines
            if (-not $areaObj.line_counts) {
                $areaObj | Add-Member -NotePropertyName line_counts -NotePropertyValue ([pscustomobject]@{}) -Force
            }
            if ($areaObj.line_counts.PSObject.Properties[$src]) {
                $areaObj.line_counts.$src = $lineCount
            } else {
                $areaObj.line_counts | Add-Member -NotePropertyName $src -NotePropertyValue $lineCount -Force
            }
        }
    }

    if ($Parity) {
        $areaObj.parity = $Parity
    }

    Write-Host ""
    Write-Host "  Bumped area '$Area'"
    if ($Parity) { Write-Host "    parity → $Parity" }
    Write-Host "    line counts refreshed for $($allSources.Count) source file(s)"
}

# Write baseline back with stable formatting.
$json = $baseline | ConvertTo-Json -Depth 10
Set-Content -Path $BaselinePath -Value $json -Encoding UTF8

Write-Host ""
Write-Host "  Baseline now at: $headShort"
Write-Host "    $headMsg"
Write-Host ""
Write-Host "  Next step: commit docs/audit-baseline.json to lock in the new state."
Write-Host ""
