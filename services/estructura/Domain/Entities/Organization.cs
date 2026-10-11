using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class Organization : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private Organization() { }

    public static Organization Create(string? name, string? description = null)
    {
        var organization = new Organization();
        organization.Rename(name);
        organization.Description = CleanDescription(description);
        return organization;
    }

    public void Rename(string? name)
    {
        var cleanName = name?.Trim();
        if (string.IsNullOrWhiteSpace(cleanName) || cleanName.Length > 150)
            throw new DomainException("El nombre de la organización es obligatorio y debe tener entre 1 y 150 caracteres.");

        Name = cleanName;
    }

    public void UpdateDescription(string? description) => Description = CleanDescription(description);

    private static string? CleanDescription(string? description)
    {
        var clean = description?.Trim();
        if (clean is { Length: > 500 })
            throw new DomainException("La descripción de la organización no puede superar los 500 caracteres.");

        return string.IsNullOrWhiteSpace(clean) ? null : clean;
    }
}
