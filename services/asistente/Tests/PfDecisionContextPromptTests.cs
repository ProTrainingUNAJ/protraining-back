namespace Asistente.Tests;

using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Ai.SuggestRoutine;
using Application.UseCases.PlanDrafts.CreatePlanDraft;
using Domain.Entities;
using Moq;
using Xunit;

public sealed class PfDecisionContextPromptTests
{
    private static readonly Guid AthleteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SuggestedExerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FinalExerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Given_PreviousDecision_When_BuildingRoutinePrompt_Then_IncludesDecisionWithoutFreeTextReason()
    {
        var request = new RoutineSuggestionRequestDto(
            AthleteId,
            "Fuerza",
            PreviousPfDecisions:
            [
                new PfDecisionContextDto(
                    PfDecisionSource.DayAdjustment,
                    new DateOnly(2026, 11, 15),
                    "rodilla",
                    SuggestedExerciseId,
                    PfDecisionType.Modified,
                    FinalExerciseId,
                    60m)
            ]);

        var prompt = RoutinePromptBuilder.BuildPrompt(request);

        Assert.Contains("DECISIONES PREVIAS DEL PREPARADOR FÍSICO:", prompt);
        Assert.Contains("decisión: Modified", prompt);
        Assert.Contains(SuggestedExerciseId.ToString(), prompt);
        Assert.Contains(FinalExerciseId.ToString(), prompt);
        Assert.Contains("60%", prompt);
    }

    [Fact]
    public void Given_PreviousDecision_When_BuildingPlanDraftPrompt_Then_IncludesDecisionContext()
    {
        var request = new CreatePlanDraftRequestDto(
            Goal: "Fuerza",
            AthleteId: AthleteId,
            Weeks: 1,
            DaysPerWeek: 3,
            PreviousPfDecisions:
            [
                new PfDecisionContextDto(
                    PfDecisionSource.PlanDraft,
                    new DateOnly(2026, 11, 14),
                    "espalda_baja",
                    SuggestedExerciseId,
                    PfDecisionType.Approved,
                    null,
                    null)
            ]);

        var prompt = RoutinePromptBuilder.BuildPlanDraftPrompt(
            request,
            [new CatalogExerciseDto(SuggestedExerciseId, "Peso muerto", "Espalda", "Barra")]);

        Assert.Contains("DECISIONES PREVIAS DEL PREPARADOR FÍSICO:", prompt);
        Assert.Contains("origen: PlanDraft", prompt);
        Assert.Contains("zona: espalda_baja", prompt);
    }

    [Fact]
    public async Task Given_AthleteGeneration_When_PreviousDecisionsExist_Then_PassesThemToAiService()
    {
        var decision = PfDecision.Create(
            AthleteId,
            PfDecisionSource.DayAdjustment,
            Guid.NewGuid(),
            SuggestedExerciseId,
            PfDecisionType.Approved);
        var decisionRepository = new Mock<IPfDecisionRepository>();
        decisionRepository
            .Setup(r => r.GetRecentAsync(AthleteId, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([decision]);

        var aiService = new Mock<IAiService>();
        aiService
            .Setup(s => s.SuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutineSuggestionResponseDto(
                AthleteId, "Rutina", "Resumen", [], "Rules"));

        var useCase = new SuggestRoutineUseCase(
            aiService.Object,
            Mock.Of<IAthleteAdaptationContextService>(),
            Mock.Of<IStructureAthleteClient>(),
            pfDecisionRepository: decisionRepository.Object);

        await useCase.ExecuteAsync(new RoutineSuggestionRequestDto(AthleteId, "Fuerza"));

        aiService.Verify(s => s.SuggestRoutineAsync(
            It.Is<RoutineSuggestionRequestDto>(r =>
                r.PreviousPfDecisions != null &&
                r.PreviousPfDecisions.Count == 1 &&
                r.PreviousPfDecisions[0].Decision == PfDecisionType.Approved),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
