using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Moonshine.Core.Media;
using Moonshine.Core.Network;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
using Moonshine.Host.Control;
using Moonshine.Host.Encoding;
using Moonshine.Host.Input;
using Moonshine.Host.Session;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Control;
using Moonshine.Protocol.Feedback;
using Moonshine.Protocol.Video;
using Xunit;
using MoonshineErrorCode = Moonshine.Protocol.Contracts.MoonshineErrorCode;

namespace Moonshine.Host.Tests;

[Collection("HardwareExclusive")]
public class HostStreamingSessionTests
{
    internal sealed class TestDesktopCapturePipeline : IDesktopCapturePipeline
    {
        public uint Width => 1920;
        public uint Height => 1080;
        public uint Format => 28; // DXGI_FORMAT_R8G8B8A8_UNORM
        public bool IsHdr => false;
        public uint AdapterIndex => 0;
        public uint OutputIndex => 0;
        public bool IsAvailable { get; set; } = true;
        public CaptureSourceDescriptor? Source { get; set; }
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

        public bool TryReconfigureSource(CaptureSourceDescriptor source)
        {
            Source = source;
            return true;
        }

        public void Dispose()
        {
            IsAvailable = false;
        }
    }

    internal sealed class TestVideoEncoderPipeline : IVideoEncoderPipeline
    {
        public uint Width => 1920;
        public uint Height => 1080;
        public uint Fps => 60;
        public uint BitrateKbps { get; private set; } = 20000;
        public VideoCodec Codec => VideoCodec.HevcMain10;
        public EncoderVendor Vendor => EncoderVendor.Direct3D11Hardware;
        public bool IsActive { get; set; } = true;
        public EncoderImplementationKind ImplementationKind { get; set; } = EncoderImplementationKind.SyntheticTest;
        public bool IsHardwareAccelerated { get; set; }
        public bool HasProducedValidOutput { get; set; } = true;
        public Type ImplementationType => GetType();
        public EncoderRuntimeState RuntimeState => IsActive ? EncoderRuntimeState.Ready : EncoderRuntimeState.Disposed;
        private ulong _lastDecoderAcceptedFrameId;
        public EncoderEvidence Evidence
        {
            get
            {
                ulong lastValid = 1;
                ulong lastAccepted = _lastDecoderAcceptedFrameId;
                bool latestMatch = lastAccepted != 0 && lastAccepted == lastValid;
                bool healthy = IsActive && lastAccepted != 0 && lastAccepted <= lastValid && (lastValid - lastAccepted) <= HardwareVideoEncoderPipeline.DecoderAcceptanceLagWindow;

                return new EncoderEvidence(
                    ApiAvailable: true,
                    HardwareSupported: IsHardwareAccelerated,
                    SessionInitialised: IsActive,
                    FrameSubmitted: true,
                    OutputReceived: HasProducedValidOutput,
                    BitstreamStructurallyValid: HasProducedValidOutput,
                    AccessUnitValid: HasProducedValidOutput,
                    DecoderAccepted: healthy,
                    FirstValidFrameId: 1,
                    LastValidFrameId: lastValid,
                    LastDecoderAcceptedFrameId: lastAccepted,
                    DecoderAcceptedLatestFrame: latestMatch,
                    DecoderAcceptanceHealthy: healthy
                );
            }
        }
        public double AverageEncodingLatencyMicroseconds => 250.0;

        public int ForceIdrCallCount { get; private set; }
        public int ReconfigureCallCount { get; private set; }

        public bool TryEncodeFrame(
            IntPtr d3dTexture,
            ulong frameId,
            ulong timestampUs,
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
                FrameIndex = frameId,
                TimestampQpc = timestampUs > 0 ? (long)timestampUs : Stopwatch.GetTimestamp(),
                IsHeaderPacket = 0,
                TemporalId = 0,
                Reserved = 0
            };
            return true;
        }

        public bool TryEncodeFrame(
            IntPtr d3dTexture,
            bool forceIdr,
            out MoonshineEncodedPacketDesc desc,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            return TryEncodeFrame(d3dTexture, 0, (ulong)Stopwatch.GetTimestamp(), forceIdr, out desc, outBitstream, out bytesWritten);
        }

        public void RecordDecoderAcceptance(ulong frameId)
        {
            _lastDecoderAcceptedFrameId = frameId;
        }

