using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Moonshine.Core.Control;
using Moonshine.Core.Media;
using Moonshine.Core.Security;
using Moonshine.Core.Session;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
using Moonshine.Host.Control;
using Moonshine.Host.Encoding;
using Moonshine.Host.Input;
using Moonshine.Host.Session;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;
using MoonshineErrorCode = Moonshine.Protocol.Contracts.MoonshineErrorCode;
using Xunit;

namespace Moonshine.Host.Tests;

public class HostConfigurationSecurityAndStressTests
{
    private sealed class MockDesktopCapturePipeline : IDesktopCapturePipeline
    {
        public uint Width => 1920;
        public uint Height => 1080;
        public uint Format => 28;
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
                TextureHandle = (void*)0x1000,
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
        public EncoderImplementationKind ImplementationKind { get; set; } = EncoderImplementationKind.SyntheticTest;
        public bool IsHardwareAccelerated { get; set; }
        public bool HasProducedValidOutput { get; set; } = true;
        public Type ImplementationType => GetType();
        public EncoderRuntimeState RuntimeState => EncoderRuntimeState.Ready;
        private ulong _lastDecoderAcceptedFrameId;
        public EncoderEvidence Evidence
        {
            get
            {
                ulong lastValid = Math.Max(1, _frameIndex);
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
        public double AverageEncodingLatencyMicroseconds => 150.0;

        private uint _frameIndex;

        public bool TryEncodeFrame(
            IntPtr d3dTexture,
            ulong frameId,
            ulong timestampUs,
            bool forceIdr,
            out MoonshineEncodedPacketDesc desc,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            uint idx = frameId > 0 ? (uint)frameId : Interlocked.Increment(ref _frameIndex);
            int size = 1200;
            ReadOnlySpan<byte> naluHeader = forceIdr
                ? stackalloc byte[] { 0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xAF, 0xFE }
                : stackalloc byte[] { 0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xAF, 0xFE };
            naluHeader.CopyTo(outBitstream);
            outBitstream[naluHeader.Length..size].Fill(0xAA);
            bytesWritten = size;

            desc = new MoonshineEncodedPacketDesc
            {
                FrameIndex = idx,
                PayloadSize = (uint)size,
                IsKeyframe = forceIdr ? (byte)1 : (byte)0,
                IsHeaderPacket = 0,
                TemporalId = 0,
                Reserved = 0,
                TimestampQpc = timestampUs > 0 ? (long)timestampUs : Stopwatch.GetTimestamp()
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
            TryEncodeFrame(d3dTexture, frameId, timestampUs, forceIdr, out var desc, outBitstream, out bytesWritten);
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
            return true;
        }

        public void RequestKeyframe() { }
        public void Dispose() { }
    }

    [Fact]
    public async Task HostConfiguration_LiveSessionMutation_DoesNotCorruptActiveVideoOrAudio()
    {
        ulong sessionId = 0x1122334455667788UL;

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
        var encoderEngine = new UnifiedHardwareEncoderEngine(mockEncoder);
        using var hostInput = new MoonshineHostInputPipeline(config: new HostInputConfig { ExpectedSessionId = sessionId });
        using var hostAudio = new MoonshineHostAudioPipeline(sampleRate: 48000, topology: AudioChannelTopology.Stereo, bitrate: 128000, frameDurationMs: 5);

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: mockCapture,
            encoderEngine: encoderEngine,
            audioPipeline: hostAudio,
            inputPipeline: hostInput);

        await hostSession.StartAsync();
        hostSession.IsStreaming.Should().BeTrue();
        hostSession.LastError.Should().BeNull();

        int hostVideoPort = hostSession.BoundLocalVideoPort;
        int hostAudioPort = hostSession.BoundLocalAudioPort;
        int hostControlPort = hostSession.BoundLocalControlPort;

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

        var reassembledFrames = new List<uint>();
        await using var clientSession = new MoonshineClientStreamingSession(clientConfig);
        clientSession.OnVideoFrameReassembled = frame =>
        {
            lock (reassembledFrames)
            {
                reassembledFrames.Add(frame.FrameIndex);
            }
        };

        await clientSession.StartAsync();
        clientSession.IsStreaming.Should().BeTrue();
        clientSession.LastError.Should().BeNull();

        hostSession.SetClientEndpoints(
            videoEp: new IPEndPoint(IPAddress.Loopback, clientSession.BoundLocalVideoPort),
            audioEp: new IPEndPoint(IPAddress.Loopback, clientSession.BoundLocalAudioPort),
            controlEp: new IPEndPoint(IPAddress.Loopback, clientSession.BoundLocalControlPort));

        clientSession.ControlClient.Should().NotBeNull();
        var controlClient = clientSession.ControlClient!;

        // Stream initial audio PCM frames
        float[] pcmFrame = new float[240 * 2]; // 5ms @ 48kHz stereo
        for (int i = 0; i < 5; i++)
        {
            hostAudio.ProcessPcmFrame(pcmFrame, hostSession.SendAudioPacket);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        uint lastAppliedVersion = 1;
        uint finalVideoBitrate = 0;
        uint finalAudioBitrate = 0;

        // Apply 20 rapid runtime configuration updates modifying bitrate between 10Mbps and 40Mbps, and audio bitrate between 64kbps and 320kbps
        for (int iteration = 1; iteration <= 20; iteration++)
        {
            // Linearly interpolate video bitrate between 10,000 kbps (10 Mbps) and 40,000 kbps (40 Mbps)
            uint targetVideoBitrateKbps = 10000 + (uint)((iteration - 1) * (30000 / 19));
            // Linearly interpolate audio bitrate between 64 kbps and 320 kbps
            uint targetAudioBitrateKbps = 64 + (uint)((iteration - 1) * (256 / 19));

            finalVideoBitrate = targetVideoBitrateKbps;
            finalAudioBitrate = targetAudioBitrateKbps;

            var proposed = new MoonshineHostConfigurationPayload
            {
                ConfigVersion = lastAppliedVersion,
                DisplayWidth = 1920,
                DisplayHeight = 1080,
                RefreshRateHz = 60,
                TargetBitrateKbps = targetVideoBitrateKbps,
                MaxBitrateKbps = targetVideoBitrateKbps + 10000,
                PreferredCodec = MoonshineVideoCodec.Hevc,
                Hdr10Enabled = 0,
                AudioChannels = 2,
                AudioQualityMode = 0,
                AudioBitrateKbps = targetAudioBitrateKbps,
                InputPollingRateHz = 1000,
                MicPassthroughEnabled = 0,
                VirtualAudioDriverEnabled = 1,
                Reserved1 = 0,
                Reserved2 = 0,
                Reserved3 = 0
            };

            (MoonshineErrorCode statusCode, uint appliedVersion) = await controlClient.SetConfigurationAsync(proposed, cts.Token);
            statusCode.Should().Be(MoonshineErrorCode.Success, $"iteration {iteration} must apply successfully");
            appliedVersion.Should().Be((uint)(1 + iteration), $"version must increment monotonically on iteration {iteration}");
            lastAppliedVersion = appliedVersion;

            // Maintain continuous audio processing during mutations
            hostAudio.ProcessPcmFrame(pcmFrame, hostSession.SendAudioPacket);
        }

        // Assert final version increments to 21
        lastAppliedVersion.Should().Be(21);
        hostSession.ConfigurationService.ConfigVersion.Should().Be(21);
        hostSession.ConfigurationService.CurrentConfiguration.ConfigVersion.Should().Be(21);
        hostSession.ConfigurationService.CurrentConfiguration.TargetBitrateKbps.Should().Be(finalVideoBitrate);
        hostSession.ConfigurationService.CurrentConfiguration.AudioBitrateKbps.Should().Be(finalAudioBitrate);

        // Verify encoder and audio pipeline received dynamic updates
        mockEncoder.BitrateKbps.Should().Be(finalVideoBitrate);
        hostAudio.Bitrate.Should().Be(finalAudioBitrate * 1000);

        // Stream additional audio and wait for video frames to ensure streaming loop continues with 0 errors
        for (int i = 0; i < 10; i++)
        {
            hostAudio.ProcessPcmFrame(pcmFrame, hostSession.SendAudioPacket);
            await Task.Delay(10, cts.Token);
        }

        // Verify continuous streaming with 0 errors
        hostSession.State.Should().Be(HostSessionState.Streaming);
        hostSession.LastError.Should().BeNull();
        clientSession.State.Should().Be(ClientSessionState.Streaming);
        clientSession.LastError.Should().BeNull();

        hostSession.Metrics.TotalFramesEncoded.Should().BeGreaterThan(0);
        hostSession.Metrics.TotalPacketsSent.Should().BeGreaterThan(0);
        hostSession.Metrics.TotalAudioPacketsSent.Should().BeGreaterThan(0);
    }

    [Fact]
    public void HostConfiguration_InvalidSettings_RejectedAtomicallyWithoutPartialState()
    {
        var service = new HostConfigurationService();
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;
        uint initialVersion = service.ConfigVersion;

        initialVersion.Should().Be(1);
        baseline.DisplayWidth.Should().Be(1920);
        baseline.DisplayHeight.Should().Be(1080);
        baseline.RefreshRateHz.Should().Be(60);
        baseline.TargetBitrateKbps.Should().Be(20000);
        baseline.AudioChannels.Should().Be(2);

        bool eventFired = false;
        service.ConfigurationApplied += (_, _) => eventFired = true;

        // 1. Impossible width 16384 (exceeds max encode width 3840)
        MoonshineHostConfigurationPayload invalidWidth = baseline;
        invalidWidth.DisplayWidth = 16384;

        bool widthResult = service.TryApplyConfiguration(
            in invalidWidth,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effectiveAfterWidth,
            out MoonshineErrorCode widthError,
            out string? widthMessage);

        widthResult.Should().BeFalse();
        widthError.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        widthMessage.Should().NotBeNullOrWhiteSpace();
        service.ConfigVersion.Should().Be(initialVersion);
        service.CurrentConfiguration.Should().BeEquivalentTo(baseline);
        effectiveAfterWidth.Should().BeEquivalentTo(baseline);
        eventFired.Should().BeFalse();

        // 2. Impossible fps 1000 (exceeds max encode fps 240)
        MoonshineHostConfigurationPayload invalidFps = baseline;
        invalidFps.RefreshRateHz = 1000;

        bool fpsResult = service.TryApplyConfiguration(
            in invalidFps,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effectiveAfterFps,
            out MoonshineErrorCode fpsError,
            out string? fpsMessage);

        fpsResult.Should().BeFalse();
        fpsError.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        fpsMessage.Should().NotBeNullOrWhiteSpace();
        service.ConfigVersion.Should().Be(initialVersion);
        service.CurrentConfiguration.Should().BeEquivalentTo(baseline);
        effectiveAfterFps.Should().BeEquivalentTo(baseline);
        eventFired.Should().BeFalse();

        // 3. Impossible bitrate 2,000,000 kbps (exceeds max host bitrate capability 150000)
        MoonshineHostConfigurationPayload invalidBitrate = baseline;
        invalidBitrate.TargetBitrateKbps = 2_000_000;
        invalidBitrate.MaxBitrateKbps = 2_500_000;

        bool bitrateResult = service.TryApplyConfiguration(
            in invalidBitrate,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effectiveAfterBitrate,
            out MoonshineErrorCode bitrateError,
            out string? bitrateMessage);

        bitrateResult.Should().BeFalse();
        bitrateError.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        bitrateMessage.Should().NotBeNullOrWhiteSpace();
        service.ConfigVersion.Should().Be(initialVersion);
        service.CurrentConfiguration.Should().BeEquivalentTo(baseline);
        effectiveAfterBitrate.Should().BeEquivalentTo(baseline);
        eventFired.Should().BeFalse();

        // 4. Impossible 3-channel audio (strictly only 2, 6, or 8 channels supported)
        MoonshineHostConfigurationPayload invalidChannels = baseline;
        invalidChannels.AudioChannels = 3;

        bool channelsResult = service.TryApplyConfiguration(
            in invalidChannels,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effectiveAfterChannels,
            out MoonshineErrorCode channelsError,
            out string? channelsMessage);

        channelsResult.Should().BeFalse();
        channelsError.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        channelsMessage.Should().NotBeNullOrWhiteSpace();
        service.ConfigVersion.Should().Be(initialVersion);
        service.CurrentConfiguration.Should().BeEquivalentTo(baseline);
        effectiveAfterChannels.Should().BeEquivalentTo(baseline);
        eventFired.Should().BeFalse();

        // 5. Combined impossible parameters (all at once)
        MoonshineHostConfigurationPayload impossibleAll = baseline;
        impossibleAll.DisplayWidth = 16384;
        impossibleAll.RefreshRateHz = 1000;
        impossibleAll.TargetBitrateKbps = 2_000_000;
        impossibleAll.MaxBitrateKbps = 2_500_000;
        impossibleAll.AudioChannels = 3;

        bool allResult = service.TryApplyConfiguration(
            in impossibleAll,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effectiveAfterAll,
            out MoonshineErrorCode allError,
            out string? allMessage);

        allResult.Should().BeFalse();
        allError.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        allMessage.Should().NotBeNullOrWhiteSpace();
        service.ConfigVersion.Should().Be(initialVersion);
        service.CurrentConfiguration.Should().BeEquivalentTo(baseline);
        effectiveAfterAll.Should().BeEquivalentTo(baseline);
        eventFired.Should().BeFalse();
    }

    [Theory]
    [InlineData(AuthorisationLevel.None)]
    [InlineData(AuthorisationLevel.Viewer)]
    public void HostConfiguration_UnauthorizedRole_RejectedWithDeterministicError(AuthorisationLevel role)
    {
        var service = new HostConfigurationService();
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;
        uint initialVersion = service.ConfigVersion;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.DisplayWidth = 2560;
        proposed.DisplayHeight = 1440;
        proposed.TargetBitrateKbps = 35000;
        proposed.AudioBitrateKbps = 256;

        bool eventFired = false;
        service.ConfigurationApplied += (_, _) => eventFired = true;

        bool result = service.TryApplyConfiguration(
            in proposed,
            role,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        result.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.UnauthorizedConfiguration);
        errorMessage.Should().NotBeNullOrWhiteSpace();
        errorMessage.Should().Contain("authorisation level is insufficient");
        eventFired.Should().BeFalse();

        service.ConfigVersion.Should().Be(initialVersion);
        service.CurrentConfiguration.Should().BeEquivalentTo(baseline);
        effective.Should().BeEquivalentTo(baseline);
    }

    [Fact]
    public void HostConfiguration_SanitizedDTO_ZeroSecretsOrUninitializedMemoryLeaked()
    {
        // 1. MoonshineHostCapabilitiesResponsePayload:
        // Dirty uninitialized memory simulation / secret bytes in reserved fields
        var dirtyCaps = new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = (uint)MoonshineCapabilities.Hevc,
            SupportedAudioCodecs = (uint)MoonshineAudioCodec.Opus,
            MaxEncodeWidth = 3840,
            MaxEncodeHeight = 2160,
            MaxEncodeFps = 120,
            SupportsHdr10 = 1,
            SupportsVirtualAudio = 1,
            SupportsMicBackchannel = 1,
            Reserved = 0xAA,
            MaxBitrateKbps = 100000,
            Reserved2 = 0xDEADBEEF
        };

        var service = new HostConfigurationService(dirtyCaps);
        MoonshineHostCapabilitiesResponsePayload advertisedCaps = service.Capabilities;

        advertisedCaps.MaxEncodeWidth.Should().Be(3840);
        advertisedCaps.MaxEncodeHeight.Should().Be(2160);

        // Reserved fields must be strictly zeroed out
        advertisedCaps.Reserved.Should().Be(0);
        advertisedCaps.Reserved2.Should().Be(0);

        // Check serialised datagram byte memory
        byte[] capsBuffer = new byte[32];
        bool capsWritten = MoonshineProtocolCodec.TryWriteHostCapabilitiesResponse(in advertisedCaps, capsBuffer);
        capsWritten.Should().BeTrue();
        capsBuffer[23].Should().Be(0x00, "byte offset 23 (Reserved) must be strictly 0x00");
        capsBuffer.AsSpan(28, 4).ToArray().Should().Equal([0x00, 0x00, 0x00, 0x00], "bytes 28-31 (Reserved2) must be strictly 0x00");

        // 2. MoonshineHostConfigurationPayload:
        // Simulate uninitialized DTO containing host pointers or heap fragments in Reserved fields
        var dirtyConfig = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 1,
            DisplayWidth = 2560,
            DisplayHeight = 1440,
            RefreshRateHz = 120,
            TargetBitrateKbps = 35000,
            MaxBitrateKbps = 50000,
            PreferredCodec = MoonshineVideoCodec.Hevc,
            Hdr10Enabled = 1,
            AudioChannels = 6,
            AudioQualityMode = 0,
            AudioBitrateKbps = 256,
            InputPollingRateHz = 1000,
            MicPassthroughEnabled = 1,
            VirtualAudioDriverEnabled = 1,
            Reserved1 = 0xDEADBEEF,
            Reserved2 = 0xCAFEBABE,
            Reserved3 = 0x1337C0DE
        };

        bool applied = service.TryApplyConfiguration(
            in dirtyConfig,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeTrue();
        errorCode.Should().Be(MoonshineErrorCode.Success);
        errorMessage.Should().BeNull();

        // Verify effective DTO zero-wipes all reserved fields
        effective.Reserved1.Should().Be(0);
        effective.Reserved2.Should().Be(0);
        effective.Reserved3.Should().Be(0);

        // Verify GetSanitizedConfiguration zero-wipes all reserved fields
        MoonshineHostConfigurationPayload sanitized = service.GetSanitizedConfiguration();
        sanitized.DisplayWidth.Should().Be(2560);
        sanitized.DisplayHeight.Should().Be(1440);
        sanitized.Reserved1.Should().Be(0);
        sanitized.Reserved2.Should().Be(0);
        sanitized.Reserved3.Should().Be(0);

        // Check serialised configuration datagram byte memory
        byte[] configBuffer = new byte[48];
        bool configWritten = MoonshineProtocolCodec.TryWriteHostConfiguration(in sanitized, configBuffer);
        configWritten.Should().BeTrue();

        configBuffer.AsSpan(36, 4).ToArray().Should().Equal([0x00, 0x00, 0x00, 0x00], "Reserved1 must be zeroed");
        configBuffer.AsSpan(40, 4).ToArray().Should().Equal([0x00, 0x00, 0x00, 0x00], "Reserved2 must be zeroed");
        configBuffer.AsSpan(44, 4).ToArray().Should().Equal([0x00, 0x00, 0x00, 0x00], "Reserved3 must be zeroed");

        // Verify deserialisation from raw zeroes
        MoonshineErrorCode readErr = MoonshineProtocolCodec.TryReadHostConfiguration(configBuffer, out MoonshineHostConfigurationPayload parsed);
        readErr.Should().Be(MoonshineErrorCode.Success);
        parsed.Reserved1.Should().Be(0);
        parsed.Reserved2.Should().Be(0);
        parsed.Reserved3.Should().Be(0);
    }

    [Fact]
    public async Task HostConfiguration_WithAuthenticator_RejectsReplayStaleAndTamperedPackets()
    {
        ulong sessionId = 0xAABBCCDDEEFF0011UL;
        byte[] hmacKey = new byte[32];
        for (int i = 0; i < 32; i++) hmacKey[i] = (byte)(i + 1);

        var hostAuthenticator = new MoonshineSessionAuthenticator(hmacKey);
        var clientAuthenticator = new MoonshineSessionAuthenticator(hmacKey);

        var hostConfig = new HostSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            EnableMicrophoneBackchannel = false
        };

        using var mockCapture = new MockDesktopCapturePipeline();
        using var mockEncoder = new MockVideoEncoderPipeline();
        var encoderEngine = new UnifiedHardwareEncoderEngine(mockEncoder);

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: mockCapture,
            encoderEngine: encoderEngine,
            authenticator: hostAuthenticator);

