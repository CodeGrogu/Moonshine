using FluentAssertions;
using Moonshine.Core.Audio;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Core.Tests;

public sealed class ClientMicrophoneCapturePipelineTests
{
    [Fact]
    public void Pipeline_Initialises_WithDefaultParameters()
    {
        using var pipeline = new ClientMicrophoneCapturePipeline();
        pipeline.SampleRate.Should().Be(48000);
        pipeline.Channels.Should().Be(1);
        pipeline.Bitrate.Should().Be(32000);
        pipeline.FrameDurationMs.Should().Be(10);
        pipeline.StreamId.Should().Be(0x99887766);
        pipeline.IsMuted.Should().BeFalse();
        pipeline.GainMultiplier.Should().Be(1.0f);
        pipeline.NoiseGateThresholdDb.Should().Be(-50.0f);
        pipeline.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void Pipeline_ProcessesPcmFrame_RtpFraming()
    {
        using var pipeline = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10,
            streamId: 0x12345678,
            payloadType: 98
        );

        float[] pcm = new float[480];
        for (int i = 0; i < pcm.Length; ++i)
        {
            pcm[i] = MathF.Sin(2.0f * MathF.PI * 440.0f * i / 48000.0f) * 0.5f;
        }

        byte[] datagram = new byte[1500];
        bool success = pipeline.TryProcessRecordedFrame(pcm, datagram, out int bytesWritten, preferMoonshineFraming: false);

        success.Should().BeTrue();
        bytesWritten.Should().BeGreaterThan(MicAudioPacket.RtpHeaderSize);

        bool parseOk = MicAudioPacket.TryParse(datagram.AsSpan(0, bytesWritten), out var packet);
        parseOk.Should().BeTrue();
        packet.PayloadType.Should().Be(98);
        packet.Ssrc.Should().Be(0x12345678);
        packet.SequenceNumber.Should().Be(0);
        packet.Payload.Length.Should().Be(bytesWritten - MicAudioPacket.RtpHeaderSize);
    }

    [Fact]
    public void Pipeline_ProcessesPcmFrame_MnbpFraming()
    {
        using var pipeline = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10,
            streamId: 0xABCDEF01,
            sessionId: 0x9999888877776666UL
        );

        float[] pcm = new float[480];
        for (int i = 0; i < pcm.Length; ++i)
        {
            pcm[i] = MathF.Sin(2.0f * MathF.PI * 1000.0f * i / 48000.0f) * 0.5f;
        }

        byte[] datagram = new byte[1500];
        bool success = pipeline.TryProcessRecordedFrame(pcm, datagram, out int bytesWritten, preferMoonshineFraming: true);

        success.Should().BeTrue();
        bytesWritten.Should().BeGreaterThan(MoonshineProtocolConstants.HeaderSize + MoonshineMicPacketCodec.HeaderSize);

        MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(datagram.AsSpan(0, bytesWritten), out var outerHeader);
        err.Should().Be(MoonshineErrorCode.Success);
        outerHeader.MessageType.Should().Be(MoonshineMessageType.MicPacket);
        outerHeader.SessionId.Should().Be(0x9999888877776666UL);
        outerHeader.SequenceNumber.Should().Be(0);

        bool micOk = MoonshineMicPacketCodec.TryReadHeader(
            datagram.AsSpan(MoonshineProtocolConstants.HeaderSize, MoonshineMicPacketCodec.HeaderSize),
            out var micHeader
        );
        micOk.Should().BeTrue();
        micHeader.StreamId.Should().Be(0xABCDEF01);
        micHeader.SampleRate.Should().Be(48000);
        micHeader.Channels.Should().Be(1);
        micHeader.Codec.Should().Be(MoonshineAudioCodec.Opus);
        micHeader.PayloadSize.Should().Be((ushort)(bytesWritten - MoonshineProtocolConstants.HeaderSize - MoonshineMicPacketCodec.HeaderSize));
    }

    [Fact]
    public void Pipeline_DynamicControls_SetGainMuteAndThreshold()
    {
        using var pipeline = new ClientMicrophoneCapturePipeline();

        pipeline.SetGain(2.5f);
        pipeline.GainMultiplier.Should().Be(2.5f);

        pipeline.SetGain(15.0f);
        pipeline.GainMultiplier.Should().Be(10.0f);

        pipeline.SetGain(-1.0f);
        pipeline.GainMultiplier.Should().Be(0.0f);

        pipeline.SetMute(true);
        pipeline.IsMuted.Should().BeTrue();

        pipeline.SetNoiseGateThreshold(-40.0f);
        pipeline.NoiseGateThresholdDb.Should().Be(-40.0f);
    }

    [Fact]
    public void Pipeline_MuteSilencing_EncodesSilentFrame()
    {
        using var pipeline = new ClientMicrophoneCapturePipeline();
        pipeline.SetMute(true);

        float[] pcm = new float[480];
        Array.Fill(pcm, 0.9f);

        byte[] datagram = new byte[1500];
        bool success = pipeline.TryProcessRecordedFrame(pcm, datagram, out int bytesWritten, preferMoonshineFraming: false);

        success.Should().BeTrue();
        bytesWritten.Should().BeGreaterThan(MicAudioPacket.RtpHeaderSize);
    }

    [Fact]
    public void Pipeline_Disposal_ThrowsObjectDisposedException()
    {
        var pipeline = new ClientMicrophoneCapturePipeline();
        pipeline.Dispose();
        pipeline.IsInitialized.Should().BeFalse();

        float[] pcm = new float[480];
        byte[] datagram = new byte[1500];
        var act = () => pipeline.TryProcessRecordedFrame(pcm, datagram, out _);
        act.Should().Throw<ObjectDisposedException>();
    }
}
