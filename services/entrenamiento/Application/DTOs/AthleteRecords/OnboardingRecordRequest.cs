namespace Application.DTOs.AthleteRecords;

public class OnboardingRecordRequest
{
    public Guid ExerciseId { get; set; }

    public decimal WeightOneRepMaxKg { get; set; }

    public OnboardingRecordRequest() { }

    public OnboardingRecordRequest(Guid exerciseId, decimal weightOneRepMaxKg)
    {
        ExerciseId = exerciseId;
        WeightOneRepMaxKg = weightOneRepMaxKg;
    }
}