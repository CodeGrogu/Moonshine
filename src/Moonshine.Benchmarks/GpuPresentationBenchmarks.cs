using BenchmarkDotNet.Attributes;
using Moonshine.Core.Video;
using Moonshine.Interop;

namespace Moonshine.Benchmarks;

[InProcess]
[MemoryDiagnoser]
public class GpuPresentationBenchmarks : IDisposable
{
    private MoonshineClientGpuPresenter _presenter = null!;
    private ulong _frameCounter;

    [GlobalSetup]
    public void Setup()
    {
        _presenter = new MoonshineClientGpuPresenter(
            hwnd: IntPtr.Zero,
            d3d11Device: IntPtr.Zero,
            width: 1920,
            height: 1080,
            targetRefreshRate: 144,
            queueCapacity: 32
        );
    }

    [Benchmark]
    public bool EnqueueFrame_HotPath()
    {
        ulong idx = ++_frameCounter;
        return _presenter.EnqueueFrame(
            textureHandle: (IntPtr)0x5678,
            frameIndex: idx,
            captureTimestampQpc: 1000000 + (long)idx * 100,
            isKeyframe: (idx % 60) == 0
        );
    }

#pragma warning disable CA1822 // Mark members as static
    [Benchmark]
    public int SwapchainPresent_CAbiCall()
    {
        // Direct C-ABI call (fails closed safely on null handle with 0 alloc)
        return MoonshineNativeMethods.SwapchainPresent(IntPtr.Zero, 0, 0);
    }

    [Benchmark]
    public int SwapchainPresentTexture_CAbiCall()
    {
        // Direct C-ABI call (fails closed safely on null handle with 0 alloc)
        return MoonshineNativeMethods.SwapchainPresentTexture(IntPtr.Zero, IntPtr.Zero, 0, 0);
    }
#pragma warning restore CA1822

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        _presenter?.Dispose();
        GC.SuppressFinalize(this);
    }
}
