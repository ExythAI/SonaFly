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

    private bool IsAdmin => User.IsInRole("Admin");

    /// <summary>
    /// Redacts a scan job for a non-admin caller.
    /// <para>
    /// ErrorSummary holds raw filesystem paths, file names and parser messages — a map of the
    /// server's disk and of files the caller may not even be allowed to see. Counts and status
    /// are harmless and stay, so the UI can still show that a scan ran; the detail is admin-only
    /// (backlog N21).
    /// </para>
    /// </summary>
    private ScanJobDto Present(ScanJobDto job)
    {
        if (IsAdmin) return job;

        return job with
        {
            ErrorSummary = job.ErrorsCount > 0
                ? $"{job.ErrorsCount} file(s) could not be processed. Ask an administrator for details."
                : null
        };
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScanJobDto>>> GetAll(CancellationToken ct)
    {
        var jobs = await _db.ScanJobs
            .AsNoTracking()
            .Include(j => j.LibraryRoot)
            .OrderByDescending(j => j.StartedUtc)
            .ThenByDescending(j => j.Id)
            .Take(50)
            .Select(j => LibraryIndexService.MapScanJob(j, j.LibraryRoot.Name))
            .ToListAsync(ct);

        return Ok(jobs.Select(Present).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScanJobDto>> GetById(Guid id, CancellationToken ct)
    {
        var job = await _db.ScanJobs
            .AsNoTracking()
            .Include(j => j.LibraryRoot)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

        if (job == null) return NotFound();
        return Ok(Present(LibraryIndexService.MapScanJob(job, job.LibraryRoot.Name)));
    }

    [HttpGet("current")]
    public async Task<ActionResult<ScanJobDto?>> GetCurrent(CancellationToken ct)
    {
        var job = await _db.ScanJobs
            .AsNoTracking()
            .Include(j => j.LibraryRoot)
            .Where(j => j.Status == Domain.Enums.ScanStatus.Running || j.Status == Domain.Enums.ScanStatus.Queued)
            .OrderByDescending(j => j.StartedUtc)
            .ThenByDescending(j => j.Id)
            .FirstOrDefaultAsync(ct);

        if (job == null) return Ok(new { message = "No scan currently running." });
        return Ok(Present(LibraryIndexService.MapScanJob(job, job.LibraryRoot.Name)));
    }
}
