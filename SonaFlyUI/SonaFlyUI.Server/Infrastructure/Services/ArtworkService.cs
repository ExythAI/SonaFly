using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

public class ArtworkService : IArtworkService
{
    private readonly SonaFlyDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ArtworkService> _logger;
    private readonly OnlineArtworkService _onlineArtwork;

    // Common cover image filenames to search for in album folders
    private static readonly string[] CoverFileNames =
    [
        "cover", "folder", "front", "album", "artwork", "art", "thumb", "scan"
    ];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

    public ArtworkService(SonaFlyDbContext db, IConfiguration config, ILogger<ArtworkService> logger, OnlineArtworkService onlineArtwork)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _onlineArtwork = onlineArtwork;
    }

    private string ArtworkRoot => _config["SonaFly:ArtworkRoot"] ?? "./data/artwork";

    public async Task<ArtworkResult?> ExtractAndStoreAsync(AudioMetadata metadata, string filePath, CancellationToken ct)
    {
        // 1. Try embedded tag artwork first
        if (metadata.ArtworkData != null && metadata.ArtworkData.Length > 0)
        {
            var result = await StoreArtworkBytesAsync(
                metadata.ArtworkData, metadata.ArtworkMimeType, ArtworkSourceType.EmbeddedTag, ct);
            if (result != null) return result;
        }

        // 2. Fall back to folder image scanning
        var folderResult = await FindFolderArtworkAsync(filePath, ct);
        if (folderResult != null) return folderResult;

        // 3. Fall back to online services (MusicBrainz Cover Art Archive)
        return await FetchOnlineArtworkAsync(metadata.Artist ?? metadata.AlbumArtist, metadata.Album, ct);
    }

    public async Task<ArtworkResult?> FetchOnlineArtworkAsync(string? artistName, string? albumTitle, CancellationToken ct)
    {
        try
        {
            var online = await _onlineArtwork.FetchAlbumArtAsync(artistName, albumTitle, ct);
            if (online == null) return null;

            return await StoreArtworkBytesAsync(online.Value.Data, online.Value.MimeType, ArtworkSourceType.Manual, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Online artwork fetch failed for '{Artist} - {Album}'", artistName, albumTitle);
            return null;
        }
    }

    public async Task<ArtworkResult?> FindFolderArtworkAsync(string audioFilePath, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(audioFilePath);
            if (dir == null || !Directory.Exists(dir)) return null;

            // Search for common cover image patterns
            foreach (var coverName in CoverFileNames)
            {
                foreach (var ext in ImageExtensions)
                {
                    var candidate = Path.Combine(dir, $"{coverName}{ext}");
                    if (File.Exists(candidate))
                    {
                        var bytes = await File.ReadAllBytesAsync(candidate, ct);
                        var mime = ext switch
                        {
                            ".png" => "image/png",
                            ".gif" => "image/gif",
                            ".webp" => "image/webp",
                            ".bmp" => "image/bmp",
                            _ => "image/jpeg"
                        };
                        return await StoreArtworkBytesAsync(bytes, mime, ArtworkSourceType.FolderImage, ct);
                    }
                }
            }

            // Also check case-insensitive if filesystem supports it
            var files = Directory.GetFiles(dir);
            foreach (var file in files)
            {
                var nameNoExt = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                var ext = Path.GetExtension(file).ToLowerInvariant();

                if (CoverFileNames.Contains(nameNoExt) && ImageExtensions.Contains(ext))
                {
                    var bytes = await File.ReadAllBytesAsync(file, ct);
                    var mime = ext switch
                    {
                        ".png" => "image/png",
                        ".gif" => "image/gif",
                        ".webp" => "image/webp",
                        ".bmp" => "image/bmp",
                        _ => "image/jpeg"
                    };
                    return await StoreArtworkBytesAsync(bytes, mime, ArtworkSourceType.FolderImage, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning folder artwork near {FilePath}", audioFilePath);
        }

        return null;
    }

    private async Task<ArtworkResult?> StoreArtworkBytesAsync(byte[] data, string? mimeType, ArtworkSourceType sourceType, CancellationToken ct)
    {
        try
        {
            // Hash-based dedup
            var hash = Convert.ToHexStringLower(SHA256.HashData(data));
            var existing = await _db.ArtworkAssets.FirstOrDefaultAsync(a => a.Hash == hash, ct);
            if (existing != null)
                return new ArtworkResult(existing.Id, existing.StoragePath);

            // Determine extension from mime type
            var ext = mimeType?.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/bmp" => ".bmp",
                _ => ".jpg"
            };

            // Store under hash-based directory structure
            var subDir = hash[..2];
            var dir = Path.Combine(ArtworkRoot, subDir);
            Directory.CreateDirectory(dir);

            var storagePath = Path.Combine(subDir, $"{hash}{ext}");
            var fullPath = Path.Combine(ArtworkRoot, storagePath);

            await File.WriteAllBytesAsync(fullPath, data, ct);

            var asset = new ArtworkAsset
            {
                StoragePath = storagePath,
                MimeType = mimeType ?? "image/jpeg",
                FileSizeBytes = data.Length,
                Hash = hash,
                SourceType = sourceType
            };

            _db.ArtworkAssets.Add(asset);
            await _db.SaveChangesAsync(ct);

            return new ArtworkResult(asset.Id, storagePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store artwork");
            return null;
        }
    }

    public async Task<FileStreamResultModel?> OpenArtworkAsync(Guid artworkId, CancellationToken ct)
    {
        var asset = await _db.ArtworkAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == artworkId, ct);
        if (asset == null) return null;

        var fullPath = Path.Combine(ArtworkRoot, asset.StoragePath);
        if (!File.Exists(fullPath)) return null;

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new FileStreamResultModel(stream, asset.MimeType, Path.GetFileName(fullPath), asset.FileSizeBytes);
    }
}

