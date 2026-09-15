using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Api.Hubs;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

public sealed class PlaybackTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly SonaFlyDbContext _db;
    private readonly AuditoriumStateService _state = new();
    private readonly Mock<IClientProxy> _client = new();
    private readonly TrackEndSchedulerService _scheduler;
    private readonly StreamTicketService _tickets = new(new EphemeralDataProtectionProvider());
    private readonly string _audioFile = Path.GetTempFileName();
    private readonly ApplicationUser _user = new() { Id = Guid.NewGuid(), UserName = "listener", IsEnabled = true, SecurityStamp = "stamp" };
    private readonly Auditorium _auditorium;
    private readonly Track _track;

    public PlaybackTests()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        _db = NewDb();
        _db.Database.EnsureCreated();
        var artist = new Artist { Name = "Artist" };
        _track = new Track { Title = "First", FilePath = _audioFile, FileName = "first.mp3",
            MimeType = "audio/mpeg", IsIndexed = true, PrimaryArtist = artist,
            DurationSeconds = 60, LibraryRoot = new LibraryRoot { Name = "Test", Path = "test" } };
        _auditorium = new Auditorium { Name = "Room", CreatedByUserId = _user.Id };
        _db.AddRange(_user, _track, _auditorium);
        _db.SaveChanges();

        _client.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_client.Object);
        var hub = new Mock<IHubContext<AuditoriumHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        _scheduler = new TrackEndSchedulerService(hub.Object, _services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IHostApplicationLifetime>(), _state, NullLogger<TrackEndSchedulerService>.Instance);
    }

    private SonaFlyDbContext NewDb() => new(new DbContextOptionsBuilder<SonaFlyDbContext>().UseSqlite(_connection).Options);
    private ClaimsPrincipal Principal => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString()), new Claim(ClaimTypes.Name, "listener")], "test"));

    private StreamController Controller(bool authenticated = false) => new(new StreamingService(_db), _tickets, _db)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = authenticated ? Principal : new ClaimsPrincipal() } }
    };

    [Fact]
    public async Task StreamingRequiresCredentialsAndTicketIsTrackScoped()
    {
        var controller = Controller();
        Assert.IsType<UnauthorizedResult>(await controller.StreamTrack(_track.Id, null, default));
        var ticket = _tickets.Create(_user.Id, _track.Id, _user.SecurityStamp);
        Assert.IsType<UnauthorizedResult>(await controller.StreamTrack(Guid.NewGuid(), ticket, default));
        Assert.IsType<UnauthorizedResult>(await controller.StreamTrack(_track.Id, "invalid", default));
        var result = Assert.IsType<FileStreamResult>(await controller.StreamTrack(_track.Id, ticket, default));
        Assert.True(result.EnableRangeProcessing);
        await result.FileStream.DisposeAsync();
    }

    [Fact]
    public async Task TicketsRecheckRestrictionsAndAccountStatus()
    {
        var controller = Controller();
        var ticket = _tickets.Create(_user.Id, _track.Id, _user.SecurityStamp);
        _user.IsEnabled = false;
        await _db.SaveChangesAsync();
        Assert.IsType<UnauthorizedResult>(await controller.StreamTrack(_track.Id, ticket, default));
        _user.IsEnabled = true;
        _db.UserRestrictions.Add(new UserRestriction { UserId = _user.Id,
            RestrictionType = RestrictionType.Artist, TargetId = _track.PrimaryArtistId!.Value });
        await _db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.StreamTrack(_track.Id, ticket, default));
        Assert.IsType<NotFoundResult>(await Controller(true).GetUrl(_track.Id, default));
    }

    [Fact]
    public async Task AuthenticatedClientCanRequestAPlayableTicket()
    {
        var result = Assert.IsType<OkObjectResult>(await Controller(true).GetUrl(_track.Id, default));
        var url = JsonSerializer.SerializeToElement(result.Value).GetProperty("url").GetString()!;
        var ticket = Uri.UnescapeDataString(url.Split("ticket=")[1]);
        var file = Assert.IsType<FileStreamResult>(await Controller().StreamTrack(_track.Id, ticket, default));
        await file.FileStream.DisposeAsync();
        _user.SecurityStamp = "changed";
        await _db.SaveChangesAsync();
        Assert.IsType<UnauthorizedResult>(await Controller().StreamTrack(_track.Id, ticket, default));
    }

    [Fact]
    public void ExpiredTicketsAreRejected()
    {
        var provider = new EphemeralDataProtectionProvider();
        var token = provider.CreateProtector("SonaFly.StreamTicket.v1").Protect(JsonSerializer.Serialize(
            new StreamTicket(_user.Id, _track.Id, _user.SecurityStamp, DateTimeOffset.UtcNow.AddMinutes(-1))));
        Assert.Null(new StreamTicketService(provider).Validate(token, _track.Id));
    }

    [Fact]
    public async Task PausedTimerDoesNotAdvanceAndResumeSchedulesCompletion()
    {
        var room = _state.GetOrCreateRoom(_auditorium.Id);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.Setup(c => c.SendCoreAsync("OnTrackEnded", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => ended.TrySetResult()).Returns(Task.CompletedTask);
        using (await room.EnterAsync())
        {
            room.StartTrack(_track.Id, "First", null, null, 0, _user.Id, "listener");
            _scheduler.ScheduleTrackEnd(room);
            room.Pause();
        }
        await Task.Delay(1200);
        using (await room.EnterAsync())
        {
            Assert.Equal(_track.Id, room.CurrentTrackId);
            Assert.False(ended.Task.IsCompleted);
            room.Resume();
            _scheduler.ScheduleTrackEnd(room);
        }
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using (await room.EnterAsync()) Assert.Null(room.CurrentTrackId);
    }

    [Fact]
    public async Task TimerFromPreviousPlaybackCannotStopSameTrackReplay()
    {
        var room = _state.GetOrCreateRoom(_auditorium.Id);
        using (await room.EnterAsync())
        {
            room.StartTrack(_track.Id, "First", null, null, 0, _user.Id, "listener");
            _scheduler.ScheduleTrackEnd(room);
            room.StopPlayback();
            room.StartTrack(_track.Id, "Replay", null, null, 60, _user.Id, "listener");
            _scheduler.ScheduleTrackEnd(room);
        }
        await Task.Delay(1200);
        using (await room.EnterAsync())
        {
            Assert.Equal("Replay", room.CurrentTrackTitle);
            Assert.Equal(_track.Id, room.CurrentTrackId);
            room.StopPlayback();
        }
    }

    [Fact]
    public async Task RestartLoadsQueueInPositionOrderAndAdvancementRemovesOnlyHead()
    {
        var first = new AuditoriumQueueItem { AuditoriumId = _auditorium.Id, TrackId = _track.Id,
            QueuedByUserId = _user.Id, Position = 10 };
        var second = new AuditoriumQueueItem { AuditoriumId = _auditorium.Id, TrackId = _track.Id,
            QueuedByUserId = _user.Id, Position = 20 };
        _db.AddRange(second, first);
        await _db.SaveChangesAsync();
        var room = new AuditoriumStateService().GetOrCreateRoom(_auditorium.Id);
        using (await room.EnterAsync())
        {
            await _scheduler.LoadQueueAsync(room, _db);
            await _scheduler.LoadQueueAsync(room, _db);
            Assert.Equal(new[] { first.Id, second.Id }, room.Queue.Select(q => q.Id));
            await _scheduler.TryPlayNextFromQueue(room);
            Assert.Equal(second.Id, Assert.Single(room.Queue).Id);
            Assert.Equal(_track.Id, room.CurrentTrackId);
            Assert.True(room.IsPaused);
        }
        Assert.Equal(second.Id, (await _db.AuditoriumQueueItems.AsNoTracking().SingleAsync()).Id);
    }

    private AuditoriumHub Hub(SonaFlyDbContext db)
    {
        var caller = new Mock<HubCallerContext>();
        caller.SetupGet(c => c.ConnectionId).Returns(Guid.NewGuid().ToString());
        caller.SetupGet(c => c.User).Returns(Principal);
        var clients = new Mock<IHubCallerClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_client.Object);
        var groups = new Mock<IGroupManager>();
        groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        groups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new AuditoriumHub(_state, db, NullLogger<AuditoriumHub>.Instance, _scheduler)
            { Context = caller.Object, Clients = clients.Object, Groups = groups.Object };
    }

    [Fact]
    public async Task ConcurrentEnqueuesStartOneTrackAndPreserveTheRest()
    {
        using var db1 = NewDb();
        using var db2 = NewDb();
        var hub1 = Hub(db1);
        var hub2 = Hub(db2);
        await hub1.JoinAuditorium(_auditorium.Id);
        await hub2.JoinAuditorium(_auditorium.Id);
        await Task.WhenAll(Task.Run(() => hub1.QueueTrack(_track.Id)), Task.Run(() => hub2.QueueTrack(_track.Id)));
        var room = _state.GetRoom(_auditorium.Id)!;
        using (await room.EnterAsync())
        {
            Assert.Equal(_track.Id, room.CurrentTrackId);
            Assert.Single(room.Queue);
        }
        Assert.Equal(1, await _db.AuditoriumQueueItems.CountAsync());
        _client.Verify(c => c.SendCoreAsync("OnTrackStarted", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        await hub1.LeaveAuditorium();
        await hub2.LeaveAuditorium();
    }

    public void Dispose()
    {
        _db.Dispose();
        _services.Dispose();
        _connection.Dispose();
        File.Delete(_audioFile);
    }
}
