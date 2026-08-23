using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using FluentAssertions;
using Moonshine.Core.Audio;
using Moonshine.Host.Audio;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Host.Tests;

/// <summary>
/// Comprehensive stress, packet loss, clock drift, and fault-tolerance integration tests
/// for the host microphone uplink subsystem.
/// </summary>
public sealed class MicrophoneUplinkStressTests
{
    private static float[] GenerateSineWavePcm(int sampleCount, uint sampleRate = 48000, float frequency = 440.0f, float amplitude = 0.4f)
    {
        float[] pcm = new float[sampleCount];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = amplitude * MathF.Sin(2.0f * MathF.PI * frequency * (i / (float)sampleRate));
        }
        return pcm;
    }

    [Fact]
    public void MicrophoneUplink_ContinuousConcurrentStreaming_StressTest()
    {
        using var uplink = new HostMicrophoneUplinkService(
            sampleRate: 48000,
            channels: 1,
            frameDurationMs: 10,
            ipcBridge: null,
            autoStartWorker: true
        );

        uplink.IsRunning.Should().BeTrue();

        const int WorkerCount = 4;
        const int FramesPerWorker = 500;
        const int TotalExpectedFrames = WorkerCount * FramesPerWorker;

        var barrier = new Barrier(WorkerCount + 1);
        var exceptions = new ConcurrentBag<Exception>();
        using var cts = new CancellationTokenSource();

        // 1. Launch 4 parallel client capture workers
        var workers = new Thread[WorkerCount];
        for (int w = 0; w < WorkerCount; w++)
        {
            int workerIndex = w;
            workers[w] = new Thread(() =>
            {
                using var clientCapture = new ClientMicrophoneCapturePipeline(
                    sampleRate: 48000,
                    channels: 1,
                    bitrate: 32000,
                    frameDurationMs: 10,
                    streamId: (uint)(0x1000 + workerIndex),
                    sessionId: (ulong)(0xCAFE0000 + workerIndex)
                );

                float[] inputPcm = GenerateSineWavePcm(480, sampleRate: 48000, frequency: 300.0f + (workerIndex * 150.0f));
                byte[] datagramBuffer = new byte[1500];

                barrier.SignalAndWait();

                try
                {
                    for (int i = 0; i < FramesPerWorker; i++)
                    {
                        bool preferMnbp = (i % 2 == 0);
                        bool captureOk = clientCapture.TryProcessRecordedFrame(
                            inputPcm,
                            datagramBuffer,
                            out int datagramLength,
                            preferMoonshineFraming: preferMnbp
                        );

                        captureOk.Should().BeTrue();
                        datagramLength.Should().BeGreaterThan(0);

                        bool ingestOk = uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));
                        ingestOk.Should().BeTrue();
                    }
                }
                // ALLOWED_EXCEPTION: Collect any unexpected exceptions from parallel workers for test assertion
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })
            {
                Name = $"MicStressWorker-{workerIndex}",
                IsBackground = true
            };
            workers[w].Start();
        }

        // 2. Launch concurrent control thread rapidly toggling mute and randomising gain
        var controlThread = new Thread(() =>
        {
            barrier.SignalAndWait();
            try
            {
                int iteration = 0;
                while (!cts.IsCancellationRequested)
                {
                    bool isMuted = (iteration % 4 == 0);
                    float gain = (float)(Random.Shared.NextDouble() * 3.0);

                    uplink.SetMute(isMuted);
                    uplink.SetGain(gain);

                    iteration++;
                    Thread.Yield();
                }
            }
            // ALLOWED_EXCEPTION: Collect any unexpected exceptions from control thread for test assertion
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })
        {
            Name = "MicStressControlThread",
            IsBackground = true
        };
        controlThread.Start();

        // 3. Wait for all 4 client capture workers to complete streaming 500 frames each
        for (int w = 0; w < WorkerCount; w++)
        {
            workers[w].Join();
        }

        // Stop control thread
        cts.Cancel();
        controlThread.Join();

        // Give background pump worker time to finish processing queued frames
        Thread.Sleep(100);
        uplink.Stop();

        // Verify zero exceptions occurred during concurrent operations
        exceptions.Should().BeEmpty();

        // 4. Assert telemetry metrics and output integrity
        HostMicSinkMetrics metrics = uplink.GetMetrics();
        metrics.TotalPacketsReceived.Should().Be(TotalExpectedFrames, "uplink service must ingest all 2,000 packets from the 4 concurrent client workers");
        metrics.TotalSamplesRendered.Should().BeGreaterThan(0, "pumping worker must render decoded samples");

        // Pull verified PCM frames from the sink and verify all samples are valid Float32 numbers
        Span<float> drainBuffer = stackalloc float[480];
        for (int i = 0; i < 5; i++)
        {
            bool pulled = uplink.Sink.TryPullPcm(drainBuffer, out int samplesRead);
            pulled.Should().BeTrue();
            samplesRead.Should().Be(480);

            foreach (float sample in drainBuffer)
            {
                float.IsNaN(sample).Should().BeFalse("decoded Float32 PCM must not contain NaN");
                float.IsInfinity(sample).Should().BeFalse("decoded Float32 PCM must not contain Infinity");
                sample.Should().BeInRange(-1.0f, 1.0f, "decoded Float32 PCM must remain within valid audio bounds");
            }
        }
    }

    [Fact]
    public void MicrophoneUplink_PacketLossAndPlcSynthesising_RecoversCleanly()
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

        const int TotalFrames = 100;
        const int ExpectedLossFrames = 25; // 25% loss (indices 2, 6, 10, ..., 98 dropped)
        const int ExpectedReceivedFrames = TotalFrames - ExpectedLossFrames; // 75 frames

        float[] inputPcm = GenerateSineWavePcm(480, sampleRate: 48000, frequency: 440.0f, amplitude: 0.5f);
        byte[] datagramBuffer = new byte[1500];

        Span<float> pulledPcm = stackalloc float[480];
        int totalPulledSamples = 0;

        for (int i = 0; i < TotalFrames; i++)
        {
            bool captureOk = clientCapture.TryProcessRecordedFrame(
                inputPcm,
                datagramBuffer,
                out int datagramLength,
                preferMoonshineFraming: false
            );
            captureOk.Should().BeTrue();

            // Simulate 25% packet loss on the network (drop frame index 2, 6, 10, ..., 98)
            bool isLost = (i % 4 == 2);
            if (!isLost)
            {
                bool ingestOk = uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));
                ingestOk.Should().BeTrue();
            }

            // Pull decoded/synthesised PCM from sink on each frame step
            bool pulled = uplink.Sink.TryPullPcm(pulledPcm, out int samplesRead);
            pulled.Should().BeTrue();
            samplesRead.Should().Be(480);
            totalPulledSamples += samplesRead;

            // Validate that libopus PLC synthesises valid non-NaN Float32 PCM without panics
            foreach (float sample in pulledPcm)
            {
                float.IsNaN(sample).Should().BeFalse("libopus PLC synthesised PCM must not contain NaN");
                float.IsInfinity(sample).Should().BeFalse("libopus PLC synthesised PCM must not contain Infinity");
                sample.Should().BeInRange(-1.0f, 1.0f, "libopus PLC synthesised PCM must remain within [-1.0f, 1.0f]");
            }
        }

        // Pull remaining buffered frames
        for (int i = 0; i < 4; i++)
        {
            bool pulled = uplink.Sink.TryPullPcm(pulledPcm, out int samplesRead);
            pulled.Should().BeTrue();
            samplesRead.Should().Be(480);
            totalPulledSamples += samplesRead;

            foreach (float sample in pulledPcm)
            {
                float.IsNaN(sample).Should().BeFalse();
                float.IsInfinity(sample).Should().BeFalse();
                sample.Should().BeInRange(-1.0f, 1.0f);
            }
        }

        // Verify packet loss and concealment metrics
        HostMicSinkMetrics metrics = uplink.GetMetrics();
        metrics.TotalPacketsReceived.Should().Be(ExpectedReceivedFrames, "uplink must receive exactly 75 packets under 25% loss");
        metrics.LossCount.Should().Be(ExpectedLossFrames, "native sink must accurately track all 25 lost sequence numbers");
        metrics.TotalSamplesRendered.Should().BeGreaterThanOrEqualTo((ulong)(ExpectedReceivedFrames * 480));
    }

    [Fact]
    public void MicrophoneUplink_ClockDriftBursts_TrimsExcessQueueWithoutClicks()
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

        float[] inputPcm = GenerateSineWavePcm(480, sampleRate: 48000, frequency: 440.0f, amplitude: 0.4f);
        byte[] datagramBuffer = new byte[1500];

        // 1. Stream rapid burst of 15 frames (simulating fast client clock exceeding queue depth of 4)
        const int BurstFrameCount = 15;
        for (int i = 0; i < BurstFrameCount; i++)
        {
            bool captureOk = clientCapture.TryProcessRecordedFrame(
                inputPcm,
                datagramBuffer,
                out int datagramLength,
                preferMoonshineFraming: false
            );
            captureOk.Should().BeTrue();

            bool ingestOk = uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));
            ingestOk.Should().BeTrue();
        }

        // Assert clock drift compensation trimmed excess frames to preserve sub-15ms target latency
        HostMicSinkMetrics metricsAfterBurst = uplink.GetMetrics();
        metricsAfterBurst.TotalPacketsReceived.Should().Be(BurstFrameCount);
        metricsAfterBurst.DriftCorrections.Should().Be(11, "drift compensation must trim 11 excess frames when 15 frames are pushed into a 4-frame queue");

        // 2. Drain the trimmed queue and verify valid Float32 PCM output without buffer corruption
        Span<float> pulledPcm = stackalloc float[480];
        for (int i = 0; i < 4; i++)
        {
            bool pulled = uplink.Sink.TryPullPcm(pulledPcm, out int samplesRead);
            pulled.Should().BeTrue();
            samplesRead.Should().Be(480);

            foreach (float sample in pulledPcm)
            {
                float.IsNaN(sample).Should().BeFalse("pulled PCM must not contain NaN");
                float.IsInfinity(sample).Should().BeFalse("pulled PCM must not contain Infinity");
                sample.Should().BeInRange(-1.0f, 1.0f);
            }
        }

        // 3. Resume streaming subsequent steady-state frames and assert clean continuous audio without click spikes
        float previousSample = 0.0f;
        for (int i = 0; i < 20; i++)
        {
            bool captureOk = clientCapture.TryProcessRecordedFrame(
                inputPcm,
                datagramBuffer,
                out int datagramLength,
                preferMoonshineFraming: false
            );
            captureOk.Should().BeTrue();

            bool ingestOk = uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));
            ingestOk.Should().BeTrue();

            bool pulled = uplink.Sink.TryPullPcm(pulledPcm, out int samplesRead);
            pulled.Should().BeTrue();
            samplesRead.Should().Be(480);

            // Assert continuous waveform transitions (no clicks or disconnections)
            foreach (float sample in pulledPcm)
            {
                float.IsNaN(sample).Should().BeFalse();
                float delta = MathF.Abs(sample - previousSample);
                delta.Should().BeLessThan(1.8f, "sample transitions must remain smooth without click spikes");
                previousSample = sample;
            }
        }

        HostMicSinkMetrics finalMetrics = uplink.GetMetrics();
        finalMetrics.TotalPacketsReceived.Should().Be(35);
        finalMetrics.DriftCorrections.Should().Be(11);
    }

    [Fact]
    public void MicrophoneUplink_DriverBridgeIntegration_WritesCapturePcmUnderContention()
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

        const int TotalFrames = 300;
        const int SamplesPerFrame = 480;

        var barrier = new Barrier(2);
        var exceptions = new ConcurrentBag<Exception>();

        // Thread 1: Continuous microphone ingestion and pumping to capture ring buffer
        var micThread = new Thread(() =>
        {
            float[] inputPcm = GenerateSineWavePcm(SamplesPerFrame, sampleRate: 48000, frequency: 440.0f, amplitude: 0.35f);
            byte[] datagramBuffer = new byte[1500];

            barrier.SignalAndWait();

            try
            {
                for (int i = 0; i < TotalFrames; i++)
                {
                    bool captureOk = clientCapture.TryProcessRecordedFrame(
                        inputPcm,
                        datagramBuffer,
                        out int datagramLength,
                        preferMoonshineFraming: (i % 2 == 0)
                    );
                    captureOk.Should().BeTrue();

                    bool ingestOk = uplink.IngestDatagram(datagramBuffer.AsSpan(0, datagramLength));
                    ingestOk.Should().BeTrue();

                    bool pumpOk = uplink.PumpFrame(out int samplesProcessed);
                    pumpOk.Should().BeTrue();
                    samplesProcessed.Should().Be(SamplesPerFrame);
                }
            }
            // ALLOWED_EXCEPTION: Collect any unexpected exceptions from mic worker for test assertion
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })
        {
            Name = "MicIngestAndPumpWorker",
            IsBackground = true
        };

        // Thread 2: Concurrent reader reading render audio from speaker ring buffer
        var renderThread = new Thread(() =>
        {
            Span<float> renderBuffer = stackalloc float[SamplesPerFrame];
            barrier.SignalAndWait();

            try
            {
                for (int i = 0; i < TotalFrames; i++)
                {
                    int read = ipcBridge.ReadRenderPcm(renderBuffer, waitEvent: false, timeoutMs: 5);

                    // Unpumped render channel underruns safely and zero-pads dest buffer
                    foreach (float sample in renderBuffer)
                    {
                        float.IsNaN(sample).Should().BeFalse("render buffer output must not contain NaN under contention");
                        float.IsInfinity(sample).Should().BeFalse("render buffer output must not contain Infinity under contention");
                        sample.Should().BeInRange(-1.0f, 1.0f);
                    }

                    Thread.Yield();
                }
            }
            // ALLOWED_EXCEPTION: Collect any unexpected exceptions from render worker for test assertion
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })
        {
            Name = "RenderReadWorker",
            IsBackground = true
        };

        micThread.Start();
        renderThread.Start();

        micThread.Join();
        renderThread.Join();

        // Assert zero exceptions across both concurrent threads
        exceptions.Should().BeEmpty();

        // Verify IPC bridge metrics
        bool metricsOk = ipcBridge.TryGetMetrics(out var ipcMetrics);
        metricsOk.Should().BeTrue();
        ipcMetrics.CapturePacketsWritten.Should().Be(TotalFrames, "IPC bridge must record all 300 capture frames written");

        // Verify uplink telemetry metrics
        HostMicSinkMetrics uplinkMetrics = uplink.GetMetrics();
        uplinkMetrics.TotalPacketsReceived.Should().Be(TotalFrames);
        uplinkMetrics.TotalSamplesRendered.Should().Be((ulong)(TotalFrames * SamplesPerFrame));
    }
}
