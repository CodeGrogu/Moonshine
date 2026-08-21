<#
.SYNOPSIS
    Builds and tests Moonshine on Windows 11 using MSVC C++23/CMake/CTest and .NET 9/xUnit.
#>
[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$LocalDotNetExe = Join-Path $RootDir "tools\dotnet_sdk\dotnet.exe"
$DotNetExe = if (Test-Path $LocalDotNetExe) {
    $LocalDotNetExe
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    (Get-Command dotnet).Source
} else {
    $null
}
$ConfigurePreset = if ($Configuration -eq "Release") { "windows-release-avx2" } else { "windows-debug" }
$BuildDir = if ($Configuration -eq "Release") { Join-Path $RootDir "build\release-avx2" } else { Join-Path $RootDir "build\debug" }

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Moonshine Windows 11 Build and Test [$Configuration]" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

& "$ScriptDir\verify_environment.ps1"
if ($LASTEXITCODE -ne 0) { throw "Windows 11 toolchain verification failed." }
if (-not $DotNetExe) { throw ".NET 9 SDK is absent. Install the version pinned in global.json." }

Write-Host "`n[1/4] Configuring and building the MSVC C++23 native engine..." -ForegroundColor Yellow
& cmake --preset $ConfigurePreset
if ($LASTEXITCODE -ne 0) { throw "CMake configuration failed." }
& cmake --build $BuildDir --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) { throw "Native compilation failed." }

$nativeDll = Join-Path $BuildDir "bin\Moonshine.Native.dll"
if (-not (Test-Path $nativeDll)) {
    throw "Native build reported success but Moonshine.Native.dll is absent: $nativeDll"
}
Write-Host "[+] Native Windows artifact verified: $nativeDll" -ForegroundColor Green

if (-not $SkipTests) {
    Write-Host "`n[2/4] Running CTest native test suite..." -ForegroundColor Yellow
    & ctest --test-dir $BuildDir --build-config $Configuration --output-on-failure --no-tests=error
    if ($LASTEXITCODE -ne 0) { throw "CTest native suite failed." }
}

Write-Host "`n[3/4] Restoring and building .NET 9 Windows 11 solution..." -ForegroundColor Yellow
& $DotNetExe restore "$RootDir\Moonshine.sln" --runtime win-x64
if ($LASTEXITCODE -ne 0) { throw ".NET restore failed." }
& $DotNetExe build "$RootDir\Moonshine.sln" -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw ".NET build failed." }

$hostDll = Get-ChildItem -Path "$RootDir\src\Moonshine.Host\bin\$Configuration" -Recurse -Filter "Moonshine.Host.dll" -File | Select-Object -First 1
if (-not $hostDll) {
    throw "Managed build reported success but Moonshine.Host.dll is absent."
}
Write-Host "[+] Managed Windows artifact verified: $($hostDll.FullName)" -ForegroundColor Green

if (-not $SkipTests) {
    Get-ChildItem -Path "$RootDir\src", "$RootDir\tests" -Recurse -Directory | Where-Object {
        $_.FullName -match '[\\/]bin[\\/]'
    } | ForEach-Object {
        Copy-Item $nativeDll $_.FullName -Force
    }

    $env:PATH = "$(Split-Path -Parent $nativeDll);$env:PATH"
    $resultsDirectory = Join-Path $RootDir "TestResults\$Configuration"
    Write-Host "`n[4/4] Running .NET 9 xUnit test suite through dotnet test..." -ForegroundColor Yellow
    & $DotNetExe test "$RootDir\Moonshine.sln" -c $Configuration --no-build --no-restore --arch x64 --logger "console;verbosity=normal" --results-directory $resultsDirectory
    if ($LASTEXITCODE -ne 0) { throw ".NET xUnit test suite failed." }
}

Write-Host "`n[+] Windows 11 build and standardised native/managed tests completed successfully." -ForegroundColor Green
