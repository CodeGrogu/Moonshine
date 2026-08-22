using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moonshine.Host.Input;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Input;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class HostInputBenchmarks : IDisposable
{
    private WindowsSendInputInjector _injector = null!;
    private MoonshineHostInputPipeline _pipeline = null!;
    private byte[] _compactBuffer = null!;
    private byte[] _mnbpBuffer = null!;

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
    public bool SendInput_Keyboard_DirectHotPath()
    {
        return _injector.InjectKeyboardKey(0x41, 0, false);
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
