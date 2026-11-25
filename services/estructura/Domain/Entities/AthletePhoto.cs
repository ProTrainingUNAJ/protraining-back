using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class AthletePhoto : BaseEntity
{
    public Guid AthleteId { get; private set; }
    public byte[] Data { get; private set; } = [];
    public string ContentType { get; private set; } = "image/webp";
    public long FileSize { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }

    private AthletePhoto() { }

    public static AthletePhoto Create(
        Guid athleteId,
        byte[] data,
        string contentType,
        long fileSize,
        int? width = null,
        int? height = null)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        if (data == null || data.Length == 0)
            throw new DomainException("Los datos de la foto no pueden estar vacíos.");

        return new AthletePhoto
        {
            AthleteId = athleteId,
            Data = data,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/webp" : contentType.Trim(),
            FileSize = fileSize,
            Width = width,
            Height = height
        };
    }

    public void Update(
        byte[] data,
        string contentType,
        long fileSize,
        int? width = null,
        int? height = null)
    {
        if (data == null || data.Length == 0)
            throw new DomainException("Los datos de la foto no pueden estar vacíos.");

        Data = data;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/webp" : contentType.Trim();
        FileSize = fileSize;
        Width = width;
        Height = height;
    }
}

