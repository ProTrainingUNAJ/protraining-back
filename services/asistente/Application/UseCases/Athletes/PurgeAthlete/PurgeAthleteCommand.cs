using Application.DTOs.Internal;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Athletes.PurgeAthlete;

/// <summary>
/// Comando para la purga física de los datos de un atleta en Asistente (#204 · KAN-499).
/// </summary>
public sealed record PurgeAthleteCommand(Guid AthleteId);

public sealed class PurgeAthleteCommandHandler(IAsistentePurgeRepository purgeRepository)
{
    public async Task<PurgedAthleteDataDto> HandleAsync(
        PurgeAthleteCommand command,
        CancellationToken cancellationToken = default)
    {
        return await purgeRepository.PurgeAthleteDataAsync(command.AthleteId, cancellationToken);
    }
}
