using System.Diagnostics;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Core.Congestion;

public sealed record CongestionMetrics(
    uint CurrentBitrateKbps,
    uint TargetBitrateKbps,
    double SmoothedLossRate,
    double MeasuredRttMs,
    double SmoothedJitterUs,
    uint ClientQueueDepth,
    uint EffectiveThroughputKbps,
    uint PacingAdjustmentUs,
    ulong IdrRequestsSent,
    ulong CongestionEventsCount
);

/// <summary>
/// Predictive Moonshine & RTCP Bandwidth Adaptation and Congestion Controller.
/// Adjusts video streaming bitrate, frame pacing, and requests IDR keyframes based on real-time network feedback.
/// Implements AIMD with hysteresis, deadbands, and queue depth backpressure to prevent oscillation and bufferbloat.
/// </summary>
public sealed class CongestionController
{
    public const uint DefaultMinBitrateKbps = 5000;
    public const uint DefaultMaxBitrateKbps = 150000;
    public const uint DefaultInitialBitrateKbps = 50000;
    public const int DefaultHysteresisHoldMs = 500;
    public const double DefaultLossDeadband = 0.005; // 0.5% loss deadband
    public const double DefaultSevereLossThreshold = 0.05; // 5% severe loss
    public const double DefaultModerateLossThreshold = 0.01; // 1% moderate loss
    public const uint DefaultMaxQueueDepthThreshold = 8; // 8 frames queued

    private readonly uint _minBitrateKbps;
    private uint _maxBitrateKbps;
    private readonly long _hysteresisHoldTicks;
    private readonly Action<uint>? _onBitrateChanged;
    private readonly Action<uint>? _onPacingChanged;
    private readonly Action? _onIdrRequested;
    private readonly Lock _lock = new();

    private uint _currentBitrateKbps;
    private uint _targetBitrateKbps;
    private double _smoothedLossRate;
    private double _measuredRttMs;
    private double _smoothedJitterUs;
    private uint _clientQueueDepth;
    private uint _effectiveThroughputKbps;
    private uint _pacingAdjustmentUs;
    private ulong _idrRequestsSent;
    private ulong _congestionEventsCount;

    private bool _hasLossSample;
    private uint _lastStreamId;
    private ulong _lastFrameIndex;
    private uint _lastPacketsReceived;
    private uint _lastPacketsLost;
    private uint _lastPacketsRecovered;
    private long _lastBitrateIncreaseTimestamp;

    public uint MinBitrateKbps => _minBitrateKbps;
    public uint MaxBitrateKbps => _maxBitrateKbps;
    public uint CurrentBitrateKbps => Volatile.Read(ref _currentBitrateKbps);
    public uint TargetBitrateKbps => Volatile.Read(ref _targetBitrateKbps);
    public double SmoothedLossRate => Volatile.Read(ref _smoothedLossRate);
    public double MeasuredRttMs => Volatile.Read(ref _measuredRttMs);
    public double SmoothedJitterUs => Volatile.Read(ref _smoothedJitterUs);
    public uint ClientQueueDepth => Volatile.Read(ref _clientQueueDepth);
    public uint EffectiveThroughputKbps => Volatile.Read(ref _effectiveThroughputKbps);
    public uint PacingAdjustmentUs => Volatile.Read(ref _pacingAdjustmentUs);
    public ulong IdrRequestsSent => Volatile.Read(ref _idrRequestsSent);
    public ulong CongestionEventsCount => Volatile.Read(ref _congestionEventsCount);

    public CongestionMetrics Metrics => new(
        Volatile.Read(ref _currentBitrateKbps),
        Volatile.Read(ref _targetBitrateKbps),
        Volatile.Read(ref _smoothedLossRate),
        Volatile.Read(ref _measuredRttMs),
        Volatile.Read(ref _smoothedJitterUs),
        Volatile.Read(ref _clientQueueDepth),
        Volatile.Read(ref _effectiveThroughputKbps),
        Volatile.Read(ref _pacingAdjustmentUs),
        Volatile.Read(ref _idrRequestsSent),
        Volatile.Read(ref _congestionEventsCount)
    );

    public CongestionController(
        uint initialBitrateKbps = DefaultInitialBitrateKbps,
        uint minBitrateKbps = DefaultMinBitrateKbps,
        uint maxBitrateKbps = DefaultMaxBitrateKbps,
        int hysteresisHoldMs = DefaultHysteresisHoldMs,
        Action<uint>? onBitrateChanged = null,
        Action<uint>? onPacingChanged = null,
        Action? onIdrRequested = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minBitrateKbps);
        if (maxBitrateKbps < minBitrateKbps)
        {
            throw new ArgumentException("Max bitrate cannot be lower than min bitrate.", nameof(maxBitrateKbps));
        }

