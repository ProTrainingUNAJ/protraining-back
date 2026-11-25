using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.CheckIns;

public sealed record SubmitCheckInInjuryPainRequest
{
    public Guid InjuryId { get; init; }

    [Range(0, 10, ErrorMessage = "El dolor de la molestia debe estar entre 0 y 10.")]
    public int? Level { get; init; }
}
