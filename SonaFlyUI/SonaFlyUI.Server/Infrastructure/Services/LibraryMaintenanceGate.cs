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
    private readonly HashSet<Guid> _rootsPendingDeletion = [];
    private CancellationTokenSource? _runningScan;
    private Guid? _runningScanRootId;
    private TaskCompletionSource? _runningScanDrained;
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
    public Task<Lease> AcquireForScanAsync(CancellationToken ct) => AcquireForScanAsync(null, ct);

    /// <summary>
    /// Takes the library slot for a scan of one root, so deleting that root can interrupt this
    /// scan without disturbing scans of other roots.
    /// </summary>
    public async Task<Lease> AcquireForScanAsync(Guid? libraryRootId, CancellationToken ct)
    {
        await _exclusive.WaitAsync(ct);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_sync)
        {
            _runningScan = cts;
            _runningScanRootId = libraryRootId;
            _runningScanDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_maintenancePending || IsRootPendingDeletionLocked(libraryRootId)) cts.Cancel();
        }

        return new Lease(this, cts, isScan: true, isMaintenance: false);
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

        return new Lease(this, cancellation: null, isScan: false, isMaintenance: true);
    }

    /// <summary>
    /// True while the given root is being deleted. Queued scans of that root abandon their turn.
    /// </summary>
    public bool IsRootPendingDeletion(Guid libraryRootId)
    {
        lock (_sync) return _rootsPendingDeletion.Contains(libraryRootId);
    }

    private bool IsRootPendingDeletionLocked(Guid? libraryRootId) =>
        libraryRootId.HasValue && _rootsPendingDeletion.Contains(libraryRootId.Value);

    /// <summary>
    /// Guards deletion of one library root. Only a running scan of that same root is cancelled
    /// and drained; scans of other roots keep running, since the delete cascades rows owned by
    /// this root alone. A scan of the root that starts while the lease is held cancels itself.
    /// </summary>
    public async Task<RootDeletionLease> AcquireForRootDeletionAsync(Guid libraryRootId, CancellationToken ct)
    {
        Task? drained = null;
        lock (_sync)
        {
            _rootsPendingDeletion.Add(libraryRootId);
            if (_runningScan != null && _runningScanRootId == libraryRootId)
            {
                _runningScan.Cancel();
                drained = _runningScanDrained?.Task;
            }
        }

        var lease = new RootDeletionLease(this, libraryRootId);
        if (drained != null)
        {
            try
            {
                await drained.WaitAsync(ct);
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        return lease;
    }

    private void ReleaseRootDeletion(Guid libraryRootId)
    {
        lock (_sync) _rootsPendingDeletion.Remove(libraryRootId);
    }

    /// <summary>
    /// Takes the library slot for an approved catalog apply (review backlog U02).
    /// Unlike maintenance, this never cancels a running scan: approvals wait for
    /// the slot instead, so routine approvals cannot silently kill a scan. If
    /// maintenance (purge or root deletion) is already pending, the apply is
    /// refused so it cannot interleave with destructive work; the caller
    /// retries after maintenance completes.
    /// </summary>
    public async Task<Lease> AcquireForApplyAsync(CancellationToken ct)
    {
        await _exclusive.WaitAsync(ct);

        lock (_sync)
        {
            if (_maintenancePending)
            {
                _exclusive.Release();
                throw new InvalidOperationException(
                    "Library maintenance is in progress; retry the apply after it completes.");
            }
        }

        return new Lease(this, cancellation: null, isScan: false, isMaintenance: false);
    }

    private void Release(Lease lease)
    {
        lock (_sync)
        {
            if (lease.IsScan)
            {
                if (ReferenceEquals(_runningScan, lease.Cancellation))
                {
                    _runningScan = null;
                    _runningScanRootId = null;
                    _runningScanDrained?.TrySetResult();
                    _runningScanDrained = null;
                }
            }
            else if (lease.IsMaintenance)
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

        internal Lease(LibraryMaintenanceGate gate, CancellationTokenSource? cancellation, bool isScan, bool isMaintenance)
        {
            _gate = gate;
            Cancellation = cancellation;
            IsScan = isScan;
            IsMaintenance = isMaintenance;
        }

        internal CancellationTokenSource? Cancellation { get; }
        internal bool IsScan { get; }
        internal bool IsMaintenance { get; }

        /// <summary>The token a scan must observe, so maintenance can interrupt it.</summary>
        public CancellationToken Token => Cancellation?.Token ?? CancellationToken.None;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            _gate.Release(this);
        }
    }

    public sealed class RootDeletionLease : IDisposable
    {
        private readonly LibraryMaintenanceGate _gate;
        private readonly Guid _libraryRootId;
        private int _released;

        internal RootDeletionLease(LibraryMaintenanceGate gate, Guid libraryRootId)
        {
            _gate = gate;
            _libraryRootId = libraryRootId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            _gate.ReleaseRootDeletion(_libraryRootId);
        }
    }
}
