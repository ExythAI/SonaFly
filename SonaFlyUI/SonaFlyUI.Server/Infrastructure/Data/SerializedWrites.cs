using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SonaFlyUI.Server.Infrastructure.Data;

/// <summary>
/// Transactions for read-then-write invariants — "refuse this if it would remove the last
/// administrator" and the like — where checking and then acting must be one indivisible step.
/// </summary>
public static class SerializedWrites
{
    /// <summary>
    /// Begins a transaction that takes the database's write lock immediately, before the
    /// guard query runs.
    ///
    /// SQLite's ordinary <c>BEGIN</c> is deferred: it takes no lock until the first write, so
    /// two requests can both read "two enabled administrators", both decide they may proceed,
    /// and the second one's write then lands on state its check never saw. <c>BEGIN
    /// IMMEDIATE</c> makes the second request wait at the start instead, and re-read the truth
    /// once the first has committed.
    ///
    /// A competing writer that will not yield within the connection's timeout surfaces as a
    /// <see cref="SqliteException"/>; <see cref="IsWriteConflict"/> recognises it so the caller
    /// can answer "try again" rather than 500.
    /// </summary>
    public static async Task<IDbContextTransaction> BeginExclusiveAsync(
        this DbContext db, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();

        if (connection is SqliteConnection sqlite && db.Database.CurrentTransaction is null)
        {
            if (sqlite.State != ConnectionState.Open)
            {
                await sqlite.OpenAsync(ct);
            }

            var transaction = sqlite.BeginTransaction(IsolationLevel.Serializable, deferred: false);
            return await db.Database.UseTransactionAsync(transaction, ct)
                ?? throw new InvalidOperationException("The exclusive transaction was not adopted by the context.");
        }

        // Another provider, or a transaction already in progress: fall back to the strictest
        // isolation the provider offers.
        return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    }

    /// <summary>
    /// Whether this exception is SQLite refusing to grant the write lock, rather than a real
    /// failure. The caller lost a race and the request is safe to retry.
    /// </summary>
    public static bool IsWriteConflict(Exception ex) =>
        ex is SqliteException { SqliteErrorCode: SQLITE_BUSY or SQLITE_LOCKED } ||
        (ex.InnerException is not null && IsWriteConflict(ex.InnerException));

    private const int SQLITE_BUSY = 5;
    private const int SQLITE_LOCKED = 6;
}
