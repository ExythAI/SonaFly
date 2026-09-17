using System.Security.Cryptography;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// Whole-file SHA-256 for stable file identity (upgrade plan 5.3).
/// Files are opened read-only with shared read access and streamed, so large
/// libraries do not spike memory. Analysis never opens a file for write.
/// </summary>
public sealed class FileHashService : IFileHashService
{
    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Digest helper for cache keys and fingerprint digests: SHA-256 of text.
    /// </summary>
    public static string DigestText(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

