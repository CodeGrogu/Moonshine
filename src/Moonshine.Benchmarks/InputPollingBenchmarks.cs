using BenchmarkDotNet.Attributes;
using Moonshine.Protocol.Input;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class InputPollingBenchmarks
{
    private MouseMovePacket _mouseMove;
    private ControllerStatePacket _gamepad;
    private byte[] _targetBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _mouseMove = new MouseMovePacket(15, -25);
        _gamepad = new ControllerStatePacket(0, GamepadButtons.A | GamepadButtons.RightShoulder, 128, 255, -16000, 32000, 1200, -5000);
        _targetBuffer = new byte[64];
    }

    [Benchmark(Baseline = true)]
    public int SerializeMouseMove()
    {
        return _mouseMove.WriteTo(_targetBuffer);
    }

    [Benchmark]
    public int SerializeControllerState()
    {
        return _gamepad.WriteTo(_targetBuffer);
    }

    [Benchmark]
    public bool ParseMouseMove()
    {
        return MouseMovePacket.TryParse(_targetBuffer, out _);
    }

    [Benchmark]
    public bool ParseControllerState()
    {
        return ControllerStatePacket.TryParse(_targetBuffer, out _);
    }
}
