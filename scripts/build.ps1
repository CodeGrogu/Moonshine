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

. "$ScriptDir\verify_environment.ps1"
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
Get-Process -Name testhost*, vstest* -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
& $DotNetExe restore "$RootDir\Moonshine.sln" --runtime win-x64
if ($LASTEXITCODE -ne 0) { throw ".NET restore failed." }
& $DotNetExe build "$RootDir\Moonshine.sln" -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw ".NET build failed." }

$hostDll = Get-ChildItem -Path "$RootDir\src\Moonshine.Host\bin\$Configuration" -Recurse -Filter "Moonshine.Host.dll" -File | Select-Object -First 1
if (-not $hostDll) {
    throw "Managed build reported success but Moonshine.Host.dll is absent."
}
Write-Host "[+] Managed Windows artifact verified: $($hostDll.FullName)" -ForegroundColor Green

$targetBinDir = Join-Path $RootDir "bin"
if (-not (Test-Path $targetBinDir)) { New-Item -ItemType Directory -Path $targetBinDir -Force | Out-Null }
try {
    & $DotNetExe publish "$RootDir\src\Moonshine.Client\Moonshine.Client.csproj" -c $Configuration -r win-x64 --self-contained true --no-restore -o $targetBinDir
    Write-Host "[+] Staged self-contained Moonshine executable to root: $(Join-Path $targetBinDir 'Moonshine.exe')" -ForegroundColor Green
} catch {
    Write-Host "[-] Warning: Could not stage to root bin: $($_.Exception.Message)" -ForegroundColor Yellow
}

Get-Process -Name testhost*, vstest* -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 250
Get-ChildItem -Path "$RootDir\src", "$RootDir\tests" -Recurse -Directory | Where-Object {
    $_.FullName -match '[\\/]bin[\\/]'
} | ForEach-Object {
    try {
        Copy-Item $nativeDll $_.FullName -Force -ErrorAction Stop
    } catch {
        Get-Process -Name testhost*, vstest* -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 250
        Copy-Item $nativeDll $_.FullName -Force -ErrorAction Stop
    }
}

if (-not $SkipTests) {
    $env:PATH = "$(Split-Path -Parent $nativeDll);$env:PATH"
    $testProjects = @(
        "$RootDir\tests\Moonshine.Protocol.Tests\Moonshine.Protocol.Tests.csproj",
        "$RootDir\tests\Moonshine.Core.Tests\Moonshine.Core.Tests.csproj",
        "$RootDir\tests\Moonshine.Host.Tests\Moonshine.Host.Tests.csproj",
        "$RootDir\tests\Moonshine.Interop.Tests\Moonshine.Interop.Tests.csproj"
    )
    foreach ($testProj in $testProjects) {
        $projName = [System.IO.Path]::GetFileNameWithoutExtension($testProj)
        Write-Host "--> Running $projName test suite..." -ForegroundColor Cyan
        & $DotNetExe test $testProj -c $Configuration --no-build --no-restore --logger "console;verbosity=minimal"
        if ($LASTEXITCODE -ne 0) { throw ".NET xUnit test suite $projName failed." }
    }
}

Write-Host "`n[+] Windows 11 build and standardised native/managed tests completed successfully." -ForegroundColor Green
