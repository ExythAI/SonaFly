using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api")]
public class SystemController : ControllerBase
{
    private readonly SonaFlyDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<SystemController> _logger;

    public SystemController(SonaFlyDbContext db, IConfiguration config, ILogger<SystemController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "Healthy", timestamp = DateTime.UtcNow });

    [HttpGet("system/status")]
    [Authorize]
    public async Task<ActionResult<SystemStatusDto>> Status(CancellationToken ct)
    {
        var lastScan = await _db.ScanJobs.AsNoTracking()
            .OrderByDescending(j => j.CompletedUtc)
            .FirstOrDefaultAsync(ct);

        var currentScan = await _db.ScanJobs.AsNoTracking()
            .Where(j => j.Status == ScanStatus.Running || j.Status == ScanStatus.Queued)
            .FirstOrDefaultAsync(ct);

        return Ok(new SystemStatusDto(
            Version: "1.0.0-mvp",
            TotalTracks: await _db.Tracks.CountAsync(t => t.IsIndexed && !t.IsMissing, ct),
            TotalAlbums: await _db.Albums.CountAsync(ct),
            TotalArtists: await _db.Artists.CountAsync(ct),
            TotalGenres: await _db.Genres.CountAsync(ct),
            TotalPlaylists: await _db.Playlists.CountAsync(ct),
            LibraryRootCount: await _db.LibraryRoots.CountAsync(lr => lr.IsEnabled, ct),
            CurrentScanStatus: currentScan?.Status.ToString(),
            LastScanCompletedUtc: lastScan?.CompletedUtc
        ));
    }

    /// <summary>
    /// Purges all music library data (tracks, albums, artists, genres, playlists,
    /// artwork, scan jobs) while preserving users, roles, and library root configs.
    /// </summary>
    [HttpPost("system/purge")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PurgeLibraryData(CancellationToken ct)
    {
        _logger.LogWarning("Admin initiated library data purge.");

        // Delete in dependency order to avoid FK issues
        await _db.PlaylistItems.ExecuteDeleteAsync(ct);
        await _db.Playlists.ExecuteDeleteAsync(ct);
        await _db.TrackGenres.ExecuteDeleteAsync(ct);
        await _db.TrackArtists.ExecuteDeleteAsync(ct);
        await _db.Tracks.ExecuteDeleteAsync(ct);
        await _db.Albums.ExecuteDeleteAsync(ct);
        await _db.Artists.ExecuteDeleteAsync(ct);
        await _db.Genres.ExecuteDeleteAsync(ct);
        await _db.ArtworkAssets.ExecuteDeleteAsync(ct);
        await _db.ScanJobs.ExecuteDeleteAsync(ct);

        // Reset scan status on library roots (keep the roots themselves)
        await _db.LibraryRoots.ExecuteUpdateAsync(lr => lr
            .SetProperty(x => x.LastScanStartedUtc, (DateTime?)null)
            .SetProperty(x => x.LastScanCompletedUtc, (DateTime?)null)
            .SetProperty(x => x.LastScanStatus, (ScanStatus?)null)
            .SetProperty(x => x.LastScanError, (string?)null), ct);

        // Delete cached artwork files from disk
        var artworkRoot = _config["SonaFly:ArtworkRoot"] ?? "./data/artwork";
        if (Directory.Exists(artworkRoot))
        {
            try
            {
                Directory.Delete(artworkRoot, recursive: true);
                Directory.CreateDirectory(artworkRoot);
                _logger.LogInformation("Artwork cache cleared.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fully clear artwork directory.");
            }
        }

        _logger.LogWarning("Library data purge complete.");
        return Ok(new { message = "All library data purged. Library roots preserved." });
    }
}

