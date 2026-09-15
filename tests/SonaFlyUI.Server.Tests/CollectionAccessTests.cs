using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identity;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Playlists and mixed tapes belong to somebody. Knowing an ID must not be enough to read a
/// private one or to change anybody's — public means readable, never writable — and the
/// entries they hand back must obey the caller's own restrictions.
///
/// Two users share this fixture throughout, because every interesting case is "the other
/// user tries it". Runs on real SQLite so the ownership and restriction predicates are
/// proven to translate rather than silently falling back to client evaluation.
/// </summary>
public sealed class CollectionAccessTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SonaFlyDbContext _db;

    private readonly ApplicationUser _owner = new()
    {
        Id = Guid.NewGuid(), UserName = "owner", Email = "owner@sonafly.local", DisplayName = "Owner", IsEnabled = true
    };
    private readonly ApplicationUser _stranger = new()
    {
        Id = Guid.NewGuid(), UserName = "stranger", Email = "stranger@sonafly.local", DisplayName = "Stranger", IsEnabled = true
    };
    private readonly ApplicationUser _admin = new()
    {
        Id = Guid.NewGuid(), UserName = "admin", Email = "admin@sonafly.local", DisplayName = "Admin", IsEnabled = true
    };

    private readonly LibraryRoot _root = new() { Name = "Test", Path = "test" };
    private readonly Artist _allowedArtist = new() { Name = "Allowed Artist" };
    private readonly Artist _blockedArtist = new() { Name = "Blocked Artist" };

    private readonly Track _plainTrack;
    private readonly Track _strangerBlockedTrack;

    private readonly Playlist _privatePlaylist;
    private readonly Playlist _publicPlaylist;
    private readonly MixedTape _tape;

    public CollectionAccessTests()
    {
        _connection.Open();
        _db = new SonaFlyDbContext(new DbContextOptionsBuilder<SonaFlyDbContext>()
            .UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _db.AddRange(_owner, _stranger, _admin, _root, _allowedArtist, _blockedArtist);

        _plainTrack = AddTrack("Everyone Can Hear This", _allowedArtist);
        _strangerBlockedTrack = AddTrack("Not For The Stranger", _blockedArtist);

        _privatePlaylist = new Playlist { Name = "Private", OwnerUserId = _owner.Id, IsPublic = false };
        _publicPlaylist = new Playlist { Name = "Public", OwnerUserId = _owner.Id, IsPublic = true };
        _tape = new MixedTape { Name = "Owner Tape", OwnerUserId = _owner.Id, TargetDurationSeconds = 3600 };
        _db.AddRange(_privatePlaylist, _publicPlaylist, _tape);

        // Both collections hold one track the stranger may hear and one they may not.
        _db.AddRange(
            new PlaylistItem { Playlist = _publicPlaylist, Track = _plainTrack, SortOrder = 1 },
            new PlaylistItem { Playlist = _publicPlaylist, Track = _strangerBlockedTrack, SortOrder = 2 },
            new PlaylistItem { Playlist = _privatePlaylist, Track = _plainTrack, SortOrder = 1 },
            new MixedTapeItem { MixedTape = _tape, Track = _plainTrack, SortOrder = 1 },
            new MixedTapeItem { MixedTape = _tape, Track = _strangerBlockedTrack, SortOrder = 2 });

        _db.Add(new UserRestriction
        {
            UserId = _stranger.Id, RestrictionType = RestrictionType.Artist, TargetId = _blockedArtist.Id
        });

        _db.SaveChanges();
    }

    private Track AddTrack(string title, Artist artist)
    {
        var track = new Track
        {
            Title = title,
            PrimaryArtist = artist,
            LibraryRoot = _root,
            FilePath = $"/music/{title}.mp3",
            FileName = $"{title}.mp3",
            MimeType = "audio/mpeg",
            DurationSeconds = 120,
            TrackNumber = 1,
            DiscNumber = 1,
            IsIndexed = true,
            IsMissing = false,
        };
        _db.Add(track);
        return track;
    }

    private T ControllerFor<T>(ApplicationUser user, bool isAdmin, Func<SonaFlyDbContext, T> create)
        where T : ControllerBase
    {
        Claim[] claims = isAdmin
            ? [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
               new Claim(ClaimTypes.Role, IdentitySeeder.AdminRole)]
            : [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())];

        var controller = create(_db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
        return controller;
    }

    private PlaylistsController Playlists(ApplicationUser user, bool isAdmin = false) =>
        ControllerFor(user, isAdmin, db => new PlaylistsController(new PlaylistService(db)));

    private MixedTapesController Tapes(ApplicationUser user, bool isAdmin = false) =>
        ControllerFor(user, isAdmin, db => new MixedTapesController(new MixedTapeService(db)));

    private static TValue Value<TValue>(ActionResult<TValue> result) =>
        Assert.IsType<TValue>(Assert.IsType<OkObjectResult>(result.Result).Value);

    /// <summary>
    /// The list routes declare <c>IReadOnlyList&lt;T&gt;</c> but hand back a concrete
    /// <c>List&lt;T&gt;</c>, which Assert.IsType rejects. Assert assignability instead.
    /// </summary>
    private static IReadOnlyList<TItem> Values<TItem>(ActionResult<IReadOnlyList<TItem>> result) =>
        Assert.IsAssignableFrom<IReadOnlyList<TItem>>(Assert.IsType<OkObjectResult>(result.Result).Value);

    /// <summary>Re-reads a playlist's stored name through a context that cannot see tracked edits.</summary>
    private string NameOf(Guid playlistId) =>
        _db.Playlists.AsNoTracking().Single(p => p.Id == playlistId).Name;

    // ── Playlist reads ──

    [Fact]
    public async Task APrivatePlaylist_IsNotReadableByAnotherUser()
    {
        var result = await Playlists(_stranger).GetById(_privatePlaylist.Id, default);

        // Not "forbidden": that would confirm the ID names a real playlist.
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task APrivatePlaylist_IsReadableByItsOwner()
    {
        var playlist = Value(await Playlists(_owner).GetById(_privatePlaylist.Id, default));

        Assert.Equal("Private", playlist.Name);
    }

    [Fact]
    public async Task APublicPlaylist_IsReadableByAnotherUser()
    {
        var playlist = Value(await Playlists(_stranger).GetById(_publicPlaylist.Id, default));

        Assert.Equal("Public", playlist.Name);
    }

    [Fact]
    public async Task ListingPlaylists_OmitsOtherUsersPrivateOnes()
    {
        var names = Values(await Playlists(_stranger).GetAll(default)).Select(p => p.Name).ToArray();

        Assert.Equal(["Public"], names);
    }

    [Fact]
    public async Task AnAdmin_CanReadAPrivatePlaylist()
    {
        var playlist = Value(await Playlists(_admin, isAdmin: true).GetById(_privatePlaylist.Id, default));

        Assert.Equal("Private", playlist.Name);
    }

    // ── Playlist reads honour restrictions ──

    [Fact]
    public async Task APublicPlaylist_HidesEntriesTheReaderIsRestrictedFrom()
    {
        var playlist = Value(await Playlists(_stranger).GetById(_publicPlaylist.Id, default));

        Assert.Equal(["Everyone Can Hear This"], playlist.Items.Select(i => i.TrackTitle).ToArray());
    }

    [Fact]
    public async Task TheTrackCount_MatchesTheEntriesTheReaderIsShown()
    {
        // A count of 2 beside one visible row tells the reader exactly what is hidden.
        var detail = Value(await Playlists(_stranger).GetById(_publicPlaylist.Id, default));
        var listed = Values(await Playlists(_stranger).GetAll(default)).Single(p => p.Id == _publicPlaylist.Id);

        Assert.Equal(1, detail.TrackCount);
        Assert.Equal(1, listed.TrackCount);
    }

    [Fact]
    public async Task AnUnrestrictedReader_StillSeesEverything()
    {
        var playlist = Value(await Playlists(_owner).GetById(_publicPlaylist.Id, default));

        Assert.Equal(2, playlist.TrackCount);
        Assert.Equal(2, playlist.Items.Count);
    }

    [Fact]
    public async Task ARestrictionAddedAfterTheFact_HidesAnExistingEntry()
    {
        _db.Add(new UserRestriction
        {
            UserId = _owner.Id, RestrictionType = RestrictionType.Artist, TargetId = _blockedArtist.Id
        });
        await _db.SaveChangesAsync();

        var playlist = Value(await Playlists(_owner).GetById(_publicPlaylist.Id, default));

        Assert.Equal(["Everyone Can Hear This"], playlist.Items.Select(i => i.TrackTitle).ToArray());
    }

    // ── Playlist writes ──

    [Fact]
    public async Task APublicPlaylist_IsNotWritableByAnotherUser()
    {
        // The whole point of the separate read and write policies: they can see it, and that
        // is all.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Playlists(_stranger).Update(_publicPlaylist.Id, new UpdatePlaylistRequest("Hijacked", null, null), default));

        Assert.Equal("Public", NameOf(_publicPlaylist.Id));
    }

    [Fact]
    public async Task APrivatePlaylist_ReportsMissingRatherThanForbiddenOnWrite()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Playlists(_stranger).Update(_privatePlaylist.Id, new UpdatePlaylistRequest("Hijacked", null, null), default));

        Assert.Equal("Private", NameOf(_privatePlaylist.Id));
    }

    [Fact]
    public async Task AnotherUser_CannotDeleteAPublicPlaylist()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Playlists(_stranger).Delete(_publicPlaylist.Id, default));

        Assert.True(_db.Playlists.AsNoTracking().Any(p => p.Id == _publicPlaylist.Id));
    }

    [Fact]
    public async Task AnotherUser_CannotAddTracksToAPublicPlaylist()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Playlists(_stranger).AddTrack(_publicPlaylist.Id, new AddTrackToPlaylistRequest(_plainTrack.Id), default));

        Assert.Equal(2, _db.PlaylistItems.AsNoTracking().Count(i => i.PlaylistId == _publicPlaylist.Id));
    }

    [Fact]
    public async Task AnotherUser_CannotReorderAPublicPlaylist()
    {
        var ids = _db.PlaylistItems.AsNoTracking()
            .Where(i => i.PlaylistId == _publicPlaylist.Id)
            .OrderBy(i => i.SortOrder).Select(i => i.Id).ToList();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Playlists(_stranger).Reorder(_publicPlaylist.Id, new ReorderPlaylistItemsRequest([ids[1], ids[0]]), default));

        var after = _db.PlaylistItems.AsNoTracking()
            .Where(i => i.PlaylistId == _publicPlaylist.Id)
            .OrderBy(i => i.SortOrder).Select(i => i.Id).ToList();
        Assert.Equal(ids, after);
    }

    [Fact]
    public async Task AnotherUser_CannotRemoveAnItemFromAPublicPlaylist()
    {
        var itemId = _db.PlaylistItems.AsNoTracking().First(i => i.PlaylistId == _publicPlaylist.Id).Id;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Playlists(_stranger).RemoveItem(_publicPlaylist.Id, itemId, default));

        Assert.True(_db.PlaylistItems.AsNoTracking().Any(i => i.Id == itemId));
    }

    [Fact]
    public async Task TheOwner_CanStillDoAllOfThat()
    {
        await Playlists(_owner).Update(_publicPlaylist.Id, new UpdatePlaylistRequest("Renamed", null, null), default);

        Assert.Equal("Renamed", NameOf(_publicPlaylist.Id));
    }

    [Fact]
    public async Task AnAdmin_CanWriteToAPlaylistTheyDoNotOwn()
    {
        await Playlists(_admin, isAdmin: true)
            .Update(_privatePlaylist.Id, new UpdatePlaylistRequest("Moderated", null, null), default);

        Assert.Equal("Moderated", NameOf(_privatePlaylist.Id));
    }

    [Fact]
    public async Task AddingATrackTheCallerIsRestrictedFrom_IsRefused()
    {
        var strangersPlaylist = new Playlist { Name = "Stranger's", OwnerUserId = _stranger.Id };
        _db.Add(strangersPlaylist);
        await _db.SaveChangesAsync();

        // Restricted content must not become reachable simply by routing it through a
        // playlist the caller does own.
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Playlists(_stranger).AddTrack(
                strangersPlaylist.Id, new AddTrackToPlaylistRequest(_strangerBlockedTrack.Id), default));

        Assert.Empty(_db.PlaylistItems.AsNoTracking().Where(i => i.PlaylistId == strangersPlaylist.Id));
    }

    [Fact]
    public async Task AddingAnUnrestrictedTrack_Works()
    {
        var strangersPlaylist = new Playlist { Name = "Stranger's", OwnerUserId = _stranger.Id };
        _db.Add(strangersPlaylist);
        await _db.SaveChangesAsync();

        await Playlists(_stranger).AddTrack(
            strangersPlaylist.Id, new AddTrackToPlaylistRequest(_plainTrack.Id), default);

        Assert.Single(_db.PlaylistItems.AsNoTracking().Where(i => i.PlaylistId == strangersPlaylist.Id));
    }

    // ── Mixed tapes ──

    [Fact]
    public async Task AMixedTape_IsNotReadableByAnotherUser()
    {
        var result = await Tapes(_stranger).GetById(_tape.Id, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task AMixedTape_IsReadableByItsOwner()
    {
        var tape = Value(await Tapes(_owner).GetById(_tape.Id, default));

        Assert.Equal("Owner Tape", tape.Name);
        Assert.Equal(2, tape.TrackCount);
    }

    [Fact]
    public async Task AMixedTapeListing_ShowsOnlyTheCallersOwn()
    {
        Assert.Empty(Values(await Tapes(_stranger).GetAll(default)));
        Assert.Single(Values(await Tapes(_owner).GetAll(default)));
    }

    [Fact]
    public async Task AMixedTape_HidesEntriesTheReaderIsRestrictedFrom()
    {
        // The admin is unrestricted; give them the stranger's deny list and read as them.
        _db.Add(new UserRestriction
        {
            UserId = _admin.Id, RestrictionType = RestrictionType.Artist, TargetId = _blockedArtist.Id
        });
        await _db.SaveChangesAsync();

        var tape = Value(await Tapes(_admin, isAdmin: true).GetById(_tape.Id, default));

        Assert.Equal(["Everyone Can Hear This"], tape.Items.Select(i => i.TrackTitle).ToArray());
        Assert.Equal(1, tape.TrackCount);
        // The totals describe what is shown, not what is stored.
        Assert.Equal(120, tape.TotalDurationSeconds);
    }

    [Fact]
    public async Task AnotherUser_CannotDeleteOrEditAMixedTape()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Tapes(_stranger).Delete(_tape.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Tapes(_stranger).AddTrack(_tape.Id, new AddTrackToMixedTapeRequest(_plainTrack.Id), default));

        var itemId = _db.MixedTapeItems.AsNoTracking().First(i => i.MixedTapeId == _tape.Id).Id;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Tapes(_stranger).RemoveItem(_tape.Id, itemId, default));

        Assert.True(_db.MixedTapes.AsNoTracking().Any(m => m.Id == _tape.Id));
        Assert.Equal(2, _db.MixedTapeItems.AsNoTracking().Count(i => i.MixedTapeId == _tape.Id));
    }

    [Fact]
    public async Task AddingARestrictedTrackToOnesOwnMixedTape_IsRefused()
    {
        var strangersTape = new MixedTape { Name = "Stranger's", OwnerUserId = _stranger.Id, TargetDurationSeconds = 3600 };
        _db.Add(strangersTape);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Tapes(_stranger).AddTrack(strangersTape.Id, new AddTrackToMixedTapeRequest(_strangerBlockedTrack.Id), default));

        Assert.Empty(_db.MixedTapeItems.AsNoTracking().Where(i => i.MixedTapeId == strangersTape.Id));
    }

    [Fact]
    public async Task TheOwner_CanStillDeleteTheirMixedTape()
    {
        await Tapes(_owner).Delete(_tape.Id, default);

        Assert.False(_db.MixedTapes.AsNoTracking().Any(m => m.Id == _tape.Id));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
