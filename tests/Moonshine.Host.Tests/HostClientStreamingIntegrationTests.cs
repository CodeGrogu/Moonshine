using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Moonshine.Core.Audio;
using Moonshine.Core.Media;
using Moonshine.Core.Session;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
using Moonshine.Host.Encoding;
using Moonshine.Host.Input;
using Moonshine.Host.Session;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Host.Tests;

public class HostClientStreamingIntegrationTests
{
    private sealed class MockDesktopCapturePipeline : IDesktopCapturePipeline
    {
        public uint Width => 1920;
        public uint Height => 1080;
        public uint Format => 28; // DXGI_FORMAT_R8G8B8A8_UNORM
        public bool IsHdr => false;
        public uint AdapterIndex => 0;
        public uint OutputIndex => 0;
        public bool IsAvailable => true;
        public CaptureSourceDescriptor? Source => null;
        public CaptureMetrics Metrics => new(0, 0, 0, 0, 1920, 1080, 28, false, 0.0);

        public unsafe bool TryAcquireNextFrame(uint timeoutMs, out MoonshineCaptureFrameDesc frame)
        {
            frame = new MoonshineCaptureFrameDesc
            {
                TextureHandle = (void*)0x2000,
                Width = 1920,
                Height = 1080,
                Format = 28,
                TimestampQpc = (ulong)Stopwatch.GetTimestamp()
            };
            return true;
        }

        public void ReleaseFrame() { }
        public bool TryRecover() => true;
        public bool TryReconfigureSource(CaptureSourceDescriptor source) => true;
        public void Dispose() { }
    }

    private sealed class MockVideoEncoderPipeline : IVideoEncoderPipeline
    {
        public uint Width => 1920;
        public uint Height => 1080;
        public uint Fps => 60;
        public uint BitrateKbps { get; private set; } = 20000;
        public VideoCodec Codec => VideoCodec.HevcMain10;
        public EncoderVendor Vendor => EncoderVendor.Direct3D11Hardware;
        public bool IsActive => true;
        public double AverageEncodingLatencyMicroseconds => 200.0;

        private uint _frameIndex;
        public int ForceIdrCount { get; private set; }

        public bool TryEncodeFrame(
            IntPtr d3dTexture,
            bool forceIdr,
            out MoonshineEncodedPacketDesc desc,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            if (forceIdr) ForceIdrCount++;
            uint idx = Interlocked.Increment(ref _frameIndex);
            bool isKey = forceIdr || idx == 1;

            int payloadSize = 2500; // 3 UDP MTU packets
            outBitstream[..payloadSize].Fill((byte)(isKey ? 0xAA : 0xBB));
            bytesWritten = payloadSize;

            desc = new MoonshineEncodedPacketDesc
            {
                FrameIndex = idx,
                PayloadSize = (uint)payloadSize,
                IsKeyframe = isKey ? (byte)1 : (byte)0,
                IsHeaderPacket = 0,
                TemporalId = 0,
                Reserved = 0,
                TimestampQpc = Stopwatch.GetTimestamp()
            };
            return true;
        }

        public bool Reconfigure(uint bitrateKbps, uint fps, uint peakBitrateKbps = 0)
        {
            BitrateKbps = bitrateKbps;
            return true;
        }

        public void RequestKeyframe()
        {
            ForceIdrCount++;
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task HostClientStreaming_EndToEnd_RealLoopbackTransmission_CompletesVideoAndAudioFrames()
    {
        ulong sessionId = 0xABCD112233445566UL;

        // 1. Configure and launch MoonshineHostStreamingSession on ephemeral ports
        var hostConfig = new HostSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            EnableFec = false,
            MtuPayloadSize = 1000,
            ClientAddress = IPAddress.Loopback,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0
        };

        using var mockCapture = new MockDesktopCapturePipeline();
        using var mockEncoder = new MockVideoEncoderPipeline();
        using var hostInput = new MoonshineHostInputPipeline(config: new HostInputConfig { ExpectedSessionId = sessionId });
        using var hostAudio = new MoonshineHostAudioPipeline(sampleRate: 48000, topology: AudioChannelTopology.Stereo, bitrate: 128000, frameDurationMs: 5);

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: mockCapture,
            encoderEngine: new UnifiedHardwareEncoderEngine(mockEncoder),
            audioPipeline: hostAudio,
            inputPipeline: hostInput);

