using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/library-roots")]
[Authorize(Roles = "Admin")]
public class LibraryRootsController : ControllerBase
{
    private readonly ILibraryRootService _libraryRootService;
    private readonly IScanQueue _scanQueue;
    private readonly SonaFlyDbContext _db;

    public LibraryRootsController(ILibraryRootService libraryRootService, IScanQueue scanQueue, SonaFlyDbContext db)
    {
        _libraryRootService = libraryRootService;
        _scanQueue = scanQueue;
        _db = db;
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

    /// <summary>
    /// Queues a scan. The ScanJob row is written before the request is accepted, so the queued
    /// work is visible in scan history and survives a restart instead of evaporating with the
    /// in-memory channel (backlog N17). Repeated clicks collapse onto the one pending job.
    /// </summary>
    [HttpPost("{id:guid}/scan")]
    public async Task<IActionResult> TriggerScan(Guid id, [FromQuery] bool fullScan = false, CancellationToken ct = default)
    {
        var root = await _db.LibraryRoots.FirstOrDefaultAsync(lr => lr.Id == id, ct);
        if (root == null) return NotFound();

        if (!root.IsEnabled)
            return Conflict(new { message = "This library root is disabled. Enable it before scanning." });

        // Deduplicate against the database, not against the channel: a job already queued or
        // running for this root is the same work.
        var pending = await _db.ScanJobs
            .Where(j => j.LibraryRootId == id &&
                        (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running))
            .OrderByDescending(j => j.StartedUtc)
            .FirstOrDefaultAsync(ct);

        if (pending != null)
        {
            return Accepted(new
            {
                scanJobId = pending.Id,
                status = pending.Status.ToString(),
                message = "A scan is already queued or running for this library root."
            });
        }

        var job = new ScanJob
        {
            LibraryRootId = id,
            Status = ScanStatus.Queued
        };
        _db.ScanJobs.Add(job);

        root.LastScanStatus = ScanStatus.Queued;
        root.LastScanError = null;
        await _db.SaveChangesAsync(ct);

        if (!_scanQueue.TryEnqueue(new ScanRequest(id, fullScan, job.Id)))
        {
            // The worker is saturated. Fail the job we just persisted rather than leaving a
            // Queued row that nothing will ever pick up.
            job.Status = ScanStatus.Failed;
            job.CompletedUtc = DateTime.UtcNow;
            job.ErrorSummary = "Scan queue is full; try again once running scans finish.";
            root.LastScanStatus = ScanStatus.Failed;
            root.LastScanError = job.ErrorSummary;
            await _db.SaveChangesAsync(ct);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { scanJobId = job.Id, message = job.ErrorSummary });
        }

        return Accepted(new
        {
            scanJobId = job.Id,
            status = job.Status.ToString(),
            message = fullScan ? "Full scan queued." : "Scan queued."
        });
    }
}
