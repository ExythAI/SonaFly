using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.Common;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

public class LibraryRootService : ILibraryRootService
{
    private readonly SonaFlyDbContext _db;

    public LibraryRootService(SonaFlyDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LibraryRootDto>> GetAllAsync(CancellationToken ct)
    {
        return await _db.LibraryRoots
            .AsNoTracking()
            .OrderBy(lr => lr.Name)
            .Select(lr => MapToDto(lr))
            .ToListAsync(ct);
    }

    public async Task<LibraryRootDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var lr = await _db.LibraryRoots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return lr == null ? null : MapToDto(lr);
    }

    public async Task<Guid> CreateAsync(CreateLibraryRootRequest request, CancellationToken ct)
    {
        var path = await ValidateAndNormalizePathAsync(request.Path, excludeId: null, ct);

        var entity = new LibraryRoot
        {
            Name = request.Name.Trim(),
            Path = path,
            IsReadOnly = request.IsReadOnly,
            IsEnabled = true
        };

        _db.LibraryRoots.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    /// <summary>
    /// Canonicalizes a requested root path and applies the same accessibility and duplicate
    /// rules on create and on update.
    /// <para>
    /// Normalization happens before the duplicate check so the check sees the value that will
    /// actually be stored, and it goes through <see cref="FileSystemPaths"/> rather than
    /// trimming separators by hand — blind trimming turns "/" into "" and "C:\" into the
    /// drive-relative "C:" (backlog N18).
    /// </para>
    /// </summary>
    private async Task<string> ValidateAndNormalizePathAsync(string requestedPath, Guid? excludeId, CancellationToken ct)
    {
        if (!FileSystemPaths.TryNormalize(requestedPath, out var normalized, out var error))
            throw new ArgumentException(error);

        if (!Directory.Exists(normalized))
            throw new ArgumentException($"Path '{normalized}' does not exist or is not accessible.");

        // Compare canonicalized values: stored rows may predate normalization, and on Windows
        // two spellings differing only in case name the same directory.
        var existing = await _db.LibraryRoots
            .Where(lr => excludeId == null || lr.Id != excludeId)
            .Select(lr => lr.Path)
            .ToListAsync(ct);

        if (existing.Any(p => FileSystemPaths.AreSame(p, normalized)))
            throw new InvalidOperationException($"A library root with path '{normalized}' already exists.");

        return normalized;
    }

    public async Task UpdateAsync(Guid id, UpdateLibraryRootRequest request, CancellationToken ct)
    {
        var entity = await _db.LibraryRoots.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Library root {id} not found.");

        if (request.Name != null) entity.Name = request.Name.Trim();
        if (request.Path != null)
        {
            // Update now applies exactly the same validation as create; it previously accepted
            // an unreachable path silently.
            entity.Path = await ValidateAndNormalizePathAsync(request.Path, excludeId: id, ct);
        }
        if (request.IsEnabled.HasValue) entity.IsEnabled = request.IsEnabled.Value;
        if (request.IsReadOnly.HasValue) entity.IsReadOnly = request.IsReadOnly.Value;

        entity.ModifiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.LibraryRoots.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Library root {id} not found.");

        _db.LibraryRoots.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    private static LibraryRootDto MapToDto(LibraryRoot lr) => new(
        lr.Id, lr.Name, lr.Path, lr.IsEnabled, lr.IsReadOnly,
        lr.LastScanStartedUtc, lr.LastScanCompletedUtc,
        lr.LastScanStatus?.ToString(), lr.LastScanError
    );
}
