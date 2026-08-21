namespace Moonshine.Core.Transport;

public enum TransportState
{
    Uninitialised = 0,
    Connecting = 1,
    Connected = 2,
    Degraded = 3,
    Faulted = 4,
    Disconnected = 5
}

public enum QueueDropPolicy
{
    DropOldest = 0,
    DropNewest = 1,
    RejectWithBackpressure = 2
}

public readonly record struct TransportMetrics(
    ulong BytesSent,
    ulong BytesReceived,
    ulong PacketsSent,
    ulong PacketsReceived,
    ulong PacketsDropped,
    ulong SocketFaults,
    int CurrentQueueDepth,
    int PeakQueueDepth,
    double AverageLatencyUs);
