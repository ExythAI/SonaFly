using System.Threading.Channels;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Infrastructure.BackgroundServices;

public class ScanQueue : IScanQueue
{
    /// <summary>
    /// Bounded rather than unbounded: the authoritative record of pending work is the ScanJob
    /// table, and an unbounded in-memory channel just lets a request loop allocate without limit
    /// (backlog N17). Deduplication happens against the database before anything reaches here,
    /// so the capacity only ever needs to cover distinct enabled roots.
    /// </summary>
    private const int Capacity = 64;

    private readonly Channel<ScanRequest> _queue = Channel.CreateBounded<ScanRequest>(
        new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    public bool TryEnqueue(ScanRequest request) => _queue.Writer.TryWrite(request);

    public ValueTask<ScanRequest> DequeueAsync(CancellationToken ct) =>
        _queue.Reader.ReadAsync(ct);
}
