namespace SonaFly.Services;

/// <summary>
/// Decides which player surface owns playback.
///
/// Every tab hosts its own hidden media element, because a media element has to live
/// inside a page and stops if it is moved while playing. Only one of them may hold the
/// stream at a time: the <em>owner</em>. Ownership is separate from visibility: audio
/// keeps coming from the owner after the user moves to another tab, so pause, seek and
/// end-of-track must be handled by the owner, not by whichever surface is on screen.
/// Treating "visible" as "in charge" is what made pause and auto-advance stop working
/// after a tab switch.
///
/// A new stream moves to the visible surface when there is one, and otherwise stays
/// with the current owner, so playback also advances on a screen that has no surface.
/// Pushed detail pages are such screens: they show the player controls but hold no
/// media element, because theirs would be torn down, and the audio with it, as soon as
/// the page was closed. The first track started from one loads on the surface that was
/// on screen last, which is the tab page underneath.
/// </summary>
public sealed class PlaybackSurfaceCoordinator
{
    private readonly object _sync = new();
    private readonly List<object> _surfaces = [];
    private object? _visible;
    private object? _lastVisible;
    private object? _owner;
    private int? _resolvedVersion;

    /// <summary>Makes a surface known. Safe to call more than once.</summary>
    public void Register(object surface)
    {
        lock (_sync)
        {
            if (_surfaces.Contains(surface) == false)
            {
                _surfaces.Add(surface);
            }
        }
    }

    /// <summary>Forgets a surface that is going away, releasing ownership if it held it.</summary>
    public void Unregister(object surface)
    {
        lock (_sync)
        {
            _surfaces.Remove(surface);
            if (ReferenceEquals(_visible, surface)) _visible = null;
            if (ReferenceEquals(_lastVisible, surface)) _lastVisible = null;
            if (ReferenceEquals(_owner, surface)) _owner = null;
        }
    }

    /// <summary>The surface's page came on screen. It receives the next new stream.</summary>
    public void MarkVisible(object surface)
    {
        lock (_sync)
        {
            Register(surface);
            _visible = surface;
            _lastVisible = surface;
        }
    }

    /// <summary>The surface's page left the screen. Ownership is unaffected.</summary>
    public void MarkHidden(object surface)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_visible, surface)) _visible = null;
        }
    }

    /// <summary>
    /// The surface that must load stream <paramref name="streamVersion"/>, which then
    /// owns playback. Every surface hears about a new stream; they all get the same
    /// answer for one version, however many of them ask and in whatever order.
    /// </summary>
    public object? ResolveLoader(int streamVersion)
    {
        lock (_sync)
        {
            if (_resolvedVersion != streamVersion)
            {
                _resolvedVersion = streamVersion;
                _owner = _visible ?? _owner ?? _lastVisible ?? _surfaces.LastOrDefault();
            }

            return _owner;
        }
    }

    /// <summary>True when <paramref name="surface"/> holds the stream that is playing.</summary>
    public bool IsOwner(object surface)
    {
        lock (_sync)
        {
            return ReferenceEquals(_owner, surface);
        }
    }
}
