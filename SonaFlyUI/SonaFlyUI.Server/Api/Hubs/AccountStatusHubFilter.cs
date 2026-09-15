using Microsoft.AspNetCore.SignalR;
using SonaFlyUI.Server.Infrastructure.Identity;

namespace SonaFlyUI.Server.Api.Hubs;

/// <summary>
/// A SignalR connection authenticates once, at the handshake, and then survives for as
/// long as the socket is open. Without this, disabling or deleting a user would leave
/// their open connection able to keep issuing hub commands until they reconnected.
/// Every invocation re-checks the account and closes the connection when it fails.
/// </summary>
public sealed class AccountStatusHubFilter : IHubFilter
{
    private readonly ILogger<AccountStatusHubFilter> _logger;

    public AccountStatusHubFilter(ILogger<AccountStatusHubFilter> logger) => _logger = logger;

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await EnsureAccountIsValidAsync(
            invocationContext.ServiceProvider,
            invocationContext.Context,
            invocationContext.HubMethodName);

        return await next(invocationContext);
    }

    public async Task OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        await EnsureAccountIsValidAsync(context.ServiceProvider, context.Context, "OnConnected");
        await next(context);
    }

    private async Task EnsureAccountIsValidAsync(
        IServiceProvider services, HubCallerContext context, string operation)
    {
        var principal = context.User;
        if (principal is null)
        {
            context.Abort();
            throw new HubException("Not authenticated.");
        }

        var security = services.GetRequiredService<IUserSecurityService>();
        var user = await security.ResolveValidUserAsync(principal, context.ConnectionAborted);
        if (user is not null)
        {
            return;
        }

        _logger.LogInformation(
            "Closing hub connection {ConnectionId}: the account is no longer valid for its token ({Operation}).",
            context.ConnectionId, operation);

        // Drop the socket as well as failing the call, so the client is forced to
        // re-authenticate rather than retrying against a dead session.
        context.Abort();
        throw new HubException("Your session is no longer valid. Please sign in again.");
    }
}
