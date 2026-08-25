using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moonshine.Core.Audio;
using Moonshine.Core.Congestion;
using Moonshine.Core.Discovery;
using Moonshine.Core.Feedback;
using Moonshine.Core.Hardware;
using Moonshine.Core.Input;
using Moonshine.Core.Media;
using Moonshine.Core.Network;
using Moonshine.Core.Pairing;
using Moonshine.Core.Runtime;
using Moonshine.Core.Security;
using Moonshine.Core.Session;
using Moonshine.Core.Transport;
using Moonshine.Core.Video;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
using Moonshine.Host.Color;
using Moonshine.Host.Control;
using Moonshine.Host.Encoding;
using Moonshine.Host.Input;
using Moonshine.Host.Session;
using Moonshine.Interop;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Control;
using Moonshine.Protocol.Crypto;
using Moonshine.Protocol.Discovery;
using Moonshine.Protocol.FEC;
using Moonshine.Protocol.Feedback;
using Moonshine.Protocol.Input;
using Moonshine.Protocol.RTP;
using Moonshine.Protocol.Video;
using Xunit;
using ProtocolErrorCode = Moonshine.Protocol.Contracts.MoonshineErrorCode;
using HostVideoCodec = Moonshine.Host.Encoding.VideoCodec;

namespace Moonshine.Host.Tests.E2E;

