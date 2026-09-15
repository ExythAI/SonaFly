using SonaFlyUI.Server.Infrastructure.Identity;

namespace SonaFlyUI.Server.Api.Middleware;

/// <summary>
/// Confines a caller whose account still carries a bootstrap or administrator-assigned
/// password to the endpoints needed to replace it. Without this, a one-time generated
/// password would work indefinitely as an ordinary credential.
/// </summary>
public sealed class PasswordChangeRequiredMiddleware
{
    /// <summary>Endpoints a caller may still reach while the flag is set.</summary>
    private static readonly string[] AllowedPaths =
    [
        "/api/auth/change-password",
        "/api/auth/logout",
        "/api/auth/me",
        "/api/auth/refresh"
    ];

    private readonly RequestDelegate _next;

    public PasswordChangeRequiredMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresPasswordChange(context) && !IsAllowed(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                detail = "You must change your password before using this account.",
                code = "password_change_required"
            });
            return;
        }

        await _next(context);
    }

    private static bool RequiresPasswordChange(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true &&
        string.Equals(
            context.User.FindFirst(TokenService.MustChangePasswordClaim)?.Value,
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowed(PathString path) =>
        AllowedPaths.Any(allowed => path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase));
}
