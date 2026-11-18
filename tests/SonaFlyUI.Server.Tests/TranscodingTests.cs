using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Compressed streams: an MP3 copy of a lossless file, made once, kept in a bounded
/// cache, and never allowed to stand between a listener and the original.
/// </summary>
public sealed class TranscodingTests : IDisposable
{
    private readonly string _scratch = Directory.CreateTempSubdirectory("sonafly-transcode-tests").FullName;
    private string CacheRoot => Path.Combine(_scratch, "cache");

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FakeEncoder(bool available = true, bool succeeds = true, int outputBytes = 1000) : IAudioEncoder
    {
        public int Encodes { get; private set; }
        public TaskCompletionSource? Gate { get; init; }

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(available);

        public async Task<bool> EncodeAsync(string source, string target, int bitrateKbps, CancellationToken ct)
        {
            Encodes++;
            if (Gate != null) await Gate.Task;
            if (succeeds) await File.WriteAllBytesAsync(target, new byte[outputBytes], ct);
            return succeeds;
        }
    }

    private TranscodeCache Cache(IAudioEncoder encoder, Action<TranscodingOptions>? configure = null)
    {
        var options = new TranscodingOptions { CacheRoot = CacheRoot };
        configure?.Invoke(options);
        return new TranscodeCache(encoder, Options.Create(options), NullLogger<TranscodeCache>.Instance);
    }

    private string Source(string name, int bytes = 5000)
    {
        var path = Path.Combine(_scratch, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Theory]
    [InlineData("song.flac", true)]
    [InlineData("song.WAV", true)]
    [InlineData("song.ape", true)]
    [InlineData("song.wma", true)]
    [InlineData("song.mp3", false)]
    [InlineData("song.M4A", false)]
    [InlineData("song.opus", false)]
    public void OnlyFilesThatAreLargeOrAwkwardAreWorthCompressing(string file, bool expected)
    {
        Assert.Equal(expected, TranscodeCache.WouldBenefit(file));
    }

    [Fact]
    public async Task ACopyIsMadeOnceAndReusedAfterThat()
    {
        var encoder = new FakeEncoder();
        var cache = Cache(encoder);
        var source = Source("album.flac");

        var first = await cache.GetOrCreateAsync(source, CancellationToken.None);
        var second = await cache.GetOrCreateAsync(source, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(1, encoder.Encodes);
        Assert.StartsWith(CacheRoot, first);
        Assert.Empty(Directory.GetFiles(CacheRoot, "*.partial"));
    }

    [Fact]
    public async Task TwoRequestsForTheSameNewTrackEncodeItOnce()
    {
        var gate = new TaskCompletionSource();
        var encoder = new FakeEncoder { Gate = gate };
        var cache = Cache(encoder);
        var source = Source("album.flac");

        // A player commonly opens the URL twice: once to probe it, once to play it.
        var probe = cache.GetOrCreateAsync(source, CancellationToken.None);
        var play = cache.GetOrCreateAsync(source, CancellationToken.None);
        gate.SetResult();

        Assert.Equal(await probe, await play);
        Assert.Equal(1, encoder.Encodes);
    }

    [Fact]
    public async Task ARetaggedFileGetsAFreshCopy()
    {
        var encoder = new FakeEncoder();
        var cache = Cache(encoder);
        var source = Source("album.flac");
        var before = await cache.GetOrCreateAsync(source, CancellationToken.None);

        File.WriteAllBytes(source, new byte[6000]);
        var after = await cache.GetOrCreateAsync(source, CancellationToken.None);

        Assert.NotEqual(before, after);
        Assert.Equal(2, encoder.Encodes);
    }

    [Fact]
    public async Task TheOriginalIsServedWheneverACopyCannotBeHad()
    {
        var source = Source("album.flac");

        // No ffmpeg on the machine.
        Assert.Null(await Cache(new FakeEncoder(available: false)).GetOrCreateAsync(source, CancellationToken.None));
        // Switched off by the administrator.
        Assert.Null(await Cache(new FakeEncoder(), o => o.Enabled = false).GetOrCreateAsync(source, CancellationToken.None));
        // Already a compact format.
        Assert.Null(await Cache(new FakeEncoder()).GetOrCreateAsync(Source("song.mp3"), CancellationToken.None));
        // The encoder gave up, or produced nothing.
        Assert.Null(await Cache(new FakeEncoder(succeeds: false)).GetOrCreateAsync(source, CancellationToken.None));
        Assert.Null(await Cache(new FakeEncoder(outputBytes: 0)).GetOrCreateAsync(source, CancellationToken.None));
        // The file went away.
        Assert.Null(await Cache(new FakeEncoder()).GetOrCreateAsync(Path.Combine(_scratch, "gone.flac"), CancellationToken.None));

        // And none of those left anything behind.
        Assert.True(Directory.Exists(CacheRoot) == false || Directory.GetFiles(CacheRoot).Length == 0);
    }

    [Fact]
    public async Task TheCacheDropsTheLeastRecentlyPlayedCopiesOnceItIsFull()
    {
        Directory.CreateDirectory(CacheRoot);
        var megabyte = new byte[1024 * 1024];
        string Copy(string name, int hoursAgo)
        {
            var path = Path.Combine(CacheRoot, name);
            File.WriteAllBytes(path, megabyte);
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow.AddHours(-hoursAgo));
            return path;
        }

        var stale = Copy("stale.mp3", 30);
        var older = Copy("older.mp3", 20);
        var recent = Copy("recent.mp3", 1);
        var encoding = Path.Combine(CacheRoot, "in-progress.partial");
        File.WriteAllBytes(encoding, megabyte);

        await Cache(new FakeEncoder(), o => o.MaxCacheMegabytes = 2).TrimAsync(CacheRoot);

        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(older));
        Assert.False(File.Exists(stale));
        // An encode still running is not a cache entry and is left alone.
        Assert.True(File.Exists(encoding));
    }

    [Fact]
    public async Task RealFfmpegTurnsAWavIntoAnMp3()
    {
        var encoder = new FfmpegAudioEncoder(Options.Create(new TranscodingOptions()), NullLogger<FfmpegAudioEncoder>.Instance);
        if (await encoder.IsAvailableAsync(CancellationToken.None) == false)
        {
            // Not installed on this machine; the cache behaviour is covered above.
            return;
        }

        // One second of silence: 44.1 kHz, 16-bit, mono. An awkward file name on purpose.
        var source = Path.Combine(_scratch, "it's a test & more (live).wav");
        const int sampleRate = 44100;
        var dataBytes = sampleRate * 2;
        await using (var file = File.Create(source))
        await using (var writer = new BinaryWriter(file))
        {
            writer.Write("RIFF"u8); writer.Write(36 + dataBytes); writer.Write("WAVE"u8);
            writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(dataBytes); writer.Write(new byte[dataBytes]);
        }

        var cache = new TranscodeCache(encoder, Options.Create(new TranscodingOptions { CacheRoot = CacheRoot }),
            NullLogger<TranscodeCache>.Instance);
        var copy = await cache.GetOrCreateAsync(source, CancellationToken.None);

        Assert.NotNull(copy);
        var bytes = await File.ReadAllBytesAsync(copy);
        Assert.True(bytes.Length > 1000);
        Assert.True(bytes.Length < dataBytes, "the compressed copy should be smaller than the WAV");
        // An MP3 starts with an ID3 tag or a frame sync.
        Assert.True((bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3') || (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0));
    }
}
