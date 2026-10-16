using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interfaces.Services;

public interface IWellnessCheckInService
{
    // Verifica si el atleta ya cargo su check-in de wellness en la fecha indicada
    Task<bool> HasCompletedCheckInAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default);
}
