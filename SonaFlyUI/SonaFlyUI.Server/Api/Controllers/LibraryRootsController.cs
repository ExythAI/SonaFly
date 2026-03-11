using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/library-roots")]
[Authorize(Roles = "Admin")]
public class LibraryRootsController : ControllerBase
{
    private readonly ILibraryRootService _libraryRootService;
    private readonly IScanQueue _scanQueue;

    public LibraryRootsController(ILibraryRootService libraryRootService, IScanQueue scanQueue)
    {
        _libraryRootService = libraryRootService;
        _scanQueue = scanQueue;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LibraryRootDto>>> GetAll(CancellationToken ct)
    {
        var roots = await _libraryRootService.GetAllAsync(ct);
        return Ok(roots);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LibraryRootDto>> GetById(Guid id, CancellationToken ct)
    {
        var root = await _libraryRootService.GetByIdAsync(id, ct);
        if (root == null) return NotFound();
        return Ok(root);
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreateLibraryRootRequest request, CancellationToken ct)
    {
        var id = await _libraryRootService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLibraryRootRequest request, CancellationToken ct)
    {
        await _libraryRootService.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _libraryRootService.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/scan")]
    public async Task<IActionResult> TriggerScan(Guid id, CancellationToken ct)
    {
        // Verify root exists
        var root = await _libraryRootService.GetByIdAsync(id, ct);
        if (root == null) return NotFound();

        await _scanQueue.EnqueueAsync(id, ct);
        return Accepted(new { message = "Scan queued." });
    }
}