        await hostSession.StartAsync();

        using var clientSocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var hostControlEp = new IPEndPoint(IPAddress.Loopback, hostSession.BoundLocalControlPort);

        async Task<(MoonshineErrorCode Status, uint Version)> SendAndReceiveAsync(byte[] packet)
        {
            await clientSocket.SendToAsync(packet, System.Net.Sockets.SocketFlags.None, hostControlEp);
            byte[] resp = new byte[128];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            while (true)
            {
                var res = await clientSocket.ReceiveFromAsync(resp.AsMemory(), System.Net.Sockets.SocketFlags.None, hostControlEp, cts.Token);
                MoonshineProtocolCodec.TryReadHeader(resp.AsSpan(0, res.ReceivedBytes), out var header);
                if (header.MessageType == MoonshineMessageType.SetHostConfigurationResponse)
                {
                    MoonshineProtocolCodec.TryReadSetHostConfigurationResponse(resp.AsSpan(MoonshineProtocolConstants.HeaderSize), out var payload);
                    return (payload.StatusCode, payload.AppliedConfigVersion);
                }
            }
        }

        var proposed = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 1,
            DisplayWidth = 1920,
            DisplayHeight = 1080,
            RefreshRateHz = 60,
            TargetBitrateKbps = 25000,
            MaxBitrateKbps = 50000,
            PreferredCodec = MoonshineVideoCodec.Hevc,
            AudioChannels = 2,
            AudioBitrateKbps = 128
        };

