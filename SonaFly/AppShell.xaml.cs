using SonaFly.Services;
using SonaFly.Views;

namespace SonaFly;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Detail pages are pushed above whichever tab opened them.
        Routing.RegisterRoute(LibraryNavigator.AlbumRoute, typeof(AlbumDetailPage));
        Routing.RegisterRoute(LibraryNavigator.ArtistRoute, typeof(ArtistAlbumsPage));
        Routing.RegisterRoute(LibraryNavigator.PlaylistRoute, typeof(PlaylistDetailPage));
        Routing.RegisterRoute(LibraryNavigator.MixedTapeRoute, typeof(MixedTapeDetailPage));
    }
}
