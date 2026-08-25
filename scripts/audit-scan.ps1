# scripts/audit-scan.ps1
# Change-tracking scan for the desktop app's WebUI/server parity baseline.
#
# What it does:
#  1. Reads docs/audit-baseline.json to find the server SHA we last audited.
#  2. Fetches the official Silo server origin/main (and optionally fast-forwards the checkout).
#  3. Diffs baseline..origin/main to find every changed file under web/src/ and internal/.
#  4. Classifies each changed file against the "areas" map in audit-baseline.json.
#  5. Writes a markdown report under docs/audit-reports/ with a prioritized work list.
#
# Classification:
#   P0 -- modified file IS tracked by an area with parity="full" or "partial". The
#        desktop counterpart may now be out of sync. Highest priority to re-audit.
#   P1 -- new file in a directory tracked by an existing area. Likely a new sub-feature
#        of that area; owner of the area should integrate it.
#   P2 -- modified/new file in a directory NOT covered by any area. Potentially a
#        brand-new feature that needs a new area entry.
#
# Usage:
#   pwsh scripts/audit-scan.ps1                 # scan freshly fetched official origin/main
#   pwsh scripts/audit-scan.ps1 -Pull           # also fast-forward the local checkout
#   pwsh scripts/audit-scan.ps1 -ServerPath X   # override the bundled reference worktree

param(
    [string]$ServerPath = (Join-Path (Join-Path $PSScriptRoot "..") ".codex-tmp\silo-server-current"),
    [string]$BaselinePath = (Join-Path (Join-Path $PSScriptRoot "..") "docs\audit-baseline.json"),
    [string]$ReportsDir = (Join-Path (Join-Path $PSScriptRoot "..") "docs\audit-reports"),
    [string]$Since = "",   # Optional: override the baseline SHA for an ad-hoc scan
    [switch]$Pull
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\audit-common.ps1"

function Invoke-GitChecked {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
    return $output
}

if (-not (Test-Path $ServerPath)) {
    Write-Error "Official Silo server clone not found at '$ServerPath'. Clone https://github.com/Silo-Server/silo-server.git first."
    exit 1
}
if (-not (Test-Path $BaselinePath)) {
    Write-Error "Baseline not found at '$BaselinePath'."
    exit 1
}

$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json
$baselineSha = if ($Since) { $Since } else { $baseline.continuum_server_sha }
if (-not $baselineSha) {
    Write-Error "audit-baseline.json has no continuum_server_sha. Bootstrap it first."
    exit 1
}

Push-Location $ServerPath
try {
    $originUrl = (Invoke-GitChecked @("remote", "get-url", "origin")).Trim()
    Assert-OfficialSiloOrigin $originUrl
    Write-Host "Fetching latest official Silo server main..."
    Invoke-GitChecked @("fetch", "origin", "main") | Out-Null
    if ($Pull) {
        $dirty = @(Invoke-GitChecked @("status", "--porcelain")) -join "`n"
        if (-not [string]::IsNullOrWhiteSpace($dirty)) {
            throw "Refusing to fast-forward a dirty Silo server checkout. Commit or stash its changes first."
        }
        & git merge-base --is-ancestor HEAD origin/main
        if ($LASTEXITCODE -ne 0) {
            throw "Local Silo checkout cannot be fast-forwarded to official origin/main."
        }
        Invoke-GitChecked @("merge", "--ff-only", "origin/main") | Out-Null
    }

    # Verify the baseline SHA actually exists in the server repo.
    & git cat-file -e "$baselineSha^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Baseline SHA $baselineSha not found in $ServerPath. Did the repo get force-pushed?"
        exit 1
    }

    $headSha = (Invoke-GitChecked @("rev-parse", "origin/main")).Trim()
    $headShort = (Invoke-GitChecked @("rev-parse", "--short", "origin/main")).Trim()
    $headDate = (Invoke-GitChecked @("log", "-1", "--format=%ci", "origin/main")).Trim()
    & git merge-base --is-ancestor $baselineSha $headSha
    if ($LASTEXITCODE -ne 0) {
        throw "Baseline SHA $baselineSha is not an ancestor of official origin/main $headSha; refusing an ambiguous audit window."
    }

    if ($headSha -eq $baselineSha) {
        Write-Host ""
        Write-Host "  Baseline is up to date with official origin/main ($headShort)."
        Write-Host "  Nothing to scan."
        Write-Host ""
        exit 0
    }

    Write-Host ""
    Write-Host "  Baseline: $($baseline.continuum_server_short_sha)"
    Write-Host "  Official origin/main: $headShort ($headDate)"
    Write-Host ""

    # Build { filePath → change kind (A/M/D) } for every file under tracked roots.
    $diffRaw = Invoke-GitChecked @("diff", "--name-status", $baselineSha, $headSha, "--", "web/src/", "internal/")
    $changes = @()
    foreach ($line in $diffRaw) {
        if (-not $line) { continue }
        $parts = $line -split "`t"
        $kind = $parts[0]
        $path = $parts[1] -replace '\\', '/'
        # For renames (R100 old new), git gives 3 columns; treat as delete + add.
        if ($kind -match '^R') {
            $oldPath = $parts[1] -replace '\\', '/'
            $newPath = $parts[2] -replace '\\', '/'
            $changes += [pscustomobject]@{ Kind = 'D'; Path = $oldPath }
            $changes += [pscustomobject]@{ Kind = 'A'; Path = $newPath }
        } else {
            $changes += [pscustomobject]@{ Kind = $kind; Path = $path }
        }
    }

    # List of commit messages for context.
    $commits = Invoke-GitChecked @("log", "--format=%h %s", "$baselineSha..$headSha", "--", "web/src/", "internal/")
}
finally {
    Pop-Location
}

