using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class Category : BaseEntity
{
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private Category() { }

    public static Category Create(Guid organizationId, string? name, string? description = null)
    {
        if (organizationId == Guid.Empty)
            throw new DomainException("El identificador de la organización es inválido.");

        var category = new Category { OrganizationId = organizationId };
        category.Rename(name);
        category.Description = CleanDescription(description);
        return category;
    }

    public void Rename(string? name)
    {
        var cleanName = name?.Trim();
        if (string.IsNullOrWhiteSpace(cleanName) || cleanName.Length > 150)
            throw new DomainException("El nombre de la categoría es obligatorio y debe tener entre 1 y 150 caracteres.");

        Name = cleanName;
    }

    public void UpdateDescription(string? description) => Description = CleanDescription(description);

    private static string? CleanDescription(string? description)
    {
        var clean = description?.Trim();
        if (clean is { Length: > 500 })
            throw new DomainException("La descripción de la categoría no puede superar los 500 caracteres.");

        return string.IsNullOrWhiteSpace(clean) ? null : clean;
    }
}
