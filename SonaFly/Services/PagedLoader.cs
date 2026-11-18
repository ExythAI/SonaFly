using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SonaFly.Models;

namespace SonaFly.Services;

/// <summary>
/// A list that fills one server page at a time as the user scrolls.
///
/// The same list can be pointed at a different source (all albums, then one artist's
/// albums), and a slow reply to an earlier source must never land in a later one, so
/// every reload starts a new generation and replies from older generations are dropped.
/// Failures are thrown to the caller, which decides what to tell the user; the items
/// already shown are kept.
/// </summary>
public sealed partial class PagedLoader<T> : ObservableObject
{
    public delegate Task<PaginatedResult<T>?> PageFetcher(int page, int pageSize);

    private readonly int _pageSize;
    private PageFetcher? _fetch;
    private int _generation;
    private int _nextPage = 1;
    private int? _loadingMoreGeneration;

    public PagedLoader(int pageSize = 60)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        _pageSize = pageSize;
    }

    public ObservableCollection<T> Items { get; } = [];

    /// <summary>True while the server holds items that are not in the list yet.</summary>
    [ObservableProperty] private bool _hasMore;

    /// <summary>How many items the server has in total, as of the last page received.</summary>
    [ObservableProperty] private int _totalCount;

    /// <summary>
    /// Replaces the list with the first page of <paramref name="fetch"/>. Returns false
    /// when a newer reload overtook this one and its result was discarded.
    /// </summary>
    public async Task<bool> ReloadAsync(PageFetcher fetch)
    {
        var generation = ++_generation;
        _fetch = fetch;
        // Until the first page arrives there is nothing valid to continue from.
        HasMore = false;

        var result = await fetch(1, _pageSize);
        if (generation != _generation) return false;

        Items.Clear();
        Append(result, page: 1);
        return true;
    }

    /// <summary>
    /// Appends the next page. Does nothing when everything is loaded or a page for the
    /// current source is already on its way, so it is safe to call on every scroll.
    /// </summary>
    public async Task LoadMoreAsync()
    {
        if (_fetch is null || HasMore == false) return;
        if (_loadingMoreGeneration == _generation) return;

        var generation = _generation;
        var page = _nextPage;
        _loadingMoreGeneration = generation;
        try
        {
            var result = await _fetch(page, _pageSize);
            if (generation != _generation) return;
            Append(result, page);
        }
        finally
        {
            if (_loadingMoreGeneration == generation) _loadingMoreGeneration = null;
        }
    }

    private void Append(PaginatedResult<T>? result, int page)
    {
        var items = result?.Items ?? [];
        foreach (var item in items) Items.Add(item);

        TotalCount = result?.TotalCount ?? Items.Count;
        _nextPage = page + 1;
        // An empty page ends the list whatever the total claims; otherwise a count
        // that is out of date would have the app asking for the same page for ever.
        HasMore = items.Count > 0 && Items.Count < TotalCount;
    }
}
