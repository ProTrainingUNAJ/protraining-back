using Application.DTOs.TrainingSessions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.TrainingSessions.GetTrainingSession;

public class GetTrainingSessionQueryHandler
{
    private readonly ITrainingSessionRepository _sessionRepository;

    public GetTrainingSessionQueryHandler(ITrainingSessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }

    public async Task<TrainingSessionDto?> Handle(GetTrainingSessionQuery query, CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(query.SessionId, cancellationToken);
        if (session == null)
            return null;

        return new TrainingSessionDto(
            session.Id,
            session.TrainingWeekId,
            session.Name,
            session.Order,
            session.DayOfWeek,
            session.CoachNotes
        );
    }
}
