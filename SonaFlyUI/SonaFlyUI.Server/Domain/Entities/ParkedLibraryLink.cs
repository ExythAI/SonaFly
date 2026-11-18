namespace SonaFlyUI.Server.Domain.Entities;

/// <summary>
/// Something a person made that pointed at library data which a purge removed: an entry
/// in a playlist or mixed tape, a favourite, or a content restriction.
/// <para>
/// A purge deletes every track, album, artist and genre, and a rescan issues new IDs, so
/// anything that referred to the old IDs would be lost. Instead the purge parks each
/// reference here under something that survives a rescan (the file path for a track, the
/// name for an album, artist or genre) and the scan puts it back when the same thing
/// reappears. A row that never finds its target again stays parked and harms nothing.
/// </para>
/// <para>
/// Deliberately has no foreign keys: its whole purpose is to outlive the rows it names.
/// </para>
/// </summary>
public class ParkedLibraryLink : EntityBase
{
    public ParkedLinkKind Kind { get; set; }

    /// <summary>The playlist, the mixed tape, or (for favourites and restrictions) the user.</summary>
    public Guid OwnerId { get; set; }

    // ── Track references: playlist entries, mixed tape entries, favourites ──

    /// <summary>The track's file, which is what identifies it across a rescan.</summary>
    public string? FilePath { get; set; }
    public int SortOrder { get; set; }

    /// <summary>When the entry was originally added, so order and history survive.</summary>
    public DateTime AddedUtc { get; set; }

    // ── Restrictions ──

    public RestrictionType? RestrictionType { get; set; }

    /// <summary>The album title, artist name or genre name the restriction applied to.</summary>
    public string? TargetName { get; set; }

    /// <summary>For an album, its artist: two artists can each have a "Greatest Hits".</summary>
    public string? TargetArtistName { get; set; }
}

public enum ParkedLinkKind
{
    PlaylistItem = 0,
    MixedTapeItem = 1,
    Restriction = 2,
    Favorite = 3,
}
