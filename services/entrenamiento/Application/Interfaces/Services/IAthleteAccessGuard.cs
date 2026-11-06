namespace Application.Interfaces.Services;

// Centraliza la autorización de lectura de datos de un atleta (PF dueño o el mismo atleta).
public interface IAthleteAccessGuard
{
    Task EnsureCanReadAsync(Guid athleteId, CancellationToken cancellationToken = default);
}

