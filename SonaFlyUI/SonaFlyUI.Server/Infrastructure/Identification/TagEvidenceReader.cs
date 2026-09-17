using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Entities.Identification;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// The tags exactly as the file states them (upgrade plan 5.1). Unlike the
/// playback scan's reader, a missing title stays missing instead of becoming
/// the filename, and identification-only fields are included: totals, ISRC,
/// and embedded MusicBrainz IDs. Picard stores the recording ID under
/// "MusicBrainz Track Id", which TagLib exposes as MusicBrainzTrackId.
/// </summary>
public sealed record TagEvidence(
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    string? Genre,
    int? Year,
    int? TrackNumber,
    int? TrackTotal,
    int? DiscNumber,
    int? DiscTotal,
    string? Isrc,
    string? MusicBrainzRecordingId,
    string? MusicBrainzReleaseId,
    string? MusicBrainzReleaseGroupId);

public interface ITagEvidenceReader
{
    /// <summary>Reads the file's tags, or null when they cannot be parsed.</summary>
    TagEvidence? Read(string filePath);
}

public sealed class TagEvidenceReader : ITagEvidenceReader
{
    public const string ParserVersion = "TagLibSharp 2.3";

    private readonly ILogger<TagEvidenceReader> _logger;

    public TagEvidenceReader(ILogger<TagEvidenceReader> logger)
    {
        _logger = logger;
    }

    public TagEvidence? Read(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var tag = file.Tag;
            return new TagEvidence(
                Clean(tag.Title, 512),
                Clean(tag.FirstPerformer, 512),
                Clean(tag.Album, 512),
                Clean(tag.FirstAlbumArtist, 512),
                Clean(tag.FirstGenre, 256),
                tag.Year > 0 ? (int)tag.Year : null,
                tag.Track > 0 ? (int)tag.Track : null,
                tag.TrackCount > 0 ? (int)tag.TrackCount : null,
                tag.Disc > 0 ? (int)tag.Disc : null,
                tag.DiscCount > 0 ? (int)tag.DiscCount : null,
                Clean(tag.ISRC, 32),
                Clean(tag.MusicBrainzTrackId, 64),
                Clean(tag.MusicBrainzReleaseId, 64),
                Clean(tag.MusicBrainzReleaseGroupId, 64));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tag evidence could not be read.");
            return null;
        }
    }

    /// <summary>Builds the preserved snapshot; unreadable tags give an empty one.</summary>
    public static OriginalTagSnapshot BuildSnapshot(TagEvidence? evidence, Guid fileRevisionId)
    {
        var metadata = evidence == null
            ? new AudioMetadata()
            : new AudioMetadata
            {
                Title = evidence.Title,
                Artist = evidence.Artist,
                Album = evidence.Album,
                AlbumArtist = evidence.AlbumArtist,
                Genre = evidence.Genre,
                Year = evidence.Year,
                TrackNumber = evidence.TrackNumber,
                DiscNumber = evidence.DiscNumber
            };

        var snapshot = OriginalTagSnapshotBuilder.Build(metadata, fileRevisionId, ParserVersion);
        if (evidence != null)
        {
            snapshot.TrackTotal = evidence.TrackTotal;
            snapshot.DiscTotal = evidence.DiscTotal;
            snapshot.Isrc = evidence.Isrc;
            snapshot.MusicBrainzRecordingId = evidence.MusicBrainzRecordingId;
            snapshot.MusicBrainzReleaseId = evidence.MusicBrainzReleaseId;
            snapshot.MusicBrainzReleaseGroupId = evidence.MusicBrainzReleaseGroupId;
        }

        return snapshot;
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
