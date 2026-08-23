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
    CaptureSourceFallbackPolicy FallbackPolicy = CaptureSourceFallbackPolicy.FallbackToPrimary
);

public sealed record CaptureSourceSelectionResult(
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
/// </summary>
public static class CaptureSourceSelector
{
    /// <summary>
    /// Resolves the optimal capture source from the current display topology according to the specified criteria.
    /// </summary>
    public static CaptureSourceSelectionResult SelectSource(
        DisplayTopology topology,
        CaptureSourceSelectionCriteria? criteria = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        criteria ??= new CaptureSourceSelectionCriteria();

        // 1. Headless validation: Never invent or simulate displays
        if (topology.IsHeadless || topology.Displays.Count == 0)
        {
            return CaptureSourceSelectionResult.Headless(
                "Host environment has no physical displays attached to the Windows desktop. Headless capture requires hardware EDID emulator plug or IddCx indirect driver.");
        }

        var attachedDisplays = new List<DisplayOutputInfo>();
        for (int i = 0; i < topology.Displays.Count; i++)
        {
            if (topology.Displays[i].IsAttachedToDesktop)
            {
                attachedDisplays.Add(topology.Displays[i]);
            }
        }

        if (attachedDisplays.Count == 0)
        {
            return CaptureSourceSelectionResult.Headless(
                "All enumerated display outputs are detached from the Windows desktop.");
        }

        DisplayOutputInfo? candidate = null;

        switch (criteria.Policy)
        {
            case CaptureSelectionPolicy.PrimaryDisplay:
                candidate = topology.PrimaryDisplay ?? attachedDisplays[0];
                break;

            case CaptureSelectionPolicy.SpecificDisplayIndex:
                for (int i = 0; i < attachedDisplays.Count; i++)
                {
                    if (attachedDisplays[i].AdapterIndex == criteria.PreferredAdapterIndex &&
                        attachedDisplays[i].DisplayIndex == criteria.PreferredDisplayIndex)
                    {
                        candidate = attachedDisplays[i];
                        break;
                    }
                }
                break;

            case CaptureSelectionPolicy.SpecificMonitorHandle:
                if (criteria.PreferredMonitorHandle != IntPtr.Zero)
                {
                    for (int i = 0; i < attachedDisplays.Count; i++)
                    {
                        if (attachedDisplays[i].MonitorHandle == criteria.PreferredMonitorHandle)
                        {
                            candidate = attachedDisplays[i];
                            break;
                        }
                    }
                }
                break;

            case CaptureSelectionPolicy.SpecificDeviceName:
                if (!string.IsNullOrWhiteSpace(criteria.PreferredDeviceName))
                {
                    for (int i = 0; i < attachedDisplays.Count; i++)
                    {
                        if (string.Equals(attachedDisplays[i].DeviceName, criteria.PreferredDeviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            candidate = attachedDisplays[i];
                            break;
                        }
                    }
                }
                break;

            case CaptureSelectionPolicy.MatchResolution:
                double bestScore = double.MaxValue;
                for (int i = 0; i < attachedDisplays.Count; i++)
                {
                    var d = attachedDisplays[i];
                    double resScore = Math.Abs((int)d.Width - (int)criteria.TargetWidth) +
                                      Math.Abs((int)d.Height - (int)criteria.TargetHeight);
                    double fpsScore = Math.Abs(d.RefreshRateHz - criteria.TargetFps) * 5.0;
                    double hdrPenalty = (criteria.RequireHdr && !d.IsHdr) ? 10000.0 : 0.0;
                    double primaryBonus = d.IsPrimary ? -10.0 : 0.0;

                    double totalScore = resScore + fpsScore + hdrPenalty + primaryBonus;
                    if (totalScore < bestScore)
                    {
                        bestScore = totalScore;
                        candidate = d;
                    }
                }
                break;
        }

        // Direct match found
        if (candidate != null)
        {
            return CaptureSourceSelectionResult.Success(ToDescriptor(candidate), isFallback: false);
        }

        // Fallback resolution
        if (criteria.FallbackPolicy == CaptureSourceFallbackPolicy.FallbackToPrimary)
        {
            var fallback = topology.PrimaryDisplay ?? attachedDisplays[0];
            return CaptureSourceSelectionResult.Success(ToDescriptor(fallback), isFallback: true);
        }

        return CaptureSourceSelectionResult.Failure(
            $"No attached display output matching criteria (Policy: {criteria.Policy}, Adapter: {criteria.PreferredAdapterIndex}, Output: {criteria.PreferredDisplayIndex}, Device: {criteria.PreferredDeviceName ?? "none"}) was found.");
    }

    private static CaptureSourceDescriptor ToDescriptor(DisplayOutputInfo display)
    {
        uint format = display.IsHdr ? 24u /* DXGI_FORMAT_R10G10B10A2_UNORM */ : 87u /* DXGI_FORMAT_B8G8R8A8_UNORM */;
        return new CaptureSourceDescriptor(
            AdapterIndex: display.AdapterIndex,
            OutputIndex: display.DisplayIndex,
            MonitorHandle: display.MonitorHandle,
            DeviceName: display.DeviceName,
            FriendlyName: display.FriendlyName,
            Width: display.Width,
            Height: display.Height,
            RefreshRateHz: display.RefreshRateHz,
            Format: format,
            IsHdr: display.IsHdr,
            IsPrimary: display.IsPrimary,
            DesktopBounds: display.Bounds
        );
    }
}
