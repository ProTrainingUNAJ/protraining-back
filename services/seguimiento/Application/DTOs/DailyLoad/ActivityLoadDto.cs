using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.DailyLoad;

public sealed record ActivityLoadDto
{
    [Range(0, int.MaxValue, ErrorMessage = "Los minutos deben ser 0 o más.")]
    public int Minutes { get; init; }

    [Range(0, 10, ErrorMessage = "El RPE debe estar entre 0 y 10.")]
    public int? Rpe { get; init; }
}
