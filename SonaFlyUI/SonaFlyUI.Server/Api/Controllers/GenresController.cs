using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/genres")]
[Authorize]
public class GenresController : ControllerBase
{
    private readonly SonaFlyDbContext _db;

    public GenresController(SonaFlyDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> GetAll(CancellationToken ct)
    {
        var genres = await _db.Genres.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GenreDto(g.Id, g.Name, g.TrackGenres.Count))
            .ToListAsync(ct);

        return Ok(genres);
    }
}
