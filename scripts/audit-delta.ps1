<#
.SYNOPSIS
    Incremental parity audit -- identifies which desktop pages need re-auditing
    after a continuum-server update by cross-referencing changed webui files
    against docs/audit-page-map.json.

.DESCRIPTION
    Instead of deploying 25+ full-audit agents every time the server updates,
    this script:
    1. Pulls the latest continuum-server (optional -Pull flag)
    2. Diffs from last-verified SHA to HEAD for each page-map entry
    3. Reports which pages have upstream changes (AFFECTED) vs unchanged (SKIP)
    4. For affected pages, shows the specific files + line-count delta

.PARAMETER Pull
    Run `git pull` on the continuum-server repo before scanning.

.PARAMETER ServerRepo
    Path to the continuum-server clone. Default: F:\continuum-server

.PARAMETER PageMap
    Path to the page-map JSON. Default: docs\audit-page-map.json

.EXAMPLE
    pwsh scripts/audit-delta.ps1 -Pull
    pwsh scripts/audit-delta.ps1
#>

param(
    [switch]$Pull,
    [string]$ServerRepo = "F:\continuum-server",
    [string]$PageMap = "$PSScriptRoot\..\docs\audit-page-map.json"
)

$ErrorActionPreference = "Stop"

# --- Pull if requested ---
if ($Pull) {
    Write-Host "Pulling latest continuum-server..." -ForegroundColor Cyan
    Push-Location $ServerRepo
    git pull --ff-only
    Pop-Location
    Write-Host ""
}

# --- Load page map ---
if (-not (Test-Path $PageMap)) {
    Write-Error "Page map not found at $PageMap"
    exit 1
}
$map = Get-Content $PageMap -Raw | ConvertFrom-Json

# --- Get server HEAD ---
Push-Location $ServerRepo
$headSha = (git rev-parse --short HEAD).Trim()
$headMsg = (git log -1 --format="%s").Trim()
Pop-Location

Write-Host "Server HEAD: $headSha ($headMsg)" -ForegroundColor Cyan
Write-Host ""

# --- Scan each section (admin, user) ---
$affected = @()
$skipped = @()
$unaudited = @()

foreach ($section in @("admin", "user")) {
    $pages = $map.$section
    if (-not $pages) { continue }

    foreach ($prop in $pages.PSObject.Properties) {
        $webPath = $prop.Name
        $entry = $prop.Value
        $lastSha = $entry.last_verified_sha

        # Determine all webui files to check for this entry
        $filesToCheck = @($webPath)
        if ($entry.also_includes) {
            foreach ($extra in $entry.also_includes) {
                # If it looks like a bare filename, resolve relative to the webPath's directory
                if ($extra -notmatch "[\\/]") {
                    $dir = Split-Path $webPath -Parent
                    if ($dir) { $filesToCheck += "$dir/$extra" }
                    else { $filesToCheck += $extra }
                } else {
                    $filesToCheck += $extra
                }
            }
        }
        if ($entry.also_includes_dir) {
            # Include all .tsx files in the specified directory
            $dirPath = Join-Path $ServerRepo ($entry.also_includes_dir -replace '/', '\')
            if (Test-Path $dirPath) {
                Get-ChildItem $dirPath -Filter "*.tsx" -File | ForEach-Object {
                    $rel = $_.FullName.Substring($ServerRepo.Length + 1) -replace '\\', '/'
                    $filesToCheck += $rel
                }
            }
        }

        # If never audited, always flag
        if (-not $lastSha) {
            $unaudited += [PSCustomObject]@{
                Section  = $section
                WebPath  = $webPath
                Desktop  = ($entry.desktop_files -join ", ")
                Reason   = "Never audited"
            }
            continue
        }

        # Check if any of the webui files changed since last_verified_sha
        $changedFiles = @()
        Push-Location $ServerRepo
        foreach ($f in $filesToCheck) {
            # Skip directory entries (trailing /)
            if ($f.EndsWith("/")) { continue }
            # Check if file exists in the repo
            $fullPath = Join-Path $ServerRepo ($f -replace '/', '\')
            if (-not (Test-Path $fullPath)) { continue }

            $diffStat = git diff --stat "$lastSha..HEAD" -- $f 2>$null
            if ($diffStat) {
                $changedFiles += $f
            }
        }
        Pop-Location

        if ($changedFiles.Count -gt 0) {
            $affected += [PSCustomObject]@{
                Section      = $section
                WebPath      = $webPath
                Desktop      = ($entry.desktop_files -join ", ")
                AuditFile    = $entry.audit_file
                ChangedFiles = $changedFiles
                ChangedCount = $changedFiles.Count
            }
        } else {
            $skipped += [PSCustomObject]@{
                Section = $section
                WebPath = $webPath
            }
        }
    }
}

# --- Report ---
Write-Host "============================================" -ForegroundColor Yellow
Write-Host " INCREMENTAL PARITY AUDIT -- DELTA REPORT" -ForegroundColor Yellow
Write-Host " Baseline SHA per page-map entry" -ForegroundColor Yellow
Write-Host " Server HEAD: $headSha" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
Write-Host ""

if ($affected.Count -gt 0) {
    Write-Host "AFFECTED PAGES ($($affected.Count)) -- re-audit needed:" -ForegroundColor Red
    Write-Host ""
    foreach ($a in $affected) {
        Write-Host "  [$($a.Section)] $($a.WebPath)" -ForegroundColor White
        Write-Host "    Desktop: $($a.Desktop)" -ForegroundColor DarkGray
        if ($a.AuditFile) {
            Write-Host "    Audit:   $($a.AuditFile)" -ForegroundColor DarkGray
        }
        Write-Host "    Changed ($($a.ChangedCount) file(s)):" -ForegroundColor DarkGray
        foreach ($cf in $a.ChangedFiles) {
            Write-Host "      ~ $cf" -ForegroundColor DarkYellow
        }
        Write-Host ""
    }
} else {
    Write-Host "NO AFFECTED PAGES -- all audited pages are up to date." -ForegroundColor Green
    Write-Host ""
}

if ($unaudited.Count -gt 0) {
    Write-Host "NEVER AUDITED ($($unaudited.Count)) -- full audit needed:" -ForegroundColor Magenta
    Write-Host ""
    foreach ($u in $unaudited) {
        Write-Host "  [$($u.Section)] $($u.WebPath)" -ForegroundColor White
        Write-Host "    Desktop: $($u.Desktop)" -ForegroundColor DarkGray
        Write-Host ""
    }
}

if ($skipped.Count -gt 0) {
    Write-Host "UNCHANGED ($($skipped.Count)) -- skip:" -ForegroundColor Green
    foreach ($s in $skipped) {
        Write-Host "  [$($s.Section)] $($s.WebPath)" -ForegroundColor DarkGreen
    }
    Write-Host ""
}

Write-Host "Summary: $($affected.Count) affected, $($unaudited.Count) unaudited, $($skipped.Count) unchanged" -ForegroundColor Cyan
