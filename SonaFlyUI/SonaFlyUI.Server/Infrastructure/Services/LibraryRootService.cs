using Microsoft.EntityFrameworkCore;
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
        // Validate no duplicate path
        var exists = await _db.LibraryRoots.AnyAsync(lr => lr.Path == request.Path, ct);
        if (exists)
            throw new InvalidOperationException($"A library root with path '{request.Path}' already exists.");

        // Validate path exists on filesystem
        if (!Directory.Exists(request.Path))
            throw new ArgumentException($"Path '{request.Path}' does not exist or is not accessible.");

        var entity = new LibraryRoot
        {
            Name = request.Name.Trim(),
            Path = request.Path.TrimEnd('/', '\\'),
            IsReadOnly = request.IsReadOnly,
            IsEnabled = true
        };

        _db.LibraryRoots.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task UpdateAsync(Guid id, UpdateLibraryRootRequest request, CancellationToken ct)
    {
        var entity = await _db.LibraryRoots.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Library root {id} not found.");

        if (request.Name != null) entity.Name = request.Name.Trim();
        if (request.Path != null)
        {
            var dup = await _db.LibraryRoots.AnyAsync(lr => lr.Path == request.Path && lr.Id != id, ct);
            if (dup) throw new InvalidOperationException($"Another library root already uses path '{request.Path}'.");
            entity.Path = request.Path.TrimEnd('/', '\\');
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
