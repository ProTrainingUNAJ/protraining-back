using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.Athletes.RegisterMyConsent;

public sealed record RegisterMyConsentCommand(
    bool AllowsHabitsRecommendations = false,
    bool AllowsPhysicalTraining = true);

public sealed class RegisterMyConsentCommandHandler(
    ICurrentUser currentUser,
    IAthleteRepository athleteRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ConsentStatusDto> HandleAsync(RegisterMyConsentCommand command, CancellationToken cancellationToken = default)
    {
        var accountId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var athlete = await athleteRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        // Athlete.RegisterAdultConsent se encarga de las reglas de dominio:
        // - Si IsMinor -> DomainException("Los atletas menores de edad deben ser autorizados por un tutor legal.")
        // - Si DigitalConsent != null && DigitalConsent.IsSigned -> DomainException("El consentimiento digital ya fue firmado y su registro es inmutable.")
        // - Si !allowsPhysicalTraining -> DomainException("El consentimiento debe autorizar la preparación física.")
        athlete.RegisterAdultConsent(
            command.AllowsPhysicalTraining,
            command.AllowsHabitsRecommendations);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var consent = athlete.DigitalConsent;

        return new ConsentStatusDto(
            HasSignedConsent: true,
            IsMinor: athlete.IsMinor,
            ConsentDate: consent?.ConsentDate,
            AllowsPhysicalTraining: consent?.AllowsPhysicalTraining ?? true,
            AllowsHabitsRecommendations: consent?.AllowsHabitsRecommendations ?? false,
            GuardianName: null);
    }
}
