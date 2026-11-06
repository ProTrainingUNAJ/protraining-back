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
            if (!await structureClient.IsOwnAthleteAsync(athleteId, cancellationToken))
                throw new AthleteNotFoundException();
        }
        else
        {
            var me = await structureClient.GetMeAsync(cancellationToken);
            if (me is null)
                throw new NoAthleteLinkedException();

            if (me.AthleteId != athleteId)
                throw new AthleteNotFoundException();
        }
    }
}

