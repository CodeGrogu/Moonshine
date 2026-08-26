using System;
using System.Collections.Generic;
using Moonshine.Protocol.Codecs;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Protocol.Tests;

public sealed class AcceptanceProtocolCodecTests
{
    [Fact]
    public void AcceptanceRunId_Generate_HasExpectedPrefixAndLength()
    {
        var runId = AcceptanceRunId.Generate();
        string val = runId.ToString();

        Assert.StartsWith("acc-", val, StringComparison.Ordinal);
        Assert.True(val.Length >= 20);
        Assert.Equal(runId, new AcceptanceRunId(val));
    }

    [Fact]
    public void StartRunRequest_SerialiseAndDeserialise_RoundtripsAccurately()
    {
        var expectedRunId = AcceptanceRunId.Generate();
        uint expectedFlags = 0xDEADBEEF;

        byte[] buffer = new byte[64];
        bool writeSuccess = MoonshineAcceptanceProtocolCodec.TryWriteStartRunRequest(expectedRunId, expectedFlags, buffer, out int written);

        Assert.True(writeSuccess);
        Assert.Equal(40, written);

        bool readSuccess = MoonshineAcceptanceProtocolCodec.TryReadStartRunRequest(buffer.AsSpan(0, written), out var actualRunId, out var actualFlags);

        Assert.True(readSuccess);
        Assert.Equal(expectedRunId, actualRunId);
        Assert.Equal(expectedFlags, actualFlags);
    }

    [Fact]
    public void StepResult_SerialiseAndDeserialise_PreservesAllMetrics()
    {
        var step = new AcceptanceStepResult
        {
            StepId = AcceptanceStepId.Step02_RealVideoPipeline,
            StepName = "Real Direct3D 11 NVENC Video Pipeline",
            Status = AcceptanceStepStatus.Passed,
            DurationMs = 5000.5,
            FramesObserved = 300,
            PacketsObserved = 300,
            LossCount = 0,
            P50LatencyUs = 2100.0,
            P95LatencyUs = 3500.0,
            P99LatencyUs = 4200.0,
            AverageJitterUs = 150.0,
            BitrateKbps = 20000.0,
            EvidenceSummary = "300 continuous frames decoded with 0 losses."
        };

        byte[] buffer = new byte[2048];
        bool writeSuccess = MoonshineAcceptanceProtocolCodec.TryWriteStepResult(step, buffer, out int written);

        Assert.True(writeSuccess);
        Assert.True(written > 32);

        bool readSuccess = MoonshineAcceptanceProtocolCodec.TryReadStepResult(buffer.AsSpan(0, written), out var roundtrip);

        Assert.True(readSuccess);
        Assert.Equal(step.StepId, roundtrip.StepId);
        Assert.Equal(step.StepName, roundtrip.StepName);
        Assert.Equal(step.Status, roundtrip.Status);
        Assert.Equal(step.DurationMs, roundtrip.DurationMs, precision: 1);
        Assert.Equal(step.FramesObserved, roundtrip.FramesObserved);
        Assert.Equal(step.PacketsObserved, roundtrip.PacketsObserved);
        Assert.Equal(step.P50LatencyUs, roundtrip.P50LatencyUs, precision: 1);
        Assert.Equal(step.EvidenceSummary, roundtrip.EvidenceSummary);
    }

    [Fact]
    public void ClientEvidenceBundle_Sha256Checksum_EvaluatesDeterministicHash()
    {
        var bundle = new ClientEvidenceBundle
        {
            AcceptanceRunId = "acc-20260826-120000-abcd1234",
            HumanConfirmationPassed = true,
            HumanConfirmationNotes = "Confirmed smooth 60 FPS video and clear audio.",
            Environment = new DeviceEnvironmentEvidence
            {
                Role = "Client",
                IpAddress = "192.168.48.254",
                MachineName = "REMOTE-WIN10",
                PrimaryGpu = "Intel HD Graphics 620"
            },
            Steps =
            [
                new AcceptanceStepResult
                {
                    StepId = AcceptanceStepId.Step01_EnvironmentInventory,
                    StepName = "Hardware Inventory",
                    Status = AcceptanceStepStatus.Passed
                }
            ]
        };

        string hash1 = bundle.ComputeChecksum();
        string hash2 = bundle.ComputeChecksum();

        Assert.False(string.IsNullOrWhiteSpace(hash1));
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }
}
