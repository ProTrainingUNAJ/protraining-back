using Application.DTOs.Injuries;
using Domain.Entities;

namespace Application.Mappings;

public static class AthleteInjuryMappings
{
    public static AthleteInjuryDto ToDto(AthleteInjury injury) =>
        new(
            injury.Id,
            injury.AthleteId,
            injury.Zone,
            injury.Description,
            injury.InjuryDate,
            injury.EstimatedReturnDate,
            injury.Status.ToString(),
            injury.ClearedAt,
            injury.CreatedAt);
}
