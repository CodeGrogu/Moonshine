using BenchmarkDotNet.Attributes;
using Moonshine.Core.Audio;
using Moonshine.Core.Media;
using Moonshine.Host.Audio;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Benchmarks;

[InProcess]
[MemoryDiagnoser]
public class ClientAudioBenchmarks : IDisposable
{
    private OpusAudioEncoderPipeline _encoderStereo = null!;
    private OpusAudioEncoderPipeline _encoder51 = null!;
    private OpusAudioDecoderPipeline _decoderStereo = null!;
    private OpusAudioDecoderPipeline _decoder51 = null!;
    private MoonshineAudioPipeline _renderer = null!;
    private MoonshineClientAudioPipeline _clientPipeline = null!;

    private float[] _pcmInStereo = null!;
    private float[] _pcmIn51 = null!;
    private byte[] _compressedStereo = null!;
    private int _compressedStereoLen;
    private byte[] _compressed51 = null!;
    private int _compressed51Len;

    private float[] _pcmOutStereo = null!;
    private float[] _pcmOut51 = null!;

    private byte[] _moonshineDatagram = null!;
    private int _datagramLen;

    [GlobalSetup]
    public void Setup()
    {
        _encoderStereo = new OpusAudioEncoderPipeline(48000, AudioChannelTopology.Stereo, 160000, 5, 8, true);
        _encoder51 = new OpusAudioEncoderPipeline(48000, AudioChannelTopology.Surround51, 256000, 5, 8, true);

        _decoderStereo = new OpusAudioDecoderPipeline(48000, AudioChannelConfiguration.Stereo);
        _decoder51 = new OpusAudioDecoderPipeline(48000, AudioChannelConfiguration.Surround51);

        _renderer = new MoonshineAudioPipeline(48000, AudioChannelConfiguration.Stereo, isExclusive: false);
        _clientPipeline = new MoonshineClientAudioPipeline(
            sampleRate: 48000,
            channels: AudioChannelConfiguration.Stereo,
            isExclusive: false,
            startBackgroundWorker: false
        );

        _pcmInStereo = new float[480]; // 240 samples * 2 ch
        _pcmIn51 = new float[1440]; // 240 samples * 6 ch

        _pcmOutStereo = new float[480];
        _pcmOut51 = new float[1440];

        _compressedStereo = new byte[1024];
        _compressed51 = new byte[2048];

        for (int i = 0; i < _pcmInStereo.Length; i++)
        {
            _pcmInStereo[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0);
        }

        for (int i = 0; i < _pcmIn51.Length; i++)
        {
            _pcmIn51[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0);
        }

        _encoderStereo.TryEncode(_pcmInStereo, 240, _compressedStereo, out _compressedStereoLen);
        _encoder51.TryEncode(_pcmIn51, 240, _compressed51, out _compressed51Len);

        // Pre-create Moonshine datagram
        var packetiser = new MoonshineAudioPacketiser(1, 0x12345678, 48000, 2, MoonshineAudioCodec.Opus);
        _moonshineDatagram = new byte[2048];
        packetiser.PacketiseAudioFrame(
            _compressedStereo.AsSpan(0, _compressedStereoLen),
            0,
            5000,
            1000000,
            datagram =>
            {
                datagram.CopyTo(_moonshineDatagram);
                _datagramLen = datagram.Length;
            }
        );
    }

    [Benchmark]
    public bool OpusDecoder_DecodeStereo_DirectHotPath()
    {
        return _decoderStereo.DecodeFloat(_compressedStereo.AsSpan(0, _compressedStereoLen), _pcmOutStereo, out _);
    }

    [Benchmark]
    public bool OpusDecoder_Decode51Surround_DirectHotPath()
    {
        return _decoder51.DecodeFloat(_compressed51.AsSpan(0, _compressed51Len), _pcmOut51, out _);
    }

    [Benchmark]
    public bool WasapiRenderer_SubmitPcm_DirectHotPath()
    {
        return _renderer.SubmitPcm(_pcmOutStereo);
    }

    [Benchmark]
    public bool ClientAudioPipeline_EndToEnd_IngestDecodeRender_HotPath()
    {
        return _clientPipeline.ProcessDirectFrame(_compressedStereo.AsSpan(0, _compressedStereoLen));
    }

    public void Dispose()
    {
        _encoderStereo.Dispose();
        _encoder51.Dispose();
        _decoderStereo.Dispose();
        _decoder51.Dispose();
        _renderer.Dispose();
        _clientPipeline.Dispose();
        GC.SuppressFinalize(this);
    }
}
