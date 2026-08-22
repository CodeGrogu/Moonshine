using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Moonshine.Interop;

#region Raw Input Structs

[StructLayout(LayoutKind.Sequential)]
public struct RAWINPUTDEVICE
{
    public ushort usUsagePage;
    public ushort usUsage;
    public uint dwFlags;
    public IntPtr hwndTarget;
}

[StructLayout(LayoutKind.Sequential)]
public struct RAWINPUTHEADER
{
    public uint dwType;
    public uint dwSize;
    public IntPtr hDevice;
    public IntPtr wParam;
}

[StructLayout(LayoutKind.Explicit)]
public struct RAWMOUSE
{
    [FieldOffset(0)] public ushort usFlags;
    [FieldOffset(2)] public uint ulButtons;
    [FieldOffset(2)] public ushort usButtonFlags;
    [FieldOffset(4)] public ushort usButtonData;
    [FieldOffset(8)] public uint ulRawButtons;
    [FieldOffset(12)] public int lLastX;
    [FieldOffset(16)] public int lLastY;
    [FieldOffset(20)] public uint ulExtraInformation;
}

[StructLayout(LayoutKind.Sequential)]
public struct RAWKEYBOARD
{
    public ushort MakeCode;
    public ushort Flags;
    public ushort Reserved;
    public ushort VKey;
    public uint Message;
    public uint ExtraInformation;
}

[StructLayout(LayoutKind.Sequential)]
public struct RAWHID
{
    public uint dwSizeHid;
    public uint dwCount;
    public byte bRawData;
}

[StructLayout(LayoutKind.Explicit)]
public struct RAWINPUT
{
    [FieldOffset(0)] public RAWINPUTHEADER header;
    [FieldOffset(24)] public RAWMOUSE mouse;
    [FieldOffset(24)] public RAWKEYBOARD keyboard;
    [FieldOffset(24)] public RAWHID hid;
}

#endregion

#region XInput Structs

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_GAMEPAD
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX;
    public short sThumbLY;
    public short sThumbRX;
    public short sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_STATE
{
    public uint dwPacketNumber;
    public XINPUT_GAMEPAD Gamepad;
}

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_VIBRATION
{
    public ushort wLeftMotorSpeed;
    public ushort wRightMotorSpeed;
}

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_CAPABILITIES
{
    public byte Type;
    public byte SubType;
    public ushort Flags;
    public XINPUT_GAMEPAD Gamepad;
    public XINPUT_VIBRATION Vibration;
}

#endregion

/// <summary>
/// Direct Windows P/Invoke interop for User32 Raw Input and dynamic XInput game controller APIs.
/// </summary>
public static unsafe partial class WindowsInputNativeMethods
{
    public const int RIM_TYPEMOUSE = 0;
    public const int RIM_TYPEKEYBOARD = 1;
    public const int RIM_TYPEHID = 2;

    public const uint RID_INPUT = 0x10000003;
    public const uint RID_HEADER = 0x10000005;

    public const uint RIDEV_REMOVE = 0x00000001;
    public const uint RIDEV_EXCLUDE = 0x00000010;
    public const uint RIDEV_PAGEONLY = 0x00000020;
    public const uint RIDEV_NOLEGACY = 0x00000030;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_CAPTUREMOUSE = 0x00000200;
    public const uint RIDEV_NOHOTKEY = 0x00000200;
    public const uint RIDEV_APPKEYS = 0x00000400;
    public const uint RIDEV_EXINPUTSINK = 0x00001000;
    public const uint RIDEV_DEVNOTIFY = 0x00002000;

    public const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    public const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
    public const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;

    // Mouse button flags
    public const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    public const ushort RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
    public const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    public const ushort RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
    public const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
    public const ushort RI_MOUSE_BUTTON_4_UP = 0x0080;
    public const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;
    public const ushort RI_MOUSE_BUTTON_5_UP = 0x0200;
    public const ushort RI_MOUSE_WHEEL = 0x0400;
    public const ushort RI_MOUSE_HWHEEL = 0x0800;

    // Mouse motion attributes
    public const ushort MOUSE_MOVE_RELATIVE = 0x0000;
    public const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;
    public const ushort MOUSE_VIRTUAL_DESKTOP = 0x0002;
    public const ushort MOUSE_ATTRIBUTES_CHANGED = 0x0004;

