using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

/// <summary>
/// Ajuste aprobado por el preparador físico para la sesión de hoy de un atleta ante dolor reportado en el check-in (#157 · KAN-452, #168 · KAN-463).
/// </summary>
public sealed class AthleteSessionAdjustment : BaseEntity
{
    public Guid TrainingSessionId { get; private set; }
    public Guid AthleteId { get; private set; }
    public Guid TrainingPlanId { get; private set; }
    public DateOnly Date { get; private set; }
    public string? Note { get; private set; }
    public string Items { get; private set; } = "[]";
    public DateTime UpdatedAt { get; private set; }

    private AthleteSessionAdjustment() { }

    public static AthleteSessionAdjustment Create(
        Guid trainingSessionId,
        Guid athleteId,
        DateOnly date,
        string itemsJson,
        string? note = null,
        Guid? trainingPlanId = null)
    {
        if (trainingSessionId == Guid.Empty)
            throw new DomainException("El identificador de la sesión de entrenamiento es obligatorio.");

        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        if (date == default)
            throw new DomainException("La fecha del ajuste es obligatoria.");

        if (string.IsNullOrWhiteSpace(itemsJson))
            throw new DomainException("Debe incluir al menos un cambio de ejercicio para la sesión.");

        return new AthleteSessionAdjustment
        {
            TrainingSessionId = trainingSessionId,
            AthleteId = athleteId,
            TrainingPlanId = trainingPlanId ?? Guid.Empty,
            Date = date,
            Items = itemsJson.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(string itemsJson, string? note = null, Guid? trainingPlanId = null)
    {
        if (string.IsNullOrWhiteSpace(itemsJson))
            throw new DomainException("Debe incluir al menos un cambio de ejercicio para la sesión.");

        Items = itemsJson.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trainingPlanId.HasValue && trainingPlanId.Value != Guid.Empty)
        {
            TrainingPlanId = trainingPlanId.Value;
        }
        UpdatedAt = DateTime.UtcNow;
    }
}
