# ==============================================================================
# Moonshine Preflight Fixture Test Suite (Rule 3: Value & Detection Proof)
# ==============================================================================
# Verifies that scripts/preflight.ps1 correctly catches violations across all
# categories and respects valid >= 15 char justification comments.
# ==============================================================================

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$preflightScript = Join-Path $repoRoot "scripts\preflight.ps1"
$tempFixtureDir = Join-Path ([System.IO.Path]::GetTempPath()) "moonshine_preflight_fixtures"

if (Test-Path $tempFixtureDir) {
    Remove-Item -Recurse -Force $tempFixtureDir
}
New-Item -ItemType Directory -Path $tempFixtureDir | Out-Null

$passCount = 0
$failCount = 0

function Assert-Fixture([string]$name, [string]$content, [string]$filename, [bool]$expectedSuccess) {
    Write-Host "`n[Test] Fixture: $name (Expected Success: $expectedSuccess)..." -ForegroundColor Cyan
    
    $fixtureSubDir = Join-Path $tempFixtureDir $name
    New-Item -ItemType Directory -Path $fixtureSubDir | Out-Null
    $filePath = Join-Path $fixtureSubDir $filename
    [System.IO.File]::WriteAllText($filePath, $content, [System.Text.Encoding]::UTF8)

    $output = & powershell -ExecutionPolicy Bypass -File $preflightScript -ScanPath $fixtureSubDir 2>&1
    $exitCode = $LASTEXITCODE

    if ($expectedSuccess -and $exitCode -eq 0) {
        Write-Host "  [PASS] Correctly passed clean/justified fixture." -ForegroundColor Green
        $global:passCount++
    } elseif (-not $expectedSuccess -and $exitCode -ne 0) {
        Write-Host "  [PASS] Correctly flagged violation and exited with code $exitCode." -ForegroundColor Green
        $global:passCount++
    } else {
        Write-Host "  [FAIL] Unexpected exit code $exitCode for fixture: $name" -ForegroundColor Red
        Write-Host "  Output: $output" -ForegroundColor Yellow
        $global:failCount++
    }
}

try {
    # 1. Unannotated stub
    Assert-Fixture -name "unannotated_stub" `
                   -content "public class Test { public void Run() { var x = simulated_frame; } }" `
                   -filename "test_stub.cs" `
                   -expectedSuccess $false

    # 2. Lazy justification (< 15 chars)
    Assert-Fixture -name "lazy_justification" `
                   -content "public class Test { // STUB: fix later`npublic void Run() { var x = simulated_frame; } }" `
                   -filename "test_lazy.cs" `
                   -expectedSuccess $false

    # 3. Valid justification (>= 15 chars)
    Assert-Fixture -name "valid_justification" `
                   -content "public class Test { // STUB: Hardware NVENC encoder unavailable on test runner`npublic void Run() { var x = simulated_frame; } }" `
                   -filename "test_valid_stub.cs" `
                   -expectedSuccess $true

    # 4. Swallowed exception
    Assert-Fixture -name "swallowed_exception" `
                   -content "public class Test { public void Run() { try {} catch (Exception) {} } }" `
                   -filename "test_swallow.cs" `
                   -expectedSuccess $false

    # 5. Justified exception (>= 15 chars)
    Assert-Fixture -name "justified_exception" `
                   -content "public class Test { public void Run() { try {} // ALLOWED_EXCEPTION: client closed socket connection during stream shutdown`ncatch (Exception) {} } }" `
                   -filename "test_justified_catch.cs" `
                   -expectedSuccess $true

    # 6. Hardcoded secret
    Assert-Fixture -name "hardcoded_secret" `
                   -content "public class Test { string apiKey = ""AKIAIOSFODNN7EXAMPLE""; }" `
                   -filename "test_secret.cs" `
                   -expectedSuccess $false

    # 7. Unapproved TLS bypass
    Assert-Fixture -name "unapproved_tls" `
                   -content "public class Test { public void Run() { var h = new SocketsHttpHandler { SslOptions = new() { RemoteCertificateValidationCallback = (a,b,c,d) => true } }; } }" `
                   -filename "test_tls.cs" `
                   -expectedSuccess $false

    # 8. Unprovenanced metric claim in Markdown
    Assert-Fixture -name "unprovenanced_doc_claim" `
                   -content "# Test Status`nExactly 50 tests passing in test suite." `
                   -filename "status.md" `
                   -expectedSuccess $false

    # 9. Provenanced metric claim in Markdown
    Assert-Fixture -name "provenanced_doc_claim" `
                   -content "# Test Status`n<!-- VERIFIED: 2026-08-21, via dotnet test in Developer PowerShell -->`nExactly 50 tests passing in test suite." `
                   -filename "status_provenance.md" `
                   -expectedSuccess $true

    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host "Preflight Fixture Test Results: $passCount passed, $failCount failed." -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
    Write-Host "==========================================================" -ForegroundColor Cyan

    if ($failCount -gt 0) {
        exit 1
    }
}
finally {
    if (Test-Path $tempFixtureDir) {
        Remove-Item -Recurse -Force $tempFixtureDir -ErrorAction SilentlyContinue
    }
}

exit 0
