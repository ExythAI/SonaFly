using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/restrictions")]
[Authorize(Roles = "Admin")]
public class RestrictionsController : ControllerBase
{
    private readonly SonaFlyDbContext _db;

    public RestrictionsController(SonaFlyDbContext db) => _db = db;

    /// <summary>Get all restrictions for a specific user.</summary>
    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<List<UserRestrictionDto>>> GetForUser(Guid userId, CancellationToken ct)
    {
        var restrictions = await _db.UserRestrictions
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.RestrictionType).ThenBy(r => r.TargetName)
            .Select(r => new UserRestrictionDto(r.Id, r.UserId, r.RestrictionType.ToString(), r.TargetId, r.TargetName))
            .ToListAsync(ct);
        return Ok(restrictions);
    }

    /// <summary>Add a restriction for a user.</summary>
    [HttpPost]
    public async Task<ActionResult> Add([FromBody] AddRestrictionRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<RestrictionType>(request.RestrictionType, true, out var type))
            return BadRequest($"Invalid restriction type: {request.RestrictionType}");

        // Resolve target name for display
        var targetName = await ResolveTargetNameAsync(type, request.TargetId, ct);

        // Check for duplicate
        var exists = await _db.UserRestrictions.AnyAsync(r =>
            r.UserId == request.UserId &&
            r.RestrictionType == type &&
            r.TargetId == request.TargetId, ct);

        if (exists) return Conflict("Restriction already exists.");

        var restriction = new UserRestriction
        {
            UserId = request.UserId,
            RestrictionType = type,
            TargetId = request.TargetId,
            TargetName = targetName
        };

        _db.UserRestrictions.Add(restriction);
        await _db.SaveChangesAsync(ct);
        return Ok(new { restriction.Id });
    }

    /// <summary>Remove a specific restriction.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var restriction = await _db.UserRestrictions.FindAsync([id], ct);
        if (restriction == null) return NotFound();
        _db.UserRestrictions.Remove(restriction);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Get all restrictions for all users (admin overview).</summary>
    [HttpGet]
    public async Task<ActionResult<List<UserRestrictionDto>>> GetAll(CancellationToken ct)
    {
        var restrictions = await _db.UserRestrictions
            .Include(r => r.User)
            .OrderBy(r => r.User!.UserName).ThenBy(r => r.RestrictionType)
            .Select(r => new UserRestrictionDto(r.Id, r.UserId, r.RestrictionType.ToString(), r.TargetId, r.TargetName))
            .ToListAsync(ct);
        return Ok(restrictions);
    }

    private async Task<string?> ResolveTargetNameAsync(RestrictionType type, Guid targetId, CancellationToken ct)
    {
        return type switch
        {
            RestrictionType.Album => await _db.Albums.Where(a => a.Id == targetId).Select(a => a.Title).FirstOrDefaultAsync(ct),
            RestrictionType.Artist => await _db.Artists.Where(a => a.Id == targetId).Select(a => a.Name).FirstOrDefaultAsync(ct),
            RestrictionType.Genre => await _db.Genres.Where(g => g.Id == targetId).Select(g => g.Name).FirstOrDefaultAsync(ct),
            _ => null
        };
    }
}

public record UserRestrictionDto(Guid Id, Guid UserId, string RestrictionType, Guid TargetId, string? TargetName);
public record AddRestrictionRequest(Guid UserId, string RestrictionType, Guid TargetId);
