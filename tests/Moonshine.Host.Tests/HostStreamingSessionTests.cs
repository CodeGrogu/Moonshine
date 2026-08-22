using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Moonshine.Core.Media;
using Moonshine.Core.Network;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
using Moonshine.Host.Encoding;
using Moonshine.Host.Input;
using Moonshine.Host.Session;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Control;
using Moonshine.Protocol.Video;
using Xunit;
using MoonshineErrorCode = Moonshine.Protocol.Contracts.MoonshineErrorCode;

namespace Moonshine.Host.Tests;

public class HostStreamingSessionTests
{
    private sealed class TestDesktopCapturePipeline : IDesktopCapturePipeline
    {
        public uint Width => 1920;
        public uint Height => 1080;
        public uint Format => 28; // DXGI_FORMAT_R8G8B8A8_UNORM
        public bool IsHdr => false;
        public uint AdapterIndex => 0;
        public uint OutputIndex => 0;
        public bool IsAvailable { get; set; } = true;
        public CaptureMetrics Metrics => new(0, 0, 0, 0, 1920, 1080, 28, false, 0.0);

        public bool ShouldFailAcquire { get; set; }
        public bool ShouldFailRecovery { get; set; }

        public unsafe bool TryAcquireNextFrame(uint timeoutMs, out MoonshineCaptureFrameDesc frame)
        {
            if (ShouldFailAcquire || !IsAvailable)
            {
                frame = default;
                return false;
            }

            frame = new MoonshineCaptureFrameDesc
            {
                TextureHandle = (void*)0x1000,
                Width = 1920,
                Height = 1080,
                Format = 28,
                TimestampQpc = (ulong)Stopwatch.GetTimestamp()
            };
            return true;
        }

        public void ReleaseFrame()
        {
        }

        public bool TryRecover()
        {
            if (ShouldFailRecovery)
            {
                IsAvailable = false;
                return false;
            }

            IsAvailable = true;
            return true;
        }

        public void Dispose()
        {
            IsAvailable = false;
        }
    }

    private sealed class TestVideoEncoderPipeline : IVideoEncoderPipeline
    {
        public uint Width => 1920;
        public uint Height => 1080;
        public uint Fps => 60;
        public uint BitrateKbps { get; private set; } = 20000;
        public VideoCodec Codec => VideoCodec.HevcMain10;
        public EncoderVendor Vendor => EncoderVendor.Direct3D11Hardware;
        public bool IsActive { get; set; } = true;
        public double AverageEncodingLatencyMicroseconds => 250.0;

        public int ForceIdrCallCount { get; private set; }
        public int ReconfigureCallCount { get; private set; }

        public bool TryEncodeFrame(
            IntPtr d3dTexture,
            bool forceIdr,
            out MoonshineEncodedPacketDesc desc,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            if (!IsActive)
            {
                desc = default;
                bytesWritten = 0;
                return false;
            }

            if (forceIdr)
            {
                ForceIdrCallCount++;
            }

            byte[] syntheticNalu = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xAF, 0xFE };
            syntheticNalu.CopyTo(outBitstream);
            bytesWritten = syntheticNalu.Length;

            desc = new MoonshineEncodedPacketDesc
            {
                PayloadSize = (uint)bytesWritten,
                IsKeyframe = (byte)(forceIdr ? 1 : 0),
                FrameIndex = 0,
                TimestampQpc = Stopwatch.GetTimestamp(),
                IsHeaderPacket = 0,
                TemporalId = 0,
                Reserved = 0
            };
            return true;
        }

        public bool Reconfigure(uint bitrateKbps, uint fps, uint peakBitrateKbps = 0)
        {
            BitrateKbps = bitrateKbps;
            ReconfigureCallCount++;
            return true;
        }

        public void RequestKeyframe()
        {
            ForceIdrCallCount++;
        }

