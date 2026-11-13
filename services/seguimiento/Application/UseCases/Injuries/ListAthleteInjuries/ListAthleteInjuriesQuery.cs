using Domain.Entities;

namespace Application.UseCases.Injuries.ListAthleteInjuries;

// Historial de lesiones de un atleta (KAN-419). El filtro por estado es opcional;
// el filtro por PF no se pide acá: lo aplica el AppDbContext.
public sealed record ListAthleteInjuriesQuery(
    Guid AthleteId,
    InjuryStatus? Status = null);
