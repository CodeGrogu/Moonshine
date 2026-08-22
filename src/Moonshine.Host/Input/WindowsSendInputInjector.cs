using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Host.Input;

/// <summary>
/// High-performance zero-allocation native Windows input injector using User32 SendInput.
/// Tracks physical key and button state to guarantee deterministic stuck-key release on session loss.
/// </summary>
public sealed unsafe class WindowsSendInputInjector : IWindowsInputInjector
{
    private ulong _heldKey0;
    private ulong _heldKey1;
    private ulong _heldKey2;
    private ulong _heldKey3;
    private byte _heldButtons;
    private bool _disposed;
    private readonly Lock _syncRoot = new();

    public bool IsDisposed => _disposed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InjectMouseMove(short deltaX, short deltaY)
    {
        if (_disposed) return false;
        if (deltaX == 0 && deltaY == 0) return true;

        INPUT input = default;
        input.type = WindowsInputNativeMethods.INPUT_MOUSE;
        input.mi.dx = deltaX;
        input.mi.dy = deltaY;
        input.mi.dwFlags = WindowsInputNativeMethods.MOUSEEVENTF_MOVE;

        uint sent = WindowsInputNativeMethods.SendInput(1, &input, sizeof(INPUT));
        return sent == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InjectMouseMoveAbsolute(int x, int y, int screenWidth, int screenHeight)
    {
        if (_disposed) return false;
        if (screenWidth <= 0 || screenHeight <= 0) return false;

        int clampedX = Math.Clamp(x, 0, screenWidth - 1);
        int clampedY = Math.Clamp(y, 0, screenHeight - 1);

        int normX = (int)((clampedX * 65535L) / (screenWidth - 1));
        int normY = (int)((clampedY * 65535L) / (screenHeight - 1));

        INPUT input = default;
        input.type = WindowsInputNativeMethods.INPUT_MOUSE;
        input.mi.dx = normX;
        input.mi.dy = normY;
        input.mi.dwFlags = WindowsInputNativeMethods.MOUSEEVENTF_MOVE |
                           WindowsInputNativeMethods.MOUSEEVENTF_ABSOLUTE |
                           WindowsInputNativeMethods.MOUSEEVENTF_VIRTUALDESK;

        uint sent = WindowsInputNativeMethods.SendInput(1, &input, sizeof(INPUT));
        return sent == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InjectMouseButton(byte buttonIndex, bool isDown)
    {
        if (_disposed) return false;

        uint flags;
        uint mouseData = 0;

        switch (buttonIndex)
        {
            case 1: // Left Button
                flags = isDown ? WindowsInputNativeMethods.MOUSEEVENTF_LEFTDOWN : WindowsInputNativeMethods.MOUSEEVENTF_LEFTUP;
                break;
            case 2: // Right Button
                flags = isDown ? WindowsInputNativeMethods.MOUSEEVENTF_RIGHTDOWN : WindowsInputNativeMethods.MOUSEEVENTF_RIGHTUP;
                break;
            case 3: // Middle Button
                flags = isDown ? WindowsInputNativeMethods.MOUSEEVENTF_MIDDLEDOWN : WindowsInputNativeMethods.MOUSEEVENTF_MIDDLEUP;
                break;
            case 4: // X1 Button
                flags = isDown ? WindowsInputNativeMethods.MOUSEEVENTF_XDOWN : WindowsInputNativeMethods.MOUSEEVENTF_XUP;
                mouseData = WindowsInputNativeMethods.XBUTTON1;
                break;
            case 5: // X2 Button
                flags = isDown ? WindowsInputNativeMethods.MOUSEEVENTF_XDOWN : WindowsInputNativeMethods.MOUSEEVENTF_XUP;
                mouseData = WindowsInputNativeMethods.XBUTTON2;
                break;
            default:
                return false;
        }

        lock (_syncRoot)
        {
            if (buttonIndex is >= 1 and <= 5)
            {
                int bit = 1 << (buttonIndex - 1);
                if (isDown)
                {
                    _heldButtons |= (byte)bit;
                }
                else
                {
                    _heldButtons &= (byte)~bit;
                }
            }
        }

        INPUT input = default;
        input.type = WindowsInputNativeMethods.INPUT_MOUSE;
        input.mi.mouseData = mouseData;
        input.mi.dwFlags = flags;

        uint sent = WindowsInputNativeMethods.SendInput(1, &input, sizeof(INPUT));
        return sent == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InjectMouseScroll(short scrollDelta, bool isHorizontal = false)
    {
        if (_disposed) return false;
        if (scrollDelta == 0) return true;

        INPUT input = default;
        input.type = WindowsInputNativeMethods.INPUT_MOUSE;
        input.mi.mouseData = (uint)scrollDelta;
        input.mi.dwFlags = isHorizontal ? WindowsInputNativeMethods.MOUSEEVENTF_HWHEEL : WindowsInputNativeMethods.MOUSEEVENTF_WHEEL;

        uint sent = WindowsInputNativeMethods.SendInput(1, &input, sizeof(INPUT));
        return sent == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InjectKeyboardKey(short virtualKeyCode, short scanCode, bool isDown, byte modifiers = 0)
    {
        if (_disposed) return false;
        if (virtualKeyCode is < 0 or > 255) return false;

        byte vkey = (byte)virtualKeyCode;
        uint flags = isDown ? 0 : WindowsInputNativeMethods.KEYEVENTF_KEYUP;

        if (scanCode != 0)
        {
            flags |= WindowsInputNativeMethods.KEYEVENTF_SCANCODE;
        }

        // Extended key handling (Arrow keys, Insert, Delete, Home, End, PageUp, PageDown, Right Alt, Right Ctrl)
        if (vkey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0xA3 or 0xA5)
        {
            flags |= WindowsInputNativeMethods.KEYEVENTF_EXTENDEDKEY;
        }

        lock (_syncRoot)
        {
            int block = vkey >> 6;
            int bit = vkey & 63;
            ulong mask = 1UL << bit;

            if (isDown)
            {
                switch (block)
                {
                    case 0: _heldKey0 |= mask; break;
                    case 1: _heldKey1 |= mask; break;
                    case 2: _heldKey2 |= mask; break;
                    case 3: _heldKey3 |= mask; break;
                }
            }
            else
            {
                switch (block)
                {
                    case 0: _heldKey0 &= ~mask; break;
                    case 1: _heldKey1 &= ~mask; break;
                    case 2: _heldKey2 &= ~mask; break;
                    case 3: _heldKey3 &= ~mask; break;
                }
            }
        }

        INPUT input = default;
        input.type = WindowsInputNativeMethods.INPUT_KEYBOARD;
        input.ki.wVk = vkey;
        input.ki.wScan = (ushort)scanCode;
        input.ki.dwFlags = flags;

        uint sent = WindowsInputNativeMethods.SendInput(1, &input, sizeof(INPUT));
        return sent == 1;
    }

    public int ReleaseAllHeldInputs()
    {
        if (_disposed) return 0;

        int releaseCount = 0;
        INPUT* inputs = stackalloc INPUT[32];

        lock (_syncRoot)
        {
            // Release held mouse buttons
            for (byte b = 1; b <= 5; b++)
            {
                if ((_heldButtons & (1 << (b - 1))) != 0)
                {
                    uint flags = b switch
                    {
                        1 => WindowsInputNativeMethods.MOUSEEVENTF_LEFTUP,
                        2 => WindowsInputNativeMethods.MOUSEEVENTF_RIGHTUP,
                        3 => WindowsInputNativeMethods.MOUSEEVENTF_MIDDLEUP,
                        4 => WindowsInputNativeMethods.MOUSEEVENTF_XUP,
                        5 => WindowsInputNativeMethods.MOUSEEVENTF_XUP,
                        _ => 0
                    };

                    uint mouseData = b switch
                    {
                        4 => WindowsInputNativeMethods.XBUTTON1,
                        5 => WindowsInputNativeMethods.XBUTTON2,
                        _ => 0
                    };

                    INPUT input = default;
                    input.type = WindowsInputNativeMethods.INPUT_MOUSE;
                    input.mi.dwFlags = flags;
                    input.mi.mouseData = mouseData;
                    WindowsInputNativeMethods.SendInput(1, &input, sizeof(INPUT));
                    releaseCount++;
                }
            }
            _heldButtons = 0;

            // Release held keyboard keys
            ulong[] heldBlocks = [_heldKey0, _heldKey1, _heldKey2, _heldKey3];
            for (int block = 0; block < 4; block++)
            {
                ulong bits = heldBlocks[block];
                while (bits != 0)
                {
                    int bitIndex = BitOperations.TrailingZeroCount(bits);
                    byte vkey = (byte)((block << 6) | bitIndex);

                    uint flags = WindowsInputNativeMethods.KEYEVENTF_KEYUP;
                    if (vkey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0xA3 or 0xA5)
                    {
                        flags |= WindowsInputNativeMethods.KEYEVENTF_EXTENDEDKEY;
                    }

                    INPUT input = default;
                    input.type = WindowsInputNativeMethods.INPUT_KEYBOARD;
                    input.ki.wVk = vkey;
                    input.ki.dwFlags = flags;
                    WindowsInputNativeMethods.SendInput(1, &input, sizeof(INPUT));
                    releaseCount++;

                    bits &= bits - 1;
                }
            }

            _heldKey0 = 0;
            _heldKey1 = 0;
            _heldKey2 = 0;
            _heldKey3 = 0;
        }

        return releaseCount;
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_syncRoot)
        {
            if (_disposed) return;
            ReleaseAllHeldInputs();
            _disposed = true;
        }
    }
}
