using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Moonshine.Host.Encoding;

/// <summary>
/// High-precision, zero-allocation media timestamp clock utilizing 64-bit integer arithmetic.
/// Guarantees microsecond-accurate, non-drifting presentation timestamps across all hardware encoding pipelines.
/// </summary>
public static class MoonshineMediaClock
{
    private static readonly long Frequency = Stopwatch.Frequency;

    /// <summary>
    /// Gets the current monotonic timestamp in microseconds using pure integer arithmetic.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong GetCurrentTimestampMicroseconds()
    {
        long ticks = Stopwatch.GetTimestamp();
        return TicksToMicroseconds(ticks);
    }

    /// <summary>
    /// Converts high-precision Stopwatch QPC ticks to integer microseconds without floating-point conversions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong TicksToMicroseconds(long ticks)
    {
        if (ticks <= 0) return 0;
        ulong uTicks = (ulong)ticks;
        ulong freq = (ulong)Frequency;
        return (uTicks / freq) * 1_000_000UL + ((uTicks % freq) * 1_000_000UL) / freq;
    }

    /// <summary>
    /// Calculates elapsed microseconds between two QPC tick values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong CalculateElapsedMicroseconds(long startTicks, long endTicks)
    {
        if (endTicks <= startTicks) return 0;
        return TicksToMicroseconds(endTicks - startTicks);
    }
}
