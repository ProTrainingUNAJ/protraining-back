using Domain.Entities;
namespace Application.UseCases.AthleteRecord.RegisterAthleteRecord;

public class RegisterAthleteRecordCommand
{
    public Guid AthleteId { get; set; }
    public Guid ExerciseId { get; set; }
    public decimal Weight { get; set; }
    public RecordSource Source { get; set; }
}