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
/// Track search has to find a track the way a person looks for one. Typing an artist is the
/// most common way, and matching only the title meant that returned nothing at all unless
/// the artist's name happened to appear in the song title.
///
/// The fixture is a small discography library, because that is where the second problem
/// shows up: the same song on three albums is three legitimately different rows, and they
/// have to be ordered so they read as choices rather than as a list repeating itself.
/// </summary>
public sealed class SearchTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SonaFlyDbContext _db;

    private readonly ApplicationUser _user = new()
    {
        Id = Guid.NewGuid(), UserName = "listener", Email = "listener@sonafly.local", IsEnabled = true
    };
    private readonly LibraryRoot _root = new() { Name = "Test", Path = "test" };

    private readonly Artist _maiden = new() { Name = "Iron Maiden" };
    private readonly Artist _diamond = new() { Name = "King Diamond" };
    private readonly Artist _roth = new() { Name = "David Lee Roth" };
    private readonly Artist _vai = new() { Name = "Steve Vai" };
    private readonly Artist _malmsteen = new() { Name = "Yngwie Malmsteen" };

    public SearchTests()
    {
        _connection.Open();
        _db = new SonaFlyDbContext(new DbContextOptionsBuilder<SonaFlyDbContext>()
            .UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.AddRange(_user, _root, _maiden, _diamond, _roth, _vai, _malmsteen);

        // "Evil" appears on three King Diamond releases, as it would in a real discography.
        var abigail = new Album { Title = "Abigail", AlbumArtist = _diamond, Year = 1987 };
        var dangerous = new Album { Title = "A Dangerous Meeting", AlbumArtist = _diamond, Year = 1992 };
        var live = new Album { Title = "In Concert", AlbumArtist = _diamond, Year = 1991 };
        var numberOfTheBeast = new Album { Title = "The Number of the Beast", AlbumArtist = _maiden, Year = 1982 };
        _db.AddRange(abigail, dangerous, live, numberOfTheBeast);

        AddTrack("Evil", _diamond, abigail, track: 3);
        AddTrack("Evil", _diamond, dangerous, track: 3);
        AddTrack("Evil", _diamond, live, track: 5);
        AddTrack("Arrival", _diamond, abigail, track: 2);
        AddTrack("Hallowed Be Thy Name", _maiden, numberOfTheBeast, track: 8);
        AddTrack("Run to the Hills", _maiden, numberOfTheBeast, track: 5);
        AddTrack("The Evil That Men Do", _maiden, numberOfTheBeast, track: 9);

        // The two cases reported from the real library. Both artists were invisible to
        // search while unrelated songs matched a fragment of their name mid-word:
        // "roth" found five variations of "Brothers"; "vai" found "Vain".
        var eatEm = new Album { Title = "Eat 'Em And Smile", AlbumArtist = _roth, Year = 1986 };
        var fireGarden = new Album { Title = "Fire Garden", AlbumArtist = _vai, Year = 1996 };
        var seventhSign = new Album { Title = "The Seventh Sign", AlbumArtist = _malmsteen, Year = 1994 };
        _db.AddRange(eatEm, fireGarden, seventhSign);

        AddTrack("Yankee Rose", _roth, eatEm, track: 1);
        AddTrack("Goin' Crazy!", _roth, eatEm, track: 2);
        AddTrack("For the Love of God", _vai, fireGarden, track: 4);
        AddTrack("Brothers", _malmsteen, seventhSign, track: 6);
        AddTrack("Blood Brothers", _maiden, numberOfTheBeast, track: 11);
        AddTrack("Vain Glory Opera", _malmsteen, seventhSign, track: 7);

        _db.SaveChanges();
    }

    private void AddTrack(string title, Artist artist, Album album, int track)
    {
        _db.Add(new Track
        {
            Title = title,
            PrimaryArtist = artist,
            Album = album,
            LibraryRoot = _root,
            FilePath = $"/music/{album.Title}/{title}.mp3",
            FileName = $"{title}.mp3",
            MimeType = "audio/mpeg",
            DurationSeconds = 300,
            TrackNumber = track,
            DiscNumber = 1,
            IsIndexed = true,
            IsMissing = false,
        });
    }

    private SearchController Controller()
    {
        var controller = new SearchController(_db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString())], "Test"))
                }
            }
        };
        return controller;
    }

    private async Task<SearchResultDto> SearchAsync(string q, int limit = 10) =>
        Assert.IsType<SearchResultDto>(
            Assert.IsType<OkObjectResult>((await Controller().Search(q, limit)).Result).Value);

    // ── Finding a track by who made it ──

    [Fact]
    public async Task SearchingAnArtistName_FindsThatArtistsTracks()
    {
        var result = await SearchAsync("iron maiden");

        // Previously empty: the filter only looked at the title and the genre, so the one
        // thing people type most often matched nothing.
        Assert.NotEmpty(result.Tracks);
        Assert.All(result.Tracks, t => Assert.Equal("Iron Maiden", t.ArtistName));
    }

    [Fact]
    public async Task SearchingAnAlbumTitle_FindsItsTracks()
    {
        var result = await SearchAsync("abigail");

        Assert.Equal(["Arrival", "Evil"], result.Tracks.Select(t => t.Title).OrderBy(t => t).ToArray());
        Assert.All(result.Tracks, t => Assert.Equal("Abigail", t.AlbumTitle));
    }

    [Fact]
    public async Task SearchingATitle_StillWorks()
    {
        var result = await SearchAsync("run to the hills");

        Assert.Equal("Run to the Hills", Assert.Single(result.Tracks).Title);
    }

    [Fact]
    public async Task ThePartialArtistName_IsEnough()
    {
        var result = await SearchAsync("maiden");

        // Every Iron Maiden track in the fixture, found by a fragment of the artist name.
        Assert.Equal(4, result.Tracks.Count);
        Assert.All(result.Tracks, t => Assert.Equal("Iron Maiden", t.ArtistName));
    }

    // ── Telling duplicates apart ──

    [Fact]
    public async Task ASongOnSeveralAlbums_ComesBackWithTheAlbumThatIdentifiesIt()
    {
        var result = await SearchAsync("evil");

        var evils = result.Tracks.Where(t => t.Title == "Evil").ToList();
        Assert.Equal(3, evils.Count);

        // Same title, same artist — the album is the only thing that distinguishes them,
        // so it must be present and populated for the client to show.
        Assert.All(evils, t => Assert.False(string.IsNullOrWhiteSpace(t.AlbumTitle)));
        Assert.Equal(3, evils.Select(t => t.AlbumTitle).Distinct().Count());
        Assert.All(evils, t => Assert.NotNull(t.DurationSeconds));
    }

    [Fact]
    public async Task AnExactTitleMatch_IsRankedAboveALooserOne()
    {
        var result = await SearchAsync("evil");

        // "The Evil That Men Do" also matches, but someone typing "evil" means the song.
        Assert.Equal("Evil", result.Tracks.First().Title);
        Assert.Contains(result.Tracks, t => t.Title == "The Evil That Men Do");
    }

    [Fact]
    public async Task RepeatsOfOneSong_AreGroupedByArtistAndAlbumRatherThanArbitrary()
    {
        var result = await SearchAsync("evil");

        var evils = result.Tracks.Where(t => t.Title == "Evil").Select(t => t.AlbumTitle).ToList();

        // Deterministic and alphabetical by album, so the same search twice reads the same
        // way and the three rows are visibly three different records.
        Assert.Equal(["A Dangerous Meeting", "Abigail", "In Concert"], evils);
    }

    [Fact]
    public async Task ArtistResults_AreStillReturnedAlongsideTracks()
    {
        // The auditorium only consumes tracks, but the main search page shows all three.
        var result = await SearchAsync("iron maiden");

        Assert.Equal("Iron Maiden", Assert.Single(result.Artists).Name);
    }

    // ── Restrictions still apply ──

    [Fact]
    public async Task ARestrictedArtistsTracks_AreNotFoundByArtistName()
    {
        _db.Add(new UserRestriction
        {
            UserId = _user.Id, RestrictionType = RestrictionType.Artist, TargetId = _maiden.Id
        });
        await _db.SaveChangesAsync();

        var result = await SearchAsync("iron maiden");

        // Widening what the search matches must not widen what it exposes.
        Assert.Empty(result.Tracks);
        Assert.Empty(result.Artists);
    }

    [Fact]
    public async Task ARestrictedAlbumsTracks_AreNotFoundByAlbumTitle()
    {
        var abigail = _db.Albums.Single(a => a.Title == "Abigail");
        _db.Add(new UserRestriction
        {
            UserId = _user.Id, RestrictionType = RestrictionType.Album, TargetId = abigail.Id
        });
        await _db.SaveChangesAsync();

        var result = await SearchAsync("abigail");

        Assert.Empty(result.Tracks);
    }

    [Fact]
    public async Task UnindexedOrMissingTracks_AreNotReturned()
    {
        var missing = _db.Tracks.Single(t => t.Title == "Run to the Hills");
        missing.IsMissing = true;
        await _db.SaveChangesAsync();

        var result = await SearchAsync("iron maiden");

        Assert.DoesNotContain(result.Tracks, t => t.Title == "Run to the Hills");
    }

    [Fact]
    public async Task AnEmptyQuery_ReturnsNothingRatherThanEverything()
    {
        var result = await SearchAsync("   ");

        Assert.Empty(result.Tracks);
        Assert.Empty(result.Albums);
        Assert.Empty(result.Artists);
    }

    // ── Relevance: the answer before the coincidence ──

    [Fact]
    public async Task SearchingAnArtistSurname_PutsThatArtistAboveMidWordCoincidences()
    {
        // Reported from the real library: "roth" returned five variations of "Brothers"
        // and not one David Lee Roth track.
        var result = await SearchAsync("roth", limit: 20);

        Assert.Contains(result.Tracks, t => t.ArtistName == "David Lee Roth");

        var firstUnrelated = result.Tracks.ToList().FindIndex(t => t.Title.Contains("Brothers"));
        var lastRoth = result.Tracks.ToList().FindLastIndex(t => t.ArtistName == "David Lee Roth");
        Assert.True(lastRoth < firstUnrelated,
            "every David Lee Roth track should rank above an incidental 'Brothers' match");
    }

    [Fact]
    public async Task AShortArtistName_IsNotDrownedByWordsThatMerelyContainIt()
    {
        // Reported from the real library: "vai" matched "Vain" but never Steve Vai.
        var result = await SearchAsync("vai", limit: 20);

        Assert.Contains(result.Tracks, t => t.ArtistName == "Steve Vai");

        // "Vain Glory Opera" starts with the letters, but Vai is the whole word, and the
        // whole word is what someone typing three letters meant.
        Assert.Equal("Steve Vai", result.Tracks.First().ArtistName);
        Assert.Contains(result.Tracks, t => t.Title == "Vain Glory Opera");
    }

    [Fact]
    public async Task MidWordMatches_AreDemotedButNotDiscarded()
    {
        // Still findable — ranked down, not filtered out, so nothing silently disappears.
        var result = await SearchAsync("roth", limit: 20);

        Assert.Contains(result.Tracks, t => t.Title == "Brothers");
        Assert.Contains(result.Tracks, t => t.Title == "Blood Brothers");
    }

    [Fact]
    public async Task AnAlbumNameMatch_RanksAboveAMidWordTitleMatch()
    {
        var result = await SearchAsync("garden", limit: 20);

        // "Fire Garden" the album, ahead of anything that merely contains the letters.
        Assert.Equal("Fire Garden", result.Tracks.First().AlbumTitle);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