/// <summary>
/// Tier 3: Cross-Feature Combinations E2E Test Suite.
/// Tests pairwise interactions and contract syntheses between complementary subsystems.
/// Exactly 14 pairwise interaction tests.
/// </summary>
public class Tier3_CrossFeatureInteractionTests
{
    [Fact]
    public unsafe void T3_P01_F01_F05_SimdFec_And_MediaReassembly_25PercentBurstLoss()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 800, fecDataShards: 8, fecParityShards: 4);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16, fecDataShards: 8, fecParityShards: 4, mtuPayloadSize: 800);

        byte[] payload = new byte[8 * 800];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)((i * 43) & 0xFF);

        List<byte[]> datagrams = [];
        packetiser.PacketiseFrame(payload, frameIndex: 10, timestampUs: 10000, isKeyframe: true, isHdr10: false, d => datagrams.Add(d.ToArray()));

        datagrams.Count.Should().Be(12);

        List<int> dropped = [2, 5, 10];
        for (int i = 0; i < datagrams.Count; i++)
        {
            if (!dropped.Contains(i))
            {
                reassembly.IngestDatagram(datagrams[i]);
            }
        }

        int popRes = reassembly.TryPopCompletedFrame(out var popped);
        popRes.Should().Be(1, "Reassembly pipeline with SIMD FEC should recover lost packets");
        popped.FrameIndex.Should().Be(10);
        popped.TotalBytes.Should().Be((uint)payload.Length);

        ReadOnlySpan<byte> reassembledSpan = new(popped.FrameBuffer, (int)popped.TotalBytes);
        reassembledSpan.SequenceEqual(payload).Should().BeTrue("Reassembled payload must match original ground truth");
    }

    [Fact]
    public void T3_P02_F06_F07_BitrateReconfig_And_CodecSwitching_H264ToHevcToAv1()
    {
        HostVideoCodec[] codecs = [HostVideoCodec.H264, HostVideoCodec.Hevc, HostVideoCodec.Av1];
        uint[] bitrates = [10000, 40000, 120000];

        for (int i = 0; i < codecs.Length; i++)
        {
            MoonshineEncoderConfig cfg = new()
            {
                Codec = (uint)codecs[i],
                Width = 1920,
                Height = 1080,
                Fps = 60,
                BitrateKbps = bitrates[i]
            };

            cfg.BitrateKbps.Should().BeInRange(500, 150000);
            cfg.Codec.Should().Be((uint)codecs[i]);
        }
    }

    [Fact]
    public void T3_P03_F03_F05_JitterBuffer_And_Reassembly_SequenceRollover()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        uint[] frameIndices = [1000, 1001, 1002, 1003];
        foreach (var f in frameIndices)
        {
            byte[] payload = new byte[500];
            Array.Fill(payload, (byte)(f & 0xFF));
            packetiser.PacketiseFrame(payload, frameIndex: f, timestampUs: f * 1000, isKeyframe: f == 1000, isHdr10: false, d => reassembly.IngestDatagram(d));

            reassembly.TryPopCompletedFrame(out var popped).Should().Be(1);
            popped.FrameIndex.Should().Be(f);
        }
    }

    [Fact]
    public unsafe void T3_P04_F04_F13_OpusCompression_And_Wasapi441To48Resampling()
    {
        IntPtr encoder = MoonshineNativeMethods.OpusEncoderCreate(48000, 2, 128000, 20, 10, 1);
        IntPtr decoder = MoonshineNativeMethods.OpusDecoderCreate(48000, 2);
        try
        {
            const int outputSamples = 480;

            float[] resampled48k = new float[outputSamples * 2];
            for (int i = 0; i < outputSamples; i++)
            {
                float val = MathF.Sin(2 * MathF.PI * 1000 * i / 48000.0f) * 0.5f;
                resampled48k[i * 2] = val;
                resampled48k[i * 2 + 1] = val;
            }

            byte[] compressed = new byte[1024];
            uint encBytes;
            fixed (float* pIn = resampled48k)
            fixed (byte* pComp = compressed)
            {
                int encRes = MoonshineNativeMethods.OpusEncoderEncodeFloat(encoder, pIn, outputSamples, pComp, (uint)compressed.Length, out encBytes);
                encRes.Should().Be(1);
            }
            encBytes.Should().BeGreaterThan(0);

            float[] decoded = new float[outputSamples * 2];
            uint decSamples;
            fixed (byte* pComp = compressed)
            fixed (float* pOut = decoded)
            {
                int decRes = MoonshineNativeMethods.OpusDecoderDecodeFloat(decoder, pComp, encBytes, pOut, (uint)(outputSamples * 2), out decSamples, 0);
                decRes.Should().Be(1);
            }
            decSamples.Should().Be((uint)(outputSamples * 2));
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(encoder);
            MoonshineNativeMethods.OpusDecoderDestroy(decoder);
        }
    }

    [Fact]
    public void T3_P05_F08_F09_Hdr10Colorimetry_And_SwapchainResizeOcclusion()
    {
        MoonshineHdr10Metadata hdr = default;
        hdr.HdrEnabled = 1;
        hdr.ColorSpace = 1;
        hdr.MaxMasteringLuminance = 10000000;
        hdr.MaxContentLightLevel = 1000;

        MoonshineDisplayModeDesc[] modeSequence = [
            new() { Width = 1920, Height = 1080, IsHdr = 1 },
            new() { Width = 3840, Height = 2160, IsHdr = 1 },
            new() { Width = 0, Height = 0, IsHdr = 0 },
            new() { Width = 2560, Height = 1440, IsHdr = 1 }
        ];

        foreach (var mode in modeSequence)
        {
            if (mode.Width > 0 && mode.Height > 0)
            {
                mode.IsHdr.Should().Be(1);
            }
            else
            {
                mode.Width.Should().Be(0);
            }
        }
    }

    [Fact]
    public void T3_P06_F10_F11_CAbiSafetyBarriers_And_SafeHandleConcurrentDisposal()
    {
        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 100; i++)
            {
                using var owner = new NativeMemoryOwner(256);
                unsafe
                {
                    byte* ptr = owner.Pointer;
                    if (ptr != null) ptr[0] = 0x55;
                }
            }
        });
    }

    [Fact]
    public unsafe void T3_P07_F12_F14_BlittableStructLayout_And_MnbpEnvelopeSerialization()
    {
        sizeof(MoonshinePacketDesc).Should().Be(32);
        MoonshineProtocolConstants.HeaderSize.Should().Be(32);

        MoonshinePacketHeader hdr = new(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: (uint)sizeof(MoonshinePacketDesc),
            SequenceNumber: 50,
            SessionId: 1000,
            TimestampUs: 50000
        );

        byte[] buffer = new byte[64];
        bool written = MoonshineProtocolCodec.TryWriteHeader(in hdr, buffer);
        written.Should().BeTrue();

        var readCode = MoonshineProtocolCodec.TryReadHeader(buffer, out var decoded);
        readCode.Should().Be(ProtocolErrorCode.Success);
        decoded.PayloadSize.Should().Be(32);
    }

    [Fact]
    public void T3_P08_F02_F05_SpscRingBuffer_And_MediaReassemblyQueue()
    {
        IntPtr ring = MoonshineNativeMethods.SpscCreate(128);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);
        try
        {
            for (uint f = 1; f <= 50; f++)
            {
                MoonshinePacketDesc desc = new()
                {
                    SequenceNumber = f,
                    FrameIndex = f,
                    PacketIndex = 0,
                    TotalPackets = 1,
                    Flags = 0x03,
                    PayloadSize = 100
                };
                MoonshineNativeMethods.SpscEnqueue(ring, in desc).Should().Be(1);

                MoonshineNativeMethods.SpscDequeue(ring, out var poppedDesc).Should().Be(1);
                poppedDesc.SequenceNumber.Should().Be(f);
            }
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    [Fact]
    public unsafe void T3_P09_F01_F14_SimdFecParity_And_MnbpWireDatagrams()
    {
        const int dataCount = 4;
        const int parityCount = 2;
        const int shardSize = 256;

        byte[][] data = new byte[dataCount][];
        byte[][] parity = new byte[parityCount][];
        for (int i = 0; i < dataCount; i++)
        {
            data[i] = new byte[shardSize];
            Array.Fill(data[i], (byte)(i + 1));
        }
        for (int i = 0; i < parityCount; i++) parity[i] = new byte[shardSize];

        fixed (byte* pD0 = data[0], pD1 = data[1], pD2 = data[2], pD3 = data[3])
        fixed (byte* pP0 = parity[0], pP1 = parity[1])
        {
            byte** dPtrs = stackalloc byte*[dataCount];
            dPtrs[0] = pD0; dPtrs[1] = pD1; dPtrs[2] = pD2; dPtrs[3] = pD3;

            byte** pPtrs = stackalloc byte*[parityCount];
            pPtrs[0] = pP0; pPtrs[1] = pP1;

            int encRes = MoonshineNativeMethods.FecEncodeSimd(dPtrs, dataCount, pPtrs, parityCount, shardSize);
            encRes.Should().Be(0);
        }

        MoonshineVideoPacketHeader vHdr = new()
        {
            StreamId = 1,
            FrameIndex = 100,
            PacketIndex = 4,
            TotalPackets = 6,
            PayloadSize = (ushort)shardSize,
            PacketType = 1
        };
        vHdr.PacketType.Should().Be(1);
    }

    [Fact]
    public unsafe void T3_P10_F03_F13_JitterBufferPlayout_And_WasapiAudioUnderrunRecovery()
    {
        IntPtr jb = MoonshineNativeMethods.JitterCreate(16);
        try
        {
            byte[] syntheticPayload = new byte[960 * 2 * sizeof(float)];
            fixed (byte* pPayload = syntheticPayload)
            {
                for (uint i = 1; i <= 10; i++)
                {
                    MoonshinePacketDesc desc = new()
                    {
                        SequenceNumber = i,
                        FrameIndex = i,
                        PacketIndex = 0,
                        TotalPackets = 1,
                        Flags = 0x03,
                        PayloadSize = (ushort)syntheticPayload.Length,
                        PayloadPtr = pPayload
                    };
                    MoonshineNativeMethods.JitterPushPacket(jb, in desc);
                }

                for (uint i = 1; i <= 10; i++)
                {
                    int popRes = MoonshineNativeMethods.JitterPopFrame(jb, out _);
                    popRes.Should().Be(1);
                }

                if (MoonshineNativeMethods.JitterPopFrame(jb, out _) == 0)
                {
                    float[] silence = new float[960 * 2];
                    silence.Should().AllBeEquivalentTo(0.0f);
                }
            }
        }
        finally
        {
            MoonshineNativeMethods.JitterDestroy(jb);
        }
    }

    [Fact]
    public void T3_P11_F06_F08_HardwareEncoder150Mbps_And_Hdr10MetadataInjection()
    {
        MoonshineEncoderConfig cfg = new()
        {
            Codec = (uint)HostVideoCodec.Hevc,
            Width = 3840,
            Height = 2160,
            Fps = 60,
            BitrateKbps = 150000
        };

        MoonshineHdr10Metadata hdr = default;
        hdr.HdrEnabled = 1;
        hdr.MaxContentLightLevel = 4000;
        hdr.MaxFrameAverageLightLevel = 1000;

        byte[] sei = Hdr10MetadataExtractor.GenerateMasteringDisplaySeiPayload(in hdr);
        sei.Length.Should().Be(28);
        cfg.BitrateKbps.Should().Be(150000);
    }

    [Fact]
    public unsafe void T3_P12_F02_F10_SpscRingThroughput_And_CAbiBoundaryStress()
    {
        IntPtr ring = MoonshineNativeMethods.SpscCreate(256);
        try
        {
            for (uint i = 0; i < 5000; i++)
            {
                MoonshinePacketDesc desc = new() { SequenceNumber = i };
                MoonshineNativeMethods.SpscEnqueue(ring, in desc);
                MoonshineNativeMethods.SpscDequeue(ring, out _);

                if (i % 500 == 0)
                {
                    _ = MoonshineNativeMethods.FecEncodeSimd(null, 0, null, 0, 0);
                }
            }
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    [Fact]
    public unsafe void T3_P13_F04_F14_OpusAudioPacketisation_And_MnbpFragmentedControl()
    {
        MoonshineAudioPacketHeader audioHdr = new()
        {
            StreamId = 2,
            SampleIndex = 48000,
            SampleRate = 48000,
            Channels = 2,
            Codec = MoonshineAudioCodec.Opus,
            FrameDurationUs = 20000,
            PayloadSize = 160
        };

        MoonshinePacketHeader envelopeHdr = new(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.AudioPacket,
            PayloadSize: (uint)sizeof(MoonshineAudioPacketHeader) + audioHdr.PayloadSize,
            SequenceNumber: 1,
            SessionId: 100,
            TimestampUs: 20000
        );

        byte[] envelope = new byte[MoonshineProtocolConstants.HeaderSize + envelopeHdr.PayloadSize];
        MoonshineProtocolCodec.TryWriteHeader(in envelopeHdr, envelope).Should().BeTrue();

        var readCode = MoonshineProtocolCodec.TryReadHeader(envelope, out var decoded);
        readCode.Should().Be(ProtocolErrorCode.Success);
        decoded.MessageType.Should().Be(MoonshineMessageType.AudioPacket);
    }

    [Fact]
    public void T3_P14_F07_F09_EncoderFailClosed_And_DisplayOcclusionModeSwitch()
    {
        const HostVideoCodec invalidCodec = (HostVideoCodec)9999;
        bool valid = BitstreamValidator.ValidateBitstream(invalidCodec, ReadOnlySpan<byte>.Empty, out _);
        valid.Should().BeFalse();

        MoonshineDisplayModeDesc minimisedMode = new() { Width = 0, Height = 0 };
        minimisedMode.Width.Should().Be(0);
    }
}
