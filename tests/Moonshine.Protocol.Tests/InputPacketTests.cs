using FluentAssertions;
using Moonshine.Protocol.Input;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class InputPacketTests
{
    [Fact]
    public void MouseMovePacket_WriteAndParse_RoundtripsSuccessfully()
    {
        var packet = new MouseMovePacket(-125, 450);
        byte[] buffer = new byte[32];

        int written = packet.WriteTo(buffer);
        written.Should().Be(MouseMovePacket.PacketSize);

        bool parsed = MouseMovePacket.TryParse(buffer.AsSpan(0, written), out var result);
        parsed.Should().BeTrue();
        result.DeltaX.Should().Be(-125);
        result.DeltaY.Should().Be(450);
    }

    [Fact]
    public void MouseButtonPacket_WriteAndParse_RoundtripsSuccessfully()
    {
        var packet = new MouseButtonPacket(1, isDown: true);
        byte[] buffer = new byte[16];

        int written = packet.WriteTo(buffer);
        written.Should().Be(MouseButtonPacket.PacketSize);

        bool parsed = MouseButtonPacket.TryParse(buffer.AsSpan(0, written), out var result);
        parsed.Should().BeTrue();
        result.ButtonIndex.Should().Be(1);
        result.IsDown.Should().Be(1);
    }

    [Fact]
    public void KeyboardPacket_WriteAndParse_RoundtripsSuccessfully()
    {
        var packet = new KeyboardPacket(0x41, isDown: true, modifiers: 0x02);
        byte[] buffer = new byte[16];

        int written = packet.WriteTo(buffer);
        written.Should().Be(KeyboardPacket.PacketSize);

        bool parsed = KeyboardPacket.TryParse(buffer.AsSpan(0, written), out var result);
        parsed.Should().BeTrue();
        result.KeyCode.Should().Be(0x41);
        result.PacketType.Should().Be(InputPacketType.KeyDown);
        result.Modifiers.Should().Be(0x02);
    }

    [Fact]
    public void ControllerStatePacket_WriteAndParse_RoundtripsSuccessfully()
    {
        var packet = new ControllerStatePacket(
            controllerNumber: 0,
            buttons: GamepadButtons.A | GamepadButtons.RightShoulder,
            leftTrigger: 128,
            rightTrigger: 255,
            leftStickX: -16000,
            leftStickY: 32000,
            rightStickX: 1200,
            rightStickY: -5000
        );
        byte[] buffer = new byte[32];

        int written = packet.WriteTo(buffer);
        written.Should().Be(ControllerStatePacket.PacketSize);

        bool parsed = ControllerStatePacket.TryParse(buffer.AsSpan(0, written), out var result);
        parsed.Should().BeTrue();
        result.ControllerNumber.Should().Be(0);
        result.Buttons.Should().Be((ushort)(GamepadButtons.A | GamepadButtons.RightShoulder));
        result.LeftTrigger.Should().Be(128);
        result.RightTrigger.Should().Be(255);
        result.LeftStickX.Should().Be(-16000);
        result.LeftStickY.Should().Be(32000);
        result.RightStickX.Should().Be(1200);
        result.RightStickY.Should().Be(-5000);
    }

    [Fact]
    public void MouseScrollPacket_WriteAndParse_RoundtripsSuccessfully()
    {
        var packet = new MouseScrollPacket(-120);
        byte[] buffer = new byte[16];

        int written = packet.WriteTo(buffer);
        written.Should().Be(MouseScrollPacket.PacketSize);

        bool parsed = MouseScrollPacket.TryParse(buffer.AsSpan(0, written), out var result);
        parsed.Should().BeTrue();
        result.ScrollDelta.Should().Be(-120);
    }

    [Fact]
    public void InputPackets_BufferTooSmall_ReturnsFailure()
    {
        byte[] smallBuffer = new byte[2];
        var mouse = new MouseMovePacket(10, 10);
        mouse.WriteTo(smallBuffer).Should().Be(-1);

        MouseMovePacket.TryParse(smallBuffer, out _).Should().BeFalse();
        MouseButtonPacket.TryParse(smallBuffer, out _).Should().BeFalse();
        KeyboardPacket.TryParse(smallBuffer, out _).Should().BeFalse();
        ControllerStatePacket.TryParse(smallBuffer, out _).Should().BeFalse();
        MouseScrollPacket.TryParse(smallBuffer, out _).Should().BeFalse();
    }
}
