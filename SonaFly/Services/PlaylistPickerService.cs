using SonaFly.Models;

namespace SonaFly.Services;

/// <summary>
/// Shows a bottom-sheet-style action sheet to pick a playlist, then adds the track to it.
/// </summary>
public class PlaylistPickerService
{
    private readonly SonaFlyApiClient _api;

    public PlaylistPickerService(SonaFlyApiClient api)
    {
        _api = api;
    }

    /// <summary>
    /// Shows a playlist picker and adds the given track to the selected playlist.
    /// Call this from a long-press gesture handler.
    /// </summary>
    public async Task ShowAndAddTrackAsync(Page page, TrackDto track)
    {
        if (track == null) return;

        try
        {
            var playlists = await _api.GetPlaylistsAsync();
            if (playlists == null || playlists.Count == 0)
            {
                var create = await page.DisplayAlert(
                    "No Playlists",
                    "You don't have any playlists yet. Create one?",
                    "Create", "Cancel");

                if (create)
                {
                    var name = await page.DisplayPromptAsync("New Playlist", "Enter playlist name:");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var id = await _api.CreatePlaylistAsync(name.Trim(), null);
                        if (id.HasValue)
                        {
                            await _api.AddTrackToPlaylistAsync(id.Value, track.Id);
                            await ShowToast(page, $"Added to \"{name.Trim()}\" ✓");
                        }
                    }
                }
                return;
            }

            // Build options list
            var options = playlists.Select(p => p.Name).ToList();
            options.Add("＋ Create New Playlist");

            var choice = await page.DisplayActionSheet(
                $"Add \"{track.Title}\" to playlist",
                "Cancel", null,
                options.ToArray());

            if (choice == null || choice == "Cancel") return;

            if (choice == "＋ Create New Playlist")
            {
                var name = await page.DisplayPromptAsync("New Playlist", "Enter playlist name:");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var id = await _api.CreatePlaylistAsync(name.Trim(), null);
                    if (id.HasValue)
                    {
                        await _api.AddTrackToPlaylistAsync(id.Value, track.Id);
                        await ShowToast(page, $"Added to \"{name.Trim()}\" ✓");
                    }
                }
            }
            else
            {
                var playlist = playlists.FirstOrDefault(p => p.Name == choice);
                if (playlist != null)
                {
                    await _api.AddTrackToPlaylistAsync(playlist.Id, track.Id);
                    await ShowToast(page, $"Added to \"{playlist.Name}\" ✓");
                }
            }
        }
        catch (Exception ex)
        {
            await page.DisplayAlert("Error", $"Failed to add track: {ex.Message}", "OK");
        }
    }

    private static async Task ShowToast(Page page, string message)
    {
        // Simple toast via DisplayAlert with auto-dismiss feel
        await page.DisplayAlert("✓", message, "OK");
    }
}
