using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/scans")]
[Authorize]
public class ScansController : ControllerBase
{
    private readonly SonaFlyDbContext _db;

    public ScansController(SonaFlyDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScanJobDto>>> GetAll(CancellationToken ct)
    {
        var jobs = await _db.ScanJobs
            .AsNoTracking()
            .Include(j => j.LibraryRoot)
            .OrderByDescending(j => j.StartedUtc)
            .Take(50)
            .Select(j => LibraryIndexService.MapScanJob(j, j.LibraryRoot.Name))
            .ToListAsync(ct);

        return Ok(jobs);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScanJobDto>> GetById(Guid id, CancellationToken ct)
    {
        var job = await _db.ScanJobs
            .AsNoTracking()
            .Include(j => j.LibraryRoot)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

        if (job == null) return NotFound();
        return Ok(LibraryIndexService.MapScanJob(job, job.LibraryRoot.Name));
    }

    [HttpGet("current")]
    public async Task<ActionResult<ScanJobDto?>> GetCurrent(CancellationToken ct)
    {
        var job = await _db.ScanJobs
            .AsNoTracking()
            .Include(j => j.LibraryRoot)
            .Where(j => j.Status == Domain.Enums.ScanStatus.Running || j.Status == Domain.Enums.ScanStatus.Queued)
            .OrderByDescending(j => j.StartedUtc)
            .FirstOrDefaultAsync(ct);

        if (job == null) return Ok(new { message = "No scan currently running." });
        return Ok(LibraryIndexService.MapScanJob(job, job.LibraryRoot.Name));
    }
}
