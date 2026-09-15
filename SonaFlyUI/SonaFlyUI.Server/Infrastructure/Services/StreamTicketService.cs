using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace SonaFlyUI.Server.Infrastructure.Services;

// Grants access to one track only, never to other API endpoints.
public class StreamTicketService(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("SonaFly.StreamTicket.v1");

    public string Create(Guid userId, Guid trackId, string? securityStamp) =>
        _protector.Protect(JsonSerializer.Serialize(new StreamTicket(
            userId, trackId, securityStamp, DateTimeOffset.UtcNow.AddHours(2))));

    public StreamTicket? Validate(string? token, Guid trackId)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var ticket = JsonSerializer.Deserialize<StreamTicket>(_protector.Unprotect(token));
            return ticket?.TrackId == trackId && ticket.ExpiresUtc > DateTimeOffset.UtcNow ? ticket : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}

public record StreamTicket(Guid UserId, Guid TrackId, string? SecurityStamp, DateTimeOffset ExpiresUtc);
