using System.Net;
using System.Text.Json;

namespace SonaFly.Services;

/// <summary>
/// Turns a failed request into a sentence a person can act on.
/// </summary>
public static class ApiErrorMessage
{
    /// <summary>
    /// <paramref name="whatFailed"/> says what the app was doing, as a sentence
    /// ("Couldn't load artists."); the reason is appended.
    /// </summary>
    public static string Describe(string whatFailed, Exception ex) => $"{whatFailed} {Reason(ex)}";

    public static string Reason(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } =>
            "Your session has ended. Sign in again from Settings.",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } =>
            "This account isn't allowed to do that.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } =>
            "It is no longer on the server.",
        HttpRequestException { StatusCode: { } code } when (int)code >= 500 =>
            $"The server had a problem ({(int)code}). Try again in a moment.",
        HttpRequestException { StatusCode: { } code } =>
            $"The server refused the request ({(int)code}).",
        HttpRequestException =>
            "Can't reach the server. Check your connection and the server address.",
        OperationCanceledException =>
            "The server took too long to answer.",
        JsonException =>
            "The server sent a reply the app couldn't read.",
        // Raised by the API client with a message written for the user: a redirect
        // (wrong address or scheme) or no server selected.
        InvalidOperationException => ex.Message,
        _ => "Something unexpected went wrong.",
    };
}
