using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class PrescribedExercise : BaseEntity
{
    public int Order { get; private set; }
    public int Sets { get; private set; }
    public string Reps { get; private set; }
    public decimal? TargetPercentage { get; private set; }
    public decimal? TargetDistanceMeters { get; private set; }
    public decimal? TargetTimeSeconds { get; private set; }

    public IntensityType? IntensityType { get; private set; }
    public decimal? TargetRpe { get; private set; }
    public int? TargetRir { get; private set; }
    public string? Notes { get; private set; }

    public Guid TrainingBlockId { get; private set; }
    public TrainingBlock TrainingBlock { get; private set; } = null!;

    public Guid? TrainingSessionId { get; private set; }
    public TrainingSession? TrainingSession { get; private set; }

    public Guid ExerciseId { get; private set; }
    public Exercise Exercise { get; private set; } = null!;

    private PrescribedExercise(
        int order,
        int sets,
        string reps,
        decimal? targetPercentage,
        Guid trainingBlockId,
        Guid exerciseId,
        IntensityType? intensityType,
        decimal? targetRpe,
        int? targetRir,
        string? notes,
        Guid? trainingSessionId,
        decimal? targetDistanceMeters,
        decimal? targetTimeSeconds)
    {
        Order = order;
        Sets = sets;
        Reps = reps;
        TargetPercentage = targetPercentage;
        TrainingBlockId = trainingBlockId;
        ExerciseId = exerciseId;
        IntensityType = intensityType;
        TargetRpe = targetRpe;
        TargetRir = targetRir;
        Notes = notes;
        TrainingSessionId = trainingSessionId;
        TargetDistanceMeters = targetDistanceMeters;
        TargetTimeSeconds = targetTimeSeconds;
    }

    public static PrescribedExercise Create(
        int order,
        int sets,
        string reps,
        decimal? targetPercentage,
        Guid trainingBlockId,
        Guid exerciseId,
        IntensityType? intensityType = null,
        decimal? targetRpe = null,
        int? targetRir = null,
        string? notes = null,
        Guid? trainingSessionId = null,
        decimal? targetDistanceMeters = null,
        decimal? targetTimeSeconds = null)
    {
        if (order <= 0)
            throw new DomainException("El orden debe ser mayor a cero.");

        if (sets <= 0)
            throw new DomainException("Las series y repeticiones deben ser mayores a cero.");

        ValidateReps(reps);
        ValidateIntensity(intensityType, targetRpe, targetRir);
        ValidateTargets(targetDistanceMeters, targetTimeSeconds);

        return new PrescribedExercise(
            order,
            sets,
            reps.Trim(),
            targetPercentage,
            trainingBlockId,
            exerciseId,
            intensityType,
            targetRpe,
            targetRir,
            notes,
            trainingSessionId,
            targetDistanceMeters,
            targetTimeSeconds);
    }

    public static PrescribedExercise Create(
        int order,
        int sets,
        int reps,
        decimal? targetPercentage,
        Guid trainingBlockId,
        Guid exerciseId,
        Guid? trainingSessionId = null)
    {
        return Create(
            order: order,
            sets: sets,
            reps: reps.ToString(),
            targetPercentage: targetPercentage,
            trainingBlockId: trainingBlockId,
            exerciseId: exerciseId,
            trainingSessionId: trainingSessionId);
    }

    public void Update(
        int order,
        int sets,
        string reps,
        decimal? targetPercentage,
        Guid exerciseId,
        IntensityType? intensityType = null,
        decimal? targetRpe = null,
        int? targetRir = null,
        string? notes = null,
        decimal? targetDistanceMeters = null,
        decimal? targetTimeSeconds = null)
    {
        if (order <= 0)
            throw new DomainException("El orden debe ser mayor a cero.");

        if (sets <= 0)
            throw new DomainException("Las series y repeticiones deben ser mayores a cero.");

        ValidateReps(reps);
        ValidateIntensity(intensityType, targetRpe, targetRir);
        ValidateTargets(targetDistanceMeters, targetTimeSeconds);

        Order = order;
        Sets = sets;
        Reps = reps.Trim();
        TargetPercentage = targetPercentage;
        ExerciseId = exerciseId;
        IntensityType = intensityType;
        TargetRpe = targetRpe;
        TargetRir = targetRir;
        Notes = notes;
        TargetDistanceMeters = targetDistanceMeters;
        TargetTimeSeconds = targetTimeSeconds;
    }

    public void Update(
        int order,
        int sets,
        int reps,
        decimal? targetPercentage,
        Guid exerciseId)
    {
        Update(order, sets, reps.ToString(), targetPercentage, exerciseId);
    }

    public void UpdateOrder(int order)
    {
        if (order <= 0)
            throw new DomainException("El orden debe ser mayor a cero.");

        Order = order;
    }

    public void SetTrainingBlockId(Guid blockId)
    {
        TrainingBlockId = blockId;
    }

    public void SetTrainingSessionId(Guid sessionId)
    {
        TrainingSessionId = sessionId;
    }

    private static void ValidateReps(string reps)
    {
        if (string.IsNullOrWhiteSpace(reps))
            throw new DomainException("Las repeticiones no pueden estar vacías.");

        var trimmed = reps.Trim();
        if (int.TryParse(trimmed, out var singleReps))
        {
            if (singleReps <= 0)
                throw new DomainException("Las series y repeticiones deben ser mayores a cero.");
            return;
        }

        var parts = trimmed.Split('-');
        if (parts.Length == 2 &&
            int.TryParse(parts[0].Trim(), out var minReps) &&
            int.TryParse(parts[1].Trim(), out var maxReps))
        {
            if (minReps <= 0 || maxReps <= 0)
                throw new DomainException("Las repeticiones en el rango deben ser mayores a cero.");
            if (minReps > maxReps)
                throw new DomainException("El rango de repeticiones es inválido (el mínimo no puede superar al máximo).");
            return;
        }

        throw new DomainException("Las repeticiones deben ser un número mayor a cero o un rango válido (ej. 6-8).");
    }

    private static void ValidateIntensity(IntensityType? intensityType, decimal? targetRpe, int? targetRir)
    {
        if (intensityType == Entities.IntensityType.RPE)
        {
            if (targetRpe.HasValue && (targetRpe.Value < 1m || targetRpe.Value > 10m))
                throw new DomainException("El RPE debe estar entre 1 y 10.");
        }
        else if (intensityType == Entities.IntensityType.RIR)
        {
            if (targetRir.HasValue && (targetRir.Value < 0 || targetRir.Value > 5))
                throw new DomainException("El RIR debe estar entre 0 y 5.");
        }

        if (targetRpe.HasValue && (targetRpe.Value < 1m || targetRpe.Value > 10m))
            throw new DomainException("El RPE debe estar entre 1 y 10.");

        if (targetRir.HasValue && (targetRir.Value < 0 || targetRir.Value > 5))
            throw new DomainException("El RIR debe estar entre 0 y 5.");
    }

    private static void ValidateTargets(decimal? targetDistanceMeters, decimal? targetTimeSeconds)
    {
        if (targetDistanceMeters.HasValue && targetDistanceMeters.Value <= 0)
            throw new DomainException("La distancia objetivo debe ser mayor a cero.");

        if (targetTimeSeconds.HasValue && targetTimeSeconds.Value <= 0)
            throw new DomainException("El tiempo objetivo debe ser mayor a cero.");
    }
}
