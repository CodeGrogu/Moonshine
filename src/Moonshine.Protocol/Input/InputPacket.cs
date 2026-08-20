using System.Runtime.InteropServices;

namespace Moonshine.Protocol.Input;

public enum InputPacketType : uint
{
    KeyDown = 0x00000003,
    KeyUp = 0x00000004,
    MouseMoveAbs = 0x00000005,
    MouseMoveRel = 0x00000007,
    MouseButtonDown = 0x00000008,
    MouseButtonUp = 0x00000009,
    ControllerState = 0x0000000A,
    Scroll = 0x0000000B,
    TouchDown = 0x0000000C,
    TouchMove = 0x0000000D,
    TouchUp = 0x0000000E
}

[Flags]
public enum GamepadButtons : ushort
{
    None = 0,
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000
}

/// <summary>
/// Gamepad controller state packet sent at 1000Hz polling rate.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct ControllerStatePacket
{
    public readonly InputPacketType PacketType;
    public readonly byte ControllerNumber;
    public readonly byte Reserved;
    public readonly ushort Buttons;
    public readonly byte LeftTrigger;
    public readonly byte RightTrigger;
    public readonly short LeftStickX;
    public readonly short LeftStickY;
    public readonly short RightStickX;
    public readonly short RightStickY;

    public ControllerStatePacket(
        byte controllerNumber,
        GamepadButtons buttons,
        byte leftTrigger,
        byte rightTrigger,
        short leftStickX,
        short leftStickY,
        short rightStickX,
        short rightStickY)
    {
        PacketType = InputPacketType.ControllerState;
        ControllerNumber = controllerNumber;
        Reserved = 0;
        Buttons = (ushort)buttons;
        LeftTrigger = leftTrigger;
        RightTrigger = rightTrigger;
        LeftStickX = leftStickX;
        LeftStickY = leftStickY;
        RightStickX = rightStickX;
        RightStickY = rightStickY;
    }
}

/// <summary>
/// High-DPI Mouse input packet for sub-millisecond cursor updates.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MouseMovePacket
{
    public readonly InputPacketType PacketType;
    public readonly short DeltaX;
    public readonly short DeltaY;

    public MouseMovePacket(short deltaX, short deltaY)
    {
        PacketType = InputPacketType.MouseMoveRel;
        DeltaX = deltaX;
        DeltaY = deltaY;
    }
}
