namespace Application.Common.Helpers;

using System.Globalization;
using System.Text;

/// <summary>
/// Normalizador centralizado de texto para búsquedas, comparaciones y categorización semántica (#158 · KAN-563).
/// Convierte caracteres con diacríticos (á, é, í, ó, ú, ü, ñ -> n) a su forma básica en minúsculas y sin acentos.
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// Normaliza una cadena de texto eliminando tildes/marcas diacríticas, pasando a minúsculas y recortando espacios.
    /// Si el texto es nulo o vacío, retorna <see cref="string.Empty"/>.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Trim()
            .ToLowerInvariant();
    }
}

