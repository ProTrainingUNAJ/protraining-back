using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interfaces.Services;

public interface ISessionScheduleService
{
    // devuelve los Ids de los atletas que tienen sesion de entrenamiento programada en la fecha indicada
    Task<IReadOnlyList<Guid>> GetAthletesWithScheduledSessionAsync(DateOnly date, CancellationToken cancellationToken = default);
}
