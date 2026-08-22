using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Moonshine.Core.Media;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Feedback;

namespace Moonshine.Core.Feedback;

public delegate void FeedbackPacketSink(ReadOnlySpan<byte> datagram);

/// <summary>
/// Client-side periodic statistics aggregator and feedback reporter.
/// Measures RTT, packet loss, RFC 3550 inter-arrival jitter, receive queue depth, and effective throughput.
/// Emits bounded periodic Moonshine-native FeedbackLossStats packets and immediate IdrRequest packets.
/// </summary>
public sealed class MoonshineFeedbackReporter : IDisposable
{
    public const int DefaultReportIntervalMs = 50; // 20 Hz periodic feedback cadence

    private readonly uint _streamId;
    private readonly ulong _sessionId;
    private readonly int _reportIntervalMs;
    private readonly MoonshineMediaReassemblyPipeline? _reassemblyPipeline;
    private readonly FeedbackPacketSink? _sink;
    private readonly Socket? _socket;
    private readonly IPEndPoint? _remoteFeedbackEndpoint;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task? _reportWorker;
    private readonly Lock _lock = new();

    private uint _sequenceNumber;
    private uint _packetsReceived;
    private uint _packetsLost;
    private uint _packetsRecoveredFec;
    private ulong _lastReceivedFrameIndex;
    private ulong _lastValidFrameIndex;
    private uint _roundTripTimeUs;
    private double _jitterUs;
    private long _lastPacketArrivalQpc;
    private ulong _lastPacketTimestampUs;
    private ulong _intervalBytesReceived;
    private long _lastIntervalQpc;
    private uint _effectiveThroughputKbps;
    private uint _receiveQueueDepth;
    private bool _disposed;

    public uint StreamId => _streamId;
    public ulong SessionId => _sessionId;
    public int ReportIntervalMs => _reportIntervalMs;
    public uint RoundTripTimeUs => Volatile.Read(ref _roundTripTimeUs);
    public uint JitterUs => (uint)Volatile.Read(ref _jitterUs);
    public uint EffectiveThroughputKbps => Volatile.Read(ref _effectiveThroughputKbps);
    public uint ReceiveQueueDepth => Volatile.Read(ref _receiveQueueDepth);

    public MoonshineFeedbackReporter(
        uint streamId,
        ulong sessionId,
        int reportIntervalMs = DefaultReportIntervalMs,
        MoonshineMediaReassemblyPipeline? reassemblyPipeline = null,
        FeedbackPacketSink? sink = null,
        Socket? socket = null,
        IPEndPoint? remoteFeedbackEndpoint = null)
    {
        _streamId = streamId;
        _sessionId = sessionId;
        _reportIntervalMs = Math.Clamp(reportIntervalMs, 10, 1000);
        _reassemblyPipeline = reassemblyPipeline;
        _sink = sink;
        _socket = socket;
        _remoteFeedbackEndpoint = remoteFeedbackEndpoint;
        _lastIntervalQpc = Stopwatch.GetTimestamp();

        if (_sink != null || _socket != null)
        {
            _reportWorker = Task.Run(ReportLoopAsync);
        }
    }

