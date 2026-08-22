using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Host.Input;

/// <summary>
/// High-performance zero-allocation native Windows input injector using User32 SendInput.
/// Features multi-monitor virtual-desktop coordinate mapping, extended key translation,
/// hardware scan code fallback, single-call batched dispatch, and deterministic stuck-key release.
/// </summary>
public sealed unsafe class WindowsSendInputInjector : IWindowsInputInjector
{
    private VirtualDesktopGeometry _bounds;
    private ulong _heldKey0;
    private ulong _heldKey1;
    private ulong _heldKey2;
    private ulong _heldKey3;
    private byte _heldButtons;
    private bool _disposed;
    private readonly Lock _syncRoot = new();

    public WindowsSendInputInjector()
    {
        RefreshVirtualDesktopBounds();
    }

    public bool IsDisposed => _disposed;

    public VirtualDesktopGeometry GetVirtualDesktopBounds()
    {
        lock (_syncRoot)
        {
            return _bounds;
        }
    }

    public void RefreshVirtualDesktopBounds()
    {
        lock (_syncRoot)
        {
            int x = WindowsInputNativeMethods.GetSystemMetrics(WindowsInputNativeMethods.SM_XVIRTUALSCREEN);
            int y = WindowsInputNativeMethods.GetSystemMetrics(WindowsInputNativeMethods.SM_YVIRTUALSCREEN);
            int cx = WindowsInputNativeMethods.GetSystemMetrics(WindowsInputNativeMethods.SM_CXVIRTUALSCREEN);
            int cy = WindowsInputNativeMethods.GetSystemMetrics(WindowsInputNativeMethods.SM_CYVIRTUALSCREEN);

            if (cx <= 0 || cy <= 0)
            {
                x = 0;
                y = 0;
                cx = WindowsInputNativeMethods.GetSystemMetrics(WindowsInputNativeMethods.SM_CXSCREEN);
                cy = WindowsInputNativeMethods.GetSystemMetrics(WindowsInputNativeMethods.SM_CYSCREEN);
                if (cx <= 0) cx = 1920;
                if (cy <= 0) cy = 1080;
            }

            _bounds = new VirtualDesktopGeometry(x, y, cx, cy);
        }
    }

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
        if (sent == 1) return true;
        int err = Marshal.GetLastPInvokeError();
        return err is 5 or 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InjectMouseMoveAbsolute(
        int x,
        int y,
        int clientWidth,
        int clientHeight,
        int monitorOffsetX = 0,
        int monitorOffsetY = 0,
        int monitorWidth = 0,
        int monitorHeight = 0)
    {
        if (_disposed) return false;
        if (clientWidth <= 0 || clientHeight <= 0) return false;

        VirtualDesktopGeometry b;
        lock (_syncRoot)
        {
            b = _bounds;
        }

        if (b.Width <= 1 || b.Height <= 1) return false;

        if (monitorWidth <= 0) monitorWidth = b.Width;
        if (monitorHeight <= 0) monitorHeight = b.Height;

        int clampedX = Math.Clamp(x, 0, clientWidth - 1);
        int clampedY = Math.Clamp(y, 0, clientHeight - 1);

        long targetVirtX = monitorOffsetX + ((long)clampedX * monitorWidth) / clientWidth;
        long targetVirtY = monitorOffsetY + ((long)clampedY * monitorHeight) / clientHeight;

        int normX = (int)(((targetVirtX - b.X) * 65535L + ((b.Width - 1) / 2)) / (b.Width - 1));
        int normY = (int)(((targetVirtY - b.Y) * 65535L + ((b.Height - 1) / 2)) / (b.Height - 1));

        normX = Math.Clamp(normX, 0, 65535);
        normY = Math.Clamp(normY, 0, 65535);

        INPUT input = default;
        input.type = WindowsInputNativeMethods.INPUT_MOUSE;
        input.mi.dx = normX;
        input.mi.dy = normY;
        input.mi.dwFlags = WindowsInputNativeMethods.MOUSEEVENTF_MOVE |
                           WindowsInputNativeMethods.MOUSEEVENTF_ABSOLUTE |
                           WindowsInputNativeMethods.MOUSEEVENTF_VIRTUALDESK;

        uint sent = WindowsInputNativeMethods.SendInput(1, &input, sizeof(INPUT));
        if (sent == 1) return true;
        int err = Marshal.GetLastPInvokeError();
        return err is 5 or 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InjectMouseButton(byte buttonIndex, bool isDown)
    {
        if (_disposed) return false;
        if (buttonIndex is < 1 or > 5) return false;

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

        INPUT input = default;
        input.type = WindowsInputNativeMethods.INPUT_MOUSE;
        input.mi.mouseData = mouseData;
        input.mi.dwFlags = flags;

        uint sent = WindowsInputNativeMethods.SendInput(1, &input, sizeof(INPUT));
        if (sent == 1) return true;
        int err = Marshal.GetLastPInvokeError();
        return err is 5 or 0;
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
        if (sent == 1) return true;
        int err = Marshal.GetLastPInvokeError();
        return err is 5 or 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InjectKeyboardKey(short virtualKeyCode, short scanCode, bool isDown, byte modifiers = 0)
    {
        if (_disposed) return false;
        if (virtualKeyCode is < 0 or > 255) return false;

        byte vkey = (byte)virtualKeyCode;
        uint flags = isDown ? 0 : WindowsInputNativeMethods.KEYEVENTF_KEYUP;

        if (scanCode == 0)
        {
            scanCode = (short)WindowsInputNativeMethods.MapVirtualKeyW((uint)vkey, WindowsInputNativeMethods.MAPVK_VK_TO_VSC);
        }

        if (scanCode != 0)
        {
            flags |= WindowsInputNativeMethods.KEYEVENTF_SCANCODE;
        }

        // Extended key handling (Arrow keys, Insert, Delete, Home, End, PageUp, PageDown, Right Alt, Right Ctrl, NumLock, Divide, PrintScreen)
        if (vkey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0xA3 or 0xA5 or 0x90 or 0x6F or 0x2C)
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
        if (sent == 1) return true;
        int err = Marshal.GetLastPInvokeError();
        return err is 5 or 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int InjectBatch(ReadOnlySpan<INPUT> inputs)
    {
        if (_disposed || inputs.IsEmpty) return 0;

        fixed (INPUT* pInputs = inputs)
        {
            uint sent = WindowsInputNativeMethods.SendInput((uint)inputs.Length, pInputs, sizeof(INPUT));
            if (sent == (uint)inputs.Length) return (int)sent;
            int err = Marshal.GetLastPInvokeError();
            if (err is 5 or 0) return inputs.Length;
            return (int)sent;
        }
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

                    if (flags != 0 && releaseCount < 32)
                    {
                        ref INPUT inp = ref inputs[releaseCount++];
                        inp.type = WindowsInputNativeMethods.INPUT_MOUSE;
                        inp.mi.dx = 0;
                        inp.mi.dy = 0;
                        inp.mi.mouseData = mouseData;
                        inp.mi.dwFlags = flags;
                    }
                }
            }
            _heldButtons = 0;

            // Release held keyboard keys across 4 64-bit blocks
            ReleaseBlock(ref _heldKey0, 0, inputs, ref releaseCount);
            ReleaseBlock(ref _heldKey1, 1, inputs, ref releaseCount);
            ReleaseBlock(ref _heldKey2, 2, inputs, ref releaseCount);
            ReleaseBlock(ref _heldKey3, 3, inputs, ref releaseCount);
        }

        if (releaseCount > 0)
        {
            WindowsInputNativeMethods.SendInput((uint)releaseCount, inputs, sizeof(INPUT));
        }

        return releaseCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReleaseBlock(ref ulong blockValue, int blockIndex, INPUT* inputs, ref int releaseCount)
    {
        if (blockValue == 0) return;

        while (blockValue != 0 && releaseCount < 32)
        {
            int bit = BitOperations.TrailingZeroCount(blockValue);
            byte vkey = (byte)((blockIndex << 6) | bit);

            uint flags = WindowsInputNativeMethods.KEYEVENTF_KEYUP;
            if (vkey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0xA3 or 0xA5 or 0x90 or 0x6F or 0x2C)
            {
                flags |= WindowsInputNativeMethods.KEYEVENTF_EXTENDEDKEY;
            }

            ref INPUT inp = ref inputs[releaseCount++];
            inp.type = WindowsInputNativeMethods.INPUT_KEYBOARD;
            inp.ki.wVk = vkey;
            inp.ki.wScan = 0;
            inp.ki.dwFlags = flags;

            blockValue &= ~(1UL << bit);
        }

        blockValue = 0;
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
