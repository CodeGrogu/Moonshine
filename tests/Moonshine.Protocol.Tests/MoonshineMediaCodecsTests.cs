using FluentAssertions;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Video;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class MoonshineMediaCodecsTests
{
    [Fact]
    public void MoonshineVideoPacketCodec_RoundtripsSuccessfully()
    {
        var header = new MoonshineVideoPacketHeader
        {
            StreamId = 101,
            FrameIndex = 42000,
            PacketIndex = 7,
            TotalPackets = 12,
            FecBlockIndex = 1,
            PayloadSize = 1188,
            PacketType = 0,
            Flags = MoonshineVideoAttributes.Keyframe | MoonshineVideoAttributes.Hdr10Present,
            TotalFrameBytes = 14256
        };

        Span<byte> buffer = stackalloc byte[MoonshineVideoPacketCodec.HeaderSize];
        bool writeSuccess = MoonshineVideoPacketCodec.TryWriteHeader(in header, buffer);
        writeSuccess.Should().BeTrue();

        bool readSuccess = MoonshineVideoPacketCodec.TryReadHeader(buffer, out var parsed);
        readSuccess.Should().BeTrue();
        parsed.StreamId.Should().Be(header.StreamId);
        parsed.FrameIndex.Should().Be(header.FrameIndex);
        parsed.PacketIndex.Should().Be(header.PacketIndex);
        parsed.TotalPackets.Should().Be(header.TotalPackets);
        parsed.FecBlockIndex.Should().Be(header.FecBlockIndex);
        parsed.PayloadSize.Should().Be(header.PayloadSize);
        parsed.PacketType.Should().Be(header.PacketType);
        parsed.Flags.Should().Be(header.Flags);
        parsed.TotalFrameBytes.Should().Be(header.TotalFrameBytes);
    }

    [Fact]
    public void MoonshineAudioPacketCodec_RoundtripsSuccessfully()
    {
        var header = new MoonshineAudioPacketHeader
        {
            StreamId = 202,
            SampleIndex = 960000,
            SampleRate = 48000,
            FrameDurationUs = 10000,
            PayloadSize = 320,
            Channels = 6, // Surround 5.1
            Codec = MoonshineAudioCodec.Opus,
            Reserved = 0
        };

        Span<byte> buffer = stackalloc byte[MoonshineAudioPacketCodec.HeaderSize];
        bool writeSuccess = MoonshineAudioPacketCodec.TryWriteHeader(in header, buffer);
        writeSuccess.Should().BeTrue();

        bool readSuccess = MoonshineAudioPacketCodec.TryReadHeader(buffer, out var parsed);
        readSuccess.Should().BeTrue();
        parsed.StreamId.Should().Be(header.StreamId);
        parsed.SampleIndex.Should().Be(header.SampleIndex);
        parsed.SampleRate.Should().Be(header.SampleRate);
        parsed.FrameDurationUs.Should().Be(header.FrameDurationUs);
        parsed.PayloadSize.Should().Be(header.PayloadSize);
        parsed.Channels.Should().Be(header.Channels);
        parsed.Codec.Should().Be(header.Codec);
    }

    [Fact]
    public void Codecs_BufferTooSmall_ReturnsFalse()
    {
        Span<byte> tiny = stackalloc byte[10];
        MoonshineVideoPacketCodec.TryWriteHeader(default, tiny).Should().BeFalse();
        MoonshineVideoPacketCodec.TryReadHeader(tiny, out _).Should().BeFalse();

        MoonshineAudioPacketCodec.TryWriteHeader(default, tiny).Should().BeFalse();
        MoonshineAudioPacketCodec.TryReadHeader(tiny, out _).Should().BeFalse();

        MoonshineMicPacketCodec.TryWriteHeader(default, tiny).Should().BeFalse();
        MoonshineMicPacketCodec.TryReadHeader(tiny, out _).Should().BeFalse();
    }

    [Fact]
    public void MoonshineMicPacketCodec_RoundtripsSuccessfully()
    {
        var header = new MoonshineMicPacketHeader
        {
            StreamId = 0x99887766,
            SampleIndex = 480000,
            PayloadSize = 80,
            Channels = 1,
            Codec = MoonshineAudioCodec.Opus,
            SampleRate = 48000
        };

        Span<byte> buffer = stackalloc byte[MoonshineMicPacketCodec.HeaderSize];
        bool writeSuccess = MoonshineMicPacketCodec.TryWriteHeader(in header, buffer);
        writeSuccess.Should().BeTrue();

        bool readSuccess = MoonshineMicPacketCodec.TryReadHeader(buffer, out var parsed);
        readSuccess.Should().BeTrue();
        parsed.StreamId.Should().Be(header.StreamId);
        parsed.SampleIndex.Should().Be(header.SampleIndex);
        parsed.PayloadSize.Should().Be(header.PayloadSize);
        parsed.Channels.Should().Be(header.Channels);
        parsed.Codec.Should().Be(header.Codec);
        parsed.SampleRate.Should().Be(header.SampleRate);
    }

    [Fact]
    public void MoonshineVideoPacketCodec_DefensiveBounds_RejectsInvalidPackets()
    {
        Span<byte> buffer = stackalloc byte[MoonshineVideoPacketCodec.HeaderSize];

        // 1. Zero stream ID
        var header = new MoonshineVideoPacketHeader
        {
            StreamId = 0,
            FrameIndex = 1,
            PacketIndex = 0,
            TotalPackets = 10,
            PayloadSize = 1000,
            TotalFrameBytes = 10000
        };
        MoonshineVideoPacketCodec.TryWriteHeader(in header, buffer).Should().BeTrue();
        MoonshineVideoPacketCodec.TryReadHeader(buffer, out _).Should().BeFalse();

        // 2. Fragment indexing violation (packetIndex >= totalPackets on data packet)
        header = header with { StreamId = 101, PacketIndex = 10, TotalPackets = 10, PacketType = 0 };
        MoonshineVideoPacketCodec.TryWriteHeader(in header, buffer).Should().BeTrue();
        MoonshineVideoPacketCodec.TryReadHeader(buffer, out _).Should().BeFalse();

        // 3. MTU ceiling violation (> 65507)
        header = header with { PacketIndex = 0, PayloadSize = 65535 };
        MoonshineVideoPacketCodec.TryWriteHeader(in header, buffer).Should().BeTrue();
        MoonshineVideoPacketCodec.TryReadHeader(buffer, out _).Should().BeFalse();
    }

    [Fact]
    public void MoonshineAudioPacketCodec_DefensiveBounds_RejectsInvalidAudio()
    {
        Span<byte> buffer = stackalloc byte[MoonshineAudioPacketCodec.HeaderSize];

        // 1. Invalid channels (e.g. 3 channels)
        var header = new MoonshineAudioPacketHeader
        {
            StreamId = 202,
            SampleIndex = 1000,
            SampleRate = 48000,
            Channels = 3, // Invalid
            Codec = MoonshineAudioCodec.Opus,
            PayloadSize = 320
        };
        MoonshineAudioPacketCodec.TryWriteHeader(in header, buffer).Should().BeTrue();
        MoonshineAudioPacketCodec.TryReadHeader(buffer, out _).Should().BeFalse();

        // 2. Invalid sample rate (4000 Hz)
        header = header with { Channels = 2, SampleRate = 4000 };
        MoonshineAudioPacketCodec.TryWriteHeader(in header, buffer).Should().BeTrue();
        MoonshineAudioPacketCodec.TryReadHeader(buffer, out _).Should().BeFalse();

        // 3. Unknown codec
        header = header with { SampleRate = 48000, Codec = MoonshineAudioCodec.Unknown };
        MoonshineAudioPacketCodec.TryWriteHeader(in header, buffer).Should().BeTrue();
        MoonshineAudioPacketCodec.TryReadHeader(buffer, out _).Should().BeFalse();
    }

    [Fact]
    public void MoonshineMicPacketCodec_DefensiveBounds_RejectsInvalidMic()
    {
        Span<byte> buffer = stackalloc byte[MoonshineMicPacketCodec.HeaderSize];

        // 1. Invalid channels (3 channels)
        var header = new MoonshineMicPacketHeader
        {
            StreamId = 303,
            SampleIndex = 1000,
            SampleRate = 48000,
            Channels = 3, // Max 2 for mic
            Codec = MoonshineAudioCodec.Opus,
            PayloadSize = 80
        };
        MoonshineMicPacketCodec.TryWriteHeader(in header, buffer).Should().BeTrue();
        MoonshineMicPacketCodec.TryReadHeader(buffer, out _).Should().BeFalse();

        // 2. Zero stream ID
        header = header with { Channels = 1, StreamId = 0 };
        MoonshineMicPacketCodec.TryWriteHeader(in header, buffer).Should().BeTrue();
        MoonshineMicPacketCodec.TryReadHeader(buffer, out _).Should().BeFalse();
    }
}

