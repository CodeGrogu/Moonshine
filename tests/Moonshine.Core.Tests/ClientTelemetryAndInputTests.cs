using System;
using System.Diagnostics;
using System.Drawing;
using FluentAssertions;
using Moonshine.Core;
using Moonshine.Core.Audio;
using Moonshine.Protocol;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Core.Tests;

public sealed class ClientTelemetryAndInputTests
{
    [Fact]
    public void KeepAlive_EchoTimestamp_FidelityAndRttCalculation()
    {
        // 1. Client creates KeepAlive header with microsecond timestamp
        ulong clientSentTimestampUs = 123456789UL;
        var keepAliveHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.KeepAlive,
            PayloadSize: 0,
            SequenceNumber: 42,
            SessionId: 0xDEADBEEFCAFEBABEUL,
            TimestampUs: clientSentTimestampUs);

        // 2. Host creates KeepAliveAck header echoing client's TimestampUs
        var ackHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.KeepAliveAck,
            PayloadSize: 0,
            SequenceNumber: keepAliveHeader.SequenceNumber,
            SessionId: keepAliveHeader.SessionId,
            TimestampUs: keepAliveHeader.TimestampUs);

        // 3. Assert fidelity
        ackHeader.TimestampUs.Should().Be(clientSentTimestampUs);

        // 4. Client computes RTT when ack is received 15,000 microseconds later
        ulong clientReceivedTimestampUs = clientSentTimestampUs + 15000UL;
        long rttUs = (long)(clientReceivedTimestampUs - ackHeader.TimestampUs);
        double rttMs = rttUs / 1000.0;

        rttMs.Should().Be(15.0);
    }

    [Theory]
    [InlineData(0.0, 0.0, 1920.0, 1080.0, 0, 0)]
    [InlineData(1920.0, 1080.0, 1920.0, 1080.0, 1919, 1079)]
    [InlineData(960.0, 540.0, 1920.0, 1080.0, 960, 540)]
    [InlineData(640.0, 360.0, 1280.0, 720.0, 960, 540)]
    [InlineData(1280.0, 720.0, 1280.0, 720.0, 1919, 1079)]
    public void PointerCoordinate_ScalingMath_CalculatesAccurately(
        double pointerX, double pointerY,
        double panelWidth, double panelHeight,
        int expectedScaledX, int expectedScaledY)
    {
        const int targetWidth = 1920;
        const int targetHeight = 1080;

        double normalizedX = Math.Clamp(pointerX / panelWidth, 0.0, 1.0);
        double normalizedY = Math.Clamp(pointerY / panelHeight, 0.0, 1.0);

        int scaledX = (int)Math.Round(normalizedX * (targetWidth - 1));
        int scaledY = (int)Math.Round(normalizedY * (targetHeight - 1));

        scaledX.Should().Be(expectedScaledX);
        scaledY.Should().Be(expectedScaledY);
    }

    [Fact]
    public void AudioJitterBuffer_PreRoll_RequiresMinimumPacketsBeforeFirstPop()
    {
        var jitter = new AudioJitterBuffer(capacity: 32, maxPacketSize: 1024);
        byte[] payload = new byte[100];
        byte[] popBuf = new byte[1024];

        // First packet pushed (QueuedCount = 1)
        jitter.Push(sequence: 10, timestampQpc: 1000, payload: payload);
        jitter.QueuedCount.Should().Be(1);

        // Pre-roll gating: With only 1 packet queued and 0 popped, Pop must return false without incrementing underruns
        bool popped = jitter.Pop(popBuf, out int bytesPopped, out _, out _);
        popped.Should().BeFalse();
        bytesPopped.Should().Be(0);

        // Second packet pushed (QueuedCount = 2)
        jitter.Push(sequence: 11, timestampQpc: 2000, payload: payload);
        jitter.QueuedCount.Should().Be(2);

        // Pre-roll fulfilled: Now Pop succeeds
        popped = jitter.Pop(popBuf, out bytesPopped, out uint seq, out _);
        popped.Should().BeTrue();
        seq.Should().Be(10);
        bytesPopped.Should().Be(100);
    }

    [Fact]
    public void AppLogger_RingBuffer_CapsAtMaxEntriesWithoutMemoryLeak()
    {
        AppLogger.ClearRecentLogs();
        AppLogger.GetRecentLogs().Should().BeEmpty();

        for (int i = 0; i < 1200; i++)
        {
            AppLogger.Log($"Test log message iteration {i}");
        }

        var logs = AppLogger.GetRecentLogs();
        logs.Count.Should().Be(1000);
        logs[^1].Should().Contain("Test log message iteration 1199");

        AppLogger.ClearRecentLogs();
        AppLogger.GetRecentLogs().Should().BeEmpty();
    }
}
