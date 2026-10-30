using Application.DTOs.Attendance;
using Application.Interfaces.Persistence;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Attendance.SetAttendance;

public sealed record SetAttendanceCommand(Guid AthleteId, SetAttendanceRequest Request);

public sealed record SetAttendanceResult(Domain.Entities.Attendance Attendance, bool Created);

public sealed class SetAttendanceCommandHandler(
    IAttendanceRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<SetAttendanceResult> HandleAsync(
        SetAttendanceCommand command, CancellationToken cancellationToken = default)
    {
        if (command.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        var request = command.Request ?? new SetAttendanceRequest(false);
        var date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue);

        Domain.Entities.Attendance? attendance = await repository.GetByAthleteAndDateAsync(command.AthleteId, date, cancellationToken);
        var created = attendance is null;

        if (created)
        {
            attendance = new Domain.Entities.Attendance { AthleteId = command.AthleteId, Date = date };
            await repository.AddAsync(attendance, cancellationToken);
        }

        attendance!.IsPresent = request.Present;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SetAttendanceResult(attendance, created);
    }

    public Task<SetAttendanceResult> Handle(
        SetAttendanceCommand command, CancellationToken cancellationToken = default) =>
        HandleAsync(command, cancellationToken);
}