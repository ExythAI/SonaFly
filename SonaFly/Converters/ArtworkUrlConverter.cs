using System.Globalization;
using SonaFly.Services;

namespace SonaFly.Converters;

/// <summary>
/// Converts a Guid? artworkId into a full artwork URL using the active server's base URL.
/// Usage: {Binding ArtworkId, Converter={StaticResource ArtworkUrlConverter}}
/// </summary>
public class ArtworkUrlConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Guid artworkId || artworkId == Guid.Empty)
            return null;

        try
        {
            var storage = Application.Current?.Handler?.MauiContext?.Services.GetService<ServerStorageService>();
            var baseUrl = storage?.GetActive()?.BaseUrl?.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return null;

            return $"{baseUrl}/api/artwork/{artworkId}";
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
