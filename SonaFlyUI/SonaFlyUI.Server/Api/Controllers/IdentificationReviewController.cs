using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SonaFlyUI.Server.Infrastructure.Identification;

namespace SonaFlyUI.Server.Api.Controllers;

public record ReviewDecisionsRequest(IReadOnlyList<ReviewDecision> Items);

/// <summary>
/// Review of identification results: which tracks have a match that disagrees with the
/// catalog, and applying, rejecting or undoing those corrections. Administrators only.
/// Audio files are never written; see <see cref="IdentificationReviewService"/>.
/// </summary>
[ApiController]
[Route("api/identification")]
[Authorize(Roles = "Admin")]
public class IdentificationReviewController(IdentificationReviewService review) : ControllerBase
{
    private const int MaxPageSize = 200;

    /// <summary>The most decisions one request may carry.</summary>
    private const int MaxBatch = 500;

    private Guid? ReviewerId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>
    /// Tracks whose best match would change their title or artist, best matches first.
    /// <paramref name="band"/> is "High", "Review" or "Unresolved".
    /// </summary>
    [HttpGet("review")]
    public async Task<ActionResult<ReviewPageDto>> Pending(
        [FromQuery] Guid? rootId, [FromQuery] string? band,
        [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await review.PendingAsync(rootId, band, Math.Max(0, skip), Math.Clamp(take, 1, MaxPageSize), ct));

    /// <summary>Corrections that have been applied, newest first.</summary>
    [HttpGet("review/applied")]
    public async Task<ActionResult<IReadOnlyList<AppliedChangeDto>>> Applied(
        [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await review.AppliedAsync(Math.Clamp(take, 1, MaxPageSize), ct));

    /// <summary>Applies the chosen matches to the catalog.</summary>
    [HttpPost("proposals/apply-catalog")]
    public async Task<ActionResult<ReviewOutcomeDto>> Apply([FromBody] ReviewDecisionsRequest request, CancellationToken ct)
    {
        if (Refuse(request) is { } refusal) return refusal;
        try
        {
            return Ok(await review.ApplyAsync(request.Items, ReviewerId, ct));
        }
        catch (InvalidOperationException ex)
        {
            // Raised by the maintenance gate while a purge or root deletion is under way.
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Marks the chosen matches as wrong so they are not offered again.</summary>
    [HttpPost("proposals/reject")]
    public async Task<ActionResult<ReviewOutcomeDto>> Reject([FromBody] ReviewDecisionsRequest request, CancellationToken ct)
    {
        if (Refuse(request) is { } refusal) return refusal;
        return Ok(await review.RejectAsync(request.Items, ReviewerId, ct));
    }

    /// <summary>Undoes an applied correction.</summary>
    [HttpPost("proposals/{id:guid}/revert")]
    public async Task<IActionResult> Revert(Guid id, CancellationToken ct)
    {
        try
        {
            var refusal = await review.RevertAsync(id, ReviewerId, ct);
            return refusal == null ? NoContent() : Conflict(new { message = refusal });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    private ActionResult? Refuse(ReviewDecisionsRequest? request)
    {
        if (request?.Items == null || request.Items.Count == 0)
            return BadRequest(new { message = "Choose at least one match." });
        if (request.Items.Count > MaxBatch)
            return BadRequest(new { message = $"At most {MaxBatch} matches can be decided at once." });
        return null;
    }
}
