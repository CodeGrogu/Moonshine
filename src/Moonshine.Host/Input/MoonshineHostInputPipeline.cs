using System.Diagnostics;
using System.Runtime.CompilerServices;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Input;

namespace Moonshine.Host.Input;

public readonly record struct HostInputMetrics(
    long TotalPacketsReceived,
    long MouseMovesInjected,
    long MouseButtonsInjected,
    long MouseScrollsInjected,
    long KeyboardKeysInjected,
    long GamepadStatesInjected,
    long PacketsRejected,
    long BytesReceived,
    long StuckKeysReleased,
    double LastDispatchLatencyUs
);

public sealed record HostInputConfig
{
    public int ScreenWidth { get; init; } = 1920;
    public int ScreenHeight { get; init; } = 1080;
    public bool EnforceSequenceMonotonicity { get; init; } = true;
    public ulong ExpectedSessionId { get; init; }

    public static HostInputConfig Default => new();
}

/// <summary>
/// Custom host-side remote input pipeline for receiving, validating, and injecting native Moonshine
/// keyboard, mouse, and game controller input with zero steady-state heap allocations.
/// </summary>
public sealed class MoonshineHostInputPipeline : IDisposable
{
    private readonly IWindowsInputInjector _inputInjector;
    private readonly IWindowsVirtualGamepadInjector _gamepadInjector;
    private readonly HostInputConfig _config;

    private long _totalPacketsReceived;
    private long _mouseMovesInjected;
    private long _mouseButtonsInjected;
    private long _mouseScrollsInjected;
    private long _keyboardKeysInjected;
    private long _gamepadStatesInjected;
    private long _packetsRejected;
    private long _bytesReceived;
    private long _stuckKeysReleased;
    private double _lastDispatchLatencyUs;

    private uint _lastSequenceNumber;
    private bool _hasReceivedSequence;
    private bool _disposed;
    private readonly Lock _syncRoot = new();

    public MoonshineHostInputPipeline(
        IWindowsInputInjector? inputInjector = null,
        IWindowsVirtualGamepadInjector? gamepadInjector = null,
        HostInputConfig? config = null)
    {
        _inputInjector = inputInjector ?? new WindowsSendInputInjector();
        _gamepadInjector = gamepadInjector ?? new WindowsVirtualGamepadInjector();
        _config = config ?? HostInputConfig.Default;
    }

    public HostInputConfig Config => _config;

    public HostInputMetrics Metrics => new(
        Interlocked.Read(ref _totalPacketsReceived),
        Interlocked.Read(ref _mouseMovesInjected),
        Interlocked.Read(ref _mouseButtonsInjected),
        Interlocked.Read(ref _mouseScrollsInjected),
        Interlocked.Read(ref _keyboardKeysInjected),
        Interlocked.Read(ref _gamepadStatesInjected),
        Interlocked.Read(ref _packetsRejected),
        Interlocked.Read(ref _bytesReceived),
        Interlocked.Read(ref _stuckKeysReleased),
        Volatile.Read(ref _lastDispatchLatencyUs)
    );

    public bool IsVirtualGamepadDriverAvailable => _gamepadInjector.IsDriverAvailable;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ProcessInputPacket(ReadOnlySpan<byte> datagram)
    {
        if (_disposed || datagram.IsEmpty)
        {
            Interlocked.Increment(ref _packetsRejected);
            return false;
        }

        long startTimestamp = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _totalPacketsReceived);
        Interlocked.Add(ref _bytesReceived, datagram.Length);

        bool success;

        // Check if the datagram starts with the 32-byte MNBP header ('MSHN' magic = 0x4D53484E)
        if (datagram.Length >= MoonshineProtocolConstants.HeaderSize &&
            datagram[0] == 0x4D && datagram[1] == 0x53 && datagram[2] == 0x48 && datagram[3] == 0x4E)
        {
            success = ProcessMnbpPacket(datagram);
        }
        else
        {
            success = ProcessCompactPacket(datagram);
        }

        long endTimestamp = Stopwatch.GetTimestamp();
        double elapsedUs = (endTimestamp - startTimestamp) * 1_000_000.0 / Stopwatch.Frequency;
        Volatile.Write(ref _lastDispatchLatencyUs, elapsedUs);

        if (!success)
        {
            Interlocked.Increment(ref _packetsRejected);
        }

