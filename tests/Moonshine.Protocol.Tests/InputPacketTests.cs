using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Protocol.Input;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class InputPacketTests
{
    [Fact]
    public void ControllerStatePacket_StructLayout_MatchesBinaryProtocolFormat()
    {
        var packet = new ControllerStatePacket(
            controllerNumber: 0,
            buttons: GamepadButtons.A | GamepadButtons.Start,
            leftTrigger: 128,
            rightTrigger: 255,
            leftStickX: -10000,
            leftStickY: 20000,
            rightStickX: 15000,
            rightStickY: -15000
        );

        packet.PacketType.Should().Be(InputPacketType.ControllerState);
        packet.ControllerNumber.Should().Be(0);
        packet.Buttons.Should().Be((ushort)(GamepadButtons.A | GamepadButtons.Start));
        packet.LeftTrigger.Should().Be(128);
        packet.RightTrigger.Should().Be(255);
        packet.LeftStickX.Should().Be(-10000);
        packet.LeftStickY.Should().Be(20000);
        packet.RightStickX.Should().Be(15000);
        packet.RightStickY.Should().Be(-15000);

        Marshal.SizeOf<ControllerStatePacket>().Should().Be(18);
    }

    [Fact]
    public void MouseMovePacket_StructLayout_StoresRelativeDeltas()
    {
        var mouse = new MouseMovePacket(deltaX: -50, deltaY: 75);

        mouse.PacketType.Should().Be(InputPacketType.MouseMoveRel);
        mouse.DeltaX.Should().Be(-50);
        mouse.DeltaY.Should().Be(75);

        Marshal.SizeOf<MouseMovePacket>().Should().Be(8);
    }
}
