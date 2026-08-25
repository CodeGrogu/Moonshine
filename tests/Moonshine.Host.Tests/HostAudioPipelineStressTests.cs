using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Moonshine.Host.Audio;
using Xunit;

namespace Moonshine.Host.Tests;

[Collection("HardwareExclusive")]
public class HostAudioPipelineStressTests
{
    [Fact]
    public void HostAudioPipeline_ConcurrentEncodeAndBitrateReconfigure_StressTest()
    {
        using var pipeline = new MoonshineHostAudioPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Stereo,
            bitrate: 160000,
            frameDurationMs: 5
        );

        var pcm = new float[480];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0);
        }

        const int ThreadCount = 8;
        const int IterationsPerThread = 500;
        var barrier = new Barrier(ThreadCount + 1);
        var exceptions = new ConcurrentBag<Exception>();
        long totalPackets = 0;

        var encodeThreads = new Thread[ThreadCount];
        for (int t = 0; t < ThreadCount; t++)
        {
            encodeThreads[t] = new Thread(() =>
            {
                barrier.SignalAndWait();
                try
                {
                    for (int i = 0; i < IterationsPerThread; i++)
                    {
                        bool ok = pipeline.ProcessPcmFrame(pcm, datagram =>
                        {
                            if (datagram.Length > 0)
                            {
                                Interlocked.Increment(ref totalPackets);
                            }
                        }, preferMoonshineFraming: true);

                        ok.Should().BeTrue();
                    }
                }
                // ALLOWED_EXCEPTION: Collect any unexpected exceptions from parallel workers for test assertion
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
            encodeThreads[t].Start();
        }

        // Reconfiguration thread
        var reconfigThread = new Thread(() =>
        {
            barrier.SignalAndWait();
            try
            {
                uint[] bitrates = [64000, 128000, 160000, 256000, 320000, 450000];
                for (int i = 0; i < IterationsPerThread; i++)
                {
                    pipeline.ReconfigureBitrate(bitrates[i % bitrates.Length]);
                }
            }
            // ALLOWED_EXCEPTION: Collect any unexpected exceptions from parallel workers for test assertion
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });
        reconfigThread.Start();

        for (int t = 0; t < ThreadCount; t++)
        {
            encodeThreads[t].Join();
        }
        reconfigThread.Join();

        exceptions.Should().BeEmpty();
        totalPackets.Should().Be((long)ThreadCount * IterationsPerThread);
    }

    [Fact]
    public void HostAudioPipeline_RapidStartStopDispose_StressTest()
    {
        for (int iteration = 0; iteration < 25; iteration++)
        {
            using var pipeline = new MoonshineHostAudioPipeline(
                sampleRate: 48000,
                topology: AudioChannelTopology.Stereo,
                bitrate: 160000,
                frameDurationMs: 5
            );

            long packetCount = 0;
            pipeline.Start(_ => Interlocked.Increment(ref packetCount), preferMoonshineFraming: true);

            // Let worker run briefly
            Thread.Sleep(5);

            pipeline.Stop();
            pipeline.IsRunning.Should().BeFalse();
        }
    }

    [Fact]
    public void HostAudioPipeline_MultiChannelTopologies_ConcurrentStressTest()
    {
        AudioChannelTopology[] topologies =
        [
            AudioChannelTopology.Mono,
            AudioChannelTopology.Stereo,
            AudioChannelTopology.Surround51,
            AudioChannelTopology.Surround71
        ];

        var exceptions = new ConcurrentBag<Exception>();

        Parallel.ForEach(topologies, new ParallelOptions { MaxDegreeOfParallelism = 4 }, topology =>
        {
            try
            {
                int channels = (int)topology;
                using var pipeline = new MoonshineHostAudioPipeline(
                    sampleRate: 48000,
                    topology: topology,
                    bitrate: (uint)(96000 * channels),
                    frameDurationMs: 5
                );

                var pcm = new float[240 * channels];
                for (int i = 0; i < pcm.Length; i++)
                {
                    pcm[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0);
                }

                for (int i = 0; i < 500; i++)
                {
                    bool ok = pipeline.ProcessPcmFrame(pcm, datagram =>
                    {
                        datagram.Length.Should().BeGreaterThan(56);
                    }, preferMoonshineFraming: true);

                    ok.Should().BeTrue();
                }
            }
            // ALLOWED_EXCEPTION: Collect any unexpected exceptions from parallel workers for test assertion
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty();
    }
}