        public void Dispose()
        {
            IsActive = false;
        }
    }

    [Fact]
    public async Task HostStreamingSession_RealClientSession_StreamsActualMediaPackets()
    {
        int clientVideoPort = 57000 + Random.Shared.Next(0, 500);
        int clientAudioPort = 57500 + Random.Shared.Next(0, 500);
        int clientControlPort = 58000 + Random.Shared.Next(0, 500);

        using var clientVideoSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        clientVideoSocket.Bind(new IPEndPoint(IPAddress.Loopback, clientVideoPort));

        using var clientAudioSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        clientAudioSocket.Bind(new IPEndPoint(IPAddress.Loopback, clientAudioPort));

        var capture = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline();
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        var config = new HostSessionConfig
        {
            ClientAddress = IPAddress.Loopback,
            ClientVideoPort = clientVideoPort,
            ClientAudioPort = clientAudioPort,
            ClientControlFeedbackPort = clientControlPort,
            Fps = 60,
            BitrateKbps = 15000,
            EnableFec = true,
            FecDataShards = 10,
            FecParityShards = 2
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder);

        session.State.Should().Be(HostSessionState.Created);

        await session.StartAsync();
        session.State.Should().Be(HostSessionState.Streaming);
        session.IsStreaming.Should().BeTrue();

        // 1. Verify Client receives actual video datagrams
        byte[] videoPacketBuffer = new byte[2048];
        var remoteVideoEp = new IPEndPoint(IPAddress.Any, 0);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        SocketReceiveFromResult result = await clientVideoSocket.ReceiveFromAsync(
            videoPacketBuffer.AsMemory(),
            SocketFlags.None,
            remoteVideoEp,
            cts.Token);

        result.ReceivedBytes.Should().BeGreaterThanOrEqualTo(MoonshineVideoPacketiser.TotalHeaderOverhead);

        // Verify MSHN magic header
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(videoPacketBuffer.AsSpan(0, 4));
        magic.Should().Be(MoonshineProtocolConstants.Magic);

        // Verify Video packet header
        bool parsed = MoonshineVideoPacketCodec.TryReadHeader(
            videoPacketBuffer.AsSpan(32, 32),
            out MoonshineVideoPacketHeader videoHeader);
        parsed.Should().BeTrue();
        videoHeader.StreamId.Should().Be(config.StreamId);

        // 2. Verify Input Injection
        var mousePayload = new MoonshineInputMousePayload
        {
            X = 10,
            Y = 20,
            IsAbsolute = 0
        };
        byte[] inputDatagram = new byte[32 + 20];
        var inputHeader = new MoonshinePacketHeader(
            MoonshineProtocolConstants.Magic,
            1,
            MoonshineMessageType.InputMouse,
            20,
            1,
            1,
            config.SessionId);
        MoonshineProtocolCodec.TryWriteHeader(inputHeader, inputDatagram);
        MoonshineProtocolCodec.TryWriteMouseInput(mousePayload, inputDatagram.AsSpan(32));
        bool inputResult = session.ProcessInputDatagram(inputDatagram);
        inputResult.Should().BeTrue();
        session.Metrics.TotalInputPacketsProcessed.Should().Be(1);

        // 3. Verify Keyframe Request
        session.RequestKeyframe();
        await Task.Delay(100);
        session.Metrics.KeyframesRequested.Should().BeGreaterThan(0);

        // 4. Verify Metrics and Latency Tracking
        HostSessionMetrics metrics = session.Metrics;
        metrics.TotalFramesCaptured.Should().BeGreaterThan(0);
        metrics.TotalFramesEncoded.Should().BeGreaterThan(0);
        metrics.TotalPacketsSent.Should().BeGreaterThan(0);
        metrics.TotalBytesSent.Should().BeGreaterThan(0);
        metrics.AverageCaptureToNetworkLatencyUs.Should().BeGreaterThanOrEqualTo(0.0);

        // 5. Clean teardown
        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task HostStreamingSession_RefusesStreaming_WhenCaptureUnavailable()
    {
        var capture = new TestDesktopCapturePipeline { IsAvailable = false };
        var encoderPipeline = new TestVideoEncoderPipeline();
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        await using var session = new MoonshineHostStreamingSession(
            capturePipeline: capture,
            encoderEngine: encoder);

        Func<Task> act = async () => await session.StartAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Desktop capture pipeline is unavailable*");

        session.State.Should().Be(HostSessionState.Faulted);
        session.LastError.Should().Contain("Desktop capture pipeline is unavailable");
    }

    [Fact]
    public async Task HostStreamingSession_RefusesStreaming_WhenEncoderInactive()
    {
        var capture = new TestDesktopCapturePipeline { IsAvailable = true };
        var encoderPipeline = new TestVideoEncoderPipeline { IsActive = false };
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        await using var session = new MoonshineHostStreamingSession(
            capturePipeline: capture,
            encoderEngine: encoder);

        Func<Task> act = async () => await session.StartAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Hardware video encoder is unavailable*");

        session.State.Should().Be(HostSessionState.Faulted);
        session.LastError.Should().Contain("Hardware video encoder is unavailable");
    }

    [Fact]
    public async Task HostStreamingSession_RepeatedSessions_DoNotLeakResources()
    {
        for (int i = 0; i < 5; i++)
        {
            var capture = new TestDesktopCapturePipeline();
            var encoderPipeline = new TestVideoEncoderPipeline();
            using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

            var config = new HostSessionConfig
            {
                ClientVideoPort = 58500 + i,
                ClientAudioPort = 58600 + i,
                ClientControlFeedbackPort = 58700 + i,
                LocalVideoPort = 58800 + i,
                LocalAudioPort = 58900 + i,
                LocalControlFeedbackPort = 59000 + i
            };

            await using var session = new MoonshineHostStreamingSession(
                config: config,
                capturePipeline: capture,
                encoderEngine: encoder);

            await session.StartAsync();
            session.State.Should().Be(HostSessionState.Streaming);

            await Task.Delay(50);

            await session.StopAsync();
            session.State.Should().Be(HostSessionState.Terminated);
        }
    }

    [Fact]
    public async Task MoonshineHostCoordinator_SessionLifecycle_TracksActiveSessions()
    {
        using var coordinator = new MoonshineHostCoordinator(
            endpointConfig: HostEndpointConfig.Ephemeral);

        await coordinator.StartAsync();
        coordinator.State.Should().Be(HostState.Running);
        coordinator.ActiveSessions.Should().BeEmpty();

        var capture = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline();
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        var sessionConfig = new HostSessionConfig
        {
            LocalVideoPort = 59100 + Random.Shared.Next(0, 100),
            LocalAudioPort = 59200 + Random.Shared.Next(0, 100),
            LocalControlFeedbackPort = 59300 + Random.Shared.Next(0, 100)
        };

        MoonshineHostStreamingSession session = await coordinator.CreateAndStartSessionAsync(
            sessionConfig: sessionConfig,
            capturePipeline: capture,
            encoderEngine: encoder);

        session.State.Should().Be(HostSessionState.Streaming);
        coordinator.ActiveSessions.Should().HaveCount(1);
        coordinator.GetStatus().ActiveSessionCount.Should().Be(1);

        await coordinator.StopSessionAsync(session);
        coordinator.ActiveSessions.Should().BeEmpty();
        coordinator.GetStatus().ActiveSessionCount.Should().Be(0);

        await coordinator.StopAsync();
        coordinator.State.Should().Be(HostState.Disabled);
    }
}
