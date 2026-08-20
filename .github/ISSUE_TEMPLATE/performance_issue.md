---
name: Performance Issue or Latency Regression
about: Report a performance bottleneck, frame pacing spike, or latency regression
title: '[PERF] '
labels: 'performance'
assignees: ''
---

**Performance Metric Impacted:**
- [ ] Network Ingestion / RTP Parsing
- [ ] FEC Reed-Solomon SIMD Recovery
- [ ] Jitter Buffer / Frame Pacing
- [ ] Hardware Video Decode Latency (D3D11/D3D12/Vulkan)
- [ ] Audio Buffering / WASAPI Latency
- [ ] Input Capture & Polling Rate

**Observed Metric vs Target Budget:**
- Measured Latency / Allocation: [e.g. 5.2 ms frame decode / 120 KB/s allocations]
- Target Budget: [e.g. < 1.5 ms / 0 B allocations]

**Hardware / CPU Architecture:**
- CPU: [e.g. AMD Ryzen 7 7800X3D / Intel Core i9-14900K / Apple M3 Pro]
- AVX / SIMD Features Supported: [e.g. AVX2, AVX-512, GFNI]

**Steps / Environment to Reproduce:**
Detailed steps to reproduce the latency spike or profile capture trace.
