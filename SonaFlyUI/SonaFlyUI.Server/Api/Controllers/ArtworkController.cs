using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/artwork")]
[AllowAnonymous]
public class ArtworkController : ControllerBase
{
    private readonly IArtworkService _artworkService;

    public ArtworkController(IArtworkService artworkService) => _artworkService = artworkService;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetArtwork(Guid id, CancellationToken ct)
    {
        var result = await _artworkService.OpenArtworkAsync(id, ct);
        if (result == null) return NotFound();

        // Sanitize invalid MIME types (e.g., "binary" from embedded art)
        var mime = result.MimeType;
        if (string.IsNullOrWhiteSpace(mime) || !mime.Contains('/'))
            mime = "image/jpeg";

        return File(result.Stream, mime, enableRangeProcessing: true);
    }
}
