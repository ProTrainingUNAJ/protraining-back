using Application.DTOs.Internal;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Users.PurgeAthlete;

/// <summary>
/// Comando para la purga física de los datos de un atleta/usuario en Identidad (#204 · KAN-499).
/// Recibe el AccountId (el ID de la cuenta en Identidad enviado por el job de purga).
/// </summary>
public sealed record PurgeAthleteCommand(Guid AccountId);

public sealed class PurgeAthleteCommandHandler(IIdentidadPurgeRepository purgeRepository)
{
    public async Task<PurgedAthleteDataDto> HandleAsync(
        PurgeAthleteCommand command,
        CancellationToken cancellationToken = default)
    {
        return await purgeRepository.PurgeAthleteDataAsync(command.AccountId, cancellationToken);
    }
}
