using Application.DTOs.Exercise;
using Application.DTOs.Mcp;
using Application.Interfaces.Persistence;
using Domain.Exceptions;

namespace Application.UseCases.Exercise.GetRestrictionsByZone;

public sealed class GetRestrictionsByZoneQueryHandler(
    IExerciseRestrictionRepository restrictionRepository)
{
    public async Task<ZoneRestrictionsResultDto> Handle(
        GetRestrictionsByZoneQuery query,
        CancellationToken cancellationToken = default)
    {
        var restrictionZones = GetRestrictionsByZoneQuery.ResolveRestrictionZones(query.Zone);
        if (restrictionZones.Count == 0)
            throw new DomainException("La zona es obligatoria.");

        var normalizedInput = query.Zone!.Trim().ToLowerInvariant();
        var restrictions = await restrictionRepository.GetRestrictionsByZonesAsync(restrictionZones, cancellationToken);

        var items = restrictions
            .Select(r => new RestrictedExerciseItemDto(
                ExerciseId: r.ExerciseId,
                ExerciseName: r.Exercise?.Name ?? string.Empty,
                Zone: r.Zone,
                MinPainLevel: r.MinPainLevel,
                Reason: r.Reason,
                Substitutes: r.Substitutes
                    .OrderBy(s => s.Order)
                    .Select(s => new ExerciseSubstituteDto
                    {
                        ExerciseId = s.SubstituteExerciseId,
                        Name = s.SubstituteExercise?.Name ?? string.Empty
                    })
                    .ToList()))
            .OrderBy(r => r.ExerciseName)
            .ToList();

        return new ZoneRestrictionsResultDto(normalizedInput, items);
    }
}
