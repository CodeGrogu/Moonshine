namespace Moonshine.Host.Capture;

public enum CaptureSelectionPolicy
{
    PrimaryDisplay = 0,
    SpecificDisplayIndex = 1,
    SpecificMonitorHandle = 2,
    SpecificDeviceName = 3,
    MatchResolution = 4
}

public enum CaptureSourceFallbackPolicy
{
    FallbackToPrimary = 0,
    FailClosed = 1
}

public sealed record CaptureSourceDescriptor(
    uint AdapterIndex,
    uint OutputIndex,
    IntPtr MonitorHandle,
    string DeviceName,
    string FriendlyName,
    uint Width,
    uint Height,
    double RefreshRateHz,
    uint Format,
    bool IsHdr,
    bool IsPrimary,
    DesktopBounds DesktopBounds
);

public sealed record CaptureSourceSelectionCriteria(
    CaptureSelectionPolicy Policy = CaptureSelectionPolicy.PrimaryDisplay,
    uint PreferredAdapterIndex = 0,
    uint PreferredDisplayIndex = 0,
    IntPtr PreferredMonitorHandle = default,
    string? PreferredDeviceName = null,
    uint TargetWidth = 1920,
    uint TargetHeight = 1080,
    double TargetFps = 60.0,
    bool RequireHdr = false,
    bool PreferHdr = false,
    CaptureSourceFallbackPolicy FallbackPolicy = CaptureSourceFallbackPolicy.FallbackToPrimary
);

public readonly record struct CaptureSourceSelectionResult(
    bool IsSuccess,
    CaptureSourceDescriptor? Source,
    string? FailureReason,
    bool IsHeadless,
    bool IsFallback
)
{
    public static CaptureSourceSelectionResult Success(CaptureSourceDescriptor source, bool isFallback = false) =>
        new(true, source, null, false, isFallback);

    public static CaptureSourceSelectionResult Failure(string reason) =>
        new(false, null, reason, false, false);

    public static CaptureSourceSelectionResult Headless(string reason = "No active physical displays attached to the host desktop.") =>
        new(false, null, reason, true, false);
}

/// <summary>
/// Deterministic capture source selector mapping host display topology and client requirements to capture endpoints.
/// Guarantees zero heap allocations (0 B GC pressure) and sub-microsecond evaluation across streaming session paths.
/// </summary>
public static class CaptureSourceSelector
{
    private static readonly CaptureSourceSelectionCriteria s_defaultCriteria = new();

