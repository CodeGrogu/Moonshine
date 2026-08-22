using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Input;

namespace Moonshine.Host.Input;

/// <summary>
/// Native Windows virtual game controller injector interfacing dynamically with kernel bus drivers
/// (e.g. Nefarius ViGEmBus) with fail-closed capability detection and zero false simulations.
/// </summary>
public sealed unsafe class WindowsVirtualGamepadInjector : IWindowsVirtualGamepadInjector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XUSB_REPORT
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    private readonly struct VigemBindings
    {
        public readonly IntPtr Module;
        public readonly delegate* unmanaged[Cdecl]<IntPtr> Alloc;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, void> Free;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, int> Connect;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, void> Disconnect;
        public readonly delegate* unmanaged[Cdecl]<IntPtr> TargetX360Alloc;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, void> TargetFree;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int> TargetAdd;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int> TargetRemove;
        public readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, XUSB_REPORT, int> TargetX360Update;

        public VigemBindings(
            IntPtr module,
            delegate* unmanaged[Cdecl]<IntPtr> alloc,
            delegate* unmanaged[Cdecl]<IntPtr, void> free,
            delegate* unmanaged[Cdecl]<IntPtr, int> connect,
            delegate* unmanaged[Cdecl]<IntPtr, void> disconnect,
            delegate* unmanaged[Cdecl]<IntPtr> targetX360Alloc,
            delegate* unmanaged[Cdecl]<IntPtr, void> targetFree,
            delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int> targetAdd,
            delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int> targetRemove,
            delegate* unmanaged[Cdecl]<IntPtr, IntPtr, XUSB_REPORT, int> targetX360Update)
        {
            Module = module;
            Alloc = alloc;
            Free = free;
            Connect = connect;
            Disconnect = disconnect;
            TargetX360Alloc = targetX360Alloc;
            TargetFree = targetFree;
            TargetAdd = targetAdd;
            TargetRemove = targetRemove;
            TargetX360Update = targetX360Update;
        }
    }

    private static readonly VigemBindings _bindings = LoadVigemBindings();

    private static VigemBindings LoadVigemBindings()
    {
        string[] candidateDlls = ["vigemclient.dll", "ViGEmClient.dll"];
        foreach (var dll in candidateDlls)
        {
            if (NativeLibrary.TryLoad(dll, out var handle))
            {
                if (NativeLibrary.TryGetExport(handle, "vigem_alloc", out var pAlloc) &&
                    NativeLibrary.TryGetExport(handle, "vigem_free", out var pFree) &&
                    NativeLibrary.TryGetExport(handle, "vigem_connect", out var pConnect) &&
                    NativeLibrary.TryGetExport(handle, "vigem_disconnect", out var pDisconnect) &&
                    NativeLibrary.TryGetExport(handle, "vigem_target_x360_alloc", out var pTargetAlloc) &&
                    NativeLibrary.TryGetExport(handle, "vigem_target_free", out var pTargetFree) &&
                    NativeLibrary.TryGetExport(handle, "vigem_target_add", out var pTargetAdd) &&
                    NativeLibrary.TryGetExport(handle, "vigem_target_remove", out var pTargetRemove) &&
                    NativeLibrary.TryGetExport(handle, "vigem_target_x360_update", out var pTargetUpdate))
                {
                    return new VigemBindings(
                        handle,
                        (delegate* unmanaged[Cdecl]<IntPtr>)pAlloc,
                        (delegate* unmanaged[Cdecl]<IntPtr, void>)pFree,
                        (delegate* unmanaged[Cdecl]<IntPtr, int>)pConnect,
                        (delegate* unmanaged[Cdecl]<IntPtr, void>)pDisconnect,
                        (delegate* unmanaged[Cdecl]<IntPtr>)pTargetAlloc,
                        (delegate* unmanaged[Cdecl]<IntPtr, void>)pTargetFree,
                        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)pTargetAdd,
                        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)pTargetRemove,
                        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, XUSB_REPORT, int>)pTargetUpdate
                    );
                }
            }
        }
        return default;
    }

    private readonly IntPtr _clientHandle;
    private readonly IntPtr[] _targets = new IntPtr[4];
    private readonly bool _driverAvailable;
    private readonly Lock _syncRoot = new();
    private bool _disposed;

    public WindowsVirtualGamepadInjector()
    {
        if (_bindings.Module != IntPtr.Zero && _bindings.Alloc != null)
        {
            _clientHandle = _bindings.Alloc();
            if (_clientHandle != IntPtr.Zero && _bindings.Connect != null)
            {
                int error = _bindings.Connect(_clientHandle);
                _driverAvailable = error == 0;
            }
        }
    }

    public bool IsDriverAvailable => _driverAvailable && !_disposed;

    public int AllocatedControllerCount
    {
        get
        {
            lock (_syncRoot)
            {
                int count = 0;
                for (int i = 0; i < _targets.Length; i++)
                {
                    if (_targets[i] != IntPtr.Zero) count++;
                }
                return count;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool UpdateControllerState(byte controllerIndex, in ControllerStatePacket packet)
    {
        if (!IsDriverAvailable || controllerIndex >= 4) return false;

        IntPtr target = EnsureTargetAllocated(controllerIndex);
        if (target == IntPtr.Zero || _bindings.TargetX360Update == null) return false;

        XUSB_REPORT report = default;
        report.wButtons = packet.Buttons;
        report.bLeftTrigger = packet.LeftTrigger;
        report.bRightTrigger = packet.RightTrigger;
        report.sThumbLX = packet.LeftStickX;
        report.sThumbLY = packet.LeftStickY;
        report.sThumbRX = packet.RightStickX;
        report.sThumbRY = packet.RightStickY;

        int result = _bindings.TargetX360Update(_clientHandle, target, report);
        return result == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool UpdateControllerState(byte controllerIndex, in MoonshineInputGamepadPayload payload)
    {
        if (!IsDriverAvailable || controllerIndex >= 4) return false;

        IntPtr target = EnsureTargetAllocated(controllerIndex);
        if (target == IntPtr.Zero || _bindings.TargetX360Update == null) return false;

        XUSB_REPORT report = default;
        report.wButtons = payload.ButtonMask;
        report.bLeftTrigger = payload.LeftTrigger;
        report.bRightTrigger = payload.RightTrigger;
        report.sThumbLX = payload.ThumbLx;
        report.sThumbLY = payload.ThumbLy;
        report.sThumbRX = payload.ThumbRx;
        report.sThumbRY = payload.ThumbRy;

        int result = _bindings.TargetX360Update(_clientHandle, target, report);
        return result == 0;
    }

    private IntPtr EnsureTargetAllocated(byte controllerIndex)
    {
        lock (_syncRoot)
        {
            if (_disposed || !_driverAvailable) return IntPtr.Zero;

            if (_targets[controllerIndex] == IntPtr.Zero && _bindings.TargetX360Alloc != null && _bindings.TargetAdd != null)
            {
                IntPtr target = _bindings.TargetX360Alloc();
                if (target != IntPtr.Zero)
                {
                    int addResult = _bindings.TargetAdd(_clientHandle, target);
                    if (addResult == 0)
                    {
                        _targets[controllerIndex] = target;
                    }
                    else
                    {
                        if (_bindings.TargetFree != null) _bindings.TargetFree(target);
                        return IntPtr.Zero;
                    }
                }
            }

            return _targets[controllerIndex];
        }
    }

    public void DisconnectAll()
    {
        lock (_syncRoot)
        {
            if (!_driverAvailable) return;

            for (int i = 0; i < _targets.Length; i++)
            {
                if (_targets[i] != IntPtr.Zero)
                {
                    if (_bindings.TargetRemove != null) _bindings.TargetRemove(_clientHandle, _targets[i]);
                    if (_bindings.TargetFree != null) _bindings.TargetFree(_targets[i]);
                    _targets[i] = IntPtr.Zero;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_syncRoot)
        {
            if (_disposed) return;
            DisconnectAll();

            if (_clientHandle != IntPtr.Zero)
            {
                if (_bindings.Disconnect != null) _bindings.Disconnect(_clientHandle);
                if (_bindings.Free != null) _bindings.Free(_clientHandle);
            }

            _disposed = true;
        }
    }
}
