# ==============================================================================
# Moonshine Preflight Scanner: Rule 4 Mechanical Gate
# ==============================================================================
# Scans the codebase for unannotated stubs, potential secrets, swallowed
# exceptions, unapproved TLS bypasses, and unprovenanced metric claims.
# ==============================================================================

[CmdletBinding()]
param(
    [string]$ScanPath = "",
    [int]$FloorChars = 15
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ScanPath)) {
    $ScanPath = (Get-Item $PSScriptRoot).Parent.FullName
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Moonshine Pre-Commit Preflight Scanner (Rule 4)" -ForegroundColor Cyan
Write-Host "Scan Path: $ScanPath" -ForegroundColor Cyan
Write-Host "Justification Floor: $FloorChars characters" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$violations = [System.Collections.Generic.List[PSObject]]::new()

# Helper to validate justification comments
function Test-ValidJustification([string]$line, [string]$prevLine, [string]$prefix, [int]$minLen) {
    $textToCheck = "$prevLine $line"
    $pattern = [regex]::Escape($prefix) + "\s*(.*)"
    if ($textToCheck -match $pattern) {
        $justification = $matches[1].Trim()
        # Strip trailing comment delimiters if any
        $justification = $justification -replace "\*/.*$", ""
        if ($justification.Length -ge $minLen) {
            return $true
        }
    }
    return $false
}

# 1. Scan Source Files (.cs, .cpp, .h, .hpp, .c)
$sourceExtensions = @("*.cs", "*.cpp", "*.h", "*.hpp", "*.c")
$excludeDirs = @("\build\", "\bin\", "\obj\", "\.git\", "\.vs\", "\TestResults\")

$sourceFiles = Get-ChildItem -Path $ScanPath -Recurse -Include $sourceExtensions -File | Where-Object {
    $fullName = $_.FullName
    $excluded = $false
    foreach ($dir in $excludeDirs) {
        if ($fullName.Contains($dir)) { $excluded = $true; break }
    }
    -not $excluded
}

foreach ($file in $sourceFiles) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $lineNum = $i + 1
        $line = $lines[$i]
        $prevLine = if ($i -gt 0) { $lines[$i - 1] } else { "" }

        # Ignore comments explaining the rules themselves
        if ($line -match "^\s*//\s*(?:STUB|SIMULATED|ALLOWED_EXCEPTION|PROTOCOL_PARAM|ALLOWED_TLS_BYPASS):") {
            # Check if this justification itself satisfies the length floor
            if ($line -match "^\s*//\s*(STUB|SIMULATED|ALLOWED_EXCEPTION|PROTOCOL_PARAM|ALLOWED_TLS_BYPASS):\s*(.*)$") {
                $tag = $matches[1]
                $content = $matches[2].Trim()
                if ($content.Length -lt $FloorChars) {
                    $violations.Add([PSCustomObject]@{
                        File = $file.FullName
                        Line = $lineNum
                        Type = "Lazy Justification Escape Hatch"
                        Detail = "Tag '//${tag}:' provides only $($content.Length) chars of explanation (minimum required: $FloorChars chars)."
                    })
                }
            }
            continue
        }

        # A. Unannotated Stubs / Simulated logic
        if ($line -match "(?i)\b(simulat\w*|placeholder\w*|dummy\w*|stub\w*)\b" -and $line -notmatch "^\s*//") {
            $hasStubTag = (Test-ValidJustification $line $prevLine "// STUB:" $FloorChars) -or 
                          (Test-ValidJustification $line $prevLine "// SIMULATED:" $FloorChars)
            if (-not $hasStubTag) {
                $violations.Add([PSCustomObject]@{
                    File = $file.FullName
                    Line = $lineNum
                    Type = "Unannotated Stub / Simulation (Rule 5)"
                    Detail = "Contains simulation/stub keyword without valid '// STUB: <reason>' (min $FloorChars chars)."
                })
            }
        }

        # B. Swallowed Exceptions
        if ($line -match "catch\s*\(\s*Exception\b" -or $line -match "catch\s*\{\s*\}") {
            $hasCatchTag = Test-ValidJustification $line $prevLine "// ALLOWED_EXCEPTION:" $FloorChars
            if (-not $hasCatchTag) {
                $violations.Add([PSCustomObject]@{
                    File = $file.FullName
                    Line = $lineNum
                    Type = "Swallowed Exception (Rule 4)"
                    Detail = "Wildcard catch block without '// ALLOWED_EXCEPTION: <reason>' justification (min $FloorChars chars)."
                })
            }
        }

        # C. Hardcoded Secrets (string literals assigned to password/secret/api_key)
        if ($line -match "(?i)(api[_-]?key|private[_-]?key|password|secret)\s*[:=]\s*[""][^""]{8,}[""]") {
            $hasSecretTag = Test-ValidJustification $line $prevLine "// PROTOCOL_PARAM:" $FloorChars
            if (-not $hasSecretTag) {
                $violations.Add([PSCustomObject]@{
                    File = $file.FullName
                    Line = $lineNum
                    Type = "Potential Hardcoded Secret (Rule 6)"
                    Detail = "Literal credential/token pattern detected without '// PROTOCOL_PARAM: <reason>'."
                })
            }
        }

        # D. Inline TLS Bypasses
        if ($line -match "(?i)(?:ServerCertificateCustomValidationCallback|RemoteCertificateValidationCallback)\s*=.*=>\s*true") {
            $hasTlsTag = (Test-ValidJustification $line $prevLine "// ALLOWED_TLS_BYPASS:" $FloorChars) -or
                         ($line.Contains("AcceptSelfSignedGameStreamCert"))
            if (-not $hasTlsTag) {
                $violations.Add([PSCustomObject]@{
                    File = $file.FullName
                    Line = $lineNum
                    Type = "Unapproved TLS Bypass (Rule 6)"
                    Detail = "Inline lambda TLS validation bypass detected without AcceptSelfSignedGameStreamCert or '// ALLOWED_TLS_BYPASS:'."
                })
            }
        }
    }
}

