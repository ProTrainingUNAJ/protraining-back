using Application.DTOs.Structure;

namespace Application.Interfaces.Services;

public interface IStructureClient
{
    Task<CategoryInvitationDto> ValidateInvitationTokenAsync(string token, CancellationToken cancellationToken = default);
    Task UpdateMyAthleteTelegramAsync(string? telegramUsername, string? bearerToken, CancellationToken cancellationToken = default);
}