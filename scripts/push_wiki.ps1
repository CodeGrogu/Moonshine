#!/usr/bin/env pwsh
# Moonshine Wiki Push Script
# Pushes wiki/ directory contents to the GitHub wiki repository.
#
# Prerequisites:
#   - Git authentication configured for https://github.com/CodeGrogu/Moonshine.wiki.git
#   - The GitHub wiki must be initialised (create at least one page via the GitHub web UI first)
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\scripts\push_wiki.ps1

param(
    [string]$WikiRepoUrl = "https://github.com/CodeGrogu/Moonshine.wiki.git",
    [string]$SourceDir = "$PSScriptRoot\..\wiki",
    [string]$TempDir = "$env:TEMP\moonshine-wiki-push"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "`n=== Moonshine Wiki Push ===" -ForegroundColor Cyan
Write-Host "Source: $SourceDir"
Write-Host "Target: $WikiRepoUrl"

# Clean up any previous temp directory
if (Test-Path $TempDir) {
    Remove-Item -Recurse -Force $TempDir
}

# Clone the wiki repository
Write-Host "`nCloning wiki repository..." -ForegroundColor Yellow
git clone $WikiRepoUrl $TempDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to clone wiki repo. Ensure the wiki is initialised on GitHub." -ForegroundColor Red
    Write-Host "Go to https://github.com/CodeGrogu/Moonshine/wiki and create at least one page first." -ForegroundColor Red
    exit 1
}

# Remove existing wiki content (except .git)
Get-ChildItem -Path $TempDir -Exclude ".git" | Remove-Item -Recurse -Force

# Copy wiki source files
Write-Host "Copying wiki source files..." -ForegroundColor Yellow
Copy-Item -Path "$SourceDir\*" -Destination $TempDir -Recurse -Force

# Commit and push
Push-Location $TempDir
try {
    git add -A
    $changes = git diff --cached --name-only
    if ($changes) {
        git commit -m "docs(wiki): synchronise wiki with repository v0.5.6-alpha`n`nTruth audit: added development status disclaimers, fixed identity claims,`nupdated version references, corrected British English, and removed`nfalse operational status assertions across all 32 wiki pages."
        Write-Host "`nPushing to GitHub wiki..." -ForegroundColor Yellow
        git push origin master
        if ($LASTEXITCODE -eq 0) {
            Write-Host "`nWiki pushed successfully!" -ForegroundColor Green
        } else {
            Write-Host "ERROR: Push failed. Check your Git credentials." -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "No changes to push." -ForegroundColor Green
    }
} finally {
    Pop-Location
}

# Clean up
Remove-Item -Recurse -Force $TempDir
Write-Host "`nDone." -ForegroundColor Cyan
