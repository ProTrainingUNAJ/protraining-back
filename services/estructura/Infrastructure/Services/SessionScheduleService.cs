using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class SessionScheduleService(ILogger<SessionScheduleService> logger) : ISessionScheduleService
{
    public Task<IReadOnlyList<Guid>> GetAthletesWithScheduledSessionAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        // consulta los atletas con sesion de entrenamiento programada para la fecha
        logger.LogInformation("Consultando atletas con sesion programada para la fecha {Date}", date);
        return Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
    }
}
