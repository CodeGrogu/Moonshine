using System.Runtime.InteropServices;
using System.Text;
using Moonshine.Interop;

namespace Moonshine.Host.Audio;

/// <summary>
/// Represents the installation and endpoint readiness state of the virtual audio driver.
/// </summary>
public enum DriverInstallationState
{
    /// <summary>
    /// The driver package is not registered with the operating system.
    /// </summary>
    NotInstalled = 0,

    /// <summary>
    /// The driver package is installed in the device tree but endpoints are not yet active in CoreAudio.
    /// </summary>
    Installed = 1,

    /// <summary>
    /// The driver is installed and endpoints (render and capture) are active and selectable by audio clients.
    /// </summary>
    EndpointsActive = 2,

    /// <summary>
    /// An error occurred while inspecting driver installation state.
    /// </summary>
    Error = 3
}

/// <summary>
/// Managed coordinator and service for querying, verifying, and configuring
/// the dedicated Moonshine Windows Virtual Audio Driver (PortCls WaveRT miniport).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2216:DisposableTypesShouldDeclareFinalizer", Justification = "Finaliser deliberately omitted: managed disposal deterministically releases unmanaged driver service handles via C-ABI.")]
public sealed class VirtualAudioDriverService : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    /// <summary>
    /// Gets whether the native controller handle has been successfully initialised.
    /// </summary>
    public bool IsInitialized => _handle != IntPtr.Zero && !_disposed;

    /// <summary>
    /// Initialises a new instance of the <see cref="VirtualAudioDriverService"/> class.
    /// </summary>
    public VirtualAudioDriverService()
    {
        _handle = MoonshineNativeMethods.VirtualAudioDriverCreate();
    }

    /// <summary>
    /// Queries whether the Moonshine Virtual Audio Driver is installed and active on the host.
    /// </summary>
    public bool IsDriverInstalled()
    {
        ThrowIfDisposed();
        return MoonshineNativeMethods.VirtualAudioDriverIsInstalled(_handle) != 0;
    }

    /// <summary>
    /// Retrieves full driver status, including endpoint presence and supported channel topologies.
    /// </summary>
    public bool TryGetStatus(out MoonshineNativeMethods.VirtualAudioDriverStatusInterop status)
    {
        ThrowIfDisposed();
        int result = MoonshineNativeMethods.VirtualAudioDriverGetStatus(_handle, out status);
        return result != 0;
    }

    /// <summary>
    /// Validates whether a specific sample rate, channel count, and format are supported by the driver pin descriptors.
    /// </summary>
    public bool ValidateFormat(uint sampleRate, uint channels, uint formatType)
    {
        ThrowIfDisposed();
        return MoonshineNativeMethods.VirtualAudioDriverValidateFormat(_handle, sampleRate, channels, formatType) != 0;
    }

    /// <summary>
    /// Retrieves the friendly endpoint names for the Playback (Render) and Recording (Capture) devices.
    /// </summary>
    public unsafe bool TryGetEndpointNames(out string renderName, out string captureName)
    {
        ThrowIfDisposed();
        renderName = string.Empty;
        captureName = string.Empty;

        Span<byte> renderBuffer = stackalloc byte[128];
        Span<byte> captureBuffer = stackalloc byte[128];

        fixed (byte* pRender = renderBuffer)
        fixed (byte* pCapture = captureBuffer)
        {
            int result = MoonshineNativeMethods.VirtualAudioDriverGetEndpointNames(
                _handle,
                pRender,
                (uint)renderBuffer.Length,
                pCapture,
                (uint)captureBuffer.Length
            );

            if (result == 0)
            {
                return false;
            }

            renderName = Marshal.PtrToStringAnsi((IntPtr)pRender) ?? string.Empty;
            captureName = Marshal.PtrToStringAnsi((IntPtr)pCapture) ?? string.Empty;
            return true;
        }
    }

    /// <summary>
    /// Registers the calling audio thread with Windows Multimedia Class Scheduler Service (MMCSS) for Pro Audio priority.
    /// </summary>
    public bool TryEnableMmcss(out IntPtr taskHandle)
    {
        ThrowIfDisposed();
        int result = MoonshineNativeMethods.VirtualAudioDriverEnableMmcss(_handle, out taskHandle);
        return result != 0;
    }

    /// <summary>
    /// Disables MMCSS scheduling for the given task handle.
    /// </summary>
    public bool TryDisableMmcss(IntPtr taskHandle)
    {
        ThrowIfDisposed();
        int result = MoonshineNativeMethods.VirtualAudioDriverDisableMmcss(_handle, taskHandle);
        return result != 0;
    }

    /// <summary>
    /// Queries the detailed installation and endpoint readiness state of the virtual audio driver.
    /// </summary>
    public DriverInstallationState GetInstallationState()
    {
        ThrowIfDisposed();
        int state = MoonshineNativeMethods.VirtualAudioDriverGetInstallationState(_handle);
        return state switch
        {
            0 => DriverInstallationState.NotInstalled,
            1 => DriverInstallationState.Installed,
            2 => DriverInstallationState.EndpointsActive,
            _ => DriverInstallationState.Error
        };
    }

    /// <summary>
    /// Attempts to register and install the driver package via its INF configuration file.
    /// </summary>
    public bool TryInstallDriver(string infPath)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(infPath);
        return MoonshineNativeMethods.VirtualAudioDriverInstall(_handle, infPath) != 0;
    }

    /// <summary>
    /// Attempts to remove the virtual audio device from the operating system device tree.
    /// </summary>
    public bool TryRemoveDriver()
    {
        ThrowIfDisposed();
        return MoonshineNativeMethods.VirtualAudioDriverRemove(_handle) != 0;
    }

    /// <summary>
    /// Restarts the virtual audio device to reset its hardware state or reload parameters.
    /// </summary>
    public bool TryRestartDriver()
    {
        ThrowIfDisposed();
        return MoonshineNativeMethods.VirtualAudioDriverRestart(_handle) != 0;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed || _handle == IntPtr.Zero, this);
    }

    /// <summary>
    /// Releases all native unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (_handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.VirtualAudioDriverDestroy(_handle);
                _handle = IntPtr.Zero;
            }
            _disposed = true;
        }
    }
}
