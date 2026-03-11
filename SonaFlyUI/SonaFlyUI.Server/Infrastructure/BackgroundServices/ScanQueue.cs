using System.Threading.Channels;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Infrastructure.BackgroundServices;

public class ScanQueue : IScanQueue
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true
    });

    public ValueTask EnqueueAsync(Guid libraryRootId, CancellationToken ct) =>
        _queue.Writer.WriteAsync(libraryRootId, ct);

    public ValueTask<Guid> DequeueAsync(CancellationToken ct) =>
        _queue.Reader.ReadAsync(ct);
}
