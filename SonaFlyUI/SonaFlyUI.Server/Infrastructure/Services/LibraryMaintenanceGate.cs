namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>
/// Serializes whole-library operations against each other.
/// <para>
/// A purge that runs while a scan is in flight deletes rows the scan is still writing, and the
/// scan then happily repopulates the library the admin just asked to empty. This gate gives
/// both sides one exclusive slot: maintenance first cancels any running scan, then waits for it
/// to drain before it touches anything (backlog N16).
/// </para>
/// <para>Registered as a singleton.</para>
/// </summary>
public sealed class LibraryMaintenanceGate
{
    private readonly SemaphoreSlim _exclusive = new(1, 1);
    private readonly Lock _sync = new();
    private CancellationTokenSource? _runningScan;
    private bool _maintenancePending;

    /// <summary>
    /// True while an admin has asked for maintenance. Queued scans check this so they abandon
    /// their turn instead of piling up behind the purge and repopulating it afterwards.
    /// </summary>
    public bool MaintenancePending
    {
        get { lock (_sync) return _maintenancePending; }
    }

    /// <summary>
    /// Takes the library slot for a scan. The returned lease carries a token that maintenance
    /// can cancel; pass it to the scan instead of the caller's own token.
    /// </summary>
    public async Task<Lease> AcquireForScanAsync(CancellationToken ct)
    {
        await _exclusive.WaitAsync(ct);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_sync)
        {
            _runningScan = cts;
            if (_maintenancePending) cts.Cancel();
        }

        return new Lease(this, cts, isScan: true);
    }

    /// <summary>
    /// Takes the library slot for destructive maintenance, first asking any running scan to stop.
    /// </summary>
    public async Task<Lease> AcquireForMaintenanceAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            _maintenancePending = true;
            _runningScan?.Cancel();
        }

        try
        {
            await _exclusive.WaitAsync(ct);
        }
        catch
        {
            lock (_sync) _maintenancePending = false;
            throw;
        }

        return new Lease(this, cancellation: null, isScan: false);
    }

    private void Release(Lease lease)
    {
        lock (_sync)
        {
            if (lease.IsScan)
            {
                if (ReferenceEquals(_runningScan, lease.Cancellation)) _runningScan = null;
            }
            else
            {
                _maintenancePending = false;
            }
        }

        lease.Cancellation?.Dispose();
        _exclusive.Release();
    }

    public sealed class Lease : IDisposable
    {
        private readonly LibraryMaintenanceGate _gate;
        private int _released;

        internal Lease(LibraryMaintenanceGate gate, CancellationTokenSource? cancellation, bool isScan)
        {
            _gate = gate;
            Cancellation = cancellation;
            IsScan = isScan;
        }

        internal CancellationTokenSource? Cancellation { get; }
        internal bool IsScan { get; }

        /// <summary>The token a scan must observe, so maintenance can interrupt it.</summary>
        public CancellationToken Token => Cancellation?.Token ?? CancellationToken.None;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            _gate.Release(this);
        }
    }
}