        await hostSession.StartAsync();
        hostSession.IsStreaming.Should().BeTrue();
        int hostVideoPort = hostSession.BoundLocalVideoPort;
        int hostAudioPort = hostSession.BoundLocalAudioPort;
        int hostControlPort = hostSession.BoundLocalControlPort;

        // 2. Configure and launch MoonshineClientStreamingSession on ephemeral ports pointing to host
        var clientConfig = new ClientSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            HostAddress = IPAddress.Loopback,
            HostVideoPort = hostVideoPort,
            HostAudioPort = hostAudioPort,
            HostControlFeedbackPort = hostControlPort,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            EnableFec = false,
            MtuPayloadSize = 1000
        };

        var completedVideoFrames = new List<uint>();
        await using var clientSession = new MoonshineClientStreamingSession(clientConfig);
        clientSession.OnVideoFrameReassembled = frame =>
        {
            lock (completedVideoFrames)
            {
                completedVideoFrames.Add(frame.FrameIndex);
            }
        };

        await clientSession.StartAsync();
        clientSession.IsStreaming.Should().BeTrue();
        int clientVideoPort = clientSession.BoundLocalVideoPort;
        int clientAudioPort = clientSession.BoundLocalAudioPort;
        int clientControlPort = clientSession.BoundLocalControlPort;

        // Connect host destination endpoints to client bound ports
        hostSession.SetClientEndpoints(
            videoEp: new IPEndPoint(IPAddress.Loopback, clientVideoPort),
            audioEp: new IPEndPoint(IPAddress.Loopback, clientAudioPort),
            controlEp: new IPEndPoint(IPAddress.Loopback, clientControlPort));

        // 3. Inject audio PCM frames on host
        float[] pcmFrame = new float[240 * 2]; // 5ms @ 48kHz stereo
        for (int i = 0; i < 10; i++)
        {
            hostAudio.ProcessPcmFrame(pcmFrame, hostSession.SendAudioPacket);
        }

        // 4. Send Client Input back to Host
        bool keySent = clientSession.SendKeyboardInput(keyCode: 0x57, scanCode: 0x11, isDown: true, modifiers: 0); // 'W' key
        keySent.Should().BeTrue();

        bool mouseSent = clientSession.SendMouseInput(x: 100, y: 50, wheelY: 0, buttonFlags: 0);
        mouseSent.Should().BeTrue();

        // 5. Trigger IDR keyframe request from client
        clientSession.RequestIdrKeyframe(reasonCode: 1);

        // Wait for streaming transmission and loopback receipt
        for (int i = 0; i < 100; i++)
        {
            if (clientSession.Metrics.TotalVideoFramesCompleted >= 5 &&
                clientSession.Metrics.TotalAudioPacketsReceived >= 5 &&
                hostSession.Metrics.TotalInputPacketsProcessed >= 2 &&
                hostSession.Metrics.KeyframesRequested >= 1)
            {
                break;
            }
            await Task.Delay(25);
        }

        // 6. Assert End-to-End Delivery & State Invariants
        hostSession.Metrics.TotalFramesCaptured.Should().BeGreaterThan(0);
        hostSession.Metrics.TotalFramesEncoded.Should().BeGreaterThan(0);
        hostSession.Metrics.TotalPacketsSent.Should().BeGreaterThan(0);
        hostSession.Metrics.TotalAudioPacketsSent.Should().BeGreaterThan(0);
        hostSession.Metrics.TotalInputPacketsProcessed.Should().BeGreaterThanOrEqualTo(2);
        hostSession.Metrics.KeyframesRequested.Should().BeGreaterThanOrEqualTo(1);

        clientSession.Metrics.TotalVideoPacketsReceived.Should().BeGreaterThan(0);
        clientSession.Metrics.TotalVideoFramesCompleted.Should().BeGreaterThan(0);
        clientSession.Metrics.TotalAudioPacketsReceived.Should().BeGreaterThan(0);
        clientSession.Metrics.TotalInputPacketsSent.Should().BeGreaterThanOrEqualTo(2);

        lock (completedVideoFrames)
        {
            completedVideoFrames.Should().NotBeEmpty();
        }

