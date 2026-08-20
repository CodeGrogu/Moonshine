using FluentAssertions;
using Moonshine.Core.Session;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineStreamSessionTests
{
    [Fact]
    public async Task MoonshineStreamSession_CreateAndDisposeAsync_InitializesCleanly()
    {
        var config = new StreamConfiguration(
            Width: 1920,
            Height: 1080,
            Fps: 60,
            BitrateKbps: 20000,
            Codec: 1, // HEVC
            EnableHdr: false,
            AudioChannels: 2
        );

        var session = new MoonshineStreamSession("127.0.0.1", config, rtspPort: 48010);
        var act = async () => await session.DisposeAsync();
        await act.Should().NotThrowAsync();
    }
}
