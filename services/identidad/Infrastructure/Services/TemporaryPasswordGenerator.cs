using System.Security.Cryptography;
using Application.Interfaces.Services;

namespace Infrastructure.Services;

/// <summary>
/// Generador criptográficamente seguro de contraseñas temporales que utiliza RandomNumberGenerator (CSPRNG).
/// Cumple con las políticas de seguridad: longitud mínima de 8 caracteres (por defecto 12),
/// al menos una mayúscula, una minúscula, un dígito y un carácter especial.
/// </summary>
public class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Special = "!@#$%^&*()_+-=";
    private static readonly string All = Uppercase + Lowercase + Digits + Special;

    public string Generate(int length = 12)
    {
        if (length < 8)
        {
            length = 8;
        }

        var chars = new char[length];

        // 1. Garantizar al menos un carácter de cada clase requerida
        chars[0] = Uppercase[RandomNumberGenerator.GetInt32(Uppercase.Length)];
        chars[1] = Lowercase[RandomNumberGenerator.GetInt32(Lowercase.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[3] = Special[RandomNumberGenerator.GetInt32(Special.Length)];

        // 2. Rellenar el resto de caracteres con el conjunto completo
        for (int i = 4; i < length; i++)
        {
            chars[i] = All[RandomNumberGenerator.GetInt32(All.Length)];
        }

        // 3. Mezclado criptográfico Fisher-Yates para asegurar entropía y posiciones impredecibles
        for (int i = length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