    // Keyboard flags
    public const ushort RI_KEY_MAKE = 0x0000;
    public const ushort RI_KEY_BREAK = 0x0001;
    public const ushort RI_KEY_E0 = 0x0002;
    public const ushort RI_KEY_E1 = 0x0004;

    // Windows Messages
    public const int WM_INPUT = 0x00FF;
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
    public const int WM_KILLFOCUS = 0x0008;
    public const int WM_SETFOCUS = 0x0007;
    public const int WM_ACTIVATE = 0x0006;
    public const int WA_INACTIVE = 0;

    // XInput Error Codes
    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

    // XInput Gamepad Button Flags
    public const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
    public const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
    public const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
    public const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
    public const ushort XINPUT_GAMEPAD_START = 0x0010;
    public const ushort XINPUT_GAMEPAD_BACK = 0x0020;
    public const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
    public const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
    public const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
    public const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
    public const ushort XINPUT_GAMEPAD_A = 0x1000;
    public const ushort XINPUT_GAMEPAD_B = 0x2000;
    public const ushort XINPUT_GAMEPAD_X = 0x4000;
    public const ushort XINPUT_GAMEPAD_Y = 0x8000;

    // XInput Thumbstick Deadzones
    public const short XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE = 7849;
    public const short XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE = 8689;
    public const byte XINPUT_GAMEPAD_TRIGGER_THRESHOLD = 30;

    #region User32 Raw Input P/Invoke

    [LibraryImport("user32.dll", EntryPoint = "RegisterRawInputDevices", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices(
        RAWINPUTDEVICE* pRawInputDevices,
        uint uiNumDevices,
        uint cbSize
    );

    [LibraryImport("user32.dll", EntryPoint = "GetRawInputData", SetLastError = true)]
    public static partial uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        void* pData,
        uint* pcbSize,
        uint cbSizeHeader
    );

    [LibraryImport("user32.dll", EntryPoint = "DefRawInputProc")]
    public static partial IntPtr DefRawInputProc(
        RAWINPUT** paRawInput,
        int nInput,
        uint cbSizeHeader
    );

    [LibraryImport("user32.dll", EntryPoint = "GetKeyState")]
    public static partial short GetKeyState(int nVirtKey);

    [LibraryImport("user32.dll", EntryPoint = "GetRawInputDeviceList", SetLastError = true)]
    public static partial uint GetRawInputDeviceList(
        void* pRawInputDeviceList,
        uint* puiNumDevices,
        uint cbSize
    );

    #endregion

    #region Dynamic XInput Loader

    private static readonly IntPtr _xinputHandle;
    private static readonly delegate* unmanaged[Cdecl]<uint, XINPUT_STATE*, uint> _pfnXInputGetState;
    private static readonly delegate* unmanaged[Cdecl]<uint, uint, XINPUT_CAPABILITIES*, uint> _pfnXInputGetCapabilities;

    public static bool IsXInputSupported => _pfnXInputGetState != null;

    static WindowsInputNativeMethods()
    {
        string[] xinputDlls = ["xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll"];
        foreach (var dll in xinputDlls)
        {
            if (NativeLibrary.TryLoad(dll, out var handle))
            {
                _xinputHandle = handle;
                if (NativeLibrary.TryGetExport(handle, "XInputGetState", out var pfnGetState))
                {
                    _pfnXInputGetState = (delegate* unmanaged[Cdecl]<uint, XINPUT_STATE*, uint>)pfnGetState;
                }
                if (NativeLibrary.TryGetExport(handle, "XInputGetCapabilities", out var pfnGetCaps))
                {
                    _pfnXInputGetCapabilities = (delegate* unmanaged[Cdecl]<uint, uint, XINPUT_CAPABILITIES*, uint>)pfnGetCaps;
                }
                break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint XInputGetState(uint dwUserIndex, XINPUT_STATE* pState)
    {
        if (_pfnXInputGetState == null)
        {
            return ERROR_DEVICE_NOT_CONNECTED;
        }
        return _pfnXInputGetState(dwUserIndex, pState);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint XInputGetCapabilities(uint dwUserIndex, uint dwFlags, XINPUT_CAPABILITIES* pCapabilities)
    {
        if (_pfnXInputGetCapabilities == null)
        {
            return ERROR_DEVICE_NOT_CONNECTED;
        }
        return _pfnXInputGetCapabilities(dwUserIndex, dwFlags, pCapabilities);
    }

    #endregion
}
