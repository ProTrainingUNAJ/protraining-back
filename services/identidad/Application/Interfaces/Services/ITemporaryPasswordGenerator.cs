namespace Application.Interfaces.Services;

/// <summary>
/// Generador criptográficamente seguro de contraseñas temporales.
/// </summary>
public interface ITemporaryPasswordGenerator
{
    string Generate(int length = 12);
}
