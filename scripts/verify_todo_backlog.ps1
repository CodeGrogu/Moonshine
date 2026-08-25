<#
.SYNOPSIS
    Verifies the structural integrity, dependency DAG, and readiness of the Moonshine TODO backlog (TODO.md).
.DESCRIPTION
    Validates task entries, field schemas, topological cycle detection (Kahn's algorithm),
    Rule 9 provenance tags, checkbox consistency, and returns the next unblocked actionable task.
.PARAMETER TodoPath
    Path to the TODO.md backlog file.
.PARAMETER Next
    If specified, selects and prints the highest-priority unblocked actionable task.
.PARAMETER AsJson
    If specified with -Next, outputs the next task as a JSON string.
#>

[CmdletBinding()]
param(
    [string]$TodoPath = "TODO.md",
    [switch]$Next,
    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $Next -or -not $AsJson) {
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "Moonshine TODO Backlog Structural & Dependency Validator" -ForegroundColor Cyan
    Write-Host "Path: $TodoPath" -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan
}

if (-not (Test-Path $TodoPath)) {
    Write-Error "TODO backlog file not found at $TodoPath"
    exit 1
}

$content = Get-Content -Path $TodoPath -Raw

# Detect Git merge conflict markers in backlog
if ($content -match '(?m)^<{7}\s|^={7}$|^>{7}\s') {
    Write-Host "[!] Unresolved Git merge conflict markers (<<<<<<<, =======, >>>>>>>) detected in $TodoPath" -ForegroundColor Red
    exit 1
}

# 1. Multi-Stage Masking: Code Fences, HTML Comments, and Inline Code
# Replace with equal-length whitespace to preserve exact character indices and line offsets
$maskedContent = $content
# Mask fenced code blocks (```...``` and ~~~...~~~)
$maskedContent = [regex]::Replace($maskedContent, '(?s)```.*?```|~~~.*?~~~', { param($m) " " * $m.Length })
# Mask HTML comments (<!--...-->)
$maskedContent = [regex]::Replace($maskedContent, '(?s)<!--.*?-->', { param($m) " " * $m.Length })
# Mask inline code spans (`...`)
$maskedContent = [regex]::Replace($maskedContent, '`[^`\r\n]+`', { param($m) " " * $m.Length })

$taskPattern = '(?m)^###\s*\[(TODO-[A-Za-z0-9_\-]+)\]\s*(.+)$'
$taskMatches = [regex]::Matches($maskedContent, $taskPattern)

if ($taskMatches.Count -eq 0) {
    if (-not $AsJson) {
        Write-Warning "No structured TODO items found matching '### [TODO-XXX]' format."
    }
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
        Status = "Pending"
        Priority = "P2"
        PriorityWeight = 2
        Prerequisites = @()
        Scope = ""
        Objective = ""
        AcceptanceCriteriaCount = 0
        UncheckedCriteriaCount = 0
        CheckedCriteriaCount = 0
        HasValidRule9Provenance = $false
        ProvenanceRecord = ""
        ProvenanceTimestamp = ""
        ProvenanceCommit = ""
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

# 2. Parse task blocks and metadata
for ($i = 0; $i -lt $taskOrder.Count; $i++) {
    $id = $taskOrder[$i]
    $currentMatch = $taskMatches[$i]
    $startIndex = $currentMatch.Index
    
    $endIndex = if ($i + 1 -lt $taskMatches.Count) {
        $taskMatches[$i + 1].Index
    } else {
        $content.Length
    }
    
    # Use raw content for block inspection so we can extract real comments and checkboxes
    $block = $content.Substring($startIndex, $endIndex - $startIndex)
    $taskObj = $tasks[$id]
    $taskObj.RawBlock = $block

    # Check for ambiguous duplicate metadata fields in the same task block
    $statusCount = [regex]::Matches($block, '(?m)^\*\s*\*\*Status\*\*:\s*').Count
    if ($statusCount -gt 1) {
        $duplicateErrors += "Task $id contains multiple ($statusCount) **Status** declarations."
    }

    $priorityCount = [regex]::Matches($block, '(?m)^\*\s*\*\*Priority\*\*:\s*').Count
    if ($priorityCount -gt 1) {
        $duplicateErrors += "Task $id contains multiple ($priorityCount) **Priority** declarations."
    }

    # Extract Status
    if ($block -match '\*\s*\*\*Status\*\*:\s*`?([a-zA-Z\s]+)`?') {
        $taskObj.Status = $matches[1].Trim()
    }

    # Extract Priority & Weight
    if ($block -match '\*\s*\*\*Priority\*\*:\s*`?(P[0-3])`?') {
        $taskObj.Priority = $matches[1].Trim()
        $taskObj.PriorityWeight = switch ($taskObj.Priority) {
            "P0" { 0 }
            "P1" { 1 }
            "P2" { 2 }
            "P3" { 3 }
            default { 2 }
        }
    }

    # Extract Prerequisites
    if ($block -match '\*\s*\*\*Prerequisites\*\*:\s*(.+)') {
        $prereqStr = $matches[1].Trim()
        if ($prereqStr -ne "None" -and $prereqStr -ne "none") {
            $prereqs = [regex]::Matches($prereqStr, 'TODO-[A-Za-z0-9_\-]+') | ForEach-Object { $_.Value }
            $taskObj.Prerequisites = @($prereqs)
        }
    }

    # Extract Scope
    if ($block -match '\*\s*\*\*Scope\*\*:\s*(.+)') {
        $taskObj.Scope = $matches[1].Trim()
    }

    # Extract Objective
    if ($block -match '\*\s*\*\*Objective\*\*:\s*(.+)') {
        $taskObj.Objective = $matches[1].Trim()
    }

    # Count Acceptance Criteria Checkboxes
    $uncheckedMatches = [regex]::Matches($block, '-\s*\[\s*\]\s*(.+)')
    $checkedMatches = [regex]::Matches($block, '-\s*\[[xX]\]\s*(.+)')
    $taskObj.UncheckedCriteriaCount = $uncheckedMatches.Count
    $taskObj.CheckedCriteriaCount = $checkedMatches.Count
    $taskObj.AcceptanceCriteriaCount = $uncheckedMatches.Count + $checkedMatches.Count

    # Strict Rule 9 Provenance Tag Schema Check
    # Pattern: <!-- VERIFIED: <ISO8601> | Commit: <SHA> | Proof: <Description> -->
    $provPattern = '<!--\s*VERIFIED:\s*(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z)\s*\|\s*Commit:\s*([0-9a-fA-F]{7,40})\s*\|\s*Proof:\s*([^>]+?)\s*-->'
    if ($block -match $provPattern) {
        $taskObj.HasValidRule9Provenance = $true
        $taskObj.ProvenanceRecord = $matches[0]
        $taskObj.ProvenanceTimestamp = $matches[1]
        $taskObj.ProvenanceCommit = $matches[2]
    }
}

if ($duplicateErrors.Count -gt 0) {
    Write-Host "[!] Backlog Structural Ambiguity Errors Detected:" -ForegroundColor Red
    foreach ($err in $duplicateErrors) {
        Write-Host "  - $err" -ForegroundColor Red
    }
    exit 1
}

if (-not $Next -or -not $AsJson) {
    Write-Host "Parsed $($taskOrder.Count) task items from backlog." -ForegroundColor Green
    Write-Host ""
}

$errors = @()

# 3. Validate Structural Invariants
foreach ($id in $taskOrder) {
    $t = $tasks[$id]
    
    # Status validity
    $validStatuses = @("Pending", "In Progress", "Completed", "Blocked")
    if ($validStatuses -notcontains $t.Status) {
        $errors += "Task $id has invalid Status: '$($t.Status)' (Allowed: $($validStatuses -join ', '))"
    }

    # Prerequisites existence and self-reference checks
    foreach ($p in $t.Prerequisites) {
        if ($p -eq $id) {
            $errors += "Task $id lists itself as a prerequisite (self-referential loop)."
        } elseif (-not $tasks.ContainsKey($p)) {
            $errors += "Task $id references non-existent prerequisite: $p"
        }
    }

    # Acceptance Criteria presence
    if ($t.AcceptanceCriteriaCount -eq 0) {
        $errors += "Task $id has zero defined acceptance criteria checkboxes."
    }

    # Checkbox vs Status Invariant Enforcement
    if ($t.Status -eq "Completed") {
        if ($t.UncheckedCriteriaCount -gt 0) {
            $errors += "Task $id is marked 'Completed' but has $($t.UncheckedCriteriaCount) unchecked acceptance criteria (- [ ])."
        }
        if (-not $t.HasValidRule9Provenance) {
            $errors += "Task $id is marked 'Completed' but lacks a valid Rule 9 provenance tag (Format: <!-- VERIFIED: YYYY-MM-DDTHH:MM:SSZ | Commit: <SHA> | Proof: <desc> -->)."
        } else {
            # Timestamp sanity check: must parse as valid ISO 8601 and not be in future
            try {
                $parsedDate = [DateTime]::Parse($t.ProvenanceTimestamp, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AdjustToUniversal)
                if ($parsedDate -gt [DateTime]::UtcNow.AddHours(2)) {
                    $errors += "Task $id has Rule 9 provenance timestamp in the future: $($t.ProvenanceTimestamp)"
                }
            } catch {
                $errors += "Task $id has unparseable Rule 9 provenance timestamp: $($t.ProvenanceTimestamp)"
            }
        }
    } else {
        # If not completed, cannot have all checkboxes checked without status transition
        if ($t.AcceptanceCriteriaCount -gt 0 -and $t.UncheckedCriteriaCount -eq 0 -and $t.Status -eq "Pending") {
            $errors += "Task $id has all criteria checked (- [x]) but Status is still 'Pending' (State Drift)."
        }
    }
}

# 4. Topological Sort & Dependency Cycle Detection (Kahn's Algorithm)
$inDegree = @{}
$adjacency = @{}

foreach ($id in $taskOrder) {
    $inDegree[$id] = 0
    $adjacency[$id] = [System.Collections.Generic.List[string]]::new()
}

foreach ($id in $taskOrder) {
    $t = $tasks[$id]
    foreach ($p in $t.Prerequisites) {
        if ($tasks.ContainsKey($p)) {
            $adjacency[$p].Add($id)
            $inDegree[$id]++
        }
    }
}

$queue = [System.Collections.Generic.Queue[string]]::new()
foreach ($id in $taskOrder) {
    if ($inDegree[$id] -eq 0) {
        $queue.Enqueue($id)
    }
}

$topoOrder = [System.Collections.Generic.List[string]]::new()
while ($queue.Count -gt 0) {
    $curr = $queue.Dequeue()
    $topoOrder.Add($curr)
    
    foreach ($neighbor in $adjacency[$curr]) {
        $inDegree[$neighbor]--
        if ($inDegree[$neighbor] -eq 0) {
            $queue.Enqueue($neighbor)
        }
    }
}

if ($topoOrder.Count -ne $taskOrder.Count) {
    $cycleTasks = $taskOrder | Where-Object { $inDegree[$_] -gt 0 }
    $errors += "Cyclical dependency detected in backlog. Tasks in cycle: $($cycleTasks -join ', ')"
}

if ($errors.Count -gt 0) {
    Write-Host "[!] Backlog Validation Errors Detected:" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "  - $err" -ForegroundColor Red
    }
    exit 1
}

# 5. Output / Next Task Selector
if ($Next) {
    # Find all actionable tasks: Status is Pending or In Progress, and all Prerequisites are Completed
    $actionable = @()
    foreach ($id in $taskOrder) {
        $t = $tasks[$id]
        if ($t.Status -eq "Completed" -or $t.Status -eq "Blocked") {
            continue
        }
        
        $prereqsMet = $true
        foreach ($p in $t.Prerequisites) {
            if ($tasks[$p].Status -ne "Completed") {
                $prereqsMet = $false
                break
            }
        }
        
        if ($prereqsMet) {
            $actionable += $t
        }
    }

    if ($actionable.Count -eq 0) {
        if ($AsJson) {
            Write-Output "{ `"status`": `"AllTasksCompleted`", `"task`": null }"
        } else {
            Write-Host "[+] All actionable tasks in the backlog are completed or blocked." -ForegroundColor Green
        }
        exit 0
    }

    # Sort by PriorityWeight ascending, then backlog order
    $sorted = $actionable | Sort-Object -Property PriorityWeight
    $selected = $sorted[0]

    if ($AsJson) {
        $outObj = [PSCustomObject]@{
            status = "Actionable"
            task = [PSCustomObject]@{
                id = $selected.Id
                title = $selected.Title
                status = $selected.Status
                priority = $selected.Priority
                scope = $selected.Scope
                objective = $selected.Objective
                prerequisites = $selected.Prerequisites
                criteriaCount = $selected.AcceptanceCriteriaCount
            }
        }
        $json = $outObj | ConvertTo-Json -Depth 5
        Write-Output $json
    } else {
        Write-Host "Selected Next Actionable Task:" -ForegroundColor Cyan
        Write-Host "  ID:            $($selected.Id)" -ForegroundColor Yellow
        Write-Host "  Title:         $($selected.Title)" -ForegroundColor White
        Write-Host "  Priority:      $($selected.Priority)" -ForegroundColor Green
        Write-Host "  Status:        $($selected.Status)" -ForegroundColor Gray
        Write-Host "  Scope:         $($selected.Scope)" -ForegroundColor Gray
        Write-Host "  Objective:     $($selected.Objective)" -ForegroundColor Gray
    }
    exit 0
}

Write-Host "[+] All tasks conform to structural schema and topological DAG dependencies." -ForegroundColor Green
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
