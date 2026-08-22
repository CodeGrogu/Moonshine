using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Moonshine.Core.Input;
using Moonshine.Interop;
using Moonshine.Protocol.Input;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class InputCaptureBenchmarks : IDisposable
{
    private WindowsRawInputCapture _rawInputCapture = null!;
    private WindowsXInputCapture _xinputCapture = null!;
    private MoonshineClientInputPipeline _pipeline = null!;
    private IntPtr _rawInputBuffer;

    [GlobalSetup]
    public unsafe void Setup()
    {
        _rawInputCapture = new WindowsRawInputCapture();
        _xinputCapture = new WindowsXInputCapture();
        _pipeline = new MoonshineClientInputPipeline(pollingFrequencyHz: 1000, controllerPollRateHz: 250);

        _rawInputBuffer = (IntPtr)NativeMemory.AllocZeroed((nuint)sizeof(RAWINPUT));
        RAWINPUT* pRaw = (RAWINPUT*)_rawInputBuffer;
        pRaw->header.dwType = WindowsInputNativeMethods.RIM_TYPEMOUSE;
        pRaw->header.dwSize = (uint)sizeof(RAWINPUT);
        pRaw->mouse.lLastX = 12;
        pRaw->mouse.lLastY = -8;
    }

    [GlobalCleanup]
    public unsafe void Cleanup()
    {
        _pipeline?.Dispose();
        _rawInputCapture?.Dispose();
        _xinputCapture?.Dispose();

        if (_rawInputBuffer != IntPtr.Zero)
        {
            NativeMemory.Free((void*)_rawInputBuffer);
            _rawInputBuffer = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Benchmark(Baseline = true)]
    public unsafe int RawInput_MouseMove_ProcessingHotPath()
    {
        return _rawInputCapture.ProcessRawInputData(*(RAWINPUT*)_rawInputBuffer);
    }

    [Benchmark]
    public int XInput_ControllerPoll_HotPath()
    {
        return _xinputCapture.PollControllers();
    }

    [Benchmark]
    public void InputPipeline_MouseMove_EndToEndHotPath()
    {
        _pipeline.PollingEngine.IngestMouseMove(15, -10);
    }
}
