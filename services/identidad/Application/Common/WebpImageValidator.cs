using Domain.Exceptions;

namespace Application.Common;

public static class WebpImageValidator
{
    public const int MaxSizeBytes = 200 * 1024; // 200 KB
    public const int MinDimensionPixels = 10;
    public const int MaxDimensionPixels = 2048;

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

        var (width, height) = TryGetDimensions(data);
        if (width.HasValue && height.HasValue)
        {
            if (width.Value < MinDimensionPixels || height.Value < MinDimensionPixels)
                throw new DomainException($"Las dimensiones de la imagen ({width.Value}x{height.Value}) deben ser de al menos {MinDimensionPixels}x{MinDimensionPixels} píxeles.");

            if (width.Value > MaxDimensionPixels || height.Value > MaxDimensionPixels)
                throw new DomainException($"Las dimensiones de la imagen ({width.Value}x{height.Value}) no pueden superar los {MaxDimensionPixels}x{MaxDimensionPixels} píxeles.");
        }
    }

    public static (int? width, int? height) TryGetDimensions(byte[] data)
    {
        if (data.Length < 16)
            return (null, null);

        var chunkHeader = System.Text.Encoding.ASCII.GetString(data, 12, 4);

        if (chunkHeader == "VP8 " && data.Length >= 30)
        {
            if (data[23] == 0x9D && data[24] == 0x01 && data[25] == 0x2A)
            {
                var width = (data[26] | (data[27] << 8)) & 0x3FFF;
                var height = (data[28] | (data[29] << 8)) & 0x3FFF;
                return (width, height);
            }
        }
        else if (chunkHeader == "VP8L" && data.Length >= 25)
        {
            if (data[20] == 0x2F)
            {
                uint bits = (uint)(data[21] | (data[22] << 8) | (data[23] << 16) | (data[24] << 24));
                var width = (int)((bits & 0x3FFF) + 1);
                var height = (int)(((bits >> 14) & 0x3FFF) + 1);
                return (width, height);
            }
        }
        else if (chunkHeader == "VP8X" && data.Length >= 30)
        {
            var width = 1 + data[24] + (data[25] << 8) + (data[26] << 16);
            var height = 1 + data[27] + (data[28] << 8) + (data[29] << 16);
            return (width, height);
        }

        return (null, null);
    }
}
