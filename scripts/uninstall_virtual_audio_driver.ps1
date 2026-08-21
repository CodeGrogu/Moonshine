# uninstall_virtual_audio_driver.ps1
# Automated Removal and Clean-up for Moonshine Virtual Audio Driver
# Copyright (c) 2026 Moonshine Stream Project. All rights reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$DriverName = "MoonshineAudio.inf"
)

$ErrorActionPreference = "Stop"

Write-Host "[*] Moonshine Virtual Audio Driver Uninstaller" -ForegroundColor Cyan

# Verify Administrator Privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warning "[-] Administrator elevation is required to remove kernel-mode audio drivers."
    Write-Warning "[-] Please re-run this script in an elevated PowerShell session."
    exit 1
}

# 1. Remove device node via devcon if present
$devconPath = (Get-Command "devcon.exe" -ErrorAction SilentlyContinue)?.Source
if ($devconPath) {
    Write-Host "[*] Removing root software device node via devcon..." -ForegroundColor Yellow
    & "$devconPath" remove "ROOT\MoonshineAudio"
}

# 2. Enumerate and delete OEM package via PnPUtil
Write-Host "[*] Searching Windows Driver Store for published OEM packages matching $DriverName..." -ForegroundColor Yellow
$drivers = & pnputil.exe /enum-drivers
$oemName = $null

$lines = $drivers -split "`r?`n"
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "Original Name:\s+MoonshineAudio.inf") {
        # Check preceding line for Published Name
        if ($i -gt 0 -and $lines[$i-1] -match "Published Name:\s+(oem\d+\.inf)") {
            $oemName = $Matches[1]
            break
        }
    }
}

if ($oemName) {
    Write-Host "[*] Removing driver package $oemName via PnPUtil..." -ForegroundColor Yellow
    & pnputil.exe /delete-driver "$oemName" /uninstall /force
    Write-Host "[+] Driver package $oemName removed successfully." -ForegroundColor Green
} else {
    Write-Host "[*] No active published OEM package found for MoonshineAudio.inf." -ForegroundColor Yellow
}

Write-Host "[+] Moonshine Virtual Audio Driver uninstallation completed." -ForegroundColor Green