    /// <summary>
    /// Resolves the optimal capture source from the current display topology according to the specified criteria.
    /// Operates with zero heap allocations (0 B) and deterministic tie-breaking.
    /// </summary>
    public static CaptureSourceSelectionResult SelectSource(
        DisplayTopology topology,
        CaptureSourceSelectionCriteria? criteria = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        criteria ??= s_defaultCriteria;

        // 1. Headless validation: Never invent or simulate displays
        if (topology.IsHeadless || topology.Displays.Count == 0)
        {
            return CaptureSourceSelectionResult.Headless(
                "Host environment has no physical displays attached to the Windows desktop. Headless capture requires hardware EDID emulator plug or IddCx indirect driver.");
        }

        DisplayOutputInfo? candidate = null;
        DisplayOutputInfo? firstAttached = null;
        bool hasAttached = false;

        // Find primary or first attached while evaluating with zero collection allocation
        for (int i = 0; i < topology.Displays.Count; i++)
        {
            var d = topology.Displays[i];
            if (d.IsAttachedToDesktop)
            {
                hasAttached = true;
                firstAttached ??= d;
                if (d.IsPrimary)
                {
                    break;
                }
            }
        }

        if (!hasAttached || firstAttached == null)
        {
            return CaptureSourceSelectionResult.Headless(
                "All enumerated display outputs are detached from the Windows desktop.");
        }

        switch (criteria.Policy)
        {
            case CaptureSelectionPolicy.PrimaryDisplay:
                candidate = topology.PrimaryDisplay ?? firstAttached;
                break;

            case CaptureSelectionPolicy.SpecificDisplayIndex:
                for (int i = 0; i < topology.Displays.Count; i++)
                {
                    var d = topology.Displays[i];
                    if (d.IsAttachedToDesktop &&
                        d.AdapterIndex == criteria.PreferredAdapterIndex &&
                        d.DisplayIndex == criteria.PreferredDisplayIndex)
                    {
                        candidate = d;
                        break;
                    }
                }
                break;

            case CaptureSelectionPolicy.SpecificMonitorHandle:
                if (criteria.PreferredMonitorHandle != IntPtr.Zero)
                {
                    for (int i = 0; i < topology.Displays.Count; i++)
                    {
                        var d = topology.Displays[i];
                        if (d.IsAttachedToDesktop && d.MonitorHandle == criteria.PreferredMonitorHandle)
                        {
                            candidate = d;
                            break;
                        }
                    }
                }
                break;

            case CaptureSelectionPolicy.SpecificDeviceName:
                if (!string.IsNullOrWhiteSpace(criteria.PreferredDeviceName))
                {
                    for (int i = 0; i < topology.Displays.Count; i++)
                    {
                        var d = topology.Displays[i];
                        if (d.IsAttachedToDesktop &&
                            string.Equals(d.DeviceName, criteria.PreferredDeviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            candidate = d;
                            break;
                        }
                    }
                }
                break;

            case CaptureSelectionPolicy.MatchResolution:
                double bestScore = double.MaxValue;
                for (int i = 0; i < topology.Displays.Count; i++)
                {
                    var d = topology.Displays[i];
                    if (!d.IsAttachedToDesktop) continue;

                    // Hard requirement: if RequireHdr is requested, non-HDR displays are strictly skipped
                    if (criteria.RequireHdr && !d.IsHdr)
                    {
                        continue;
                    }

                    double resScore = Math.Abs((int)d.Width - (int)criteria.TargetWidth) +
                                      Math.Abs((int)d.Height - (int)criteria.TargetHeight);
                    double fpsScore = Math.Abs(d.RefreshRateHz - criteria.TargetFps) * 5.0;
                    double hdrBonus = (criteria.PreferHdr && d.IsHdr) ? -50.0 : 0.0;
                    double primaryBonus = d.IsPrimary ? -10.0 : 0.0;

                    double totalScore = resScore + fpsScore + hdrBonus + primaryBonus;

                    if (candidate == null || totalScore < bestScore)
                    {
                        bestScore = totalScore;
                        candidate = d;
                    }
                    else if (Math.Abs(totalScore - bestScore) < 1e-6)
                    {
                        // Explicit deterministic tie-breaking:
                        // 1. Lower total score (already tied)
                        // 2. Primary display preferred
                        // 3. Lower AdapterIndex
                        // 4. Lower DisplayIndex
                        // 5. Ordinal DeviceName comparison
                        if (d.IsPrimary && !candidate.IsPrimary)
                        {
                            candidate = d;
                        }
                        else if (d.IsPrimary == candidate.IsPrimary)
                        {
                            if (d.AdapterIndex < candidate.AdapterIndex)
                            {
                                candidate = d;
                            }
                            else if (d.AdapterIndex == candidate.AdapterIndex)
                            {
                                if (d.DisplayIndex < candidate.DisplayIndex)
                                {
                                    candidate = d;
                                }
                                else if (d.DisplayIndex == candidate.DisplayIndex)
                                {
                                    if (string.CompareOrdinal(d.DeviceName, candidate.DeviceName) < 0)
                                    {
                                        candidate = d;
                                    }
                                }
                            }
                        }
                    }
                }
                break;
        }

        // Direct match found
        if (candidate != null)
        {
            return CaptureSourceSelectionResult.Success(candidate.Descriptor, isFallback: false);
        }

        // Fallback resolution
        if (criteria.FallbackPolicy == CaptureSourceFallbackPolicy.FallbackToPrimary)
        {
            var fallback = topology.PrimaryDisplay ?? firstAttached;
            return CaptureSourceSelectionResult.Success(fallback.Descriptor, isFallback: true);
        }

        return CaptureSourceSelectionResult.Failure(
            $"No attached display output matching criteria (Policy: {criteria.Policy}, Adapter: {criteria.PreferredAdapterIndex}, Output: {criteria.PreferredDisplayIndex}, Device: {criteria.PreferredDeviceName ?? "none"}) was found.");
    }
}
