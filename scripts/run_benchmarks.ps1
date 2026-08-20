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

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "⚡ Moonshine Performance Benchmarking Suite" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Ensure native binaries are built first
& "$ScriptDir\build.ps1" -Configuration Release -SkipTests

Write-Host "`n[*] Running Micro-Benchmarks (Filter: $Filter)..." -ForegroundColor Yellow
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet run --project src/Moonshine.Benchmarks/Moonshine.Benchmarks.csproj -c Release -- --job short --filter $Filter
} else {
    Write-Host "[!] Note: .NET SDK runner will execute once dotnet is active in PATH." -ForegroundColor Yellow
}
