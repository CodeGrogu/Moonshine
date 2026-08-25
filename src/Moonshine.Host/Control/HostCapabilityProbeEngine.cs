using System.Runtime.InteropServices;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
using Moonshine.Host.Encoding;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Host.Control;

using PhysicalAdapterInfo = DisplayAdapterInfo;

/// <summary>
/// Defines the readiness lifecycle status of an individual host subsystem component.
/// <para>
/// Hardware Encoder Operational Invariant:
/// No encoder may report <see cref="Operational"/> based solely on device discovery, API availability,
/// session creation, successful configuration, or frame submission. Operational requires a successfully
/// validated encoded bitstream produced from a real input frame by the selected vendor backend.
/// </para>
/// </summary>
public enum ComponentReadiness
{
    Unsupported = 0,
    Available = 1,
    Operational = 2,
    Faulted = 3
}

/// <summary>
/// Diagnostic record containing readiness states across all host streaming subsystems.
/// </summary>
public sealed record HostBackendReadiness(
    ComponentReadiness VideoEncoder,
    ComponentReadiness DesktopCapture,
    ComponentReadiness AudioLoopback,
    ComponentReadiness VirtualAudioDriver,
    ComponentReadiness MicrophoneBackchannel,
    string PrimaryGpuName,
    uint AttachedDisplayCount,
    bool IsHeadless);

