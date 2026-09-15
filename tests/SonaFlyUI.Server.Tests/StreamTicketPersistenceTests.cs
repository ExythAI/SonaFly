using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Stream tickets are signed with the Data Protection key ring. With the default
/// (unconfigured) key storage those keys live with the container, so replacing it
/// invalidated every stream URL a client was part-way through playing, and two replicas
/// could never validate each other's tickets.
///
/// These run against real key rings on disk rather than EphemeralDataProtectionProvider,
/// because persistence across process lifetimes is the whole point.
/// </summary>
public sealed class StreamTicketPersistenceTests : IDisposable
{
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), $"sonafly-keys-{Guid.NewGuid():N}");
    private readonly string _otherKeyRing = Path.Combine(Path.GetTempPath(), $"sonafly-keys-{Guid.NewGuid():N}");
    private readonly List<ServiceProvider> _providers = [];

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _trackId = Guid.NewGuid();

    /// <summary>
    /// Builds a ticket service the way Program.cs does. Each call stands in for a
    /// separate process — a replacement container, or another replica.
    /// </summary>
    private StreamTicketService NewInstance(string? keyRing = null, string? applicationName = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (applicationName is null)
        {
            services.AddSonaFlyDataProtection(keyRing ?? _keyRing);
        }
        else
        {
            // A different deployment: same storage, different isolation boundary.
            services.AddDataProtection()
                .SetApplicationName(applicationName)
                .PersistKeysToFileSystem(Directory.CreateDirectory(keyRing ?? _keyRing));
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return new StreamTicketService(provider.GetRequiredService<IDataProtectionProvider>());
    }

    [Fact]
    public void ATicketSurvivesAnOrdinaryContainerReplacement()
    {
        var ticket = NewInstance().Create(_userId, _trackId, "stamp-1");

        // The container is replaced: new process, same mounted key volume.
        var afterRestart = NewInstance().Validate(ticket, _trackId);

        Assert.NotNull(afterRestart);
        Assert.Equal(_userId, afterRestart.UserId);
        Assert.Equal("stamp-1", afterRestart.SecurityStamp);
    }

    [Fact]
    public void ATicketFromOneReplicaValidatesOnAnotherSharingKeys()
    {
        var replicaA = NewInstance();
        var replicaB = NewInstance();

        var ticket = replicaA.Create(_userId, _trackId, "stamp-1");

        Assert.NotNull(replicaB.Validate(ticket, _trackId));
    }

    [Fact]
    public void ATicketFromADifferentDeploymentIsRejected()
    {
        // Another installation with its own key volume.
        var ticket = NewInstance(keyRing: _otherKeyRing).Create(_userId, _trackId, "stamp-1");

        Assert.Null(NewInstance().Validate(ticket, _trackId));
    }

    [Fact]
    public void ATicketFromADifferentApplicationSharingStorageIsRejected()
    {
        // Same directory, different application name: purpose isolation still applies,
        // so an unrelated app cannot mint tickets this server will honour.
        var ticket = NewInstance(applicationName: "SomeOtherApp").Create(_userId, _trackId, "stamp-1");

        Assert.Null(NewInstance().Validate(ticket, _trackId));
    }

    [Fact]
    public void ATicketRemainsScopedToItsOwnTrack()
    {
        // Persistence must not widen what a ticket grants.
        var ticket = NewInstance().Create(_userId, _trackId, "stamp-1");

        Assert.Null(NewInstance().Validate(ticket, Guid.NewGuid()));
    }

    [Fact]
    public void AKeyRingIsWrittenToTheConfiguredDirectory()
    {
        NewInstance().Create(_userId, _trackId, "stamp-1");

        // If this is empty the keys went somewhere ephemeral and the tests above would
        // only be passing because one process created and read them.
        Assert.NotEmpty(Directory.GetFiles(_keyRing, "*.xml"));
    }

    [Fact]
    public void AnUnwritableKeyDirectoryFailsStartupRatherThanFallingBackSilently()
    {
        // Data Protection would otherwise use an in-memory key ring and log a warning,
        // and the breakage would show up much later as tickets dying on every restart.
        var services = new ServiceCollection();
        services.AddLogging();

        // A path whose parent is a file cannot be created as a directory.
        var file = Path.Combine(Path.GetTempPath(), $"sonafly-not-a-dir-{Guid.NewGuid():N}");
        File.WriteAllText(file, "");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => services.AddSonaFlyDataProtection(Path.Combine(file, "keys")));

            Assert.Contains(DataProtectionSetup.KeyRingPathKey, ex.Message);
        }
        finally
        {
            File.Delete(file);
        }
    }

    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
        foreach (var dir in new[] { _keyRing, _otherKeyRing })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }
}
