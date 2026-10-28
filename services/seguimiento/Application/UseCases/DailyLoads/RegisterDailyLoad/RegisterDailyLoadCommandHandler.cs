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

        Validate(gym, "Gimnasio");
        Validate(sport, "Entrenamiento del deporte");
        Validate(match1, "Partido 1");
        Validate(match2, "Partido 2");
        Validate(individual, "Entrenamiento individual");

        if (gym.Minutes == 0 && sport.Minutes == 0 && match1.Minutes == 0
            && match2.Minutes == 0 && individual.Minutes == 0)
            throw new DomainException("Debe registrar al menos una actividad con minutos mayores a 0.");

        var date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue);
        var load = await repository.GetByAthleteAndDateAsync(command.AthleteId, date, cancellationToken);
        var created = load is null;

        if (created)
        {
            load = new DailyLoad { AthleteId = command.AthleteId, Date = date };
            await repository.AddAsync(load, cancellationToken);
        }

        load!.GymMinutes = gym.Minutes; load.GymRpe = gym.Rpe;
        load.SportTrainingMinutes = sport.Minutes; load.SportTrainingRpe = sport.Rpe;
        load.Match1Minutes = match1.Minutes; load.Match1Rpe = match1.Rpe;
        load.Match2Minutes = match2.Minutes; load.Match2Rpe = match2.Rpe;
        load.IndividualWorkMinutes = individual.Minutes; load.IndividualWorkRpe = individual.Rpe;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RegisterDailyLoadResult(
            DailyLoadMappings.ToResponse(load, "Carga del día registrada."), created);
    }

    public Task<RegisterDailyLoadResult> Handle(
        RegisterDailyLoadCommand command, CancellationToken cancellationToken = default)
        => HandleAsync(command, cancellationToken);

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

    private static void Validate(ActivityLoadDto activity, string name)
    {
        if (activity.Minutes < 0)
            throw new DomainException($"{name}: los minutos deben ser 0 o más.");
        if (activity.Rpe is < 0 or > 10)
            throw new DomainException($"{name}: el RPE debe estar entre 0 y 10.");
        if (activity.Minutes > 0 && activity.Rpe is null)
            throw new DomainException($"{name}: el RPE es obligatorio si los minutos son mayores a 0.");
    }
}