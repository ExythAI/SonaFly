using System.Threading.Channels;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Infrastructure.BackgroundServices;

public class ScanQueue : IScanQueue
{
    private readonly Channel<ScanRequest> _queue = Channel.CreateUnbounded<ScanRequest>(new UnboundedChannelOptions
    {
        SingleReader = true
    });

    public ValueTask EnqueueAsync(ScanRequest request, CancellationToken ct) =>
        _queue.Writer.WriteAsync(request, ct);

    public ValueTask<ScanRequest> DequeueAsync(CancellationToken ct) =>
        _queue.Reader.ReadAsync(ct);
}
