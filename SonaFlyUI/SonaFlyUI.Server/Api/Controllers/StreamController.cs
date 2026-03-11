using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/stream")]
[AllowAnonymous]
public class StreamController : ControllerBase
{
    private readonly IStreamingService _streamingService;

    public StreamController(IStreamingService streamingService) => _streamingService = streamingService;

    [HttpGet("tracks/{id:guid}")]
    public async Task<IActionResult> StreamTrack(Guid id, CancellationToken ct)
    {
        var result = await _streamingService.GetStreamableTrackAsync(id, ct);
        if (result == null) return NotFound();

        var stream = new FileStream(result.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, result.MimeType, result.FileName, enableRangeProcessing: true);
    }
}
