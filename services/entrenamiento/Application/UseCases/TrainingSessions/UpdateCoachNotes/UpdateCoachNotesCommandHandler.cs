using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.TrainingSessions.UpdateCoachNotes;

public class UpdateCoachNotesCommandHandler
{
    private readonly ITrainingSessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCoachNotesCommandHandler(ITrainingSessionRepository sessionRepository, IUnitOfWork unitOfWork)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(UpdateCoachNotesCommand command, CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(command.SessionId, cancellationToken);
        if (session == null)
            throw new NotFoundException($"No se encontró la sesión con ID '{command.SessionId}'.");

        session.UpdateCoachNotes(command.CoachNotes);
        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
