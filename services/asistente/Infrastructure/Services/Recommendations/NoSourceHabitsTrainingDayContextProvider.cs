using Application.Interfaces.Services;

namespace Infrastructure.Services.Recommendations;

/// <summary>
/// Implementación provisional mientras no existe criterio aprobado ni fuente del momento de
/// entrenamiento intenso. Devuelve null para que el momento figure en <c>missingData</c> y no se
/// invente una clasificación.
/// TODO: reemplazar por la fuente acordada con PO/PF. No inferir de 1RM, carga diaria,
/// RPE ni objetivos de plan sin una definición funcional validada.
/// </summary>
public sealed class NoSourceHabitsTrainingDayContextProvider : IHabitsTrainingDayContextProvider
{
    public Task<bool?> IsHeavyTrainingDayAsync(
        Guid athleteId,
        DateOnly date,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<bool?>(null);
}
