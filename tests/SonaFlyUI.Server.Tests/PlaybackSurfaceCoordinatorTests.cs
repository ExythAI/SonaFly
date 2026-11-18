using SonaFly.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Which of the app's per-tab player surfaces owns playback. Ownership must survive
/// a tab switch: the owner keeps sounding, so it is the one that pauses and advances.
/// </summary>
public sealed class PlaybackSurfaceCoordinatorTests
{
    private readonly PlaybackSurfaceCoordinator _coordinator = new();
    private readonly object _home = new();
    private readonly object _browse = new();
    private readonly object _search = new();

    public PlaybackSurfaceCoordinatorTests()
    {
        _coordinator.Register(_home);
        _coordinator.Register(_browse);
        _coordinator.Register(_search);
    }

    [Fact]
    public void ANewStreamLoadsOnTheVisibleSurface()
    {
        _coordinator.MarkVisible(_home);

        Assert.Same(_home, _coordinator.ResolveLoader(1));
        Assert.True(_coordinator.IsOwner(_home));
        Assert.False(_coordinator.IsOwner(_browse));
    }

    [Fact]
    public void SwitchingTabsDoesNotMoveOwnership()
    {
        _coordinator.MarkVisible(_home);
        _coordinator.ResolveLoader(1);

        // The user moves to another tab while the track keeps playing from Home.
        _coordinator.MarkHidden(_home);
        _coordinator.MarkVisible(_browse);

        // Pause, seek and end-of-track still belong to Home's media element.
        Assert.True(_coordinator.IsOwner(_home));
        Assert.False(_coordinator.IsOwner(_browse));
    }

    [Fact]
    public void TheNextStreamMovesToTheTabNowOnScreen()
    {
        _coordinator.MarkVisible(_home);
        _coordinator.ResolveLoader(1);
        _coordinator.MarkHidden(_home);
        _coordinator.MarkVisible(_browse);

        Assert.Same(_browse, _coordinator.ResolveLoader(2));
        Assert.True(_coordinator.IsOwner(_browse));
        Assert.False(_coordinator.IsOwner(_home));
    }

    [Fact]
    public void WithNoSurfaceOnScreenTheOwnerLoadsTheNextStream()
    {
        _coordinator.MarkVisible(_home);
        _coordinator.ResolveLoader(1);

        // A tab without a player surface (Settings) is showing when the track ends.
        _coordinator.MarkHidden(_home);

        Assert.Same(_home, _coordinator.ResolveLoader(2));
    }

    [Fact]
    public void EverySurfaceGetsTheSameAnswerForOneStream()
    {
        _coordinator.MarkVisible(_home);
        var first = _coordinator.ResolveLoader(7);

        // Visibility changes before a slower surface asks about the same stream.
        _coordinator.MarkHidden(_home);
        _coordinator.MarkVisible(_search);

        Assert.Same(first, _coordinator.ResolveLoader(7));
        Assert.Same(_home, first);
    }

    [Fact]
    public void ASurfaceHiddenByAnotherTabsAppearanceDoesNotClearThatTab()
    {
        // Appearing and disappearing callbacks can arrive in either order.
        _coordinator.MarkVisible(_home);
        _coordinator.MarkVisible(_browse);
        _coordinator.MarkHidden(_home);

        Assert.Same(_browse, _coordinator.ResolveLoader(1));
    }

    [Fact]
    public void ARemovedOwnerGivesUpPlayback()
    {
        _coordinator.MarkVisible(_home);
        _coordinator.ResolveLoader(1);

        _coordinator.Unregister(_home);

        Assert.False(_coordinator.IsOwner(_home));
        Assert.NotSame(_home, _coordinator.ResolveLoader(2));
    }

    [Fact]
    public void WithNothingRegisteredThereIsNoLoader()
    {
        Assert.Null(new PlaybackSurfaceCoordinator().ResolveLoader(1));
    }

    [Fact]
    public void TheFirstStreamStartedFromADetailPageLoadsOnTheTabUnderneath()
    {
        // Browse was on screen, then a detail page covered it. Detail pages hold no
        // surface, so nothing is visible when the first track is chosen there.
        _coordinator.MarkVisible(_browse);
        _coordinator.MarkHidden(_browse);

        Assert.Same(_browse, _coordinator.ResolveLoader(1));
    }

    [Fact]
    public void ADetailPageOnAnotherTabLeavesPlaybackWithItsOwner()
    {
        _coordinator.MarkVisible(_home);
        _coordinator.ResolveLoader(1);
        _coordinator.MarkHidden(_home);
        _coordinator.MarkVisible(_browse);
        _coordinator.MarkHidden(_browse);

        Assert.Same(_home, _coordinator.ResolveLoader(2));
    }
}
