using System.Net;
using FluentAssertions;
using Moonshine.Core.Pipelines;
using Xunit;

namespace Moonshine.Core.Tests;

public class UdpSocketPipelineTests
{
    [Fact]
    public async Task UdpSocketPipeline_CreateAndDisposeAsync_GracefullyCleansUpResources()
    {
        var pipeline = new UdpSocketPipeline(localPort: 0);

        pipeline.Reader.Should().NotBeNull();
        var act = async () => await pipeline.DisposeAsync();
        await act.Should().NotThrowAsync();
    }
}
