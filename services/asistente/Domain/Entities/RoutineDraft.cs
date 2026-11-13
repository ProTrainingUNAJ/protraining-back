namespace Domain.Entities;

using Domain.Common;
using Domain.Exceptions;

/// <summary>
/// Representa una propuesta o borrador de rutina generado con asistencia de IA para un atleta (#088 · KAN-383).
/// Almacena el objetivo solicitado, título sugerido, justificación técnica, proveedor que resolvió la propuesta
/// y el conjunto estructurado de ejercicios con sus repeticiones, series y cargas relativas en formato JSON.
/// </summary>
public sealed class RoutineDraft : BaseEntity
{
    public Guid AthleteId { get; private set; }
    public string Goal { get; private set; } = string.Empty;
    public string ProposedTitle { get; private set; } = string.Empty;
    public string JustificationSummary { get; private set; } = string.Empty;
    public string ProviderUsed { get; private set; } = string.Empty;
    public string ExercisesJson { get; private set; } = string.Empty;

    private RoutineDraft() { }

    public static RoutineDraft Create(
        Guid athleteId,
        string goal,
        string proposedTitle,
        string justificationSummary,
        string providerUsed,
        string exercisesJson)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        if (string.IsNullOrWhiteSpace(goal))
            throw new DomainException("El objetivo de entrenamiento es obligatorio.");

        if (string.IsNullOrWhiteSpace(proposedTitle))
            throw new DomainException("El título propuesto es obligatorio.");

        if (string.IsNullOrWhiteSpace(justificationSummary))
            throw new DomainException("El resumen de justificación es obligatorio.");

        if (string.IsNullOrWhiteSpace(exercisesJson))
            throw new DomainException("El borrador debe contener al menos un ejercicio en formato JSON.");

        return new RoutineDraft
        {
            AthleteId = athleteId,
            Goal = goal.Trim(),
            ProposedTitle = proposedTitle.Trim(),
            JustificationSummary = justificationSummary.Trim(),
            ProviderUsed = string.IsNullOrWhiteSpace(providerUsed) ? "Unknown" : providerUsed.Trim(),
            ExercisesJson = exercisesJson.Trim()
        };
    }
}
