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

    // Mapeo hacia el catálogo de ejercicios de Entrenamiento para los tests de
    // fuerza en kg. TargetExerciseName es el nombre exacto del ejercicio en Entrenamiento
    // (los catálogos no comparten ids ni nombres). ExerciseId es una caché del GUID
    // resuelto la primera vez que se confirma: los ids de Entrenamiento se generan con
    // Guid.NewGuid() por entorno, así que no pueden fijarse en el seed.
    public string? TargetExerciseName { get; private set; }
    public Guid? ExerciseId { get; private set; }

    private CatalogTest() { }

    public CatalogTest(
        string name,
        MotorCapacity motorCapacity,
        string measurementUnit,
        int difficultyLevel = 1,
        bool active = true,
        string? targetExerciseName = null)
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

        if (targetExerciseName is not null)
            SetTargetExercise(targetExerciseName);
    }

    // Vincula el test con el ejercicio homónimo del catálogo de Entrenamiento.
    public void SetTargetExercise(string targetExerciseName)
    {
        if (string.IsNullOrWhiteSpace(targetExerciseName))
            throw new DomainException("El nombre del ejercicio objetivo no puede estar vacío.");

        TargetExerciseName = targetExerciseName.Trim();
        // Cambiar el ejercicio objetivo invalida el GUID cacheado de la resolución anterior.
        ExerciseId = null;
    }

    // Persiste el ejercicio resuelto por nombre la primera vez que se confirma:
    // a partir de ahí las confirmaciones siguientes van directo contra Entrenamiento.
    public void CacheExerciseId(Guid exerciseId)
    {
        if (exerciseId == Guid.Empty)
            throw new DomainException("El ejercicio resuelto no puede ser vacío.");

        ExerciseId = exerciseId;
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