if (-not $changes -or $changes.Count -eq 0) {
    Write-Host "  No files changed under web/src/ or internal/."
    exit 0
}

# Classify each change.
# Build a reverse index: file path → areaName (first area that claims it).
$fileToArea = @{}
foreach ($areaName in $baseline.areas.PSObject.Properties.Name) {
    $area = $baseline.areas.$areaName
    foreach ($src in @($area.web_sources) + @($area.server_sources)) {
        if ($src) { $fileToArea[$src] = $areaName }
    }
}

# P1 logic: a directory "belongs" to an area ONLY if the area has 2+ source files
# in that exact directory. Single-file claims don't anchor a directory -- otherwise
# e.g. an area that tracks one file in web/src/components/ would claim every new
# file under web/src/components/ as its own.
$dirFileCounts = @{}  # "area|dir" -> count
foreach ($areaName in $baseline.areas.PSObject.Properties.Name) {
    $area = $baseline.areas.$areaName
    foreach ($src in @($area.web_sources) + @($area.server_sources)) {
        if ($src) {
            # Exclude test files from anchoring a directory claim -- a single
            # component.tsx + its component.test.tsx shouldn't count as 2 real files.
            $leaf = Split-Path $src -Leaf
            if ($leaf -match '\.test\.' -or $leaf -match '_test\.go$') { continue }
            $dir = Split-Path $src -Parent -ErrorAction SilentlyContinue
            if ($dir) {
                $dir = $dir -replace '\\', '/'
                $key = "$areaName|$dir"
                if (-not $dirFileCounts.ContainsKey($key)) { $dirFileCounts[$key] = 0 }
                $dirFileCounts[$key]++
            }
        }
    }
}
$areaDirs = @{}
foreach ($key in $dirFileCounts.Keys) {
    if ($dirFileCounts[$key] -ge 2) {
        $parts = $key -split '\|', 2
        $areaName = $parts[0]
        $dir = $parts[1]
        if (-not $areaDirs.ContainsKey($dir)) { $areaDirs[$dir] = @() }
        if ($areaDirs[$dir] -notcontains $areaName) { $areaDirs[$dir] += $areaName }
    }
}

$p0 = @()  # modified tracked files
$p1 = @()  # new files in tracked areas' directories
$p2 = @()  # unclassified

foreach ($c in $changes) {
    if ($fileToArea.ContainsKey($c.Path)) {
        $areaName = $fileToArea[$c.Path]
        $parity = $baseline.areas.$areaName.parity
        $p0 += [pscustomobject]@{ Kind = $c.Kind; Path = $c.Path; Area = $areaName; Parity = $parity }
        continue
    }
    # Exact-parent match only -- no walking up the tree.
    $fileDir = (Split-Path $c.Path -Parent) -replace '\\', '/'
    if ($fileDir -and $areaDirs.ContainsKey($fileDir)) {
        foreach ($areaName in $areaDirs[$fileDir]) {
            $p1 += [pscustomobject]@{ Kind = $c.Kind; Path = $c.Path; Area = $areaName }
        }
    } else {
        $p2 += [pscustomobject]@{ Kind = $c.Kind; Path = $c.Path }
    }
}

