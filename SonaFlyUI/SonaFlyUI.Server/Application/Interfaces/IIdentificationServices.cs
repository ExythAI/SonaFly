namespace SonaFlyUI.Server.Application.Interfaces;

/// <summary>
/// Streams SHA-256 over source files without opening anything for write
/// (upgrade plan 5.3). Whole-file hashes distinguish exact byte copies and
/// feed move matching, duplicate grouping, and proposal preconditions.
/// </summary>
public interface IFileHashService
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken ct);
}

