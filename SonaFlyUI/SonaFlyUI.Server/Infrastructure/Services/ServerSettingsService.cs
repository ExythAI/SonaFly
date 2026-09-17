using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>
/// Encrypted server settings backed by the database and the Data Protection
/// key ring. The protector purpose is fixed so values survive restarts and
/// work across replicas sharing the ring; if the ring itself is lost, stored
/// secrets become unreadable and are treated as unset (logged, never thrown
/// to callers), which the status API surfaces as a configuration blocker.
/// </summary>
public sealed class ServerSettingsService : IServerSettingsService
{
    public const int MaxSecretLength = 512;

    private const string ProtectorPurpose = "SonaFly.ServerSettings.v1";

    private readonly SonaFlyDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IdentificationOptions _options;
    private readonly ILogger<ServerSettingsService> _logger;

    public ServerSettingsService(
        SonaFlyDbContext db,
        IDataProtectionProvider protection,
        IOptions<IdentificationOptions> options,
        ILogger<ServerSettingsService> logger)
    {
        _db = db;
        _protector = protection.CreateProtector(ProtectorPurpose);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct)
    {
        var row = await _db.ServerSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row == null || string.IsNullOrEmpty(row.EncryptedValue))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(row.EncryptedValue);
        }
        catch (Exception ex)
        {
            // The key ring was lost or replaced: the ciphertext is useless.
            // Report unset rather than failing callers; the status surface
            // tells the admin to save the secret again.
            _logger.LogError(ex, "Stored server setting {SettingKey} cannot be decrypted. It will be treated as unset.", key);
            return null;
        }
    }

    public async Task SetSecretAsync(string key, string? value, Guid? userId, CancellationToken ct)
    {
        if (value != null && value.Length > MaxSecretLength)
        {
            throw new ArgumentException($"Value must be at most {MaxSecretLength} characters.", nameof(value));
        }

        var row = await _db.ServerSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (string.IsNullOrWhiteSpace(value))
        {
            // Clearing falls back to the configuration-file value.
            if (row != null)
            {
                _db.ServerSettings.Remove(row);
                await _db.SaveChangesAsync(ct);
            }

            return;
        }

        var protectedValue = _protector.Protect(value);
        if (row == null)
        {
            _db.ServerSettings.Add(new ServerSetting
            {
                Key = key,
                EncryptedValue = protectedValue,
                UpdatedByUserId = userId,
                UpdatedUtc = DateTime.UtcNow
            });
        }
        else
        {
            row.EncryptedValue = protectedValue;
            row.UpdatedByUserId = userId;
            row.UpdatedUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetEffectiveAcoustIdKeyAsync(CancellationToken ct)
    {
        var stored = await GetSecretAsync(ServerSettingKeys.AcoustIdClientKey, ct);
        if (string.IsNullOrWhiteSpace(stored) == false)
        {
            return stored;
        }

        return string.IsNullOrWhiteSpace(_options.AcoustIdClientKey) ? null : _options.AcoustIdClientKey;
    }

    public async Task<AcoustIdKeySource> GetAcoustIdKeySourceAsync(CancellationToken ct)
    {
        var stored = await GetSecretAsync(ServerSettingKeys.AcoustIdClientKey, ct);
        if (string.IsNullOrWhiteSpace(stored) == false)
        {
            return AcoustIdKeySource.Database;
        }

        return string.IsNullOrWhiteSpace(_options.AcoustIdClientKey)
            ? AcoustIdKeySource.None
            : AcoustIdKeySource.Configuration;
    }
}

