using Moonshine.Protocol.RTP;

namespace Moonshine.Core.Congestion;

public sealed record CongestionMetrics(
    uint CurrentBitrateKbps,
    uint TargetBitrateKbps,
    double SmoothedLossRate,
    double MeasuredRttMs,
    ulong IdrRequestsSent,
    ulong CongestionEventsCount
);

/// <summary>
/// Predictive RTCP Bandwidth Adaptation & Congestion Controller.
/// Utilizes real-time RTCP feedback to adjust video streaming bitrate and request IDR keyframes.
/// </summary>
public sealed class CongestionController
{
    private readonly uint _minBitrateKbps;
    private readonly uint _maxBitrateKbps;
    private readonly Action<uint>? _onBitrateChanged;
    private readonly Action? _onIdrRequested;
    private readonly Lock _lock = new();

    private uint _currentBitrateKbps;
    private uint _targetBitrateKbps;
    private double _smoothedLossRate;
    private double _measuredRttMs;
    private ulong _idrRequestsSent;
    private ulong _congestionEventsCount;

    private bool _hasLossSample;
    private uint _lastPacketsLost;
    private uint _lastPacketsRecovered;

    public uint CurrentBitrateKbps => Volatile.Read(ref _currentBitrateKbps);
    public uint TargetBitrateKbps => Volatile.Read(ref _targetBitrateKbps);
    public double SmoothedLossRate => Volatile.Read(ref _smoothedLossRate);
    public double MeasuredRttMs => Volatile.Read(ref _measuredRttMs);

    public CongestionMetrics Metrics => new(
        Volatile.Read(ref _currentBitrateKbps),
        Volatile.Read(ref _targetBitrateKbps),
        Volatile.Read(ref _smoothedLossRate),
        Volatile.Read(ref _measuredRttMs),
        Volatile.Read(ref _idrRequestsSent),
        Volatile.Read(ref _congestionEventsCount)
    );

    public CongestionController(
        uint initialBitrateKbps = 50000,
        uint minBitrateKbps = 5000,
        uint maxBitrateKbps = 150000,
        Action<uint>? onBitrateChanged = null,
        Action? onIdrRequested = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minBitrateKbps);
        if (maxBitrateKbps < minBitrateKbps)
        {
            throw new ArgumentException("Max bitrate cannot be lower than min bitrate.", nameof(maxBitrateKbps));
        }

        _minBitrateKbps = minBitrateKbps;
        _maxBitrateKbps = maxBitrateKbps;
        _currentBitrateKbps = Math.Clamp(initialBitrateKbps, minBitrateKbps, maxBitrateKbps);
        _targetBitrateKbps = _currentBitrateKbps;
        _onBitrateChanged = onBitrateChanged;
        _onIdrRequested = onIdrRequested;
    }

    /// <summary>
    /// Processes incoming RTCP feedback report and adjusts bitrate accordingly.
    /// </summary>
    public void ProcessFeedback(in RtcpLossStatsPacket stats, double rttMs = 0)
    {
        lock (_lock)
        {
            if (rttMs > 0)
            {
                _measuredRttMs = _measuredRttMs <= 0.0 ? rttMs : (_measuredRttMs * 0.7) + (rttMs * 0.3);
            }

            double instantLossRate = stats.UnrecoverableLossRate;
            if (!_hasLossSample)
            {
                _smoothedLossRate = instantLossRate;
                _hasLossSample = true;
            }
            else
            {
                _smoothedLossRate = (_smoothedLossRate * 0.6) + (instantLossRate * 0.4);
            }

            uint oldTarget = _targetBitrateKbps;

            if (_smoothedLossRate >= 0.05)
            {
                // Severe packet loss (>= 5%): Multiplicative decrease by 30%
                _targetBitrateKbps = (uint)Math.Max(_minBitrateKbps, _currentBitrateKbps * 0.70);
                Interlocked.Increment(ref _congestionEventsCount);
            }
            else if (_smoothedLossRate >= 0.01)
            {
                // Moderate packet loss (1% - 5%): Gentle decrease by 10%
                _targetBitrateKbps = (uint)Math.Max(_minBitrateKbps, _currentBitrateKbps * 0.90);
                Interlocked.Increment(ref _congestionEventsCount);
            }
            else
            {
                // Clean network (< 1% loss): Additive increase (+2000 kbps)
                _targetBitrateKbps = Math.Min(_maxBitrateKbps, _currentBitrateKbps + 2000);
            }

            if (_targetBitrateKbps != oldTarget)
            {
                _currentBitrateKbps = _targetBitrateKbps;
                _onBitrateChanged?.Invoke(_targetBitrateKbps);
            }

            // Check if unrecoverable loss warrants an IDR frame request
            uint lostDelta = stats.PacketsLost >= _lastPacketsLost ? stats.PacketsLost - _lastPacketsLost : stats.PacketsLost;
            uint recoveredDelta = stats.PacketsRecovered >= _lastPacketsRecovered ? stats.PacketsRecovered - _lastPacketsRecovered : stats.PacketsRecovered;

            if (lostDelta > recoveredDelta && (lostDelta - recoveredDelta) >= 5)
            {
                RequestIdr();
            }

            _lastPacketsLost = stats.PacketsLost;
            _lastPacketsRecovered = stats.PacketsRecovered;
        }
    }

    /// <summary>
    /// Explicitly triggers an Instantaneous Decoder Refresh (IDR) frame request.
    /// </summary>
    public void RequestIdr()
    {
        Interlocked.Increment(ref _idrRequestsSent);
        _onIdrRequested?.Invoke();
    }
}
