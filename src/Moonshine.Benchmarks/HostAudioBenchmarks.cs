using BenchmarkDotNet.Attributes;
using Moonshine.Core.Media;
using Moonshine.Host.Audio;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Benchmarks;

[InProcess]
[MemoryDiagnoser]
public class HostAudioBenchmarks : IDisposable
{
    private WasapiLoopbackAudioPipeline _wasapiLoopback = null!;
    private OpusAudioEncoderPipeline _opusEncoderStereo = null!;
    private MoonshineAudioPacketiser _moonshinePacketiser = null!;
    private RtpAudioPacketiser _rtpPacketiser = null!;
    private MoonshineHostAudioPipeline _pipeline = null!;

    private float[] _pcmBuffer = null!;
    private byte[] _encodedBuffer = null!;
    private byte[] _rtpOutBuffer = null!;
    private AudioPacketSink _sink = null!;
    private ulong _sampleCounter;

    [GlobalSetup]
    public void Setup()
    {
        _wasapiLoopback = new WasapiLoopbackAudioPipeline(48000, AudioChannelTopology.Stereo, 5);
        _opusEncoderStereo = new OpusAudioEncoderPipeline(48000, AudioChannelTopology.Stereo, 160000, 5, 8, true);
        _moonshinePacketiser = new MoonshineAudioPacketiser(1, 0x12345678, 48000, 2, MoonshineAudioCodec.Opus);
        _rtpPacketiser = new RtpAudioPacketiser(97, 0x12345678, 0);

        _pipeline = new MoonshineHostAudioPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Stereo,
            bitrate: 160000,
            frameDurationMs: 5,
            forceWasapiFallback: true
        );

        _pcmBuffer = new float[480]; // 240 samples * 2 ch
        _encodedBuffer = new byte[1024];
        _rtpOutBuffer = new byte[1024];

        // Seed with test samples
        for (int i = 0; i < _pcmBuffer.Length; i++)
        {
            _pcmBuffer[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0);
        }

        _sink = datagram =>
        {
            _ = datagram.Length;
        };

        // Warmup encoder to populate encoded buffer
        _opusEncoderStereo.TryEncode(_pcmBuffer, 240, _encodedBuffer, out _);
    }

    [Benchmark]
    public bool WasapiLoopback_ReadSamples_DirectHotPath()
    {
        return _wasapiLoopback.TryReadSamples(_pcmBuffer, out _, out _);
    }

    [Benchmark]
    public bool OpusEncoder_EncodeStereo_DirectHotPath()
    {
        return _opusEncoderStereo.TryEncode(_pcmBuffer, 240, _encodedBuffer, out _);
    }

    [Benchmark]
    public int MoonshineAudioPacketiser_PacketiseAudioFrame_DirectHotPath()
    {
        ulong sampleIdx = Interlocked.Increment(ref _sampleCounter) * 240;
        return _moonshinePacketiser.PacketiseAudioFrame(
            _encodedBuffer.AsSpan(0, 128),
            sampleIdx,
            5000,
            1000000 + sampleIdx,
            _sink
        );
    }

    [Benchmark]
    public bool RtpAudioPacketiser_Packetise_DirectHotPath()
    {
        uint rtpTs = (uint)(Interlocked.Increment(ref _sampleCounter) * 240);
        return _rtpPacketiser.TryPacketise(
            _encodedBuffer.AsSpan(0, 128),
            rtpTs,
            marker: true,
            _rtpOutBuffer,
            out _
        );
    }

    [Benchmark]
    public bool HostAudioPipeline_EndToEnd_CaptureEncodePacketise_HotPath()
    {
        return _pipeline.ProcessNextAudioFrame(_sink, preferMoonshineFraming: true);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        _wasapiLoopback?.Dispose();
        _opusEncoderStereo?.Dispose();
        _pipeline?.Dispose();
        GC.SuppressFinalize(this);
    }
}
