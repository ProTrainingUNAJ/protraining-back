namespace Application.Common.Ai;

using System.Text.RegularExpressions;

/// <summary>
/// Limpia el texto libre del preparador físico antes de enviarlo a un proveedor de IA externo (KAN-600).
/// Quita emails y teléfonos. No intenta detectar nombres propios: hacerlo con expresiones regulares borraría
/// ejercicios como "Press Arnold", "Curl Nórdico" o "Plancha Copenhagen".
/// </summary>
public static partial class PromptSanitizer
{
    public const string Placeholder = "[dato omitido]";

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<![\w/])\+?\(?\d[\d\s().\-]{6,}\d(?![\w/])")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$|^\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4}$")]
    private static partial Regex DateRegex();

    public static string RedactContactData(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var withoutEmails = EmailRegex().Replace(text, Placeholder);

        return PhoneRegex().Replace(withoutEmails, match =>
        {
            var value = match.Value.Trim();
            if (DateRegex().IsMatch(value))
            {
                return match.Value;
            }

            var digits = value.Count(char.IsDigit);
            return digits is >= 8 and <= 15 ? Placeholder : match.Value;
        });
    }
}
