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

# Locate DotNet SDK
$DotNetExe = if (Test-Path "$RootDir\tools\dotnet_sdk\dotnet.exe") {
    "$RootDir\tools\dotnet_sdk\dotnet.exe"
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    "dotnet"
} else {
    $null
}

# Ensure native binaries are built first
& "$ScriptDir\build.ps1" -Configuration Release -SkipTests

Write-Host "`n[*] Running Micro-Benchmarks (Filter: $Filter)..." -ForegroundColor Yellow
if ($DotNetExe) {
    & $DotNetExe run --project "$RootDir\src\Moonshine.Benchmarks\Moonshine.Benchmarks.csproj" -c Release -- --job short --filter $Filter
} else {
    Write-Host "[!] Note: .NET SDK runner will execute once dotnet is active in PATH." -ForegroundColor Yellow
}
