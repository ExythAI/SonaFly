using SonaFly.Models;
using SonaFly.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// The player's progress reporting: what the mini player's times and seek slider show.
/// </summary>
public sealed class PlaybackProgressTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5.9, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(600, "10:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3661, "1:01:01")]
    [InlineData(-3, "0:00")]
    [InlineData(double.NaN, "0:00")]
    [InlineData(double.PositiveInfinity, "0:00")]
    public void TimesAreFormattedForDisplay(double seconds, string expected)
    {
        Assert.Equal(expected, PlaybackProgress.Format(seconds));
    }

    [Fact]
    public void APositionIsKeptWithinAKnownLength()
    {
        Assert.Equal(200, PlaybackProgress.Clamp(250, 200));
        Assert.Equal(120, PlaybackProgress.Clamp(120, 200));
        Assert.Equal(0, PlaybackProgress.Clamp(-5, 200));
        // An unknown length cannot clamp anything.
        Assert.Equal(250, PlaybackProgress.Clamp(250, 0));
        Assert.Equal(0, PlaybackProgress.Clamp(double.NaN, 200));
    }

    [Fact]
    public void ANewTrackStartsAtZeroWithItsCatalogLength()
    {
        var player = Player();
        player.Play(Track(180));
        player.ReportPosition(42);

        player.Play(Track(240));

        Assert.Equal(0, player.Position);
        Assert.Equal(240, player.Duration);
    }

    [Fact]
    public void TheMeasuredLengthReplacesTheCatalogOneButNeverWithNothing()
    {
        var player = Player();
        player.Play(Track(180));

        player.ReportDuration(181.4);
        Assert.Equal(181.4, player.Duration);

        player.ReportDuration(0);
        player.ReportDuration(double.NaN);
        Assert.Equal(181.4, player.Duration);
    }

    [Fact]
    public void SmallPositionChangesAreNotPushedToTheUi()
    {
        var player = Player();
        player.Play(Track(180));
        var notifications = 0;
        player.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AudioPlayerService.Position)) notifications++; };

        player.ReportPosition(1.0);
        player.ReportPosition(1.1);
        player.ReportPosition(1.2);
        player.ReportPosition(1.3);

        Assert.Equal(2, notifications);
        Assert.Equal(1.3, player.Position);
    }

    [Fact]
    public void AReportedPositionNeverPassesTheEndOfTheTrack()
    {
        var player = Player();
        player.Play(Track(180));

        player.ReportPosition(185);

        Assert.Equal(180, player.Position);
    }

    [Fact]
    public void PositionsFromBeforeASeekLandsDoNotDragTheSliderBack()
    {
        var player = Player();
        player.Play(Track(300));
        player.ReportPosition(10);
        double? requested = null;
        player.SeekRequested += position => requested = position;

        player.SeekTo(200);
        Assert.Equal(200, requested);
        Assert.Equal(200, player.Position);

        // The media element is still reporting from where it was.
        player.ReportPosition(10.5);
        player.ReportPosition(11);
        Assert.Equal(200, player.Position);

        // The seek lands and progress follows it again.
        player.ReportPosition(200.4);
        player.ReportPosition(203);
        Assert.Equal(203, player.Position);
    }

    [Fact]
    public void ASeekIsKeptWithinTheTrack()
    {
        var player = Player();
        player.Play(Track(180));
        double? requested = null;
        player.SeekRequested += position => requested = position;

        player.SeekTo(999);

        Assert.Equal(180, requested);
        Assert.Equal(180, player.Position);
    }

    [Fact]
    public void StoppingClearsProgress()
    {
        var player = Player();
        player.Play(Track(180));
        player.ReportPosition(42);

        player.Stop();

        Assert.Equal(0, player.Position);
        Assert.Equal(0, player.Duration);
        Assert.Null(player.CurrentTrack);
    }

    private static AudioPlayerService Player() =>
        new(new SonaFlyApiClient(new HttpClient(), new ServerStorageService()));

    private static TrackDto Track(double? duration) =>
        new(Guid.NewGuid(), "Song", "Artist", "Album", null, null, null, duration, null, null);
}
