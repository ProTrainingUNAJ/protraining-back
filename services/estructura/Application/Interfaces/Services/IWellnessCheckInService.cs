using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interfaces.Services;

public interface IWellnessCheckInService
{
    // Verifica si el atleta ya cargo su check-in de wellness en la fecha indicada
    Task<bool> HasCompletedCheckInAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default);

    // Consulta en lote que atletas de la lista ya completaron el check-in en la fecha indicada
    Task<IReadOnlySet<Guid>> GetCompletedCheckInAthleteIdsAsync(
        IReadOnlyList<Guid> athleteIds,
        DateOnly date,
        Guid? pfId = null,
        CancellationToken cancellationToken = default);
}
