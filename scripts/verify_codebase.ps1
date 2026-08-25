<#
.SYNOPSIS
    Runs toolchain environment verification, preflight sweep, compiles native/managed libraries, and executes test suites.
#>
[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Moonshine Complete Repository Verification Pipeline" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Step -1: Toolchain and Environment Verification Probe
Write-Host "`n[Step -1] Probing MSVC Toolchain and Standard Header Resolution..." -ForegroundColor Yellow
& "$ScriptDir\verify_environment.ps1"
if ($LASTEXITCODE -ne 0) { throw "Toolchain environment verification failed." }

# Step 0: Pre-Commit Preflight Scanner (Rule 4)
Write-Host "`n[Step 0] Executing Repository Preflight Scanner..." -ForegroundColor Yellow
& "$ScriptDir\preflight.ps1"
if ($LASTEXITCODE -ne 0) { throw "Preflight scanner failed." }

# Step 0.5: TODO Backlog Verification
Write-Host "`n[Step 0.5] Validating TODO Backlog Schema and Dependencies..." -ForegroundColor Yellow
& "$ScriptDir\verify_todo_backlog.ps1"
if ($LASTEXITCODE -ne 0) { throw "TODO backlog validation failed." }

# Steps 1-4: Native Build, CTests, Managed Build, and xUnit Suites
Write-Host "`n[Steps 1-4] Executing Unified Build & Test Pipeline ($Configuration)..." -ForegroundColor Yellow
& "$ScriptDir\build.ps1" -Configuration $Configuration

Write-Host "`n[+] All Toolchain, Preflight, Native CTest, and Managed xUnit suites verified (100%)." -ForegroundColor Green
Write-Host "[+] Repository Verification Complete: All systems healthy and compliant." -ForegroundColor Green
