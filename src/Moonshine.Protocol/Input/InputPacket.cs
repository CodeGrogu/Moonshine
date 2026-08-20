using System.Buffers.Binary;
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
/// High-DPI relative mouse movement packet.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MouseMovePacket
{
    public const int PacketSize = 12;

    public readonly InputPacketType PacketType;
    public readonly short DeltaX;
    public readonly short DeltaY;

    public MouseMovePacket(short deltaX, short deltaY)
    {
        PacketType = InputPacketType.MouseMoveRel;
        DeltaX = deltaX;
        DeltaY = deltaY;
    }

    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < PacketSize) return -1;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], (uint)PacketType);
        BinaryPrimitives.WriteInt16BigEndian(destination.Slice(4, 2), DeltaX);
        BinaryPrimitives.WriteInt16BigEndian(destination.Slice(6, 2), DeltaY);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), 0);
        return PacketSize;
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out MouseMovePacket packet)
    {
        if (source.Length < PacketSize)
        {
            packet = default;
            return false;
        }

        uint type = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        if (type != (uint)InputPacketType.MouseMoveRel && type != (uint)InputPacketType.MouseMoveAbs)
        {
            packet = default;
            return false;
        }

        short deltaX = BinaryPrimitives.ReadInt16BigEndian(source.Slice(4, 2));
        short deltaY = BinaryPrimitives.ReadInt16BigEndian(source.Slice(6, 2));
        packet = new MouseMovePacket(deltaX, deltaY);
        return true;
    }
}

/// <summary>
/// Mouse button press / release packet.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MouseButtonPacket
{
    public const int PacketSize = 8;

    public readonly InputPacketType PacketType;
    public readonly byte ButtonIndex;
    public readonly byte IsDown;

    public MouseButtonPacket(byte buttonIndex, bool isDown)
    {
        PacketType = isDown ? InputPacketType.MouseButtonDown : InputPacketType.MouseButtonUp;
        ButtonIndex = buttonIndex;
        IsDown = (byte)(isDown ? 1 : 0);
    }

    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < PacketSize) return -1;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], (uint)PacketType);
        destination[4] = ButtonIndex;
        destination[5] = IsDown;
        destination[6] = 0;
        destination[7] = 0;
        return PacketSize;
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out MouseButtonPacket packet)
    {
        if (source.Length < PacketSize)
        {
            packet = default;
            return false;
        }

        uint type = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        if (type != (uint)InputPacketType.MouseButtonDown && type != (uint)InputPacketType.MouseButtonUp)
        {
            packet = default;
            return false;
        }

        byte button = source[4];
        bool isDown = type == (uint)InputPacketType.MouseButtonDown || source[5] != 0;
        packet = new MouseButtonPacket(button, isDown);
        return true;
    }
}

/// <summary>
/// Keyboard key press / release packet.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct KeyboardPacket
{
    public const int PacketSize = 8;

    public readonly InputPacketType PacketType;
    public readonly short KeyCode;
    public readonly byte Modifiers;

    public KeyboardPacket(short keyCode, bool isDown, byte modifiers = 0)
    {
        PacketType = isDown ? InputPacketType.KeyDown : InputPacketType.KeyUp;
        KeyCode = keyCode;
        Modifiers = modifiers;
    }

    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < PacketSize) return -1;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], (uint)PacketType);
        BinaryPrimitives.WriteInt16BigEndian(destination.Slice(4, 2), KeyCode);
        destination[6] = (byte)(PacketType == InputPacketType.KeyDown ? 1 : 0);
        destination[7] = Modifiers;
        return PacketSize;
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out KeyboardPacket packet)
    {
        if (source.Length < PacketSize)
        {
            packet = default;
            return false;
        }

        uint type = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        if (type != (uint)InputPacketType.KeyDown && type != (uint)InputPacketType.KeyUp)
        {
            packet = default;
            return false;
        }

        short keyCode = BinaryPrimitives.ReadInt16BigEndian(source.Slice(4, 2));
        bool isDown = type == (uint)InputPacketType.KeyDown || source[6] != 0;
        byte modifiers = source[7];
        packet = new KeyboardPacket(keyCode, isDown, modifiers);
        return true;
    }
}

/// <summary>
/// Gamepad controller state packet sent at 1000Hz polling rate.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct ControllerStatePacket
{
    public const int PacketSize = 20;

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

    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < PacketSize) return -1;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], (uint)PacketType);
        destination[4] = ControllerNumber;
        destination[5] = Reserved;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(6, 2), Buttons);
        destination[8] = LeftTrigger;
        destination[9] = RightTrigger;
        BinaryPrimitives.WriteInt16BigEndian(destination.Slice(10, 2), LeftStickX);
        BinaryPrimitives.WriteInt16BigEndian(destination.Slice(12, 2), LeftStickY);
        BinaryPrimitives.WriteInt16BigEndian(destination.Slice(14, 2), RightStickX);
        BinaryPrimitives.WriteInt16BigEndian(destination.Slice(16, 2), RightStickY);
        BinaryPrimitives.WriteInt16BigEndian(destination.Slice(18, 2), 0);
        return PacketSize;
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out ControllerStatePacket packet)
    {
        if (source.Length < PacketSize)
        {
            packet = default;
            return false;
        }

        uint type = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        if (type != (uint)InputPacketType.ControllerState)
        {
            packet = default;
            return false;
        }

        byte controller = source[4];
        GamepadButtons buttons = (GamepadButtons)BinaryPrimitives.ReadUInt16BigEndian(source.Slice(6, 2));
        byte leftTrigger = source[8];
        byte rightTrigger = source[9];
        short leftX = BinaryPrimitives.ReadInt16BigEndian(source.Slice(10, 2));
        short leftY = BinaryPrimitives.ReadInt16BigEndian(source.Slice(12, 2));
        short rightX = BinaryPrimitives.ReadInt16BigEndian(source.Slice(14, 2));
        short rightY = BinaryPrimitives.ReadInt16BigEndian(source.Slice(16, 2));

        packet = new ControllerStatePacket(controller, buttons, leftTrigger, rightTrigger, leftX, leftY, rightX, rightY);
        return true;
    }
}

/// <summary>
/// Mouse scroll wheel packet.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MouseScrollPacket
{
    public const int PacketSize = 8;

    public readonly InputPacketType PacketType;
    public readonly short ScrollDelta;

    public MouseScrollPacket(short scrollDelta)
    {
        PacketType = InputPacketType.Scroll;
        ScrollDelta = scrollDelta;
    }

    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < PacketSize) return -1;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], (uint)PacketType);
        BinaryPrimitives.WriteInt16BigEndian(destination.Slice(4, 2), ScrollDelta);
        BinaryPrimitives.WriteInt16BigEndian(destination.Slice(6, 2), 0);
        return PacketSize;
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out MouseScrollPacket packet)
    {
        if (source.Length < PacketSize)
        {
            packet = default;
            return false;
        }

        uint type = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        if (type != (uint)InputPacketType.Scroll)
        {
            packet = default;
            return false;
        }

        short delta = BinaryPrimitives.ReadInt16BigEndian(source.Slice(4, 2));
        packet = new MouseScrollPacket(delta);
        return true;
    }
}
