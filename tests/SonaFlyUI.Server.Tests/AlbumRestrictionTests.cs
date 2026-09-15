using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Restricted content must not surface as metadata either. The stream endpoint already
/// refuses these tracks; these tests pin the browse endpoints to the same answer, so a
/// user never sees a title they cannot play or a count that includes one.
///
/// Runs against real SQLite so the restriction subqueries are proven to translate.
/// </summary>
public sealed class AlbumRestrictionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SonaFlyDbContext _db;

    private readonly ApplicationUser _user = new()
    {
        Id = Guid.NewGuid(), UserName = "listener", Email = "listener@sonafly.local", IsEnabled = true
    };
    private readonly LibraryRoot _root = new() { Name = "Test", Path = "test" };
    private readonly Artist _allowedArtist = new() { Name = "Allowed Artist" };
    private readonly Artist _blockedArtist = new() { Name = "Blocked Artist" };
    private readonly Genre _blockedGenre = new() { Name = "Blocked Genre" };
    private readonly Album _mixedAlbum;
    private readonly Album _fullyBlockedAlbum;

    public AlbumRestrictionTests()
    {
        _connection.Open();
        _db = new SonaFlyDbContext(new DbContextOptionsBuilder<SonaFlyDbContext>()
            .UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _mixedAlbum = new Album { Title = "Mixed", AlbumArtist = _allowedArtist };
        _fullyBlockedAlbum = new Album { Title = "All Blocked", AlbumArtist = _allowedArtist };

        _db.AddRange(_user, _root, _allowedArtist, _blockedArtist, _blockedGenre, _mixedAlbum, _fullyBlockedAlbum);

        // Mixed album: one playable track, one blocked by track artist, one by genre.
        AddTrack("Playable", _mixedAlbum, _allowedArtist, genre: null, number: 1);
        AddTrack("By Blocked Artist", _mixedAlbum, _blockedArtist, genre: null, number: 2);
        AddTrack("In Blocked Genre", _mixedAlbum, _allowedArtist, genre: _blockedGenre, number: 3);

        // Every track blocked, but the album itself is not on the deny list.
        AddTrack("Blocked A", _fullyBlockedAlbum, _allowedArtist, genre: _blockedGenre, number: 1);
        AddTrack("Blocked B", _fullyBlockedAlbum, _blockedArtist, genre: null, number: 2);

        _db.AddRange(
            new UserRestriction { UserId = _user.Id, RestrictionType = RestrictionType.Artist, TargetId = _blockedArtist.Id },
            new UserRestriction { UserId = _user.Id, RestrictionType = RestrictionType.Genre, TargetId = _blockedGenre.Id });

        _db.SaveChanges();
    }

    private void AddTrack(string title, Album album, Artist artist, Genre? genre, int number)
    {
        var track = new Track
        {
            Title = title,
            Album = album,
            PrimaryArtist = artist,
            LibraryRoot = _root,
            FilePath = $"/music/{title}.mp3",
            FileName = $"{title}.mp3",
            MimeType = "audio/mpeg",
            TrackNumber = number,
            DiscNumber = 1,
            IsIndexed = true,
            IsMissing = false,
        };
        if (genre != null)
        {
            track.TrackGenres.Add(new TrackGenre { Track = track, Genre = genre });
        }
        _db.Add(track);
    }

    /// <summary>A controller acting as the restricted user.</summary>
    private T ControllerFor<T>(Func<SonaFlyDbContext, T> create) where T : ControllerBase
    {
        var controller = create(_db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString())], "Test"))
            }
        };
        return controller;
    }

    private AlbumsController Albums() => ControllerFor(db => new AlbumsController(db));
    private ArtistsController Artists() => ControllerFor(db => new ArtistsController(db));

    private static TValue Value<TValue>(ActionResult<TValue> result) =>
        Assert.IsType<TValue>(Assert.IsType<OkObjectResult>(result.Result).Value);

    // ── Album detail ──

    [Fact]
    public async Task AlbumDetail_OmitsTracksRestrictedByArtistOrGenre()
    {
        var album = Value(await Albums().GetById(_mixedAlbum.Id, default));

        var titles = album.Tracks.Select(t => t.Title).ToArray();
        Assert.Equal(["Playable"], titles);
    }

    [Fact]
    public async Task AlbumDetail_ForAnAlbumWithNothingPlayable_IsNotFound()
    {
        // An empty track list would still confirm the album exists and name it.
        var result = await Albums().GetById(_fullyBlockedAlbum.Id, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task AlbumDetail_ForAnUnrestrictedUser_ShowsEveryTrack()
    {
        var unrestricted = ControllerFor(db => new AlbumsController(db));
        unrestricted.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"));

        var album = Value(await unrestricted.GetById(_mixedAlbum.Id, default));

        Assert.Equal(3, album.Tracks.Count);
    }

    [Fact]
    public async Task AlbumDetail_StillHidesAnAlbumOnTheDenyList()
    {
        _db.Add(new UserRestriction
        {
            UserId = _user.Id,
            RestrictionType = RestrictionType.Album,
            TargetId = _mixedAlbum.Id
        });
        await _db.SaveChangesAsync();

        var result = await Albums().GetById(_mixedAlbum.Id, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Album list ──

    [Fact]
    public async Task AlbumList_CountsOnlyPlayableTracks()
    {
        var page = Value(await Albums().GetAll(ct: default));

        var mixed = Assert.Single(page.Items, a => a.Id == _mixedAlbum.Id);
        Assert.Equal(1, mixed.TrackCount);
    }

    [Fact]
    public async Task AlbumList_OmitsAlbumsWithNothingPlayable()
    {
        var page = Value(await Albums().GetAll(ct: default));

        Assert.DoesNotContain(page.Items, a => a.Id == _fullyBlockedAlbum.Id);
        // The total has to agree with the items, or paging reports phantom rows.
        Assert.Equal(page.Items.Count, page.TotalCount);
    }

    [Fact]
    public async Task AlbumList_ForAnUnrestrictedUser_ShowsBothAlbumsWithFullCounts()
    {
        var unrestricted = ControllerFor(db => new AlbumsController(db));
        unrestricted.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"));

        var page = Value(await unrestricted.GetAll(ct: default));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(3, Assert.Single(page.Items, a => a.Id == _mixedAlbum.Id).TrackCount);
    }

    // ── Artist counts ──

    [Fact]
    public async Task ArtistDetail_CountsOnlyPlayableAlbumsAndTracks()
    {
        var artist = Value(await Artists().GetById(_allowedArtist.Id, default));

        // Only the mixed album still has something playable.
        Assert.Equal(1, artist.AlbumCount);
        // Of this artist's four primary tracks, one survives: the rest are genre-blocked.
        Assert.Equal(1, artist.TrackCount);
    }

    [Fact]
    public async Task ArtistList_HidesAnArtistOnTheDenyList()
    {
        var page = Value(await Artists().GetAll(ct: default));

        Assert.DoesNotContain(page.Items, a => a.Id == _blockedArtist.Id);
        Assert.Contains(page.Items, a => a.Id == _allowedArtist.Id);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
