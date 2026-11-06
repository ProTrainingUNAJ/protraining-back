using Application.Exceptions;
using Application.Interfaces.Services;

namespace Application.Services;

public sealed class AthleteAccessGuard(
    ICurrentUser currentUser,
    IStructureClient structureClient) : IAthleteAccessGuard
{
    public async Task EnsureCanReadAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        if (currentUser.IsInRole("PreparadorFisico"))
        {
            var athlete = await structureClient.GetAthleteByIdAsync(athleteId, cancellationToken);
            if (athlete is null)
                throw new AthleteNotFoundException(athleteId);
        }
        else
        {
            var me = await structureClient.GetMeAsync(cancellationToken);
            if (me is null)
                throw new NotFoundException("No se encontró un perfil de atleta vinculado a la cuenta actual.");

            if (me.AthleteId != athleteId)
                throw new AthleteNotFoundException(athleteId);
        }
    }
}

