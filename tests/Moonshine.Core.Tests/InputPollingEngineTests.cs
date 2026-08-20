using FluentAssertions;
using Moonshine.Core.Input;
using Moonshine.Protocol.Input;
using Xunit;

namespace Moonshine.Core.Tests;

public class InputPollingEngineTests
{
    [Fact]
    public void InputPollingEngine_StartAndStop_ExecutesCleanly()
    {
        using var engine = new InputPollingEngine(targetFrequencyHz: 1000);
        engine.TargetFrequencyHz.Should().Be(1000);
        engine.IsRunning.Should().BeFalse();

        engine.Start();
        engine.IsRunning.Should().BeTrue();

        Thread.Sleep(50);
        engine.Metrics.SamplesPolled.Should().BeGreaterThan(10);
    }

    [Fact]
    public void InputPollingEngine_IngestMouseMove_EmitsPacketToTransmitter()
    {
        int packetsReceived = 0;
        using var engine = new InputPollingEngine(targetFrequencyHz: 1000, packet =>
        {
            if (MouseMovePacket.TryParse(packet, out _))
            {
                Interlocked.Increment(ref packetsReceived);
            }
        });

        engine.Start();
        engine.IngestMouseMove(100, -50);

        Thread.Sleep(50);
        packetsReceived.Should().BeGreaterThanOrEqualTo(1);
        engine.Metrics.PacketsEmitted.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void InputPollingEngine_IngestMouseButton_EmitsPacketImmediately()
    {
        int buttonPackets = 0;
        using var engine = new InputPollingEngine(targetFrequencyHz: 1000, packet =>
        {
            if (MouseButtonPacket.TryParse(packet, out _))
            {
                Interlocked.Increment(ref buttonPackets);
            }
        });

        engine.IngestMouseButton(1, isDown: true);
        engine.IngestMouseButton(1, isDown: false);

        buttonPackets.Should().Be(2);
        engine.Metrics.PacketsEmitted.Should().Be(2);
    }

    [Fact]
    public void InputPollingEngine_IngestKeyboardKey_EmitsPacketImmediately()
    {
        int keyPackets = 0;
        using var engine = new InputPollingEngine(targetFrequencyHz: 1000, packet =>
        {
            if (KeyboardPacket.TryParse(packet, out _))
            {
                Interlocked.Increment(ref keyPackets);
            }
        });

        engine.IngestKeyboardKey(0x57, isDown: true); // 'W' key
        engine.IngestKeyboardKey(0x57, isDown: false);

        keyPackets.Should().Be(2);
        engine.Metrics.PacketsEmitted.Should().Be(2);
    }

    [Fact]
    public void InputPollingEngine_IngestGamepadState_EmitsGamepadPacket()
    {
        int padPackets = 0;
        using var engine = new InputPollingEngine(targetFrequencyHz: 1000, packet =>
        {
            if (ControllerStatePacket.TryParse(packet, out _))
            {
                Interlocked.Increment(ref padPackets);
            }
        });

        engine.Start();

        var state = new ControllerStatePacket(0, GamepadButtons.A, 0, 255, 0, 0, 0, 0);
        engine.IngestGamepadState(in state);

        Thread.Sleep(50);
        padPackets.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void InputPollingEngine_DoubleDispose_IsSafe()
    {
        var engine = new InputPollingEngine(targetFrequencyHz: 1000);
        engine.Start();
        engine.Dispose();
        engine.Dispose();
    }
}
