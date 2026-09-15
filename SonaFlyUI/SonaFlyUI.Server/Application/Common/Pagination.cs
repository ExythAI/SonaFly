namespace SonaFlyUI.Server.Application.Common;

/// <summary>
/// Shared bounds for paged and search endpoints.
/// <para>
/// Unbounded page sizes let a single request pull an entire library into memory and onto the
/// wire, and zero/negative/overflowing values produce bad offsets and nonsensical TotalPages
/// arithmetic. Every list endpoint validates through here so the limits are the same wherever
/// a client looks (backlog N20).
/// </para>
/// </summary>
public static class Pagination
{
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 50;

    public const int MaxSearchLimit = 50;
    public const int DefaultSearchLimit = 10;

    /// <summary>Longest accepted free-text query or filter.</summary>
    public const int MaxQueryLength = 200;

    /// <summary>
    /// Highest page that can be requested. Combined with <see cref="MaxPageSize"/> the largest
    /// offset is well inside <see cref="int"/>, so <c>(page - 1) * pageSize</c> cannot overflow.
    /// </summary>
    public const int MaxPage = 100_000;

    public static bool TryValidate(int page, int pageSize, out int validPage, out int validPageSize, out string? error)
    {
        validPage = page;
        validPageSize = pageSize;

        if (page < 1 || page > MaxPage)
        {
            error = $"page must be between 1 and {MaxPage}.";
            return false;
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            error = $"pageSize must be between 1 and {MaxPageSize}.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryValidateSearchLimit(int limit, out int validLimit, out string? error)
    {
        validLimit = limit;

        if (limit < 1 || limit > MaxSearchLimit)
        {
            error = $"limit must be between 1 and {MaxSearchLimit}.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryValidateQueryText(string? text, string parameterName, out string? error)
    {
        if (text != null && text.Length > MaxQueryLength)
        {
            error = $"{parameterName} must be {MaxQueryLength} characters or fewer.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Offset for a page that has already been validated by <see cref="TryValidate"/>.</summary>
    public static int Offset(int page, int pageSize) => checked((page - 1) * pageSize);
}
