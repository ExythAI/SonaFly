using System.Net;
using System.Text.Json;
using SonaFly.Services;
using Xunit;
using Page = SonaFly.Models.PaginatedResult<int>;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// The mobile app's scrolling lists: loading a page at a time, and what a failed
/// request is reported as.
/// </summary>
public sealed class PagedLoaderTests
{
    /// <summary>A server holding the numbers 1..total, which records the pages asked for.</summary>
    private sealed class Source(int total)
    {
        public List<int> Requested { get; } = [];

        public Task<Page?> Fetch(int page, int pageSize)
        {
            Requested.Add(page);
            var items = Enumerable.Range(1, total).Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<Page?>(new Page(items, total, page, pageSize, 0));
        }
    }

    [Fact]
    public async Task TheWholeListArrivesAPageAtATime()
    {
        var source = new Source(total: 25);
        var loader = new PagedLoader<int>(pageSize: 10);

        Assert.True(await loader.ReloadAsync(source.Fetch));
        Assert.Equal(10, loader.Items.Count);
        Assert.Equal(25, loader.TotalCount);
        Assert.True(loader.HasMore);

        await loader.LoadMoreAsync();
        Assert.Equal(20, loader.Items.Count);
        Assert.True(loader.HasMore);

        await loader.LoadMoreAsync();
        Assert.Equal(Enumerable.Range(1, 25), loader.Items);
        Assert.False(loader.HasMore);

        // Scrolling at the end asks for nothing further.
        await loader.LoadMoreAsync();
        Assert.Equal([1, 2, 3], source.Requested);
    }

    [Fact]
    public async Task AListThatFitsOnOnePageHasNothingMore()
    {
        var loader = new PagedLoader<int>(pageSize: 10);

        await loader.ReloadAsync(new Source(total: 4).Fetch);

        Assert.Equal(4, loader.Items.Count);
        Assert.False(loader.HasMore);
    }

    [Fact]
    public async Task NothingIsRequestedBeforeTheFirstLoad()
    {
        var loader = new PagedLoader<int>();

        await loader.LoadMoreAsync();

        Assert.Empty(loader.Items);
    }

    [Fact]
    public async Task ScrollingWhileAPageIsOnItsWayDoesNotRequestItTwice()
    {
        var source = new Source(total: 30);
        var gate = new TaskCompletionSource();
        var loader = new PagedLoader<int>(pageSize: 10);
        await loader.ReloadAsync(source.Fetch);

        var slowCalls = 0;
        await loader.ReloadAsync(async (page, size) =>
        {
            if (page > 1)
            {
                slowCalls++;
                await gate.Task;
            }
            return await source.Fetch(page, size);
        });

        var first = loader.LoadMoreAsync();
        var second = loader.LoadMoreAsync();
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, slowCalls);
        Assert.Equal(Enumerable.Range(1, 20), loader.Items);
    }

    [Fact]
    public async Task ASlowReplyForAnEarlierSourceIsDropped()
    {
        var gate = new TaskCompletionSource();
        var loader = new PagedLoader<int>(pageSize: 10);

        // "All albums" is slow; the user picks an artist before it answers.
        var allAlbums = loader.ReloadAsync(async (page, size) =>
        {
            await gate.Task;
            return new Page([901, 902, 903], 3, page, size, 1);
        });
        var oneArtist = loader.ReloadAsync(new Source(total: 2).Fetch);

        Assert.True(await oneArtist);
        gate.SetResult();
        Assert.False(await allAlbums);

        Assert.Equal([1, 2], loader.Items);
    }

    [Fact]
    public async Task ALatePageForAnEarlierSourceIsNotAppendedToTheNewList()
    {
        var gate = new TaskCompletionSource();
        var loader = new PagedLoader<int>(pageSize: 2);

        await loader.ReloadAsync(async (page, size) =>
        {
            if (page > 1) await gate.Task;
            return new Page([900 + page, 950 + page], 10, page, size, 5);
        });
        var stalePage = loader.LoadMoreAsync();

        await loader.ReloadAsync(new Source(total: 6).Fetch);
        gate.SetResult();
        await stalePage;

        Assert.Equal([1, 2], loader.Items);

        // The new list still pages normally afterwards.
        await loader.LoadMoreAsync();
        Assert.Equal([1, 2, 3, 4], loader.Items);
    }

    [Fact]
    public async Task AFailedPageKeepsWhatIsShownAndCanBeRetried()
    {
        var source = new Source(total: 15);
        var fail = false;
        var loader = new PagedLoader<int>(pageSize: 10);
        await loader.ReloadAsync((page, size) =>
            fail ? throw new HttpRequestException("offline") : source.Fetch(page, size));

        fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(loader.LoadMoreAsync);
        Assert.Equal(10, loader.Items.Count);
        Assert.True(loader.HasMore);

        fail = false;
        await loader.LoadMoreAsync();
        Assert.Equal(Enumerable.Range(1, 15), loader.Items);
    }

    [Fact]
    public async Task AFailedReloadLeavesTheOldListWithNothingToContinue()
    {
        var loader = new PagedLoader<int>(pageSize: 10);
        await loader.ReloadAsync(new Source(total: 30).Fetch);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            loader.ReloadAsync((_, _) => throw new HttpRequestException("offline")));

        // Paging on would mix the failed source's pages into the list still on screen.
        Assert.Equal(10, loader.Items.Count);
        Assert.False(loader.HasMore);
    }

    [Fact]
    public async Task AnEmptyPageEndsTheListEvenWhenTheTotalClaimsMore()
    {
        var loader = new PagedLoader<int>(pageSize: 10);

        await loader.ReloadAsync((page, size) => Task.FromResult<Page?>(
            page == 1 ? new Page([1, 2, 3], 50, page, size, 5) : new Page([], 50, page, size, 5)));
        Assert.True(loader.HasMore);

        await loader.LoadMoreAsync();

        Assert.False(loader.HasMore);
        Assert.Equal(3, loader.Items.Count);
    }

    [Fact]
    public async Task AnEmptyReplyIsAnEmptyList()
    {
        var loader = new PagedLoader<int>();

        Assert.True(await loader.ReloadAsync((_, _) => Task.FromResult<Page?>(null)));

        Assert.Empty(loader.Items);
        Assert.False(loader.HasMore);
    }

    // ── Error wording ──

    public static TheoryData<Exception, string> Failures => new()
    {
        { new HttpRequestException("x", null, HttpStatusCode.Unauthorized), "Sign in again" },
        { new HttpRequestException("x", null, HttpStatusCode.Forbidden), "isn't allowed" },
        { new HttpRequestException("x", null, HttpStatusCode.NotFound), "no longer on the server" },
        { new HttpRequestException("x", null, HttpStatusCode.BadGateway), "(502)" },
        { new HttpRequestException("x", null, HttpStatusCode.BadRequest), "refused the request (400)" },
        { new HttpRequestException("No such host"), "Can't reach the server" },
        { new TaskCanceledException(), "took too long" },
        { new JsonException("bad"), "couldn't read" },
        { new InvalidOperationException("Use https:// in the server address."), "Use https://" },
        { new NullReferenceException("internal detail"), "Something unexpected" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void AFailureIsDescribedInPlainWords(Exception failure, string expected)
    {
        var message = ApiErrorMessage.Describe("Couldn't load artists.", failure);

        Assert.StartsWith("Couldn't load artists. ", message);
        Assert.Contains(expected, message);
        Assert.DoesNotContain("internal detail", message);
    }
}
