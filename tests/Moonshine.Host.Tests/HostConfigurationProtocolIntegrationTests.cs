using System.Diagnostics;
using System.Net;
using FluentAssertions;
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

public class HostConfigurationProtocolIntegrationTests
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
        public double AverageEncodingLatencyMicroseconds => 150.0;

        private uint _frameIndex;

        public bool TryEncodeFrame(
            IntPtr d3dTexture,
            bool forceIdr,
            out MoonshineEncodedPacketDesc desc,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            uint idx = Interlocked.Increment(ref _frameIndex);
            int size = 1200;
            outBitstream[..size].Fill(0xAA);
            bytesWritten = size;

            desc = new MoonshineEncodedPacketDesc
            {
                FrameIndex = idx,
                PayloadSize = (uint)size,
                IsKeyframe = forceIdr ? (byte)1 : (byte)0,
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

        public void RequestKeyframe() { }
        public void Dispose() { }
    }

    [Fact]
    public async Task HostAndClient_EndToEndHostConfigurationProtocol_ExchangesCapabilitiesAndMutatesConfig()
    {
        var capture = new MockDesktopCapturePipeline();
        var mockEncoder = new MockVideoEncoderPipeline();
        var encoderEngine = new UnifiedHardwareEncoderEngine(mockEncoder);

        var hostConfig = new HostSessionConfig
        {
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            LocalMicPort = 0,
            ClientVideoPort = 0,
            ClientAudioPort = 0,
            ClientControlFeedbackPort = 0,
            EnableMicrophoneBackchannel = false
        };

        await using var hostSession = new MoonshineHostStreamingSession(
            config: hostConfig,
            capturePipeline: capture,
            encoderEngine: encoderEngine);

        await hostSession.StartAsync();

        int hostVideoPort = hostSession.BoundLocalVideoPort;
        int hostAudioPort = hostSession.BoundLocalAudioPort;
        int hostControlPort = hostSession.BoundLocalControlPort;

        var clientConfig = new ClientSessionConfig
        {
            HostAddress = IPAddress.Loopback,
            HostVideoPort = hostVideoPort,
            HostAudioPort = hostAudioPort,
            HostControlFeedbackPort = hostControlPort,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            EnableMicrophoneUplink = false
        };

        await using var clientSession = new MoonshineClientStreamingSession(clientConfig);
        await clientSession.StartAsync();

        hostSession.SetClientEndpoints(
            new IPEndPoint(IPAddress.Loopback, clientSession.BoundLocalVideoPort),
            new IPEndPoint(IPAddress.Loopback, clientSession.BoundLocalAudioPort),
            new IPEndPoint(IPAddress.Loopback, clientSession.BoundLocalControlPort));

        clientSession.ControlClient.Should().NotBeNull();
        var controlClient = clientSession.ControlClient!;

        // 1. GetCapabilitiesAsync
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        MoonshineHostCapabilitiesResponsePayload capabilities = await controlClient.GetCapabilitiesAsync(ct: cts.Token);
        capabilities.MaxEncodeWidth.Should().Be(3840);
        capabilities.MaxEncodeHeight.Should().Be(2160);
        capabilities.MaxEncodeFps.Should().Be(240);

        // 2. GetConfigurationAsync
        MoonshineHostConfigurationPayload currentConfig = await controlClient.GetConfigurationAsync(ct: cts.Token);
        currentConfig.DisplayWidth.Should().Be(1920);
        currentConfig.DisplayHeight.Should().Be(1080);
        currentConfig.TargetBitrateKbps.Should().Be(20000);
        currentConfig.ConfigVersion.Should().Be(1);

        // 3. SetConfigurationAsync
        var changedTcs = new TaskCompletionSource<MoonshineConfigurationChangedPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        controlClient.ConfigurationChanged += payload =>
        {
            changedTcs.TrySetResult(payload);
        };

        var proposedConfig = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 1,
            DisplayWidth = 1920,
            DisplayHeight = 1080,
            RefreshRateHz = 60,
            TargetBitrateKbps = 35000,
            MaxBitrateKbps = 45000,
            PreferredCodec = MoonshineVideoCodec.Hevc,
            AudioChannels = 2,
            AudioBitrateKbps = 128
        };

        (MoonshineErrorCode statusCode, uint appliedVersion) = await controlClient.SetConfigurationAsync(proposedConfig, cts.Token);
        statusCode.Should().Be(MoonshineErrorCode.Success);
        appliedVersion.Should().Be(2);

        // Verify ConfigurationChanged notification arrived
        MoonshineConfigurationChangedPayload changedNotification = await changedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        changedNotification.NewConfigVersion.Should().Be(2);

        // Verify host encoder was dynamically reconfigured
        mockEncoder.BitrateKbps.Should().Be(35000);
        hostSession.ConfigurationService.CurrentConfiguration.TargetBitrateKbps.Should().Be(35000);
        hostSession.ConfigurationService.ConfigVersion.Should().Be(2);
    }
}
