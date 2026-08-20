# Script to configure GitHub labels, milestones, and issues for Moonshine

$Repo = "CodeGrogu/Moonshine"

# 1. Create Labels
$Labels = @(
    @{ name = "area: native-simd"; color = "5319e7"; desc = "AVX2/AVX-512 Galois Field FEC and SIMD acceleration" },
    @{ name = "area: video-d3d"; color = "1d76db"; desc = "Direct3D 11/12 hardware decoding and DXGI swapchains" },
    @{ name = "area: audio-wasapi"; color = "006b75"; desc = "WASAPI Exclusive low-latency audio rendering" },
    @{ name = "area: protocol-rtsp"; color = "0e8a16"; desc = "RTSP session negotiation and SDP streaming parameters" },
    @{ name = "area: protocol-rtp"; color = "fbca04"; desc = "RTP demuxing, sequence unwrapping, and jitter buffering" },
    @{ name = "area: pairing-crypto"; color = "d93f0b"; desc = "X.509 certificate generation, PIN exchange, AES-GCM" },
    @{ name = "area: input-hid"; color = "b60205"; desc = "1000Hz raw input polling (XInput, DirectInput, Mouse)" },
    @{ name = "area: network-io"; color = "c5def5"; desc = "System.IO.Pipelines and zero-copy UDP socket ingestion" },
    @{ name = "area: documentation"; color = "0075ca"; desc = "GitHub wiki and architectural design documents" },
    @{ name = "type: performance-regression"; color = "b60205"; desc = "Measured increase in latency, stutter, or GC allocations" },
    @{ name = "type: bug"; color = "d73a4a"; desc = "Defect, crash, or decoding error" },
    @{ name = "type: feature"; color = "a2eeef"; desc = "New capability, codec, or SIMD kernel" },
    @{ name = "type: security"; color = "e11d48"; desc = "Cryptographic vulnerability or CVE mitigation" },
    @{ name = "type: benchmark"; color = "7057ff"; desc = "Micro-benchmarks and latency measurement harnesses" },
    @{ name = "priority: critical-path"; color = "b60205"; desc = "Blocks core streaming engine functionality" },
    @{ name = "priority: high"; color = "d93f0b"; desc = "High impact on performance or stability" },
    @{ name = "priority: medium"; color = "fbca04"; desc = "Standard development task" },
    @{ name = "priority: low"; color = "0e8a16"; desc = "Minor polish or optional enhancement" },
    @{ name = "status: benchmark-required"; color = "e99695"; desc = "Requires BenchmarkDotNet verification before merge" },
    @{ name = "status: in-progress"; color = "fef2c0"; desc = "Actively being implemented" },
    @{ name = "status: needs-repro"; color = "bfdadc"; desc = "Requires reproduction steps or packet capture" },
    @{ name = "status: blocked"; color = "d4c5f9"; desc = "Blocked by an upstream dependency or hardware requirement" }
)

Write-Host "Creating GitHub labels..." -ForegroundColor Cyan
foreach ($label in $Labels) {
    gh label create $label.name --color $label.color --description $label.desc --force --repo $Repo
}

# 2. Create Milestones via GitHub API
Write-Host "Creating GitHub milestones..." -ForegroundColor Cyan

$Milestones = @(
    @{
        title = "v0.1.0 - Alpha: Protocol Ingestion & Native SIMD Pipeline"
        description = "Core protocol negotiation (mDNS, HTTPS pairing, RTSP/SDP), zero-copy UDP socket pipeline, and SIMD Galois Field FEC recovery."
        due_on = "2026-09-15T00:00:00Z"
    },
    @{
        title = "v0.2.0 - Beta: Hardware Acceleration & Presentation Subsystem"
        description = "Direct3D 11/12 hardware video decoders, DXGI Flip Model sub-millisecond presentation, HDR10 tone mapping, and WASAPI Exclusive mode audio."
        due_on = "2026-10-15T00:00:00Z"
    },
    @{
        title = "v1.0.0 - Production: 1000Hz Input & Sub-5ms Streaming Engine"
        description = "1000Hz raw input polling (XInput, DirectInput, high-DPI mouse), dynamic RTCP bitrate adaptation, full test verification, and cross-platform native packaging."
        due_on = "2026-11-15T00:00:00Z"
    }
)

foreach ($ms in $Milestones) {
    $body = @{
        title = $ms.title
        description = $ms.description
        due_on = $ms.due_on
        state = "open"
    } | ConvertTo-Json

    $body | gh api -X POST "repos/$Repo/milestones" --input -
}

Write-Host "GitHub configuration complete." -ForegroundColor Green