        return success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ProcessMnbpPacket(ReadOnlySpan<byte> datagram)
    {
        MoonshineErrorCode headerResult = MoonshineProtocolCodec.TryReadHeader(datagram, out var header);
        if (headerResult != MoonshineErrorCode.Success)
        {
            return false;
        }

        if (_config.ExpectedSessionId != 0 && header.SessionId != _config.ExpectedSessionId)
        {
            return false;
        }

        if (_config.EnforceSequenceMonotonicity)
        {
            lock (_syncRoot)
            {
                if (_hasReceivedSequence && header.SequenceNumber <= _lastSequenceNumber)
                {
                    return false;
                }
                _lastSequenceNumber = header.SequenceNumber;
                _hasReceivedSequence = true;
            }
        }

        ReadOnlySpan<byte> payload = datagram[MoonshineProtocolConstants.HeaderSize..];
        if (payload.Length < header.PayloadSize)
        {
            return false;
        }

        switch (header.MessageType)
        {
            case MoonshineMessageType.InputKeyboard:
                {
                    if (MoonshineProtocolCodec.TryReadKeyboardInput(payload, out var keyboardPayload) != MoonshineErrorCode.Success)
                    {
                        return false;
                    }
                    bool injected = _inputInjector.InjectKeyboardKey((short)keyboardPayload.KeyCode, (short)keyboardPayload.ScanCode, keyboardPayload.IsDown != 0, keyboardPayload.Modifiers);
                    if (injected) Interlocked.Increment(ref _keyboardKeysInjected);
                    return injected;
                }

            case MoonshineMessageType.InputMouse:
                {
                    if (MoonshineProtocolCodec.TryReadMouseInput(payload, out var mousePayload) != MoonshineErrorCode.Success)
                    {
                        return false;
                    }

                    bool injectedAny = false;

                    // Relative or Absolute Motion
                    if (mousePayload.IsAbsolute != 0)
                    {
                        if (_inputInjector.InjectMouseMoveAbsolute(mousePayload.X, mousePayload.Y, _config.ScreenWidth, _config.ScreenHeight))
                        {
                            injectedAny = true;
                            Interlocked.Increment(ref _mouseMovesInjected);
                        }
                    }
                    else if (mousePayload.X != 0 || mousePayload.Y != 0)
                    {
                        if (_inputInjector.InjectMouseMove((short)mousePayload.X, (short)mousePayload.Y))
                        {
                            injectedAny = true;
                            Interlocked.Increment(ref _mouseMovesInjected);
                        }
                    }

                    // Vertical Wheel
                    if (mousePayload.WheelDeltaY != 0)
                    {
                        if (_inputInjector.InjectMouseScroll(mousePayload.WheelDeltaY, isHorizontal: false))
                        {
                            injectedAny = true;
                            Interlocked.Increment(ref _mouseScrollsInjected);
                        }
                    }

                    // Horizontal Wheel
                    if (mousePayload.WheelDeltaX != 0)
                    {
                        if (_inputInjector.InjectMouseScroll(mousePayload.WheelDeltaX, isHorizontal: true))
                        {
                            injectedAny = true;
                            Interlocked.Increment(ref _mouseScrollsInjected);
                        }
                    }

                    // Button Transitions
                    if (mousePayload.ButtonFlags != 0)
                    {
                        for (byte b = 1; b <= 5; b++)
                        {
                            if ((mousePayload.ButtonFlags & (1 << (b - 1))) != 0)
                            {
                                if (_inputInjector.InjectMouseButton(b, isDown: true))
                                {
                                    injectedAny = true;
                                    Interlocked.Increment(ref _mouseButtonsInjected);
                                }
                            }
                        }
                    }

                    return injectedAny || (mousePayload.X == 0 && mousePayload.Y == 0 && mousePayload.ButtonFlags == 0 && mousePayload.WheelDeltaX == 0 && mousePayload.WheelDeltaY == 0);
                }

            case MoonshineMessageType.InputGamepad:
                {
                    if (MoonshineProtocolCodec.TryReadGamepadInput(payload, out var gamepadPayload) != MoonshineErrorCode.Success)
                    {
                        return false;
                    }
                    bool updated = _gamepadInjector.UpdateControllerState(gamepadPayload.GamepadIndex, in gamepadPayload);
                    if (updated) Interlocked.Increment(ref _gamepadStatesInjected);
                    return updated;
                }

            default:
                return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ProcessCompactPacket(ReadOnlySpan<byte> datagram)
    {
        if (MouseMovePacket.TryParse(datagram, out var mouseMove))
        {
            bool injected = _inputInjector.InjectMouseMove(mouseMove.DeltaX, mouseMove.DeltaY);
            if (injected) Interlocked.Increment(ref _mouseMovesInjected);
            return injected;
        }

        if (MouseButtonPacket.TryParse(datagram, out var mouseButton))
        {
            bool injected = _inputInjector.InjectMouseButton(mouseButton.ButtonIndex, mouseButton.IsDown != 0);
            if (injected) Interlocked.Increment(ref _mouseButtonsInjected);
            return injected;
        }

        if (KeyboardPacket.TryParse(datagram, out var keyboard))
        {
            bool injected = _inputInjector.InjectKeyboardKey(keyboard.KeyCode, 0, keyboard.PacketType == InputPacketType.KeyDown, keyboard.Modifiers);
            if (injected) Interlocked.Increment(ref _keyboardKeysInjected);
            return injected;
        }

        if (MouseScrollPacket.TryParse(datagram, out var scroll))
        {
            bool injected = _inputInjector.InjectMouseScroll(scroll.ScrollDelta);
            if (injected) Interlocked.Increment(ref _mouseScrollsInjected);
            return injected;
        }

        if (ControllerStatePacket.TryParse(datagram, out var controller))
        {
            bool updated = _gamepadInjector.UpdateControllerState(controller.ControllerNumber, in controller);
            if (updated) Interlocked.Increment(ref _gamepadStatesInjected);
            return updated;
        }

        return false;
    }

    public void ResetSession()
    {
        lock (_syncRoot)
        {
            int released = _inputInjector.ReleaseAllHeldInputs();
            _gamepadInjector.DisconnectAll();
            _hasReceivedSequence = false;
            _lastSequenceNumber = 0;
            if (released > 0)
            {
                Interlocked.Add(ref _stuckKeysReleased, released);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_syncRoot)
        {
            if (_disposed) return;
            ResetSession();
            _inputInjector.Dispose();
            _gamepadInjector.Dispose();
            _disposed = true;
        }
    }
}
