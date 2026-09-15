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
    SonaFlyDbContext db) : ControllerBase
{
    [Authorize]
    [HttpGet("tracks/{id:guid}/url")]
    public async Task<IActionResult> GetUrl(Guid id, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsEnabled, ct);
        if (user == null) return Unauthorized();
        if (await streamingService.GetStreamableTrackAsync(id, userId, ct) == null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        var token = tickets.Create(userId, id, user.SecurityStamp);
        return Ok(new { url = $"{Request.PathBase}/api/stream/tracks/{id}?ticket={Uri.EscapeDataString(token)}" });
    }

    [HttpGet("tracks/{id:guid}")]
    public async Task<IActionResult> StreamTrack(Guid id, [FromQuery] string? ticket, CancellationToken ct)
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
        var stream = new FileStream(result.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, result.MimeType, result.FileName, enableRangeProcessing: true);
    }
}
