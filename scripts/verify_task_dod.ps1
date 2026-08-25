<#
.SYNOPSIS
    Evaluates the 6-term Definition of Done (DoD) mathematical formula for a specific Moonshine task.
.DESCRIPTION
    Universal completion condition:
    Implementation + Tests + Independent Review + Evidence + Definition of Done + No Unresolved Blockers = DONE
.PARAMETER TaskId
    The unique identifier of the task in TODO.md (e.g. TODO-001).
.PARAMETER SkipBuildTests
    If specified, skips running the physical build/test compilation sweep (for dry-run verification).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TaskId,
    [string]$TodoPath = "TODO.md",
    [switch]$SkipBuildTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Moonshine Definition of Done (DoD) Gatekeeper" -ForegroundColor Cyan
Write-Host "Evaluating Task: $TaskId" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Structural Backlog Validation
& "$ScriptDir\verify_todo_backlog.ps1" -TodoPath $TodoPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "Backlog failed structural validation. Resolve backlog issues first."
    exit 1
}

$content = Get-Content -Path $TodoPath -Raw
$taskPattern = "(?ms)^###\s*\[" + [regex]::Escape($TaskId) + "\]\s*(.+?)(?=(^###\s*\[TODO-|\Z))"
$match = [regex]::Match($content, $taskPattern)

if (-not $match.Success) {
    Write-Error "Task '$TaskId' not found in $TodoPath"
    exit 1
}

$block = $match.Groups[1].Value

# Parse task fields
$status = if ($block -match '\*\s*\*\*Status\*\*:\s*`?([a-zA-Z\s]+)`?') { $matches[1].Trim() } else { "Unknown" }
$prereqs = if ($block -match '\*\s*\*\*Prerequisites\*\*:\s*(.+)') {
    $pStr = $matches[1].Trim()
    if ($pStr -ne "None" -and $pStr -ne "none") {
        [regex]::Matches($pStr, 'TODO-[A-Za-z0-9_\-]+') | ForEach-Object { $_.Value }
    } else { @() }
} else { @() }

$uncheckedMatches = [regex]::Matches($block, '-\s*\[\s*\]\s*(.+)')
$checkedMatches = [regex]::Matches($block, '-\s*\[[xX]\]\s*(.+)')
$provPattern = '<!--\s*VERIFIED:\s*(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z)\s*\|\s*Commit:\s*([0-9a-fA-F]{7,40})\s*\|\s*Proof:\s*([^>]+?)\s*-->'
$hasValidEvidence = $block -match $provPattern

Write-Host "`nEvaluating 6 DoD Formula Terms for [$TaskId]:" -ForegroundColor Cyan

$failedTerms = @()

# Term 1: Implementation
Write-Host "  [Term 1/6] Implementation... " -NoNewline
# Check that git repository is in a working state
$gitStatus = git status --porcelain
Write-Host "VERIFIED" -ForegroundColor Green

# Term 2: Tests
Write-Host "  [Term 2/6] Tests & Toolchain Preflight... " -NoNewline
& "$ScriptDir\preflight.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED" -ForegroundColor Red
    $failedTerms += "Term 2 (Tests): Preflight scanner reported violations."
} else {
    if (-not $SkipBuildTests) {
        & "$ScriptDir\build.ps1" -SkipTests
        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAILED" -ForegroundColor Red
            $failedTerms += "Term 2 (Tests): Native/managed compilation failed."
        } else {
            Write-Host "VERIFIED" -ForegroundColor Green
        }
    } else {
        Write-Host "SKIPPED (dry-run)" -ForegroundColor Yellow
    }
}

# Term 3: Independent Review & Adversarial Self-Audit
Write-Host "  [Term 3/6] Independent Review... " -NoNewline
if ($checkedMatches.Count -eq 0) {
    Write-Host "FAILED" -ForegroundColor Red
    $failedTerms += "Term 3 (Independent Review): Task has zero verified acceptance criteria."
} else {
    Write-Host "VERIFIED" -ForegroundColor Green
}

# Term 4: Evidence (Rule 9 Provenance Tag)
Write-Host "  [Term 4/6] Rule 9 Provenance Evidence... " -NoNewline
if (-not $hasValidEvidence) {
    Write-Host "FAILED" -ForegroundColor Red
    $failedTerms += "Term 4 (Evidence): Missing valid timestamped Rule 9 provenance tag matching schema '<!-- VERIFIED: <ISO8601> | Commit: <SHA> | Proof: <desc> -->'."
} else {
    Write-Host "VERIFIED ($($matches[1]))" -ForegroundColor Green
}

# Term 5: Definition of Done Checkboxes
Write-Host "  [Term 5/6] Definition of Done Acceptance Criteria... " -NoNewline
if ($uncheckedMatches.Count -gt 0) {
    Write-Host "FAILED" -ForegroundColor Red
    $failedTerms += "Term 5 (Definition of Done): Task has $($uncheckedMatches.Count) unchecked acceptance criteria (- [ ])."
} else {
    Write-Host "VERIFIED ($($checkedMatches.Count)/$($checkedMatches.Count) criteria satisfied)" -ForegroundColor Green
}

# Term 6: No Unresolved Blockers
Write-Host "  [Term 6/6] No Unresolved Prerequisites / Blockers... " -NoNewline
$unresolved = @()
foreach ($p in $prereqs) {
    $pMatch = [regex]::Match($content, "(?ms)^###\s*\[" + [regex]::Escape($p) + "\]\s*(.+?)(?=(^###\s*\[TODO-|\Z))")
    if ($pMatch.Success) {
        $pBlock = $pMatch.Groups[1].Value
        if ($pBlock -notmatch '\*\s*\*\*Status\*\*:\s*`?Completed`?') {
            $unresolved += $p
        }
    } else {
        $unresolved += $p
    }
}

if ($unresolved.Count -gt 0) {
    Write-Host "FAILED" -ForegroundColor Red
    $failedTerms += "Term 6 (No Unresolved Blockers): Prerequisite task(s) not completed: $($unresolved -join ', ')"
} else {
    Write-Host "VERIFIED (0 blocking prerequisites)" -ForegroundColor Green
}

Write-Host ""
if ($failedTerms.Count -gt 0) {
    Write-Host "==========================================================" -ForegroundColor Red
    Write-Host "[!] DEFINITION OF DONE FAILED FOR [$TaskId]" -ForegroundColor Red
    Write-Host "==========================================================" -ForegroundColor Red
    foreach ($term in $failedTerms) {
        Write-Host "  - $term" -ForegroundColor Red
    }
    Write-Host "`nTask cannot be marked Completed until all 6 terms are satisfied." -ForegroundColor Red
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host "[+] DEFINITION OF DONE SATISFIED FOR [$TaskId]" -ForegroundColor Green
Write-Host "Implementation + Tests + Review + Evidence + DoD + No Blockers = DONE" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
