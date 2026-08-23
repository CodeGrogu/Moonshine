using Moonshine.Interop;

namespace Moonshine.Host.Color;

/// <summary>
/// Direct3D 11/12 GPU Color Space Converter.
/// Provides real-time RGB10A2 / BGRA8 conversion to P010 / NV12 surfaces for hardware encoders.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2216:DisposableTypesShouldDeclareFinalizer", Justification = "Finaliser deliberately omitted: managed disposal deterministically releases unmanaged color converter resources via C-ABI.")]
public sealed class D3DColorSpaceConverter : IDisposable
{
    private IntPtr _handle;
    private readonly uint _width;
    private readonly uint _height;
    private readonly uint _inFormat;
    private readonly uint _outFormat;
    private bool _disposed;
    private readonly Lock _lock = new();

    public uint Width => _width;
    public uint Height => _height;
    public uint InFormat => _inFormat;
    public uint OutFormat => _outFormat;
    public bool IsAvailable => _handle != IntPtr.Zero;

    public D3DColorSpaceConverter(
        uint width,
        uint height,
        uint inFormat = 24,  // DXGI_FORMAT_R10G10B10A2_UNORM
        uint outFormat = 104, // DXGI_FORMAT_P010
        IntPtr d3d11Device = 0
    )
    {
        _width = width;
        _height = height;
        _inFormat = inFormat;
        _outFormat = outFormat;
        _handle = MoonshineNativeMethods.ColorConverterCreate(d3d11Device, width, height, inFormat, outFormat);
    }

    public bool TryConvert(IntPtr inTexture, IntPtr outTexture)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;
            return MoonshineNativeMethods.ColorConverterConvert(_handle, inTexture, outTexture) > 0;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.ColorConverterDestroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
