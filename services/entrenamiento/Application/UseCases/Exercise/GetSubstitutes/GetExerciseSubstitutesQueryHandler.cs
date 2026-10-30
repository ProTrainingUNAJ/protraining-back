using Application.DTOs.Exercise;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;

namespace Application.UseCases.Exercise.GetSubstitutes;

public class GetExerciseSubstitutesQueryHandler
{
    private readonly IExerciseRepository _exerciseRepository;
    private readonly IExerciseRestrictionRepository _restrictionRepository;
    private readonly IExerciseRestrictionChecker _restrictionChecker;

    public GetExerciseSubstitutesQueryHandler(
        IExerciseRepository exerciseRepository,
        IExerciseRestrictionRepository restrictionRepository,
        IExerciseRestrictionChecker restrictionChecker)
    {
        _exerciseRepository = exerciseRepository;
        _restrictionRepository = restrictionRepository;
        _restrictionChecker = restrictionChecker;
    }

    public async Task<List<ExerciseSubstituteDto>> Handle(
        GetExerciseSubstitutesQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.Zone))
            throw new DomainException("La zona de dolor es obligatoria.");

        var normalizedZone = query.Zone.Trim().ToLowerInvariant();
        if (!ExerciseRestriction.ValidZones.Contains(normalizedZone))
            throw new DomainException($"La zona '{query.Zone}' no es una zona de dolor válida.");

        var exerciseExists = await _exerciseRepository.ExistsAsync(query.ExerciseId, cancellationToken);
        if (!exerciseExists)
            throw new NotFoundException("El ejercicio no existe.");

        var restriction = await _restrictionRepository.GetRestrictionByExerciseAndZoneAsync(
            query.ExerciseId,
            normalizedZone,
            cancellationToken);

        var substitutes = _restrictionChecker.GetSubstitutesForZone(
            query.ExerciseId,
            normalizedZone,
            restriction != null ? [restriction] : []);

        return substitutes.Select(s => new ExerciseSubstituteDto
        {
            ExerciseId = s.ExerciseId,
            Name = s.Name
        }).ToList();
    }
}