/// <summary>
/// Hardware capability and system readiness probe engine for Moonshine host.
/// Discovers encoder support, display topologies, audio loopback, and virtual driver endpoints.
/// </summary>
public static class HostCapabilityProbeEngine
{
    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidIMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);

    /// <summary>
    /// Probes live hardware, video encoders, display topologies, and virtual audio subsystems to produce a host capabilities payload.
    /// </summary>
    /// <param name="topologyOverride">Optional display topology override for mock or virtualised scenarios.</param>
    /// <param name="adaptersOverride">Optional physical GPU adapter list override.</param>
    /// <returns>A populated and sanitised <see cref="MoonshineHostCapabilitiesResponsePayload"/>.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Native virtual audio driver query may fail if PortCls or C-ABI runtime is not present.")]
    public static MoonshineHostCapabilitiesResponsePayload ProbeLiveCapabilities(
        DisplayTopology? topologyOverride = null,
        IReadOnlyList<PhysicalAdapterInfo>? adaptersOverride = null)
    {
        // 1. Probe hardware video encoders
        bool nvencAv1 = NvencHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Av1);
        bool nvencHevc = NvencHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Hevc);
        bool nvencH264 = NvencHardwareEncoderPipeline.IsCodecSupported(VideoCodec.H264);

        bool amfAv1 = AmfHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Av1);
        bool amfHevc = AmfHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Hevc);
        bool amfH264 = AmfHardwareEncoderPipeline.IsCodecSupported(VideoCodec.H264);

        bool qsvAv1 = QsvHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Av1);
        bool qsvHevc = QsvHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Hevc);
        bool qsvH264 = QsvHardwareEncoderPipeline.IsCodecSupported(VideoCodec.H264);

        bool anyNvenc = nvencAv1 || nvencHevc || nvencH264;
        bool anyAmf = amfAv1 || amfHevc || amfH264;
        bool anyQsv = qsvAv1 || qsvHevc || qsvH264;

        bool supportsAv1 = nvencAv1 || amfAv1 || qsvAv1;
        bool supportsHevc = nvencHevc || amfHevc || qsvHevc;
        bool supportsH264 = nvencH264 || amfH264 || qsvH264;

        uint supportedVideoCodecs = 0;
        if (supportsAv1) supportedVideoCodecs |= (uint)MoonshineCapabilities.Av1;
        if (supportsHevc) supportedVideoCodecs |= (uint)MoonshineCapabilities.Hevc;
        if (supportsH264) supportedVideoCodecs |= (uint)MoonshineCapabilities.H264;

        // 2. Resolve maximum encode dimensions based on active encoder hardware and VRAM capacity
        uint maxEncodeWidth;
        uint maxEncodeHeight;

        if (anyNvenc)
        {
            maxEncodeWidth = 8192;
            maxEncodeHeight = 4320;
        }
        else if (anyAmf || anyQsv)
        {
            maxEncodeWidth = 7680;
            maxEncodeHeight = 4320;
        }
        else
        {
            maxEncodeWidth = 3840;
            maxEncodeHeight = 2160;
        }

        var adapters = adaptersOverride ?? topologyOverride?.Adapters ?? DisplayManager.GetPhysicalAdapters();
        DisplayTopology topology = topologyOverride ?? DisplayManager.GetDisplayTopology();

        DisplayAdapterInfo? primaryGpu = null;
        if (topology.PrimaryDisplay != null)
        {
            primaryGpu = FindAdapter(adapters, topology.PrimaryDisplay.AdapterIndex);
        }
        primaryGpu ??= FindPreferredAdapter(adapters);

        const ulong FourGigabytes = 4UL * 1024 * 1024 * 1024;
        if (primaryGpu == null || !primaryGpu.IsHardware || primaryGpu.DedicatedVideoMemoryBytes <= FourGigabytes)
        {
            if (maxEncodeWidth > 3840) maxEncodeWidth = 3840;
            if (maxEncodeHeight > 2160) maxEncodeHeight = 2160;
        }

        // 3. Inspect display topology and HDR/refresh rate support
        byte supportsHdr10 = 0;
        uint maxEncodeFps = 0;

        for (int i = 0; i < topology.Displays.Count; i++)
        {
            var display = topology.Displays[i];
            if (display.IsAttachedToDesktop)
            {
                if (display.IsHdr)
                {
                    supportsHdr10 = 1;
                }

                uint fps = (uint)Math.Max(1, (int)Math.Round(display.RefreshRateHz));
                if (fps > maxEncodeFps)
                {
                    maxEncodeFps = fps;
                }
            }
        }

        if (maxEncodeFps == 0)
        {
            maxEncodeFps = 60u;
        }

        // 4. Query virtual audio driver and backchannel readiness
        DriverInstallationState audioDriverState;
        try
        {
            using var driverService = new VirtualAudioDriverService();
            audioDriverState = driverService.GetInstallationState();
        }
        catch (Exception) // ALLOWED_EXCEPTION: Native virtual audio driver query may fail if PortCls or C-ABI runtime is not present.
        {
            audioDriverState = DriverInstallationState.Error;
        }

        byte supportsVirtualAudio = (byte)(audioDriverState == DriverInstallationState.EndpointsActive ? 1 : 0);
        byte supportsMicBackchannel = (byte)(audioDriverState == DriverInstallationState.EndpointsActive ? 1 : 0);

        return new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = supportedVideoCodecs,
            SupportedAudioCodecs = (uint)MoonshineAudioCodec.Opus,
            MaxEncodeWidth = maxEncodeWidth,
            MaxEncodeHeight = maxEncodeHeight,
            MaxEncodeFps = maxEncodeFps,
            SupportsHdr10 = supportsHdr10,
            SupportsVirtualAudio = supportsVirtualAudio,
            SupportsMicBackchannel = supportsMicBackchannel,
            Reserved = 0,
            MaxBitrateKbps = 150_000,
            Reserved2 = 0
        };
    }

    /// <summary>
    /// Checks whether any hardware video encoder (NVENC, AMF, QSV) is supported on the current host.
    /// </summary>
    public static bool IsAnyHardwareEncoderSupported()
    {
        bool nvencSupported = NvencHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Av1) ||
                              NvencHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Hevc) ||
                              NvencHardwareEncoderPipeline.IsCodecSupported(VideoCodec.H264);
        bool amfSupported = AmfHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Av1) ||
                            AmfHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Hevc) ||
                            AmfHardwareEncoderPipeline.IsCodecSupported(VideoCodec.H264);
        bool qsvSupported = QsvHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Av1) ||
                            QsvHardwareEncoderPipeline.IsCodecSupported(VideoCodec.Hevc) ||
                            QsvHardwareEncoderPipeline.IsCodecSupported(VideoCodec.H264);

        return nvencSupported || amfSupported || qsvSupported;
    }

    /// <summary>
    /// Probes comprehensive backend subsystem readiness states for diagnostic reporting.
    /// </summary>
    /// <param name="topologyOverride">Optional display topology override.</param>
    /// <param name="adaptersOverride">Optional physical GPU adapter list override.</param>
    /// <returns>A structured <see cref="HostBackendReadiness"/> status report.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Native virtual audio driver or CoreAudio query may fail if runtime components are not present.")]
    public static HostBackendReadiness ProbeBackendReadiness(
        DisplayTopology? topologyOverride = null,
        IReadOnlyList<PhysicalAdapterInfo>? adaptersOverride = null)
    {
        var adapters = adaptersOverride ?? topologyOverride?.Adapters ?? DisplayManager.GetPhysicalAdapters();
        DisplayTopology topology = topologyOverride ?? DisplayManager.GetDisplayTopology();

        // Video encoder readiness
        bool anyEncoderSupported = IsAnyHardwareEncoderSupported();
        ComponentReadiness videoEncoder = anyEncoderSupported
            ? ComponentReadiness.Available
            : ComponentReadiness.Unsupported;

        // Desktop capture readiness
        uint attachedDisplayCount = 0;
        for (int i = 0; i < topology.Displays.Count; i++)
        {
            if (topology.Displays[i].IsAttachedToDesktop)
            {
                attachedDisplayCount++;
            }
        }

        bool isHeadless = topology.IsHeadless || attachedDisplayCount == 0;
        ComponentReadiness desktopCapture = (!isHeadless && attachedDisplayCount > 0)
            ? ComponentReadiness.Available
            : ComponentReadiness.Unsupported;

        // Audio loopback readiness
        ComponentReadiness audioLoopback = HasActiveRenderEndpoint()
            ? ComponentReadiness.Available
            : ComponentReadiness.Unsupported;

        // Virtual audio driver readiness
        DriverInstallationState audioDriverState;
        try
        {
            using var driverService = new VirtualAudioDriverService();
            audioDriverState = driverService.GetInstallationState();
        }
        catch (Exception) // ALLOWED_EXCEPTION: Native virtual audio driver query may fail if PortCls or C-ABI runtime is not present.
        {
            audioDriverState = DriverInstallationState.Error;
        }

        ComponentReadiness virtualAudio = (audioDriverState == DriverInstallationState.EndpointsActive)
            ? ComponentReadiness.Available
            : (audioDriverState == DriverInstallationState.Error ? ComponentReadiness.Faulted : ComponentReadiness.Unsupported);

        ComponentReadiness micBackchannel = (audioDriverState == DriverInstallationState.EndpointsActive)
            ? ComponentReadiness.Available
            : (audioDriverState == DriverInstallationState.Error ? ComponentReadiness.Faulted : ComponentReadiness.Unsupported);

        // Primary GPU identification
        DisplayAdapterInfo? primaryGpu = null;
        if (topology.PrimaryDisplay != null)
        {
            primaryGpu = FindAdapter(adapters, topology.PrimaryDisplay.AdapterIndex);
        }
        primaryGpu ??= FindPreferredAdapter(adapters);
        string primaryGpuName = primaryGpu?.Description ?? string.Empty;

        return new HostBackendReadiness(
            VideoEncoder: videoEncoder,
            DesktopCapture: desktopCapture,
            AudioLoopback: audioLoopback,
            VirtualAudioDriver: virtualAudio,
            MicrophoneBackchannel: micBackchannel,
            PrimaryGpuName: primaryGpuName,
            AttachedDisplayCount: attachedDisplayCount,
            IsHeadless: isHeadless
        );
    }

    public static DisplayAdapterInfo? FindAdapter(IReadOnlyList<DisplayAdapterInfo> adapters, uint adapterIndex)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        for (int i = 0; i < adapters.Count; i++)
        {
            if (adapters[i].AdapterIndex == adapterIndex)
            {
                return adapters[i];
            }
        }
        return null;
    }

    public static DisplayAdapterInfo? FindPreferredAdapter(IReadOnlyList<DisplayAdapterInfo> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        for (int i = 0; i < adapters.Count; i++)
        {
            if (adapters[i].IsHardware)
            {
                return adapters[i];
            }
        }
        return adapters.Count > 0 ? adapters[0] : null;
    }

    /// <summary>
    /// Checks whether an active Windows CoreAudio MMDevice render endpoint is present on the host system.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "CoreAudio COM activation may fail on headless environments or systems without audio devices.")]
    public static bool HasActiveRenderEndpoint()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            const uint ClsctxAll = 23; // CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER
            int hr = CoCreateInstance(in ClsidMMDeviceEnumerator, IntPtr.Zero, ClsctxAll, in IidIMMDeviceEnumerator, out IntPtr pUnknown);
            if (hr != 0 || pUnknown == IntPtr.Zero)
            {
                return false;
            }

            IMMDeviceEnumerator? enumerator = null;
            try
            {
                enumerator = Marshal.GetObjectForIUnknown(pUnknown) as IMMDeviceEnumerator;
            }
            finally
            {
                Marshal.Release(pUnknown);
            }

            if (enumerator == null)
            {
                return false;
            }

            try
            {
                hr = enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.Active, out var collection);
                if (hr == 0 && collection != null)
                {
                    try
                    {
                        hr = collection.GetCount(out uint count);
                        if (hr == 0 && count > 0)
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(collection);
                    }
                }

                hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defaultEndpoint);
                if (hr == 0 && defaultEndpoint != null)
                {
                    Marshal.ReleaseComObject(defaultEndpoint);
                    return true;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }

            return false;
        }
        catch (Exception) // ALLOWED_EXCEPTION: CoreAudio COM activation may fail on headless environments.
        {
            return false;
        }
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(
            EDataFlow dataFlow,
            DeviceState stateMask,
            [Out] out IMMDeviceCollection? devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(
            EDataFlow dataFlow,
            ERole role,
            [Out] out IMMDevice? endpoint);

        [PreserveSig]
        int GetDevice(
            [MarshalAs(UnmanagedType.LPWStr)] string pwstrId,
            [Out] out IMMDevice? endpoint);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(
            IntPtr pClient);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(
            IntPtr pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount([Out] out uint count);

        [PreserveSig]
        int Item(uint index, [Out] out IMMDevice? device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            [In] ref Guid iid,
            uint dwClsCtx,
            IntPtr pActivationParams,
            [Out] [MarshalAs(UnmanagedType.IUnknown)] out object? ppInterface);

        [PreserveSig]
        int OpenPropertyStore(
            uint stgmAccess,
            [Out] out IntPtr ppProperties);

        [PreserveSig]
        int GetId([Out] [MarshalAs(UnmanagedType.LPWStr)] out string? ppstrId);

        [PreserveSig]
        int GetState([Out] out uint pdwState);
    }

    private enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [Flags]
    private enum DeviceState : uint
    {
        Active = 0x00000001,
        Disabled = 0x00000002,
        NotPresent = 0x00000004,
        Unplugged = 0x00000008,
        All = 0x0000000F
    }
}
