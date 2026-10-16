using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class WellnessCheckInService(ILogger<WellnessCheckInService> logger) : IWellnessCheckInService
{
    public Task<bool> HasCompletedCheckInAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default)
    {
        // verifica si el atleta ya registro su check-in diario de wellness
        logger.LogInformation("Verificando check-in de wellness del atleta {AthleteId} para la fecha {Date}", athleteId, date);
        return Task.FromResult(false);
    }
}
