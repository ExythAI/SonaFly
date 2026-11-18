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

            // Lists and grids get a thumbnail. A page that shows the cover large passes
            // the size it wants as the converter parameter.
            var size = parameter is string text && int.TryParse(text, out var asked) ? asked : 320;
            return $"{baseUrl}/api/artwork/{artworkId}?size={size}";
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
