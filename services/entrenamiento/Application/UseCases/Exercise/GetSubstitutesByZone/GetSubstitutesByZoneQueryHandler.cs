using Application.DTOs.Exercise;
using Application.DTOs.Mcp;
using Application.Interfaces.Persistence;
using Domain.Exceptions;

namespace Application.UseCases.Exercise.GetSubstitutesByZone;

public sealed class GetSubstitutesByZoneQueryHandler(
    IExerciseRestrictionRepository restrictionRepository)
{
    public async Task<ZoneSubstitutesResultDto> Handle(
        GetSubstitutesByZoneQuery query,
        CancellationToken cancellationToken = default)
    {
        var restrictionZones = GetSubstitutesByZoneQuery.ResolveRestrictionZones(query.Zone);
        if (restrictionZones.Count == 0)
            throw new DomainException("La zona es obligatoria.");

        var normalizedInput = query.Zone!.Trim().ToLowerInvariant();
        var restrictions = await restrictionRepository.GetRestrictionsByZonesAsync(restrictionZones, cancellationToken);

        var substitutes = restrictions
            .SelectMany(r => r.Substitutes)
            .Select(s => new ExerciseSubstituteDto
            {
                ExerciseId = s.SubstituteExerciseId,
                Name = s.SubstituteExercise?.Name ?? string.Empty
            })
            .GroupBy(s => s.ExerciseId)
            .Select(g => g.First())
            .OrderBy(s => s.Name)
            .ToList();

        return new ZoneSubstitutesResultDto(normalizedInput, substitutes);
    }
}
