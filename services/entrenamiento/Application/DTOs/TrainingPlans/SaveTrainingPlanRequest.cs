using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Application.DTOs.TrainingPlans;

public record PrescribedExerciseRequest : IValidatableObject
{
    public Guid ExerciseId { get; init; }
    public int Order { get; init; }
    public int Sets { get; init; }

    [JsonConverter(typeof(RepsJsonConverter))]
    public string Reps { get; init; } = string.Empty;

    public decimal? TargetPercentage { get; init; }
    public Guid? Id { get; init; }
    public string? IntensityType { get; init; }
    public decimal? TargetRpe { get; init; }
    public int? TargetRir { get; init; }
    public string? Notes { get; init; }

    public PrescribedExerciseRequest() { }

    public PrescribedExerciseRequest(
        Guid exerciseId,
        int order,
        int sets,
        string reps,
        decimal? targetPercentage = null,
        Guid? id = null,
        string? intensityType = null,
        decimal? targetRpe = null,
        int? targetRir = null,
        string? notes = null)
    {
        ExerciseId = exerciseId;
        Order = order;
        Sets = sets;
        Reps = reps;
        TargetPercentage = targetPercentage;
        Id = id;
        IntensityType = intensityType;
        TargetRpe = targetRpe;
        TargetRir = targetRir;
        Notes = notes;
    }

    public PrescribedExerciseRequest(
        Guid exerciseId,
        int order,
        int sets,
        int reps,
        decimal? targetPercentage = null,
        Guid? id = null,
        string? intensityType = null,
        decimal? targetRpe = null,
        int? targetRir = null,
        string? notes = null)
        : this(exerciseId, order, sets, reps.ToString(), targetPercentage, id, intensityType, targetRpe, targetRir, notes)
    {
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(IntensityType))
        {
            var normalized = IntensityType.Trim().ToUpperInvariant();
            if (normalized != "RPE" && normalized != "RIR")
            {
                yield return new ValidationResult(
                    "La métrica de intensidad debe ser 'RPE' o 'RIR'.",
                    [nameof(IntensityType)]);
            }
        }

        if (TargetRpe.HasValue && (TargetRpe.Value < 1m || TargetRpe.Value > 10m))
        {
            yield return new ValidationResult(
                "El RPE debe estar entre 1 y 10.",
                [nameof(TargetRpe)]);
        }

        if (TargetRir.HasValue && (TargetRir.Value < 0 || TargetRir.Value > 5))
        {
            yield return new ValidationResult(
                "El RIR debe estar entre 0 y 5.",
                [nameof(TargetRir)]);
        }
    }
}

public record TrainingBlockRequest(
    string Title,
    int Order,
    List<PrescribedExerciseRequest>? Exercises = null,
    Guid? Id = null
);

public record TrainingSessionRequest(
    string Name,
    int Order,
    DayOfWeek DayOfWeek,
    string? CoachNotes = null,
    List<PrescribedExerciseRequest>? Exercises = null,
    Guid? Id = null,
    TimeOnly? StartTime = null,
    List<TrainingBlockRequest>? Blocks = null
);

public record TrainingWeekRequest(
    int WeekNumber,
    List<TrainingSessionRequest>? Sessions = null,
    Guid? Id = null
);

public record SaveTrainingPlanRequest(
    string Name,
    string? Objective = null,
    List<TrainingWeekRequest>? Weeks = null
);
