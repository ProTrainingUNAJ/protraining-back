namespace Application.Interfaces.Services;

public sealed record StructureAthleteSummaryDto(
    Guid Id,
    string FullName,
    Guid? CategoryId = null,
    bool Active = true,
    Guid PfId = default);

public interface IStructureAthleteClient
{
    /// <summary>
    /// Verifica si un atleta existe y pertenece al preparador físico autenticado (KAN-423 / KAN-431).
    /// </summary>
    Task<bool> IsOwnAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene la lista de atletas activos para el análisis del worker nocturno (#177 · KAN-472).
    /// </summary>
    Task<IReadOnlyList<StructureAthleteSummaryDto>> GetAthletesAsync(CancellationToken cancellationToken = default);
}
