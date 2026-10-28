using Domain.Exceptions;

namespace Domain.Entities;

public sealed class ExerciseRestriction
{
    public static readonly IReadOnlySet<string> ValidZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "abdominales",
        "antebrazos",
        "biceps",
        "cuadriceps",
        "cuello_trapecios",
        "dorsales",
        "espalda_alta",
        "espalda_baja",
        "espalda_media",
        "gemelos",
        "gluteos",
        "hombros",
        "isquiotibiales",
        "manos",
        "oblicuos",
        "pecho",
        "pies",
        "trapecios",
        "triceps"
    };

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Zone { get; private set; } = string.Empty;
    public Guid ExerciseId { get; private set; }
    public Exercise Exercise { get; private set; } = null!;
    public int MinPainLevel { get; private set; } = 1;
    public string Reason { get; private set; } = string.Empty;

    public IReadOnlyCollection<ExerciseSubstitute> Substitutes => _substitutes.AsReadOnly();
    private readonly List<ExerciseSubstitute> _substitutes = new();

    private ExerciseRestriction() { }

    private ExerciseRestriction(string zone, Guid exerciseId, string reason, int minPainLevel)
    {
        Zone = zone;
        ExerciseId = exerciseId;
        Reason = reason;
        MinPainLevel = minPainLevel;
    }

    public static ExerciseRestriction Create(string zone, Guid exerciseId, string reason, int minPainLevel = 1)
    {
        if (string.IsNullOrWhiteSpace(zone))
            throw new DomainException("La zona de dolor es obligatoria.");

        var normalizedZone = zone.Trim().ToLowerInvariant();
        if (!ValidZones.Contains(normalizedZone))
            throw new DomainException($"La zona '{zone}' no es una zona de dolor válida.");

        if (exerciseId == Guid.Empty)
            throw new DomainException("El ejercicio es obligatorio.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("El motivo de la restricción es obligatorio.");

        if (minPainLevel < 1)
            throw new DomainException("El nivel mínimo de dolor debe ser al menos 1.");

        return new ExerciseRestriction(normalizedZone, exerciseId, reason.Trim(), minPainLevel);
    }

    public ExerciseSubstitute AddSubstitute(Guid substituteExerciseId, int order)
    {
        if (substituteExerciseId == ExerciseId)
            throw new DomainException("El ejercicio sustituto no puede ser igual al ejercicio restringido.");

        if (order <= 0)
            throw new DomainException("El orden del sustituto debe ser mayor a cero.");

        if (_substitutes.Any(s => s.SubstituteExerciseId == substituteExerciseId))
            throw new DomainException("El ejercicio ya fue agregado como sustituto.");

        var substitute = ExerciseSubstitute.Create(Id, substituteExerciseId, order);
        _substitutes.Add(substitute);
        return substitute;
    }
}
