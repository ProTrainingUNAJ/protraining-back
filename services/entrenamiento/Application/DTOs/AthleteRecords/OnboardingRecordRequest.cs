using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.AthleteRecords;

public class OnboardingRecordRequest
{
    public Guid ExerciseId { get; set; }

    [Range(1, 500, ErrorMessage = "El peso de la marca debe estar entre 1 y 500 kg.")]
    public decimal WeightOneRepMaxKg { get; set; }

    public OnboardingRecordRequest() { }

    public OnboardingRecordRequest(Guid exerciseId, decimal weightOneRepMaxKg)
    {
        ExerciseId = exerciseId;
        WeightOneRepMaxKg = weightOneRepMaxKg;
    }
}