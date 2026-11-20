using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.Athletes.UpdateMyTelegram;

public sealed record UpdateMyTelegramRequestDto(string? TelegramUsername = null);

public sealed record UpdateMyTelegramResponseDto(string? TelegramUsername = null);

public sealed record UpdateMyTelegramCommand(string? TelegramUsername);

/// <summary>
/// Permite al atleta autenticado actualizar su propio usuario de Telegram (KAN-503).
/// </summary>
public sealed class UpdateMyTelegramCommandHandler(
    ICurrentUser currentUser,
    IAthleteRepository athleteRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<UpdateMyTelegramResponseDto> HandleAsync(
        UpdateMyTelegramCommand command,
        CancellationToken cancellationToken = default)
    {
        var accountId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var athlete = await athleteRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        athlete.UpdateTelegramUsername(command.TelegramUsername);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateMyTelegramResponseDto(athlete.TelegramUsername);
    }
}
