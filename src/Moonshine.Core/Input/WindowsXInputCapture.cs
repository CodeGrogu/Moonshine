using System.Diagnostics;
using System.Runtime.CompilerServices;
using Moonshine.Interop;
using Moonshine.Protocol.Input;

namespace Moonshine.Core.Input;

public delegate void ControllerStateHandler(in ControllerStatePacket controllerPacket);

/// <summary>
/// High-resolution Windows XInput controller polling service.
/// Enumerate up to 4 controllers, tracks connection lifecycle with adaptive throttling,
/// applies deadzones, and dispatches state updates with zero steady-state heap allocations.
/// </summary>
public sealed class WindowsXInputCapture : IDisposable
{
    public const int MaxControllers = 4;

    private readonly ControllerStateHandler? _onControllerState;
    private readonly Lock _lock = new();

    private readonly bool[] _connected = new bool[MaxControllers];
    private readonly uint[] _lastPacketNumber = new uint[MaxControllers];
    private readonly long[] _nextProbeTicks = new long[MaxControllers];
    private readonly ControllerStatePacket[] _lastStates = new ControllerStatePacket[MaxControllers];

    private readonly bool _applyDeadzones;
    private ulong _totalPolls;
    private ulong _stateChangesDispatched;
    private ulong _disconnectEvents;
    private bool _disposed;

    public static bool IsXInputSupported => WindowsInputNativeMethods.IsXInputSupported;
    public ulong TotalPolls => Volatile.Read(ref _totalPolls);
    public ulong StateChangesDispatched => Volatile.Read(ref _stateChangesDispatched);
    public ulong DisconnectEvents => Volatile.Read(ref _disconnectEvents);

    public int ConnectedControllersCount
    {
        get
        {
            lock (_lock)
            {
                int count = 0;
                for (int i = 0; i < MaxControllers; i++)
                {
                    if (_connected[i]) count++;
                }
                return count;
            }
        }
    }

    public WindowsXInputCapture(
        ControllerStateHandler? onControllerState = null,
        bool applyDeadzones = true)
    {
        _onControllerState = onControllerState;
        _applyDeadzones = applyDeadzones;
    }

    /// <summary>
    /// Checks connection state for a specific controller index (0..3).
    /// </summary>
    public bool IsControllerConnected(int controllerIndex)
    {
        if (controllerIndex is < 0 or >= MaxControllers) return false;
        lock (_lock)
        {
            return _connected[controllerIndex];
        }
    }

    /// <summary>
    /// Executes a single zero-allocation polling pass across all 4 controller slots.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int PollControllers()
    {
        if (_disposed || !IsXInputSupported) return 0;

        Interlocked.Increment(ref _totalPolls);
        long now = Stopwatch.GetTimestamp();
        int activeCount = 0;

        XINPUT_STATE state;

        for (uint slot = 0; slot < MaxControllers; slot++)
        {
            // If slot is currently disconnected, throttle polling to 1Hz discovery probes
            if (!_connected[slot] && now < _nextProbeTicks[slot])
            {
                continue;
            }

            uint result = WindowsInputNativeMethods.XInputGetState(slot, &state);

            if (result == WindowsInputNativeMethods.ERROR_SUCCESS)
            {
                activeCount++;
                if (!_connected[slot])
                {
                    _connected[slot] = true;
                }

                // Only dispatch when packet number changed or on initial connection
                if (state.dwPacketNumber != _lastPacketNumber[slot])
                {
                    _lastPacketNumber[slot] = state.dwPacketNumber;

                    short lx = state.Gamepad.sThumbLX;
                    short ly = state.Gamepad.sThumbLY;
                    short rx = state.Gamepad.sThumbRX;
                    short ry = state.Gamepad.sThumbRY;
                    byte lt = state.Gamepad.bLeftTrigger;
                    byte rt = state.Gamepad.bRightTrigger;

                    if (_applyDeadzones)
                    {
                        // Apply Left Stick Deadzone
                        if (Math.Abs(lx) < WindowsInputNativeMethods.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE &&
                            Math.Abs(ly) < WindowsInputNativeMethods.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE)
                        {
                            lx = 0;
                            ly = 0;
                        }

                        // Apply Right Stick Deadzone
                        if (Math.Abs(rx) < WindowsInputNativeMethods.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE &&
                            Math.Abs(ry) < WindowsInputNativeMethods.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE)
                        {
                            rx = 0;
                            ry = 0;
                        }

                        // Apply Trigger Threshold
                        if (lt < WindowsInputNativeMethods.XINPUT_GAMEPAD_TRIGGER_THRESHOLD) lt = 0;
                        if (rt < WindowsInputNativeMethods.XINPUT_GAMEPAD_TRIGGER_THRESHOLD) rt = 0;
                    }

                    GamepadButtons buttons = MapXInputButtons(state.Gamepad.wButtons);

                    var packet = new ControllerStatePacket(
                        (byte)slot,
                        buttons,
                        lt,
                        rt,
                        lx,
                        ly,
                        rx,
                        ry
                    );

                    _lastStates[slot] = packet;
                    Interlocked.Increment(ref _stateChangesDispatched);
                    _onControllerState?.Invoke(in packet);
                }
            }
            else if (result == WindowsInputNativeMethods.ERROR_DEVICE_NOT_CONNECTED)
            {
                if (_connected[slot])
                {
                    // Device was disconnected: dispatch neutral reset packet to clear held buttons/axes
                    _connected[slot] = false;
                    _lastPacketNumber[slot] = 0;
                    Interlocked.Increment(ref _disconnectEvents);

                    var neutralPacket = new ControllerStatePacket(
                        (byte)slot,
                        GamepadButtons.None,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0
                    );
                    _lastStates[slot] = neutralPacket;
                    _onControllerState?.Invoke(in neutralPacket);
                }

                // Throttle next discovery probe to 1 second
                _nextProbeTicks[slot] = now + Stopwatch.Frequency;
            }
        }

        return activeCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static GamepadButtons MapXInputButtons(ushort wButtons)
    {
        GamepadButtons buttons = GamepadButtons.None;

        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_DPAD_UP) != 0) buttons |= GamepadButtons.DpadUp;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_DPAD_DOWN) != 0) buttons |= GamepadButtons.DpadDown;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_DPAD_LEFT) != 0) buttons |= GamepadButtons.DpadLeft;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_DPAD_RIGHT) != 0) buttons |= GamepadButtons.DpadRight;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_START) != 0) buttons |= GamepadButtons.Start;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_BACK) != 0) buttons |= GamepadButtons.Back;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_LEFT_THUMB) != 0) buttons |= GamepadButtons.LeftThumb;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_RIGHT_THUMB) != 0) buttons |= GamepadButtons.RightThumb;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0) buttons |= GamepadButtons.LeftShoulder;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0) buttons |= GamepadButtons.RightShoulder;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_A) != 0) buttons |= GamepadButtons.A;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_B) != 0) buttons |= GamepadButtons.B;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_X) != 0) buttons |= GamepadButtons.X;
        if ((wButtons & WindowsInputNativeMethods.XINPUT_GAMEPAD_Y) != 0) buttons |= GamepadButtons.Y;

        return buttons;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            for (byte slot = 0; slot < MaxControllers; slot++)
            {
                if (_connected[slot])
                {
                    _connected[slot] = false;
                    var neutralPacket = new ControllerStatePacket(
                        slot,
                        GamepadButtons.None,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0
                    );
                    _onControllerState?.Invoke(in neutralPacket);
                }
            }
        }
    }
}
