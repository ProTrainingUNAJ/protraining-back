using Application.Exceptions;
using Application.Interfaces.Persistence;

using Domain.Entities;

namespace Application.UseCases.Athletes.DeleteAthlete;

public sealed record DeleteAthleteCommand(Guid Id);

public sealed class DeleteAthleteCommandHandler(
    IAthleteRepository repository,
    IPendingPurgeRepository pendingPurgeRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(DeleteAthleteCommand command, CancellationToken cancellationToken = default)
    {
        var athlete = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        // 1. Marca el soft delete del atleta (AppDbContext asigna deleted_at).
        await repository.DeleteAsync(athlete, cancellationToken);

        // 2. Registra la purga pendiente en la misma unidad de trabajo (Transactional Outbox).
        var purge = PendingPurge.Create(athlete.Id, "Athlete");
        await pendingPurgeRepository.AddAsync(purge, cancellationToken);

        // 3. Confirma ambas operaciones atómicamente en la misma transacción.
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
