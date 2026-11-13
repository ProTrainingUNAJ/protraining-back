namespace Infrastructure.Services;

using Microsoft.AspNetCore.Identity;

/// <summary>
/// Traductor de errores de ASP.NET Core Identity al español (#KAN-539).
/// </summary>
public sealed class SpanishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = $"La contraseña debe tener al menos {length} caracteres."
    };

    public override IdentityError PasswordRequiresDigit() => new()
    {
        Code = nameof(PasswordRequiresDigit),
        Description = "La contraseña debe tener al menos un número ('0'-'9')."
    };

    public override IdentityError PasswordRequiresLower() => new()
    {
        Code = nameof(PasswordRequiresLower),
        Description = "La contraseña debe tener al menos una letra minúscula ('a'-'z')."
    };

    public override IdentityError PasswordRequiresUpper() => new()
    {
        Code = nameof(PasswordRequiresUpper),
        Description = "La contraseña debe tener al menos una letra mayúscula ('A'-'Z')."
    };

    public override IdentityError PasswordRequiresNonAlphanumeric() => new()
    {
        Code = nameof(PasswordRequiresNonAlphanumeric),
        Description = "La contraseña debe tener al menos un carácter especial."
    };

    public override IdentityError PasswordMismatch() => new()
    {
        Code = nameof(PasswordMismatch),
        Description = "La contraseña actual es incorrecta."
    };

    public override IdentityError DefaultError() => new()
    {
        Code = nameof(DefaultError),
        Description = "Ocurrió un error inesperado al validar la identidad."
    };
}

