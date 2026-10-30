using System.Text.RegularExpressions;

namespace Domain.Services;


public static class WhatsAppPhoneNormalizer
{
    private static readonly Regex NonDigitsRegex = new(@"[^\d]", RegexOptions.Compiled);
    private static readonly Regex HasLettersRegex = new(@"[a-zA-Z]", RegexOptions.Compiled);

    /// <summary>
    /// Normaliza y valida un numero de WhatsApp.
    /// Solo digitos, con codigo de pais, sin +, espacios ni guiones.
    /// Longitud valida E.164: entre 8 y 15 dígitos.
    /// </summary>
    public static (bool IsValid, string? NormalizedNumber, string? ErrorMessage) ValidateAndNormalize(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            return (false, null, "El número de teléfono de WhatsApp es obligatorio.");
        }

        // Si contiene letras, es invalido directamente
        if (HasLettersRegex.IsMatch(rawPhone))
        {
            return (false, null, "El número de teléfono no puede contener letras ni caracteres inválidos.");
        }

        // Extraer unicamente los dígitos
        var digitsOnly = NonDigitsRegex.Replace(rawPhone, string.Empty);

        // Validar longitud internacional estandar E.164 (entre 8 y 15 digitos con codigo de pais)
        if (digitsOnly.Length < 8 || digitsOnly.Length > 15)
        {
            return (false, null, "El número de teléfono debe incluir el código de país y contener entre 8 y 15 dígitos.");
        }

        return (true, digitsOnly, null);
    }

    /// <summary>
    /// Genera el enlace directo a WhatsApp (https://wa.me/<numero>).
    /// Si incluye texto inicial, se codifica correctamente.
    /// </summary>
    public static string? BuildWhatsAppLink(string? rawPhone, string? initialText = null)
    {
        var (isValid, normalized, _) = ValidateAndNormalize(rawPhone);
        if (!isValid || string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(initialText))
        {
            return $"https://wa.me/{normalized}";
        }

        return $"https://wa.me/{normalized}?text={Uri.EscapeDataString(initialText)}";
    }
}
