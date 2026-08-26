using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Moonshine.UI.Controls;

/// <summary>
/// COM interop definition for WinUI 3 SwapChainPanel native interface.
/// </summary>
[ComImport]
[Guid("634459D8-FA9C-4348-982E-1B1EB875F097")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISwapChainPanelNative
{
    [PreserveSig]
    int SetSwapChain(IntPtr swapChain);
    
    [PreserveSig]
    int SetSize(int width, int height);
    
    [PreserveSig]
    int SetTransform(IntPtr transform);
}

/// <summary>
/// Bridge that attaches a native DXGI swap chain to a WinUI 3 <see cref="SwapChainPanel"/>.
/// Handles high-DPI composition scale changes and panel dimension resizing without managed frame copies.
/// </summary>
public sealed class DirectXVideoPresenter : IDisposable
{
    private readonly SwapChainPanel _panel;
    private IntPtr _swapChainHandle = IntPtr.Zero;
    private bool _disposed;

    public event EventHandler<(double Width, double Height, float ScaleX, float ScaleY)>? DimensionsChanged;

    public DirectXVideoPresenter(SwapChainPanel panel)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _panel.SizeChanged += OnSizeChanged;
        _panel.CompositionScaleChanged += OnCompositionScaleChanged;
    }

    /// <summary>
    /// Attaches the native DXGI swap chain handle to the WinUI 3 SwapChainPanel.
    /// </summary>
    public bool AttachSwapChain(IntPtr swapChainHandle)
    {
        if (_disposed || swapChainHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            // Query ISwapChainPanelNative from the WinUI 3 SwapChainPanel
            var panelUnknown = Marshal.GetIUnknownForObject(_panel);
            if (panelUnknown == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var iid = new Guid("634459D8-FA9C-4348-982E-1B1EB875F097");
                int hr = Marshal.QueryInterface(panelUnknown, in iid, out var nativePtr);
                if (hr != 0 || nativePtr == IntPtr.Zero)
                {
                    // Fallback to legacy Reunion/WinUI 3 IID
                    iid = new Guid("F92F66D2-C588-4B41-A05C-22E3047C74C6");
                    hr = Marshal.QueryInterface(panelUnknown, in iid, out nativePtr);
                }

                if (hr == 0 && nativePtr != IntPtr.Zero)
                {
                    try
                    {
                        var panelNative = (ISwapChainPanelNative)Marshal.GetObjectForIUnknown(nativePtr);
                        int setHr = panelNative.SetSwapChain(swapChainHandle);
                        if (setHr == 0)
                        {
                            _swapChainHandle = swapChainHandle;
                            return true;
                        }
                    }
                    finally
                    {
                        Marshal.Release(nativePtr);
                    }
                }
            }
            finally
            {
                Marshal.Release(panelUnknown);
            }
        }
        // ALLOWED_EXCEPTION: Handle native COM interface query failures gracefully during DXGI attachment.
        catch (Exception)
        {
        }

        return false;
    }

    /// <summary>
    /// Detaches the active DXGI swap chain from the SwapChainPanel.
    /// </summary>
    public void DetachSwapChain()
    {
        if (_swapChainHandle != IntPtr.Zero)
        {
            AttachSwapChain(IntPtr.Zero);
            _swapChainHandle = IntPtr.Zero;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        NotifyDimensionsChanged();
    }

    private void OnCompositionScaleChanged(SwapChainPanel sender, object args)
    {
        NotifyDimensionsChanged();
    }

    private void NotifyDimensionsChanged()
    {
        if (_disposed) return;
        
        double w = _panel.ActualWidth;
        double h = _panel.ActualHeight;
        float sx = _panel.CompositionScaleX;
        float sy = _panel.CompositionScaleY;

        DimensionsChanged?.Invoke(this, (w, h, sx, sy));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _panel.SizeChanged -= OnSizeChanged;
        _panel.CompositionScaleChanged -= OnCompositionScaleChanged;
        DetachSwapChain();
    }
}
