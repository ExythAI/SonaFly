namespace SonaFlyUI.Server.Domain.Enums;

public enum ScanStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,

    /// <summary>
    /// The scan finished but could not reach part of the tree, so absent files were not
    /// reconciled as deleted and orphan cleanup was skipped (backlog N13). New values go
    /// at the end: the enum is persisted by ordinal.
    /// </summary>
    Partial
}
