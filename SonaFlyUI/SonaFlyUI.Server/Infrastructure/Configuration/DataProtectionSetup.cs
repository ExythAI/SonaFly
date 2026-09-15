using Microsoft.AspNetCore.DataProtection;

namespace SonaFlyUI.Server.Infrastructure.Configuration;

/// <summary>
/// Configures the Data Protection key ring that signs stream tickets.
///
/// Without an explicit key location, ASP.NET keeps keys under the user profile — which
/// in a container is thrown away with the container. Every outstanding stream URL then
/// stops validating the moment the container is replaced, mid-playback, and two replicas
/// can never validate each other's tickets because each invents its own key ring.
/// </summary>
public static class DataProtectionSetup
{
    /// <summary>
    /// Configuration key for the directory holding the key ring. Should point at
    /// persistent storage — in Docker, a named volume.
    /// </summary>
    public const string KeyRingPathKey = "SonaFly:DataProtectionKeyRoot";

    /// <summary>
    /// Fixes the purpose-isolation boundary. It must stay the same across restarts and
    /// replicas, or a persisted key ring still would not validate yesterday's tickets.
    /// It also keeps a ticket minted by some other application useless here, even in the
    /// unlikely event that it shares a key ring.
    /// </summary>
    public const string ApplicationName = "SonaFly";

    private const string DefaultKeyRingPath = "./data/keys";

    public static string ResolveKeyRingPath(IConfiguration configuration)
    {
        var configured = configuration[KeyRingPathKey];
        return string.IsNullOrWhiteSpace(configured) ? DefaultKeyRingPath : configured;
    }

    /// <summary>
    /// Registers Data Protection with a persistent, stably-named key ring.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The key directory cannot be created or written to. Failing here is deliberate:
    /// Data Protection would otherwise fall back to an in-memory key ring with only a
    /// log warning, and the problem would surface much later as tickets that stop
    /// working after every restart.
    /// </exception>
    public static IDataProtectionBuilder AddSonaFlyDataProtection(
        this IServiceCollection services, string keyRingPath)
    {
        var directory = EnsureWritableDirectory(keyRingPath);

        return services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(directory);
    }

    private static DirectoryInfo EnsureWritableDirectory(string keyRingPath)
    {
        DirectoryInfo directory;
        try
        {
            directory = Directory.CreateDirectory(keyRingPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Cannot create the Data Protection key directory '{keyRingPath}'. " +
                $"Set {KeyRingPathKey} to a writable, persistent location. In Docker this " +
                "should be a named volume mounted at /app/data/keys.", ex);
        }

        // Creating the directory can succeed on a read-only mount; writing is the real test.
        var probe = Path.Combine(directory.FullName, $".write-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The Data Protection key directory '{directory.FullName}' is not writable. " +
                "Stream tickets would stop working after every restart. Check the volume " +
                "mount and its ownership — the container runs as a non-root user.", ex);
        }
        finally
        {
            try { File.Delete(probe); } catch (IOException) { /* best effort */ }
        }

        return directory;
    }
}
