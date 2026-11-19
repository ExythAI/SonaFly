using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Application.Common;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Search that ignores case and accents, sorting albums by when they were added, and the
/// per-user listening record: plays and favourites.
/// </summary>
public sealed class ListeningAndSearchTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly SonaFlyDbContext _db;
    private readonly LibraryRoot _root = new() { Name = "Music", Path = "/music" };

    private readonly ApplicationUser _user = new()
    {
        Id = Guid.NewGuid(), UserName = "listener", Email = "listener@sonafly.local", IsEnabled = true
    };
    private readonly ApplicationUser _other = new()
    {
        Id = Guid.NewGuid(), UserName = "other", Email = "other@sonafly.local", IsEnabled = true
    };

    public ListeningAndSearchTests()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        _db = _services.GetRequiredService<SonaFlyDbContext>();
        _db.Database.EnsureCreated();
        _db.Users.AddRange(_user, _other);
        _db.LibraryRoots.Add(_root);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private ControllerContext ContextFor(ApplicationUser user) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"))
        }
    };

    private Track AddTrack(string title, string? artist = null, string? album = null, string? genre = null)
    {
        var track = new Track
        {
            LibraryRootId = _root.Id, FilePath = $"/music/{Guid.NewGuid():N}.mp3", FileName = "x.mp3",
            FileExtension = ".mp3", Title = title, MimeType = "audio/mpeg", IsIndexed = true, Genre = genre,
            PrimaryArtist = artist == null ? null : new Artist { Name = artist },
            Album = album == null ? null : new Album { Title = album },
        };
        _db.Tracks.Add(track);
        _db.SaveChanges();
        return track;
    }

    private static T Value<T>(ActionResult<T> result) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    // ── Search ──

    [Theory]
    [InlineData("Björk", "bjork")]
    [InlineData("BEYONCÉ", "beyonce")]
    [InlineData("  Sigur Rós ", "sigur ros")]
    [InlineData("Motörhead", "motorhead")]
    [InlineData("AC/DC", "ac/dc")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void NamesAreFoldedToLowerCaseWithoutAccents(string? name, string expected)
    {
        Assert.Equal(expected, SearchText.Normalize(name));
    }

    [Fact]
    public void TheSearchFormFollowsTheNameWheneverItIsSaved()
    {
        var track = AddTrack("Jóga", artist: "Björk", album: "Homogénic", genre: "Électronique");
        Assert.Equal("joga", track.SearchTitle);
        Assert.Equal("electronique", track.SearchGenre);
        Assert.Equal("bjork", track.PrimaryArtist!.SearchName);
        Assert.Equal("homogenic", track.Album!.SearchTitle);

        track.Title = "Hyperballad";
        _db.SaveChanges();
        Assert.Equal("hyperballad", track.SearchTitle);
    }

    [Fact]
    public async Task RowsSavedBeforeTheSearchColumnsExistedAreFilledIn()
    {
        var track = AddTrack("Jóga", artist: "Björk");
        await _db.Tracks.ExecuteUpdateAsync(s => s.SetProperty(t => t.SearchTitle, ""));
        await _db.Artists.ExecuteUpdateAsync(s => s.SetProperty(a => a.SearchName, ""));
        _db.ChangeTracker.Clear();

        Assert.Equal(2, await _db.BackfillSearchColumnsAsync());
        Assert.Equal(0, await _db.BackfillSearchColumnsAsync());
        Assert.Equal("joga", (await _db.Tracks.AsNoTracking().SingleAsync(t => t.Id == track.Id)).SearchTitle);
    }

    [Theory]
    [InlineData("bjork")]
    [InlineData("BJÖRK")]
    [InlineData("Björk")]
    [InlineData("homogenic")]
    public async Task SearchFindsAccentedNamesHoweverTheyAreTyped(string query)
    {
        AddTrack("Jóga", artist: "Björk", album: "Homogénic");
        AddTrack("Unrelated", artist: "Someone Else");
        var controller = new SearchController(_db) { ControllerContext = ContextFor(_user) };

        var result = Value(await controller.Search(query));

        Assert.Equal("Jóga", Assert.Single(result.Tracks).Title);
    }

    [Fact]
    public async Task ASearchThatFoldsToNothingReturnsNothingRatherThanEverything()
    {
        AddTrack("Anything");
        var controller = new SearchController(_db) { ControllerContext = ContextFor(_user) };

        // A lone combining accent: not blank, but nothing is left once it is folded.
        var result = Value(await controller.Search("́"));

        Assert.Empty(result.Tracks);
    }

    // ── Album order ──

    [Fact]
    public async Task AlbumsCanBeListedNewestAdditionFirst()
    {
        AddTrack("a", album: "Added first").Album!.CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        AddTrack("b", album: "Added last").Album!.CreatedUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        AddTrack("c", album: "Added between").Album!.CreatedUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.SaveChanges();
        var controller = new AlbumsController(_db) { ControllerContext = ContextFor(_user) };

        var recent = Value(await controller.GetAll(sort: "recent"));
        var byTitle = Value(await controller.GetAll(sort: "title"));

        Assert.Equal(["Added last", "Added between", "Added first"], recent.Items.Select(a => a.Title));
        Assert.Equal(["Added between", "Added first", "Added last"], byTitle.Items.Select(a => a.Title));
    }

    // ── Listening ──

    private ListeningController Listening(ApplicationUser user) => new(_db) { ControllerContext = ContextFor(user) };

    private void AddPlay(ApplicationUser user, Track track, DateTime when)
    {
        _db.PlayEvents.Add(new PlayEvent { UserId = user.Id, TrackId = track.Id, PlayedUtc = when });
        _db.SaveChanges();
    }

    [Fact]
    public async Task APlayIsRecordedOnceEvenWhenReportedTwiceInQuickSuccession()
    {
        var track = AddTrack("Song");
        var controller = Listening(_user);

        Assert.IsType<NoContentResult>(await controller.ReportPlay(new ReportPlayRequest(track.Id), CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.ReportPlay(new ReportPlayRequest(track.Id), CancellationToken.None));

        Assert.Equal(1, await _db.PlayEvents.CountAsync());
        Assert.IsType<NotFoundResult>(await controller.ReportPlay(new ReportPlayRequest(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task RecentlyPlayedListsEachTrackOnceNewestFirstAndOnlyTheCallersOwn()
    {
        var older = AddTrack("Older");
        var newer = AddTrack("Newer");
        var theirs = AddTrack("Someone else's");
        var now = DateTime.UtcNow;
        AddPlay(_user, older, now.AddHours(-5));
        AddPlay(_user, newer, now.AddHours(-3));
        AddPlay(_user, older, now.AddHours(-4));
        AddPlay(_other, theirs, now.AddHours(-1));

        var recent = Value(await Listening(_user).RecentlyPlayed());

        Assert.Equal(["Newer", "Older"], recent.Select(p => p.Track.Title));
        Assert.Equal(2, recent[1].PlayCount);
    }

    [Fact]
    public async Task MostPlayedCountsListensAndCanBeLimitedToRecentDays()
    {
        var favourite = AddTrack("Played a lot long ago");
        var current = AddTrack("Played lately");
        var now = DateTime.UtcNow;
        foreach (var day in new[] { 90, 91, 92 }) AddPlay(_user, favourite, now.AddDays(-day));
        AddPlay(_user, current, now.AddDays(-1));
        AddPlay(_user, current, now.AddDays(-2));

        var allTime = Value(await Listening(_user).MostPlayed());
        var thisMonth = Value(await Listening(_user).MostPlayed(days: 30));

        Assert.Equal("Played a lot long ago", allTime[0].Track.Title);
        Assert.Equal(3, allTime[0].PlayCount);
        Assert.Equal("Played lately", Assert.Single(thisMonth).Track.Title);
    }

    [Fact]
    public async Task FavouritesCanBeAddedOnceListedAndRemoved()
    {
        var track = AddTrack("Loved");
        var controller = Listening(_user);

        Assert.IsType<NoContentResult>(await controller.AddFavorite(track.Id, CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.AddFavorite(track.Id, CancellationToken.None));
        Assert.Equal(1, await _db.FavoriteTracks.CountAsync());
        Assert.Equal("Loved", Assert.Single(Value(await controller.Favorites())).Title);
        Assert.Empty(Value(await Listening(_other).Favorites()));

        Assert.IsType<NoContentResult>(await controller.RemoveFavorite(track.Id, CancellationToken.None));
        Assert.Empty(Value(await controller.Favorites()));
    }

    [Fact]
    public async Task ARestrictedTrackCannotBeReportedFavouritedOrSeenInHistory()
    {
        var track = AddTrack("Explicit", artist: "Restricted Artist");
        AddPlay(_user, track, DateTime.UtcNow.AddHours(-1));
        _db.FavoriteTracks.Add(new FavoriteTrack { UserId = _user.Id, TrackId = track.Id });
        _db.UserRestrictions.Add(new UserRestriction
        {
            UserId = _user.Id, RestrictionType = RestrictionType.Artist, TargetId = track.PrimaryArtistId!.Value
        });
        _db.SaveChanges();
        var controller = Listening(_user);

        Assert.Empty(Value(await controller.RecentlyPlayed()));
        Assert.Empty(Value(await controller.Favorites()));
        Assert.IsType<NotFoundResult>(await controller.ReportPlay(new ReportPlayRequest(track.Id), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.AddFavorite(track.Id, CancellationToken.None));
    }

    // ── Browsing for the web UI ──

    [Fact]
    public async Task AlbumsCanBeFilteredByTitleOrArtistIgnoringAccents()
    {
        AddTrack("a", artist: "Björk", album: "Homogénic").Album!.AlbumArtist = _db.Artists.Local.Single(a => a.Name == "Björk");
        AddTrack("b", album: "Unrelated");
        _db.SaveChanges();
        var controller = new AlbumsController(_db) { ControllerContext = ContextFor(_user) };

        Assert.Equal("Homogénic", Assert.Single(Value(await controller.GetAll(filter: "homogenic")).Items).Title);
        Assert.Equal("Homogénic", Assert.Single(Value(await controller.GetAll(filter: "BJORK")).Items).Title);
        Assert.Equal(2, Value(await controller.GetAll(filter: "  ")).TotalCount);
    }

    [Fact]
    public async Task ArtistsCanBeFilteredAndOrderedByHowMuchMusicTheyHave()
    {
        AddTrack("one", artist: "Aardvark");
        var prolific = AddTrack("two", artist: "Zebra").PrimaryArtist!;
        _db.Tracks.Add(new Track
        {
            LibraryRootId = _root.Id, FilePath = "/music/z2.mp3", FileName = "z2.mp3", FileExtension = ".mp3",
            Title = "three", MimeType = "audio/mpeg", IsIndexed = true, PrimaryArtist = prolific,
        });
        _db.SaveChanges();
        var controller = new ArtistsController(_db) { ControllerContext = ContextFor(_user) };

        Assert.Equal(["Aardvark", "Zebra"], Value(await controller.GetAll()).Items.Select(a => a.Name));
        Assert.Equal(["Zebra", "Aardvark"], Value(await controller.GetAll(sort: "tracks")).Items.Select(a => a.Name));
        Assert.Equal("Zebra", Assert.Single(Value(await controller.GetAll(filter: "zeb")).Items).Name);
    }

    [Fact]
    public async Task AnArtistWithoutAPhotographIsPicturedByAnAlbumCoverTheyAreAllowedToSee()
    {
        var early = new ArtworkAsset { StoragePath = "aa/early.jpg", MimeType = "image/jpeg", Hash = "early" };
        var late = new ArtworkAsset { StoragePath = "bb/late.jpg", MimeType = "image/jpeg", Hash = "late" };
        _db.ArtworkAssets.AddRange(early, late);
        var first = AddTrack("a", artist: "Band", album: "Debut");
        var artist = first.PrimaryArtist!;
        first.Album!.AlbumArtist = artist; first.Album.Year = 1980; first.Album.ArtworkId = early.Id;
        var second = AddTrack("b", album: "Follow-up");
        second.PrimaryArtist = artist; second.Album!.AlbumArtist = artist; second.Album.Year = 1990; second.Album.ArtworkId = late.Id;
        _db.SaveChanges();
        var controller = new ArtistsController(_db) { ControllerContext = ContextFor(_user) };

        Assert.Equal(early.Id, Value(await controller.GetById(artist.Id, CancellationToken.None)).ArtworkId);

        // With the earliest album restricted, its cover must not be the one shown.
        _db.UserRestrictions.Add(new UserRestriction { UserId = _user.Id, RestrictionType = RestrictionType.Album, TargetId = first.Album.Id });
        _db.SaveChanges();
        Assert.Equal(late.Id, Value(await controller.GetById(artist.Id, CancellationToken.None)).ArtworkId);

        var search = new SearchController(_db) { ControllerContext = ContextFor(_user) };
        Assert.Equal(late.Id, Assert.Single(Value(await search.Search("band")).Artists).ArtworkId);
    }

    [Fact]
    public async Task TrackResultsCarryTheArtistSoItsNameCanLinkToTheArtist()
    {
        var track = AddTrack("Song", artist: "Someone");
        var tracks = new TracksController(_db) { ControllerContext = ContextFor(_user) };
        var search = new SearchController(_db) { ControllerContext = ContextFor(_user) };

        Assert.Equal(track.PrimaryArtistId, Assert.Single(Value(await tracks.GetAll()).Items).ArtistId);
        Assert.Equal(track.PrimaryArtistId, Assert.Single(Value(await search.Search("song")).Tracks).ArtistId);
    }

    [Fact]
    public async Task FavouriteIdsListEveryFavouriteOfTheCallerOnly()
    {
        var mine = AddTrack("Mine");
        var theirs = AddTrack("Theirs");
        _db.FavoriteTracks.AddRange(
            new FavoriteTrack { UserId = _user.Id, TrackId = mine.Id },
            new FavoriteTrack { UserId = _other.Id, TrackId = theirs.Id });
        _db.SaveChanges();

        Assert.Equal([mine.Id], Value(await Listening(_user).FavoriteIds(CancellationToken.None)));
    }
}
