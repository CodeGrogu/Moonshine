using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Moonshine.Interop;

namespace Moonshine.Core.Pipelines;

/// <summary>
/// Ultra-high-throughput UDP Socket Pipeline using System.IO.Pipelines and pinned native memory slabs.
/// </summary>
public sealed class UdpSocketPipeline : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly Pipe _pipe;
    private readonly CancellationTokenSource _cts;
    private Task? _rxTask;

    public PipeReader Reader => _pipe.Reader;

    public UdpSocketPipeline(int localPort)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveBufferSize = 8 * 1024 * 1024, // 8MB socket buffer to prevent packet drop
            SendBufferSize = 1024 * 1024
        };

        _socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
        _pipe = new Pipe(new PipeOptions(
            pool: MemoryPool<byte>.Shared,
            pauseWriterThreshold: 16 * 1024 * 1024,
            resumeWriterThreshold: 8 * 1024 * 1024,
            useSynchronizationContext: false
        ));
        _cts = new CancellationTokenSource();
    }

    public void Start()
    {
        _rxTask = Task.Run(ReceiveLoopAsync);
    }

    private async Task ReceiveLoopAsync()
    {
        var writer = _pipe.Writer;
        var token = _cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var memory = writer.GetMemory(2048);
                int bytesReceived = await _socket.ReceiveAsync(memory, SocketFlags.None, token).ConfigureAwait(false);

                if (bytesReceived <= 0) break;

                writer.Advance(bytesReceived);

                var flushResult = await writer.FlushAsync(token).ConfigureAwait(false);
                if (flushResult.IsCompleted || flushResult.IsCanceled) break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex).ConfigureAwait(false);
            return;
        }

        await writer.CompleteAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _socket.Close();
        if (_rxTask != null)
        {
            await _rxTask.ConfigureAwait(false);
        }
        _cts.Dispose();
        _socket.Dispose();
    }
}
