using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Core.Input;

public delegate void MouseMoveHandler(short deltaX, short deltaY);
public delegate void MouseButtonHandler(byte buttonIndex, bool isDown);
public delegate void MouseScrollHandler(short scrollDelta);
public delegate void KeyboardKeyHandler(short keyCode, bool isDown, byte modifiers);

/// <summary>
/// Event-driven Windows Raw Input acquisition service for high-DPI mouse, keyboard, and wheel capture.
/// Manages device registration, unaccelerated relative motion, button/modifier states, and stuck-key prevention on focus loss.
/// </summary>
public sealed class WindowsRawInputCapture : IDisposable
{
    private readonly Lock _lock = new();
    private readonly IntPtr _hwndTarget;
    private readonly MouseMoveHandler? _onMouseMove;
    private readonly MouseButtonHandler? _onMouseButton;
    private readonly MouseScrollHandler? _onMouseScroll;
    private readonly KeyboardKeyHandler? _onKeyboardKey;

    private readonly ulong[] _pressedKeysBitmask = new ulong[4]; // 256 virtual keys
    private byte _pressedMouseButtons; // Bits 1..5 for Left, Right, Middle, X1, X2
    private byte _activeModifiers; // 0x01: Shift, 0x02: Ctrl, 0x04: Alt, 0x08: Meta

    private ulong _rawEventsProcessed;
    private ulong _mouseEventsCaptured;
    private ulong _keyboardEventsCaptured;
    private ulong _scrollEventsCaptured;
    private ulong _focusLostClears;
    private bool _isRegistered;
    private bool _disposed;

    public bool IsRegistered => _isRegistered;
    public ulong RawEventsProcessed => Volatile.Read(ref _rawEventsProcessed);
    public ulong MouseEventsCaptured => Volatile.Read(ref _mouseEventsCaptured);
    public ulong KeyboardEventsCaptured => Volatile.Read(ref _keyboardEventsCaptured);
    public ulong ScrollEventsCaptured => Volatile.Read(ref _scrollEventsCaptured);
    public ulong FocusLostClears => Volatile.Read(ref _focusLostClears);

    public WindowsRawInputCapture(
        IntPtr hwndTarget = default,
        MouseMoveHandler? onMouseMove = null,
        MouseButtonHandler? onMouseButton = null,
        MouseScrollHandler? onMouseScroll = null,
        KeyboardKeyHandler? onKeyboardKey = null)
    {
        _hwndTarget = hwndTarget;
        _onMouseMove = onMouseMove;
        _onMouseButton = onMouseButton;
        _onMouseScroll = onMouseScroll;
        _onKeyboardKey = onKeyboardKey;
    }

    /// <summary>
    /// Registers mouse and keyboard devices with the Windows Raw Input subsystem.
    /// </summary>
    public unsafe bool RegisterDevices()
    {
        lock (_lock)
        {
            if (_disposed || _isRegistered) return _isRegistered;

            RAWINPUTDEVICE* devices = stackalloc RAWINPUTDEVICE[2];

            // 1. Mouse: UsagePage Generic (0x01), Usage Mouse (0x02)
            devices[0].usUsagePage = WindowsInputNativeMethods.HID_USAGE_PAGE_GENERIC;
            devices[0].usUsage = WindowsInputNativeMethods.HID_USAGE_GENERIC_MOUSE;
            devices[0].dwFlags = _hwndTarget != IntPtr.Zero
                ? WindowsInputNativeMethods.RIDEV_DEVNOTIFY
                : WindowsInputNativeMethods.RIDEV_INPUTSINK | WindowsInputNativeMethods.RIDEV_DEVNOTIFY;
            devices[0].hwndTarget = _hwndTarget;

            // 2. Keyboard: UsagePage Generic (0x01), Usage Keyboard (0x06)
            devices[1].usUsagePage = WindowsInputNativeMethods.HID_USAGE_PAGE_GENERIC;
            devices[1].usUsage = WindowsInputNativeMethods.HID_USAGE_GENERIC_KEYBOARD;
            devices[1].dwFlags = _hwndTarget != IntPtr.Zero
                ? WindowsInputNativeMethods.RIDEV_DEVNOTIFY
                : WindowsInputNativeMethods.RIDEV_INPUTSINK | WindowsInputNativeMethods.RIDEV_DEVNOTIFY;
            devices[1].hwndTarget = _hwndTarget;

            bool success = WindowsInputNativeMethods.RegisterRawInputDevices(devices, 2, (uint)sizeof(RAWINPUTDEVICE));
            _isRegistered = success;
            return success;
        }
    }

