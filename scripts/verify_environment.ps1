# ==============================================================================
# Moonshine Toolchain & Environment Verification Probe (Step -1)
# ==============================================================================
# Verifies the Windows 11, MSVC C++23, CMake, CTest, Ninja, and .NET 9
# toolchain required by Moonshine.
# ==============================================================================

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$localDotNetExe = Join-Path $repoRoot "tools\dotnet_sdk\dotnet.exe"
$dotnetExe = if (Test-Path $localDotNetExe) {
    $localDotNetExe
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    (Get-Command dotnet).Source
} else {
    $null
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Moonshine Toolchain & Environment Verification" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$os = Get-CimInstance -ClassName Win32_OperatingSystem
if ($os.Version -notmatch '^10\.0\.' -or [int]$os.BuildNumber -lt 22000) {
    Write-Error "[!] Moonshine requires Windows 11 version 21H2 (build 22000) or later. Detected: $($os.Caption) build $($os.BuildNumber)."
    exit 1
}
Write-Host "[+] Windows 11 requirement verified: $($os.Caption) build $($os.BuildNumber)." -ForegroundColor Green

# 1. Check if cl.exe is on PATH; if not, attempt auto-initialisation of developer shell
if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    Write-Host "[*] cl.exe not found on PATH. Attempting auto-initialisation..." -ForegroundColor Yellow
    
    $vsPaths = @(
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\Launch-VsDevShell.ps1",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\Launch-VsDevShell.ps1",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\Tools\Launch-VsDevShell.ps1",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\Tools\Launch-VsDevShell.ps1"
    )

    $initialized = $false
    foreach ($path in $vsPaths) {
        if (Test-Path $path) {
            Write-Host "[+] Initialising Visual Studio environment via $path" -ForegroundColor Green
            & $path -Arch amd64 -HostArch amd64 | Out-Null
            $initialized = $true
            break
        }
    }

    if (-not $initialized) {
        # Fallback to vcvars64.bat extraction
        $vcvarsPaths = @(
            "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat",
            "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat",
            "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat",
            "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat"
        )
        foreach ($vcvars in $vcvarsPaths) {
            if (Test-Path $vcvars) {
                Write-Host "[+] Importing environment variables via $vcvars" -ForegroundColor Green
                $cmdOutput = & cmd.exe /c "call `"$vcvars`" && set"
                foreach ($line in $cmdOutput) {
                    if ($line -match "^([^=]+)=(.*)$") {
                        [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2], "Process")
                    }
                }
                $initialized = $true
                break
            }
        }
    }

    if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
        Write-Error "[!] cl.exe is not available on PATH. Please run from 'Developer PowerShell for VS 2022' or install VS 2022 C++ Build Tools."
        exit 1
    }
}

# 2. Compile and link a real C++ test binary probing standard library headers
$tempDir = [System.IO.Path]::GetTempPath()
$testCpp = Join-Path $tempDir "moonshine_env_probe.cpp"
$testExe = Join-Path $tempDir "moonshine_env_probe.exe"

$probeSource = @"
#include <cstdint>
#include <expected>
#include <iostream>
#include <vector>
#include <string>

int main() {
    uint32_t magic = 0x4D4F4F4E; // MOON
    std::expected<uint32_t, int> value = magic;
    std::cout << "MOONSHINE_CPP23_OK_" << value.value() << std::endl;
    return 0;
}
"@

try {
    [System.IO.File]::WriteAllText($testCpp, $probeSource, [System.Text.Encoding]::ASCII)

    if (Test-Path $testExe) {
        Remove-Item -Force $testExe
    }

    # Execute cl.exe to compile and link
    $clOutput = & cl.exe /nologo /EHsc /std:c++latest $testCpp /Fe:$testExe 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $testExe)) {
        Write-Host $clOutput -ForegroundColor Red
        Write-Error "[!] MSVC toolchain failed to compile standard C++ program. Standard headers (<iostream>, <cstdint>) could not be resolved. Please verify INCLUDE / LIB environment variables."
        exit 1
    }

    # Execute the compiled binary to confirm successful execution
    $runOutput = & $testExe
    if ($runOutput -notmatch "MOONSHINE_CPP23_OK_1297043278") {
        Write-Error "[!] Probe binary execution failed or returned invalid output: $runOutput"
        exit 1
    }

    Write-Host "[+] MSVC C++23 language support, standard headers, and linker verified." -ForegroundColor Green
}
finally {
    if (Test-Path $testCpp) { Remove-Item -Force $testCpp -ErrorAction SilentlyContinue }
    if (Test-Path $testExe) { Remove-Item -Force $testExe -ErrorAction SilentlyContinue }
    $objFile = [System.IO.Path]::ChangeExtension($testCpp, ".obj")
    if (Test-Path $objFile) { Remove-Item -Force $objFile -ErrorAction SilentlyContinue }
}

foreach ($tool in @("cmake", "ctest", "ninja")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Error "[!] Required Windows verification tool '$tool' was not found on PATH."
        exit 1
    }
}

$cmakeVersion = (& cmake --version | Select-Object -First 1)
if ($cmakeVersion -notmatch 'version\s+(\d+)\.(\d+)') {
    Write-Error "[!] Unable to determine CMake version: $cmakeVersion"
    exit 1
}
if (([int]$matches[1] -lt 3) -or (([int]$matches[1] -eq 3) -and ([int]$matches[2] -lt 25))) {
    Write-Error "[!] Moonshine requires CMake 3.25 or later. Detected: $cmakeVersion"
    exit 1
}

if (-not $dotnetExe) {
    Write-Error "[!] Required .NET 9 SDK was not found. Install it using the pinned global.json version."
    exit 1
}

$dotnetVersion = (& $dotnetExe --version).Trim()
if ($dotnetVersion -notmatch '^9\.') {
    Write-Error "[!] Moonshine requires a .NET 9 SDK. Detected: $dotnetVersion"
    exit 1
}

Write-Host "[+] Standard Windows test tools verified: $cmakeVersion, Ninja, CTest, repository .NET SDK $dotnetVersion." -ForegroundColor Green

exit 0
