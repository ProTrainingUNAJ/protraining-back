using Application.Interfaces.Persistence;

namespace Application.UseCases.AthleteRecord.RegisterAthleteRecord;

public class RegisterAthleteRecordCommandHandler
{
    private readonly IAthleteRecordRepository _repository;

    public RegisterAthleteRecordCommandHandler(IAthleteRecordRepository repository)
    {
        _repository = repository;
    }

    public async Task<Guid> Handle(RegisterAthleteRecordCommand request, CancellationToken cancellationToken = default)
    {        
        var previousRecord = await _repository.GetActiveRecordAsync(request.AthleteId, request.ExerciseId, cancellationToken);
        
        if (previousRecord != null)
        {
            previousRecord.Deactivate();
            await _repository.UpdateAsync(previousRecord, cancellationToken);
        }

        var newRecord = Domain.Entities.AthleteRecord.Create(
            request.AthleteId,
            request.ExerciseId,
            request.Weight,
            request.Source,
            DateTime.UtcNow
        );

        await _repository.AddAsync(newRecord, cancellationToken);

        
        return newRecord.Id;
    }
}