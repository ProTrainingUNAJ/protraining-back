using Application.DTOs.Exercise;
using Application.Interfaces.Persistence;
using Domain.Services;

namespace Application.UseCases.Exercise.CheckRestrictions;

public class CheckExerciseRestrictionsQueryHandler
{
    private readonly IExerciseRestrictionRepository _restrictionRepository;
    private readonly IExerciseRestrictionChecker _restrictionChecker;

    public CheckExerciseRestrictionsQueryHandler(
        IExerciseRestrictionRepository restrictionRepository,
        IExerciseRestrictionChecker restrictionChecker)
    {
        _restrictionRepository = restrictionRepository;
        _restrictionChecker = restrictionChecker;
    }

    public async Task<List<ExerciseRestrictionCheckResultDto>> Handle(
        CheckExerciseRestrictionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var exerciseIds = query.ExerciseIds ?? [];
        if (exerciseIds.Count == 0)
        {
            return [];
        }

        var parsedZones = _restrictionChecker.ParsePainZones(query.PainZones);
        if (parsedZones.Count == 0)
        {
            return exerciseIds
                .Select(id => new ExerciseRestrictionCheckResultDto
                {
                    ExerciseId = id,
                    Restricted = false,
                    Zone = null,
                    PainLevel = null,
                    Reason = null,
                    Substitutes = []
                })
                .ToList();
        }

        var restrictions = await _restrictionRepository.GetRestrictionsByZonesAndExercisesAsync(
            parsedZones.Keys,
            exerciseIds,
            cancellationToken);

        var checkResults = _restrictionChecker.Check(exerciseIds, query.PainZones, restrictions);

        return checkResults.Select(r => new ExerciseRestrictionCheckResultDto
        {
            ExerciseId = r.ExerciseId,
            Restricted = r.Restricted,
            Zone = r.Zone,
            PainLevel = r.PainLevel,
            Reason = r.Reason,
            Substitutes = r.Substitutes.Select(s => new ExerciseSubstituteDto
            {
                ExerciseId = s.ExerciseId,
                Name = s.Name
            }).ToList()
        }).ToList();
    }
}

