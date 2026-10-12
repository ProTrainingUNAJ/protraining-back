namespace Application.Interfaces.Services;

using Application.DTOs.Ai;

public interface IAiService
{
    Task<RoutineSuggestionResponseDto> SuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default);
}
