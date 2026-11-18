namespace SonaFly.Services;

/// <summary>
/// Opens the library's detail pages on the navigation stack, so the system back
/// button, the back gesture and the arrow in the title bar all return from them.
/// </summary>
public static class LibraryNavigator
{
    public const string AlbumRoute = "album";
    public const string ArtistRoute = "artist";
    public const string PlaylistRoute = "playlist";
    public const string MixedTapeRoute = "mixedtape";

    public const string IdParameter = "id";
    public const string TitleParameter = "title";

    public static Task OpenAlbumAsync(Guid id, string? title) => OpenAsync(AlbumRoute, id, title);
    public static Task OpenArtistAsync(Guid id, string? name) => OpenAsync(ArtistRoute, id, name);
    public static Task OpenPlaylistAsync(Guid id, string? name) => OpenAsync(PlaylistRoute, id, name);
    public static Task OpenMixedTapeAsync(Guid id, string? name) => OpenAsync(MixedTapeRoute, id, name);

    /// <summary>Closes the page on top, as the back button would.</summary>
    public static Task CloseAsync() => Shell.Current.GoToAsync("..");

    // Single-use parameters: they are handed to the new page once and not replayed
    // when the user comes back to it from a page opened above it.
    private static Task OpenAsync(string route, Guid id, string? title) =>
        Shell.Current.GoToAsync(route, new ShellNavigationQueryParameters
        {
            [IdParameter] = id,
            [TitleParameter] = title ?? string.Empty,
        });

    /// <summary>Reads what <see cref="OpenAsync"/> passed. False when there is no id.</summary>
    public static bool TryRead(IDictionary<string, object> query, out Guid id, out string? title)
    {
        title = query.TryGetValue(TitleParameter, out var t) && t is string s && s.Length > 0 ? s : null;
        if (query.TryGetValue(IdParameter, out var value) && value is Guid guid)
        {
            id = guid;
            return true;
        }

        id = default;
        return false;
    }
}
