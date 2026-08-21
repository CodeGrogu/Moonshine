# install_virtual_audio_driver.ps1
# Automated Installation and Registration for Moonshine Virtual Audio Driver
# Copyright (c) 2026 Moonshine Stream Project. All rights reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$DriverDir = "$PSScriptRoot\..\drivers\audio"
)

$ErrorActionPreference = "Stop"

Write-Host "[*] Moonshine Virtual Audio Driver Installer" -ForegroundColor Cyan

# Verify Administrator Privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warning "[-] Administrator elevation is required to install kernel-mode audio drivers."
    Write-Warning "[-] Please re-run this script in an elevated PowerShell session."
    exit 1
}

$infPath = Join-Path $DriverDir "MoonshineAudio.inf"
if (-not (Test-Path $infPath)) {
    Write-Error "[-] INF file not found at: $infPath"
    exit 1
}

Write-Host "[+] Staging driver package: $infPath" -ForegroundColor Green

# 1. Add driver package to driver store via PnPUtil
Write-Host "[*] Adding driver to Windows Driver Store via PnPUtil..." -ForegroundColor Yellow
$pnpOutput = & pnputil.exe /add-driver "$infPath" /install
Write-Host $pnpOutput

# 2. Check for devcon or root-enumerated device instantiation
$devconPath = (Get-Command "devcon.exe" -ErrorAction SilentlyContinue)?.Source
if ($devconPath) {
    Write-Host "[*] Instantiating root software device node via devcon..." -ForegroundColor Yellow
    & "$devconPath" install "$infPath" "ROOT\MoonshineAudio"
} else {
    Write-Host "[*] devcon.exe not detected in PATH. Driver installed via PnPUtil." -ForegroundColor Yellow
}

Write-Host "[+] Moonshine Virtual Audio Driver installation process completed." -ForegroundColor Green
