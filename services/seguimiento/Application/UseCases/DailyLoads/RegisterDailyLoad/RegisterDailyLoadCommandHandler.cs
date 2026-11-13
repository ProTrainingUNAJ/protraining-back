using Application.DTOs.DailyLoad;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.DailyLoads.RegisterDailyLoad;

public sealed record RegisterDailyLoadResult(DailyLoadResponseDto Response, bool Created);

public sealed class RegisterDailyLoadCommandHandler(
    IDailyLoadRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<RegisterDailyLoadResult> HandleAsync(
        RegisterDailyLoadCommand command, CancellationToken cancellationToken = default)
    {
        if (command.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        var request = command.Request ?? new RegisterDailyLoadRequest();
        var gym = request.Gym ?? new ActivityLoadDto();
        var sport = request.SportTraining ?? new ActivityLoadDto();
        var match1 = request.Match1 ?? new ActivityLoadDto();
        var match2 = request.Match2 ?? new ActivityLoadDto();
        var individual = request.IndividualWork ?? new ActivityLoadDto();

        var date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue);
        var load = await repository.GetByAthleteAndDateAsync(command.AthleteId, date, cancellationToken);
        var created = load is null;

        if (created)
        {
            load = new DailyLoad { AthleteId = command.AthleteId, Date = date };
            await repository.AddAsync(load, cancellationToken);
        }

        load!.Update(
            gym.Minutes, gym.Rpe,
            sport.Minutes, sport.Rpe,
            match1.Minutes, match1.Rpe,
            match2.Minutes, match2.Rpe,
            individual.Minutes, individual.Rpe);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RegisterDailyLoadResult(
            DailyLoadMappings.ToResponse(load, "Carga del día registrada."), created);
    }

    public async Task<DailyLoadResponseDto> GetTodayAsync(
        Guid athleteId, CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        var date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue);
        var load = await repository.GetByAthleteAndDateAsync(athleteId, date, cancellationToken);
        if (load is null) throw new DailyLoadNotFoundException();
        return DailyLoadMappings.ToResponse(load, "Carga del día obtenida.");
    }

}