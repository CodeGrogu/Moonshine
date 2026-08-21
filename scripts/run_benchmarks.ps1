<#
.SYNOPSIS
    Runs micro-benchmarks for Moonshine native SIMD operations and managed protocol processing.
#>
[CmdletBinding()]
param(
    [string]$Filter = "*"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Moonshine Performance Benchmarking Suite" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$LocalDotNetExe = Join-Path $RootDir "tools\dotnet_sdk\dotnet.exe"
$DotNetExe = if (Test-Path $LocalDotNetExe) {
    $LocalDotNetExe
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    (Get-Command dotnet).Source
} else {
    throw ".NET 9 SDK is absent. Install the version pinned in global.json."
}

# Ensure native binaries are built first
& "$ScriptDir\build.ps1" -Configuration Release -SkipTests
if ($LASTEXITCODE -ne 0) { throw "Windows 11 build prerequisite failed." }

Write-Host "`n[*] Running Micro-Benchmarks (Filter: $Filter)..." -ForegroundColor Yellow
& $DotNetExe run --project "$RootDir\src\Moonshine.Benchmarks\Moonshine.Benchmarks.csproj" -c Release --no-restore -- --job short --filter $Filter
if ($LASTEXITCODE -ne 0) { throw "BenchmarkDotNet execution failed." }