        // 1. Send valid packet with sequence 10
        ulong nowUs = (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency);
        byte[] validPacket = new byte[MoonshineProtocolConstants.HeaderSize + 80];
        var header10 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfiguration,
            PayloadSize: 80,
            SequenceNumber: 10,
            SessionId: sessionId,
            TimestampUs: nowUs);

        MoonshineProtocolCodec.TryWriteHeader(in header10, validPacket);
        MoonshineProtocolCodec.TryWriteHostConfiguration(in proposed, validPacket.AsSpan(MoonshineProtocolConstants.HeaderSize, 48));
        clientAuthenticator.ComputeMessageAuthTag(validPacket.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 48), validPacket.AsSpan(MoonshineProtocolConstants.HeaderSize + 48, 32));

        (MoonshineErrorCode status1, uint version1) = await SendAndReceiveAsync(validPacket);
        status1.Should().Be(MoonshineErrorCode.Success);
        version1.Should().Be(2);

        // 2. Replay identical sequence 10 -> rejected as DuplicateSequence
        (MoonshineErrorCode statusReplay, _) = await SendAndReceiveAsync(validPacket);
        statusReplay.Should().Be(MoonshineErrorCode.DuplicateSequence);

        // 3. Stale timestamp (age = 10s) with sequence 11 -> rejected as StaleTimestamp
        byte[] stalePacket = new byte[MoonshineProtocolConstants.HeaderSize + 80];
        var headerStale = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfiguration,
            PayloadSize: 80,
            SequenceNumber: 11,
            SessionId: sessionId,
            TimestampUs: nowUs > 10_000_000UL ? nowUs - 10_000_000UL : 1UL);

        MoonshineProtocolCodec.TryWriteHeader(in headerStale, stalePacket);
        MoonshineProtocolCodec.TryWriteHostConfiguration(in proposed, stalePacket.AsSpan(MoonshineProtocolConstants.HeaderSize, 48));
        clientAuthenticator.ComputeMessageAuthTag(stalePacket.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 48), stalePacket.AsSpan(MoonshineProtocolConstants.HeaderSize + 48, 32));

        (MoonshineErrorCode statusStale, _) = await SendAndReceiveAsync(stalePacket);
        statusStale.Should().Be(MoonshineErrorCode.StaleTimestamp);

        // 4. Tampered payload with sequence 12 -> rejected as AuthenticationFailed
        byte[] tamperedPacket = new byte[MoonshineProtocolConstants.HeaderSize + 80];
        var headerTampered = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfiguration,
            PayloadSize: 80,
            SequenceNumber: 12,
            SessionId: sessionId,
            TimestampUs: (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency));

        MoonshineProtocolCodec.TryWriteHeader(in headerTampered, tamperedPacket);
        MoonshineProtocolCodec.TryWriteHostConfiguration(in proposed, tamperedPacket.AsSpan(MoonshineProtocolConstants.HeaderSize, 48));
        clientAuthenticator.ComputeMessageAuthTag(tamperedPacket.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 48), tamperedPacket.AsSpan(MoonshineProtocolConstants.HeaderSize + 48, 32));
        tamperedPacket[MoonshineProtocolConstants.HeaderSize + 10] ^= 0xFF;

        (MoonshineErrorCode statusTampered, _) = await SendAndReceiveAsync(tamperedPacket);
        statusTampered.Should().Be(MoonshineErrorCode.AuthenticationFailed);
    }

    [Fact]
    public async Task HostConfiguration_WithViewerAuthorisation_RejectsSetConfiguration()
    {
        ulong sessionId = 0x5555666677778888UL;

        var hostConfig = new HostSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            AuthorisationLevel = AuthorisationLevel.Viewer,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            EnableMicrophoneBackchannel = false
        };

        using var mockCapture = new MockDesktopCapturePipeline();
        using var mockEncoder = new MockVideoEncoderPipeline();
        var encoderEngine = new UnifiedHardwareEncoderEngine(mockEncoder);

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: mockCapture,
            encoderEngine: encoderEngine);

        await hostSession.StartAsync();

        using var clientSocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var hostControlEp = new IPEndPoint(IPAddress.Loopback, hostSession.BoundLocalControlPort);

        var proposed = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 1,
            DisplayWidth = 1920,
            DisplayHeight = 1080,
            RefreshRateHz = 60,
            TargetBitrateKbps = 30000,
            MaxBitrateKbps = 50000,
            PreferredCodec = MoonshineVideoCodec.Hevc,
            AudioChannels = 2,
            AudioBitrateKbps = 128
        };

        byte[] packet = new byte[MoonshineProtocolConstants.HeaderSize + 48];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfiguration,
            PayloadSize: 48,
            SequenceNumber: 1,
            SessionId: sessionId,
            TimestampUs: (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency));

        MoonshineProtocolCodec.TryWriteHeader(in header, packet);
        MoonshineProtocolCodec.TryWriteHostConfiguration(in proposed, packet.AsSpan(MoonshineProtocolConstants.HeaderSize));

        await clientSocket.SendToAsync(packet, System.Net.Sockets.SocketFlags.None, hostControlEp);

        byte[] resp = new byte[128];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var res = await clientSocket.ReceiveFromAsync(resp.AsMemory(), System.Net.Sockets.SocketFlags.None, hostControlEp, cts.Token);
        MoonshineProtocolCodec.TryReadSetHostConfigurationResponse(resp.AsSpan(MoonshineProtocolConstants.HeaderSize), out var respPayload);

        respPayload.StatusCode.Should().Be(MoonshineErrorCode.UnauthorizedConfiguration);
    }

    [Fact]
    public async Task HostConfiguration_DimensionAndFpsChange_TriggersKeyframeAndEncoderUpdate()
    {
        ulong sessionId = 0x9988776655443322UL;

        var hostConfig = new HostSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            AuthorisationLevel = AuthorisationLevel.Administrator,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            EnableMicrophoneBackchannel = false
        };

        using var mockCapture = new MockDesktopCapturePipeline();
        using var mockEncoder = new MockVideoEncoderPipeline();
        var encoderEngine = new UnifiedHardwareEncoderEngine(mockEncoder);

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: mockCapture,
            encoderEngine: encoderEngine);

        await hostSession.StartAsync();

        ulong initialKeyframes = hostSession.Metrics.KeyframesRequested;

        using var clientSocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var hostControlEp = new IPEndPoint(IPAddress.Loopback, hostSession.BoundLocalControlPort);

        var proposed = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 1,
            DisplayWidth = 2560,
            DisplayHeight = 1440,
            RefreshRateHz = 120,
            TargetBitrateKbps = 35000,
            MaxBitrateKbps = 50000,
            PreferredCodec = MoonshineVideoCodec.Hevc,
            AudioChannels = 2,
            AudioBitrateKbps = 128
        };

        byte[] packet = new byte[MoonshineProtocolConstants.HeaderSize + 48];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfiguration,
            PayloadSize: 48,
            SequenceNumber: 1,
            SessionId: sessionId,
            TimestampUs: (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency));

        MoonshineProtocolCodec.TryWriteHeader(in header, packet);
        MoonshineProtocolCodec.TryWriteHostConfiguration(in proposed, packet.AsSpan(MoonshineProtocolConstants.HeaderSize));

        await clientSocket.SendToAsync(packet, System.Net.Sockets.SocketFlags.None, hostControlEp);

        byte[] resp = new byte[128];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var res = await clientSocket.ReceiveFromAsync(resp.AsMemory(), System.Net.Sockets.SocketFlags.None, hostControlEp, cts.Token);
        MoonshineProtocolCodec.TryReadSetHostConfigurationResponse(resp.AsSpan(MoonshineProtocolConstants.HeaderSize), out var respPayload);

        respPayload.StatusCode.Should().Be(MoonshineErrorCode.Success);
        respPayload.AppliedConfigVersion.Should().Be(2);

        // Keyframe count should have incremented
        hostSession.Metrics.KeyframesRequested.Should().BeGreaterThan(initialKeyframes);

        // Encoder bitrate should be updated
        mockEncoder.BitrateKbps.Should().Be(35000);
    }

    [Fact]
    public async Task HostCapabilitiesAndConfiguration_WithAuthenticator_RejectsReplayStaleAndTamperedQueries()
    {
        ulong sessionId = 0xCCDDEEFF00112233UL;
        byte[] hmacKey = new byte[32];
        for (int i = 0; i < 32; i++) hmacKey[i] = (byte)(i + 7);

        var hostAuthenticator = new MoonshineSessionAuthenticator(hmacKey);
        var clientAuthenticator = new MoonshineSessionAuthenticator(hmacKey);

        var hostConfig = new HostSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            EnableMicrophoneBackchannel = false
        };

        using var mockCapture = new MockDesktopCapturePipeline();
        using var mockEncoder = new MockVideoEncoderPipeline();
        var encoderEngine = new UnifiedHardwareEncoderEngine(mockEncoder);

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: mockCapture,
            encoderEngine: encoderEngine,
            authenticator: hostAuthenticator);

        await hostSession.StartAsync();

        using var clientSocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var hostControlEp = new IPEndPoint(IPAddress.Loopback, hostSession.BoundLocalControlPort);

        async Task<MoonshineMessageType?> TrySendAndReceiveAsync(byte[] packet, int timeoutMs = 200)
        {
            await clientSocket.SendToAsync(packet, System.Net.Sockets.SocketFlags.None, hostControlEp);
            byte[] resp = new byte[128];
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

            try
            {
                var res = await clientSocket.ReceiveFromAsync(resp.AsMemory(), System.Net.Sockets.SocketFlags.None, hostControlEp, cts.Token);
                MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(resp.AsSpan(0, res.ReceivedBytes), out var header);
                return err == MoonshineErrorCode.Success ? header.MessageType : null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        // 1. GetHostCapabilities valid signed packet
        ulong nowUs = (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency);
        byte[] validCapPacket = new byte[MoonshineProtocolConstants.HeaderSize + 36];
        var headerCap20 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.GetHostCapabilities,
            PayloadSize: 36,
            SequenceNumber: 20,
            SessionId: sessionId,
            TimestampUs: nowUs);

        MoonshineProtocolCodec.TryWriteHeader(in headerCap20, validCapPacket);
        MoonshineProtocolCodec.TryWriteGetHostCapabilities(0, validCapPacket.AsSpan(MoonshineProtocolConstants.HeaderSize, 4));
        clientAuthenticator.ComputeMessageAuthTag(validCapPacket.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 4), validCapPacket.AsSpan(MoonshineProtocolConstants.HeaderSize + 4, 32));

        MoonshineMessageType? capRespType = await TrySendAndReceiveAsync(validCapPacket, 3000);
        capRespType.Should().Be(MoonshineMessageType.HostCapabilitiesResponse);

        // 2. Replay GetHostCapabilities -> dropped (null)
        MoonshineMessageType? replayCapRespType = await TrySendAndReceiveAsync(validCapPacket, 200);
        replayCapRespType.Should().BeNull();

        // 3. Tampered GetHostCapabilities -> dropped (null)
        byte[] tamperedCapPacket = new byte[MoonshineProtocolConstants.HeaderSize + 36];
        var headerCap21 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.GetHostCapabilities,
            PayloadSize: 36,
            SequenceNumber: 21,
            SessionId: sessionId,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        MoonshineProtocolCodec.TryWriteHeader(in headerCap21, tamperedCapPacket);
        MoonshineProtocolCodec.TryWriteGetHostCapabilities(0, tamperedCapPacket.AsSpan(MoonshineProtocolConstants.HeaderSize, 4));
        clientAuthenticator.ComputeMessageAuthTag(tamperedCapPacket.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 4), tamperedCapPacket.AsSpan(MoonshineProtocolConstants.HeaderSize + 4, 32));
        tamperedCapPacket[MoonshineProtocolConstants.HeaderSize + 4] ^= 0xFF; // Corrupt tag

        MoonshineMessageType? tamperedCapRespType = await TrySendAndReceiveAsync(tamperedCapPacket, 200);
        tamperedCapRespType.Should().BeNull();

        // 4. GetHostConfiguration valid signed packet
        byte[] validCfgPacket = new byte[MoonshineProtocolConstants.HeaderSize + 36];
        var headerCfg30 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.GetHostConfiguration,
            PayloadSize: 36,
            SequenceNumber: 30,
            SessionId: sessionId,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        MoonshineProtocolCodec.TryWriteHeader(in headerCfg30, validCfgPacket);
        MoonshineProtocolCodec.TryWriteGetHostConfiguration(0, validCfgPacket.AsSpan(MoonshineProtocolConstants.HeaderSize, 4));
        clientAuthenticator.ComputeMessageAuthTag(validCfgPacket.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 4), validCfgPacket.AsSpan(MoonshineProtocolConstants.HeaderSize + 4, 32));

        MoonshineMessageType? cfgRespType = await TrySendAndReceiveAsync(validCfgPacket, 3000);
        cfgRespType.Should().Be(MoonshineMessageType.HostConfigurationResponse);

        // 5. Replay GetHostConfiguration -> dropped (null)
        MoonshineMessageType? replayCfgRespType = await TrySendAndReceiveAsync(validCfgPacket, 200);
        replayCfgRespType.Should().BeNull();

        // 6. Tampered GetHostConfiguration -> dropped (null)
        byte[] tamperedCfgPacket = new byte[MoonshineProtocolConstants.HeaderSize + 36];
        var headerCfg31 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.GetHostConfiguration,
            PayloadSize: 36,
            SequenceNumber: 31,
            SessionId: sessionId,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        MoonshineProtocolCodec.TryWriteHeader(in headerCfg31, tamperedCfgPacket);
        MoonshineProtocolCodec.TryWriteGetHostConfiguration(0, tamperedCfgPacket.AsSpan(MoonshineProtocolConstants.HeaderSize, 4));
        clientAuthenticator.ComputeMessageAuthTag(tamperedCfgPacket.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 4), tamperedCfgPacket.AsSpan(MoonshineProtocolConstants.HeaderSize + 4, 32));
        tamperedCfgPacket[MoonshineProtocolConstants.HeaderSize + 4] ^= 0xFF; // Corrupt tag

        MoonshineMessageType? tamperedCfgRespType = await TrySendAndReceiveAsync(tamperedCfgPacket, 200);
        tamperedCfgRespType.Should().BeNull();
    }

    [Fact]
    public async Task HostCapabilities_WithAuthenticator_UnsignedQueryIsRejected()
    {
        ulong sessionId = 0x11223344AABBCCDDUL;
        byte[] hmacKey = new byte[32];
        for (int i = 0; i < 32; i++) hmacKey[i] = (byte)(i + 3);

        var hostAuthenticator = new MoonshineSessionAuthenticator(hmacKey);

        var hostConfig = new HostSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            EnableMicrophoneBackchannel = false
        };

        using var mockCapture = new MockDesktopCapturePipeline();
        using var mockEncoder = new MockVideoEncoderPipeline();
        var encoderEngine = new UnifiedHardwareEncoderEngine(mockEncoder);

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: mockCapture,
            encoderEngine: encoderEngine,
            authenticator: hostAuthenticator);

        await hostSession.StartAsync();

        using var clientSocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var hostControlEp = new IPEndPoint(IPAddress.Loopback, hostSession.BoundLocalControlPort);

        // Unsigned 4-byte GetHostCapabilities packet
        byte[] unsignedPacket = new byte[MoonshineProtocolConstants.HeaderSize + 4];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.GetHostCapabilities,
            PayloadSize: 4,
            SequenceNumber: 1,
            SessionId: sessionId,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        MoonshineProtocolCodec.TryWriteHeader(in header, unsignedPacket);
        MoonshineProtocolCodec.TryWriteGetHostCapabilities(0, unsignedPacket.AsSpan(MoonshineProtocolConstants.HeaderSize, 4));

        await clientSocket.SendToAsync(unsignedPacket, System.Net.Sockets.SocketFlags.None, hostControlEp);

        byte[] resp = new byte[128];
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var receiveFunc = async () =>
        {
            await clientSocket.ReceiveFromAsync(resp.AsMemory(), System.Net.Sockets.SocketFlags.None, hostControlEp, cts.Token);
        };

        await receiveFunc.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HostConfiguration_WithAuthenticator_UnsignedQueryIsRejected()
    {
        ulong sessionId = 0x22334455BBCCDDEEUL;
        byte[] hmacKey = new byte[32];
        for (int i = 0; i < 32; i++) hmacKey[i] = (byte)(i + 5);

        var hostAuthenticator = new MoonshineSessionAuthenticator(hmacKey);

        var hostConfig = new HostSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            EnableMicrophoneBackchannel = false
        };

        using var mockCapture = new MockDesktopCapturePipeline();
        using var mockEncoder = new MockVideoEncoderPipeline();
        var encoderEngine = new UnifiedHardwareEncoderEngine(mockEncoder);

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: mockCapture,
            encoderEngine: encoderEngine,
            authenticator: hostAuthenticator);

        await hostSession.StartAsync();

        using var clientSocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var hostControlEp = new IPEndPoint(IPAddress.Loopback, hostSession.BoundLocalControlPort);

        // Unsigned 4-byte GetHostConfiguration packet
        byte[] unsignedPacket = new byte[MoonshineProtocolConstants.HeaderSize + 4];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.GetHostConfiguration,
            PayloadSize: 4,
            SequenceNumber: 1,
            SessionId: sessionId,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        MoonshineProtocolCodec.TryWriteHeader(in header, unsignedPacket);
        MoonshineProtocolCodec.TryWriteGetHostConfiguration(0, unsignedPacket.AsSpan(MoonshineProtocolConstants.HeaderSize, 4));

        await clientSocket.SendToAsync(unsignedPacket, System.Net.Sockets.SocketFlags.None, hostControlEp);

        byte[] resp = new byte[128];
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var receiveFunc = async () =>
        {
            await clientSocket.ReceiveFromAsync(resp.AsMemory(), System.Net.Sockets.SocketFlags.None, hostControlEp, cts.Token);
        };

        await receiveFunc.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HostSetConfiguration_WithAuthenticator_UnsignedMutationIsRejected()
    {
        ulong sessionId = 0x33445566CCDDEEFFUL;
        byte[] hmacKey = new byte[32];
        for (int i = 0; i < 32; i++) hmacKey[i] = (byte)(i + 9);

        var hostAuthenticator = new MoonshineSessionAuthenticator(hmacKey);

        var hostConfig = new HostSessionConfig
        {
            SessionId = sessionId,
            StreamId = 1,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            EnableMicrophoneBackchannel = false
        };

        using var mockCapture = new MockDesktopCapturePipeline();
        using var mockEncoder = new MockVideoEncoderPipeline();
        var encoderEngine = new UnifiedHardwareEncoderEngine(mockEncoder);

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: mockCapture,
            encoderEngine: encoderEngine,
            authenticator: hostAuthenticator);

        await hostSession.StartAsync();

        using var clientSocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var hostControlEp = new IPEndPoint(IPAddress.Loopback, hostSession.BoundLocalControlPort);

        var proposed = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 1,
            DisplayWidth = 1920,
            DisplayHeight = 1080,
            RefreshRateHz = 60,
            TargetBitrateKbps = 25000,
            MaxBitrateKbps = 50000,
            PreferredCodec = MoonshineVideoCodec.Hevc,
            AudioChannels = 2,
            AudioBitrateKbps = 128
        };

        // Unsigned 48-byte SetHostConfiguration packet
        byte[] unsignedPacket = new byte[MoonshineProtocolConstants.HeaderSize + 48];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfiguration,
            PayloadSize: 48,
            SequenceNumber: 1,
            SessionId: sessionId,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        MoonshineProtocolCodec.TryWriteHeader(in header, unsignedPacket);
        MoonshineProtocolCodec.TryWriteHostConfiguration(in proposed, unsignedPacket.AsSpan(MoonshineProtocolConstants.HeaderSize, 48));

        await clientSocket.SendToAsync(unsignedPacket, System.Net.Sockets.SocketFlags.None, hostControlEp);

        byte[] resp = new byte[128];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var res = await clientSocket.ReceiveFromAsync(resp.AsMemory(), System.Net.Sockets.SocketFlags.None, hostControlEp, cts.Token);

        MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(resp.AsSpan(0, res.ReceivedBytes), out var respHeader);
        err.Should().Be(MoonshineErrorCode.Success);
        respHeader.MessageType.Should().Be(MoonshineMessageType.SetHostConfigurationResponse);

        MoonshineErrorCode payloadErr = MoonshineProtocolCodec.TryReadSetHostConfigurationResponse(resp.AsSpan(MoonshineProtocolConstants.HeaderSize), out var respPayload);
        payloadErr.Should().Be(MoonshineErrorCode.Success);
        respPayload.StatusCode.Should().Be(MoonshineErrorCode.AuthenticationFailed);
    }
}
