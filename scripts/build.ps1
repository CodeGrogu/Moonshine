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

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "⚡ Moonshine Unified Build Engine [$Configuration]" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Locate Build Tools
$VcvarsBat = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
$CMakeExe = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$NinjaExe = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
$CTestExe = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe"

# 1. Build Native C++ Library
Write-Host "`n[1/3] Building Moonshine.Native C++23 Library..." -ForegroundColor Yellow
$cmakeConfigCmd = "call `"$VcvarsBat`" && `"$CMakeExe`" -B build -S . -G `"Ninja`" -DCMAKE_MAKE_PROGRAM=`"$NinjaExe`" -DCMAKE_BUILD_TYPE=$Configuration -DMOONSHINE_ENABLE_AVX2=ON -DMOONSHINE_BUILD_TESTS=ON"
cmd.exe /c $cmakeConfigCmd
if ($LASTEXITCODE -ne 0) { throw "CMake configuration failed." }

$cmakeBuildCmd = "call `"$VcvarsBat`" && `"$CMakeExe`" --build build --config $Configuration --parallel"
cmd.exe /c $cmakeBuildCmd
if ($LASTEXITCODE -ne 0) { throw "Native compilation failed." }

# 2. Run Native Tests
if (-not $SkipTests) {
    Write-Host "`n[2/3] Executing Native CTest Suite..." -ForegroundColor Yellow
    $ctestCmd = "call `"$VcvarsBat`" && set PATH=%CD%\build\src\Moonshine.Native;%PATH% && `"$CTestExe`" --test-dir build --output-on-failure -C $Configuration"
    cmd.exe /c $ctestCmd
    if ($LASTEXITCODE -ne 0) { throw "Native tests failed." }
}

Write-Host "`n[3/3] Build Complete! Artifacts generated in build/." -ForegroundColor Green
