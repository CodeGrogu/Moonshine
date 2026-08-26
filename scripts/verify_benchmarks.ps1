<#
.SYNOPSIS
    Automated Performance Regression and Zero-Allocation Gatekeeper for Moonshine.
    Validates microbenchmark invariants, zero-allocation assertions, and latency budgets.
#>
[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Moonshine Performance Regression & Allocation Gatekeeper" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$LocalDotNetExe = Join-Path $RootDir "tools\dotnet_sdk\dotnet.exe"
$DotNetExe = if (Test-Path $LocalDotNetExe) {
    $LocalDotNetExe
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    (Get-Command dotnet).Source
} else {
    throw ".NET 9 SDK is absent. Install the version pinned in global.json."
}

Write-Host "`n[*] Verifying native interop performance and memory ownership invariants..." -ForegroundColor Yellow
& $DotNetExe test "$RootDir\tests\Moonshine.Interop.Tests\Moonshine.Interop.Tests.csproj" -c $Configuration --filter "FullyQualifiedName~FecNative|FullyQualifiedName~NativeMemoryOwner|FullyQualifiedName~PinnedBuffer" --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { throw "Native interop memory and SIMD performance tests failed." }

Write-Host "`n[*] Verifying end-to-end transport latency and media reassembly measurements..." -ForegroundColor Yellow
& $DotNetExe test "$RootDir\tests\Moonshine.Core.Tests\Moonshine.Core.Tests.csproj" -c $Configuration --filter "FullyQualifiedName~LoopbackTransport|FullyQualifiedName~MonotonicClock|FullyQualifiedName~MediaPacketiser" --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { throw "Loopback transport latency and media measurement tests failed." }

Write-Host "`n[+] All Performance Invariants and Zero-Allocation Gates PASSED." -ForegroundColor Green
