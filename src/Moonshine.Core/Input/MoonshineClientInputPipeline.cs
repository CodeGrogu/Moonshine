using System.Diagnostics;
using Moonshine.Interop;
using Moonshine.Protocol.Input;

namespace Moonshine.Core.Input;

public readonly record struct ClientInputMetrics(
    ulong RawEventsProcessed,
    ulong MouseEventsCaptured,
    ulong KeyboardEventsCaptured,
    ulong ScrollEventsCaptured,
    ulong ControllerPollsCount,
    ulong ControllerStateChangesCount,
    int ConnectedControllersCount,
    ulong FocusLostClearsCount,
    double PollingFrequencyHz,
    double AverageJitterMicros
);

/// <summary>
/// Unified client-side hardware input acquisition pipeline.
/// Coordinates event-driven Windows Raw Input, high-resolution 1000Hz polling, and XInput game controllers
/// with zero GC heap allocations on the steady-state path.
/// </summary>
public sealed class MoonshineClientInputPipeline : IDisposable
{
    private readonly Lock _lock = new();
    private readonly InputPollingEngine _pollingEngine;
    private readonly WindowsRawInputCapture _rawInputCapture;
    private readonly WindowsXInputCapture _xinputCapture;
    private readonly Action<ReadOnlySpan<byte>>? _packetTransmitter;

    private readonly Thread? _xinputPollingThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly uint _controllerPollRateHz;
    private bool _running;
    private bool _disposed;

    public bool IsRunning
    {
        get
        {
            lock (_lock) return _running;
        }
    }

    public WindowsRawInputCapture RawInputCapture => _rawInputCapture;
    public WindowsXInputCapture XInputCapture => _xinputCapture;
    public InputPollingEngine PollingEngine => _pollingEngine;

    public ClientInputMetrics Metrics
    {
        get
        {
            var pollerMetrics = _pollingEngine.Metrics;
            return new ClientInputMetrics(
                _rawInputCapture.RawEventsProcessed,
                _rawInputCapture.MouseEventsCaptured,
                _rawInputCapture.KeyboardEventsCaptured,
                _rawInputCapture.ScrollEventsCaptured,
                _xinputCapture.TotalPolls,
                _xinputCapture.StateChangesDispatched,
                _xinputCapture.ConnectedControllersCount,
                _rawInputCapture.FocusLostClears,
                pollerMetrics.MeasuredFrequencyHz,
                pollerMetrics.AverageJitterMicros
            );
        }
    }

    public MoonshineClientInputPipeline(
        IntPtr hwndTarget = default,
        uint pollingFrequencyHz = 1000,
        uint controllerPollRateHz = 250,
        Action<ReadOnlySpan<byte>>? packetTransmitter = null,
        bool applyDeadzones = true)
    {
        _controllerPollRateHz = Math.Clamp(controllerPollRateHz, 10, 1000);
        _packetTransmitter = packetTransmitter;

        // 1. Initialise 1000Hz delta aggregation engine
        _pollingEngine = new InputPollingEngine(pollingFrequencyHz, TransmitPacket);

        // 2. Initialise Event-Driven Windows Raw Input capture
        _rawInputCapture = new WindowsRawInputCapture(
            hwndTarget,
            onMouseMove: (dx, dy) => _pollingEngine.IngestMouseMove(dx, dy),
            onMouseButton: (btn, isDown) => _pollingEngine.IngestMouseButton(btn, isDown),
            onMouseScroll: delta => IngestMouseScroll(delta),
            onKeyboardKey: (vkey, isDown, mods) => _pollingEngine.IngestKeyboardKey(vkey, isDown, mods)
        );

        // 3. Initialise XInput Gamepad capture
        _xinputCapture = new WindowsXInputCapture(
            onControllerState: (in ControllerStatePacket state) => _pollingEngine.IngestGamepadState(in state),
            applyDeadzones: applyDeadzones
        );

        if (WindowsXInputCapture.IsXInputSupported)
        {
            _xinputPollingThread = new Thread(XInputPollingLoop)
            {
                Name = "Moonshine-XInput-Poller",
                Priority = ThreadPriority.AboveNormal,
                IsBackground = true
            };
        }
    }

    /// <summary>
    /// Starts local input device registration and polling loops.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || _running) return;
            _running = true;

            _rawInputCapture.RegisterDevices();
            _pollingEngine.Start();
            _xinputPollingThread?.Start();
        }
    }

    /// <summary>
    /// Forwards a native WM_INPUT message to the Raw Input decoder.
    /// </summary>
    public int ProcessRawInput(IntPtr hRawInput) => _rawInputCapture.ProcessRawInput(hRawInput);

    /// <summary>
    /// Forwards a decoded or synthetic RAWINPUT structure directly.
    /// </summary>
    public int ProcessRawInputData(in RAWINPUT rawInput) => _rawInputCapture.ProcessRawInputData(in rawInput);

    /// <summary>
    /// Handles window focus loss by clearing all held keys/buttons.
    /// </summary>
    public void OnFocusLost() => _rawInputCapture.OnFocusLost();

    /// <summary>
    /// Handles window focus gain by refreshing keyboard modifier states.
    /// </summary>
    public void OnFocusGained() => _rawInputCapture.OnFocusGained();

    private void IngestMouseScroll(short scrollDelta)
    {
        Span<byte> buffer = stackalloc byte[MouseScrollPacket.PacketSize];
        var packet = new MouseScrollPacket(scrollDelta);
        int written = packet.WriteTo(buffer);
        if (written > 0)
        {
            TransmitPacket(buffer);
        }
    }

    private void TransmitPacket(ReadOnlySpan<byte> packet)
    {
        _packetTransmitter?.Invoke(packet);
    }

    private void XInputPollingLoop()
    {
        long intervalTicks = Stopwatch.Frequency / _controllerPollRateHz;
        long nextTick = Stopwatch.GetTimestamp();

        while (!_cts.IsCancellationRequested)
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= nextTick)
            {
                _xinputCapture.PollControllers();
                nextTick += intervalTicks;
                if (now > nextTick + (intervalTicks * 4))
                {
                    nextTick = now + intervalTicks;
                }
            }
            else
            {
                long remainingTicks = nextTick - now;
                if (remainingTicks > (Stopwatch.Frequency / 1000) * 2)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.SpinWait(20);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;

            _cts.Cancel();
            if (_xinputPollingThread != null && _xinputPollingThread.IsAlive)
            {
                _xinputPollingThread.Join(500);
            }
            _cts.Dispose();

            _rawInputCapture.Dispose();
            _xinputCapture.Dispose();
            _pollingEngine.Dispose();
        }
    }
}
