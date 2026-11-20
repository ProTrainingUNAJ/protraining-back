namespace Application.Interfaces.Services;

/// <summary>
/// Consulta en Identidad la preferencia del PF sobre recomendaciones de hábitos y alimentación (#KAN-493).
/// Devuelve <c>null</c> cuando no se puede consultar: el consumidor debe bloquear las recomendaciones (fail-closed).
/// </summary>
public interface IIdentityHabitsRecommendationsClient
{
    /// <summary>
    /// Preferencia del preparador físico autenticado. Requiere token de rol PreparadorFisico.
    /// </summary>
    Task<bool?> GetMyHabitsRecommendationsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Preferencia del preparador físico del atleta autenticado. Requiere token de rol Atleta.
    /// </summary>
    Task<bool?> GetAthletePfHabitsRecommendationsEnabledAsync(CancellationToken cancellationToken = default);
}
