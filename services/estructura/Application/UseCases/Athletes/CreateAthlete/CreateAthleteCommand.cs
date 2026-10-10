using Application.DTOs;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Athletes.CreateAthlete;

public sealed record CreateAthleteCommand(string? FullName, string? Category, decimal Record1RmKg);

public sealed class CreateAthleteCommandHandler(IAthleteRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<AthleteDto> HandleAsync(CreateAthleteCommand command, CancellationToken cancellationToken = default)
    {
        var athlete = new Athlete(command.FullName, command.Category, command.Record1RmKg);
        await repository.AddAsync(athlete, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AthleteMappings.ToDto(athlete);
    }
}