    /// <summary>
    /// Decodes a WM_INPUT message with zero managed heap allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int ProcessRawInput(IntPtr hRawInput)
    {
        if (hRawInput == IntPtr.Zero || _disposed) return -1;

        RAWINPUT rawInput;
        uint dwSize = (uint)sizeof(RAWINPUT);
        uint cbSizeHeader = (uint)sizeof(RAWINPUTHEADER);

        uint bytesRead = WindowsInputNativeMethods.GetRawInputData(
            hRawInput,
            WindowsInputNativeMethods.RID_INPUT,
            &rawInput,
            &dwSize,
            cbSizeHeader
        );

        if (bytesRead == uint.MaxValue || bytesRead == 0)
        {
            return -1;
        }

        return ProcessRawInputData(in rawInput);
    }

    /// <summary>
    /// Processes a decoded or synthetic RAWINPUT structure directly with zero allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ProcessRawInputData(in RAWINPUT rawInput)
    {
        if (_disposed) return -1;

        Interlocked.Increment(ref _rawEventsProcessed);

        if (rawInput.header.dwType == WindowsInputNativeMethods.RIM_TYPEMOUSE)
        {
            ProcessMouseInput(in rawInput.mouse);
            return WindowsInputNativeMethods.RIM_TYPEMOUSE;
        }
        else if (rawInput.header.dwType == WindowsInputNativeMethods.RIM_TYPEKEYBOARD)
        {
            ProcessKeyboardInput(in rawInput.keyboard);
            return WindowsInputNativeMethods.RIM_TYPEKEYBOARD;
        }

        return WindowsInputNativeMethods.RIM_TYPEHID;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessMouseInput(in RAWMOUSE mouse)
    {
        Interlocked.Increment(ref _mouseEventsCaptured);

        // 1. Relative motion
        if ((mouse.usFlags & WindowsInputNativeMethods.MOUSE_MOVE_ABSOLUTE) == 0)
        {
            if (mouse.lLastX != 0 || mouse.lLastY != 0)
            {
                _onMouseMove?.Invoke((short)Math.Clamp(mouse.lLastX, short.MinValue, short.MaxValue),
                                     (short)Math.Clamp(mouse.lLastY, short.MinValue, short.MaxValue));
            }
        }

        // 2. Mouse Buttons
        ushort buttonFlags = mouse.usButtonFlags;
        if (buttonFlags != 0)
        {
            // Left Button (1)
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
            {
                SetMouseButtonDown(1, true);
                _onMouseButton?.Invoke(1, true);
            }
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_LEFT_BUTTON_UP) != 0)
            {
                SetMouseButtonDown(1, false);
                _onMouseButton?.Invoke(1, false);
            }

            // Right Button (3)
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
            {
                SetMouseButtonDown(3, true);
                _onMouseButton?.Invoke(3, true);
            }
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_RIGHT_BUTTON_UP) != 0)
            {
                SetMouseButtonDown(3, false);
                _onMouseButton?.Invoke(3, false);
            }

            // Middle Button (2)
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
            {
                SetMouseButtonDown(2, true);
                _onMouseButton?.Invoke(2, true);
            }
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_MIDDLE_BUTTON_UP) != 0)
            {
                SetMouseButtonDown(2, false);
                _onMouseButton?.Invoke(2, false);
            }

            // XButton 1 (4)
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_BUTTON_4_DOWN) != 0)
            {
                SetMouseButtonDown(4, true);
                _onMouseButton?.Invoke(4, true);
            }
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_BUTTON_4_UP) != 0)
            {
                SetMouseButtonDown(4, false);
                _onMouseButton?.Invoke(4, false);
            }

            // XButton 2 (5)
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_BUTTON_5_DOWN) != 0)
            {
                SetMouseButtonDown(5, true);
                _onMouseButton?.Invoke(5, true);
            }
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_BUTTON_5_UP) != 0)
            {
                SetMouseButtonDown(5, false);
                _onMouseButton?.Invoke(5, false);
            }

            // Vertical Wheel
            if ((buttonFlags & WindowsInputNativeMethods.RI_MOUSE_WHEEL) != 0)
            {
                short wheelDelta = (short)mouse.usButtonData;
                Interlocked.Increment(ref _scrollEventsCaptured);
                _onMouseScroll?.Invoke(wheelDelta);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessKeyboardInput(in RAWKEYBOARD keyboard)
    {
        Interlocked.Increment(ref _keyboardEventsCaptured);

        ushort vkey = keyboard.VKey;
        if (vkey > 255) return;

        bool isDown = (keyboard.Flags & WindowsInputNativeMethods.RI_KEY_BREAK) == 0;

        // Update modifiers
        UpdateModifierState(vkey, isDown);

        // Update key tracking bitmask
        int word = vkey / 64;
        ulong mask = 1UL << (vkey % 64);
        lock (_lock)
        {
            if (isDown)
            {
                _pressedKeysBitmask[word] |= mask;
            }
            else
            {
                _pressedKeysBitmask[word] &= ~mask;
            }
        }

        _onKeyboardKey?.Invoke((short)vkey, isDown, _activeModifiers);
    }

    private void UpdateModifierState(ushort vkey, bool isDown)
    {
        byte modMask = vkey switch
        {
            0x10 or 0xA0 or 0xA1 => 0x01, // Shift / LShift / RShift
            0x11 or 0xA2 or 0xA3 => 0x02, // Control / LControl / RControl
            0x12 or 0xA4 or 0xA5 => 0x04, // Menu / LAlt / RAlt
            0x5B or 0x5C => 0x08,         // LWin / RWin
            _ => 0
        };

        if (modMask != 0)
        {
            if (isDown)
            {
                _activeModifiers |= modMask;
            }
            else
            {
                _activeModifiers &= (byte)~modMask;
            }
        }
    }

    private void SetMouseButtonDown(byte button, bool isDown)
    {
        if (button is < 1 or > 5) return;
        byte mask = (byte)(1 << button);
        if (isDown)
        {
            _pressedMouseButtons |= mask;
        }
        else
        {
            _pressedMouseButtons &= (byte)~mask;
        }
    }

    /// <summary>
    /// Handles window focus loss by releasing all active keys and mouse buttons to eliminate stuck keys on the host.
    /// </summary>
    public void OnFocusLost()
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _focusLostClears);

            // Release all held keyboard keys
            for (int word = 0; word < 4; word++)
            {
                ulong bits = _pressedKeysBitmask[word];
                if (bits != 0)
                {
                    for (int bit = 0; bit < 64; bit++)
                    {
                        if ((bits & (1UL << bit)) != 0)
                        {
                            short vkey = (short)((word * 64) + bit);
                            _onKeyboardKey?.Invoke(vkey, false, 0);
                        }
                    }
                    _pressedKeysBitmask[word] = 0;
                }
            }

            // Release all held mouse buttons
            for (byte btn = 1; btn <= 5; btn++)
            {
                if ((_pressedMouseButtons & (1 << btn)) != 0)
                {
                    _onMouseButton?.Invoke(btn, false);
                }
            }
            _pressedMouseButtons = 0;
            _activeModifiers = 0;
        }
    }

    /// <summary>
    /// Handles window focus gained by refreshing physical modifier key states.
    /// </summary>
    public void OnFocusGained()
    {
        lock (_lock)
        {
            byte modifiers = 0;
            if ((WindowsInputNativeMethods.GetKeyState(0x10) & 0x8000) != 0) modifiers |= 0x01; // Shift
            if ((WindowsInputNativeMethods.GetKeyState(0x11) & 0x8000) != 0) modifiers |= 0x02; // Ctrl
            if ((WindowsInputNativeMethods.GetKeyState(0x12) & 0x8000) != 0) modifiers |= 0x04; // Alt
            if ((WindowsInputNativeMethods.GetKeyState(0x5B) & 0x8000) != 0 ||
                (WindowsInputNativeMethods.GetKeyState(0x5C) & 0x8000) != 0) modifiers |= 0x08; // Win
            _activeModifiers = modifiers;
        }
    }

    public unsafe void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_isRegistered)
            {
                RAWINPUTDEVICE* devices = stackalloc RAWINPUTDEVICE[2];
                devices[0].usUsagePage = WindowsInputNativeMethods.HID_USAGE_PAGE_GENERIC;
                devices[0].usUsage = WindowsInputNativeMethods.HID_USAGE_GENERIC_MOUSE;
                devices[0].dwFlags = WindowsInputNativeMethods.RIDEV_REMOVE;
                devices[0].hwndTarget = IntPtr.Zero;

                devices[1].usUsagePage = WindowsInputNativeMethods.HID_USAGE_PAGE_GENERIC;
                devices[1].usUsage = WindowsInputNativeMethods.HID_USAGE_GENERIC_KEYBOARD;
                devices[1].dwFlags = WindowsInputNativeMethods.RIDEV_REMOVE;
                devices[1].hwndTarget = IntPtr.Zero;

                WindowsInputNativeMethods.RegisterRawInputDevices(devices, 2, (uint)sizeof(RAWINPUTDEVICE));
                _isRegistered = false;
            }

            OnFocusLost();
        }
    }
}