    /// <summary>
    /// Records reception of a video media packet to update jitter and throughput statistics with zero GC allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordPacketReceived(
        ulong frameIndex,
        uint packetBytes,
        ulong senderTimestampUs,
        bool isCompleteFrame = false)
    {
        long arrivalQpc = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            _packetsReceived++;
            _intervalBytesReceived += packetBytes;
            _lastReceivedFrameIndex = Math.Max(_lastReceivedFrameIndex, frameIndex);
            if (isCompleteFrame)
            {
                _lastValidFrameIndex = Math.Max(_lastValidFrameIndex, frameIndex);
            }

            // Calculate RFC 3550 Inter-arrival Jitter:
            // D(i, j) = (R_j - S_j) - (R_i - S_i)
            // J = J + (|D| - J) / 16
            if (_lastPacketArrivalQpc > 0 && _lastPacketTimestampUs > 0)
            {
                double arrivalUs = arrivalQpc * (1_000_000.0 / Stopwatch.Frequency);
                double prevArrivalUs = _lastPacketArrivalQpc * (1_000_000.0 / Stopwatch.Frequency);

                double transitDiff = (arrivalUs - senderTimestampUs) - (prevArrivalUs - _lastPacketTimestampUs);
                double absDiff = Math.Abs(transitDiff);
                _jitterUs += (absDiff - _jitterUs) / 16.0;
            }

            _lastPacketArrivalQpc = arrivalQpc;
            _lastPacketTimestampUs = senderTimestampUs;
        }
    }

    /// <summary>
    /// Records packet loss event from reassembly engine.
    /// </summary>
    public void RecordPacketLost(uint lostCount = 1)
    {
        lock (_lock)
        {
            _packetsLost += lostCount;
        }
    }

    /// <summary>
    /// Records FEC packet recovery event.
    /// </summary>
    public void RecordPacketRecoveredFec(uint recoveredCount = 1)
    {
        lock (_lock)
        {
            _packetsRecoveredFec += recoveredCount;
        }
    }

    /// <summary>
    /// Updates round-trip time measurement.
    /// </summary>
    public void UpdateRtt(uint rttUs)
    {
        Volatile.Write(ref _roundTripTimeUs, rttUs);
    }

    /// <summary>
    /// Updates receive queue depth (frames pending in jitter buffer).
    /// </summary>
    public void UpdateQueueDepth(uint queueDepth)
    {
        Volatile.Write(ref _receiveQueueDepth, queueDepth);
    }

    /// <summary>
    /// Creates and encodes the current FeedbackLossStats payload into the provided destination buffer.
    /// </summary>
    public bool TryBuildFeedbackPacket(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        MoonshineFeedbackLossStatsPayload payload;
        uint seq;

        lock (_lock)
        {
            long nowQpc = Stopwatch.GetTimestamp();
            double intervalSec = (nowQpc - _lastIntervalQpc) / (double)Stopwatch.Frequency;
            if (intervalSec > 0.001)
            {
                _effectiveThroughputKbps = (uint)((_intervalBytesReceived * 8.0) / (intervalSec * 1000.0));
                _intervalBytesReceived = 0;
                _lastIntervalQpc = nowQpc;
            }

            // Sync metrics from reassembly pipeline if attached
            if (_reassemblyPipeline != null)
            {
                MediaReassemblyMetrics pipelineMetrics = _reassemblyPipeline.Metrics;
                _packetsLost = (uint)pipelineMetrics.PacketsLost;
                _packetsRecoveredFec = (uint)pipelineMetrics.PacketsRecoveredFec;
            }

            payload = new MoonshineFeedbackLossStatsPayload
            {
                StreamId = _streamId,
                LastReceivedFrameIndex = _lastReceivedFrameIndex,
                PacketsReceived = _packetsReceived,
                PacketsLost = _packetsLost,
                PacketsRecoveredFec = _packetsRecoveredFec,
                RoundTripTimeUs = _roundTripTimeUs,
                JitterUs = (uint)_jitterUs,
                EstimatedBandwidthKbps = _effectiveThroughputKbps,
                ReceiveQueueDepth = _receiveQueueDepth
            };

            seq = _sequenceNumber++;
        }

        return MoonshineFeedbackCodec.TryWriteLossStats(
            in payload,
            destination,
            out bytesWritten,
            sessionId: _sessionId,
            sequenceNumber: seq);
    }

    /// <summary>
    /// Triggers an immediate IDR keyframe request packet to host.
    /// </summary>
    public bool SendIdrRequest(uint reasonCode = 1)
    {
        if (_disposed) return false;

        Span<byte> buffer = stackalloc byte[MoonshineFeedbackCodec.IdrRequestPacketSize];
        MoonshineIdrRequestPayload payload;
        uint seq;

        lock (_lock)
        {
            payload = new MoonshineIdrRequestPayload
            {
                StreamId = _streamId,
                LastValidFrameIndex = _lastValidFrameIndex,
                ReasonCode = reasonCode
            };
            seq = _sequenceNumber++;
        }

        if (!MoonshineFeedbackCodec.TryWriteIdrRequest(in payload, buffer, out int written, sessionId: _sessionId, sequenceNumber: seq))
        {
            return false;
        }

        ReadOnlySpan<byte> datagram = buffer[..written];
        _sink?.Invoke(datagram);

        if (_socket != null && _remoteFeedbackEndpoint != null)
        {
            try
            {
                _socket.SendTo(datagram, SocketFlags.None, _remoteFeedbackEndpoint);
            }
            // ALLOWED_EXCEPTION: Transient send failure on network drop.
            catch (SocketException)
            {
            }
        }

        return true;
    }

    private async Task ReportLoopAsync()
    {
        byte[] buffer = new byte[MoonshineFeedbackCodec.LossStatsPacketSize];

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_reportIntervalMs, _cts.Token).ConfigureAwait(false);

                if (TryBuildFeedbackPacket(buffer, out int written))
                {
                    ReadOnlySpan<byte> datagram = buffer.AsSpan(0, written);
                    _sink?.Invoke(datagram);

                    if (_socket != null && _remoteFeedbackEndpoint != null)
                    {
                        _socket.SendTo(datagram, SocketFlags.None, _remoteFeedbackEndpoint);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Periodic feedback background worker resilience.
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                if (_disposed) break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        try
        {
            _reportWorker?.Wait(TimeSpan.FromMilliseconds(200));
        }
        // ALLOWED_EXCEPTION: Clean background task shutdown timeout.
        catch (Exception)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
