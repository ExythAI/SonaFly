using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using TagLib;

namespace SonaFlyUI.Server.Infrastructure.Services;

public class MetadataReader : IMetadataReader
{
    private readonly ILogger<MetadataReader> _logger;

    public MetadataReader(ILogger<MetadataReader> logger)
    {
        _logger = logger;
    }

    public Task<AudioMetadata> ReadAsync(string filePath, CancellationToken ct)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var tag = tagFile.Tag;
            var props = tagFile.Properties;

            byte[]? artworkData = null;
            string? artworkMimeType = null;

            if (tag.Pictures.Length > 0)
            {
                var pic = tag.Pictures[0];
                artworkData = pic.Data.Data;
                artworkMimeType = pic.MimeType;
            }

            var metadata = new AudioMetadata
            {
                Title = NormalizeString(tag.Title) ?? Path.GetFileNameWithoutExtension(filePath),
                Album = NormalizeString(tag.Album),
                Artist = NormalizeString(tag.FirstPerformer),
                AlbumArtist = NormalizeString(tag.FirstAlbumArtist),
                TrackNumber = tag.Track > 0 ? (int)tag.Track : null,
                DiscNumber = tag.Disc > 0 ? (int)tag.Disc : null,
                Genre = NormalizeString(tag.FirstGenre),
                Year = tag.Year > 0 ? (int)tag.Year : null,
                DurationSeconds = props.Duration.TotalSeconds > 0 ? props.Duration.TotalSeconds : null,
                BitRateKbps = props.AudioBitrate > 0 ? props.AudioBitrate : null,
                SampleRateHz = props.AudioSampleRate > 0 ? props.AudioSampleRate : null,
                MimeType = FileScanner.GetMimeType(Path.GetExtension(filePath)),
                ArtworkData = artworkData,
                ArtworkMimeType = artworkMimeType
            };

            return Task.FromResult(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read metadata from {FilePath}", filePath);

            // Return fallback with filename as title
            return Task.FromResult(new AudioMetadata
            {
                Title = Path.GetFileNameWithoutExtension(filePath),
                MimeType = FileScanner.GetMimeType(Path.GetExtension(filePath))
            });
        }
    }

    private static string? NormalizeString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim();
    }
}