        public EncodeSubmissionResult SubmitFrame(
            IntPtr d3dTexture,
            ulong frameId,
            ulong timestampUs,
            bool forceIdr,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            if (!TryEncodeFrame(d3dTexture, forceIdr, out var desc, outBitstream, out bytesWritten))
            {
                return new EncodeSubmissionResult(
                    Submitted: false,
                    OutputAvailable: false,
                    KeyFrame: false,
                    BytesWritten: 0,
                    PacketDesc: default,
                    Result: IsActive ? EncoderResult.EncoderFailure : EncoderResult.DeviceLost
                );
            }

            desc.FrameIndex = frameId;
            if (timestampUs > 0)
            {
                desc.TimestampQpc = (long)timestampUs;
            }

            return new EncodeSubmissionResult(
                Submitted: true,
                OutputAvailable: true,
                KeyFrame: desc.IsKeyframe != 0,
                BytesWritten: bytesWritten,
                PacketDesc: desc,
                Result: EncoderResult.Success
            );
        }

        public EncodeSubmissionResult SubmitFrame(
            IntPtr d3dTexture,
            bool forceIdr,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            return SubmitFrame(d3dTexture, 0, (ulong)Stopwatch.GetTimestamp(), forceIdr, outBitstream, out bytesWritten);
        }

        public bool TryPollPacket(
            Span<byte> outBitstream,
            out MoonshineEncodedPacketDesc desc,
            out int bytesWritten)
        {
            desc = default;
            bytesWritten = 0;
            return false;
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

        public bool TryRecoverDevice(IntPtr newD3dDevice) => true;

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

    [Fact]
    public async Task HostStreamingSession_NativeFeedbackDatagram_AdaptsBitrate()
    {
        var capture = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline();
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        ushort basePort = (ushort)(59400 + Random.Shared.Next(0, 50) * 6);
        var config = new HostSessionConfig
        {
            BitrateKbps = 50000,
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder);

        await session.StartAsync();
        session.State.Should().Be(HostSessionState.Streaming);

        // Send Moonshine-native FeedbackLossStats packet indicating 10% packet loss
        using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var feedbackPayload = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = config.StreamId,
            PacketsReceived = 900,
            PacketsLost = 100,
            PacketsRecoveredFec = 0,
            RoundTripTimeUs = 15000,
            JitterUs = 500,
            EstimatedBandwidthKbps = 35000
        };

        byte[] packetBuffer = new byte[MoonshineFeedbackCodec.LossStatsPacketSize];
        MoonshineFeedbackCodec.TryWriteLossStats(in feedbackPayload, packetBuffer, out int written, sessionId: config.SessionId);

        clientSocket.SendTo(packetBuffer, 0, written, SocketFlags.None, new IPEndPoint(IPAddress.Loopback, config.LocalControlFeedbackPort));

        // Allow feedback loop to process
        await Task.Delay(100);

        // Encoder bitrate should have been reconfigured down due to severe packet loss
        encoderPipeline.ReconfigureCallCount.Should().BeGreaterThan(0);
        encoderPipeline.BitrateKbps.Should().BeLessThan(50000);

        await session.StopAsync();
    }

    [Fact]
    public async Task HostStreamingSession_NativeIdrRequest_TriggersKeyframe()
    {
        var capture = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline();
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        ushort basePort = GetAvailablePortBlock(6);
        var config = new HostSessionConfig
        {
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder);

        await session.StartAsync();
        session.State.Should().Be(HostSessionState.Streaming);

        // Send Moonshine-native IdrRequest packet
        using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var idrPayload = new MoonshineIdrRequestPayload
        {
            StreamId = config.StreamId,
            LastValidFrameIndex = 100,
            ReasonCode = 1
        };

        byte[] packetBuffer = new byte[MoonshineFeedbackCodec.IdrRequestPacketSize];
        MoonshineFeedbackCodec.TryWriteIdrRequest(in idrPayload, packetBuffer, out int written, sessionId: config.SessionId);

        clientSocket.SendTo(packetBuffer, 0, written, SocketFlags.None, new IPEndPoint(IPAddress.Loopback, config.LocalControlFeedbackPort));

        // Allow feedback loop and video frame loop to process
        await Task.Delay(100);

        // Hardware encoder should have received force IDR request
        encoderPipeline.ForceIdrCallCount.Should().BeGreaterThan(0);

        await session.StopAsync();
    }

