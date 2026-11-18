using System.Globalization;

namespace SonaFly.Services;

/// <summary>
/// Position and duration arithmetic for the player UI. Media elements report odd
/// values around loads and seeks (NaN, negatives, a position past the end), so
/// everything that reaches a label or a slider goes through here first.
/// </summary>
public static class PlaybackProgress
{
    /// <summary>
    /// Smallest change worth pushing to the UI. Media elements report position several
    /// times a second; a label that shows whole seconds does not need every one.
    /// </summary>
    public const double ReportThresholdSeconds = 0.25;

    /// <summary>Formats seconds as m:ss, or h:mm:ss from one hour up.</summary>
    public static string Format(double seconds)
    {
        var whole = (long)Math.Floor(Sanitize(seconds));
        var hours = whole / 3600;
        var minutes = whole % 3600 / 60;
        var secs = whole % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{secs:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{secs:00}");
    }

    /// <summary>A usable number of seconds: never NaN, infinite, or negative.</summary>
    public static double Sanitize(double seconds) =>
        double.IsFinite(seconds) && seconds > 0 ? seconds : 0;

    /// <summary>The position kept within the track, when its length is known.</summary>
    public static double Clamp(double position, double duration)
    {
        position = Sanitize(position);
        duration = Sanitize(duration);
        return duration > 0 ? Math.Min(position, duration) : position;
    }

    /// <summary>True when a reported position differs enough from the shown one.</summary>
    public static bool IsWorthReporting(double shown, double reported) =>
        Math.Abs(Sanitize(reported) - Sanitize(shown)) >= ReportThresholdSeconds;
}
