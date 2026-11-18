using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/stream")]
public class StreamController(IStreamingService streamingService, StreamTicketService tickets,
    SonaFlyDbContext db, TranscodeCache transcodes) : ControllerBase
{
    /// <summary>The value of <c>quality</c> that asks for a smaller, compressed stream.</summary>
    public const string CompressedQuality = "compressed";

    private static bool IsCompressed(string? quality) =>
        string.Equals(quality, CompressedQuality, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A short-lived URL a player can open without a bearer token.
    /// <paramref name="quality"/> = "compressed" asks for an MP3 copy of lossless files,
    /// for slow or metered connections; anything else means the file as stored.
    /// </summary>
    [Authorize]
    [HttpGet("tracks/{id:guid}/url")]
    public async Task<IActionResult> GetUrl(Guid id, CancellationToken ct, [FromQuery] string? quality = null)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsEnabled, ct);
        if (user == null) return Unauthorized();
        if (await streamingService.GetStreamableTrackAsync(id, userId, ct) == null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        var token = tickets.Create(userId, id, user.SecurityStamp);
        var url = $"{Request.PathBase}/api/stream/tracks/{id}?ticket={Uri.EscapeDataString(token)}";
        if (IsCompressed(quality)) url += $"&quality={CompressedQuality}";
        return Ok(new { url });
    }

    [HttpGet("tracks/{id:guid}")]
    public async Task<IActionResult> StreamTrack(Guid id, [FromQuery] string? ticket, CancellationToken ct,
        [FromQuery] string? quality = null)
    {
        var grant = tickets.Validate(ticket, id);
        Guid userId;
        if (grant != null)
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == grant.UserId && u.IsEnabled, ct);
            if (user == null || user.SecurityStamp != grant.SecurityStamp) return Unauthorized();
            userId = user.Id;
        }
        else if (User.Identity?.IsAuthenticated == true &&
                 Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId))
        {
            if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsEnabled, ct)) return Unauthorized();
        }
        else return Unauthorized();

        var result = await streamingService.GetStreamableTrackAsync(id, userId, ct);
        if (result == null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";

        // A compressed copy when one was asked for and can be had; otherwise the original.
        // The copy is a complete file, so range requests and seeking work on it as usual.
        if (IsCompressed(quality) &&
            await transcodes.GetOrCreateAsync(result.FilePath, ct) is { } compressed)
        {
            var copy = new FileStream(compressed, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return File(copy, TranscodeCache.MimeType,
                Path.ChangeExtension(result.FileName, ".mp3"), enableRangeProcessing: true);
        }

        var stream = new FileStream(result.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, result.MimeType, result.FileName, enableRangeProcessing: true);
    }
}
