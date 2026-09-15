using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SonaFlyUI.Server.Api.Hubs;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// The product rule for auditoriums: a room admits a track based on the restrictions of
/// whoever queued it, and keeps playing. A listener whose own restrictions exclude that
/// track is told so plainly instead of being shown a generic playback failure.
///
/// These cover <see cref="AuditoriumHub.CanPlayCurrentTrack"/>, which is what the client
/// asks before it starts shared playback, for listeners restricted by artist, album and
/// genre in turn.
/// </summary>
public sealed class AuditoriumRestrictionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly SonaFlyDbContext _db;
    private readonly AuditoriumStateService _state = new();

    // Shared playback now refuses a track whose file is not actually on disk, so the fixture
    // needs a real file rather than a plausible-looking path (backlog N19).
    private readonly string _audioFile = Path.GetTempFileName();
    private readonly TrackEndSchedulerService _scheduler;

    private readonly ApplicationUser _queuer = new() { Id = Guid.NewGuid(), UserName = "queuer", Email = "q@sonafly.local", IsEnabled = true };
    private readonly ApplicationUser _listener = new() { Id = Guid.NewGuid(), UserName = "listener", Email = "l@sonafly.local", IsEnabled = true };
    private readonly Artist _artist = new() { Name = "Artist" };
    private readonly Genre _genre = new() { Name = "Genre" };
    private readonly Album _album = new() { Title = "Album" };
    private readonly Track _track;
    private readonly Auditorium _auditorium;

    public AuditoriumRestrictionTests()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        _db = _services.GetRequiredService<SonaFlyDbContext>();
        _db.Database.EnsureCreated();

        _album.AlbumArtist = _artist;
        _track = new Track
        {
            Title = "Shared Track",
            PrimaryArtist = _artist,
            Album = _album,
            LibraryRoot = new LibraryRoot { Name = "Test", Path = "test" },
            FilePath = _audioFile,
            FileName = "shared.mp3",
            MimeType = "audio/mpeg",
            DurationSeconds = 60,
            IsIndexed = true
        };
        _track.TrackGenres.Add(new TrackGenre { Track = _track, Genre = _genre });

        _auditorium = new Auditorium { Name = "Room", CreatedByUserId = _queuer.Id };
        _db.AddRange(_queuer, _listener, _artist, _genre, _album, _track, _auditorium);
        _db.SaveChanges();

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hubContext = new Mock<IHubContext<AuditoriumHub>>();
        hubContext.SetupGet(h => h.Clients).Returns(clients.Object);
        _scheduler = new TrackEndSchedulerService(hubContext.Object,
            _services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IHostApplicationLifetime>(), _state, NullLogger<TrackEndSchedulerService>.Instance);
    }

    /// <summary>A hub whose caller is <paramref name="user"/>, already joined to the room.</summary>
    private async Task<AuditoriumHub> JoinedHubAsync(ApplicationUser user)
    {
        var hub = new AuditoriumHub(_state, _db, NullLogger<AuditoriumHub>.Instance, _scheduler)
        {
            Context = HubContextFor(user),
            Groups = Mock.Of<IGroupManager>(),
            Clients = ClientsThatIgnoreEverything()
        };
        await hub.JoinAuditorium(_auditorium.Id);
        return hub;
    }

    private static HubCallerContext HubContextFor(ApplicationUser user)
    {
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns(Guid.NewGuid().ToString());
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
             new Claim(ClaimTypes.Name, user.UserName!)], "Test")));
        return context.Object;
    }

    private static IHubCallerClients ClientsThatIgnoreEverything()
    {
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubCallerClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        return clients.Object;
    }

    private async Task RestrictListenerAsync(RestrictionType type, Guid targetId)
    {
        _db.UserRestrictions.Add(new UserRestriction
        {
            UserId = _listener.Id,
            RestrictionType = type,
            TargetId = targetId
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task WithNoRestrictions_EveryListenerCanPlayTheRoomsTrack()
    {
        var queuer = await JoinedHubAsync(_queuer);
        await queuer.PlayTrack(_track.Id);

        var listener = await JoinedHubAsync(_listener);

        Assert.True((await listener.CanPlayCurrentTrack()).Playable);
        Assert.True((await queuer.CanPlayCurrentTrack()).Playable);
    }

    [Theory]
    [InlineData(RestrictionType.Artist)]
    [InlineData(RestrictionType.Album)]
    [InlineData(RestrictionType.Genre)]
    public async Task AListenerRestrictedFromTheTrack_IsToldSo_WhileTheRoomKeepsPlaying(RestrictionType type)
    {
        var queuer = await JoinedHubAsync(_queuer);
        await queuer.PlayTrack(_track.Id);

        var targetId = type switch
        {
            RestrictionType.Artist => _artist.Id,
            RestrictionType.Album => _album.Id,
            _ => _genre.Id
        };
        await RestrictListenerAsync(type, targetId);

        var listener = await JoinedHubAsync(_listener);

        Assert.False((await listener.CanPlayCurrentTrack()).Playable);

        // The room is unaffected: it keeps the track, and the queuer plays on.
        Assert.True((await queuer.CanPlayCurrentTrack()).Playable);
        Assert.Equal(_track.Id, _state.GetRoom(_auditorium.Id)!.CurrentTrackId);
    }

    [Fact]
    public async Task TheQueuersRestrictionsStillDecideWhatEntersTheRoom()
    {
        await RestrictListenerAsync(RestrictionType.Artist, _artist.Id);
        var listener = await JoinedHubAsync(_listener);

        // A restricted user cannot put the track into the room in the first place.
        await Assert.ThrowsAsync<HubException>(() => listener.PlayTrack(_track.Id));
    }

    [Fact]
    public async Task WithNothingPlaying_TheAnswerIsYesRatherThanAFalseAlarm()
    {
        var listener = await JoinedHubAsync(_listener);

        // No current track means nothing to be restricted from; the client must not show
        // an "unavailable" notice in an idle room.
        var answer = await listener.CanPlayCurrentTrack();
        Assert.True(answer.Playable);
        Assert.Null(answer.TrackId);
    }

    [Fact]
    public async Task TheAnswerNamesTheTrackItIsAbout()
    {
        // Without this the client cannot tell a late "yes" about a finished track from a
        // "yes" about the one it is waiting to start, and would replay the old one.
        var queuer = await JoinedHubAsync(_queuer);
        await queuer.PlayTrack(_track.Id);

        var listener = await JoinedHubAsync(_listener);

        Assert.Equal(_track.Id, (await listener.CanPlayCurrentTrack()).TrackId);
    }

    [Fact]
    public async Task TheAnswerFollowsTheRoomWhenTheTrackChanges()
    {
        var queuer = await JoinedHubAsync(_queuer);
        await queuer.PlayTrack(_track.Id);
        var listener = await JoinedHubAsync(_listener);

        var before = await listener.CanPlayCurrentTrack();

        await queuer.StopTrack();
        var after = await listener.CanPlayCurrentTrack();

        Assert.Equal(_track.Id, before.TrackId);
        Assert.Null(after.TrackId);
    }

    [Fact]
    public async Task ItOnlyAnswersForTheRoomTheCallerHasJoined()
    {
        var hub = new AuditoriumHub(_state, _db, NullLogger<AuditoriumHub>.Instance, _scheduler)
        {
            Context = HubContextFor(_listener),
            Groups = Mock.Of<IGroupManager>(),
            Clients = ClientsThatIgnoreEverything()
        };

        // Never joined: there is no room whose track it could report on.
        await Assert.ThrowsAsync<HubException>(() => hub.CanPlayCurrentTrack());
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
        File.Delete(_audioFile);
    }
}
