namespace SonaFly.Services;

public enum StreamQualityMode
{
    /// <summary>The files as they are stored on the server.</summary>
    Original = 0,

    /// <summary>Compressed when the phone is on mobile data, original on Wi-Fi.</summary>
    CompressedOnMobileData = 1,

    /// <summary>Always the compressed stream.</summary>
    AlwaysCompressed = 2,
}

/// <summary>
/// The listener's choice between the files as stored and the smaller MP3 streams the
/// server can make from lossless files. A FLAC album over mobile data can use several
/// hundred megabytes; the compressed stream uses about a tenth of that.
/// </summary>
public static class StreamQuality
{
    private const string Key = "stream_quality";

    public static StreamQualityMode Mode
    {
        get => Enum.TryParse<StreamQualityMode>(Preferences.Get(Key, nameof(StreamQualityMode.Original)), out var mode)
            ? mode
            : StreamQualityMode.Original;
        set => Preferences.Set(Key, value.ToString());
    }

    /// <summary>Whether the next track should be requested compressed, as things stand right now.</summary>
    public static bool ShouldCompress() => Mode switch
    {
        StreamQualityMode.AlwaysCompressed => true,
        StreamQualityMode.CompressedOnMobileData => IsOnMobileData(),
        _ => false,
    };

    private static bool IsOnMobileData()
    {
        try
        {
            var profiles = Connectivity.Current.ConnectionProfiles.ToList();
            return profiles.Contains(ConnectionProfile.Cellular) &&
                   profiles.Contains(ConnectionProfile.WiFi) == false &&
                   profiles.Contains(ConnectionProfile.Ethernet) == false;
        }
        catch (Exception)
        {
            // The platform would not say. Treat it as Wi-Fi: full quality is the safer
            // surprise than degraded sound at home.
            return false;
        }
    }
}
