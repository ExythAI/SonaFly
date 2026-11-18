using SkiaSharp;

namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>
/// Smaller copies of cover images, for grids and lists.
/// <para>
/// Covers are stored as they were found, often 1500 pixels square and a megabyte each. A
/// screen of forty album tiles would otherwise download forty of those to draw each at the
/// size of a postage stamp. Thumbnails are made the first time a size is asked for and
/// kept beside the originals.
/// </para>
/// </summary>
public static class ArtworkThumbnails
{
    /// <summary>The sizes that exist, in pixels along the longer edge.</summary>
    public static readonly int[] Sizes = [160, 320, 640];

    public const string Folder = "thumbs";
    public const string MimeType = "image/jpeg";

    /// <summary>
    /// The thumbnail size that serves a request for <paramref name="requested"/> pixels:
    /// the smallest one at least that large. Null means "use the original", for no size, a
    /// nonsensical one, or one larger than any thumbnail. Snapping to a fixed set keeps a
    /// client from filling the disk with one file per pixel count.
    /// </summary>
    public static int? Snap(int? requested)
    {
        if (requested is not { } size || size <= 0) return null;
        foreach (var candidate in Sizes)
        {
            if (candidate >= size) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Where the thumbnail of an original lives, relative to the artwork root. Originals
    /// are stored as <c>ab/abcdef….jpg</c>, named by content hash, so the thumbnail name
    /// follows from the original's and needs no database row.
    /// </summary>
    public static string RelativePath(string originalStoragePath, int size) =>
        Path.Combine(Folder, size.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Path.GetFileNameWithoutExtension(originalStoragePath) + ".jpg");

    /// <summary>Every thumbnail an original may have, relative to the artwork root.</summary>
    public static IEnumerable<string> AllRelativePaths(string originalStoragePath) =>
        Sizes.Select(size => RelativePath(originalStoragePath, size));

    /// <summary>
    /// Scales an image to fit within <paramref name="size"/> pixels square, keeping its
    /// shape, and encodes it as JPEG. Returns null when the image cannot be decoded or is
    /// already that small, in which case the original should be served as it is.
    /// </summary>
    public static byte[]? Create(byte[] original, int size)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(original);
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0) return null;

            var longest = Math.Max(bitmap.Width, bitmap.Height);
            if (longest <= size) return null;

            var scale = (double)size / longest;
            var info = new SKImageInfo(
                Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
                Math.Max(1, (int)Math.Round(bitmap.Height * scale)));

            using var resized = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            if (resized == null) return null;

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 82);
            return data?.ToArray();
        }
        catch (Exception)
        {
            // A cover the decoder cannot read is still a cover the browser may manage.
            return null;
        }
    }
}
