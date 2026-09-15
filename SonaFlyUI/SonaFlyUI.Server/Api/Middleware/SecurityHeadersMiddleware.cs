namespace SonaFlyUI.Server.Api.Middleware;

/// <summary>
/// Response headers that reduce what a hostile page — or a hostile referrer — can do
/// with this application.
///
/// Referrer-Policy matters more here than in a typical app: stream and artwork URLs
/// carry a short-lived ticket in the query string, because the browser's media element
/// cannot send an Authorization header. Without a policy those full URLs, tickets
/// included, would be sent to any third-party origin the page navigates to or loads.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Never leak a ticketed URL to another origin.
        headers["Referrer-Policy"] = "same-origin";

        // Do not let a browser re-interpret an audio or JSON response as something else.
        headers["X-Content-Type-Options"] = "nosniff";

        // No third-party framing: this app has no embedding use case.
        headers["X-Frame-Options"] = "DENY";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";

        // Browser features the application never uses.
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";

        return _next(context);
    }
}
