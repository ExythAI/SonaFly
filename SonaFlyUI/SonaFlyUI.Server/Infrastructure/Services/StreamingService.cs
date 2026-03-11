using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

public class StreamingService : IStreamingService
{
    private readonly SonaFlyDbContext _db;

    public StreamingService(SonaFlyDbContext db)
    {
        _db = db;
    }

    public async Task<StreamableTrackResult?> GetStreamableTrackAsync(Guid trackId, CancellationToken ct)
    {
        var track = await _db.Tracks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == trackId && t.IsIndexed && !t.IsMissing, ct);

        if (track == null) return null;

        if (!File.Exists(track.FilePath))
            return null;

        return new StreamableTrackResult(
            track.FilePath,
            track.MimeType,
            track.FileName,
            track.FileSizeBytes
        );
    }
}
