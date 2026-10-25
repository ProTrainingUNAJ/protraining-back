namespace Application.UseCases.Ai.SuggestRoutine;

using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Exceptions;

public sealed class SuggestRoutineUseCase(IAiService aiService)
{
    public async Task<RoutineSuggestionResponseDto> ExecuteAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        if (string.IsNullOrWhiteSpace(request.Goal))
            throw new DomainException("El objetivo de entrenamiento es obligatorio.");

        var normalizedRequest = request with { Goal = request.Goal.Trim() };

        return await aiService.SuggestRoutineAsync(normalizedRequest, cancellationToken);
    }
}