    [Fact]
    public async Task HostStreamingSession_MicrophoneBackchannel_ControlsGainMuteAndReportsMetrics()
    {
        var capture = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline();
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        ushort basePort = (ushort)(59800 + Random.Shared.Next(0, 50) * 8);
        var config = new HostSessionConfig
        {
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            LocalMicPort = (ushort)(basePort + 3),
            ClientVideoPort = (ushort)(basePort + 4),
            ClientAudioPort = (ushort)(basePort + 5),
            ClientControlFeedbackPort = (ushort)(basePort + 6),
            EnableMicrophoneBackchannel = true
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder);

        await session.StartAsync();
        session.State.Should().Be(HostSessionState.Streaming);
        session.BoundLocalMicPort.Should().Be(config.LocalMicPort);

        // Test microphone controls
        session.SetMicrophoneGain(1.5f);
        session.SetMicrophoneMute(true);
        session.SetMicrophoneMute(false);

        HostMicSinkMetrics? metrics = session.GetMicrophoneMetrics();
        metrics.Should().NotBeNull();
        session.Metrics.MicMetrics.Should().NotBeNull();

        await session.StopAsync();
    }

    internal sealed class TestDisplayTopologyWatcher : IDisplayTopologyWatcher
    {
        private DisplayTopology _currentTopology;

#pragma warning disable CS0067
        public event EventHandler<DisplayTopologyChangedEventArgs>? TopologyChanged;
#pragma warning restore CS0067

        public DisplayTopology CurrentTopology => Volatile.Read(ref _currentTopology);

        public TestDisplayTopologyWatcher(DisplayTopology initialTopology)
        {
            _currentTopology = initialTopology;
        }

        public void SetTopology(DisplayTopology newTopology)
        {
            Volatile.Write(ref _currentTopology, newTopology);
        }

        public void Refresh()
        {
        }

        public void Dispose()
        {
        }
    }

    private static DisplayTopology CreateAttachedTopology()
    {
        var display = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8);

        var adapter = new DisplayAdapterInfo(
            AdapterIndex: 0,
            AdapterLuid: 0x1000,
            Description: "Test Video Adapter",
            DedicatedVideoMemoryBytes: 8_000_000_000,
            IsHardware: true);

