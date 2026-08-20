<#
.SYNOPSIS
    Builds the complete Moonshine stack: Native C++23 (CMake + Ninja + MSVC) and .NET 9 Managed projects.
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
$BuildDir = Join-Path $RootDir "build"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Moonshine Unified Build Engine [$Configuration]" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Locate Build Tools
$VcvarsBat = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
$CMakeExe = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$NinjaExe = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
$CTestExe = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe"

# Locate DotNet SDK
$DotNetExe = if (Test-Path "$RootDir\tools\dotnet_sdk\dotnet.exe") {
    "$RootDir\tools\dotnet_sdk\dotnet.exe"
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    "dotnet"
} else {
    $null
}

# 1. Build Native C++ Library
Write-Host "`n[1/4] Building Moonshine.Native C++23 Library..." -ForegroundColor Yellow
$cmakeConfigCmd = "call `"$VcvarsBat`" && `"$CMakeExe`" -B `"$BuildDir`" -S `"$RootDir`" -G `"Ninja`" -DCMAKE_MAKE_PROGRAM=`"$NinjaExe`" -DCMAKE_BUILD_TYPE=$Configuration -DMOONSHINE_ENABLE_AVX2=ON -DMOONSHINE_BUILD_TESTS=ON"
cmd.exe /c $cmakeConfigCmd
if ($LASTEXITCODE -ne 0) { throw "CMake configuration failed." }

$cmakeBuildCmd = "call `"$VcvarsBat`" && `"$CMakeExe`" --build `"$BuildDir`" --config $Configuration --parallel"
cmd.exe /c $cmakeBuildCmd
if ($LASTEXITCODE -ne 0) { throw "Native compilation failed." }

# 2. Run Native Tests
if (-not $SkipTests) {
    Write-Host "`n[2/4] Executing Native CTest Suite..." -ForegroundColor Yellow
    $nativeDllDir = Join-Path $BuildDir "src\Moonshine.Native"
    $ctestCmd = "call `"$VcvarsBat`" && set PATH=$nativeDllDir;%PATH% && `"$CTestExe`" --test-dir `"$BuildDir`" --output-on-failure -C $Configuration"
    cmd.exe /c $ctestCmd
    if ($LASTEXITCODE -ne 0) { throw "Native tests failed." }
}

# 3. Build Managed .NET Solution
if ($DotNetExe) {
    Write-Host "`n[3/4] Building Managed .NET Solution ($Configuration)..." -ForegroundColor Yellow
    & $DotNetExe build "$RootDir\Moonshine.sln" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw ".NET build failed." }

    # 4. Run Managed Tests
    if (-not $SkipTests) {
        Write-Host "`n[4/4] Executing .NET Test Suites..." -ForegroundColor Yellow
        & $DotNetExe test "$RootDir\Moonshine.sln" -c $Configuration --no-build --verbosity normal
        if ($LASTEXITCODE -ne 0) { throw ".NET tests failed." }
    }
} else {
    Write-Host "`n[!] Warning: .NET SDK not detected. Skipping managed build." -ForegroundColor Yellow
}

Write-Host "`n[+] Build and test execution completed successfully!" -ForegroundColor Green