# Write markdown report.
New-Item -ItemType Directory -Path $ReportsDir -Force | Out-Null
$timestamp = Get-Date -Format "yyyy-MM-dd-HHmm"
$reportPath = Join-Path $ReportsDir "$timestamp-changes.md"

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# Silo Server Change Report")
[void]$sb.AppendLine()
[void]$sb.AppendLine("| | |")
[void]$sb.AppendLine("|---|---|")
[void]$sb.AppendLine("| Generated | $(Get-Date -Format "yyyy-MM-dd HH:mm:ss") |")
$baselineMsg = if ($baseline.continuum_server_commit_message) { $baseline.continuum_server_commit_message } else { "(unknown)" }
[void]$sb.AppendLine("| Baseline  | ``$($baseline.continuum_server_short_sha)`` ($($baselineMsg.Substring(0, [Math]::Min(60, $baselineMsg.Length)))) |")
[void]$sb.AppendLine("| Official origin/main | ``$headShort`` ($headDate) |")
[void]$sb.AppendLine("| Changed files | $($changes.Count) |")
[void]$sb.AppendLine("| P0 (tracked-area modified) | $($p0.Count) |")
[void]$sb.AppendLine("| P1 (new file in tracked dir) | $($p1.Count) |")
[void]$sb.AppendLine("| P2 (unclassified) | $($p2.Count) |")
[void]$sb.AppendLine()

[void]$sb.AppendLine("## P0 -- files in tracked areas changed (re-audit needed)")
[void]$sb.AppendLine()
if ($p0.Count -eq 0) {
    [void]$sb.AppendLine("_No tracked files were modified in this window._")
} else {
    $byArea = $p0 | Group-Object -Property Area
    foreach ($grp in $byArea) {
        [void]$sb.AppendLine("### ``$($grp.Name)`` (parity: $($grp.Group[0].Parity))")
        [void]$sb.AppendLine()
        foreach ($item in $grp.Group) {
            $sigil = switch ($item.Kind) { 'A' {'+'}; 'D' {'-'}; 'M' {'~'}; default {'?'} }
            [void]$sb.AppendLine("- $sigil ``$($item.Path)``")
        }
        [void]$sb.AppendLine()
    }
}

[void]$sb.AppendLine("## P1 -- new files inside an existing tracked area's directory")
[void]$sb.AppendLine()
if ($p1.Count -eq 0) {
    [void]$sb.AppendLine("_No new files in tracked areas._")
} else {
    $byArea = $p1 | Group-Object -Property Area
    foreach ($grp in $byArea) {
        [void]$sb.AppendLine("### ``$($grp.Name)``")
        [void]$sb.AppendLine()
        foreach ($item in $grp.Group) {
            $sigil = switch ($item.Kind) { 'A' {'+'}; 'D' {'-'}; 'M' {'~'}; default {'?'} }
            [void]$sb.AppendLine("- $sigil ``$($item.Path)``")
        }
        [void]$sb.AppendLine()
    }
}

[void]$sb.AppendLine("## P2 -- unclassified changes (may be new feature areas)")
[void]$sb.AppendLine()
if ($p2.Count -eq 0) {
    [void]$sb.AppendLine("_Everything was classified._")
} else {
    foreach ($item in $p2) {
        $sigil = switch ($item.Kind) { 'A' {'+'}; 'D' {'-'}; 'M' {'~'}; default {'?'} }
        [void]$sb.AppendLine("- $sigil ``$($item.Path)``")
    }
    [void]$sb.AppendLine()
}

[void]$sb.AppendLine("## Commits in window")
[void]$sb.AppendLine()
[void]$sb.AppendLine('```')
foreach ($line in $commits) { [void]$sb.AppendLine($line) }
[void]$sb.AppendLine('```')

Set-Content -Path $reportPath -Value $sb.ToString() -Encoding UTF8

Write-Host ""
Write-Host "  Report written to: $reportPath"
Write-Host ""
Write-Host "  Summary:"
Write-Host "    P0 tracked-area changes:       $($p0.Count)"
Write-Host "    P1 new files in tracked areas: $($p1.Count)"
Write-Host "    P2 unclassified changes:       $($p2.Count)"
Write-Host ""
Write-Host "  Work through the P0 list first, then P1, then decide if any P2 items warrant a new area."
Write-Host "  When an area is re-synced, bump it with: pwsh scripts/audit-bump.ps1 -Area <name>"
Write-Host ""
