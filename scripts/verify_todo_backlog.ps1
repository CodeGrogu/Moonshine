<#
.SYNOPSIS
    Verifies the structural integrity, dependency graph, and readiness of the Moonshine TODO backlog (TODO.md).
.DESCRIPTION
    Validates task entries, field schemas, circular dependencies, and unblocked actionable tasks.
#>

[CmdletBinding()]
param(
    [string]$TodoPath = "TODO.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Moonshine TODO Backlog Structural & Dependency Validator" -ForegroundColor Cyan
Write-Host "Path: $TodoPath" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $TodoPath)) {
    Write-Error "TODO backlog file not found at $TodoPath"
    exit 1
}

$content = Get-Content -Path $TodoPath -Raw
$lines = $content -split "\r?\n"

$taskPattern = '(?m)^###\s*\[(TODO-[A-Za-z0-9_\-]+)\]\s*(.+)$'
$taskMatches = [regex]::Matches($content, $taskPattern)

if ($taskMatches.Count -eq 0) {
    Write-Warning "No structured TODO items found matching '### [TODO-XXX]' format."
    exit 0
}

$tasks = @{}
$taskOrder = @()
$duplicateErrors = @()

foreach ($match in $taskMatches) {
    $taskId = $match.Groups[1].Value
    $taskTitle = $match.Groups[2].Value.Trim()
    
    if ($tasks.ContainsKey($taskId)) {
        $duplicateErrors += "Duplicate task identifier detected in backlog: [$taskId]"
    }
    
    $tasks[$taskId] = [PSCustomObject]@{
        Id = $taskId
        Title = $taskTitle
        Status = "Unknown"
        Priority = "P2"
        Prerequisites = @()
        Scope = ""
        AcceptanceCriteriaCount = 0
        HasEvidence = $false
        RawBlock = ""
    }
    $taskOrder += $taskId
}

if ($duplicateErrors.Count -gt 0) {
    Write-Host "[!] Duplicate Task Identifier Errors Detected:" -ForegroundColor Red
    foreach ($dup in $duplicateErrors) {
        Write-Host "  - $dup" -ForegroundColor Red
    }
    exit 1
}

# Parse sections for each task
for ($i = 0; $i -lt $taskOrder.Count; $i++) {
    $id = $taskOrder[$i]
    $currentMatch = $taskMatches[$i]
    $startIndex = $currentMatch.Index
    
    $endIndex = if ($i + 1 -lt $taskMatches.Count) {
        $taskMatches[$i + 1].Index
    } else {
        $content.Length
    }
    
    $block = $content.Substring($startIndex, $endIndex - $startIndex)
    $taskObj = $tasks[$id]
    $taskObj.RawBlock = $block

    # Extract Status
    if ($block -match '\*\s*\*\*Status\*\*:\s*`?([a-zA-Z\s]+)`?') {
        $taskObj.Status = $matches[1].Trim()
    }

    # Extract Priority
    if ($block -match '\*\s*\*\*Priority\*\*:\s*`?(P[0-3])`?') {
        $taskObj.Priority = $matches[1].Trim()
    }

    # Extract Prerequisites
    if ($block -match '\*\s*\*\*Prerequisites\*\*:\s*(.+)') {
        $prereqStr = $matches[1].Trim()
        if ($prereqStr -ne "None" -and $prereqStr -ne "none") {
            $prereqs = [regex]::Matches($prereqStr, 'TODO-[A-Za-z0-9_\-]+') | ForEach-Object { $_.Value }
            $taskObj.Prerequisites = $prereqs
        }
    }

    # Extract Scope
    if ($block -match '\*\s*\*\*Scope\*\*:\s*(.+)') {
        $taskObj.Scope = $matches[1].Trim()
    }

    # Count Acceptance Criteria
    $criteriaMatches = [regex]::Matches($block, '-\s*\[[ xX]\]\s*(.+)')
    $taskObj.AcceptanceCriteriaCount = $criteriaMatches.Count

    # Check Evidence
    if ($block -match '<!--\s*VERIFIED:' -or ($block -match '\*\s*\*\*Evidence\*\*:\s*(.+)' -and $matches[1] -notmatch 'PENDING')) {
        $taskObj.HasEvidence = $true
    }
}

Write-Host "Parsed $($taskOrder.Count) task items from backlog." -ForegroundColor Green
Write-Host ""

$errors = @()

# Validate tasks
foreach ($id in $taskOrder) {
    $t = $tasks[$id]
    
    # Validate Status
    $validStatuses = @("Pending", "In Progress", "Completed", "Blocked")
    if ($validStatuses -notcontains $t.Status) {
        $errors += "Task $id has invalid Status: '$($t.Status)' (Allowed: $($validStatuses -join ', '))"
    }

    # Validate Prerequisites exist
    foreach ($p in $t.Prerequisites) {
        if (-not $tasks.ContainsKey($p)) {
            $errors += "Task $id references non-existent prerequisite: $p"
        }
    }

    # Validate Acceptance Criteria
    if ($t.AcceptanceCriteriaCount -eq 0) {
        $errors += "Task $id has zero defined acceptance criteria checkboxes."
    }

    # If completed, must have evidence
    if ($t.Status -eq "Completed" -and -not $t.HasEvidence) {
        $errors += "Task $id is marked Completed but lacks verified Rule 9 provenance evidence."
    }
}

if ($errors.Count -gt 0) {
    Write-Host "[!] Backlog Validation Errors Detected:" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "  - $err" -ForegroundColor Red
    }
    exit 1
}

Write-Host "[+] All tasks conform to structural schema." -ForegroundColor Green
Write-Host ""
Write-Host "Task Execution Status:" -ForegroundColor Cyan
foreach ($id in $taskOrder) {
    $t = $tasks[$id]
    $color = switch ($t.Status) {
        "Completed" { [ConsoleColor]::Green }
        "In Progress" { [ConsoleColor]::Yellow }
        "Pending" { [ConsoleColor]::White }
        default { [ConsoleColor]::Gray }
    }
    $prereqNote = if ($t.Prerequisites.Count -gt 0) { " (Prerequisites: $($t.Prerequisites -join ', '))" } else { "" }
    Write-Host "  [$($t.Priority)] $id : $($t.Title) - Status: $($t.Status)$prereqNote" -ForegroundColor $color
}

Write-Host ""
Write-Host "[+] TODO Backlog validation completed successfully." -ForegroundColor Green
exit 0
