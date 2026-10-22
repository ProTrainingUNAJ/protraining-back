using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

/// <summary>
/// Representa una prueba física estandarizada dentro del catálogo del microservicio de Seguimiento.
/// Hereda de BaseEntity y aplica encapsulamiento y validaciones de dominio.
/// </summary>
public sealed class CatalogTest : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public MotorCapacity MotorCapacity { get; private set; }
    public string MeasurementUnit { get; private set; } = string.Empty;
    public int DifficultyLevel { get; private set; }
    public bool Active { get; private set; }

    private CatalogTest() { }

    public CatalogTest(string name, MotorCapacity motorCapacity, string measurementUnit, int difficultyLevel = 1, bool active = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del test no puede estar vacío.");

        if (string.IsNullOrWhiteSpace(measurementUnit))
            throw new DomainException("La unidad de medida es obligatoria.");

        if (difficultyLevel is < 1 or > 5)
            throw new DomainException("El nivel de dificultad debe estar entre 1 y 5.");

        if (!Enum.IsDefined(motorCapacity))
            throw new DomainException("Capacidad motora inválida.");

        Name = name.Trim();
        MotorCapacity = motorCapacity;
        MeasurementUnit = measurementUnit.Trim();
        DifficultyLevel = difficultyLevel;
        Active = active;
    }

    public void Update(string name, MotorCapacity motorCapacity, string measurementUnit, int difficultyLevel, bool active)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del test no puede estar vacío.");

        if (difficultyLevel is < 1 or > 5)
            throw new DomainException("El nivel de dificultad debe estar entre 1 y 5.");

        Name = name.Trim();
        MotorCapacity = motorCapacity;
        MeasurementUnit = measurementUnit.Trim();
        DifficultyLevel = difficultyLevel;
        Active = active;
    }

    public void Deactivate() => Active = false;
}
