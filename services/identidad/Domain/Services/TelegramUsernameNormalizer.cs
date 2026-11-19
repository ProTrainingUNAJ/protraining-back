using System.Text.RegularExpressions;

namespace Domain.Services;

/// <summary>
/// Valida y normaliza nombres de usuario de Telegram (KAN-503).
/// Reglas: opcional (null/vacío), acepta a lo sumo una '@' inicial, se almacena sin '@',
/// longitud de 5 a 32 caracteres y solo letras, números o guión bajo.
/// </summary>
public static partial class TelegramUsernameNormalizer
{
    public const int MinLength = 5;
    public const int MaxLength = 32;

    [GeneratedRegex("^[a-zA-Z0-9_]{5,32}$")]
    private static partial Regex ValidUsernameRegex();

    public static (bool IsValid, string? NormalizedUsername, string? ErrorMessage) ValidateAndNormalize(string? rawUsername)
    {
        if (string.IsNullOrWhiteSpace(rawUsername))
        {
            return (true, null, null);
        }

        var trimmed = rawUsername.Trim();
        var withoutLeadingAt = trimmed.StartsWith('@') ? trimmed[1..] : trimmed;

        if (withoutLeadingAt.Contains('@'))
        {
            return (false, null, "El usuario de Telegram no puede contener múltiples '@'.");
        }

        if (withoutLeadingAt.Length < MinLength || withoutLeadingAt.Length > MaxLength)
        {
            return (false, null, "El usuario de Telegram debe tener entre 5 y 32 caracteres.");
        }

        if (!ValidUsernameRegex().IsMatch(withoutLeadingAt))
        {
            return (false, null, "El usuario de Telegram solo puede contener letras, números y guión bajo.");
        }

        return (true, withoutLeadingAt, null);
    }
}
