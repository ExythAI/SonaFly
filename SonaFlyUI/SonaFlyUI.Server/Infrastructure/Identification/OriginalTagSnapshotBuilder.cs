using System.Text.Json;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Entities.Identification;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// Builds the preserved tag evidence for one file revision (upgrade plan 5.1).
/// Original strings are kept verbatim; normalization for scoring happens on
/// copies in <see cref="TextNormalization"/>. Totals, dates, ISRC, barcode,
/// catalog number, and MusicBrainz IDs are captured where TagLib exposes them;
/// format-specific mappings for MP3 and FLAC become explicit here as fixture
/// coverage lands, instead of living in ad-hoc reader code.
/// </summary>
public static class OriginalTagSnapshotBuilder
{
    private const int MaxRawJsonLength = 8000;

    public static OriginalTagSnapshot Build(AudioMetadata metadata, Guid fileRevisionId, string parserVersion)
    {
        // Shorten the string fields rather than the serialized text, so the
        // stored value always stays parseable JSON.
        var raw = SerializeRaw(metadata, maxFieldLength: null);
        for (var cap = 2048; raw.Length > MaxRawJsonLength; cap /= 2)
        {
            raw = SerializeRaw(metadata, cap);
        }

        return new OriginalTagSnapshot
        {
            FileRevisionId = fileRevisionId,
            Title = metadata.Title,
            Album = metadata.Album,
            Artist = metadata.Artist,
            AlbumArtist = metadata.AlbumArtist,
            Genre = metadata.Genre,
            Year = metadata.Year,
            TrackNumber = metadata.TrackNumber,
            DiscNumber = metadata.DiscNumber,
            RawFieldsJson = raw,
            ParserVersion = parserVersion,
            CapturedUtc = DateTime.UtcNow
        };
    }

    private static string SerializeRaw(AudioMetadata metadata, int? maxFieldLength) =>
        JsonSerializer.Serialize(new
        {
            Title = Truncate(metadata.Title, maxFieldLength),
            Album = Truncate(metadata.Album, maxFieldLength),
            Artist = Truncate(metadata.Artist, maxFieldLength),
            AlbumArtist = Truncate(metadata.AlbumArtist, maxFieldLength),
            Genre = Truncate(metadata.Genre, maxFieldLength),
            metadata.Year,
            metadata.TrackNumber,
            metadata.DiscNumber
        });

    private static string? Truncate(string? value, int? maxLength)
    {
        if (value == null || maxLength == null || value.Length <= maxLength)
        {
            return value;
        }

        var length = maxLength.Value;
        // Never split a surrogate pair.
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }
}