# 2. Scan Markdown Documentation for Bare Unprovenanced Metric Claims
$docFiles = Get-ChildItem -Path $ScanPath -Recurse -Filter "*.md" -File | Where-Object {
    $fullName = $_.FullName
    $excluded = $false
    foreach ($dir in $excludeDirs) {
        if ($fullName.Contains($dir)) { $excluded = $true; break }
    }
    # Exclude standards and changelogs which discuss the rule syntax
    if ($_.Name -in @("STANDARDS.md", "CHANGELOG.md", "CODE_OF_CONDUCT.md")) { $excluded = $true }
    -not $excluded
}

foreach ($doc in $docFiles) {
    $lines = [System.IO.File]::ReadAllLines($doc.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $lineNum = $i + 1
        $line = $lines[$i]
        $prevLine = if ($i -gt 0) { $lines[$i - 1] } else { "" }

        # Check for bare number claims like "Exactly \d+ tests" or "\d+ tests passing"
        if ($line -match "(?i)\b(?:Exactly\s+)?\d+\s+(?:tests?|benchmarks?|targets?)\s+(?:passing|registered|verified|successful)\b") {
            if ($prevLine -notmatch "<!--\s*(?:VERIFIED|REGISTERED):\s*\d{4}-\d{2}-\d{2}") {
                $violations.Add([PSCustomObject]@{
                    File = $doc.FullName
                    Line = $lineNum
                    Type = "Unprovenanced Metric Claim (Rule 9)"
                    Detail = "Metric claim lacks immediately preceding '<!-- VERIFIED: YYYY-MM-DD, via <cmd> -->' tag."
                })
            }
        }
    }
}

# 3. Report Results
if ($violations.Count -eq 0) {
    Write-Host "[+] Preflight Sweep PASSED: Zero violations detected across $($sourceFiles.Count) source files and $($docFiles.Count) documents." -ForegroundColor Green
    exit 0
} else {
    Write-Host "[!] Preflight Sweep FAILED: $($violations.Count) violation(s) detected:`n" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "  - [$($v.Type)]" -ForegroundColor Red
        Write-Host "    File: $($v.File):$($v.Line)" -ForegroundColor Yellow
        Write-Host "    Detail: $($v.Detail)`n" -ForegroundColor Gray
    }
    exit 1
}
