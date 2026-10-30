using Domain.Entities;

namespace Domain.Services;

public interface IExerciseRestrictionChecker
{
    IReadOnlyDictionary<string, int> ParsePainZones(string? painZones);

    IReadOnlyList<ExerciseCheckResult> Check(
        IEnumerable<Guid> exerciseIds,
        string? painZones,
        IEnumerable<ExerciseRestriction> restrictions);

    IReadOnlyList<ExerciseSubstituteResult> GetSubstitutesForZone(
        Guid exerciseId,
        string zone,
        IEnumerable<ExerciseRestriction> restrictions);
}

