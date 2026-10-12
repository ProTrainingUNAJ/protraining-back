namespace Application.Interfaces.Services;

using Application.DTOs.Ai;

public interface IAiClient
{
    string ProviderName { get; }

    Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default);
}