        return new DisplayTopology(
            Adapters: new[] { adapter },
            Displays: new[] { display },
            PrimaryDisplay: display,
            VirtualScreenBounds: new DesktopBounds(0, 0, 1920, 1080),
            IsHeadless: false,
            TimestampQpc: 0);
    }

    private static DisplayTopology CreateHeadlessTopology()
    {
        return new DisplayTopology(
            Adapters: Array.Empty<DisplayAdapterInfo>(),
            Displays: Array.Empty<DisplayOutputInfo>(),
            PrimaryDisplay: null,
            VirtualScreenBounds: DesktopBounds.Empty,
            IsHeadless: true,
            TimestampQpc: 0);
    }

    [Fact]
    public async Task HostStreamingSession_GetLiveBackendReadiness_VideoEncoder_SemanticRules()
    {
        var capture = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline
        {
            IsActive = true,
            ImplementationKind = EncoderImplementationKind.HardwareAccelerated,
            IsHardwareAccelerated = true,
            HasProducedValidOutput = true
        };
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        ushort basePort = (ushort)(60200 + Random.Shared.Next(0, 50) * 8);
        var config = new HostSessionConfig
        {
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder);

        // Not streaming: encoder is active, reports Available
        session.GetLiveBackendReadiness().VideoEncoder.Should().Be(ComponentReadiness.Available);

        await session.StartAsync();
        session.IsStreaming.Should().BeTrue();

        // Streaming: encoder active reports Operational
        session.GetLiveBackendReadiness().VideoEncoder.Should().Be(ComponentReadiness.Operational);

        // Encoder becomes inactive while streaming: reports Faulted
        encoderPipeline.IsActive = false;
        session.GetLiveBackendReadiness().VideoEncoder.Should().Be(ComponentReadiness.Faulted);

        await session.StopAsync();
    }

    [Fact]
    public async Task SyntheticEncoder_NeverReportsOperational()
    {
        var capture = new TestDesktopCapturePipeline { IsAvailable = true };
        var encoderPipeline = new TestVideoEncoderPipeline
        {
            IsActive = true,
            ImplementationKind = EncoderImplementationKind.SyntheticTest,
            IsHardwareAccelerated = false,
            HasProducedValidOutput = true
        };
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        ushort basePort = (ushort)(60400 + Random.Shared.Next(0, 50) * 8);
        var config = new HostSessionConfig
        {
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder);

        // Not streaming: reports Available
        session.GetLiveBackendReadiness().VideoEncoder.Should().Be(ComponentReadiness.Available);

        await session.StartAsync();
        session.IsStreaming.Should().BeTrue();

        // Streaming: synthetic test encoder must report Available, NEVER Operational
        session.GetLiveBackendReadiness().VideoEncoder.Should().Be(ComponentReadiness.Available);

        await session.StopAsync();
    }

    [Fact]
    public async Task HardwareAcceleratedEncoder_ReportsOperational_OnlyWhenValidOutputProduced()
    {
        var capture = new TestDesktopCapturePipeline { IsAvailable = true };
        var encoderPipeline = new TestVideoEncoderPipeline
        {
            IsActive = true,
            ImplementationKind = EncoderImplementationKind.HardwareAccelerated,
            IsHardwareAccelerated = true,
            HasProducedValidOutput = false
        };
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        ushort basePort = (ushort)(60600 + Random.Shared.Next(0, 50) * 8);
        var config = new HostSessionConfig
        {
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder);

        // Not streaming: reports Available
        session.GetLiveBackendReadiness().VideoEncoder.Should().Be(ComponentReadiness.Available);

        await session.StartAsync();
        session.IsStreaming.Should().BeTrue();

        // Streaming but before valid output produced: reports Available
        session.GetLiveBackendReadiness().VideoEncoder.Should().Be(ComponentReadiness.Available);

        // Once valid output is produced: transitions to Operational
        encoderPipeline.HasProducedValidOutput = true;
        session.GetLiveBackendReadiness().VideoEncoder.Should().Be(ComponentReadiness.Operational);

        await session.StopAsync();
    }

    [Fact]
    public async Task HostStreamingSession_GetLiveBackendReadiness_DesktopCapture_SemanticRules()
    {
        var capture = new TestDesktopCapturePipeline { IsAvailable = true };
        var encoderPipeline = new TestVideoEncoderPipeline { IsActive = true };
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        var headlessTopology = CreateHeadlessTopology();
        var attachedTopology = CreateAttachedTopology();

        using var headlessWatcher = new TestDisplayTopologyWatcher(headlessTopology);
        using var attachedWatcher = new TestDisplayTopologyWatcher(attachedTopology);

        ushort basePort = (ushort)(60600 + Random.Shared.Next(0, 50) * 8);
        var config = new HostSessionConfig
        {
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        // 1. Headless before streaming reports Unsupported
        await using (var headlessSession = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder,
            topologyWatcher: headlessWatcher))
        {
            headlessSession.GetLiveBackendReadiness().DesktopCapture.Should().Be(ComponentReadiness.Unsupported);
        }

        // 2. Attached display before streaming reports Available
        await using (var attachedSession = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder,
            topologyWatcher: attachedWatcher))
        {
            attachedSession.GetLiveBackendReadiness().DesktopCapture.Should().Be(ComponentReadiness.Available);

            await attachedSession.StartAsync();
            attachedSession.IsStreaming.Should().BeTrue();

            // 3. Streaming reports Operational
            attachedSession.GetLiveBackendReadiness().DesktopCapture.Should().Be(ComponentReadiness.Operational);

            await attachedSession.StopAsync();
        }
    }

    [Fact]
    public async Task HostStreamingSession_GetLiveBackendReadiness_AudioLoopback_SemanticRules()
    {
        var capture = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline();
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        ushort basePort = (ushort)(61000 + Random.Shared.Next(0, 50) * 8);

        // 1. AudioTopology.None always reports Unsupported
        var disabledAudioConfig = new HostSessionConfig
        {
            AudioTopology = AudioChannelTopology.None,
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        await using (var disabledSession = new MoonshineHostStreamingSession(
            config: disabledAudioConfig,
            capturePipeline: capture,
            encoderEngine: encoder))
        {
            disabledSession.GetLiveBackendReadiness().AudioLoopback.Should().Be(ComponentReadiness.Unsupported);
            await disabledSession.StartAsync();
            disabledSession.GetLiveBackendReadiness().AudioLoopback.Should().Be(ComponentReadiness.Unsupported);
            await disabledSession.StopAsync();
        }

        // 2. AudioTopology.Stereo reports Available/Unsupported when not streaming, Operational when streaming
        ushort basePort2 = (ushort)(61400 + Random.Shared.Next(0, 50) * 8);
        var enabledAudioConfig = new HostSessionConfig
        {
            AudioTopology = AudioChannelTopology.Stereo,
            LocalVideoPort = basePort2,
            LocalAudioPort = (ushort)(basePort2 + 1),
            LocalControlFeedbackPort = (ushort)(basePort2 + 2),
            ClientVideoPort = (ushort)(basePort2 + 3),
            ClientAudioPort = (ushort)(basePort2 + 4),
            ClientControlFeedbackPort = (ushort)(basePort2 + 5)
        };

        await using (var enabledSession = new MoonshineHostStreamingSession(
            config: enabledAudioConfig,
            capturePipeline: capture,
            encoderEngine: encoder))
        {
            ComponentReadiness expectedNonStreaming = HostCapabilityProbeEngine.HasActiveRenderEndpoint()
                ? ComponentReadiness.Available
                : ComponentReadiness.Unsupported;
            enabledSession.GetLiveBackendReadiness().AudioLoopback.Should().Be(expectedNonStreaming);

            await enabledSession.StartAsync();
            enabledSession.GetLiveBackendReadiness().AudioLoopback.Should().Be(ComponentReadiness.Operational);

            await enabledSession.StopAsync();
        }
    }

    [Fact]
    public async Task HostStreamingSession_GetLiveBackendReadiness_MicrophoneBackchannel_SemanticRules()
    {
        var capture = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline();
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        ushort basePort = (ushort)(61800 + Random.Shared.Next(0, 50) * 8);

        // 1. EnableMicrophoneBackchannel = false reports Unsupported
        var disabledMicConfig = new HostSessionConfig
        {
            EnableMicrophoneBackchannel = false,
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        await using (var disabledSession = new MoonshineHostStreamingSession(
            config: disabledMicConfig,
            capturePipeline: capture,
            encoderEngine: encoder))
        {
            disabledSession.GetLiveBackendReadiness().MicrophoneBackchannel.Should().Be(ComponentReadiness.Unsupported);
            await disabledSession.StartAsync();
            disabledSession.GetLiveBackendReadiness().MicrophoneBackchannel.Should().Be(ComponentReadiness.Unsupported);
            await disabledSession.StopAsync();
        }

        // 2. EnableMicrophoneBackchannel = true reports Operational when streaming
        ushort basePort2 = (ushort)(62200 + Random.Shared.Next(0, 50) * 8);
        var enabledMicConfig = new HostSessionConfig
        {
            EnableMicrophoneBackchannel = true,
            LocalVideoPort = basePort2,
            LocalAudioPort = (ushort)(basePort2 + 1),
            LocalControlFeedbackPort = (ushort)(basePort2 + 2),
            LocalMicPort = (ushort)(basePort2 + 3),
            ClientVideoPort = (ushort)(basePort2 + 4),
            ClientAudioPort = (ushort)(basePort2 + 5),
            ClientControlFeedbackPort = (ushort)(basePort2 + 6)
        };

        await using (var enabledSession = new MoonshineHostStreamingSession(
            config: enabledMicConfig,
            capturePipeline: capture,
            encoderEngine: encoder))
        {
            await enabledSession.StartAsync();
            enabledSession.GetLiveBackendReadiness().MicrophoneBackchannel.Should().Be(ComponentReadiness.Operational);
            await enabledSession.StopAsync();
        }
    }

    [Fact]
    public async Task HostStreamingSession_GetLiveBackendReadiness_VirtualAudioDriver_SemanticRules()
    {
        var capture = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline();
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        ushort basePort = (ushort)(62600 + Random.Shared.Next(0, 50) * 8);
        var config = new HostSessionConfig
        {
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder);

        DriverInstallationState driverState;
        try
        {
            using var driverService = new VirtualAudioDriverService();
            driverState = driverService.GetInstallationState();
        }
        catch (Exception) // ALLOWED_EXCEPTION: Native virtual audio driver query may fail on test environments lacking PortCls driver runtime.
        {
            driverState = DriverInstallationState.Error;
        }

        ComponentReadiness expectedReadiness = driverState switch
        {
            DriverInstallationState.EndpointsActive => ComponentReadiness.Available,
            DriverInstallationState.Error => ComponentReadiness.Faulted,
            _ => ComponentReadiness.Unsupported
        };

        session.GetLiveBackendReadiness().VirtualAudioDriver.Should().Be(expectedReadiness);
    }

    [Fact]
    public async Task HostStreamingSession_GetLiveBackendReadiness_FaultedState_ReportsFaultedVideoEncoder()
    {
        var capture = new TestDesktopCapturePipeline { IsAvailable = false };
        var encoderPipeline = new TestVideoEncoderPipeline();
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        await using var session = new MoonshineHostStreamingSession(
            capturePipeline: capture,
            encoderEngine: encoder);

        Func<Task> act = async () => await session.StartAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();

        session.State.Should().Be(HostSessionState.Faulted);
        session.GetLiveBackendReadiness().VideoEncoder.Should().Be(ComponentReadiness.Faulted);
    }

    private static ushort GetAvailablePortBlock(int count = 6)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        socket.Close();
        return (ushort)port;
    }
}

