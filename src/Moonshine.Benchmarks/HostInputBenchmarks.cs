using BenchmarkDotNet.Attributes;
using Moonshine.Host.Input;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Input;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public unsafe class HostInputBenchmarks : IDisposable
{
    private WindowsSendInputInjector _injector = null!;
    private MoonshineHostInputPipeline _pipeline = null!;
    private byte[] _compactBuffer = null!;
    private byte[] _mnbpBuffer = null!;
    private INPUT[] _batchInputs = null!;

    [GlobalSetup]
    public void Setup()
    {
        _injector = new WindowsSendInputInjector();
        _pipeline = new MoonshineHostInputPipeline(
            _injector,
            config: new HostInputConfig { EnforceSequenceMonotonicity = false });

        var mouseMove = new MouseMovePacket(5, -5);
        _compactBuffer = new byte[MouseMovePacket.PacketSize];
        mouseMove.WriteTo(_compactBuffer);

        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.InputMouse,
            PayloadSize: 20,
            SequenceNumber: 1,
            SessionId: 0,
            TimestampUs: 1000
        );
        _mnbpBuffer = new byte[MoonshineProtocolConstants.HeaderSize + 20];
        MoonshineProtocolCodec.TryWriteHeader(header, _mnbpBuffer);

        var mousePayload = new MoonshineInputMousePayload
        {
            X = 5,
            Y = -5,
            WheelDeltaY = 0,
            WheelDeltaX = 0,
            ButtonFlags = 0,
            IsAbsolute = 0,
            TimestampOffsetUs = 0
        };
        MoonshineProtocolCodec.TryWriteMouseInput(mousePayload, _mnbpBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

        _batchInputs = new INPUT[2];
        _batchInputs[0].type = WindowsInputNativeMethods.INPUT_MOUSE;
        _batchInputs[0].mi.dx = 2;
        _batchInputs[0].mi.dy = -2;
        _batchInputs[0].mi.dwFlags = WindowsInputNativeMethods.MOUSEEVENTF_MOVE;

        _batchInputs[1].type = WindowsInputNativeMethods.INPUT_MOUSE;
        _batchInputs[1].mi.mouseData = 120;
        _batchInputs[1].mi.dwFlags = WindowsInputNativeMethods.MOUSEEVENTF_WHEEL;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pipeline?.Dispose();
        _injector?.Dispose();
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Benchmark(Baseline = true)]
    public bool SendInput_MouseMove_DirectHotPath()
    {
        return _injector.InjectMouseMove(5, -5);
    }

    [Benchmark]
    public bool SendInput_MouseAbsolute_MultiMonitor_DirectHotPath()
    {
        return _injector.InjectMouseMoveAbsolute(960, 540, 1920, 1080, 1920, 0, 1920, 1080);
    }

    [Benchmark]
    public bool SendInput_MouseButton_DirectHotPath()
    {
        return _injector.InjectMouseButton(1, false);
    }

    [Benchmark]
    public bool SendInput_MouseScroll_Horizontal_DirectHotPath()
    {
        return _injector.InjectMouseScroll(120, isHorizontal: true);
    }

    [Benchmark]
    public bool SendInput_Keyboard_DirectHotPath()
    {
        return _injector.InjectKeyboardKey(0x41, 0, false);
    }

    [Benchmark]
    public bool SendInput_Keyboard_ExtendedKey_DirectHotPath()
    {
        return _injector.InjectKeyboardKey(0x27, 0, false);
    }

    [Benchmark]
    public int SendInput_BatchedInjection_DirectHotPath()
    {
        return _injector.InjectBatch(_batchInputs);
    }

    [Benchmark]
    public bool HostInputPipeline_CompactMouseMove_EndToEndHotPath()
    {
        return _pipeline.ProcessInputPacket(_compactBuffer);
    }

    [Benchmark]
    public bool HostInputPipeline_MnbpMouse_EndToEndHotPath()
    {
        return _pipeline.ProcessInputPacket(_mnbpBuffer);
    }
}
