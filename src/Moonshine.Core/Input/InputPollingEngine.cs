using System.Diagnostics;
using Moonshine.Protocol.Input;

namespace Moonshine.Core.Input;

public sealed record InputEngineMetrics(
    ulong SamplesPolled,
    ulong PacketsEmitted,
    double MeasuredFrequencyHz,
    double AverageJitterMicros
);

/// <summary>
/// High-resolution 1000Hz raw input polling engine.
/// Delivers sub-millisecond mouse, keyboard, and gamepad state sampling without timing jitter.
/// </summary>
public sealed class InputPollingEngine : IDisposable
{
    private readonly Thread _pollingThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _lock = new();

    private readonly uint _targetFrequencyHz;
    private readonly long _targetIntervalTicks;
    private readonly Action<ReadOnlySpan<byte>>? _packetTransmitter;

    private ulong _samplesPolled;
    private ulong _packetsEmitted;
    private double _measuredFrequencyHz;
    private double _averageJitterMicros;
    private bool _running;
    private bool _disposed;

    // Thread-safe atomic input state staging
    private int _accumulatedMouseDeltaX;
    private int _accumulatedMouseDeltaY;
    private ControllerStatePacket _latestGamepadState;
    private int _hasPendingGamepadState;

    public uint TargetFrequencyHz => _targetFrequencyHz;
    public bool IsRunning => _running;

    public InputEngineMetrics Metrics => new(
        Volatile.Read(ref _samplesPolled),
        Volatile.Read(ref _packetsEmitted),
        Volatile.Read(ref _measuredFrequencyHz),
        Volatile.Read(ref _averageJitterMicros)
    );

    public InputPollingEngine(
        uint targetFrequencyHz = 1000,
        Action<ReadOnlySpan<byte>>? packetTransmitter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetFrequencyHz);

        _targetFrequencyHz = targetFrequencyHz;
        _targetIntervalTicks = Stopwatch.Frequency / _targetFrequencyHz;
        _packetTransmitter = packetTransmitter;

        _pollingThread = new Thread(PollingLoop)
        {
            Name = "Moonshine-1000Hz-Input-Poller",
            Priority = ThreadPriority.Highest,
            IsBackground = true
        };
    }

    /// <summary>
    /// Starts the high-resolution 1000Hz polling loop.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || _running) return;
            _running = true;
            _pollingThread.Start();
        }
    }

    /// <summary>
    /// Ingests relative high-DPI raw mouse deltas.
    /// </summary>
    public void IngestMouseMove(short deltaX, short deltaY)
    {
        Interlocked.Add(ref _accumulatedMouseDeltaX, deltaX);
        Interlocked.Add(ref _accumulatedMouseDeltaY, deltaY);
    }

    /// <summary>
    /// Emits a single mouse button press or release.
    /// </summary>
    public void IngestMouseButton(byte buttonIndex, bool isDown)
    {
        Span<byte> buffer = stackalloc byte[MouseButtonPacket.PacketSize];
        var packet = new MouseButtonPacket(buttonIndex, isDown);
        int written = packet.WriteTo(buffer);
        if (written > 0)
        {
            TransmitPacket(buffer);
        }
    }

    /// <summary>
    /// Emits a keyboard key state change.
    /// </summary>
    public void IngestKeyboardKey(short keyCode, bool isDown, byte modifiers = 0)
    {
        Span<byte> buffer = stackalloc byte[KeyboardPacket.PacketSize];
        var packet = new KeyboardPacket(keyCode, isDown, modifiers);
        int written = packet.WriteTo(buffer);
        if (written > 0)
        {
            TransmitPacket(buffer);
        }
    }

    /// <summary>
    /// Stages updated gamepad controller state for the next 1000Hz tick.
    /// </summary>
    public void IngestGamepadState(in ControllerStatePacket gamepadPacket)
    {
        _latestGamepadState = gamepadPacket;
        Volatile.Write(ref _hasPendingGamepadState, 1);
    }

    private void PollingLoop()
    {
        long nextTick = Stopwatch.GetTimestamp();
        long intervalTicks = _targetIntervalTicks;
        long lastMeasurementTick = nextTick;
        ulong lastSampleCount = 0;
        double accumulatedJitterTicks = 0;
        ulong jitterCount = 0;

        Span<byte> packetBuffer = stackalloc byte[32];

        while (!_cts.IsCancellationRequested)
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= nextTick)
            {
                // Measure jitter
                long jitterTicks = Math.Abs(now - nextTick);
                accumulatedJitterTicks += jitterTicks;
                jitterCount++;

                Interlocked.Increment(ref _samplesPolled);

                // 1. Drain and emit accumulated mouse motion
                int dx = Interlocked.Exchange(ref _accumulatedMouseDeltaX, 0);
                int dy = Interlocked.Exchange(ref _accumulatedMouseDeltaY, 0);
                if (dx != 0 || dy != 0)
                {
                    var mousePacket = new MouseMovePacket((short)Math.Clamp(dx, short.MinValue, short.MaxValue),
                                                         (short)Math.Clamp(dy, short.MinValue, short.MaxValue));
                    int written = mousePacket.WriteTo(packetBuffer);
                    if (written > 0)
                    {
                        TransmitPacket(packetBuffer[..written]);
                    }
                }

                // 2. Drain and emit pending gamepad state
                if (Interlocked.Exchange(ref _hasPendingGamepadState, 0) == 1)
                {
                    var padPacket = _latestGamepadState;
                    int written = padPacket.WriteTo(packetBuffer);
                    if (written > 0)
                    {
                        TransmitPacket(packetBuffer[..written]);
                    }
                }

                // Calculate next interval
                nextTick += intervalTicks;
                if (now > nextTick + intervalTicks * 5)
                {
                    // If fell significantly behind, resync clock
                    nextTick = now + intervalTicks;
                }

                // Periodic frequency and jitter recalculation every ~500ms
                if (now - lastMeasurementTick >= Stopwatch.Frequency / 2)
                {
                    double elapsedSec = (double)(now - lastMeasurementTick) / Stopwatch.Frequency;
                    ulong samplesDelta = _samplesPolled - lastSampleCount;
                    if (elapsedSec > 0)
                    {
                        Volatile.Write(ref _measuredFrequencyHz, samplesDelta / elapsedSec);
                    }

                    if (jitterCount > 0)
                    {
                        double avgJitterMicros = (accumulatedJitterTicks / jitterCount) * 1_000_000.0 / Stopwatch.Frequency;
                        Volatile.Write(ref _averageJitterMicros, avgJitterMicros);
                    }

                    lastMeasurementTick = now;
                    lastSampleCount = _samplesPolled;
                    accumulatedJitterTicks = 0;
                    jitterCount = 0;
                }
            }
            else
            {
                // High-resolution spin / yield to minimize latency
                long remainingTicks = nextTick - now;
                if (remainingTicks > (Stopwatch.Frequency / 1000) * 2)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.SpinWait(50);
                }
            }
        }
    }

    private void TransmitPacket(ReadOnlySpan<byte> packet)
    {
        Interlocked.Increment(ref _packetsEmitted);
        _packetTransmitter?.Invoke(packet);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;

            _cts.Cancel();
            if (_pollingThread.IsAlive)
            {
                _pollingThread.Join(500);
            }
            _cts.Dispose();
        }
    }
}
