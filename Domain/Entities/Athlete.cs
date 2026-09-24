using Domain.Exceptions;

namespace Domain.Entities;

public sealed class Athlete
{
    public long Id { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public decimal Record1RmKg { get; private set; }
    public bool Active { get; private set; }

    private Athlete() { }

    public Athlete(string? fullName, string? category, decimal record1RmKg)
    {
        var trimmedName = fullName?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > 150)
            throw new DomainException("El nombre del atleta debe tener entre 1 y 150 caracteres.");

        var trimmedCat = category?.Trim();
        if (string.IsNullOrEmpty(trimmedCat) || trimmedCat.Length > 100)
            throw new DomainException("La categoría debe tener entre 1 y 100 caracteres.");

        if (record1RmKg < 0 || record1RmKg > 500)
            throw new DomainException("El récord 1RM debe estar entre 0 y 500 kg.");

        FullName = trimmedName;
        Category = trimmedCat;
        Record1RmKg = record1RmKg;
        Active = true;
    }

    public void SetCategory(string? newCategory)
    {
        var trimmed = newCategory?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 100)
            throw new DomainException("La categoría no puede estar vacía ni superar 100 caracteres.");
        Category = trimmed;
    }

    public void SetActive(bool active) => Active = active;

    public void UpdateRecord(decimal newRecord1RmKg)
    {
        if (newRecord1RmKg < 0 || newRecord1RmKg > 500)
            throw new DomainException("El récord 1RM debe estar entre 0 y 500 kg.");
        Record1RmKg = newRecord1RmKg;
    }

    public static void ValidateId(long id)
    {
        if (id <= 0)
            throw new DomainException("El identificador del atleta debe ser un número positivo.");
    }
}
