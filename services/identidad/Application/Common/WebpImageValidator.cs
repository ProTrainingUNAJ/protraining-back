using Domain.Exceptions;

namespace Application.Common;

public static class WebpImageValidator
{
    public const int MaxSizeBytes = 200 * 1024; // 200 KB

    private static readonly byte[] RiffHeader = [(byte)'R', (byte)'I', (byte)'F', (byte)'F'];
    private static readonly byte[] WebpHeader = [(byte)'W', (byte)'E', (byte)'B', (byte)'P'];

    public static void Validate(byte[]? data)
    {
        if (data is null || data.Length == 0)
            throw new DomainException("El archivo de imagen no puede estar vacío.");

        if (data.Length > MaxSizeBytes)
            throw new DomainException("La foto no puede superar los 200 KB.");

        if (data.Length < 12)
            throw new DomainException("El archivo debe ser una imagen válida en formato WebP.");

        for (var i = 0; i < 4; i++)
        {
            if (data[i] != RiffHeader[i])
                throw new DomainException("El archivo debe ser una imagen válida en formato WebP.");
        }

        for (var i = 0; i < 4; i++)
        {
            if (data[8 + i] != WebpHeader[i])
                throw new DomainException("El archivo debe ser una imagen válida en formato WebP.");
        }
    }
}

