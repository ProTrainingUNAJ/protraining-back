using Application.DTOs.AthleteRecords;

namespace Application.UseCases.AthleteRecord.CompleteOnboarding;

public record CompleteOnboardingCommand(List<OnboardingRecordRequest> Records);