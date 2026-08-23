using System.Buffers.Binary;
using FluentAssertions;
using Moonshine.Core.Audio;
using Moonshine.Host.Audio;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Host.Tests;

public sealed class HostMicrophoneUplinkServiceTests
{
    private static float[] GenerateSineWavePcm(int sampleCount, uint sampleRate = 48000, float frequency = 440.0f)
    {
        float[] pcm = new float[sampleCount];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = 0.3f * MathF.Sin(2.0f * MathF.PI * frequency * (i / (float)sampleRate));
        }
        return pcm;
    }

    [Fact]
    public void HostMicrophoneUplinkService_InitialisationAndDefaultProperties_AreCorrect()
    {
        using var service = new HostMicrophoneUplinkService(
            sampleRate: 48000,
            channels: 1,
            frameDurationMs: 10,
            ipcBridge: null,
            autoStartWorker: false
        );

        service.SampleRate.Should().Be(48000);
        service.Channels.Should().Be(1);
        service.FrameDurationMs.Should().Be(10);
        service.SamplesPerFrame.Should().Be(480);
        service.IsInitialized.Should().BeTrue();
        service.IsRunning.Should().BeFalse();
        service.Sink.Should().NotBeNull();
        service.IpcBridge.Should().BeNull();

        HostMicSinkMetrics metrics = service.GetMetrics();
        metrics.TotalPacketsReceived.Should().Be(0);
        metrics.TotalSamplesRendered.Should().Be(0);
    }

    [Fact]
    public void HostMicrophoneUplinkService_InvalidConstructorParameters_ThrowsArgumentOutOfRangeException()
    {
        Action actSampleRate = () => { using var _ = new HostMicrophoneUplinkService(sampleRate: 0); };
        actSampleRate.Should().Throw<ArgumentOutOfRangeException>();

        Action actChannels = () => { using var _ = new HostMicrophoneUplinkService(channels: 0); };
        actChannels.Should().Throw<ArgumentOutOfRangeException>();

        Action actDuration = () => { using var _ = new HostMicrophoneUplinkService(frameDurationMs: 0); };
        actDuration.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HostMicrophoneUplinkService_IngestRtpDatagram_PushesAndDecodesPcmSuccessfully()
    {
        using var clientCapture = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10
        );

        using var uplink = new HostMicrophoneUplinkService(
            sampleRate: 48000,
            channels: 1,
            frameDurationMs: 10,
            ipcBridge: null,
            autoStartWorker: false
        );

        float[] inputPcm = GenerateSineWavePcm(480);
        byte[] datagramBuffer = new byte[512];

        bool captureOk = clientCapture.TryProcessRecordedFrame(
            inputPcm,
            datagramBuffer,
            out int datagramLength,
            preferMoonshineFraming: false
        );
        captureOk.Should().BeTrue();
        datagramLength.Should().BeGreaterThan(MicAudioPacket.RtpHeaderSize);

        bool ingestOk = uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));
        ingestOk.Should().BeTrue();

        bool pumpOk = uplink.PumpFrame(out int samplesProcessed);
        pumpOk.Should().BeTrue();
        samplesProcessed.Should().Be(480);

        HostMicSinkMetrics metrics = uplink.GetMetrics();
        metrics.TotalPacketsReceived.Should().Be(1);
        metrics.TotalSamplesRendered.Should().Be(480);
        metrics.LossCount.Should().Be(0);
    }

    [Fact]
    public void HostMicrophoneUplinkService_IngestMnbpDatagram_PushesAndDecodesPcmSuccessfully()
    {
        using var clientCapture = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10
        );

        using var uplink = new HostMicrophoneUplinkService(
            sampleRate: 48000,
            channels: 1,
            frameDurationMs: 10,
            ipcBridge: null,
            autoStartWorker: false
        );

        float[] inputPcm = GenerateSineWavePcm(480);
        byte[] datagramBuffer = new byte[512];

        bool captureOk = clientCapture.TryProcessRecordedFrame(
            inputPcm,
            datagramBuffer,
            out int datagramLength,
            preferMoonshineFraming: true
        );
        captureOk.Should().BeTrue();
        datagramLength.Should().BeGreaterThan(MoonshineProtocolConstants.HeaderSize + MoonshineMicPacketCodec.HeaderSize);

        bool ingestOk = uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));
        ingestOk.Should().BeTrue();

        bool pumpOk = uplink.PumpFrame(out int samplesProcessed);
        pumpOk.Should().BeTrue();
        samplesProcessed.Should().Be(480);

        HostMicSinkMetrics metrics = uplink.GetMetrics();
        metrics.TotalPacketsReceived.Should().Be(1);
        metrics.TotalSamplesRendered.Should().Be(480);
    }

    [Fact]
    public void HostMicrophoneUplinkService_IngestCustomMnbpMagicDatagram_DecodesPcmSuccessfully()
    {
        using var clientCapture = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10
        );

        using var uplink = new HostMicrophoneUplinkService(
            sampleRate: 48000,
            channels: 1,
            frameDurationMs: 10,
            ipcBridge: null,
            autoStartWorker: false
        );

        float[] inputPcm = GenerateSineWavePcm(480);
        byte[] datagramBuffer = new byte[512];

        bool captureOk = clientCapture.TryProcessRecordedFrame(
            inputPcm,
            datagramBuffer,
            out int datagramLength,
            preferMoonshineFraming: true
        );
        captureOk.Should().BeTrue();

        // Mutate magic to 0x314D5348 ('1MSH')
        BinaryPrimitives.WriteUInt32BigEndian(datagramBuffer.AsSpan(0, 4), 0x314D5348U);

        bool ingestOk = uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));
        ingestOk.Should().BeTrue();

        bool pumpOk = uplink.PumpFrame(out int samplesProcessed);
        pumpOk.Should().BeTrue();
        samplesProcessed.Should().Be(480);
    }

    [Fact]
    public void HostMicrophoneUplinkService_IngestAndPumpWithRealIpcBridge_RoutesAudioIntoVirtualDriver()
    {
        using var ipcBridge = new VirtualAudioIpcBridgePipeline(
            isHostServer: true,
            sampleRate: 48000,
            channels: 1
        );

        using var uplink = new HostMicrophoneUplinkService(
            sampleRate: 48000,
            channels: 1,
            frameDurationMs: 10,
            ipcBridge: ipcBridge,
            autoStartWorker: false
        );

        using var clientCapture = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10
        );

        float[] inputPcm = GenerateSineWavePcm(480);
        byte[] datagramBuffer = new byte[512];

        bool captureOk = clientCapture.TryProcessRecordedFrame(
            inputPcm,
            datagramBuffer,
            out int datagramLength,
            preferMoonshineFraming: true
        );
        captureOk.Should().BeTrue();

        bool ingestOk = uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));
        ingestOk.Should().BeTrue();

        bool pumpOk = uplink.PumpFrame(out int samplesProcessed);
        pumpOk.Should().BeTrue();
        samplesProcessed.Should().Be(480);

        bool metricsOk = ipcBridge.TryGetMetrics(out var ipcMetrics);
        metricsOk.Should().BeTrue();
        ipcMetrics.CapturePacketsWritten.Should().BeGreaterThan(0);
    }

    [Fact]
    public void HostMicrophoneUplinkService_DynamicGainAndMuteControls_OperateDeterministically()
    {
        using var uplink = new HostMicrophoneUplinkService(
            sampleRate: 48000,
            channels: 1,
            frameDurationMs: 10,
            ipcBridge: null,
            autoStartWorker: false
        );

        using var clientCapture = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10
        );

        uplink.SetGain(2.0f);
        uplink.SetMute(true);

        float[] inputPcm = GenerateSineWavePcm(480);
        byte[] datagramBuffer = new byte[512];

        clientCapture.TryProcessRecordedFrame(inputPcm, datagramBuffer, out int datagramLength, preferMoonshineFraming: false);
        uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));

        Span<float> pulledPcm = stackalloc float[480];
        bool pulled = uplink.Sink.TryPullPcm(pulledPcm, out int samplesRead);
        pulled.Should().BeTrue();
        samplesRead.Should().Be(480);

        foreach (float sample in pulledPcm)
        {
            sample.Should().Be(0.0f);
        }

        // Unmute and verify non-zero samples rendered
        uplink.SetMute(false);
        clientCapture.TryProcessRecordedFrame(inputPcm, datagramBuffer, out datagramLength, preferMoonshineFraming: false);
        uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));

        pulled = uplink.Sink.TryPullPcm(pulledPcm, out samplesRead);
        pulled.Should().BeTrue();
        samplesRead.Should().Be(480);

        float peak = 0.0f;
        foreach (float sample in pulledPcm)
        {
            if (MathF.Abs(sample) > peak) peak = MathF.Abs(sample);
        }
        peak.Should().BeGreaterThan(0.0f);
    }

    [Fact]
    public void HostMicrophoneUplinkService_StartStopLifecycle_TogglesWorkerThreadCorrectly()
    {
        using var uplink = new HostMicrophoneUplinkService(
            sampleRate: 48000,
            channels: 1,
            frameDurationMs: 10,
            ipcBridge: null,
            autoStartWorker: false
        );

        uplink.IsRunning.Should().BeFalse();

        bool startOk = uplink.Start();
        startOk.Should().BeTrue();
        uplink.IsRunning.Should().BeTrue();

        uplink.Stop();
        uplink.IsRunning.Should().BeFalse();

        // Restart
        startOk = uplink.Start();
        startOk.Should().BeTrue();
        uplink.IsRunning.Should().BeTrue();

        uplink.Stop();
        uplink.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void HostMicrophoneUplinkService_RapidDisposalAndGCImmunity_IsSafeAndIdempotent()
    {
        for (int i = 0; i < 5; i++)
        {
            var uplink = new HostMicrophoneUplinkService(
                sampleRate: 48000,
                channels: 1,
                frameDurationMs: 10,
                ipcBridge: null,
                autoStartWorker: true
            );

            uplink.IsRunning.Should().BeTrue();
            uplink.Dispose();
            uplink.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            uplink.IsInitialized.Should().BeFalse();
            uplink.IsRunning.Should().BeFalse();

            Action actIngest = () => uplink.IngestDatagram(new byte[16]);
            actIngest.Should().Throw<ObjectDisposedException>();

            Action actPump = () => uplink.PumpFrame();
            actPump.Should().Throw<ObjectDisposedException>();

            Action actGain = () => uplink.SetGain(1.0f);
            actGain.Should().Throw<ObjectDisposedException>();

            Action actMute = () => uplink.SetMute(true);
            actMute.Should().Throw<ObjectDisposedException>();

            Action actMetrics = () => uplink.GetMetrics();
            actMetrics.Should().Throw<ObjectDisposedException>();
        }
    }

    [Fact]
    public void HostMicrophoneUplinkService_SinkStarvation_PushesSilenceToIpcBridge()
    {
        using var ipcBridge = new VirtualAudioIpcBridgePipeline(
            isHostServer: true,
            sampleRate: 48000,
            channels: 1
        );

        using var uplink = new HostMicrophoneUplinkService(
            sampleRate: 48000,
            channels: 1,
            frameDurationMs: 10,
            ipcBridge: ipcBridge,
            autoStartWorker: false
        );

        // Pump frame with empty jitter buffer (starvation/underrun)
        bool result = uplink.PumpFrame(out int samplesProcessed);
        result.Should().BeTrue();
        samplesProcessed.Should().Be(480);

        // Virtual audio IPC bridge must have received the audio frame
        bool metricsOk = ipcBridge.TryGetMetrics(out var ipcMetrics);
        metricsOk.Should().BeTrue();
        ipcMetrics.CapturePacketsWritten.Should().Be(1);
    }

    [Fact]
    public void HostMicrophoneUplinkService_ConcurrentPumpAndDispose_NoToctouRace()
    {
        for (int i = 0; i < 10; i++)
        {
            var uplink = new HostMicrophoneUplinkService(
                sampleRate: 48000,
                channels: 1,
                frameDurationMs: 10,
                ipcBridge: null,
                autoStartWorker: false
            );

            var barrier = new Barrier(2);
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            var pumpThread = new Thread(() =>
            {
                barrier.SignalAndWait();
                try
                {
                    for (int k = 0; k < 100; k++)
                    {
                        try
                        {
                            uplink.PumpFrame();
                        }
                        // ALLOWED_EXCEPTION: ObjectDisposedException is expected when disposed concurrently
                        catch (ObjectDisposedException) { break; }
                    }
                }
                // ALLOWED_EXCEPTION: Collect any unexpected non-disposal exceptions
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            var disposeThread = new Thread(() =>
            {
                barrier.SignalAndWait();
                Thread.Yield();
                uplink.Dispose();
            });

            pumpThread.Start();
            disposeThread.Start();

            pumpThread.Join();
            disposeThread.Join();

            exceptions.Should().BeEmpty();
            uplink.IsInitialized.Should().BeFalse();
        }
    }
}