        _minBitrateKbps = minBitrateKbps;
        _maxBitrateKbps = maxBitrateKbps;
        _hysteresisHoldTicks = (long)(Math.Max(0, hysteresisHoldMs) * (Stopwatch.Frequency / 1000.0));
        _currentBitrateKbps = Math.Clamp(initialBitrateKbps, minBitrateKbps, maxBitrateKbps);
        _targetBitrateKbps = _currentBitrateKbps;
        _onBitrateChanged = onBitrateChanged;
        _onPacingChanged = onPacingChanged;
        _onIdrRequested = onIdrRequested;
        _lastBitrateIncreaseTimestamp = 0;
    }

    /// <summary>
    /// Dynamically reconfigures the target bitrate and optionally updates the maximum bitrate limit.
    /// </summary>
    public void ReconfigureBitrate(uint newBitrateKbps, uint newMaxBitrateKbps = 0)
    {
        lock (_lock)
        {
            if (newMaxBitrateKbps >= _minBitrateKbps)
            {
                _maxBitrateKbps = newMaxBitrateKbps;
            }
            _currentBitrateKbps = Math.Clamp(newBitrateKbps, _minBitrateKbps, _maxBitrateKbps);
            _targetBitrateKbps = _currentBitrateKbps;
            _lastBitrateIncreaseTimestamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// Processes incoming Moonshine-native feedback report with loss, RTT, jitter, and queue depth.
    /// Safely handles stream switches, counter rollovers, and out-of-order / stale feedback datagrams.
    /// </summary>
    public void ProcessFeedback(in MoonshineFeedbackLossStatsPayload stats)
    {
        lock (_lock)
        {
            // 1. Guard against stream ID changes / session resets
            if (_hasLossSample && stats.StreamId != 0 && _lastStreamId != 0 && stats.StreamId != _lastStreamId)
            {
                _hasLossSample = false;
                _lastStreamId = stats.StreamId;
                _lastFrameIndex = stats.LastReceivedFrameIndex;
                _lastPacketsReceived = stats.PacketsReceived;
                _lastPacketsLost = stats.PacketsLost;
                _lastPacketsRecovered = stats.PacketsRecoveredFec;
            }

            // 2. Filter out-of-order or stale feedback arriving after newer feedback
            if (_hasLossSample && stats.StreamId == _lastStreamId)
            {
                if (stats.LastReceivedFrameIndex > 0 && _lastFrameIndex > 0)
                {
                    if (stats.LastReceivedFrameIndex < _lastFrameIndex)
                    {
                        // Discard stale datagram delayed in transit to prevent polluting moving averages
                        return;
                    }

                    if (stats.LastReceivedFrameIndex == _lastFrameIndex && stats.PacketsReceived < _lastPacketsReceived)
                    {
                        // Duplicate or stale sub-frame packet sample
                        return;
                    }
                }
            }

            // 3. Calculate safe monotonic deltas across intervals
            uint lostDelta = stats.PacketsLost >= _lastPacketsLost
                ? stats.PacketsLost - _lastPacketsLost
                : stats.PacketsLost;

            uint recoveredDelta = stats.PacketsRecoveredFec >= _lastPacketsRecovered
                ? stats.PacketsRecoveredFec - _lastPacketsRecovered
                : stats.PacketsRecoveredFec;

            uint receivedDelta = stats.PacketsReceived >= _lastPacketsReceived
                ? stats.PacketsReceived - _lastPacketsReceived
                : stats.PacketsReceived;

            double rttMs = stats.RoundTripTimeUs / 1000.0;
            double instantLossRate = 0.0;
            uint intervalExpected = receivedDelta + lostDelta;
            if (intervalExpected > 0)
            {
                uint unrecoverableDelta = lostDelta >= recoveredDelta
                    ? lostDelta - recoveredDelta
                    : 0;
                instantLossRate = (double)unrecoverableDelta / intervalExpected;
            }
            else
            {
                uint totalExpected = stats.PacketsReceived + stats.PacketsLost;
                if (totalExpected > 0)
                {
                    uint unrecoverable = stats.PacketsLost >= stats.PacketsRecoveredFec
                        ? stats.PacketsLost - stats.PacketsRecoveredFec
                        : 0;
                    instantLossRate = (double)unrecoverable / totalExpected;
                }
            }

            ProcessFeedbackInternalNoLock(
                instantLossRate,
                rttMs,
                stats.JitterUs,
                stats.EstimatedBandwidthKbps,
                stats.ReceiveQueueDepth,
                lostDelta,
                recoveredDelta);

            _lastStreamId = stats.StreamId;
            _lastFrameIndex = stats.LastReceivedFrameIndex;
            _lastPacketsReceived = stats.PacketsReceived;
            _lastPacketsLost = stats.PacketsLost;
            _lastPacketsRecovered = stats.PacketsRecoveredFec;
        }
    }

    /// <summary>
    /// Handles explicit Moonshine-native IDR keyframe requests from client.
    /// </summary>
    public void ProcessIdrRequest(in MoonshineIdrRequestPayload request)
    {
        RequestIdr();
    }

    private void ProcessFeedbackInternalNoLock(
        double instantLossRate,
        double rttMs,
        uint jitterUs,
        uint estimatedBwKbps,
        uint queueDepth,
        uint lostDelta,
        uint recoveredDelta)
    {
        long now = Stopwatch.GetTimestamp();

            // 1. Smooth RTT (EMA with 0.7 historical / 0.3 new)
            if (rttMs > 0)
            {
                _measuredRttMs = _measuredRttMs <= 0.0 ? rttMs : (_measuredRttMs * 0.7) + (rttMs * 0.3);
            }

            // 2. Smooth Jitter (EMA with 0.8 historical / 0.2 new)
            if (jitterUs > 0)
            {
                _smoothedJitterUs = _smoothedJitterUs <= 0.0 ? jitterUs : (_smoothedJitterUs * 0.8) + (jitterUs * 0.2);
            }

            // 3. Smooth Loss Rate (EMA with 0.6 historical / 0.4 new)
            if (!_hasLossSample)
            {
                _smoothedLossRate = instantLossRate;
                _hasLossSample = true;
            }
            else
            {
                _smoothedLossRate = (_smoothedLossRate * 0.6) + (instantLossRate * 0.4);
            }

            _clientQueueDepth = queueDepth;
            if (estimatedBwKbps > 0)
            {
                _effectiveThroughputKbps = estimatedBwKbps;
            }

            uint oldTarget = _targetBitrateKbps;

            // 4. Determine Bitrate Adaptation Response
            if (_smoothedLossRate >= DefaultSevereLossThreshold)
            {
                // Severe packet loss (>= 5%): Multiplicative decrease by 30%
                _targetBitrateKbps = (uint)Math.Max(_minBitrateKbps, _currentBitrateKbps * 0.70);
                Interlocked.Increment(ref _congestionEventsCount);
                _lastBitrateIncreaseTimestamp = now;
            }
            else if (_smoothedLossRate >= DefaultModerateLossThreshold || queueDepth > DefaultMaxQueueDepthThreshold)
            {
                // Moderate packet loss (1% - 5%) or Client Queue Backpressure: Gentle decrease by 10-15%
                double factor = queueDepth > DefaultMaxQueueDepthThreshold ? 0.85 : 0.90;
                _targetBitrateKbps = (uint)Math.Max(_minBitrateKbps, _currentBitrateKbps * factor);
                Interlocked.Increment(ref _congestionEventsCount);
                _lastBitrateIncreaseTimestamp = now;
            }
            else if (_smoothedLossRate <= DefaultLossDeadband && queueDepth <= 2)
            {
                // Clean network (< 0.5% loss) & low queue depth: Additive increase subject to hysteresis
                if ((now - _lastBitrateIncreaseTimestamp) >= _hysteresisHoldTicks)
                {
                    uint step = _currentBitrateKbps < 30000 ? 2000u : 1000u;
                    _targetBitrateKbps = Math.Min(_maxBitrateKbps, _currentBitrateKbps + step);
                    _lastBitrateIncreaseTimestamp = now;
                }
            }

            // 5. Adapt media pacing based on queue depth and jitter
            uint oldPacing = _pacingAdjustmentUs;
            if (queueDepth > 4)
            {
                _pacingAdjustmentUs = Math.Min(5000u, queueDepth * 250u);
            }
            else if (jitterUs > 5000)
            {
                _pacingAdjustmentUs = Math.Min(2000u, jitterUs / 10u);
            }
            else
            {
                _pacingAdjustmentUs = 0;
            }

            if (_pacingAdjustmentUs != oldPacing)
            {
                _onPacingChanged?.Invoke(_pacingAdjustmentUs);
            }

            // 6. Notify bitrate change if target updated
            if (_targetBitrateKbps != oldTarget)
            {
                _currentBitrateKbps = _targetBitrateKbps;
                _onBitrateChanged?.Invoke(_targetBitrateKbps);
            }

            // 7. Check if unrecoverable loss warrants an IDR frame request
            if (lostDelta > recoveredDelta && (lostDelta - recoveredDelta) >= 5)
            {
                RequestIdr();
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