        // 7. Clean Teardown
        await clientSession.StopAsync();
        clientSession.State.Should().Be(ClientSessionState.Closed);

        await hostSession.StopAsync();
        hostSession.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task HostClientStreaming_MicrophoneBackchannel_EndToEndTransmission_Succeeds()
    {
        ulong sessionId = 0x9988776655443322UL;

        // 1. Configure HostSession with microphone backchannel enabled on ephemeral port
        var hostConfig = new HostSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            EnableFec = false,
            MtuPayloadSize = 1000,
            ClientAddress = IPAddress.Loopback,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            LocalMicPort = 0,
            EnableMicrophoneBackchannel = true
        };

        using var mockCapture = new MockDesktopCapturePipeline();
        using var mockEncoder = new MockVideoEncoderPipeline();
        using var hostInput = new MoonshineHostInputPipeline(config: new HostInputConfig { ExpectedSessionId = sessionId });
        using var hostAudio = new MoonshineHostAudioPipeline(sampleRate: 48000, topology: AudioChannelTopology.Stereo, bitrate: 128000, frameDurationMs: 5);

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: mockCapture,
            encoderEngine: new UnifiedHardwareEncoderEngine(mockEncoder),
            audioPipeline: hostAudio,
            inputPipeline: hostInput);

        await hostSession.StartAsync();
        hostSession.IsStreaming.Should().BeTrue();
        int hostVideoPort = hostSession.BoundLocalVideoPort;
        int hostAudioPort = hostSession.BoundLocalAudioPort;
        int hostControlPort = hostSession.BoundLocalControlPort;
        int hostMicPort = hostSession.BoundLocalMicPort;
        hostMicPort.Should().BeGreaterThan(0);

        // 2. Configure ClientSession with microphone uplink enabled
        var clientConfig = new ClientSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            HostAddress = IPAddress.Loopback,
            HostVideoPort = hostVideoPort,
            HostAudioPort = hostAudioPort,
            HostControlFeedbackPort = hostControlPort,
            HostMicPort = hostMicPort,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            LocalMicPort = 0,
            EnableFec = false,
            MtuPayloadSize = 1000,
            EnableMicrophoneUplink = true
        };

        await using var clientSession = new MoonshineClientStreamingSession(clientConfig);
        await clientSession.StartAsync();
        clientSession.IsStreaming.Should().BeTrue();
        int clientVideoPort = clientSession.BoundLocalVideoPort;
        int clientAudioPort = clientSession.BoundLocalAudioPort;
        int clientControlPort = clientSession.BoundLocalControlPort;

        hostSession.SetClientEndpoints(
            videoEp: new IPEndPoint(IPAddress.Loopback, clientVideoPort),
            audioEp: new IPEndPoint(IPAddress.Loopback, clientAudioPort),
            controlEp: new IPEndPoint(IPAddress.Loopback, clientControlPort));

        // 3. Generate client mic PCM samples (480 samples = 10ms @ 48kHz mono)
        float[] micPcm = new float[480];
        for (int i = 0; i < micPcm.Length; i++)
        {
            micPcm[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0) * 0.5f;
        }

        // Test client controls
        clientSession.SetMicrophoneGain(1.2f);
        clientSession.SetMicrophoneMute(false);

        // Send multiple mic frames
        for (int i = 0; i < 5; i++)
        {
            bool sent = clientSession.TrySendMicrophoneFrame(micPcm);
            sent.Should().BeTrue();
            await Task.Delay(10);
        }

        // Wait for UDP reception and jitter buffering on host
        for (int i = 0; i < 50; i++)
        {
            HostMicSinkMetrics? metrics = hostSession.GetMicrophoneMetrics();
            if (metrics.HasValue && metrics.Value.TotalPacketsReceived >= 1)
            {
                break;
            }
            await Task.Delay(20);
        }

        // 4. Assert Host received microphone packets
        HostMicSinkMetrics? hostMicMetrics = hostSession.GetMicrophoneMetrics();
        hostMicMetrics.Should().NotBeNull();
        hostMicMetrics.Value.TotalPacketsReceived.Should().BeGreaterThan(0);

        // 5. Clean Teardown
        await clientSession.StopAsync();
        await hostSession.StopAsync();
    }
}
