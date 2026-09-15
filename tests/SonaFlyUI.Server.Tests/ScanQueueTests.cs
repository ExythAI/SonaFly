using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Infrastructure.BackgroundServices;
using Xunit;

namespace SonaFlyUI.Server.Tests;

public sealed class ScanQueueTests
{
    [Fact]
    public async Task AFullQueueRejectsRatherThanSilentlyDroppingTheNextRequest()
    {
        var queue = new ScanQueue();
        var rootId = Guid.NewGuid();

        for (var index = 0; index < 64; index++)
            Assert.True(queue.TryEnqueue(new ScanRequest(rootId, false, Guid.NewGuid())));

        var rejected = new ScanRequest(rootId, false, Guid.NewGuid());
        Assert.False(queue.TryEnqueue(rejected));

        for (var index = 0; index < 64; index++)
            await queue.DequeueAsync(CancellationToken.None);

        Assert.True(queue.TryEnqueue(rejected));
        Assert.Equal(rejected, await queue.DequeueAsync(CancellationToken.None));
    }
}
