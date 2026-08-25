<#
.SYNOPSIS
    Autonomous 18-step TODO execution loop orchestrator for the Moonshine repository.
.DESCRIPTION
    Continuously executes the highest-priority unblocked task from TODO.md until all tasks
    satisfy the strict Definition of Done (DoD).
.PARAMETER SingleTask
    If specified, stops after completing or verifying a single task rather than looping indefinitely.
#>

[CmdletBinding()]
param(
    [string]$TodoPath = "TODO.md",
    [switch]$SingleTask
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Moonshine Autonomous TODO Program Execution Loop" -ForegroundColor Cyan
Write-Host "Backlog: $TodoPath" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Step 0: Toolchain Environment & Backlog Validation
Write-Host "`n[Step 0] Probing Toolchain & Backlog DAG..." -ForegroundColor Yellow
& "$ScriptDir\verify_environment.ps1"
if ($LASTEXITCODE -ne 0) { throw "Toolchain environment verification failed." }

& "$ScriptDir\verify_todo_backlog.ps1" -TodoPath $TodoPath
if ($LASTEXITCODE -ne 0) { throw "TODO backlog structural validation failed." }

$iteration = 0
$maxIterations = 50

while ($iteration -lt $maxIterations) {
    $iteration++
    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host "Execution Iteration #${iteration}: Querying Next Task..." -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan

    $nextJson = & "$ScriptDir\get_next_todo.ps1" -TodoPath $TodoPath -AsJson | Out-String
    $nextData = $nextJson | ConvertFrom-Json

    if ($nextData.status -eq "AllTasksCompleted" -or -not $nextData.task) {
        Write-Host "`n[+] ALL TASKS IN TODO BACKLOG SATISFY THE STRICT DEFINITION OF DONE (100%)." -ForegroundColor Green
        Write-Host "[+] Backlog execution program complete." -ForegroundColor Green
        break
    }

    $task = $nextData.task
    Write-Host "Selected Actionable Task: [$($task.id)] $($task.title)" -ForegroundColor Green
    Write-Host "  Priority:      $($task.priority)" -ForegroundColor Yellow
    Write-Host "  Scope:         $($task.scope)" -ForegroundColor Gray
    Write-Host "  Objective:     $($task.objective)" -ForegroundColor White

    # Evaluate current DoD state
    Write-Host "`n[Evaluating Task DoD Gatekeeper]:" -ForegroundColor Yellow
    & "$ScriptDir\verify_task_dod.ps1" -TaskId $task.id -TodoPath $TodoPath -SkipBuildTests
    $dodExitCode = $LASTEXITCODE

    if ($dodExitCode -eq 0) {
        Write-Host "[+] Task [$($task.id)] satisfies the Definition of Done." -ForegroundColor Green
        if ($SingleTask) { break }
        continue
    }

    Write-Host "`n[!] Task [$($task.id)] requires implementation, tests, and Rule 9 provenance." -ForegroundColor Yellow
    Write-Host "    Proceeding with orchestrator execution loop..." -ForegroundColor Yellow

    if ($SingleTask) {
        Write-Host "SingleTask flag set. Stopping loop for manual/subagent execution." -ForegroundColor Cyan
        break
    }
}
