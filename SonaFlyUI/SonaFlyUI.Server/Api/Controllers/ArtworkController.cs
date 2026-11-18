using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/artwork")]
[AllowAnonymous]
public class ArtworkController : ControllerBase
{
    private readonly IArtworkService _artworkService;

    public ArtworkController(IArtworkService artworkService) => _artworkService = artworkService;

    /// <summary>
    /// A cover image. <paramref name="size"/> asks for a copy scaled to about that many
    /// pixels, which is what grids and lists should request; without it the original is
    /// returned.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetArtwork(Guid id, [FromQuery] int? size, CancellationToken ct)
    {
        var result = await _artworkService.OpenThumbnailAsync(id, size, ct);
        if (result == null) return NotFound();

        // Sanitize invalid MIME types (e.g., "binary" from embedded art)
        var mime = result.MimeType;
        if (string.IsNullOrWhiteSpace(mime) || !mime.Contains('/'))
            mime = "image/jpeg";

        // An artwork ID names one image for good: the file is stored under the hash of its
        // content, and a different picture gets a different ID. So a client may keep its
        // copy indefinitely instead of fetching every cover again on every screen.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        var tag = new EntityTagHeaderValue($"\"{id:N}-{result.FileName}\"");

        return File(result.Stream, mime, lastModified: null, entityTag: tag, enableRangeProcessing: true);
    }
}
