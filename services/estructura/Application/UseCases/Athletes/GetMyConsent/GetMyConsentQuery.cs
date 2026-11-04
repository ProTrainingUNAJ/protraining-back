using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.Athletes.GetMyConsent;

public sealed record GetMyConsentQuery;

public sealed class GetMyConsentQueryHandler(
    ICurrentUser currentUser,
    IAthleteRepository athleteRepository)
{
    public async Task<ConsentStatusDto> HandleAsync(GetMyConsentQuery query, CancellationToken cancellationToken = default)
    {
        var accountId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var athlete = await athleteRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        var consent = athlete.DigitalConsent;
        var hasSigned = consent != null && consent.IsSigned;

        return new ConsentStatusDto(
            HasSignedConsent: hasSigned,
            IsMinor: athlete.IsMinor,
            ConsentDate: consent?.ConsentDate,
            AllowsPhysicalTraining: consent?.AllowsPhysicalTraining ?? false,
            AllowsHabitsRecommendations: consent?.AllowsHabitsRecommendations ?? false,
            GuardianName: consent?.GuardianName);
    }
}
