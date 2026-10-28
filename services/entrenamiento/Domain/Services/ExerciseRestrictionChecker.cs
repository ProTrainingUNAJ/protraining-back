using Domain.Entities;

namespace Domain.Services;

public class ExerciseRestrictionChecker : IExerciseRestrictionChecker
{
    public IReadOnlyDictionary<string, int> ParsePainZones(string? painZones)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(painZones))
            return result;

        var entries = painZones.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                continue;

            var zone = parts[0].ToLowerInvariant();
            var painLevel = 1;

            if (parts.Length > 1 && int.TryParse(parts[1], out var parsedLevel))
            {
                painLevel = parsedLevel;
            }

            if (painLevel < 1)
                continue;

            if (result.TryGetValue(zone, out var currentMax))
            {
                result[zone] = Math.Max(currentMax, painLevel);
            }
            else
            {
                result[zone] = painLevel;
            }
        }

        return result;
    }

    public IReadOnlyList<ExerciseCheckResult> Check(
        IEnumerable<Guid> exerciseIds,
        string? painZones,
        IEnumerable<ExerciseRestriction> restrictions)
    {
        var idsList = exerciseIds?.ToList() ?? [];
        if (idsList.Count == 0)
            return [];

        var parsedZones = ParsePainZones(painZones);
        if (parsedZones.Count == 0)
        {
            return idsList
                .Select(id => new ExerciseCheckResult(id, false, null, null, null, []))
                .ToList();
        }

        var restrictionsByExercise = restrictions
            .GroupBy(r => r.ExerciseId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var results = new List<ExerciseCheckResult>(idsList.Count);

        foreach (var exerciseId in idsList)
        {
            if (!restrictionsByExercise.TryGetValue(exerciseId, out var exerciseRestrictions))
            {
                results.Add(new ExerciseCheckResult(exerciseId, false, null, null, null, []));
                continue;
            }

            var matching = new List<(ExerciseRestriction Restriction, int PainLevel)>();
            foreach (var restriction in exerciseRestrictions)
            {
                if (parsedZones.TryGetValue(restriction.Zone, out var painLevel) && painLevel >= restriction.MinPainLevel)
                {
                    matching.Add((restriction, painLevel));
                }
            }

            if (matching.Count == 0)
            {
                results.Add(new ExerciseCheckResult(exerciseId, false, null, null, null, []));
                continue;
            }

            var best = matching
                .OrderByDescending(m => m.PainLevel)
                .ThenByDescending(m => m.Restriction.MinPainLevel)
                .First();

            var substitutes = best.Restriction.Substitutes
                .OrderBy(s => s.Order)
                .Select(s => new ExerciseSubstituteResult(s.SubstituteExerciseId, s.SubstituteExercise?.Name ?? string.Empty))
                .ToList();

            results.Add(new ExerciseCheckResult(
                exerciseId,
                true,
                best.Restriction.Zone,
                best.PainLevel,
                best.Restriction.Reason,
                substitutes));
        }

        return results;
    }

    public IReadOnlyList<ExerciseSubstituteResult> GetSubstitutesForZone(
        Guid exerciseId,
        string zone,
        IEnumerable<ExerciseRestriction> restrictions)
    {
        if (string.IsNullOrWhiteSpace(zone))
            return [];

        var normalizedZone = zone.Trim().ToLowerInvariant();

        var restriction = restrictions.FirstOrDefault(r =>
            r.ExerciseId == exerciseId &&
            r.Zone.Equals(normalizedZone, StringComparison.OrdinalIgnoreCase));

        if (restriction == null)
            return [];

        return restriction.Substitutes
            .OrderBy(s => s.Order)
            .Select(s => new ExerciseSubstituteResult(s.SubstituteExerciseId, s.SubstituteExercise?.Name ?? string.Empty))
            .ToList();
    }
}

