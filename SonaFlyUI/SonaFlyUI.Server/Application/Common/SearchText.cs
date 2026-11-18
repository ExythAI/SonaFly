using System.Globalization;
using System.Text;

namespace SonaFlyUI.Server.Application.Common;

/// <summary>
/// The form in which names are stored for searching, and into which a search term is put
/// before it is compared with them.
/// <para>
/// SQLite's own <c>lower()</c> only understands ASCII, so a search for "björk" did not
/// find "BJÖRK", and nobody types the accent in "Beyoncé" or "Sigur Rós" at all. Folding
/// both sides the same way here, once when a name is saved rather than for every row of
/// every query, makes the comparison a plain substring test.
/// </para>
/// </summary>
public static class SearchText
{
    /// <summary>Lower-cased, with accents and other combining marks removed.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
