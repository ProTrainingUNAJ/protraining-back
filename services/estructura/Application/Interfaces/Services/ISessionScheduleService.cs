using Application.DTOs.Reminders;

namespace Application.Interfaces.Services;

public interface ISessionScheduleService
{
    // Devuelve los atletas y categorias con sesion de entrenamiento programada en la fecha indicada para el PF
    Task<ScheduledSessionTargetsResponse> GetAthletesWithScheduledSessionAsync(
        DateOnly date,
        Guid pfId,
        CancellationToken cancellationToken = default);
}
