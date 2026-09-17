using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Domain.Entities.Identification;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// SQLite cache for provider responses (upgrade plan 7.4). Keys are digests of
/// the lookup inputs and never contain API keys. Confirmed not-found results
/// are cached for a shorter period; transient failures are never cached.
/// </summary>
public static class ProviderCache
{
    public static readonly TimeSpan FoundLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan NotFoundLifetime = TimeSpan.FromDays(7);

    public sealed record CachedResponse(string? Json, bool IsNotFound);

    public static string Key(params string[] parts) => FileHashService.DigestText(string.Join('|', parts));

    public static async Task<CachedResponse?> TryGetAsync(
        SonaFlyDbContext db, string provider, string keyDigest, string queryShape, CancellationToken ct)
    {
        var entry = await db.ProviderCacheEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Provider == provider && e.CacheKeyDigest == keyDigest && e.QueryShape == queryShape, ct);
        if (entry == null || (entry.ExpiresUtc.HasValue && entry.ExpiresUtc.Value <= DateTime.UtcNow))
        {
            return null;
        }

        return new CachedResponse(entry.ResponseJson, entry.IsNotFound);
    }

    /// <summary>
    /// Inserts or refreshes one entry. Uses its own short save so a concurrent
    /// writer of the same key (two copies of one song analysed at once) is
    /// harmless: whichever lands second simply refreshes the row.
    /// </summary>
    public static async Task StoreAsync(
        SonaFlyDbContext db, string provider, string keyDigest, string queryShape,
        string? json, bool isNotFound, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expires = now + (isNotFound ? NotFoundLifetime : FoundLifetime);

        var updated = await db.ProviderCacheEntries
            .Where(e => e.Provider == provider && e.CacheKeyDigest == keyDigest && e.QueryShape == queryShape)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.ResponseJson, json)
                .SetProperty(e => e.IsNotFound, isNotFound)
                .SetProperty(e => e.HttpStatus, isNotFound ? 404 : 200)
                .SetProperty(e => e.FetchedUtc, now)
                .SetProperty(e => e.ExpiresUtc, expires)
                .SetProperty(e => e.ModifiedUtc, now), ct);
        if (updated > 0)
        {
            return;
        }

        var entry = new ProviderCacheEntry
        {
            Provider = provider,
            CacheKeyDigest = keyDigest,
            QueryShape = queryShape,
            ResponseJson = json,
            IsNotFound = isNotFound,
            HttpStatus = isNotFound ? 404 : 200,
            FetchedUtc = now,
            ExpiresUtc = expires
        };
        db.ProviderCacheEntries.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another worker inserted the same key first; its row is equally valid.
        }
        finally
        {
            db.Entry(entry).State = EntityState.Detached;
        }
    }
}
